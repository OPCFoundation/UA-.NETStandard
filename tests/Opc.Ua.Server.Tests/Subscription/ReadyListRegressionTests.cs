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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The publish timer only looks at monitored items that signalled pending notifications
    /// (the ready list). These tests run the same scenario on a subscription that uses the
    /// ready list and on one that scans all items, because it also has an item that cannot
    /// signal, and require identical Publish results.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public sealed class ReadyListRegressionTests
    {
        private const double kPublishingInterval = 100;

        [Test]
        public async Task ReadyListMatchesScanForTriggeringAsync()
        {
            await AssertSameAsScanAsync(async scenario =>
            {
                // item 3 samples and is reported only when item 1 triggers it.
                await scenario.SetMonitoringModeAsync(MonitoringMode.Sampling, 3).ConfigureAwait(false);
                scenario.SetTriggering(1, 3);

                scenario.Queue(3, 30);
                scenario.Tick("sampled, not triggered");
                scenario.Queue(1, 10);
                scenario.Tick("trigger reports the sample");
                scenario.Queue(3, 31);
                scenario.Queue(3, 32);
                scenario.Tick("sampled again");
                scenario.Queue(1, 11);
                scenario.Queue(2, 20);
                scenario.Tick("trigger again");
                scenario.Tick("idle");

                // a backlog of the triggering item keeps triggering while it drains.
                scenario.Queue(1, 12);
                scenario.Queue(1, 13);
                scenario.Queue(3, 33);
                scenario.Tick("backlog");
                scenario.Tick("backlog drained");
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadyListMatchesScanForMonitoringModeChangesAsync()
        {
            await AssertSameAsScanAsync(async scenario =>
            {
                scenario.Queue(1, 10);
                scenario.Queue(2, 20);
                scenario.Tick("both report");

                await scenario.SetMonitoringModeAsync(MonitoringMode.Sampling, 2).ConfigureAwait(false);
                scenario.Queue(2, 21);
                scenario.Tick("sampling item stays quiet");
                await scenario.SetMonitoringModeAsync(MonitoringMode.Reporting, 2).ConfigureAwait(false);
                scenario.Tick("back to reporting");
                scenario.Queue(2, 22);
                scenario.Tick("reports again");

                await scenario.SetMonitoringModeAsync(MonitoringMode.Disabled, 1).ConfigureAwait(false);
                scenario.Queue(1, 11);
                scenario.Tick("disabled item ignores values");
                await scenario.SetMonitoringModeAsync(MonitoringMode.Reporting, 1).ConfigureAwait(false);
                scenario.Tick("enabled without a value");
                scenario.Queue(1, 12);
                scenario.Tick("enabled with a value");

                // queued while reporting, disabled before the publish timer looked at it.
                scenario.Queue(3, 30);
                await scenario.SetMonitoringModeAsync(MonitoringMode.Disabled, 3).ConfigureAwait(false);
                scenario.Tick("disabled before publish");
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadyListMatchesScanForDisabledPublishingAsync()
        {
            await AssertSameAsScanAsync(scenario =>
            {
                scenario.Queue(1, 10);
                scenario.Tick("enabled");

                scenario.SetPublishingMode(false);
                scenario.Queue(1, 11);
                scenario.Queue(2, 20);
                for (int ii = 0; ii < 6; ii++)
                {
                    scenario.Tick("disabled " + ii.ToString(CultureInfo.InvariantCulture));
                }

                scenario.SetPublishingMode(true);
                scenario.Tick("enabled again");
                scenario.Queue(3, 30);
                scenario.Tick("new value");

                scenario.ResendData();
                scenario.Tick("resend");
                scenario.Tick("idle");
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadyListReportsValueQueuedWhileItemWasInPublishListAsync()
        {
            await AssertSameAsScanAsync(scenario =>
            {
                // the publish limit leaves item 2 in the publish list with a backlog.
                scenario.Queue(2, 20);
                scenario.Queue(2, 21);
                scenario.Queue(2, 22);
                scenario.Queue(1, 10);
                scenario.Tick("limited");
                scenario.Queue(2, 23);
                scenario.Tick("drains backlog");
                scenario.Tick("drained");
                scenario.Queue(2, 24);
                scenario.Tick("new value after the backlog");
                return Task.CompletedTask;
            }, maxNotificationsPerPublish: 2).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadyListReportsBacklogOfItemThatLeftReportingWhilePublishingAsync()
        {
            await AssertSameAsScanAsync(async scenario =>
            {
                // item 2 is in the publish list with a backlog when it stops reporting.
                scenario.Queue(2, 20);
                scenario.Queue(2, 21);
                scenario.Queue(2, 22);
                scenario.Tick("limited");
                await scenario.SetMonitoringModeAsync(MonitoringMode.Sampling, 2).ConfigureAwait(false);
                scenario.Tick("sampling keeps the backlog");
                scenario.Tick("still sampling");
                await scenario.SetMonitoringModeAsync(MonitoringMode.Reporting, 2).ConfigureAwait(false);
                scenario.Tick("reporting again");
                scenario.Tick("drained");
            }, maxNotificationsPerPublish: 1, maxMessageCount: 1).ConfigureAwait(false);
        }

        [Test]
        public async Task OnlyChangedItemsAreCheckedByThePublishTimerAsync()
        {
            using var scenario = new Scenario(withScanItem: false, maxNotificationsPerPublish: 0, maxMessageCount: 100);
            await scenario.CreateAsync(itemCount: 50).ConfigureAwait(false);
            scenario.Tick("initial");

            scenario.Queue(7, 70);
            Assert.That(scenario.ReadyCandidateCount, Is.Zero);
            scenario.Tick("one change");
            Assert.That(scenario.Trace[scenario.Trace.Count - 1], Does.Contain("7=70"));
            Assert.That(scenario.ReadyCandidateCount, Is.Zero,
                "a published item without pending values is not tracked");
        }

        [Test]
        public async Task CreatingMonitoredItemsRaisesTheNotificationPoolBoundAsync()
        {
            // the pools are process-wide and other tests may only raise them further.
            using var scenario = new Scenario(withScanItem: false, maxNotificationsPerPublish: 0, maxMessageCount: 100);
            await scenario.CreateAsync(itemCount: 1200).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(MonitoredItemNotificationActivator.Instance.MaximumRetained, Is.GreaterThanOrEqualTo(2400));
                Assert.That(EventFieldListActivator.Instance.MaximumRetained, Is.GreaterThanOrEqualTo(2400));
            });
        }

        private static async Task AssertSameAsScanAsync(
            Func<Scenario, Task> run,
            uint maxNotificationsPerPublish = 0,
            uint maxMessageCount = 100)
        {
            using var readyList = new Scenario(withScanItem: false, maxNotificationsPerPublish, maxMessageCount);
            using var scan = new Scenario(withScanItem: true, maxNotificationsPerPublish, maxMessageCount);
            await readyList.CreateAsync(itemCount: 3).ConfigureAwait(false);
            await scan.CreateAsync(itemCount: 3).ConfigureAwait(false);

            await run(readyList).ConfigureAwait(false);
            await run(scan).ConfigureAwait(false);
            await TestContext.Out.WriteLineAsync(string.Join(Environment.NewLine, readyList.Trace)).ConfigureAwait(false);

            Assert.That(readyList.Trace, Has.Some.Contains("="),
                "the scenario publishes notifications");
            Assert.That(readyList.Trace, Is.EqualTo(scan.Trace));
        }

        /// <summary>
        /// A subscription with real monitored items and a fake clock that records what every
        /// publishing cycle returns.
        /// </summary>
        private sealed class Scenario : IDisposable
        {
            public Scenario(bool withScanItem, uint maxNotificationsPerPublish, uint maxMessageCount)
            {
                m_withScanItem = withScanItem;
                m_telemetry = NUnitTelemetryContext.Create();
                m_queueFactory = new MonitoredItemQueueFactory(m_telemetry);
                m_clock = new FakeTimeProvider();

                m_server = new Mock<IServerInternal>();
                m_session = new Mock<ISession>();
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                m_nodeManager = new Mock<IMasterNodeManager>();
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
                    .Setup(n => n.SetMonitoringModeAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<MonitoringMode>(),
                        It.IsAny<IList<IMonitoredItem>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<OperationContext, MonitoringMode, IList<IMonitoredItem>, IList<ServiceResult>, CancellationToken>(
                        (_, mode, items, _, _) =>
                        {
                            foreach (IMonitoredItem item in items)
                            {
                                ((MonitoredItem)item).SetMonitoringMode(mode);
                            }
                        })
                    .Returns(default(ValueTask));

                Subscription = new Subscription(
                    m_server.Object,
                    m_session.Object,
                    subscriptionId: 1,
                    publishingInterval: kPublishingInterval,
                    maxLifetimeCount: 1000,
                    maxKeepAliveCount: 3,
                    maxNotificationsPerPublish: maxNotificationsPerPublish,
                    priority: 0,
                    publishingEnabled: true,
                    maxMessageCount: maxMessageCount,
                    timeProvider: m_clock);
                m_context = new OperationContext(m_session.Object, DiagnosticsMasks.None);
            }

            public Subscription Subscription { get; }

            public List<string> Trace { get; } = [];

            public int ReadyCandidateCount => ((List<IMonitoredItem>)typeof(Subscription)
                .GetField("m_readyCandidates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(Subscription)!).Count;

            public async Task CreateAsync(int itemCount)
            {
                var owner = new Mock<IAsyncNodeManager>();
                var items = new List<IMonitoredItem>();
                for (uint id = 1; id <= itemCount; id++)
                {
                    var item = new MonitoredItem(
                        m_server.Object,
                        owner.Object,
                        new object(),
                        Subscription.Id,
                        id,
                        new ReadValueId { NodeId = new NodeId(id, 2), AttributeId = Attributes.Value },
                        DiagnosticsMasks.None,
                        TimestampsToReturn.Neither,
                        MonitoringMode.Reporting,
                        clientHandle: id,
                        originalFilter: null,
                        filterToUse: null,
                        range: null,
                        samplingInterval: 0,
                        queueSize: 3,
                        discardOldest: true,
                        sourceSamplingInterval: 0,
                        createDurable: false,
                        timeProvider: m_clock);
                    m_items[id] = item;
                    items.Add(item);
                }

                if (m_withScanItem)
                {
                    // an item that does not signal makes the publish timer scan all items.
                    var scanItem = new Mock<IMonitoredItem>();
                    scanItem.SetupGet(i => i.Id).Returns(1000);
                    scanItem.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Disabled);
                    var createResult = new MonitoredItemCreateResult { MonitoredItemId = 1000 };
                    scanItem.Setup(i => i.GetCreateResult(out createResult)).Returns(ServiceResult.Good);
                    items.Add(scanItem.Object);
                }

                m_nodeManager
                    .Setup(n => n.CreateMonitoredItemsAsync(
                        It.IsAny<OperationContext>(),
                        Subscription.Id,
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
                            for (int ii = 0; ii < items.Count; ii++)
                            {
                                created[ii] = items[ii];
                            }
                        })
                    .Returns(default(ValueTask));

                ArrayOf<MonitoredItemCreateRequest> requests = [.. items.Select(_ =>
                    new MonitoredItemCreateRequest { MonitoringMode = MonitoringMode.Reporting })];
                await Subscription.CreateMonitoredItemsAsync(
                    m_context,
                    TimestampsToReturn.Neither,
                    requests,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public void Queue(uint id, int value)
            {
                m_items[id].QueueValue(new DataValue(new Variant(value)), null);
            }

            public async Task SetMonitoringModeAsync(MonitoringMode mode, uint id)
            {
                await Subscription.SetMonitoringModeAsync(m_context, mode, [id]).ConfigureAwait(false);
            }

            public void SetTriggering(uint triggeringId, uint linkedId)
            {
                Subscription.SetTriggering(m_context, triggeringId, [linkedId], [], out _, out _, out _, out _);
            }

            public void SetPublishingMode(bool enabled)
            {
                Subscription.SetPublishingMode(m_context, enabled);
            }

            public void ResendData()
            {
                Subscription.ResendData(m_context);
            }

            /// <summary>
            /// Lets the publishing interval expire and answers one Publish request when
            /// notifications are available, as for a client with one outstanding request.
            /// </summary>
            public void Tick(string step)
            {
                m_clock.Advance(TimeSpan.FromMilliseconds(kPublishingInterval + 1));
                PublishingState state = Subscription.AsPipeline().PublishTimerExpired(publishRequestQueued: true);
                string entry = step + ": " + state;
                if (state == PublishingState.NotificationsAvailable)
                {
                    NotificationMessage? message = Subscription.AsPipeline().Publish(
                        m_context,
                        out _,
                        out bool moreNotifications,
                        out NotificationMessageLease? lease);
                    entry += " [" + Describe(message) + "]" + (moreNotifications ? " more" : string.Empty);
                    if (message != null && message.NotificationData.Count > 0)
                    {
                        Subscription.AsPipeline().Acknowledge(m_context, message.SequenceNumber);
                    }
                    lease?.Release();
                }
                Trace.Add(entry);
            }

            private static string Describe(NotificationMessage? message)
            {
                if (message == null)
                {
                    return "none";
                }
                if (message.NotificationData.Count == 0)
                {
                    return "keep-alive";
                }
                var notifications = new List<MonitoredItemNotification>();
                foreach (ExtensionObject data in message.NotificationData)
                {
                    if (data.TryGetValue(out DataChangeNotification? dataChange))
                    {
                        notifications.AddRange(dataChange.MonitoredItems);
                    }
                }

                // the order of different items within a message is not defined (the ready
                // list reports items in the order they became ready, the scan in list order);
                // the order of the values of one item is kept by the stable sort.
                return string.Join(",", notifications
                    .OrderBy(item => item.ClientHandle)
                    .Select(item => string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}={1}",
                        item.ClientHandle,
                        item.Value.WrappedValue)));
            }

            public void Dispose()
            {
                Subscription.Dispose();
                foreach (MonitoredItem item in m_items.Values)
                {
                    item.Dispose();
                }
                m_context.Dispose();
                m_queueFactory.Dispose();
            }

            private readonly bool m_withScanItem;
            private readonly ITelemetryContext m_telemetry;
            private readonly MonitoredItemQueueFactory m_queueFactory;
            private readonly FakeTimeProvider m_clock;
            private readonly Mock<IServerInternal> m_server;
            private readonly Mock<ISession> m_session;
            private readonly Mock<IMasterNodeManager> m_nodeManager;
            private readonly OperationContext m_context;
            private readonly Dictionary<uint, MonitoredItem> m_items = [];
        }
    }
}
