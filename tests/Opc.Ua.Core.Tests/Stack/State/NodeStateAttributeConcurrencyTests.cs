/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Characterization tests for concurrent attribute read/write on a single
    /// <see cref="NodeState"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests pin the observable behaviour of attribute access under contention.
    /// They were written before <see cref="NodeState"/> owned its synchronization and are
    /// retained unchanged in substance afterwards, which is what makes them a regression
    /// net for that change.
    /// </para>
    /// <para>
    /// Previously <see cref="NodeState.ReadAttributeAsync"/> and
    /// <see cref="NodeState.WriteAttributeAsync"/> took <c>lock(this)</c>, and the
    /// synchronous paths relied on external callers taking <c>lock(source)</c> on the node -
    /// a contract carried only in prose. The node now guards its own attribute access with a
    /// private lock, so these tests call it without any external locking; earlier revisions
    /// of this file had to take that lock to satisfy the old contract.
    /// </para>
    /// <para>
    /// The existing <c>NodeStateHandlerConcurrencyTests</c> covers a different concern -
    /// racing a handler assignment against an attribute write. It does not exercise mutual
    /// exclusion between readers and writers, which is what these tests add.
    /// </para>
    /// </remarks>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Category("NodeStateConcurrency")]
    [Parallelizable]
    public class NodeStateAttributeConcurrencyTests
    {
        private const int kIterations = 2000;
        private const int kReaderCount = 4;

        /// <summary>
        /// Concurrent asynchronous reads of the same attribute must all succeed and
        /// must never observe a value that was never written.
        /// </summary>
        [Test]
        public async Task ConcurrentAsyncReadsObserveOnlyWrittenValuesAsync()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            var observed = new ConcurrentBag<double>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Task writer = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        // The node is written through the documented contract, with no
                        // external locking: the node guards its own attributes.
                        ServiceResult result = node.WriteAttribute(
                            context,
                            Attributes.Value,
                            default,
                            new DataValue(new Variant((double)i)));

                        Assert.That(
                            ServiceResult.IsGood(result),
                            Is.True,
                            $"value write {i} failed: {result}");
                    }
                },
                cts.Token);

            Task[] readers = new Task[kReaderCount];
            for (int r = 0; r < kReaderCount; r++)
            {
                readers[r] = Task.Run(
                    async () =>
                    {
                        // do-while: guarantees at least one read even when the writer
                        // completes before this task is scheduled.
                        do
                        {
                            (ServiceResult result, DataValue value) = await node
                                .ReadAttributeAsync(
                                    context,
                                    Attributes.Value,
                                    default,
                                    default,
                                    new DataValue(),
                                    cts.Token)
                                .ConfigureAwait(false);

                            Assert.That(ServiceResult.IsGood(result), Is.True);

                            if (value.WrappedValue.TryGetValue(out double observedValue))
                            {
                                observed.Add(observedValue);
                            }
                        }
                        while (!writer.IsCompleted && !cts.IsCancellationRequested);
                    },
                    cts.Token);
            }

            await writer.ConfigureAwait(false);
            await Task.WhenAll(readers).ConfigureAwait(false);

            Assert.That(observed, Is.Not.Empty, "readers observed no values at all");

            foreach (double value in observed)
            {
                Assert.That(
                    value,
                    Is.InRange(0d, kIterations - 1),
                    "reader observed a value that was never written");
                Assert.That(
                    value % 1d,
                    Is.Zero,
                    "reader observed a torn or interpolated value");
            }
        }

        /// <summary>
        /// A value and a status code written as one attribute write must never be observed
        /// apart: the reader must not see the value from one write paired with the status
        /// code from another.
        /// </summary>
        /// <remarks>
        /// The atomic unit is a single attribute write carrying a whole
        /// <see cref="DataValue"/>. The individual <c>Value</c> / <c>StatusCode</c> setters
        /// each take the node's lock separately, so a pair of setter calls is deliberately
        /// <b>not</b> a transaction and a reader may legitimately observe the state between
        /// them. Callers needing an atomic multi-field update must use one attribute write,
        /// which is what the server does.
        /// </remarks>
        [Test]
        public async Task CorrelatedValueAndStatusAreNeverObservedTornAsync()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var failures = new ConcurrentBag<string>();

            Task writer = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        // Even iterations write Good, odd write Uncertain, so value
                        // parity and status are correlated and a torn read is visible.
                        StatusCode status = i % 2 == 0
                            ? StatusCodes.Good
                            : StatusCodes.UncertainLastUsableValue;

                        ServiceResult result = node.WriteAttribute(
                            context,
                            Attributes.Value,
                            default,
                            new DataValue(new Variant((double)i), status, DateTimeUtc.Now));

                        Assert.That(
                            ServiceResult.IsGood(result),
                            Is.True,
                            $"correlated write {i} failed: {result}");
                    }
                },
                cts.Token);

            Task[] readers = new Task[kReaderCount];
            for (int r = 0; r < kReaderCount; r++)
            {
                readers[r] = Task.Run(
                    async () =>
                    {
                        // do-while: guarantees at least one read even when the writer
                        // completes before this task is scheduled.
                        do
                        {
                            (ServiceResult result, DataValue value) = await node
                                .ReadAttributeAsync(
                                    context,
                                    Attributes.Value,
                                    default,
                                    default,
                                    new DataValue(),
                                    cts.Token)
                                .ConfigureAwait(false);

                            if (!ServiceResult.IsGood(result) ||
                                !value.WrappedValue.TryGetValue(out double observed))
                            {
                                continue;
                            }

                            bool expectedGood = (long)observed % 2 == 0;
                            bool actualGood = value.StatusCode == StatusCodes.Good;

                            if (expectedGood != actualGood)
                            {
                                failures.Add(
                                    $"value {observed} observed with status {value.StatusCode}");
                            }
                        }
                        while (!writer.IsCompleted && !cts.IsCancellationRequested);
                    },
                    cts.Token);
            }

            await writer.ConfigureAwait(false);
            await Task.WhenAll(readers).ConfigureAwait(false);

            Assert.That(
                failures,
                Is.Empty,
                $"observed {failures.Count} torn value/status pairs, e.g. {FirstOrNone(failures)}");
        }

        /// <summary>
        /// Interleaved synchronous and asynchronous reads must agree: the async path
        /// must not observe state the synchronous path cannot, and neither must fail.
        /// </summary>
        [Test]
        public async Task SyncAndAsyncReadPathsAgreeUnderContentionAsync()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            int lastWritten = -1;

            Task writer = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        node.Value = (double)i;
                        Volatile.Write(ref lastWritten, i);
                    }
                },
                cts.Token);

            Task syncReader = Task.Run(
                () =>
                {
                    // do-while: guarantees at least one read even when the writer
                    // completes before this task is scheduled.
                    do
                    {
                        var value = new DataValue();
                        ServiceResult result;

                        result = node.ReadAttribute(
                            context,
                            Attributes.Value,
                            default,
                            default,
                            ref value);

                        Assert.That(ServiceResult.IsGood(result), Is.True);
                        Assert.That(value.WrappedValue.TryGetValue(out double _), Is.True);
                    }
                    while (!writer.IsCompleted && !cts.IsCancellationRequested);
                },
                cts.Token);

            Task asyncReader = Task.Run(
                async () =>
                {
                    // do-while: guarantees at least one read even when the writer
                    // completes before this task is scheduled.
                    do
                    {
                        (ServiceResult result, DataValue value) = await node
                            .ReadAttributeAsync(
                                context,
                                Attributes.Value,
                                default,
                                default,
                                new DataValue(),
                                cts.Token)
                            .ConfigureAwait(false);

                        Assert.That(ServiceResult.IsGood(result), Is.True);
                        Assert.That(value.WrappedValue.TryGetValue(out double _), Is.True);
                    }
                    while (!writer.IsCompleted && !cts.IsCancellationRequested);
                },
                cts.Token);

            await writer.ConfigureAwait(false);
            await Task.WhenAll(syncReader, asyncReader).ConfigureAwait(false);

            var final = new DataValue();
            ServiceResult finalResult = node.ReadAttribute(
                context,
                Attributes.Value,
                default,
                default,
                ref final);

            Assert.That(ServiceResult.IsGood(finalResult), Is.True);
            Assert.That(final.WrappedValue.TryGetValue(out double finalValue), Is.True);

            // Asserted against what the writer actually wrote. These tests run in parallel
            // and the timeout may pre-empt the loop, so a fixed expectation would be flaky.
            Assert.That(Volatile.Read(ref lastWritten), Is.GreaterThanOrEqualTo(0));
            Assert.That(finalValue, Is.EqualTo((double)Volatile.Read(ref lastWritten)));
        }

        /// <summary>
        /// Concurrent writes to distinct attributes of the same node must all be
        /// applied - none may be lost to a race on the shared node state.
        /// </summary>
        [Test]
        public async Task ConcurrentWritesToDistinctAttributesAreAllAppliedAsync()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            int lastValueWritten = -1;
            int lastDescriptionWritten = -1;

            Task valueWriter = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        ServiceResult result = node.WriteAttribute(
                            context,
                            Attributes.Value,
                            default,
                            new DataValue(new Variant((double)i)));
                        Assert.That(ServiceResult.IsGood(result), Is.True);
                        Volatile.Write(ref lastValueWritten, i);
                    }
                },
                cts.Token);

            Task descriptionWriter = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        ServiceResult result = node.WriteAttribute(
                            context,
                            Attributes.Description,
                            default,
                            new DataValue(
                                new Variant(new LocalizedText($"description-{i}"))));
                        Assert.That(ServiceResult.IsGood(result), Is.True);
                        Volatile.Write(ref lastDescriptionWritten, i);
                    }
                },
                cts.Token);

            await Task.WhenAll(valueWriter, descriptionWriter).ConfigureAwait(false);

            var value = new DataValue();
            Assert.That(
                ServiceResult.IsGood(
                    node.ReadAttribute(context, Attributes.Value, default, default, ref value)),
                Is.True);
            Assert.That(value.WrappedValue.TryGetValue(out double lastValue), Is.True);

            // Asserted against what each writer actually wrote. These tests run in parallel
            // and the timeout may pre-empt a loop, so fixed expectations would be flaky.
            Assert.That(Volatile.Read(ref lastValueWritten), Is.GreaterThanOrEqualTo(0));
            Assert.That(Volatile.Read(ref lastDescriptionWritten), Is.GreaterThanOrEqualTo(0));
            Assert.That(lastValue, Is.EqualTo((double)Volatile.Read(ref lastValueWritten)));

            Assert.That(
                node.Description.Text,
                Is.EqualTo($"description-{Volatile.Read(ref lastDescriptionWritten)}"),
                "a write to Description was lost");
        }

        /// <summary>
        /// Creating a browser while another thread mutates the node's references must never
        /// fault: the node guards the browser build itself, so no caller needs to lock it.
        /// </summary>
        /// <remarks>
        /// This test could not be written before the browse path owned its synchronization.
        /// The lock lived in the node manager, outside the node, so an unrelated caller of
        /// <see cref="NodeState.CreateBrowser"/> raced the reference collection with nothing
        /// excluding it.
        /// </remarks>
        [Test]
        public async Task ConcurrentBrowseAndReferenceWritesNeverFaultAsync()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            int browsesCompleted = 0;

            Task writer = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        var targetId = new ExpandedNodeId($"target-{i}", 7);

                        node.AddReferenceIfMissing(
                            ReferenceTypeIds.HasCause,
                            false,
                            targetId);
                        node.RemoveReference(ReferenceTypeIds.HasCause, false, targetId);
                    }
                },
                cts.Token);

            Task[] browsers = new Task[kReaderCount];
            for (int r = 0; r < kReaderCount; r++)
            {
                browsers[r] = Task.Run(
                    () =>
                    {
                        // do-while: guarantees at least one browse even when the writer
                        // completes before this task is scheduled.
                        do
                        {
                            using INodeBrowser browser = node.CreateBrowser(
                                context,
                                null,
                                default,
                                false,
                                BrowseDirection.Both,
                                default,
                                null,
                                true);

                            while (browser.Next() != null)
                            {
                                // drain the snapshot.
                            }

                            Interlocked.Increment(ref browsesCompleted);
                        }
                        while (!writer.IsCompleted && !cts.IsCancellationRequested);
                    },
                    cts.Token);
            }

            await writer.ConfigureAwait(false);
            await Task.WhenAll(browsers).ConfigureAwait(false);

            Assert.That(
                Volatile.Read(ref browsesCompleted),
                Is.GreaterThan(0),
                "no browse completed at all");
        }

        /// <summary>
        /// A browser is a snapshot: references added after it was created must not appear in
        /// it, and every reference present when it was created must.
        /// </summary>
        [Test]
        public void BrowserIsASnapshotTakenAtCreation()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            for (int i = 0; i < 4; i++)
            {
                node.AddReference(
                    ReferenceTypeIds.HasCause,
                    false,
                    new ExpandedNodeId($"before-{i}", 7));
            }

            using INodeBrowser snapshot = node.CreateBrowser(
                context,
                null,
                ReferenceTypeIds.HasCause,
                false,
                BrowseDirection.Forward,
                default,
                null,
                true);

            for (int i = 0; i < 3; i++)
            {
                node.AddReference(
                    ReferenceTypeIds.HasCause,
                    false,
                    new ExpandedNodeId($"after-{i}", 7));
            }

            Assert.That(
                Drain(snapshot),
                Is.EqualTo(4),
                "references added after the browser was created must not appear in it");

            using INodeBrowser later = node.CreateBrowser(
                context,
                null,
                ReferenceTypeIds.HasCause,
                false,
                BrowseDirection.Forward,
                default,
                null,
                true);

            Assert.That(
                Drain(later),
                Is.EqualTo(7),
                "a browser created afterwards must see every reference");
        }

        /// <summary>
        /// Browsing while another thread adds and removes children must never fault and must
        /// never yield a partially built snapshot.
        /// </summary>
        [Test]
        public async Task ConcurrentBrowseAndChildWritesNeverFaultAsync()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var faults = new ConcurrentBag<string>();
            int browsesCompleted = 0;

            Task writer = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        var child = new PropertyState(node)
                        {
                            NodeId = new NodeId($"child-{i}", 7),
                            BrowseName = new QualifiedName($"child-{i}", 7),
                            DisplayName = new LocalizedText($"child-{i}"),
                            SymbolicName = $"child-{i}",
                            ReferenceTypeId = ReferenceTypeIds.HasProperty
                        };

                        node.AddChild(child);
                        node.RemoveChild(child);
                    }
                },
                cts.Token);

            Task[] browsers = new Task[kReaderCount];
            for (int r = 0; r < kReaderCount; r++)
            {
                browsers[r] = Task.Run(
                    () =>
                    {
                        do
                        {
                            using INodeBrowser browser = node.CreateBrowser(
                                context,
                                null,
                                ReferenceTypeIds.HasProperty,
                                false,
                                BrowseDirection.Forward,
                                default,
                                null,
                                true);

                            foreach (IReference reference in Enumerate(browser))
                            {
                                if (reference.ReferenceTypeId.IsNull)
                                {
                                    faults.Add("browser produced a reference with no type");
                                }
                            }

                            Interlocked.Increment(ref browsesCompleted);
                        }
                        while (!writer.IsCompleted && !cts.IsCancellationRequested);
                    },
                    cts.Token);
            }

            await writer.ConfigureAwait(false);
            await Task.WhenAll(browsers).ConfigureAwait(false);

            Assert.That(faults, Is.Empty, FirstOrNone(faults));
            Assert.That(
                Volatile.Read(ref browsesCompleted),
                Is.GreaterThan(0),
                "no browse completed at all");
        }

        /// <summary>
        /// A browser hands back every reference exactly once, and a pushed-back reference is
        /// returned again before the rest. This is the single-consumer contract that replaced
        /// the browser's former internal lock.
        /// </summary>
        [Test]
        public void BrowserReturnsEachReferenceOnceAndHonoursPushBack()
        {
            (ISystemContext context, BaseVariableState node) = CreateWritableVariable();

            for (int i = 0; i < 8; i++)
            {
                node.AddReference(
                    ReferenceTypeIds.HasCause,
                    false,
                    new ExpandedNodeId($"target-{i}", 7));
            }

            using INodeBrowser browser = node.CreateBrowser(
                context,
                null,
                ReferenceTypeIds.HasCause,
                false,
                BrowseDirection.Forward,
                default,
                null,
                true);

            IReference first = browser.Next();
            Assert.That(first, Is.Not.Null);

            browser.Push(first);
            Assert.That(
                browser.Next(),
                Is.SameAs(first),
                "a pushed-back reference must be returned before any other");

            int remaining = Drain(browser);
            Assert.That(remaining, Is.EqualTo(7), "every reference must be returned exactly once");
        }

        private static int Drain(INodeBrowser browser)
        {
            int count = 0;

            while (browser.Next() != null)
            {
                count++;
            }

            return count;
        }

        private static IEnumerable<IReference> Enumerate(INodeBrowser browser)
        {
            for (IReference reference = browser.Next();
                reference != null;
                reference = browser.Next())
            {
                yield return reference;
            }
        }

        private static (ISystemContext Context, BaseVariableState Node) CreateWritableVariable()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serviceMessageContext = ServiceMessageContext.Create(telemetry);

            var context = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            // Mirrors the proven setup in NodeStateHandlerConcurrencyTests: a fully
            // created node whose access levels are applied through WriteAttribute.
            var node = new AnalogUnitRangeState(null);

            node.Create(
                context,
                new NodeId("ConcurrencyTestNode", 7),
                new QualifiedName("ConcurrencyTestNode", 7),
                new LocalizedText("ConcurrencyTestNode"),
                true);

            node.WriteMask =
                AttributeWriteMask.AccessLevel |
                AttributeWriteMask.Description |
                AttributeWriteMask.DisplayName |
                AttributeWriteMask.UserAccessLevel |
                AttributeWriteMask.UserWriteMask |
                AttributeWriteMask.WriteMask;
            node.UserWriteMask = node.WriteMask;

            ServiceResult result = node.WriteAttribute(
                context,
                Attributes.AccessLevel,
                default,
                new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
            Assert.That(ServiceResult.IsGood(result), Is.True, $"AccessLevel setup: {result}");

            result = node.WriteAttribute(
                context,
                Attributes.UserAccessLevel,
                default,
                new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
            Assert.That(ServiceResult.IsGood(result), Is.True, $"UserAccessLevel setup: {result}");

            // Seed a value so readers starting before the first write still observe a
            // well-typed double rather than an unset variant.
            result = node.WriteAttribute(
                context,
                Attributes.Value,
                default,
                new DataValue(new Variant(0d)));
            Assert.That(ServiceResult.IsGood(result), Is.True, $"Value seed: {result}");

            return (context, node);
        }

        private static string FirstOrNone(ConcurrentBag<string> bag)
        {
            return bag.TryPeek(out string first) ? first : "<none>";
        }
    }
}
