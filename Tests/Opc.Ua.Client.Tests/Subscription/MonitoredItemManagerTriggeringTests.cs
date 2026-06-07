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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Engine-level unit tests for the V2 triggering (SetTriggering)
    /// pipeline: operations queue, Phase 4 batched apply, conflict
    /// resolution, error handling, snapshot round-trip, and restore
    /// behaviour. Tests against fakes / mocks — no live server.
    /// </summary>
    [TestFixture]
    public sealed class MonitoredItemManagerTriggeringTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_monitoredItemServices = new Mock<IMonitoredItemServiceSetClientMethods>();
            m_methodServices = new Mock<IMethodServiceSetClientMethods>();
            m_context = new FakeMonitoredItemManagerContext
            {
                Id = 7,
                MonitoredItemServiceSet = m_monitoredItemServices.Object,
                MethodServiceSet = m_methodServices.Object,
                CreateMonitoredItemFactory = (name, options, context) =>
                    new TestMonitoredItem(context, name,
                        (OptionsMonitor<MonitoredItemOptions>)options,
                        m_telemetry.CreateLogger("TestMonitoredItem"))
            };
            m_sut = new MonitoredItemManager(m_context, m_telemetry);
        }

        [TearDown]
        public async Task TearDown()
        {
            await m_sut.DisposeAsync();
        }

        [Test]
        public async Task ApplyTriggeringOperationsNoQueueReturnsFalseAsync()
        {
            // Act
            bool any = await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert
            Assert.That(any, Is.False);
            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task SingleOperationProducesOneRpcAsync()
        {
            // Arrange: two created items.
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", serverId: 101);
            ExpectSetTriggering(trig.ServerId, new uint[] { tgt.ServerId },
                Array.Empty<uint>(),
                addResults: new[] { (StatusCode)StatusCodes.Good },
                removeResults: Array.Empty<StatusCode>());

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(
                    trig, new IMonitoredItem[] { tgt },
                    Array.Empty<IMonitoredItem>(), tcs));

            // Act
            bool any = await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert
            Assert.That(any, Is.True);
            m_monitoredItemServices.VerifyAll();
            SetTriggeringResult res = await tcs.Task;
            Assert.That(StatusCode.IsGood(res.ServiceResult), Is.True);
            Assert.That(res.AddResults, Has.Count.EqualTo(1));
            Assert.That(res.AddResults[0].Item, Is.SameAs(tgt));
            Assert.That(StatusCode.IsGood(res.AddResults[0].Status), Is.True);
        }

        [Test]
        public async Task MultipleOpsSameTriggerCollapseToOneRpcAsync()
        {
            // Arrange: trig + two triggered items. Two separate ops
            // adding each triggered should collapse into ONE
            // SetTriggering RPC.
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem a = AddCreatedItem("a", serverId: 101);
            TestMonitoredItem b = AddCreatedItem("b", serverId: 102);

            uint capturedTrig = 0;
            HashSet<uint>? capturedAdd = null;
            HashSet<uint>? capturedRemove = null;
            int callCount = 0;
            m_monitoredItemServices.Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, uint, uint, ArrayOf<uint>, ArrayOf<uint>,
                    CancellationToken>(
                    (h, subId, trigId, adds, rems, ct) =>
                    {
                        callCount++;
                        capturedTrig = trigId;
                        capturedAdd = [..(adds.ToArray() ?? [])];
                        capturedRemove = [..(rems.ToArray() ?? [])];
                    })
                .ReturnsAsync((RequestHeader? h, uint subId, uint trigId,
                    ArrayOf<uint> adds, ArrayOf<uint> rems, CancellationToken ct) =>
                    new SetTriggeringResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        AddResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, adds.Count).ToArrayOf(),
                        RemoveResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, rems.Count).ToArrayOf()
                    });

            var tcs1 = new TaskCompletionSource<SetTriggeringResult>();
            var tcs2 = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { a },
                    Array.Empty<IMonitoredItem>(), tcs1));
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { b },
                    Array.Empty<IMonitoredItem>(), tcs2));

            // Act
            await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert: exactly one RPC, with merged add list.
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(capturedTrig, Is.EqualTo(trig.ServerId));
            Assert.That(capturedAdd, Is.EquivalentTo(new[] { a.ServerId, b.ServerId }));
            Assert.That(capturedRemove, Is.Empty);

            // Each TCS gets per-link results scoped to its own inputs.
            SetTriggeringResult r1 = await tcs1.Task;
            Assert.That(r1.AddResults, Has.Count.EqualTo(1));
            Assert.That(r1.AddResults[0].Item, Is.SameAs(a));
            SetTriggeringResult r2 = await tcs2.Task;
            Assert.That(r2.AddResults, Has.Count.EqualTo(1));
            Assert.That(r2.AddResults[0].Item, Is.SameAs(b));
        }

        [Test]
        public async Task MultipleOpsDifferentTriggersProduceOneRpcEachAsync()
        {
            // Arrange: two trig items, each with one triggered item.
            TestMonitoredItem t1 = AddCreatedItem("t1", serverId: 100);
            TestMonitoredItem t2 = AddCreatedItem("t2", serverId: 101);
            TestMonitoredItem x = AddCreatedItem("x", serverId: 200);
            TestMonitoredItem y = AddCreatedItem("y", serverId: 201);

            int callCount = 0;
            m_monitoredItemServices.Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => callCount++)
                .ReturnsAsync((RequestHeader? h, uint subId, uint trigId,
                    ArrayOf<uint> adds, ArrayOf<uint> rems, CancellationToken ct) =>
                    new SetTriggeringResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        AddResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, adds.Count).ToArrayOf(),
                        RemoveResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, rems.Count).ToArrayOf()
                    });

            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(t1,
                    new IMonitoredItem[] { x }, Array.Empty<IMonitoredItem>(),
                    null));
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(t2,
                    new IMonitoredItem[] { y }, Array.Empty<IMonitoredItem>(),
                    null));

            // Act
            await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert: 2 separate RPCs (one per triggering item).
            Assert.That(callCount, Is.EqualTo(2));
        }

        [Test]
        public async Task NotYetCreatedItemRequeuedAsync()
        {
            // Arrange: triggering item not yet on server.
            TestMonitoredItem trig = AddItemNoServerId("trig");
            TestMonitoredItem tgt = AddCreatedItem("tgt", serverId: 101);

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { tgt }, Array.Empty<IMonitoredItem>(),
                    tcs));

            // Act: first pass — trig not Created, op re-queued.
            await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert: no RPC, TCS not yet completed.
            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(tcs.Task.IsCompleted, Is.False);

            // Simulate the trig getting Created on the server (test
            // hook), then run another pass.
            trig.SetServerIdForTest(100);
            ExpectSetTriggering(trig.ServerId, new uint[] { tgt.ServerId },
                Array.Empty<uint>(),
                addResults: new[] { (StatusCode)StatusCodes.Good },
                removeResults: Array.Empty<StatusCode>());

            await m_sut.ApplyTriggeringOperationsAsync(default);
            Assert.That(tcs.Task.IsCompleted, Is.True);
            SetTriggeringResult res = await tcs.Task;
            Assert.That(StatusCode.IsGood(res.AddResults[0].Status), Is.True);
        }

        [Test]
        public async Task PerLinkFailedAddRollsBackDesiredStateAsync()
        {
            // Arrange: imperative path style — pre-set the desired
            // state, then queue the op. The fake server fails the add.
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", serverId: 101);
            // Simulate the ImperativeAPI mutation step (which Subscription's
            // SetTriggeringAsync does under the manager lock).
            tgt.AddDesiredTriggeredByForTest(trig.Name);
            Assert.That(tgt.DesiredTriggeredByNames, Is.EqualTo(new[] { trig.Name }));

            ExpectSetTriggering(trig.ServerId, new uint[] { tgt.ServerId },
                Array.Empty<uint>(),
                addResults: new[] { (StatusCode)StatusCodes.BadMonitoredItemIdInvalid },
                removeResults: Array.Empty<StatusCode>());

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { tgt },
                    Array.Empty<IMonitoredItem>(), tcs));

            // Act
            await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert: per-link Bad status surfaced and desired state
            // rolled back so it matches reality.
            SetTriggeringResult res = await tcs.Task;
            Assert.That(res.AddResults[0].Status.Code,
                Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(tgt.DesiredTriggeredByNames, Is.Empty);
        }

        [Test]
        public async Task BadSubscriptionIdInvalidRequeuesAsync()
        {
            // Arrange
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", serverId: 101);

            m_monitoredItemServices.SetupSequence(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    AddResults = new[] { (StatusCode)StatusCodes.Good }.ToArrayOf(),
                    RemoveResults = Array.Empty<StatusCode>().ToArrayOf()
                });

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { tgt },
                    Array.Empty<IMonitoredItem>(), tcs));

            // Act: first pass — BadSubscriptionIdInvalid → re-queue,
            // TCS NOT completed. Second pass — succeeds.
            await m_sut.ApplyTriggeringOperationsAsync(default);
            Assert.That(tcs.Task.IsCompleted, Is.False);

            await m_sut.ApplyTriggeringOperationsAsync(default);
            Assert.That(tcs.Task.IsCompleted, Is.True);
        }

        [Test]
        public async Task DeletedTriggeredItemAddFailsRemoveGoodAsync()
        {
            // Arrange: trig + two triggered items, the "remove" target
            // already deleted (server side already cleaned up per
            // §5.13.1.6).
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem deletedTgt = AddItemNoServerId("dead"); // not Created
            TestMonitoredItem addTgt = AddItemNoServerId("alsoDead"); // not Created

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { addTgt },
                    new IMonitoredItem[] { deletedTgt }, tcs));

            // Apply many passes to exhaust the retry budget; we don't
            // hit the server because both triggered items resolve to
            // pre-failed/pre-success without involving the server in
            // the test scenario. Note: the current implementation
            // re-queues if the *triggering* item is not Created, not
            // if a triggered item is not Created. Since trig IS
            // Created, the op runs immediately.

            m_monitoredItemServices.Setup(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    AddResults = Array.Empty<StatusCode>().ToArrayOf(),
                    RemoveResults = Array.Empty<StatusCode>().ToArrayOf()
                });

            // Act
            await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert: add → BadMonitoredItemIdInvalid; remove → Good
            // (auto-cleanup).
            SetTriggeringResult res = await tcs.Task;
            Assert.That(res.AddResults, Has.Count.EqualTo(1));
            Assert.That(res.AddResults[0].Status.Code,
                Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(res.RemoveResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(res.RemoveResults[0].Status), Is.True);
        }

        [Test]
        public async Task SameEdgeConflictResolutionLastWinsAsync()
        {
            // Arrange: queue add(t→x) then remove(t→x) in the same
            // batch. Last intent wins → the net result is "remove" but
            // since x was never added, the per-edge state resolves to
            // a single remove in the RPC.
            TestMonitoredItem t = AddCreatedItem("t", serverId: 100);
            TestMonitoredItem x = AddCreatedItem("x", serverId: 101);

            HashSet<uint>? capturedAdd = null;
            HashSet<uint>? capturedRemove = null;
            m_monitoredItemServices.Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, uint, uint, ArrayOf<uint>, ArrayOf<uint>,
                    CancellationToken>(
                    (h, subId, trigId, adds, rems, ct) =>
                    {
                        capturedAdd = [..(adds.ToArray() ?? [])];
                        capturedRemove = [..(rems.ToArray() ?? [])];
                    })
                .ReturnsAsync((RequestHeader? h, uint subId, uint trigId,
                    ArrayOf<uint> adds, ArrayOf<uint> rems, CancellationToken ct) =>
                    new SetTriggeringResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        AddResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, adds.Count).ToArrayOf(),
                        RemoveResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, rems.Count).ToArrayOf()
                    });

            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(t,
                    new IMonitoredItem[] { x },
                    Array.Empty<IMonitoredItem>(), null));
            // Second op REMOVES x — last intent wins, so per-edge
            // state is "remove".
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(t,
                    Array.Empty<IMonitoredItem>(),
                    new IMonitoredItem[] { x }, null));

            // Act
            await m_sut.ApplyTriggeringOperationsAsync(default);

            // Assert: only "remove" sent to the server (last wins).
            Assert.That(capturedAdd, Is.Empty);
            Assert.That(capturedRemove, Is.EquivalentTo(new[] { x.ServerId }));
        }

        // Helpers -------------------------------------------------------

        private TestMonitoredItem AddCreatedItem(string name, uint serverId)
        {
            m_sut.TryAdd(name, OptionsFactory.Create(new MonitoredItemOptions
            {
                StartNodeId = new NodeId(name, 0)
            }), out IMonitoredItem? item);
            var concrete = (TestMonitoredItem)item!;
            concrete.SetServerIdForTest(serverId);
            return concrete;
        }

        private TestMonitoredItem AddItemNoServerId(string name)
        {
            m_sut.TryAdd(name, OptionsFactory.Create(new MonitoredItemOptions
            {
                StartNodeId = new NodeId(name, 0)
            }), out IMonitoredItem? item);
            return (TestMonitoredItem)item!;
        }

        private void ExpectSetTriggering(uint expectedTrigId,
            uint[] expectedAdd, uint[] expectedRemove,
            StatusCode[] addResults, StatusCode[] removeResults)
        {
            m_monitoredItemServices.Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(),
                    m_context.Id,
                    expectedTrigId,
                    It.Is<ArrayOf<uint>>(a =>
                        ArrayOfEqualsUnordered(a, expectedAdd)),
                    It.Is<ArrayOf<uint>>(r =>
                        ArrayOfEqualsUnordered(r, expectedRemove)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    AddResults = addResults.ToArrayOf(),
                    RemoveResults = removeResults.ToArrayOf()
                })
                .Verifiable();
        }

        private static bool ArrayOfEqualsUnordered(ArrayOf<uint> actual,
            uint[] expected)
        {
            uint[]? raw = actual.ToArray();
            if (raw == null)
            {
                return expected.Length == 0;
            }
            if (raw.Length != expected.Length)
            {
                return false;
            }
            uint[] sortedRaw = (uint[])raw.Clone();
            uint[] sortedExpected = (uint[])expected.Clone();
            Array.Sort(sortedRaw);
            Array.Sort(sortedExpected);
            for (int i = 0; i < sortedRaw.Length; i++)
            {
                if (sortedRaw[i] != sortedExpected[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// MonitoredItem subclass that exposes test-only mutation of
        /// private state (server id, desired triggers).
        /// </summary>
        internal sealed class TestMonitoredItem : MonitoredItem
        {
            public TestMonitoredItem(IMonitoredItemContext context, string name,
                IOptionsMonitor<MonitoredItemOptions> options, ILogger logger)
                : base(context, name, options, logger)
            {
            }

            internal void SetServerIdForTest(uint serverId)
            {
                ApplyTransferState(ClientHandle, serverId);
            }

            internal bool AddDesiredTriggeredByForTest(string name)
            {
                return AddDesiredTriggeredBy(name);
            }

            internal bool RemoveDesiredTriggeredByForTest(string name)
            {
                return RemoveDesiredTriggeredBy(name);
            }

            internal void SetDesiredTriggeredByNamesForTest(IEnumerable<string>? names)
            {
                SetDesiredTriggeredByNames(names);
            }

            internal void ResetForTest() => Reset();
        }

        // ----- ValidateBelongsAndUpdateDesired (imperative-API entry validation) -----

        [Test]
        public void ValidateBelongsThrowsOnNullTriggeringItem()
        {
            Assert.That(() => m_sut.ValidateBelongsAndUpdateDesired(
                null!,
                Array.Empty<IMonitoredItem>(),
                Array.Empty<IMonitoredItem>()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ValidateBelongsThrowsWhenTriggeringItemNotInSubscription()
        {
            TestMonitoredItem real = AddCreatedItem("here", 100);
            // Construct a stray item with the same name but a
            // different reference and a different context.
            var strayContext = new FakeMonitoredItemContext();
            var stray = new TestMonitoredItem(strayContext, "here",
                OptionsFactory.Create(new MonitoredItemOptions
                {
                    StartNodeId = new NodeId("here", 0)
                }),
                m_telemetry.CreateLogger("stray"));

            Assert.That(() => m_sut.ValidateBelongsAndUpdateDesired(
                stray, Array.Empty<IMonitoredItem>(),
                Array.Empty<IMonitoredItem>()),
                Throws.ArgumentException);
            // Sanity: the real item is unaffected.
            Assert.That(real.DesiredTriggeredByNames, Is.Empty);
        }

        [Test]
        public void ValidateBelongsThrowsOnNullAddEntry()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            Assert.That(() => m_sut.ValidateBelongsAndUpdateDesired(
                trig,
                new IMonitoredItem[] { null! },
                Array.Empty<IMonitoredItem>()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ValidateBelongsThrowsOnNullRemoveEntry()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            Assert.That(() => m_sut.ValidateBelongsAndUpdateDesired(
                trig,
                Array.Empty<IMonitoredItem>(),
                new IMonitoredItem[] { null! }),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ValidateBelongsThrowsWhenAddEntryNotInSubscription()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            var strayContext = new FakeMonitoredItemContext();
            var stray = new TestMonitoredItem(strayContext, "stray",
                OptionsFactory.Create(new MonitoredItemOptions
                {
                    StartNodeId = new NodeId("stray", 0)
                }),
                m_telemetry.CreateLogger("stray"));

            Assert.That(() => m_sut.ValidateBelongsAndUpdateDesired(
                trig,
                new IMonitoredItem[] { stray },
                Array.Empty<IMonitoredItem>()),
                Throws.ArgumentException);
        }

        [Test]
        public void ValidateBelongsMutatesDesiredImmediately()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);

            m_sut.ValidateBelongsAndUpdateDesired(
                trig,
                new IMonitoredItem[] { tgt },
                Array.Empty<IMonitoredItem>());

            // Imperative-write semantics: the desired state mutates
            // synchronously under the manager lock, before the
            // SetTriggering RPC fires.
            Assert.That(tgt.DesiredTriggeredByNames, Is.EqualTo(new[] { "trig" }));
        }

        [Test]
        public void ValidateBelongsRemoveMutatesDesiredImmediately()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            tgt.AddDesiredTriggeredByForTest("trig");

            m_sut.ValidateBelongsAndUpdateDesired(
                trig,
                Array.Empty<IMonitoredItem>(),
                new IMonitoredItem[] { tgt });

            Assert.That(tgt.DesiredTriggeredByNames, Is.Empty);
        }

        // ----- EnqueueTriggeringDelta (declarative-path entry) -----

        [Test]
        public async Task EnqueueTriggeringDeltaWithUnresolvedNameIsSkippedAsync()
        {
            // Only the triggered item exists in this subscription;
            // the triggering name "ghost" does not resolve. The
            // engine logs and silently drops it.
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);

            m_sut.EnqueueTriggeringDelta(tgt,
                addedTriggeringNames: new[] { "ghost" },
                removedTriggeringNames: Array.Empty<string>());

            await m_sut.ApplyTriggeringOperationsAsync(default);

            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void EnqueueTriggeringDeltaEmptyListsIsNoOp()
        {
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            m_sut.EnqueueTriggeringDelta(tgt, Array.Empty<string>(),
                Array.Empty<string>());
            // No assertion needed — call must not throw.
        }

        [Test]
        public void EnqueueTriggeringDeltaThrowsOnNullTriggeredItem()
        {
            Assert.That(() => m_sut.EnqueueTriggeringDelta(
                null!, Array.Empty<string>(), Array.Empty<string>()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EnqueueTriggeringDeltaThrowsOnNullLists()
        {
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            Assert.That(() => m_sut.EnqueueTriggeringDelta(tgt, null!,
                Array.Empty<string>()), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => m_sut.EnqueueTriggeringDelta(tgt,
                Array.Empty<string>(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task EnqueueTriggeringDeltaResolvesAddAndRemoveAsync()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            TestMonitoredItem tgt1 = AddCreatedItem("tgt1", 101);
            TestMonitoredItem tgt2 = AddCreatedItem("tgt2", 102);
            // Pre-existing link to remove
            tgt2.AddDesiredTriggeredByForTest("trig");

            m_sut.EnqueueTriggeringDelta(tgt1,
                addedTriggeringNames: new[] { "trig" },
                removedTriggeringNames: Array.Empty<string>());
            m_sut.EnqueueTriggeringDelta(tgt2,
                addedTriggeringNames: Array.Empty<string>(),
                removedTriggeringNames: new[] { "trig" });

            HashSet<uint>? capturedAdd = null;
            HashSet<uint>? capturedRemove = null;
            m_monitoredItemServices.Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, uint, uint, ArrayOf<uint>, ArrayOf<uint>,
                    CancellationToken>(
                    (h, subId, trigId, adds, rems, ct) =>
                    {
                        capturedAdd = [.. adds.ToArray() ?? []];
                        capturedRemove = [.. rems.ToArray() ?? []];
                    })
                .ReturnsAsync((RequestHeader? h, uint subId, uint trigId,
                    ArrayOf<uint> adds, ArrayOf<uint> rems, CancellationToken ct) =>
                    new SetTriggeringResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        AddResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, adds.Count).ToArrayOf(),
                        RemoveResults = Enumerable.Repeat(
                            (StatusCode)StatusCodes.Good, rems.Count).ToArrayOf()
                    });

            await m_sut.ApplyTriggeringOperationsAsync(default);

            Assert.That(capturedAdd, Is.EquivalentTo(new[] { tgt1.ServerId }));
            Assert.That(capturedRemove, Is.EquivalentTo(new[] { tgt2.ServerId }));
        }

        // ----- DesiredTriggeredByNames runtime mutations -----

        [Test]
        public void AddDesiredTriggeredByIdempotent()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            Assert.That(item.AddDesiredTriggeredByForTest("a"), Is.True);
            Assert.That(item.AddDesiredTriggeredByForTest("a"), Is.False);
            Assert.That(item.DesiredTriggeredByNames, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void AddDesiredTriggeredByThrowsOnNullOrWhitespace()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            Assert.That(() => item.AddDesiredTriggeredByForTest(null!),
                Throws.ArgumentException);
            Assert.That(() => item.AddDesiredTriggeredByForTest(""),
                Throws.ArgumentException);
            Assert.That(() => item.AddDesiredTriggeredByForTest("   "),
                Throws.ArgumentException);
        }

        [Test]
        public void AddDesiredTriggeredByPreservesInsertionOrder()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            item.AddDesiredTriggeredByForTest("z");
            item.AddDesiredTriggeredByForTest("a");
            item.AddDesiredTriggeredByForTest("m");
            Assert.That(item.DesiredTriggeredByNames,
                Is.EqualTo(new[] { "z", "a", "m" }));
        }

        [Test]
        public void RemoveDesiredTriggeredByReturnsBoolean()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            item.AddDesiredTriggeredByForTest("a");
            item.AddDesiredTriggeredByForTest("b");

            Assert.That(item.RemoveDesiredTriggeredByForTest("a"), Is.True);
            Assert.That(item.RemoveDesiredTriggeredByForTest("a"), Is.False);
            Assert.That(item.RemoveDesiredTriggeredByForTest("missing"), Is.False);
            Assert.That(item.DesiredTriggeredByNames, Is.EqualTo(new[] { "b" }));

            // Removing the last element drops to empty.
            Assert.That(item.RemoveDesiredTriggeredByForTest("b"), Is.True);
            Assert.That(item.DesiredTriggeredByNames, Is.Empty);
        }

        [Test]
        public void RemoveDesiredTriggeredByNullOrEmptyReturnsFalse()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            Assert.That(item.RemoveDesiredTriggeredByForTest(null!), Is.False);
            Assert.That(item.RemoveDesiredTriggeredByForTest(""), Is.False);
        }

        [Test]
        public void SetDesiredTriggeredByNamesDeduplicatesPreservesOrder()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            item.SetDesiredTriggeredByNamesForTest(new[] { "x", "y", "x", "z", "y" });
            Assert.That(item.DesiredTriggeredByNames,
                Is.EqualTo(new[] { "x", "y", "z" }));
        }

        [Test]
        public void SetDesiredTriggeredByNamesNullClearsTheList()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            item.AddDesiredTriggeredByForTest("a");
            item.SetDesiredTriggeredByNamesForTest(null);
            Assert.That(item.DesiredTriggeredByNames, Is.Empty);
        }

        [Test]
        public void SetDesiredTriggeredByNamesThrowsOnWhitespace()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            Assert.That(() =>
                item.SetDesiredTriggeredByNamesForTest(new[] { "valid", "  " }),
                Throws.ArgumentException);
        }

        // ----- Navigation: TriggeringItems / TriggeredItems -----

        [Test]
        public void TriggeringItemsResolvesFromDesiredState()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            tgt.AddDesiredTriggeredByForTest("trig");

            List<IMonitoredItem> resolved = [.. tgt.TriggeringItems];
            Assert.That(resolved, Has.Count.EqualTo(1));
            Assert.That(resolved[0], Is.SameAs(trig));
        }

        [Test]
        public void TriggeringItemsSkipsUnresolvedNames()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            tgt.AddDesiredTriggeredByForTest("trig");
            tgt.AddDesiredTriggeredByForTest("ghost"); // not in subscription

            List<IMonitoredItem> resolved = [.. tgt.TriggeringItems];
            Assert.That(resolved, Has.Count.EqualTo(1));
            Assert.That(resolved[0], Is.SameAs(trig));
        }

        [Test]
        public void TriggeredItemsEnumeratesSiblings()
        {
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            TestMonitoredItem tgt1 = AddCreatedItem("tgt1", 101);
            TestMonitoredItem tgt2 = AddCreatedItem("tgt2", 102);
            TestMonitoredItem unrelated = AddCreatedItem("none", 103);
            tgt1.AddDesiredTriggeredByForTest("trig");
            tgt2.AddDesiredTriggeredByForTest("trig");

            HashSet<IMonitoredItem> downstream = [.. trig.TriggeredItems];
            Assert.That(downstream, Is.EquivalentTo(new[] { tgt1, tgt2 }));
            Assert.That(downstream, Does.Not.Contain(unrelated));
            Assert.That(downstream, Does.Not.Contain(trig));
        }

        [Test]
        public void TriggeringItemsEmptyWhenNoDesiredState()
        {
            TestMonitoredItem item = AddCreatedItem("item", 100);
            Assert.That(item.TriggeringItems, Is.Empty);
            Assert.That(item.TriggeredItems, Is.Empty);
        }

        // ----- Reset replay -----

        [Test]
        public async Task ResetEnqueuesReplayForDesiredTriggersAsync()
        {
            // Arrange: a triggered item with a desired triggering link
            // already established; classes simulate recreate by
            // resetting the item.
            TestMonitoredItem trig = AddCreatedItem("trig", 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            tgt.AddDesiredTriggeredByForTest("trig");

            // Act
            tgt.ResetForTest();

            // The reset clears the server id on the item; restore it
            // before setting the mock expectation so the captured
            // server id matches the actual RPC parameters.
            tgt.SetServerIdForTest(101);
            ExpectSetTriggering(trig.ServerId,
                new uint[] { tgt.ServerId },
                Array.Empty<uint>(),
                addResults: new[] { (StatusCode)StatusCodes.Good },
                removeResults: Array.Empty<StatusCode>());

            await m_sut.ApplyTriggeringOperationsAsync(default);

            m_monitoredItemServices.VerifyAll();
        }

        // ----- Snapshot round-trip with TriggeredByNames -----

        [Test]
        public void SnapshotCapturesDesiredTriggeredByNames()
        {
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            tgt.AddDesiredTriggeredByForTest("trigA");
            tgt.AddDesiredTriggeredByForTest("trigB");

            MonitoredItemStateSnapshot snap = tgt.Snapshot();
            Assert.That(snap.TriggeredByNames.IsNull, Is.False);
            Assert.That(snap.TriggeredByNames.Count, Is.EqualTo(2));
            Assert.That(snap.TriggeredByNames.ToArray(),
                Is.EqualTo(new[] { "trigA", "trigB" }));
        }

        [Test]
        public void SnapshotAsOptionsNullTriggeredByNamesProducesEmptyArray()
        {
            MonitoredItemStateSnapshot snap = MonitoredItemStateSnapshot.AsOptions(
                "name",
                new MonitoredItemOptions { StartNodeId = new NodeId("x", 0) },
                clientHandle: 1,
                serverId: 2,
                triggeredByNames: null);
            Assert.That(snap.TriggeredByNames.IsNull, Is.False);
            Assert.That(snap.TriggeredByNames.Count, Is.Zero);
        }

        [Test]
        public void SnapshotToOptionsRoundTripsTriggeredByNames()
        {
            MonitoredItemStateSnapshot snap = MonitoredItemStateSnapshot.AsOptions(
                "name",
                new MonitoredItemOptions
                {
                    StartNodeId = new NodeId("x", 0),
                    TriggeredByNames = ["a", "b"]
                },
                clientHandle: 1,
                serverId: 2,
                triggeredByNames: ["a", "b"]);

            MonitoredItemOptions options = snap.ToOptions();
            Assert.That(options.TriggeredByNames,
                Is.EqualTo(new[] { "a", "b" }));
        }

        // ----- TryAdd / Update initial-triggering enqueue -----

        [Test]
        public async Task TryAddInitialTriggeringIsEnqueuedAsync()
        {
            // Triggering item: add first.
            m_sut.TryAdd("trig", OptionsFactory.Create(
                new MonitoredItemOptions { StartNodeId = new NodeId("trig", 0) }),
                out IMonitoredItem? trig);
            ((TestMonitoredItem)trig!).SetServerIdForTest(100);

            // Triggered item: declare TriggeredByNames in initial
            // options — TryAdd should enqueue an add-delta.
            m_sut.TryAdd("tgt", OptionsFactory.Create(
                new MonitoredItemOptions
                {
                    StartNodeId = new NodeId("tgt", 0),
                    TriggeredByNames = ["trig"]
                }),
                out IMonitoredItem? tgt);
            ((TestMonitoredItem)tgt!).SetServerIdForTest(101);

            ExpectSetTriggering(100,
                new uint[] { 101 },
                Array.Empty<uint>(),
                addResults: new[] { (StatusCode)StatusCodes.Good },
                removeResults: Array.Empty<StatusCode>());

            await m_sut.ApplyTriggeringOperationsAsync(default);
            m_monitoredItemServices.VerifyAll();
        }

        private ITelemetryContext m_telemetry = null!;
        private Mock<IMonitoredItemServiceSetClientMethods> m_monitoredItemServices = null!;
        private Mock<IMethodServiceSetClientMethods> m_methodServices = null!;
        private FakeMonitoredItemManagerContext m_context = null!;
        private MonitoredItemManager m_sut = null!;
    }
}
