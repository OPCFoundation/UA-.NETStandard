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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;
using Opc.Ua.Stress.Tests.Channels.Integration;
using ClassicMonitoredItem = Opc.Ua.Client.MonitoredItem;
using ClassicSubscription = Opc.Ua.Client.Subscription;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;
using V2DataValueChange = Opc.Ua.Client.Subscriptions.DataValueChange;
using V2EventNotification = Opc.Ua.Client.Subscriptions.EventNotification;
using V2IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
using V2ISubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2PublishState = Opc.Ua.Client.Subscriptions.PublishState;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using V2SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

// CA2000: test subscription and monitored-item disposables are owned by MatrixSubscription cleanup.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
// TODO: replace these suppressions with analyzer-recognized ownership helpers when available.
#pragma warning disable CA2000, CA2016

namespace Opc.Ua.Stress.Tests.Channels.Soak
{
    /// <summary>
    /// Subscription engine variants covered by the L4 combinatorial soak matrix.
    /// </summary>
    public enum SubscriptionEngineVariant
    {
        /// <summary>
        /// The classic V1 publish engine.
        /// </summary>
        Classic,

        /// <summary>
        /// The channel-based V2 publish engine.
        /// </summary>
        V2
    }

    /// <summary>
    /// Layer-4 pairwise soak matrix for shared channel-manager reconnect and subscription permutations.
    /// </summary>
    [TestFixture]
    [Category("Soak")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CombinatorialMatrixTests : IntegrationTestBase
    {
        /// <summary>
        /// L4-SOAK2: runs one short chaos cell from the long-running combinatorial soak matrix.
        /// </summary>
        /// <param name="engine">The subscription engine variant.</param>
        /// <param name="transferOnRecreate">Whether V2 should try transfer before recreating subscriptions.</param>
        /// <param name="sessionCount">The number of sessions sharing the channel.</param>
        /// <param name="subscribe">Whether each session should create a monitored CurrentTime subscription.</param>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [Category("Soak")]
        [TestCase(SubscriptionEngineVariant.Classic, false, 1, false)]
        [TestCase(SubscriptionEngineVariant.Classic, false, 1, true)]
        [TestCase(SubscriptionEngineVariant.Classic, false, 5, false)]
        [TestCase(SubscriptionEngineVariant.Classic, false, 5, true)]
        [TestCase(SubscriptionEngineVariant.Classic, true, 5, true)]
        [TestCase(SubscriptionEngineVariant.V2, false, 1, false)]
        [TestCase(SubscriptionEngineVariant.V2, false, 1, true)]
        [TestCase(SubscriptionEngineVariant.V2, false, 5, false)]
        [TestCase(SubscriptionEngineVariant.V2, false, 5, true)]
        [TestCase(SubscriptionEngineVariant.V2, true, 1, false)]
        [TestCase(SubscriptionEngineVariant.V2, true, 1, true)]
        [TestCase(SubscriptionEngineVariant.V2, true, 5, true)]
        [CancelAfter(300_000)]
        public async Task CombinatorialMatrixAsync(
            SubscriptionEngineVariant engine,
            bool transferOnRecreate,
            int sessionCount,
            bool subscribe,
            CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            string cellName = CreateCellName(engine, transferOnRecreate, sessionCount, subscribe);
            TestContext.Out.WriteLine(FormattableString.Invariant($"L4-SOAK2 seed={seed} cell={cellName}"));

            TcpChaosProxy proxy = await TcpChaosProxy.StartAsync(ServerUrl, telemetry: Telemetry)
                .ConfigureAwait(false);

            await using ConfiguredAsyncDisposable proxyAsyncDisposable = proxy.ConfigureAwait(false);
            ClientChannelManager manager = CreateChannelManager(CreateMatrixReconnectPolicy());
            await using ConfiguredAsyncDisposable managerAsyncDisposable = manager.ConfigureAwait(false);
            using MetricsCollector collector = new();

            ConfiguredEndpoint endpoint = await GetProxyEndpointAsync(proxy.LocalUrl).ConfigureAwait(false);
            var sessions = new List<ManagedSessionType>(sessionCount);
            var subscriptions = new List<MatrixSubscription>(subscribe ? sessionCount : 0);
            StressRunner? runner = null;

            try
            {
                await ConnectSessionsAsync(
                    endpoint,
                    manager,
                    sessions,
                    engine,
                    transferOnRecreate,
                    cellName,
                    sessionCount,
                    ct).ConfigureAwait(false);
                await AssertSingleSharedReadyChannelAsync(sessions, manager, sessionCount, ct)
                    .ConfigureAwait(false);

                if (subscribe)
                {
                    await CreateSubscriptionsAsync(engine, sessions, subscriptions, ct).ConfigureAwait(false);
                }

                runner = new StressRunner(
                    CreateReadOperations(sessions),
                    concurrency: GetReadConcurrency(sessionCount),
                    targetOpsPerSecond: TargetOpsPerSecond,
                    telemetry: Telemetry);
                await runner.StartAsync(ct).ConfigureAwait(false);

                ChaosSchedule schedule = CreateMatrixSchedule(seed);
                TestContext.Out.WriteLine(FormattableString.Invariant(
                    $"L4-SOAK2 cell={cellName} scheduled {schedule.Events.Count} events over {schedule.TotalDuration}."));

                var dispatcher = new ChaosScheduleRunner(
                    schedule,
                    (chaosEvent, workerCt) => DispatchChaosEventAsync(
                        chaosEvent,
                        proxy,
                        manager,
                        seed,
                        cellName,
                        workerCt));

                await using ConfiguredAsyncDisposable dispatcherAsyncDisposable = dispatcher.ConfigureAwait(false);

                try
                {
                    await RunScheduleForDurationAsync(dispatcher, schedule, ct).ConfigureAwait(false);
                }
                finally
                {
                    await runner.StopAsync().ConfigureAwait(false);
                }

                await WaitForQuiescence.ForManagerAsync(manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);
                await AssertSessionsCanReadAsync(sessions, ct).ConfigureAwait(false);
                if (subscribe)
                {
                    AssertSubscriptionsObservedNotifications(subscriptions, cellName, seed);
                }

                collector.RecordObservableInstruments();
                string summary = CreateSummary(seed, cellName, runner, collector, subscriptions);
                TestContext.Out.WriteLine(summary);

                Assert.Multiple(() =>
                {
                    Assert.That(runner.TotalOpsAttempted, Is.GreaterThan(0), summary);
                    Assert.That(runner.FailureRate, Is.LessThan(MaxFailureRate), summary);
                });
            }
            finally
            {
                if (runner != null)
                {
                    await runner.DisposeAsync().ConfigureAwait(false);
                }

                await DisposeSubscriptionsAsync(subscriptions).ConfigureAwait(false);
                await CloseAndDisposeSessionsAsync(sessions).ConfigureAwait(false);
            }
        }

        private static string CreateCellName(
            SubscriptionEngineVariant engine,
            bool transferOnRecreate,
            int sessionCount,
            bool subscribe)
        {
            return FormattableString.Invariant(
                $"engine={engine},transfer={transferOnRecreate},sessions={sessionCount},subscribe={subscribe}");
        }

        private async Task DispatchChaosEventAsync(
            ChaosEvent chaosEvent,
            TcpChaosProxy proxy,
            ClientChannelManager manager,
            int seed,
            string cellName,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"{Timestamp()} seed={seed} cell={cellName} t={chaosEvent.At} kind={chaosEvent.Kind}"));

            switch (chaosEvent.Kind)
            {
                case ChaosEventKind.DropAllConnections:
                    await proxy.DropAllConnectionsAsync().ConfigureAwait(false);
                    break;
                case ChaosEventKind.BlockAccept:
                    await proxy.BlockAcceptAsync(BlockAcceptDuration).ConfigureAwait(false);
                    break;
                case ChaosEventKind.ReconnectAllAsync:
                    await manager.ReconnectAllAsync(ct).ConfigureAwait(false);
                    break;
            }
        }

