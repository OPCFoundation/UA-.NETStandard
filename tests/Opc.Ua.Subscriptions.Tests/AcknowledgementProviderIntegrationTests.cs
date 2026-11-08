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

// CA2016: integration tests intentionally call cleanup in finally without forwarding the test
// cancellation token, which may already be cancelled when cleanup must still run.
#pragma warning disable CA2016

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Quickstarts.ReferenceServer;
using ClassicSubscription = Opc.Ua.Client.Subscription;
using ISubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;
using MonitoredItem = Opc.Ua.Client.MonitoredItem;
using ServerMonitoredItem = Opc.Ua.Server.IMonitoredItem;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// End-to-end tests of the acknowledgement provider model against the in-process
    /// reference server: in confirmation mode a notification is only acknowledged after the
    /// application confirmed its handle, on the V2 engine and on the classic engine.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class AcknowledgementProviderIntegrationTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// A static monitored value becomes quiet after its initial notification. Its acknowledgement
        /// and subsequent keepalives must survive multiple queued Publish requests.
        /// </summary>
        [TestCase(2)]
        [TestCase(4)]
        [CancelAfter(90_000)]
        public async Task QuietSubscriptionAcknowledgementSurvivesConcurrentPublishesAsync(
            int workers, CancellationToken ct)
        {
            var observer = new RecordingObserver();
            ManagedSessionType session = await ConnectAsync(
                nameof(QuietSubscriptionAcknowledgementSurvivesConcurrentPublishesAsync),
                observer, classic: false, ct).ConfigureAwait(false);
            try
            {
                if (workers != 2)
                {
                    session.MinPublishRequestCount = workers;
                }
                var handler = new HandleCollectingHandler();
                await using ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(100),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(subscription.TryAddMonitoredItem(
                    "StartTime", VariableIds.Server_ServerStatus_StartTime,
                    options => options with { SamplingInterval = TimeSpan.FromMilliseconds(100) },
                    out _), Is.True);
                await WaitForAsync(() => !handler.Handles.IsEmpty && handler.KeepAliveCount >= workers, ct)
                    .ConfigureAwait(false);

                AcknowledgementHandle handle = handler.Handles.Single();
                Assert.That(subscription.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
                Assert.That(subscription.CurrentKeepAliveCount, Is.EqualTo(10));
                Assert.That(observer.WasAcknowledged(handle), Is.False);
                ISubscriptionEngine engine = session.InnerSession.SubscriptionEngine;
                Assert.That(engine.PublishWorkerCount, Is.EqualTo(workers));
                session.Confirm(handle);
                await WaitForAsync(
                    () => observer.WasAcknowledged(handle) && handler.KeepAliveCount >= workers + 4, ct)
                    .ConfigureAwait(false);

                Assert.That(observer.Outcomes.Count(outcome => outcome.Handle == handle), Is.EqualTo(1));
                Assert.That(engine.BadPublishRequestCount, Is.Zero);
                Assert.That(engine.PublishWorkerCount, Is.EqualTo(workers));
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A split server Publish burst preserves its response markers through native partition forwarding.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(90_000)]
        public async Task NativeSplitPublishMetadataSurvivesPartitionForwardingAsync(
            bool partitioned, CancellationToken ct)
        {
            var observer = new RecordingObserver();
            await using ManagedSessionType session = await ConnectAsync(
                nameof(NativeSplitPublishMetadataSurvivesPartitionForwardingAsync), observer, classic: false, ct)
                .ConfigureAwait(false);
            var handler = new MessageCollectingHandler();
            var options = new OptionsMonitor<Client.Subscriptions.SubscriptionOptions>(
                new Client.Subscriptions.SubscriptionOptions
                {
                    PublishingEnabled = false,
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 1,
                    MaxNotificationsPerPublish = 1,
                    MaxMonitoredItemsPerPartition = 3,
                    DisableUnboundedItemMode = !partitioned
                });
            Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            // Concurrent publish workers can deliver the responses out of order. A message overtaken
            // that way is republished, and a Republish response has no MoreNotifications to preserve.
            manager!.MinPublishWorkerCount = 1;
            manager.MaxPublishWorkerCount = 1;
            await using ISubscription subscription = manager.Add(handler, options);
            var items = new List<Client.Subscriptions.MonitoredItems.IMonitoredItem>();
            int expected = partitioned ? 6 : 3;
            for (int i = 0; i < expected; i++)
            {
                Assert.That(subscription.TryAddMonitoredItem(
                    $"state-{i}",
                    VariableIds.Server_ServerStatus_State,
                    value => value with { QueueSize = 1, SamplingInterval = TimeSpan.FromMilliseconds(100) },
                    out Client.Subscriptions.MonitoredItems.IMonitoredItem? item), Is.True);
                items.Add(item!);
            }
            await WaitForAsync(() => items.All(item => item.Created), ct).ConfigureAwait(false);
            await WaitForAsync(() => manager.PublishWorkerCount == 1, ct).ConfigureAwait(false);
            Assert.That(ReferenceServer, Is.InstanceOf<BurstReferenceServer>());
            var server = (BurstReferenceServer)ReferenceServer;
            await WaitForAsync(() => server.InitialSamplesReady(session.SessionId, expected), ct).ConfigureAwait(false);
            uint initialRepublishCount = GetServerRepublishCount(session);
            options.CurrentValue = options.CurrentValue with { PublishingEnabled = true };
            int expectedGroups = partitioned ? 2 : 1;
            // A terminal marker closes each subscription's Publish burst. The server diagnostic
            // check below confirms that Republish did not supply any collected messages.
            await WaitForAsync(() =>
            {
                SubscriptionNotificationMessage[] observed = [.. handler.Messages];
                if (observed.Length < expected)
                {
                    return false;
                }
                IGrouping<uint, SubscriptionNotificationMessage>[] observedGroups =
                    [.. observed.GroupBy(message => message.Acknowledgement.SubscriptionId)];
                return observedGroups.Length == expectedGroups &&
                    observedGroups.All(group => group.Any(message => message.MoreNotifications == false));
            }, ct).ConfigureAwait(false);

            SubscriptionNotificationMessage[] messages = [.. handler.Messages];
            Assert.That(GetServerRepublishCount(session), Is.EqualTo(initialRepublishCount));
            Assert.That(messages, Has.Length.EqualTo(expected));
            Assert.That(manager.PublishWorkerCount, Is.EqualTo(1));
            IGrouping<uint, SubscriptionNotificationMessage>[] groups =
                [.. messages.GroupBy(message => message.Acknowledgement.SubscriptionId)];
            Assert.That(groups, Has.Length.EqualTo(expectedGroups));
            Assert.That(server.RepublishRequests.Any(id => groups.Any(group => group.Key == id)), Is.False);
            Assert.That(manager.RepublishMessageCount, Is.Zero);
            foreach (IGrouping<uint, SubscriptionNotificationMessage> group in groups)
            {
                SubscriptionNotificationMessage[] ordered =
                    [.. group.OrderBy(message => message.Acknowledgement.SequenceNumber)];
                Assert.That(ordered.Select(message => message.MoreNotifications),
                    Is.EqualTo(new bool?[] { true, true, false }));
                Assert.That(ordered.All(message => message.Notifications[0].DataChanges.Count == 1), Is.True);
            }
            var acknowledgements = new HashSet<AcknowledgementHandle>();
            foreach (SubscriptionNotificationMessage message in messages)
            {
                acknowledgements.Add(message.Acknowledgement);
            }
            Assert.That(acknowledgements, Has.Count.EqualTo(expected));
            foreach (AcknowledgementHandle acknowledgement in acknowledgements)
            {
                Assert.That(observer.WasAcknowledged(acknowledgement), Is.False);
                session.Confirm(acknowledgement);
            }
            await WaitForAsync(
                () => acknowledgements.All(observer.WasAcknowledged),
                ct).ConfigureAwait(false);
            Assert.That(handler.Messages, Has.Count.EqualTo(expected));
            Assert.That(server.RepublishRequests.Any(id => groups.Any(group => group.Key == id)), Is.False);
        }

        /// <inheritdoc/>
        protected override ReferenceServer CreateReferenceServer(ITelemetryContext telemetry)
        {
            return new BurstReferenceServer(telemetry);
        }

        /// <summary>
        /// Observes committed initial samples and recovery requests without replacing live service behavior.
        /// </summary>
        private sealed class BurstReferenceServer(ITelemetryContext telemetry) : ReferenceServer(telemetry)
        {
            /// <summary>
            /// Records the subscriptions for which recovery was requested.
            /// </summary>
            public ConcurrentQueue<uint> RepublishRequests { get; } = new();

            /// <summary>
            /// Requires every intended initial sample and exactly three items per native subscription.
            /// </summary>
            public bool InitialSamplesReady(NodeId sessionId, int expected)
            {
                // The configuration/diagnostics manager owns ServerStatus.State, not the core
                // manager. Its generation registry contains the committed subscription-bound items.
                if (CurrentInstance?.DiagnosticsNodeManager is not { } nodeManager ||
                    !NodeManagerGenerationRegistry.TryGet(nodeManager, out NodeManagerGeneration? generation) ||
                    generation is null)
                {
                    return false;
                }
                ServerMonitoredItem[] items =
                    [.. generation.MonitoredItems
                        .Where(entry => entry.Value.SessionId == sessionId &&
                            entry.Key.NodeId == VariableIds.Server_ServerStatus_State)
                        .Select(entry => entry.Key)];
                return items.Length == expected &&
                    items.All(item => item.IsReadyToPublish) &&
                    items.GroupBy(item => item.SubscriptionId).All(group => group.Count() == 3);
            }

            /// <inheritdoc/>
            public override ValueTask<RepublishResponse> RepublishAsync(
                SecureChannelContext secureChannelContext,
                RequestHeader? requestHeader,
                uint subscriptionId,
                uint retransmitSequenceNumber,
                RequestLifetime requestLifetime)
            {
                RepublishRequests.Enqueue(subscriptionId);
                return base.RepublishAsync(secureChannelContext, requestHeader, subscriptionId,
                    retransmitSequenceNumber, requestLifetime);
            }
        }

        /// <summary>
        /// Deferred notification decoding is on for the V2 engine by default, delivers the
        /// same notifications either way, and stays off for the classic engine.
        /// </summary>
        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [CancelAfter(90_000)]
        public async Task DeferredNotificationDecodingFollowsTheOptionAsync(
            bool classic, bool defer, bool expected, CancellationToken ct)
        {
            var observer = new RecordingObserver();
            await using ManagedSessionType session = await ConnectAsync(
                nameof(DeferredNotificationDecodingFollowsTheOptionAsync), observer, classic, defer, ct)
                .ConfigureAwait(false);

            Assert.That(session.MessageContext, Is.InstanceOf<ServiceMessageContext>());
            Assert.That(((ServiceMessageContext)session.MessageContext).DeferNotificationBodies, Is.EqualTo(expected));
        }

        /// <summary>
        /// Classic subscriptions retain live split markers through either session engine.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(90_000)]
        public async Task ClassicSplitPublishMetadataSurvivesSessionDispatchAsync(bool classic, CancellationToken ct)
        {
            var observer = new RecordingObserver();
            await using ManagedSessionType session = await ConnectAsync(
                nameof(ClassicSplitPublishMetadataSurvivesSessionDispatchAsync), observer, classic, ct)
                .ConfigureAwait(false);
            var handler = new MessageCollectingHandler();
            using var subscription = new ClassicSubscription(Telemetry)
            {
                PublishingEnabled = false,
                PublishingInterval = 100,
                KeepAliveCount = 1,
                MaxNotificationsPerPublish = 1,
                NotificationMessageHandler = handler
            };
            for (int i = 0; i < 3; i++)
            {
                subscription.AddItem(new MonitoredItem(Telemetry)
                {
                    StartNodeId = VariableIds.Server_ServerStatus_State,
                    AttributeId = Attributes.Value,
                    SamplingInterval = 100,
                    QueueSize = 1
                });
            }
            Assert.That(session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            await subscription.SetPublishingModeAsync(true, ct).ConfigureAwait(false);
            await WaitForAsync(() => handler.Messages.Count >= 3, ct).ConfigureAwait(false);

            SubscriptionNotificationMessage[] messages =
                [.. handler.Messages.Take(3).OrderBy(message => message.Acknowledgement.SequenceNumber)];
            Assert.That(messages.Select(message => message.MoreNotifications),
                Is.EqualTo(new bool?[] { true, true, false }));
            foreach (SubscriptionNotificationMessage message in messages)
            {
                Assert.That(message.Acknowledgement.SubscriptionId, Is.EqualTo(subscription.Id));
                Assert.That(message.Notifications[0].DataChanges.Count, Is.EqualTo(1));
            }
            // The default engine's legacy dispatch fallback does not track acknowledgement handles.
            if (classic)
            {
                foreach (SubscriptionNotificationMessage message in messages)
                {
                    Assert.That(observer.WasAcknowledged(message.Acknowledgement), Is.False);
                    session.Confirm(message.Acknowledgement);
                }
                await WaitForAsync(() => messages.All(message => observer.WasAcknowledged(message.Acknowledgement)), ct)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(90_000)]
        public async Task ConfirmedNotificationsAreAcknowledgedOnTheV2EngineAsync(
            bool wholeMessage, CancellationToken ct)
        {
            var observer = new RecordingObserver();
            ManagedSessionType session = await ConnectAsync(
                nameof(ConfirmedNotificationsAreAcknowledgedOnTheV2EngineAsync), observer, classic: false, ct)
                .ConfigureAwait(false);
            var handler = new HandleCollectingHandler();
            var messages = new MessageCollectingHandler();
            try
            {
                Assert.That(session.Mode, Is.EqualTo(AcknowledgementMode.AfterConfirmation));
                var options = new Client.Subscriptions.SubscriptionOptions
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(200),
                    PublishingEnabled = true,
                    KeepAliveCount = 10,
                    LifetimeCount = 100
                };
                ISubscription subscription = wholeMessage
                    ? session.AddMessageSubscription(messages, options)
                    : session.AddSubscription(handler, options);
                Assert.That(subscription.TryAddMonitoredItem(
                    "CurrentTime",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(100), QueueSize = 10 },
                    out _), Is.True);

                ConcurrentQueue<AcknowledgementHandle> handles = wholeMessage ? messages.Handles : handler.Handles;
                await WaitForAsync(() => handles.Count >= 3, ct).ConfigureAwait(false);
                AcknowledgementHandle[] received = [.. handles.Take(3)];
                Assert.That(received.All(handle => !handle.IsNull), Is.True);

                // Unconfirmed notifications stay unacknowledged for several publish cycles.
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                Assert.That(received.Any(observer.WasAcknowledged), Is.False,
                    "Nothing is acknowledged before the application confirmed it.");

                foreach (AcknowledgementHandle handle in received)
                {
                    session.Confirm(handle);
                }
                await WaitForAsync(() => received.All(observer.WasAcknowledged), ct).ConfigureAwait(false);

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(90_000)]
        public async Task ConfirmedNotificationsAreAcknowledgedOnTheClassicEngineAsync(
            bool wholeMessage, CancellationToken ct)
        {
            var observer = new RecordingObserver();
            ManagedSessionType session = await ConnectAsync(
                nameof(ConfirmedNotificationsAreAcknowledgedOnTheClassicEngineAsync), observer, classic: true, ct)
                .ConfigureAwait(false);
            var handles = new ConcurrentQueue<AcknowledgementHandle>();
            var messages = new MessageCollectingHandler();
            if (wholeMessage)
            {
                handles = messages.Handles;
            }
            try
            {
                var subscription = new ClassicSubscription(Telemetry)
                {
                    PublishingEnabled = true,
                    PublishingInterval = 200,
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    NotificationMessageHandler = wholeMessage ? messages : null,
#pragma warning disable CS0618 // The classic engine is exercised through its fast callback.
                    FastDataChangeCallback = (owner, notification, _) =>
                        {
                            if (owner.TryGetAcknowledgementHandle(notification, out AcknowledgementHandle handle))
                            {
                                handles.Enqueue(handle);
                            }
                        }
                };
#pragma warning restore CS0618
                subscription.AddItem(new MonitoredItem(Telemetry)
                {
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    SamplingInterval = 100,
                    QueueSize = 10
                });
                Assert.That(session.AddSubscription(subscription), Is.True);
                await subscription.CreateAsync(ct).ConfigureAwait(false);

                await WaitForAsync(() => handles.Count >= 3, ct).ConfigureAwait(false);
                AcknowledgementHandle[] received = [.. handles.Take(3)];

                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                Assert.That(received.Any(observer.WasAcknowledged), Is.False,
                    "Nothing is acknowledged before the application confirmed it.");

                foreach (AcknowledgementHandle handle in received)
                {
                    session.Confirm(handle);
                }
                await WaitForAsync(() => received.All(observer.WasAcknowledged), ct).ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task<ManagedSessionType> ConnectAsync(
            string name,
            RecordingObserver observer,
            bool classic,
            CancellationToken ct)
        {
            return await ConnectAsync(name, observer, classic, deferNotificationDecoding: true, ct).ConfigureAwait(false);
        }

        private async Task<ManagedSessionType> ConnectAsync(
            string name,
            RecordingObserver observer,
            bool classic,
            bool deferNotificationDecoding,
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(name)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .AcknowledgeAfterConfirmation()
                .WithAcknowledgementObserver(observer);
            if (classic)
            {
                builder.UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance);
            }
            if (!deferNotificationDecoding)
            {
                builder.WithoutDeferredNotificationDecoding();
            }
            return await builder.ConnectAsync(ct).ConfigureAwait(false);
        }

        private static async Task WaitForAsync(Func<bool> predicate, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!predicate())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Timed out waiting for the expected condition.");
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gets the server-side Republish request count for the client session.
        /// </summary>
        private uint GetServerRepublishCount(ManagedSessionType session)
        {
            Opc.Ua.Server.ISession serverSession =
                ReferenceServer.CurrentInstance?.SessionManager.GetSession(
                    session.SaveSessionConfiguration().AuthenticationToken) ??
                throw new InvalidOperationException("The reference server did not create the client session.");
            return serverSession.ReadDiagnostics(diagnostics => diagnostics.RepublishCount.TotalCount);
        }

        private sealed class RecordingObserver : ISubscriptionAcknowledgementObserver
        {
            /// <summary>
            /// Gets all terminal acknowledgement results.
            /// </summary>
            public ConcurrentQueue<AcknowledgementOutcome> Outcomes { get; } = new();

            public bool WasAcknowledged(AcknowledgementHandle handle)
            {
                return m_acknowledged.ContainsKey(handle);
            }

            public ValueTask OnCompletedAsync(
                ArrayOf<AcknowledgementOutcome> outcomes,
                CancellationToken ct = default)
            {
                foreach (AcknowledgementOutcome outcome in outcomes)
                {
                    Outcomes.Enqueue(outcome);
                    if (outcome.Disposition == AcknowledgementDisposition.Acknowledged)
                    {
                        m_acknowledged.TryAdd(outcome.Handle, 0);
                    }
                }
                return default;
            }

            private readonly ConcurrentDictionary<AcknowledgementHandle, byte> m_acknowledged = new();
        }

        private sealed class MessageCollectingHandler : ISubscriptionNotificationMessageHandler
        {
            public ConcurrentQueue<AcknowledgementHandle> Handles { get; } = new();

            public ConcurrentQueue<SubscriptionNotificationMessage> Messages { get; } = new();

            public ValueTask OnNotificationMessageAsync(
                SubscriptionNotificationMessage message, CancellationToken ct = default)
            {
                Handles.Enqueue(message.Acknowledgement);
                Messages.Enqueue(message);
                return default;
            }
        }

        private sealed class HandleCollectingHandler : ISubscriptionNotificationHandler
        {
            public ConcurrentQueue<AcknowledgementHandle> Handles { get; } = new();

            /// <summary>
            /// Gets the number of keepalives received after the initial notification.
            /// </summary>
            public int KeepAliveCount => Volatile.Read(ref m_keepAliveCount);

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                if (notification.Length > 0)
                {
                    Handles.Enqueue(notification.Span[0].Acknowledgement);
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                Interlocked.Increment(ref m_keepAliveCount);
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private int m_keepAliveCount;
        }
    }
}
