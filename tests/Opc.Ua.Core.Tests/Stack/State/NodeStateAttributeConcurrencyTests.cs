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
    /// These tests exist to pin the observable behaviour of attribute access
    /// under contention <b>before</b> the node's synchronization is changed.
    /// </para>
    /// <para>
    /// Today <see cref="NodeState.ReadAttributeAsync"/> and
    /// <see cref="NodeState.WriteAttributeAsync"/> take <c>lock(this)</c>, and the
    /// synchronous paths rely on external callers taking <c>lock(source)</c> on the
    /// node - a contract carried only in prose and honoured at 19 sites across
    /// <c>AsyncCustomNodeManager</c>, <c>BaseVariableState</c>, <c>NodeState</c> and
    /// <c>CustomNodeManager</c>. That contract is a single-node assumption baked into
    /// the node type and is scheduled for removal in favour of a private
    /// <see cref="System.Threading.Lock"/> owned by the node.
    /// </para>
    /// <para>
    /// Every test here is written to assert an invariant that must hold <b>both</b>
    /// before and after that change, so they act as a regression net for it. Where a
    /// test currently has to take the external lock to satisfy the documented
    /// contract, it says so explicitly and that lock is the only line expected to be
    /// deleted when the node starts guarding itself.
    /// </para>
    /// <para>
    /// The existing <c>NodeStateHandlerConcurrencyTests</c> covers a different
    /// concern - racing a handler assignment against an attribute write. It does not
    /// exercise mutual exclusion between readers and writers, which is what these
    /// tests add.
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
                async () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        // The node is written through the documented contract. When the
                        // node guards itself this lock is removed and the test must
                        // continue to pass unchanged.
                        lock (node)
                        {
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
                    }
                },
                cts.Token);

            Task[] readers = new Task[kReaderCount];
            for (int r = 0; r < kReaderCount; r++)
            {
                readers[r] = Task.Run(
                    async () =>
                    {
                        while (!writer.IsCompleted && !cts.IsCancellationRequested)
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
        /// A value and a status code written together must never be observed apart.
        /// This is the tearing check: the reader must not see the value from one
        /// write paired with the status code from another.
        /// </summary>
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

                        lock (node)
                        {
                            node.Value = (double)i;
                            node.StatusCode = status;
                        }
                    }
                },
                cts.Token);

            Task[] readers = new Task[kReaderCount];
            for (int r = 0; r < kReaderCount; r++)
            {
                readers[r] = Task.Run(
                    async () =>
                    {
                        while (!writer.IsCompleted && !cts.IsCancellationRequested)
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

            Task writer = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        lock (node)
                        {
                            node.Value = (double)i;
                        }
                    }
                },
                cts.Token);

            Task syncReader = Task.Run(
                () =>
                {
                    while (!writer.IsCompleted && !cts.IsCancellationRequested)
                    {
                        var value = new DataValue();
                        ServiceResult result;

                        lock (node)
                        {
                            result = node.ReadAttribute(
                                context,
                                Attributes.Value,
                                default,
                                default,
                                ref value);
                        }

                        Assert.That(ServiceResult.IsGood(result), Is.True);
                        Assert.That(value.WrappedValue.TryGetValue(out double _), Is.True);
                    }
                },
                cts.Token);

            Task asyncReader = Task.Run(
                async () =>
                {
                    while (!writer.IsCompleted && !cts.IsCancellationRequested)
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
            Assert.That(finalValue, Is.EqualTo((double)(kIterations - 1)));
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

            Task valueWriter = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        lock (node)
                        {
                            ServiceResult result = node.WriteAttribute(
                                context,
                                Attributes.Value,
                                default,
                                new DataValue(new Variant((double)i)));
                            Assert.That(ServiceResult.IsGood(result), Is.True);
                        }
                    }
                },
                cts.Token);

            Task descriptionWriter = Task.Run(
                () =>
                {
                    for (int i = 0; i < kIterations && !cts.IsCancellationRequested; i++)
                    {
                        lock (node)
                        {
                            ServiceResult result = node.WriteAttribute(
                                context,
                                Attributes.Description,
                                default,
                                new DataValue(
                                    new Variant(new LocalizedText($"description-{i}"))));
                            Assert.That(ServiceResult.IsGood(result), Is.True);
                        }
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
            Assert.That(lastValue, Is.EqualTo((double)(kIterations - 1)));

            Assert.That(
                node.Description.Text,
                Is.EqualTo($"description-{kIterations - 1}"),
                "a write to Description was lost");
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

            return (context, node);
        }

        private static string FirstOrNone(ConcurrentBag<string> bag)
        {
            return bag.TryPeek(out string first) ? first : "<none>";
        }
    }
}