        private async Task<ConfiguredEndpoint> GetProxyEndpointAsync(Uri proxyUrl)
        {
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None, proxyUrl)
                .ConfigureAwait(false);
            endpoint.EndpointUrl = proxyUrl;
            endpoint.Description.Server.DiscoveryUrls = [proxyUrl.ToString()];
            endpoint.UpdateBeforeConnect = false;
            return endpoint;
        }

        private async Task ConnectSessionsAsync(
            ConfiguredEndpoint endpoint,
            ClientChannelManager manager,
            List<ManagedSessionType> sessions,
            SubscriptionEngineVariant engine,
            bool transferOnRecreate,
            string cellName,
            int sessionCount,
            CancellationToken ct)
        {
            for (int index = 0; index < sessionCount; index++)
            {
                sessions.Add(await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    FormattableString.Invariant($"L4-SOAK2-{cellName}-{index}"),
                    engine,
                    transferOnRecreate,
                    ct).ConfigureAwait(false));
            }
        }

        private async Task<ManagedSessionType> ConnectManagedSessionAsync(
            ConfiguredEndpoint endpoint,
            ClientChannelManager manager,
            string sessionName,
            SubscriptionEngineVariant engine,
            bool transferOnRecreate,
            CancellationToken ct)
        {
            ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithChannelManager(manager)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(policy => policy with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                })
                .UseSubscriptionEngine(GetSubscriptionEngineFactory(engine));

            if (transferOnRecreate)
            {
                builder.WithTransferSubscriptionsOnRecreate();
            }

            return await builder.ConnectAsync(ct).ConfigureAwait(false);
        }

        private static ISubscriptionEngineFactory GetSubscriptionEngineFactory(SubscriptionEngineVariant engine)
        {
            return engine switch
            {
                SubscriptionEngineVariant.Classic => ClassicSubscriptionEngineFactory.Instance,
                SubscriptionEngineVariant.V2 => DefaultSubscriptionEngineFactory.Instance,
                _ => throw new ArgumentOutOfRangeException(nameof(engine))
            };
        }

        private static ExponentialBackoffChannelReconnectPolicy CreateMatrixReconnectPolicy()
        {
            return new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(2),
                MaxAttempts = 20
            };
        }

        private static ChaosSchedule CreateMatrixSchedule(int seed)
        {
            return new ChaosSchedule(
                seed,
                ChaosDuration,
                [
                    ChaosEventKind.DropAllConnections,
                    ChaosEventKind.BlockAccept,
                    ChaosEventKind.ReconnectAllAsync
                ],
                minInterval: TimeSpan.FromSeconds(3),
                maxInterval: TimeSpan.FromSeconds(8));
        }

        private static int GetReadConcurrency(int sessionCount)
        {
            return Math.Clamp(sessionCount * 2, 2, ReadConcurrency);
        }

        private static Func<CancellationToken, Task>[] CreateReadOperations(
            List<ManagedSessionType> sessions)
        {
            var operations = new Func<CancellationToken, Task>[sessions.Count];
            for (int index = 0; index < sessions.Count; index++)
            {
                ManagedSessionType session = sessions[index];
                operations[index] = workerCt => ReadServerCurrentTimeAsync(session, workerCt);
            }

            return operations;
        }

        private static async Task ReadServerCurrentTimeAsync(
            ManagedSessionType session,
            CancellationToken ct)
        {
            DataValue value = await session
                .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, ct)
                .ConfigureAwait(false);
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.IsNull ? StatusCodes.Bad : value.StatusCode);
            }
        }

        private static async Task AssertSingleSharedReadyChannelAsync(
            List<ManagedSessionType> sessions,
            ClientChannelManager manager,
            int expectedSessionCount,
            CancellationToken ct)
        {
            Assert.That(sessions, Has.Count.EqualTo(expectedSessionCount));
            ManagedChannelKey key = GetManagedChannel(sessions[0]).Key;
            Assert.That(
                sessions.Select(session => GetManagedChannel(session).Key),
                Is.All.EqualTo(key),
                "All L4 matrix sessions should share the same managed channel.");
            Assert.That(
                await WaitForQuiescence.EntryRefcountReachesAsync(
                    manager,
                    key,
                    expectedSessionCount,
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "The shared channel should have one reference per matrix session.");
        }

        private static async Task AssertSessionsCanReadAsync(
            List<ManagedSessionType> sessions,
            CancellationToken ct)
        {
            foreach (ManagedSessionType session in sessions)
            {
                await ReadServerCurrentTimeAsync(session, ct).ConfigureAwait(false);
            }
        }

        private static async Task CreateSubscriptionsAsync(
            SubscriptionEngineVariant engine,
            List<ManagedSessionType> sessions,
            List<MatrixSubscription> subscriptions,
            CancellationToken ct)
        {
            for (int index = 0; index < sessions.Count; index++)
            {
                if (engine == SubscriptionEngineVariant.V2)
                {
                    await CreateV2SubscriptionAsync(sessions[index], subscriptions, index, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await CreateClassicSubscriptionAsync(sessions[index], subscriptions, index, ct)
                        .ConfigureAwait(false);
                }
            }
        }

        private static async Task CreateClassicSubscriptionAsync(
            ManagedSessionType session,
            List<MatrixSubscription> subscriptions,
            int sessionIndex,
            CancellationToken ct)
        {
            var counter = new NotificationCounter();
            var subscription = new ClassicSubscription(session.DefaultSubscription)
            {
                DisplayName = FormattableString.Invariant($"L4-SOAK2-classic-{sessionIndex}"),
                PublishingEnabled = true,
                PublishingInterval = (int)SamplingInterval.TotalMilliseconds,
                LifetimeCount = 60,
                MinLifetimeInterval = 3,
                KeepAliveCount = 4
            };

            bool added = session.AddSubscription(subscription);
            Assert.That(added, Is.True, "The classic subscription should be added to the session.");
            await subscription.CreateAsync(ct).ConfigureAwait(false);

            var monitoredItem = new ClassicMonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = FormattableString.Invariant($"CurrentTime-classic-{sessionIndex}"),
                SamplingInterval = (int)SamplingInterval.TotalMilliseconds,
                QueueSize = QueueSize,
                DiscardOldest = true
            };
            monitoredItem.Notification += (_, _) => counter.Increment();
            subscription.AddItem(monitoredItem);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

            subscriptions.Add(MatrixSubscription.FromClassic(
                FormattableString.Invariant($"classic-{sessionIndex}"),
                subscription,
                counter));
            Assert.That(subscription.Created, Is.True, "The classic subscription should be created on the server.");
            Assert.That(subscription.MonitoredItemCount, Is.EqualTo(1));
        }

        private static async Task CreateV2SubscriptionAsync(
            ManagedSessionType session,
            List<MatrixSubscription> subscriptions,
            int sessionIndex,
            CancellationToken ct)
        {
            var counter = new NotificationCounter();
            V2ISubscription subscription = session.AddSubscription(
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
                FormattableString.Invariant($"CurrentTime-v2-{sessionIndex}"),
                new V2MonitoredItemOptions
                {
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    SamplingInterval = SamplingInterval,
                    QueueSize = QueueSize,
                    DiscardOldest = true
                },
                out V2IMonitoredItem? monitoredItem);
            Assert.That(added, Is.True, "The V2 monitored item should be added to the subscription.");
            Assert.That(monitoredItem, Is.Not.Null);

            subscriptions.Add(MatrixSubscription.FromV2(
                FormattableString.Invariant($"v2-{sessionIndex}"),
                subscription,
                counter));
            Assert.That(
                await WaitForAsync(
                    () => subscription.Created && monitoredItem!.Created,
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "The V2 subscription and monitored item should be created on the server.");
        }

        private static void AssertSubscriptionsObservedNotifications(
            List<MatrixSubscription> subscriptions,
            string cellName,
            int seed)
        {
            foreach (MatrixSubscription subscription in subscriptions)
            {
                Assert.That(
                    subscription.NotificationCount,
                    Is.GreaterThan(0),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Subscription {0} should receive notifications for {1} (seed={2}).",
                        subscription.DisplayName,
                        cellName,
                        seed));
            }
        }

        private static async Task RunScheduleForDurationAsync(
            ChaosScheduleRunner dispatcher,
            ChaosSchedule schedule,
            CancellationToken ct)
        {
            TimeProvider timeProvider = TimeProvider.System;
            long started = timeProvider.GetTimestamp();
            await dispatcher.RunAsync(ct).ConfigureAwait(false);

            TimeSpan remaining = schedule.TotalDuration - timeProvider.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, timeProvider, ct).ConfigureAwait(false);
            }
        }

        private static async Task DisposeSubscriptionsAsync(List<MatrixSubscription> subscriptions)
        {
            for (int index = subscriptions.Count - 1; index >= 0; index--)
            {
                await subscriptions[index].DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task CloseAndDisposeSessionsAsync(List<ManagedSessionType> sessions)
        {
            for (int index = sessions.Count - 1; index >= 0; index--)
            {
                await CloseAndDisposeAsync(sessions[index]).ConfigureAwait(false);
            }
        }

        private static string CreateSummary(
            int seed,
            string cellName,
            StressRunner runner,
            MetricsCollector collector,
            List<MatrixSubscription> subscriptions)
        {
            (TimeSpan p50, TimeSpan p95, TimeSpan p99) = runner.LatencyPercentiles;
            long notificationCount = subscriptions.Sum(static subscription => subscription.NotificationCount);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Seed={0} Cell={1} Ops attempted={2} succeeded={3} failed={4} failure-rate={5:P} " +
                "p50={6} p95={7} p99={8} notifications={9} ReconnectStarted={10} " +
                "ReconnectCompleted={11} ReconnectFailed={12}",
                seed,
                cellName,
                runner.TotalOpsAttempted,
                runner.TotalOpsSucceeded,
                runner.TotalOpsFailed,
                runner.FailureRate,
                p50,
                p95,
                p99,
                notificationCount,
                collector.CountEvents("ReconnectStarted"),
                collector.CountEvents("ReconnectCompleted"),
                collector.CountEvents("ReconnectFailed"));
        }

        private static string Timestamp()
        {
            return TimeProvider.System.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        }

        private const string SeedParameterName = "Seed";
        private const string SeedEnvironmentVariableName = "OPCUA_CHAOS_SEED";
        private const int ReadConcurrency = 8;
        private const int TargetOpsPerSecond = 20;
        private const int QueueSize = 10;
        private const double MaxFailureRate = 0.15;
        private static readonly TimeSpan ChaosDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BlockAcceptDuration = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan SamplingInterval = TimeSpan.FromMilliseconds(250);

        private sealed class MatrixSubscription : IAsyncDisposable
        {
            private MatrixSubscription(
                string displayName,
                ClassicSubscription? classicSubscription,
                V2ISubscription? v2Subscription,
                NotificationCounter counter)
            {
                DisplayName = displayName;
                m_classicSubscription = classicSubscription;
                m_v2Subscription = v2Subscription;
                m_counter = counter;
            }

            public string DisplayName { get; }

            public long NotificationCount => m_counter.Count;

            public static MatrixSubscription FromClassic(
                string displayName,
                ClassicSubscription subscription,
                NotificationCounter counter)
            {
                return new MatrixSubscription(displayName, subscription, null, counter);
            }

            public static MatrixSubscription FromV2(
                string displayName,
                V2ISubscription subscription,
                NotificationCounter counter)
            {
                return new MatrixSubscription(displayName, null, subscription, counter);
            }

            public async ValueTask DisposeAsync()
            {
                if (m_v2Subscription != null)
                {
                    try
                    {
                        await m_v2Subscription.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort cleanup after transport-fault tests.
                    }
                }

                if (m_classicSubscription != null)
                {
                    try
                    {
                        await m_classicSubscription.DeleteAsync(silent: true, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort cleanup after transport-fault tests.
                    }

                    m_classicSubscription.Dispose();
                }

                GC.SuppressFinalize(this);
            }

            private readonly ClassicSubscription? m_classicSubscription;
            private readonly V2ISubscription? m_v2Subscription;
            private readonly NotificationCounter m_counter;
        }

        private sealed class NotificationCounter : Client.Subscriptions.ISubscriptionNotificationHandler
        {
            public long Count => Volatile.Read(ref m_count);

            public void Increment()
            {
                Interlocked.Increment(ref m_count);
            }

            public ValueTask OnDataChangeNotificationAsync(
                V2ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<V2DataValueChange> notification,
                V2PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<V2DataValueChange> span = notification.Span;
                int goodNotificationCount = 0;
                for (int index = 0; index < span.Length; index++)
                {
                    V2DataValueChange change = span[index];
                    if (!change.Value.IsNull && StatusCode.IsGood(change.Value.StatusCode))
                    {
                        goodNotificationCount++;
                    }
                }

                if (goodNotificationCount > 0)
                {
                    Interlocked.Add(ref m_count, goodNotificationCount);
                }

                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                V2ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<V2EventNotification> notification,
                V2PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                V2ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                V2PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                V2ISubscription subscription,
                V2SubscriptionState state,
                V2PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private long m_count;
        }

        private static class TestRunSeed
        {
            public static int Get()
            {
                string? configured = TestContext.Parameters[SeedParameterName];
                if (TryParseSeed(configured, out int seed))
                {
                    return seed;
                }

                configured = Environment.GetEnvironmentVariable(SeedEnvironmentVariableName);
                if (TryParseSeed(configured, out seed))
                {
                    return seed;
                }

                return RandomNumberGenerator.GetInt32(int.MaxValue);
            }

            private static bool TryParseSeed(string? value, out int seed)
            {
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
            }
        }
    }
}
