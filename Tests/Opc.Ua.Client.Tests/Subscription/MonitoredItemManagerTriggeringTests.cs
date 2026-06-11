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
    /// pipeline: operations queue, batched apply, conflict
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
            await m_sut.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyTriggeringOperationsNoQueueReturnsFalseAsync()
        {
            // Act
            bool any = await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

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
            bool any = await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

            // Assert
            Assert.That(any, Is.True);
            m_monitoredItemServices.VerifyAll();
            SetTriggeringResult res = await tcs.Task.ConfigureAwait(false);
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
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

            // Assert: exactly one RPC, with merged add list.
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(capturedTrig, Is.EqualTo(trig.ServerId));
            Assert.That(capturedAdd, Is.EquivalentTo(new[] { a.ServerId, b.ServerId }));
            Assert.That(capturedRemove, Is.Empty);

            // Each TCS gets per-link results scoped to its own inputs.
            SetTriggeringResult r1 = await tcs1.Task.ConfigureAwait(false);
            Assert.That(r1.AddResults, Has.Count.EqualTo(1));
            Assert.That(r1.AddResults[0].Item, Is.SameAs(a));
            SetTriggeringResult r2 = await tcs2.Task.ConfigureAwait(false);
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
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

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
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

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

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            Assert.That(tcs.Task.IsCompleted, Is.True);
            SetTriggeringResult res = await tcs.Task.ConfigureAwait(false);
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
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

            // Assert: per-link Bad status surfaced and desired state
            // rolled back so it matches reality.
            SetTriggeringResult res = await tcs.Task.ConfigureAwait(false);
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
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            Assert.That(tcs.Task.IsCompleted, Is.False);

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            Assert.That(tcs.Task.IsCompleted, Is.True);
        }

        [Test]
        public async Task RemoveOfNonCreatedTriggeredItemReturnsGoodAsync()
        {
            // Arrange: trig + one triggered item that's not Created
            // (server side already cleaned up per §5.13.1.6 — remove
            // succeeds as a no-op).
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem deletedTgt = AddItemNoServerId("dead"); // not Created

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    Array.Empty<IMonitoredItem>(),
                    new IMonitoredItem[] { deletedTgt }, tcs));

            // Mock returns success on the assumption that addList +
            // removeList is empty (both pre-resolve client-side).
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
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

            // Assert: remove → Good (auto-cleanup); no RPC issued for
            // a single-edge no-op group.
            SetTriggeringResult res = await tcs.Task.ConfigureAwait(false);
            Assert.That(res.AddResults, Is.Empty);
            Assert.That(res.RemoveResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(res.RemoveResults[0].Status), Is.True);
            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task AddOfNonCreatedTriggeredItemDefersUntilCreatedAsync()
        {
            // Arrange: trig (Created) + tgt (not yet Created).
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem tgt = AddItemNoServerId("tgt");

            ExpectSetTriggering(100,
                new uint[] { 101 },
                Array.Empty<uint>(),
                addResults: new[] { (StatusCode)StatusCodes.Good },
                removeResults: Array.Empty<StatusCode>());

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { tgt },
                    Array.Empty<IMonitoredItem>(), tcs));

            // First pass: tgt not Created → entire op deferred,
            // TCS not yet completed.
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            Assert.That(tcs.Task.IsCompleted, Is.False);
            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);

            // Simulate the create completing on the server side.
            tgt.SetServerIdForTest(101);

            // Second pass: tgt Created → RPC issued, TCS completes.
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            SetTriggeringResult res = await tcs.Task.ConfigureAwait(false);
            Assert.That(res.AddResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(res.AddResults[0].Status), Is.True);
        }

        [Test]
        public async Task AddOfNonCreatedTriggeredItemTerminatesAfterRetryBudgetExhaustionAsync()
        {
            // Arrange: trig + tgt that will never get Created.
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem tgt = AddItemNoServerId("tgt");
            // Seed the desired state to verify rollback on terminal
            // failure (mimics what ValidateBelongsAndUpdateDesired does
            // for the imperative API path).
            tgt.AddDesiredTriggeredByForTest("trig");
            Assert.That(tgt.DesiredTriggeredByNames, Has.Member("trig"));

            var tcs = new TaskCompletionSource<SetTriggeringResult>();
            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { tgt },
                    Array.Empty<IMonitoredItem>(), tcs));

            // Apply 11 times (MaxTriggeringRetryCount=10, so the 11th
            // pass terminates the op).
            for (int i = 0; i < 11; i++)
            {
                await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            }

            SetTriggeringResult res = await tcs.Task.ConfigureAwait(false);
            Assert.That(res.AddResults, Has.Count.EqualTo(1));
            Assert.That(res.AddResults[0].Status.Code,
                Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            // Desired state was rolled back for the non-Created edge.
            Assert.That(tgt.DesiredTriggeredByNames, Does.Not.Contain("trig"));
            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task BadSubscriptionIdInvalidPreservesDesiredStateAsync()
        {
            // Imperative SetTriggering path: when the service returns
            // BadSubscriptionIdInvalid, the op is re-queued for retry
            // after subscription recreate — desired state MUST NOT be
            // rolled back (otherwise snapshot/navigation observes a
            // missing link during recovery and the retry would fail
            // to re-establish it because the desired set was wiped).
            TestMonitoredItem trig = AddCreatedItem("trig", serverId: 100);
            TestMonitoredItem tgt = AddCreatedItem("tgt", serverId: 101);
            // Seed desired state to mirror the imperative path's
            // synchronous mutation.
            tgt.AddDesiredTriggeredByForTest("trig");

            m_monitoredItemServices.Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid));

            m_sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trig,
                    new IMonitoredItem[] { tgt },
                    Array.Empty<IMonitoredItem>(), null));

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

            // Desired-state preserved despite the failure.
            Assert.That(tgt.DesiredTriggeredByNames, Has.Member("trig"));
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
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

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

        private static TestMonitoredItem AddCreatedItemOn(
            MonitoredItemManager sut, string name, uint serverId)
        {
            sut.TryAdd(name, OptionsFactory.Create(new MonitoredItemOptions
            {
                StartNodeId = new NodeId(name, 0)
            }), out IMonitoredItem? item);
            var concrete = (TestMonitoredItem)item!;
            concrete.SetServerIdForTest(serverId);
            return concrete;
        }

        /// <summary>
        /// Build an isolated MonitoredItemManager whose logger calls
        /// are recorded into a returned <see cref="CapturingLoggerProvider"/>.
        /// Used by the cap-bug regression tests so they can both
        /// assert on the warning that fires when
        /// <see cref="MonitoredItemManager.MaxPendingTriggeringEntries"/>
        /// is reached, and avoid polluting the shared SetUp-built SUT.
        /// </summary>
        private (MonitoredItemManager Sut, FakeMonitoredItemManagerContext Context,
            CapturingLoggerProvider Logs)
            CreateSutWithCapturingLogger()
        {
            var capture = new CapturingLoggerProvider();
            ITelemetryContext telemetry = new CapturingTelemetryContext(capture);
            var monitoredItemServices =
                new Mock<IMonitoredItemServiceSetClientMethods>();
            var methodServices = new Mock<IMethodServiceSetClientMethods>();
            var context = new FakeMonitoredItemManagerContext
            {
                Id = 9,
                MonitoredItemServiceSet = monitoredItemServices.Object,
                MethodServiceSet = methodServices.Object,
                CreateMonitoredItemFactory = (n, opts, ctx) =>
                    new TestMonitoredItem(ctx, n,
                        (OptionsMonitor<MonitoredItemOptions>)opts,
                        telemetry.CreateLogger("TestMonitoredItem"))
            };
            var sut = new MonitoredItemManager(context, telemetry);
            return (sut, context, capture);
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
        public async Task EnqueueTriggeringDeltaPendingMaterializedOnLaterAddAsync()
        {
            // Declarative add-order independence: triggered item
            // declares TriggeredByNames=["trig"] BEFORE the triggering
            // item exists in the subscription. The pending entry is
            // persisted by name and materialized into a real
            // SetTriggering op when the triggering item is added later.
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);

            m_sut.EnqueueTriggeringDelta(tgt,
                addedTriggeringNames: new[] { "trig" },
                removedTriggeringNames: Array.Empty<string>());

            // First apply pass — no triggering item yet, no RPC.
            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);

            // For materialization to enqueue the op, the triggered
            // item's DesiredTriggeredByNames must contain "trig" — the
            // declarative path normally seeds this via Options at
            // TryAdd time. Mimic that here.
            tgt.AddDesiredTriggeredByForTest("trig");

            ExpectSetTriggering(100,
                new uint[] { 101 },
                Array.Empty<uint>(),
                addResults: new[] { (StatusCode)StatusCodes.Good },
                removeResults: Array.Empty<StatusCode>());

            // Now add the triggering item — pending materializes.
            AddCreatedItem("trig", 100);

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            m_monitoredItemServices.VerifyAll();
        }

        [Test]
        public async Task EnqueueTriggeringDeltaPendingDroppedOnTriggeredItemRemovalAsync()
        {
            // If the triggered item is removed before the triggering
            // item appears, the pending entry must be purged so a
            // later add of the triggering item does not materialize a
            // stale op against a dead reference.
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);

            m_sut.EnqueueTriggeringDelta(tgt,
                addedTriggeringNames: new[] { "trig" },
                removedTriggeringNames: Array.Empty<string>());

            // Remove the triggered item (purges pending entries).
            m_sut.TryRemove(tgt.ClientHandle);

            // Add the triggering item — must NOT enqueue any op.
            AddCreatedItem("trig", 100);

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            m_monitoredItemServices.Verify(s => s.SetTriggeringAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<uint>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<uint>>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task EnqueueTriggeringDeltaPendingFoldedAddThenRemoveAsync()
        {
            // Add-then-remove for the same (triggeringName, triggered)
            // pair before resolution folds to a single remove entry.
            // Since the link was never on the server, materializing
            // the remove is a no-op (handled as Good).
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);

            // Two declarative deltas in opposite directions for the
            // same edge — pending folds last-intent (remove) wins.
            m_sut.EnqueueTriggeringDelta(tgt,
                addedTriggeringNames: new[] { "trig" },
                removedTriggeringNames: Array.Empty<string>());
            m_sut.EnqueueTriggeringDelta(tgt,
                addedTriggeringNames: Array.Empty<string>(),
                removedTriggeringNames: new[] { "trig" });

            // Add triggering item — pending materializes a single
            // remove edge. Since tgt is Created the remove resolves
            // server-side as a no-op (item was never linked) but the
            // current implementation still issues the call for the
            // remove. Set up the mock to accept it.
            AddCreatedItem("trig", 100);

            ExpectSetTriggering(100,
                Array.Empty<uint>(),
                new uint[] { 101 },
                addResults: Array.Empty<StatusCode>(),
                removeResults: new[] { (StatusCode)StatusCodes.Good });

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            m_monitoredItemServices.VerifyAll();
        }

        [Test]
        public async Task EnqueueTriggeringDeltaPendingSkippedWhenDesireRevokedAsync()
        {
            // If the triggered item's desired set no longer contains
            // the triggering name by the time the triggering item
            // appears, the pending add is dropped (a follow-up options
            // change can have revoked the intent).
            TestMonitoredItem tgt = AddCreatedItem("tgt", 101);
            tgt.AddDesiredTriggeredByForTest("trig");

            m_sut.EnqueueTriggeringDelta(tgt,
                addedTriggeringNames: new[] { "trig" },
                removedTriggeringNames: Array.Empty<string>());

            // Revoke before the trigger appears.
            tgt.RemoveDesiredTriggeredByForTest("trig");

            AddCreatedItem("trig", 100);

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
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
        public async Task EnqueueTriggeringDeltaPendingCapEnforcedAcrossOneTriggerNameAndFoldPreservesCountAsync()
        {
            // Regression for the cap-bug where the
            // m_pendingTriggeringCount check inside
            // AddPendingTriggeringEntry only fired on the new-outer-key
            // path, letting subsequent inserts under an EXISTING outer
            // key grow the per-triggering-name inner dict unbounded
            // past MaxPendingTriggeringEntries. This test piles
            // (cap + 5) distinct triggered items under ONE unresolved
            // triggering name and asserts the manager refuses entries
            // beyond the cap, emits the warning, and continues to
            // fold (last-intent-wins) overwrites of already-tracked
            // (triggering, triggered) pairs without bumping the count.
            (MonitoredItemManager sut, _, CapturingLoggerProvider logs)
                = CreateSutWithCapturingLogger();
            await using (sut.ConfigureAwait(false))
            {
                const int Cap = MonitoredItemManager.MaxPendingTriggeringEntries;
                const int Overflow = 5;
                var triggered = new TestMonitoredItem[Cap + Overflow];
                for (int i = 0; i < triggered.Length; i++)
                {
                    string name = "tgt" + i;
                    sut.TryAdd(name,
                        OptionsFactory.Create(new MonitoredItemOptions
                        {
                            StartNodeId = new NodeId(name, 0)
                        }), out IMonitoredItem? item);
                    triggered[i] = (TestMonitoredItem)item!;
                    triggered[i].SetServerIdForTest((uint)(200 + i));
                    // All distinct pairs share the same unresolved
                    // triggering name "ghostTrigger" — exercises the
                    // existing-outer-key insertion path that the buggy
                    // code skipped past the cap check.
                    sut.EnqueueTriggeringDelta(triggered[i],
                        addedTriggeringNames: new[] { "ghostTrigger" },
                        removedTriggeringNames: Array.Empty<string>());
                    Assert.That(sut.PendingTriggeringEntryCount,
                        Is.LessThanOrEqualTo(Cap),
                        $"after enqueue #{i + 1} the count must never exceed cap");
                }

                Assert.That(sut.PendingTriggeringEntryCount, Is.EqualTo(Cap),
                    "exactly cap entries should be retained");
                Assert.That(
                    logs.Entries.Count(e =>
                        e.Level == LogLevel.Warning &&
                        e.Message.Contains(
                            "Pending triggering-name dictionary is full",
                            StringComparison.Ordinal)),
                    Is.GreaterThanOrEqualTo(1),
                    "cap-hit warning must have been emitted at least once");

                // Fold semantic: re-enqueueing an ALREADY-pending
                // (ghostTrigger, triggered[0]) pair with the opposite
                // isAdd value must succeed and must NOT bump the count
                // (the pair is overwritten, not re-counted).
                int beforeFold = sut.PendingTriggeringEntryCount;
                sut.EnqueueTriggeringDelta(triggered[0],
                    addedTriggeringNames: Array.Empty<string>(),
                    removedTriggeringNames: new[] { "ghostTrigger" });
                Assert.That(sut.PendingTriggeringEntryCount, Is.EqualTo(beforeFold),
                    "folding an existing pair must not bump the count");
            }
        }

        [Test]
        public async Task EnqueueTriggeringDeltaPendingCapEnforcedAcrossManyTriggerNamesAsync()
        {
            // Regression coverage for the path the original
            // implementation DID exercise: cap + 1 distinct outer
            // (triggering name) keys each carrying one triggered item.
            // Locks down the "new outer key" branch so a future
            // refactor cannot drop the cap check from the path the
            // original code actually covered.
            (MonitoredItemManager sut, _, CapturingLoggerProvider logs)
                = CreateSutWithCapturingLogger();
            await using (sut.ConfigureAwait(false))
            {
                const int Cap = MonitoredItemManager.MaxPendingTriggeringEntries;
                TestMonitoredItem tgt = AddCreatedItemOn(sut, "tgt", 101);
                for (int i = 0; i < Cap + 1; i++)
                {
                    sut.EnqueueTriggeringDelta(tgt,
                        addedTriggeringNames: new[] { "ghostTrig" + i },
                        removedTriggeringNames: Array.Empty<string>());
                }
                Assert.That(sut.PendingTriggeringEntryCount, Is.EqualTo(Cap),
                    "exactly cap entries should be retained across distinct trigger names");
                Assert.That(
                    logs.Entries.Count(e =>
                        e.Level == LogLevel.Warning &&
                        e.Message.Contains(
                            "Pending triggering-name dictionary is full",
                            StringComparison.Ordinal)),
                    Is.GreaterThanOrEqualTo(1),
                    "cap-hit warning must have been emitted at least once");
            }
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

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

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
        [Repeat(10)]
        public void AddRemoveDesiredTriggeredByAreThreadSafe()
        {
            // Stress test for the CAS-loop self-synchronization in
            // AddDesiredTriggeredBy / RemoveDesiredTriggeredBy. Many
            // tasks contend on the same per-item field; no lock is
            // held by the callers (mirroring the QueuePendingChanges /
            // OnOptionsChanged / Reset paths that mutate this field
            // without holding any manager lock). Without the CAS loop
            // (e.g. previous Volatile.Read + Volatile.Write code), the
            // last-writer-wins clobber would drop add intents and the
            // final set would have fewer than 100 names.
            TestMonitoredItem item = AddCreatedItem("item", 100);
            const int N = 100;
            var addTasks = new Task[N];
            for (int i = 0; i < N; i++)
            {
                int idx = i;
                addTasks[i] = Task.Run(() => item.AddDesiredTriggeredByForTest("n" + idx));
            }
            Task.WaitAll(addTasks);
            // Every distinct name must be present.
            Assert.That(item.DesiredTriggeredByNames, Has.Count.EqualTo(N));

            // Now interleave concurrent removes and adds.
            var mixTasks = new Task[2 * N];
            for (int i = 0; i < N; i++)
            {
                int idx = i;
                mixTasks[i] = Task.Run(() => item.RemoveDesiredTriggeredByForTest("n" + idx));
                mixTasks[N + i] = Task.Run(() => item.AddDesiredTriggeredByForTest("m" + idx));
            }
            Task.WaitAll(mixTasks);
            // All "n*" gone; all "m*" present.
            for (int i = 0; i < N; i++)
            {
                Assert.That(item.DesiredTriggeredByNames,
                    Does.Not.Contain("n" + i),
                    $"n{i} should have been removed");
                Assert.That(item.DesiredTriggeredByNames,
                    Contains.Item("m" + i),
                    $"m{i} should have been added");
            }
            Assert.That(item.DesiredTriggeredByNames, Has.Count.EqualTo(N));
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

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);

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

            await m_sut.ApplyTriggeringOperationsAsync(default).ConfigureAwait(false);
            m_monitoredItemServices.VerifyAll();
        }

        /// <summary>
        /// Minimal <see cref="ILoggerProvider"/> + <see cref="ILogger"/>
        /// implementation that records every <see cref="Log"/> call so
        /// the cap-bug regression tests can verify a
        /// <see cref="LogLevel.Warning"/> was emitted with the expected
        /// substring. Single instance is returned for every category
        /// to avoid per-category bookkeeping.
        /// </summary>
        private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
        {
            public IReadOnlyList<(LogLevel Level, string Message)> Entries
            {
                get
                {
                    lock (m_entries)
                    {
                        return [.. m_entries];
                    }
                }
            }

            public ILogger CreateLogger(string categoryName) => this;

            public void Dispose()
            {
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return s_nullScope;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (m_entries)
                {
                    m_entries.Add((logLevel, formatter(state, exception)));
                }
            }

            private readonly List<(LogLevel, string)> m_entries = [];

            private sealed class NullScope : IDisposable
            {
                public void Dispose()
                {
                }
            }

            private static readonly IDisposable s_nullScope = new NullScope();
        }

        /// <summary>
        /// <see cref="ITelemetryContext"/> wrapper that funnels every
        /// <c>CreateLogger</c> call to the supplied
        /// <see cref="CapturingLoggerProvider"/>. Used only by the
        /// cap-bug regression tests.
        /// </summary>
        private sealed class CapturingTelemetryContext : TelemetryContextBase
        {
#pragma warning disable CA2000 // ownership transfers to base via the ILoggerFactory it holds for the lifetime of the test fixture
            public CapturingTelemetryContext(CapturingLoggerProvider provider)
                : base(Microsoft.Extensions.Logging.LoggerFactory.Create(
                    builder => builder.AddProvider(provider)))
            {
            }
#pragma warning restore CA2000
        }

        private ITelemetryContext m_telemetry = null!;
        private Mock<IMonitoredItemServiceSetClientMethods> m_monitoredItemServices = null!;
        private Mock<IMethodServiceSetClientMethods> m_methodServices = null!;
        private FakeMonitoredItemManagerContext m_context = null!;
        private MonitoredItemManager m_sut = null!;
    }
}
