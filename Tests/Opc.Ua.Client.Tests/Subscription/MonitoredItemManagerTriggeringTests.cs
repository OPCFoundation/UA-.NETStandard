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

            internal void AddDesiredTriggeredByForTest(string name)
            {
                AddDesiredTriggeredBy(name);
            }
        }

        private ITelemetryContext m_telemetry = null!;
        private Mock<IMonitoredItemServiceSetClientMethods> m_monitoredItemServices = null!;
        private Mock<IMethodServiceSetClientMethods> m_methodServices = null!;
        private FakeMonitoredItemManagerContext m_context = null!;
        private MonitoredItemManager m_sut = null!;
    }
}
