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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;
using Opc.Ua.Stress.Tests.Channels.Integration;
using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
using ISubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2016

namespace Opc.Ua.Stress.Tests.Channels.Chaos
{
    /// <summary>
    /// Layer-3 TCP chaos tests that verify V2 subscriptions continue producing notifications.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("ManagedSession")]
    [Category("ChaosTCP")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SubscriptionSurvivalChaosTests : IntegrationTestBase
    {
        /// <summary>
        /// L3-A3: subscriptions survive proxy drops through the default recreate path.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [Category("ChaosTCP")]
        [CancelAfter(180_000)]
        public Task L3A3SubscriptionsSurviveProxyDropsWithDefaultRecreateAsync(CancellationToken ct)
        {
            return RunSubscriptionSurvivalAsync(transferSubscriptionsOnRecreate: false, "L3-A3", ct);
        }

        /// <summary>
        /// L3-A3T: subscriptions survive proxy drops with transfer-on-recreate enabled.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [Category("ChaosTCP")]
        [CancelAfter(180_000)]
        public Task L3A3TSubscriptionsSurviveProxyDropsWithTransferOnRecreateAsync(CancellationToken ct)
        {
            return RunSubscriptionSurvivalAsync(transferSubscriptionsOnRecreate: true, "L3-A3T", ct);
        }

        private async Task RunSubscriptionSurvivalAsync(
            bool transferSubscriptionsOnRecreate,
            string scenario,
            CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            TestContext.Out.WriteLine(FormattableString.Invariant($"{scenario} seed={seed}"));

            TcpChaosProxy proxy = await TcpChaosProxy.StartAsync(ServerUrl, telemetry: Telemetry)
                .ConfigureAwait(false);
            await using var proxyDispose = proxy.ConfigureAwait(false);
            ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromSeconds(1),
                    MaxAttempts = 120
                });
            await using var managerDispose = manager.ConfigureAwait(false);

            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None, proxy.LocalUrl)
                .ConfigureAwait(false);
            endpoint.EndpointUrl = proxy.LocalUrl;
            endpoint.Description.Server.DiscoveryUrls = [proxy.LocalUrl.ToString()];
            endpoint.UpdateBeforeConnect = false;

            var sessions = new List<ManagedSessionType>(SessionCount);
            var subscriptions = new List<SubscriptionTracker>(SessionCount * SubscriptionsPerSession);

            try
            {
                await ConnectSessionsAsync(
                    endpoint,
                    manager,
                    sessions,
                    scenario,
                    transferSubscriptionsOnRecreate,
                    ct).ConfigureAwait(false);
                ManagedChannelKey sharedKey = await AssertSingleSharedReadyChannelAsync(sessions, manager, ct)
                    .ConfigureAwait(false);

                await CreateSubscriptionsAsync(sessions, subscriptions, ct).ConfigureAwait(false);
                await AssertSubscriptionsReadyAsync(sessions, subscriptions, ct).ConfigureAwait(false);
                await AssertRecentNotificationsAsync(subscriptions, DefaultWait, ct).ConfigureAwait(false);

                IReadOnlyDictionary<SubscriptionTracker, long> expectedMinimums = await MeasureExpectedMinimumsAsync(
                    subscriptions,
                    ct).ConfigureAwait(false);

                ResetCounters(subscriptions);
                var runner = new StressRunner(
                    [ReadServerCurrentTimeAsync],
                    concurrency: ReadConcurrency,
                    targetOpsPerSecond: ReadTargetOpsPerSecond,
                    telemetry: Telemetry);
                await using var runnerDispose = runner.ConfigureAwait(false);
                await runner.StartAsync(ct).ConfigureAwait(false);

                try
                {
                    ChaosSchedule schedule = CreateDropSchedule(seed);
                    TestContext.Out.WriteLine(FormattableString.Invariant(
                        $"{scenario} scheduled {schedule.Events.Count} drops over {ChaosDuration.TotalSeconds}s"));
                    long started = TimeProvider.System.GetTimestamp();
                    var dispatcher = new ChaosScheduleRunner(
                        schedule,
                        (chaosEvent, workerCt) => DispatchAndValidateChaosEventAsync(
                            chaosEvent,
                            proxy,
                            sessions,
                            subscriptions,
                            manager,
                            sharedKey,
                            workerCt));
                    await using var dispatcherDispose = dispatcher.ConfigureAwait(false);

                    await dispatcher.RunAsync(ct).ConfigureAwait(false);
                    await DelayRemainingChaosWindowAsync(started, ct).ConfigureAwait(false);
                }
                finally
                {
                    await runner.StopAsync().ConfigureAwait(false);
                }

                await WaitForQuiescence.ForManagerAsync(manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);
                await AssertSubscriptionsReadyAsync(sessions, subscriptions, ct).ConfigureAwait(false);
                await AssertRecentNotificationsAsync(subscriptions, RecentNotificationWait, ct).ConfigureAwait(false);
                AssertNotificationCounts(subscriptions, expectedMinimums, scenario, seed);
                Assert.That(
                    runner.TotalOpsAttempted,
                    Is.GreaterThan(0),
                    FormattableString.Invariant($"StressRunner should execute read operations (seed={seed})."));
            }
            finally
            {
                await DisposeSubscriptionsAsync(subscriptions).ConfigureAwait(false);
                await CloseAndDisposeSessionsAsync(sessions).ConfigureAwait(false);
            }

