/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the server subscription and monitored-item locking findings of
    /// the High severity audit (work area 3): lock order of unresolved items (#12), bounded
    /// triggering links (#36), candidates of deleted items (#40) and audit events reported
    /// under the classic node-manager lock (#10).
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public sealed class SubscriptionLockingRegressionTests
    {
        /// <summary>
        /// ModifyUnresolvedAttributes held the item lock while it read the subscription's
        /// publishing interval, which takes the subscription lock. Every other path takes
        /// the subscription lock first, so the two orders deadlocked (#12). A fake
        /// subscription stands for the subscription lock; the second thread holds it and
        /// then takes the item lock, as ModifyMonitoredItems and Publish do.
        /// </summary>
        [Test]
        public void ModifyUnresolvedAttributesDoesNotTakeTheSubscriptionLockUnderTheItemLock()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            object subscriptionLock = new();
            using var publishingIntervalRead = new ManualResetEventSlim(false);

            // not disposed on failure: with the defect a deadlocked thread holds the item lock.
#pragma warning disable CA2000 // Dispose objects before losing scope
            MonitoredItem item = CreateUnresolvedItem(telemetry, subscriptionLock, publishingIntervalRead);
#pragma warning restore CA2000

            var request = new MonitoredItemModifyRequest
            {
                MonitoredItemId = item.Id,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 500,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            };
            var configuration = new ServerConfiguration
            {
                MinSupportedSamplingInterval = 0,
                MaxNotificationQueueSize = 1000,
                MaxDurableNotificationQueueSize = 1000
            };
            using var context = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Unknown,
                RequestLifetime.None);

            using var subscriptionLockHeld = new ManualResetEventSlim(false);
            using var modifyDone = new ManualResetEventSlim(false);
            using var subscriptionPathDone = new ManualResetEventSlim(false);
            ServiceResult? modifyError = null;

            // subscription lock first, then the item lock.
            var subscriptionPath = new Thread(() =>
            {
                lock (subscriptionLock)
                {
                    subscriptionLockHeld.Set();
                    publishingIntervalRead.Wait(TimeSpan.FromSeconds(10));
                    // let the modify thread block on the subscription lock.
                    Thread.Sleep(200);
                    _ = item.SamplingInterval;
                }
                subscriptionPathDone.Set();
            })
            { IsBackground = true, Name = "F12-subscription-then-item" };

            var modifyPath = new Thread(() =>
            {
                subscriptionLockHeld.Wait(TimeSpan.FromSeconds(10));
                (modifyError, _) = item.ModifyUnresolvedAttributes(
                    context, TimestampsToReturn.Both, request, configuration);
                modifyDone.Set();
            })
            { IsBackground = true, Name = "F12-modify-unresolved" };

            subscriptionPath.Start();
            modifyPath.Start();

            Assert.That(subscriptionPathDone.Wait(TimeSpan.FromSeconds(10)), Is.True,
                "the subscription-lock holder deadlocked on the item lock");
            Assert.That(modifyDone.Wait(TimeSpan.FromSeconds(10)), Is.True,
                "ModifyUnresolvedAttributes deadlocked against the subscription lock");
            Assert.That(ServiceResult.IsGood(modifyError), Is.True, modifyError?.ToString());
            Assert.That(item.SamplingInterval, Is.EqualTo(500));
            item.Dispose();
        }

        /// <summary>
        /// DeleteMonitoredItems scanned every triggering link list once per deleted id
        /// under the subscription lock (#36): 4000 deleted ids against 300 x 300 links took
        /// about 10 s on the audit machine. The links are now removed in one pass and an id
        /// that is no link target costs a hash probe.
        /// </summary>
        [Test]
        public async Task DeletingItemsDoesNotScanEveryTriggeringLinkPerItemAsync()
        {
            using var env = new SubscriptionEnvironment(publishingEnabled: true);
            const int triggers = 300;
            const int targets = 300;
            const int unlinked = 4000;

            uint[] targetIds = env.CreateIds(targets);
            uint[] triggerIds = env.CreateIds(triggers);
            uint[] unlinkedIds = env.CreateIds(unlinked);
            await env.RegisterAllAsync().ConfigureAwait(false);

            foreach (uint triggerId in triggerIds)
            {
                env.Subscription.SetTriggering(
                    env.Context, triggerId, [.. targetIds], [],
                    out ArrayOf<StatusCode> addResults, out _, out _, out _);
                Assert.That(addResults[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            }
            Assert.That(env.Subscription.TriggeringLinkCount, Is.EqualTo(triggers * targets));

            var stopwatch = Stopwatch.StartNew();
            await env.Subscription.DeleteMonitoredItemsAsync(env.Context, [.. unlinkedIds]).ConfigureAwait(false);
            stopwatch.Stop();
            await TestContext.Out.WriteLineAsync(
                $"delete of {unlinked} unlinked items: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms")
                .ConfigureAwait(false);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
                "deleting items that are no link target must not scan the links per item");
            Assert.That(env.Subscription.TriggeringLinkCount, Is.EqualTo(triggers * targets));

            // deleting half of the targets removes their links from every triggering item.
            await env.Subscription.DeleteMonitoredItemsAsync(env.Context, [.. targetIds.Take(targets / 2)])
                .ConfigureAwait(false);
            Assert.That(env.Subscription.TriggeringLinkCount, Is.EqualTo(triggers * (targets / 2)));
            env.Subscription.SetTriggering(
                env.Context, triggerIds[0], [], [targetIds[0], targetIds[targets - 1]],
                out _, out _, out ArrayOf<StatusCode> removeResults, out _);
            Assert.That(removeResults[0], Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(removeResults[1], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(env.Subscription.TriggeringLinkCount, Is.EqualTo((triggers * (targets / 2)) - 1));

            // deleting triggering items removes their own links.
            await env.Subscription.DeleteMonitoredItemsAsync(env.Context, [.. triggerIds.Skip(1)]).ConfigureAwait(false);
            Assert.That(env.Subscription.TriggeringLinkCount, Is.EqualTo((targets / 2) - 1));
        }

        /// <summary>
        /// Triggering links had no limit (#36). A link beyond
        /// <see cref="Subscription.kMaxTriggeringLinks"/> is refused with
        /// Bad_TooManyOperations, existing links are still accepted, and removing links
        /// makes room again.
        /// </summary>
        [Test]
        public async Task TriggeringLinksAreLimitedPerSubscriptionAsync()
        {
            using var env = new SubscriptionEnvironment(publishingEnabled: true);
            const int targets = 512;
            const int triggers = (Subscription.kMaxTriggeringLinks / targets) + 1;

            uint[] targetIds = env.CreateIds(targets);
            uint[] triggerIds = env.CreateIds(triggers);
            await env.RegisterAllAsync().ConfigureAwait(false);

            for (int ii = 0; ii < triggers - 1; ii++)
            {
                env.Subscription.SetTriggering(
                    env.Context, triggerIds[ii], [.. targetIds], [],
                    out ArrayOf<StatusCode> results, out _, out _, out _);
                Assert.That(results[targets - 1], Is.EqualTo((StatusCode)StatusCodes.Good));
            }
            Assert.That(env.Subscription.TriggeringLinkCount, Is.EqualTo(Subscription.kMaxTriggeringLinks));

            uint last = triggerIds[triggers - 1];
            env.Subscription.SetTriggering(
                env.Context, last, [targetIds[0]], [],
                out ArrayOf<StatusCode> refused, out _, out _, out _);
            Assert.That(refused[0], Is.EqualTo((StatusCode)StatusCodes.BadTooManyOperations));

            // an existing link is not a new one.
            env.Subscription.SetTriggering(
                env.Context, triggerIds[0], [targetIds[0]], [],
                out ArrayOf<StatusCode> existing, out _, out _, out _);
            Assert.That(existing[0], Is.EqualTo((StatusCode)StatusCodes.Good));

            // removing a link makes room for one.
            env.Subscription.SetTriggering(
                env.Context, triggerIds[0], [], [targetIds[0]],
                out _, out _, out ArrayOf<StatusCode> removed, out _);
            Assert.That(removed[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            env.Subscription.SetTriggering(
                env.Context, last, [targetIds[0], targetIds[1]], [],
                out ArrayOf<StatusCode> added, out _, out _, out _);
            Assert.That(added[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(added[1], Is.EqualTo((StatusCode)StatusCodes.BadTooManyOperations));
            Assert.That(env.Subscription.TriggeringLinkCount, Is.EqualTo(Subscription.kMaxTriggeringLinks));
        }

        /// <summary>
        /// A deleted item stayed in the ready candidates of a subscription whose publishing
        /// is disabled, because only the publish timer purged them (#40). A create/delete
        /// loop retained every deleted item.
        /// </summary>
        [Test]
        public async Task DeletedItemsDoNotAccumulateInTheReadyCandidatesAsync()
        {
            using var env = new SubscriptionEnvironment(publishingEnabled: false);
            const int cycles = 500;
            for (int ii = 0; ii < cycles; ii++)
            {
                uint[] ids = env.CreateIds(1);
                await env.RegisterAllAsync().ConfigureAwait(false);
                await env.Subscription.DeleteMonitoredItemsAsync(env.Context, [.. ids]).ConfigureAwait(false);
                env.Forget(ids[0]);
            }

            int retained = env.ReadyCandidateCount + env.ReadyIntakeCount;
            await TestContext.Out.WriteLineAsync(
                $"candidate entries after {cycles} create/delete cycles: {retained}")
                .ConfigureAwait(false);
            Assert.That(env.Subscription.MonitoredItemCount, Is.Zero);
            Assert.That(retained, Is.LessThanOrEqualTo(65),
                "the candidates of deleted items are purged once they outnumber the live items");
        }

        /// <summary>
        /// The classic CustomNodeManager2.Write reported a write audit event per item while
        /// it held <see cref="CustomNodeManager2.Lock"/>. The event consumers of the Server
        /// object need that lock for their permission checks while the writer waits for
        /// room in their bounded channel, so a multi-item Write deadlocked (#10).
        /// </summary>
        [Test]
        public void ClassicWriteReportsAuditEventsOutsideTheNodeManagerLock()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                server.Setup(s => s.Auditing).Returns(true);
                const string ns = "http://test.org/UA/ClassicWriteAudit/";
                using var nodeManager = new TestableCustomNodeManger2(server.Object, ns);
                ushort index = (ushort)server.Object.NamespaceUris.GetIndex(ns);

                const int count = 8;
                var nodesToWrite = new WriteValue[count];
                for (int ii = 0; ii < count; ii++)
                {
                    var variable = new BaseDataVariableState(null)
                    {
                        NodeId = new NodeId((uint)(ii + 1), index),
                        BrowseName = new QualifiedName("V" + ii, index),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    };
                    nodeManager.AddPredefinedNode(nodeManager.SystemContext, variable);
                    nodesToWrite[ii] = new WriteValue
                    {
                        NodeId = variable.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(ii + 1))
                    };
                }

                var lockHeldWhileReporting = new List<bool>();
                server
                    .Setup(s => s.ReportAuditEvent(It.IsAny<ISystemContext>(), It.IsAny<AuditEventState>()))
                    .Callback(() => lockHeldWhileReporting.Add(Monitor.IsEntered(nodeManager.Lock)));

                var identity = new UserIdentity(new AnonymousIdentityToken());
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Write, RequestLifetime.None, identity);
                var errors = new ServiceResult[count];

                nodeManager.Write(context, [.. nodesToWrite], errors);

                foreach (ServiceResult error in errors)
                {
                    Assert.That(error == null || ServiceResult.IsGood(error), Is.True, error?.ToString());
                }
                Assert.That(lockHeldWhileReporting, Has.Count.EqualTo(count));
                Assert.That(lockHeldWhileReporting, Has.All.False,
                    "audit events are reported after the node-manager lock is released");
            }
        }

        private static MonitoredItem CreateUnresolvedItem(
            ITelemetryContext telemetry,
            object subscriptionLock,
            ManualResetEventSlim publishingIntervalRead)
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(new MonitoredItemQueueFactory(telemetry));

            var item = new MonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                null!,
                1,
                2,
                new ReadValueId { NodeId = new NodeId("V", 1), AttributeId = Attributes.Value },
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                null,
                null,
                null,
                1000.0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);

            // the publishing interval takes the subscription lock, as Subscription does.
            var subscription = new Mock<ISubscription>();
            subscription.Setup(s => s.Session).Returns((ISession)null!);
            subscription.SetupGet(s => s.PublishingInterval).Returns(() =>
            {
                publishingIntervalRead.Set();
                lock (subscriptionLock)
                {
                    return 1000.0;
                }
            });
            item.SubscriptionCallback = subscription.Object;

            // the node manager released the source (node deleted): the item is unresolved.
            var binding = (IMonitoredItemGenerationBinding)item;
            Assert.That(binding.TryClearSource(0), Is.True);
            Assert.That(binding.IsSourceUnavailable, Is.True);
            return item;
        }

        /// <summary>
        /// A subscription with real monitored items whose node manager calls are no-ops.
        /// </summary>
        private sealed class SubscriptionEnvironment : IDisposable
        {
            public SubscriptionEnvironment(bool publishingEnabled)
            {
                m_telemetry = NUnitTelemetryContext.Create();
                m_queueFactory = new MonitoredItemQueueFactory(m_telemetry);
                m_server = new Mock<IServerInternal>();
                m_session = new Mock<ISession>();
                m_nodeManager = new Mock<IMasterNodeManager>();
                var diagnostics = new Mock<IDiagnosticsNodeManager>();

                m_server.Setup(s => s.Telemetry).Returns(m_telemetry);
                m_server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);
                m_server.Setup(s => s.NodeManager).Returns(m_nodeManager.Object);
                m_server.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactory);
                var namespaceUris = new NamespaceTable();
                m_server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
                m_server.Setup(s => s.ServerUris).Returns(new StringTable());
                m_server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
                m_server.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);
                m_server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(m_server.Object));

                var identity = new UserIdentity(new AnonymousIdentityToken());
                m_session.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
                m_session.Setup(s => s.Identity).Returns(identity);
                m_session.Setup(s => s.IdentityToken).Returns(identity.TokenHandler);
                diagnostics
                    .Setup(d => d.CreateSubscriptionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SubscriptionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>()))
                    .ReturnsAsync(new NodeId(1));
                m_nodeManager
                    .Setup(n => n.DeleteMonitoredItemsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<uint>(),
                        It.IsAny<IList<IMonitoredItem>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask));
                m_nodeManager
                    .Setup(n => n.CreateMonitoredItemsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<uint>(),
                        It.IsAny<double>(),
                        It.IsAny<TimestampsToReturn>(),
                        It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<IList<MonitoringFilterResult>>(),
                        It.IsAny<IList<IMonitoredItem>>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<OperationContext, uint, double, TimestampsToReturn, ArrayOf<MonitoredItemCreateRequest>,
                        IList<ServiceResult>, IList<MonitoringFilterResult>, IList<IMonitoredItem>, bool, CancellationToken>(
                        (_, _, _, _, _, _, _, created, _, _) =>
                        {
                            for (int ii = 0; ii < m_pending.Count; ii++)
                            {
                                created[ii] = m_pending[ii];
                            }
                        })
                    .Returns(default(ValueTask));

                Subscription = new Subscription(
                    m_server.Object,
                    m_session.Object,
                    subscriptionId: 1,
                    publishingInterval: 1000,
                    maxLifetimeCount: 1000,
                    maxKeepAliveCount: 10,
                    maxNotificationsPerPublish: 0,
                    priority: 0,
                    publishingEnabled: publishingEnabled,
                    maxMessageCount: 10);
                Context = new OperationContext(m_session.Object, DiagnosticsMasks.None);
            }

            public Subscription Subscription { get; }

            public OperationContext Context { get; }

            public int ReadyCandidateCount => GetField<List<IMonitoredItem>>("m_readyCandidates").Count;

            public int ReadyIntakeCount => GetField<List<IMonitoredItem>>("m_readyIntake").Count;

            /// <summary>
            /// Creates monitored items that the next <see cref="RegisterAllAsync"/> adds.
            /// </summary>
            public uint[] CreateIds(int count)
            {
                uint[] ids = new uint[count];
                for (int ii = 0; ii < count; ii++)
                {
                    uint id = m_nextId++;
                    var item = new MonitoredItem(
                        m_server.Object,
                        m_owner.Object,
                        new object(),
                        Subscription.Id,
                        id,
                        new ReadValueId { NodeId = new NodeId(id, 2), AttributeId = Attributes.Value },
                        DiagnosticsMasks.None,
                        TimestampsToReturn.Both,
                        MonitoringMode.Reporting,
                        clientHandle: id,
                        originalFilter: null,
                        filterToUse: null,
                        range: null,
                        samplingInterval: 1000,
                        queueSize: 10,
                        discardOldest: true,
                        sourceSamplingInterval: 1000);
                    m_items[id] = item;
                    m_pending.Add(item);
                    ids[ii] = id;
                }
                return ids;
            }

            public async Task RegisterAllAsync()
            {
                var requests = new MonitoredItemCreateRequest[m_pending.Count];
                for (int ii = 0; ii < requests.Length; ii++)
                {
                    requests[ii] = new MonitoredItemCreateRequest { MonitoringMode = MonitoringMode.Reporting };
                }
                await Subscription.CreateMonitoredItemsAsync(
                    Context, TimestampsToReturn.Both, [.. requests]).ConfigureAwait(false);
                m_pending.Clear();
            }

            /// <summary>
            /// Forgets a deleted item so that the environment does not keep it alive.
            /// </summary>
            public void Forget(uint id)
            {
                if (m_items.TryGetValue(id, out MonitoredItem? item))
                {
                    m_items.Remove(id);
                    item.Dispose();
                }
            }

            private T GetField<T>(string name)
            {
                return (T)typeof(Subscription)
                    .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(Subscription)!;
            }

            public void Dispose()
            {
                Subscription.Dispose();
                foreach (MonitoredItem item in m_items.Values)
                {
                    item.Dispose();
                }
                Context.Dispose();
                m_queueFactory.Dispose();
            }

            private readonly ITelemetryContext m_telemetry;
            private readonly MonitoredItemQueueFactory m_queueFactory;
            private readonly Mock<IServerInternal> m_server;
            private readonly Mock<ISession> m_session;
            private readonly Mock<IMasterNodeManager> m_nodeManager;
            private readonly Mock<IAsyncNodeManager> m_owner = new();
            private readonly Dictionary<uint, MonitoredItem> m_items = [];
            private readonly List<MonitoredItem> m_pending = [];
            private uint m_nextId = 1;
        }
    }
}
