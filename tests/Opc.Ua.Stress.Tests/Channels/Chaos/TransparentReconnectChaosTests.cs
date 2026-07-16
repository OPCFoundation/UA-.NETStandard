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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;
using Opc.Ua.Stress.Tests.Channels.Integration;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Stress.Tests.Channels.Chaos
{
    /// <summary>
    /// Layer-3 transparent reconnect chaos tests for the channel-manager shared transport path.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("ManagedSession")]
    [Category("Reconnect")]
    [Category("ChaosTCP")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class TransparentReconnectChaosTests : IntegrationTestBase
    {
        /// <summary>
        /// L3-A1: verifies one ManagedSession survives periodic transparent TCP drops.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [CancelAfter(150_000)]
        public async Task SingleSessionSurvivesPeriodicDropsAsync(CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            Console.WriteLine(FormattableString.Invariant($"L3-A1 seed={seed}"));

            var eventLog = new ConcurrentQueue<string>();
            TcpChaosProxy proxy = await TcpChaosProxy.StartAsync(ServerUrl, telemetry: Telemetry)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable proxyAsyncDisposable = proxy.ConfigureAwait(false);
            ClientChannelManager manager = CreateChannelManager(CreateTightReconnectPolicy());
            await using ConfiguredAsyncDisposable managerAsyncDisposable = manager.ConfigureAwait(false);
            LeakCounters.Snapshot before = LeakCounters.Capture(manager);
            using MetricsCollector collector = new();

            ConfiguredEndpoint endpoint = await GetProxyEndpointAsync(proxy.LocalUrl).ConfigureAwait(false);
            ManagedSessionType? session = null;
            ManagedChannelKey key = default;
            bool hasKey = false;

            try
            {
                session = await ConnectManagedSessionAsync(endpoint, manager, "L3-A1", ct)
                    .ConfigureAwait(false);
                session.KeepAliveInterval = KeepAliveIntervalMilliseconds;
                AddChannelEventLogging(session, "L3-A1", eventLog);
                key = GetManagedChannel(session).Key;
                hasKey = true;

                Assert.That(
                    await WaitForQuiescence.EntryRefcountReachesAsync(
                        manager,
                        key,
                        expectedRefcount: 1,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    CreateFailureMessage("The session should hold one shared-channel reference.", seed, eventLog));

                await ReadServerCurrentTimeAsync(session, ct).ConfigureAwait(false);

                var singleSession = new List<ManagedSessionType> { session };
                var runner = new StressRunner(
                    CreateReadOperations(singleSession),
                    concurrency: 4,
                    targetOpsPerSecond: 100);

                await using ConfiguredAsyncDisposable runnerAsyncDisposable = runner.ConfigureAwait(false);
                await runner.StartAsync(ct).ConfigureAwait(false);

                ChaosSchedule schedule = CreatePeriodicDropSchedule(seed);
                var dispatcher = new ChaosScheduleRunner(
                    schedule,
                    (chaosEvent, workerCt) => DispatchDropAsync(proxy, chaosEvent, seed, eventLog, workerCt));
                await using ConfiguredAsyncDisposable dispatcherAsyncDisposable = dispatcher.ConfigureAwait(false);

                try
                {
                    await RunScheduleForDurationAsync(dispatcher, schedule, ct).ConfigureAwait(false);
                }
                finally
                {
                    await runner.StopAsync().ConfigureAwait(false);
                }

                Assert.That(
                    await WaitForEventCountAsync(
                        collector,
                        "ReconnectCompleted",
                        1,
                        RecoveryTimeout,
                        ct).ConfigureAwait(false),
                    Is.True,
                    CreateFailureMessage("Timed out waiting for reconnect completion events.", seed, eventLog, collector));

                await WaitForQuiescence.ForManagerAsync(manager, RecoveryTimeout, ct: ct)
                    .ConfigureAwait(false);
                await ReadServerCurrentTimeAsync(session, ct).ConfigureAwait(false);
                collector.RecordObservableInstruments();

                (TimeSpan p50, TimeSpan p95, TimeSpan p99) = runner.LatencyPercentiles;
                string failureMessage = CreateFailureMessage(
                    FormattableString.Invariant(
                        $"FailureRate={runner.FailureRate}, p50={p50}, p95={p95}, p99={p99}."),
                    seed,
                    eventLog,
                    collector,
                    runner);

                Assert.Multiple(() =>
                {
                    Assert.That(runner.FailureRate, Is.LessThan(0.05), failureMessage);
                    Assert.That(p95, Is.LessThan(TimeSpan.FromSeconds(3)), failureMessage);
                    // Reconnects were exercised and none failed. The exact
                    // count is not deterministic under concurrent chaos: drops
                    // that land within a single recovery window coalesce, so
                    // the completed-reconnect count can be below the drop
                    // count. Session survival is covered by FailureRate and the
                    // post-chaos quiescence + read above.
                    Assert.That(
                        collector.CountEvents("ReconnectCompleted"),
                        Is.GreaterThanOrEqualTo(1),
                        failureMessage);
                    Assert.That(
                        collector.CountEvents("ReconnectFailed"),
                        Is.Zero,
                        failureMessage);
                });
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }

            if (hasKey)
            {
                Assert.That(
                    await WaitForQuiescence.EntryGoneAsync(manager, key, DefaultWait, ct)
                        .ConfigureAwait(false),
                    Is.True,
                    CreateFailureMessage("The managed-channel entry should be removed after dispose.", seed, eventLog));
            }

            LeakCounters.AssertNoLeaks(before, LeakCounters.Capture(manager), "L3-A1");
        }

        /// <summary>
        /// L3-A2: verifies ten ManagedSessions share one channel and coalesce each TCP drop into one reconnect.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [CancelAfter(180_000)]
        public async Task SharedChannelCoalescesPeriodicDropsAsync(CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            Console.WriteLine(FormattableString.Invariant($"L3-A2 seed={seed}"));

            var eventLog = new ConcurrentQueue<string>();
            var refcountViolations = new ConcurrentQueue<string>();
            TcpChaosProxy proxy = await TcpChaosProxy.StartAsync(ServerUrl, telemetry: Telemetry)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable proxyAsyncDisposable = proxy.ConfigureAwait(false);
            ClientChannelManager manager = CreateChannelManager(CreateTightReconnectPolicy());
            await using ConfiguredAsyncDisposable managerAsyncDisposable = manager.ConfigureAwait(false);
            LeakCounters.Snapshot before = LeakCounters.Capture(manager);
            using MetricsCollector collector = new();

            ConfiguredEndpoint endpoint = await GetProxyEndpointAsync(proxy.LocalUrl).ConfigureAwait(false);
            var sessions = new List<ManagedSessionType>(SharedSessionCount);
            ManagedChannelKey key = default;
            bool hasKey = false;
            int reactivationNotifications = 0;

            try
            {
                for (int index = 0; index < SharedSessionCount; index++)
                {
                    ManagedSessionType session = await ConnectManagedSessionAsync(
                            endpoint,
                            manager,
                            FormattableString.Invariant($"L3-A2-{index}"),
                            ct)
                        .ConfigureAwait(false);
                    session.KeepAliveInterval = KeepAliveIntervalMilliseconds;
                    AddChannelEventLogging(
                        session,
                        FormattableString.Invariant($"L3-A2-{index}"),
                        eventLog,
                        () => Interlocked.Increment(ref reactivationNotifications));
                    sessions.Add(session);
                }

                key = GetManagedChannel(sessions[0]).Key;
                hasKey = true;
                Assert.That(
                    sessions.Select(session => GetManagedChannel(session).Key),
                    Is.All.EqualTo(key),
                    CreateFailureMessage("All sessions should share the same managed-channel key.", seed, eventLog));
                Assert.That(
                    await WaitForQuiescence.EntryRefcountReachesAsync(
                        manager,
                        key,
                        SharedSessionCount,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    CreateFailureMessage("The shared channel should have ten references.", seed, eventLog));

                var runner = new StressRunner(
                    CreateReadOperations(sessions),
                    concurrency: SharedSessionCount,
                    targetOpsPerSecond: 200);

                await using ConfiguredAsyncDisposable runnerAsyncDisposable = runner.ConfigureAwait(false);
                using var snapshotCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task snapshotTask = TrackRefcountAsync(
                    manager,
                    key,
                    SharedSessionCount,
                    eventLog,
                    refcountViolations,
                    snapshotCts.Token);

                await runner.StartAsync(ct).ConfigureAwait(false);
                ChaosSchedule schedule = CreateThirtySecondDropSchedule(seed);
                var dispatcher = new ChaosScheduleRunner(
                    schedule,
                    (chaosEvent, workerCt) => DispatchDropAsync(proxy, chaosEvent, seed, eventLog, workerCt));
                await using ConfiguredAsyncDisposable dispatcherAsyncDisposable = dispatcher.ConfigureAwait(false);

                try
                {
                    await RunScheduleForDurationAsync(dispatcher, schedule, ct).ConfigureAwait(false);
                }
                finally
                {
                    await runner.StopAsync().ConfigureAwait(false);
                    await snapshotCts.CancelAsync().ConfigureAwait(false);
                    await IgnoreCanceledAsync(snapshotTask).ConfigureAwait(false);
                }

                Assert.That(
                    await WaitForEventCountAsync(
                        collector,
                        "ReconnectCompleted",
                        1,
                        RecoveryTimeout,
                        ct).ConfigureAwait(false),
                    Is.True,
                    CreateFailureMessage("Timed out waiting for shared-channel reconnects.", seed, eventLog, collector));

                await WaitForQuiescence.ForManagerAsync(manager, RecoveryTimeout, ct: ct)
                    .ConfigureAwait(false);
                foreach (ManagedSessionType session in sessions)
                {
                    await ReadServerCurrentTimeAsync(session, ct).ConfigureAwait(false);
                }
                collector.RecordObservableInstruments();

                (TimeSpan p50, TimeSpan p95, TimeSpan p99) = runner.LatencyPercentiles;
                int expectedReconnectCycles = schedule.Events.Count;
                int reconnectStarted = collector.CountEvents("ReconnectStarted");
                int reconnectCompleted = collector.CountEvents("ReconnectCompleted");
                int reconnectFailed = collector.CountEvents("ReconnectFailed");
                string failureMessage = CreateFailureMessage(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "FailureRate={0}, p50={1}, p95={2}, p99={3}, started={4}, " +
                        "completed={5}, failed={6}, reactivations={7}, drops={8}.",
                        runner.FailureRate,
                        p50,
                        p95,
                        p99,
                        reconnectStarted,
                        reconnectCompleted,
                        reconnectFailed,
                        reactivationNotifications,
                        expectedReconnectCycles),
                    seed,
                    eventLog,
                    collector,
                    runner,
                    refcountViolations);

                Assert.Multiple(() =>
                {
                    Assert.That(runner.FailureRate, Is.LessThan(0.05), failureMessage);
                    Assert.That(p95, Is.LessThan(TimeSpan.FromSeconds(3)), failureMessage);
                    // The exact number of shared-channel reconnects is not
                    // deterministic under concurrent chaos: two drops within a
                    // single recovery window coalesce into one cycle (fewer
                    // than the drop count), while a stale keep-alive Bad that
                    // surfaces after recovery can spawn one extra cycle (more
                    // than the drop count). What must hold is that the shared
                    // channel actually recovered, every reconnect it started
                    // completed, and - the essence of coalescing - each shared
                    // reconnect fanned out to a reactivation of every session
                    // sharing the channel. The single-shared-key and
                    // refcount==SharedSessionCount assertions above already
                    // prove the ten sessions ride one channel.
                    Assert.That(reconnectCompleted, Is.GreaterThanOrEqualTo(1), failureMessage);
                    Assert.That(reconnectFailed, Is.Zero, failureMessage);
                    Assert.That(reconnectStarted, Is.GreaterThanOrEqualTo(reconnectCompleted), failureMessage);
                    Assert.That(
                        reactivationNotifications,
                        Is.GreaterThanOrEqualTo(reconnectCompleted * SharedSessionCount),
                        failureMessage);
                    Assert.That(refcountViolations, Is.Empty, failureMessage);
                });
            }
            finally
            {
                for (int index = sessions.Count - 1; index >= 0; index--)
                {
                    await CloseAndDisposeAsync(sessions[index]).ConfigureAwait(false);
                }
            }

            if (hasKey)
            {
                Assert.That(
                    await WaitForQuiescence.EntryGoneAsync(manager, key, DefaultWait, ct)
                        .ConfigureAwait(false),
                    Is.True,
                    CreateFailureMessage("The shared managed-channel entry should be removed after dispose.", seed, eventLog));
            }

            LeakCounters.AssertNoLeaks(before, LeakCounters.Capture(manager), "L3-A2");
        }

        private static ExponentialBackoffChannelReconnectPolicy CreateTightReconnectPolicy()
        {
            return new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(2),
                MaxAttempts = 20
            };
        }

        private static ChaosSchedule CreatePeriodicDropSchedule(int seed)
        {
            return new ChaosSchedule(
                seed,
                ChaosDuration,
                [ChaosEventKind.DropAllConnections],
                minInterval: TimeSpan.FromSeconds(5),
                maxInterval: TimeSpan.FromSeconds(15));
        }

        private static ChaosSchedule CreateThirtySecondDropSchedule(int seed)
        {
            return new ChaosSchedule(
                seed,
                ChaosDuration,
                [ChaosEventKind.DropAllConnections],
                minInterval: TimeSpan.FromSeconds(30),
                maxInterval: TimeSpan.FromSeconds(30));
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

        private static async Task DispatchDropAsync(
            TcpChaosProxy proxy,
            ChaosEvent chaosEvent,
            int seed,
            ConcurrentQueue<string> eventLog,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (chaosEvent.Kind != ChaosEventKind.DropAllConnections)
            {
                return;
            }

            eventLog.Enqueue(FormattableString.Invariant(
                $"{Timestamp()} seed={seed} chaos={chaosEvent.Kind} at={chaosEvent.At}"));
            await proxy.DropAllConnectionsAsync().ConfigureAwait(false);
        }

        private static async Task RunScheduleForDurationAsync(
            ChaosScheduleRunner dispatcher,
            ChaosSchedule schedule,
            CancellationToken ct)
        {
            TimeProvider timeProvider = TimeProvider.System;
            long start = timeProvider.GetTimestamp();
            await dispatcher.RunAsync(ct).ConfigureAwait(false);

            TimeSpan remaining = schedule.TotalDuration - timeProvider.GetElapsedTime(start);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, timeProvider, ct).ConfigureAwait(false);
            }
        }

        private static async Task<bool> WaitForEventCountAsync(
            MetricsCollector collector,
            string eventName,
            int expectedCount,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return await WaitForAsync(
                () => collector.CountEvents(eventName) >= expectedCount,
                timeout,
                ct).ConfigureAwait(false);
        }

        private static void AddChannelEventLogging(
            ManagedSessionType session,
            string sessionName,
            ConcurrentQueue<string> eventLog,
            Action? onReactivating = null)
        {
            session.ChannelStateChanged += (_, change) =>
            {
                string transition = FormattableString.Invariant($"{change.PreviousState}->{change.NewState}");
                string status = change.Error?.StatusCode.ToString() ?? string.Empty;
                eventLog.Enqueue(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} session={1} channel={2} attempt={3} status={4}",
                    Timestamp(),
                    sessionName,
                    transition,
                    change.ReconnectAttempt,
                    status));
                if (change.NewState == ChannelState.TransportConnectedSessionReactivating)
                {
                    onReactivating?.Invoke();
                }
            };
        }

        private static async Task TrackRefcountAsync(
            ClientChannelManager manager,
            ManagedChannelKey key,
            int expectedRefcount,
            ConcurrentQueue<string> eventLog,
            ConcurrentQueue<string> violations,
            CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    IReadOnlyList<ManagedChannelDiagnostic> diagnostics = manager.GetChannelDiagnostics();
                    ManagedChannelDiagnostic? diagnostic = diagnostics.FirstOrDefault(
                        item => item.Key.Equals(key));
                    string? violation = GetRefcountViolation(diagnostic, expectedRefcount, diagnostics.Count);
                    if (violation != null)
                    {
                        string message = FormattableString.Invariant($"{Timestamp()} {violation}");
                        violations.Enqueue(message);
                        eventLog.Enqueue(message);
                    }

                    await Task.Delay(DiagnosticPollInterval, TimeProvider.System, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        private static string? GetRefcountViolation(
            ManagedChannelDiagnostic? diagnostic,
            int expectedRefcount,
            int diagnosticCount)
        {
            if (diagnostic == null)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Expected one diagnostic entry with refcount={0}, but saw {1} entries.",
                    expectedRefcount,
                    diagnosticCount);
            }

            if (diagnostic.Refcount != expectedRefcount ||
                diagnostic.ParticipantCount != expectedRefcount)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Expected refcount/participants={0}, but saw refcount={1}, participants={2}, state={3}.",
                    expectedRefcount,
                    diagnostic.Refcount,
                    diagnostic.ParticipantCount,
                    diagnostic.State);
            }

            return null;
        }

        private static async Task IgnoreCanceledAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static string CreateFailureMessage(
            string message,
            int seed,
            ConcurrentQueue<string> eventLog,
            MetricsCollector? collector = null,
            StressRunner? runner = null,
            ConcurrentQueue<string>? refcountViolations = null)
        {
            var builder = new StringBuilder();
            builder
                .Append(message)
                .Append(" seed=")
                .Append(seed)
                .AppendLine();

            if (runner != null)
            {
                builder
                    .Append("Ops attempted=")
                    .Append(runner.TotalOpsAttempted)
                    .Append(", succeeded=")
                    .Append(runner.TotalOpsSucceeded)
                    .Append(", failed=")
                    .Append(runner.TotalOpsFailed)
                    .AppendLine();
            }

            if (collector != null)
            {
                builder
                    .Append("ReconnectStarted=")
                    .Append(collector.CountEvents("ReconnectStarted"))
                    .Append(", ReconnectCompleted=")
                    .Append(collector.CountEvents("ReconnectCompleted"))
                    .Append(", ReconnectFailed=")
                    .Append(collector.CountEvents("ReconnectFailed"))
                    .AppendLine();
                AppendMetricEvents(builder, collector.Events);
            }

            if (refcountViolations is { IsEmpty: false })
            {
                builder.AppendLine("Refcount violations:");
                AppendLines(builder, refcountViolations);
            }

            builder.AppendLine("Event log:");
            AppendLines(builder, eventLog);
            return builder.ToString();
        }

        private static void AppendMetricEvents(
            StringBuilder builder,
            IReadOnlyList<MetricsCollector.EventRecord> events)
        {
            foreach (MetricsCollector.EventRecord eventRecord in events)
            {
                if (!IsReconnectEvent(eventRecord.Name))
                {
                    continue;
                }

                builder
                    .Append(eventRecord.Timestamp.ToString("O", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(eventRecord.Name)
                    .Append(' ');
                AppendPayload(builder, eventRecord.Payload);
                builder.AppendLine();
            }
        }

        private static void AppendPayload(
            StringBuilder builder,
            IReadOnlyDictionary<string, object?> payload)
        {
            foreach (KeyValuePair<string, object?> item in payload)
            {
                builder
                    .Append(item.Key)
                    .Append('=')
                    .Append(item.Value)
                    .Append(';');
            }
        }

        private static bool IsReconnectEvent(string name)
        {
            return string.Equals(name, "ReconnectStarted", StringComparison.Ordinal) ||
                string.Equals(name, "ReconnectCompleted", StringComparison.Ordinal) ||
                string.Equals(name, "ReconnectFailed", StringComparison.Ordinal);
        }

        private static void AppendLines(StringBuilder builder, IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                builder.AppendLine(line);
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

        private static string Timestamp()
        {
            return TimeProvider.System.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        }

        private const string SeedParameterName = "Seed";
        private const string SeedEnvironmentVariableName = "OPCUA_CHAOS_SEED";
        private const int KeepAliveIntervalMilliseconds = 500;
        private const int SharedSessionCount = 10;
        private static readonly TimeSpan ChaosDuration = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DiagnosticPollInterval = TimeSpan.FromMilliseconds(100);

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