            async Task ReadServerCurrentTimeAsync(CancellationToken workerCt)
            {
                int index = (int)(Interlocked.Increment(ref m_readIndex) % sessions.Count);
                DataValue value = await sessions[index]
                    .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, workerCt)
                    .ConfigureAwait(false);
                if (value.IsNull || StatusCode.IsBad(value.StatusCode))
                {
                    throw new ServiceResultException(value.StatusCode);
                }
            }
        }

        private async Task ConnectSessionsAsync(
            ConfiguredEndpoint endpoint,
            ClientChannelManager manager,
            List<ManagedSessionType> sessions,
            string scenario,
            bool transferSubscriptionsOnRecreate,
            CancellationToken ct)
        {
            for (int index = 0; index < SessionCount; index++)
            {
                sessions.Add(await ConnectV2ManagedSessionAsync(
                    endpoint,
                    manager,
                    FormattableString.Invariant($"{scenario}-{index}"),
                    transferSubscriptionsOnRecreate,
                    ct).ConfigureAwait(false));
            }
        }

        private async Task<ManagedSessionType> ConnectV2ManagedSessionAsync(
            ConfiguredEndpoint endpoint,
            ClientChannelManager manager,
            string sessionName,
            bool transferSubscriptionsOnRecreate,
            CancellationToken ct)
        {
            ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithChannelManager(manager)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                })
                .UseSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);

            if (transferSubscriptionsOnRecreate)
            {
                builder.WithTransferSubscriptionsOnRecreate();
            }

            return await builder.ConnectAsync(ct).ConfigureAwait(false);
        }

        private static async Task<ManagedChannelKey> AssertSingleSharedReadyChannelAsync(
            List<ManagedSessionType> sessions,
            ClientChannelManager manager,
            CancellationToken ct)
        {
            Assert.That(sessions, Has.Count.EqualTo(SessionCount));
            ManagedChannelKey key = GetManagedChannel(sessions[0]).Key;
            Assert.That(
                sessions.Select(session => GetManagedChannel(session).Key),
                Is.All.EqualTo(key),
                "All L3 subscription-survival sessions should share one managed channel.");
            Assert.That(
                await WaitForQuiescence.EntryRefcountReachesAsync(
                    manager,
                    key,
                    SessionCount,
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "The shared proxy endpoint should have one channel-manager entry with all sessions attached.");

            ManagedChannelDiagnostic diagnostic = GetDiagnostic(manager, key);
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.Refcount, Is.EqualTo(SessionCount));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(SessionCount));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
            });
            return key;
        }

        private static async Task CreateSubscriptionsAsync(
            List<ManagedSessionType> sessions,
            List<SubscriptionTracker> subscriptions,
            CancellationToken ct)
        {
            for (int sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
            {
                ManagedSessionType session = sessions[sessionIndex];
                for (int subscriptionIndex = 0; subscriptionIndex < SubscriptionsPerSession; subscriptionIndex++)
                {
                    var counter = new NotificationCounter();
                    ISubscription subscription = session.AddSubscription(
                        counter,
                        new V2SubscriptionOptions
                        {
                            PublishingInterval = SamplingInterval,
                            PublishingEnabled = true,
                            KeepAliveCount = 4,
                            LifetimeCount = 60,
                            Priority = 0,
                            MaxNotificationsPerPublish = 0
                        });

                    bool added = subscription.TryAddMonitoredItem(
                        FormattableString.Invariant($"CurrentTime-{sessionIndex}-{subscriptionIndex}"),
                        new V2MonitoredItemOptions
                        {
                            StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                            SamplingInterval = SamplingInterval,
                            QueueSize = 10,
                            DiscardOldest = true
                        },
                        out IMonitoredItem? monitoredItem);
                    Assert.That(added, Is.True, "The CurrentTime monitored item should be added to the subscription.");
                    Assert.That(monitoredItem, Is.Not.Null);

                    subscriptions.Add(new SubscriptionTracker(
                        sessionIndex,
                        subscriptionIndex,
                        subscription,
                        monitoredItem!,
                        counter));
                }
            }

            Assert.That(
                await WaitForAsync(
                    () => subscriptions.All(static tracker =>
                        tracker.Subscription.Created && tracker.MonitoredItem.Created),
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "All V2 subscriptions and monitored items should be created on the server.");
        }

        private static Task AssertSubscriptionsReadyAsync(
            List<ManagedSessionType> sessions,
            List<SubscriptionTracker> subscriptions,
            CancellationToken ct)
        {
            return AssertSubscriptionsReadyAsync(sessions, subscriptions, DefaultWait, ct);
        }

        private static async Task AssertSubscriptionsReadyAsync(
            List<ManagedSessionType> sessions,
            List<SubscriptionTracker> subscriptions,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Assert.That(
                await WaitForAsync(
                    () => sessions.All(static session =>
                        session.RequireSubscriptionManager().Count == SubscriptionsPerSession) &&
                        subscriptions.All(static tracker =>
                            tracker.Subscription.Created && tracker.MonitoredItem.Created),
                    timeout,
                    ct).ConfigureAwait(false),
                Is.True,
                "Every session should still expose its two created V2 subscriptions and monitored items.");
        }

        private static async Task<IReadOnlyDictionary<SubscriptionTracker, long>> MeasureExpectedMinimumsAsync(
            List<SubscriptionTracker> subscriptions,
            CancellationToken ct)
        {
            ResetCounters(subscriptions);
            await Task.Delay(BaselineDuration, ct).ConfigureAwait(false);

            var expectedMinimums = new Dictionary<SubscriptionTracker, long>(subscriptions.Count);
            foreach (SubscriptionTracker tracker in subscriptions)
            {
                long baselineCount = tracker.Counter.Count;
                Assert.That(
                    baselineCount,
                    Is.GreaterThan(0),
                    $"Subscription {tracker.DisplayName} should receive baseline notifications before chaos.");
                expectedMinimums[tracker] = Math.Max(
                    1,
                    baselineCount * ChaosDuration.Ticks / BaselineDuration.Ticks / 2);
            }

            return expectedMinimums;
        }

        private static async Task DispatchAndValidateChaosEventAsync(
            ChaosEvent chaosEvent,
            TcpChaosProxy proxy,
            List<ManagedSessionType> sessions,
            List<SubscriptionTracker> subscriptions,
            ClientChannelManager manager,
            ManagedChannelKey sharedKey,
            CancellationToken ct)
        {
            if (chaosEvent.Kind != ChaosEventKind.DropAllConnections)
            {
                return;
            }

            await proxy.DropAllConnectionsAsync().ConfigureAwait(false);
            await Task.Delay(SettleAfterChaos, ct).ConfigureAwait(false);
            Assert.That(
                await WaitForChannelStateAsync(
                    manager,
                    sharedKey,
                    ChannelState.Ready,
                    RecentNotificationWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "The shared managed channel should return to Ready after a proxy drop.");
            await AssertSubscriptionsReadyAsync(sessions, subscriptions, RecentNotificationWait, ct)
                .ConfigureAwait(false);
            await AssertRecentNotificationsAsync(subscriptions, RecentNotificationWait, ct)
                .ConfigureAwait(false);
        }

        private static async Task AssertRecentNotificationsAsync(
            List<SubscriptionTracker> subscriptions,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Assert.That(
                await WaitForAsync(
                    () => subscriptions.All(static tracker => tracker.Counter.IsRecent(RecentNotificationWindow)),
                    timeout,
                    ct).ConfigureAwait(false),
                Is.True,
                "Every subscription should receive a recent value-change notification.");
        }

        private static void AssertNotificationCounts(
            List<SubscriptionTracker> subscriptions,
            IReadOnlyDictionary<SubscriptionTracker, long> expectedMinimums,
            string scenario,
            int seed)
        {
            foreach (SubscriptionTracker tracker in subscriptions)
            {
                Assert.That(
                    tracker.Counter.Count,
                    Is.GreaterThanOrEqualTo(expectedMinimums[tracker]),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} subscription {1} should receive at least half of its measured chaos-free " +
                        "baseline (seed={2}).",
                        scenario,
                        tracker.DisplayName,
                        seed));
            }
        }

        private static async Task DelayRemainingChaosWindowAsync(long started, CancellationToken ct)
        {
            TimeSpan elapsed = TimeProvider.System.GetElapsedTime(started);
            TimeSpan remaining = ChaosDuration - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, ct).ConfigureAwait(false);
            }
        }

        private static ChaosSchedule CreateDropSchedule(int seed)
        {
            return new ChaosSchedule(
                seed,
                ChaosDuration,
                [ChaosEventKind.DropAllConnections],
                minInterval: MinDropInterval,
                maxInterval: MaxDropInterval);
        }

        private static void ResetCounters(List<SubscriptionTracker> subscriptions)
        {
            foreach (SubscriptionTracker tracker in subscriptions)
            {
                tracker.Counter.Reset();
            }
        }

        private static async Task DisposeSubscriptionsAsync(List<SubscriptionTracker> subscriptions)
        {
            for (int index = subscriptions.Count - 1; index >= 0; index--)
            {
                try
                {
                    await subscriptions[index].Subscription.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup after transport-fault tests.
                }
            }
        }

        private static async Task CloseAndDisposeSessionsAsync(List<ManagedSessionType> sessions)
        {
            for (int index = sessions.Count - 1; index >= 0; index--)
            {
                await CloseAndDisposeAsync(sessions[index]).ConfigureAwait(false);
            }
        }

        private const string SeedEnvironmentVariableName = "OPCUA_CHAOS_SEED";
        private const int SessionCount = 10;
        private const int SubscriptionsPerSession = 2;
        private const int ReadConcurrency = 8;
        private const int ReadTargetOpsPerSecond = 100;
        private static readonly TimeSpan SamplingInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan BaselineDuration = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ChaosDuration = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan MinDropInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MaxDropInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SettleAfterChaos = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RecentNotificationWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RecentNotificationWait = TimeSpan.FromSeconds(2);
        private long m_readIndex;

        private sealed record SubscriptionTracker(
            int SessionIndex,
            int SubscriptionIndex,
            ISubscription Subscription,
            IMonitoredItem MonitoredItem,
            NotificationCounter Counter)
        {
            public string DisplayName => FormattableString.Invariant($"S{SessionIndex}/Sub{SubscriptionIndex}");
        }

        private sealed class NotificationCounter : Opc.Ua.Client.Subscriptions.ISubscriptionNotificationHandler
        {
            public long Count => Volatile.Read(ref m_count);

            public void Reset()
            {
                Interlocked.Exchange(ref m_count, 0);
                Interlocked.Exchange(ref m_lastNotificationTicks, 0);
            }

            public bool IsRecent(TimeSpan window)
            {
                long ticks = Volatile.Read(ref m_lastNotificationTicks);
                return ticks > 0 && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) <= window;
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Opc.Ua.Client.Subscriptions.DataValueChange> notification,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                int goodNotifications = 0;
                ReadOnlySpan<Opc.Ua.Client.Subscriptions.DataValueChange> span = notification.Span;
                for (int index = 0; index < span.Length; index++)
                {
                    Opc.Ua.Client.Subscriptions.DataValueChange change = span[index];
                    if (!change.Value.IsNull && StatusCode.IsGood(change.Value.StatusCode))
                    {
                        goodNotifications++;
                    }
                }

                if (goodNotifications > 0)
                {
                    Interlocked.Add(ref m_count, goodNotifications);
                    Interlocked.Exchange(ref m_lastNotificationTicks, DateTime.UtcNow.Ticks);
                }

                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Opc.Ua.Client.Subscriptions.EventNotification> notification,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Opc.Ua.Client.Subscriptions.SubscriptionState state,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private long m_count;
            private long m_lastNotificationTicks;
        }

        private static class TestRunSeed
        {
            public static int Get()
            {
                string? value = Environment.GetEnvironmentVariable(SeedEnvironmentVariableName);
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                {
                    return seed;
                }

                return Environment.TickCount;
            }
        }
    }
}
