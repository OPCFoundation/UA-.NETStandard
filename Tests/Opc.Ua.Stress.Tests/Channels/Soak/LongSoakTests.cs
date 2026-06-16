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

#nullable enable

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
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
// TODO: remove this suppression when cleanup paths accept a separate non-test cancellation budget.
#pragma warning disable CA2016

namespace Opc.Ua.Stress.Tests.Channels.Soak
{
    /// <summary>
    /// Layer-4 long-running randomized soak coverage for shared channel-manager reconnect paths.
    /// </summary>
    [TestFixture]
    [Category("Soak")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class LongSoakTests : IntegrationTestBase
    {
        /// <summary>
        /// L4-SOAK1: runs a sixty-minute mixed chaos soak over five sessions sharing one channel.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [Category("Soak")]
        [CancelAfter(3_900_000)]
        public async Task SixtyMinuteRandomizedChaosSoakAsync(CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            TestContext.Out.WriteLine(FormattableString.Invariant($"L4-SOAK1 seed={seed}"));

            TcpChaosProxy proxy = await TcpChaosProxy.StartAsync(ServerUrl, telemetry: Telemetry)
                .ConfigureAwait(false);

            await using ConfiguredAsyncDisposable proxyAsyncDisposable = proxy.ConfigureAwait(false);
            ClientChannelManager manager = CreateChannelManager(CreateSoakReconnectPolicy());
            await using ConfiguredAsyncDisposable managerAsyncDisposable = manager.ConfigureAwait(false);
            using MetricsCollector collector = new();

            ConfiguredEndpoint endpoint = await GetProxyEndpointAsync(proxy.LocalUrl).ConfigureAwait(false);
            var sessions = new List<ManagedSessionType>(SessionCount);
            StressRunner? runner = null;

            try
            {
                for (int index = 0; index < SessionCount; index++)
                {
                    sessions.Add(await ConnectManagedSessionAsync(
                        endpoint,
                        manager,
                        FormattableString.Invariant($"L4-SOAK1-{index}"),
                        ct).ConfigureAwait(false));
                }

                await AssertSingleSharedReadyChannelAsync(sessions, manager, ct).ConfigureAwait(false);

                runner = new StressRunner(
                    CreateReadOperations(sessions),
                    concurrency: ReadConcurrency,
                    targetOpsPerSecond: TargetOpsPerSecond,
                    telemetry: Telemetry);
                await runner.StartAsync(ct).ConfigureAwait(false);

                ChaosSchedule schedule = CreateLongSoakSchedule(seed);
                TestContext.Out.WriteLine(FormattableString.Invariant(
                    $"L4-SOAK1 scheduled {schedule.Events.Count} events over {schedule.TotalDuration}."));

                var dispatcher = new ChaosScheduleRunner(
                    schedule,
                    (chaosEvent, workerCt) => DispatchChaosEventAsync(
                        chaosEvent,
                        proxy,
                        manager,
                        seed,
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
                collector.RecordObservableInstruments();

                string summary = CreateSummary(seed, runner, collector);
                TestContext.Out.WriteLine(summary);

                Assert.Multiple(() =>
                {
                    Assert.That(runner.TotalOpsAttempted, Is.GreaterThan(0), summary);
                    Assert.That(
                        runner.FailureRate,
                        Is.LessThan(MaxFailureRate),
                        FormattableString.Invariant($"Soak failure rate too high (seed={seed}). {summary}"));
                });
            }
            finally
            {
                if (runner != null)
                {
                    await runner.DisposeAsync().ConfigureAwait(false);
                }

                await CloseAndDisposeSessionsAsync(sessions).ConfigureAwait(false);
            }
        }

        private async Task DispatchChaosEventAsync(
            ChaosEvent chaosEvent,
            TcpChaosProxy proxy,
            ClientChannelManager manager,
            int seed,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"{Timestamp()} seed={seed} t={chaosEvent.At} kind={chaosEvent.Kind}"));

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
                case ChaosEventKind.TriggerFailover:
                    await RestartReferenceServerAsync(ct).ConfigureAwait(false);
                    break;
            }
        }

        private async Task RestartReferenceServerAsync(CancellationToken ct)
        {
            int port = ServerFixturePort;
            bool serverStopped = false;
            try
            {
                await ServerFixture.StopAsync().ConfigureAwait(false);
                serverStopped = true;
                await Task.Delay(ServerRestartOutage, TimeProvider.System, ct).ConfigureAwait(false);
            }
            finally
            {
                if (serverStopped)
                {
                    ReferenceServer = await ServerFixture.StartAsync(PkiRoot, port).ConfigureAwait(false);
                    ReferenceServer.TokenValidator = TokenValidator;
                    ServerFixturePort = ServerFixture.Port;
                }
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

        private static ExponentialBackoffChannelReconnectPolicy CreateSoakReconnectPolicy()
        {
            return new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(5),
                MaxAttempts = 20
            };
        }

        private static ChaosSchedule CreateLongSoakSchedule(int seed)
        {
            return new ChaosSchedule(
                seed,
                SoakDuration,
                [
                    ChaosEventKind.DropAllConnections,
                    ChaosEventKind.BlockAccept,
                    ChaosEventKind.ReconnectAllAsync,
                    ChaosEventKind.TriggerFailover
                ],
                minInterval: TimeSpan.FromMinutes(2),
                maxInterval: TimeSpan.FromMinutes(8));
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
            CancellationToken ct)
        {
            Assert.That(sessions, Has.Count.EqualTo(SessionCount));
            ManagedChannelKey key = GetManagedChannel(sessions[0]).Key;
            Assert.That(
                sessions.Select(session => GetManagedChannel(session).Key),
                Is.All.EqualTo(key),
                "All L4 soak sessions should share the same managed channel.");
            Assert.That(
                await WaitForQuiescence.EntryRefcountReachesAsync(
                    manager,
                    key,
                    SessionCount,
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "The shared channel should have one reference per soak session.");
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

        private static async Task CloseAndDisposeSessionsAsync(List<ManagedSessionType> sessions)
        {
            for (int index = sessions.Count - 1; index >= 0; index--)
            {
                await CloseAndDisposeAsync(sessions[index]).ConfigureAwait(false);
            }
        }

        private static string CreateSummary(
            int seed,
            StressRunner runner,
            MetricsCollector collector)
        {
            (TimeSpan p50, TimeSpan p95, TimeSpan p99) = runner.LatencyPercentiles;
            return string.Format(
                CultureInfo.InvariantCulture,
                "Seed={0} Ops attempted={1} succeeded={2} failed={3} failure-rate={4:P} " +
                    "p50={5} p95={6} p99={7} ReconnectStarted={8} ReconnectCompleted={9} ReconnectFailed={10}",
                seed,
                runner.TotalOpsAttempted,
                runner.TotalOpsSucceeded,
                runner.TotalOpsFailed,
                runner.FailureRate,
                p50,
                p95,
                p99,
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
        private const int SessionCount = 5;
        private const int ReadConcurrency = 8;
        private const int TargetOpsPerSecond = 50;
        private const double MaxFailureRate = 0.10;
        private static readonly TimeSpan SoakDuration = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan BlockAcceptDuration = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ServerRestartOutage = TimeSpan.FromSeconds(5);

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

