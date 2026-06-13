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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;
using Opc.Ua.Stress.Tests.Channels.Integration;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Stress.Tests.Channels.Soak
{
    /// <summary>
    /// Layer-4 soak tests for channel-manager memory stability under repeated reconnects.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("ManagedSession")]
    [Category("Soak")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class MemoryStabilitySoakTests : IntegrationTestBase
    {
        /// <summary>
        /// L4-MEM: verifies shared-channel memory and leak counters stay stable during a 30-minute TCP-drop soak.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [Category("Soak")]
        [CancelAfter(ThirtyMinuteSoakCancelAfterMilliseconds)]
        public async Task ThirtyMinuteMemoryStabilityAsync(CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            TestContext.Out.WriteLine(FormattableString.Invariant($"L4-MEM seed={seed}"));

            TcpChaosProxy proxy = await TcpChaosProxy
                .StartAsync(ServerUrl, telemetry: Telemetry)
                .ConfigureAwait(false);
            await using var proxyDispose = proxy.ConfigureAwait(false);
            ClientChannelManager manager = CreateChannelManager();
            await using var managerDispose = manager.ConfigureAwait(false);
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            CancellationToken runToken = runCts.Token;

            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None, proxy.LocalUrl)
                .ConfigureAwait(false);
            endpoint.EndpointUrl = proxy.LocalUrl;
            endpoint.Description.Server.DiscoveryUrls = [proxy.LocalUrl.ToString()];
            endpoint.UpdateBeforeConnect = false;

            var sessions = new List<ManagedSessionType>(SessionCount);
            try
            {
                for (int index = 0; index < SessionCount; index++)
                {
                    sessions.Add(await ConnectManagedSessionAsync(
                            endpoint,
                            manager,
                            FormattableString.Invariant($"mem-{index}"),
                            runToken)
                        .ConfigureAwait(false));
                }

                var snapshots = new List<MemorySnapshot>(SnapshotCount);
                snapshots.Add(CaptureSnapshot(manager, "T=0"));

                Func<CancellationToken, Task>[] operations = CreateReadOperations(sessions);
                var runner = new StressRunner(
                    operations,
                    concurrency: StressConcurrency,
                    targetOpsPerSecond: TargetOperationsPerSecond);
                await using var runnerDispose = runner.ConfigureAwait(false);
                await runner.StartAsync(runToken).ConfigureAwait(false);

                ChaosSchedule schedule = CreateDropSchedule(seed);
                var dispatcher = new ChaosScheduleRunner(
                    schedule,
                    (chaosEvent, workerCt) => DispatchChaosEventAsync(proxy, chaosEvent, workerCt));
                await using var dispatcherDispose = dispatcher.ConfigureAwait(false);
                Task dispatchTask = dispatcher.RunAsync(runToken);

                try
                {
                    for (int index = 1; index <= SoakSnapshotIntervals; index++)
                    {
                        await Task.Delay(SnapshotInterval, runToken).ConfigureAwait(false);
                        snapshots.Add(CaptureSnapshot(
                            manager,
                            FormattableString.Invariant($"T={index * 10}min")));
                    }

                    await dispatchTask.ConfigureAwait(false);
                }
                finally
                {
                    runCts.Cancel();
                    await runner.StopAsync().ConfigureAwait(false);
                    await IgnoreCanceledAsync(dispatchTask).ConfigureAwait(false);
                }

                ValidateSnapshots(snapshots, seed);
            }
            finally
            {
                for (int index = sessions.Count - 1; index >= 0; index--)
                {
                    await CloseAndDisposeAsync(sessions[index]).ConfigureAwait(false);
                }
            }
        }

        private static MemorySnapshot CaptureSnapshot(ClientChannelManager manager, string label)
        {
            if (!LeakDetectionHelpers.TryRunFinalizerSweep())
            {
                Assert.Warn(
                    $"Finalizer sweep exceeded {LeakDetectionHelpers.DefaultFinalizerSweepTimeout.TotalSeconds:0}s " +
                    "watchdog; memory/leak snapshots may be inaccurate.");
            }

            long memory = GC.GetTotalMemory(forceFullCollection: true);
            IReadOnlyList<ManagedChannelDiagnostic> diagnostics = manager.GetChannelDiagnostics();
            return new MemorySnapshot(
                label,
                memory,
                diagnostics.Count,
                diagnostics.Sum(static diagnostic => diagnostic.Refcount),
                LeakCounters.Capture(manager));
        }

        private static ChaosSchedule CreateDropSchedule(int seed)
        {
            return new ChaosSchedule(
                seed,
                SoakDuration,
                [ChaosEventKind.DropAllConnections],
                DropMinimumInterval,
                DropMaximumInterval);
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

        private static async Task DispatchChaosEventAsync(
            TcpChaosProxy proxy,
            ChaosEvent chaosEvent,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (chaosEvent.Kind == ChaosEventKind.DropAllConnections)
            {
                await proxy.DropAllConnectionsAsync().ConfigureAwait(false);
            }
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

        private static void ValidateSnapshots(
            IReadOnlyList<MemorySnapshot> snapshots,
            int seed)
        {
            MemorySnapshot baseline = snapshots[0];
            long maximumManagedBytes = (long)Math.Ceiling(baseline.ManagedBytes * ManagedMemoryGrowthLimitFactor);

            foreach (MemorySnapshot snapshot in snapshots)
            {
                Console.WriteLine(snapshot);
            }

            Assert.Multiple(() =>
            {
                foreach (MemorySnapshot snapshot in snapshots)
                {
                    Assert.That(
                        snapshot.ManagedBytes,
                        Is.LessThanOrEqualTo(maximumManagedBytes),
                        FormattableString.Invariant(
                            $"Memory grew >50% by {snapshot.Label} over 30min soak (seed={seed})."));
                    Assert.That(
                        snapshot.ActiveEntries,
                        Is.EqualTo(1),
                        FormattableString.Invariant(
                            $"Expected 1 active entry at {snapshot.Label} (seed={seed})."));
                    Assert.That(
                        snapshot.TotalRefcount,
                        Is.EqualTo(SessionCount),
                        FormattableString.Invariant(
                            $"Expected total refcount {SessionCount} at {snapshot.Label} (seed={seed})."));
                }
            });

            LeakCounters.AssertNoLeaks(baseline.LeakCount, snapshots[^1].LeakCount, "MEM-30min");
        }

        private sealed record MemorySnapshot(
            string Label,
            long ManagedBytes,
            int ActiveEntries,
            int TotalRefcount,
            LeakCounters.Snapshot LeakCount);

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

        private const double ManagedMemoryGrowthLimitFactor = 1.5;
        private const int SessionCount = 3;
        private const int SnapshotCount = SoakSnapshotIntervals + 1;
        private const int SoakSnapshotIntervals = 3;
        private const int StressConcurrency = 4;
        private const int TargetOperationsPerSecond = 30;
        private const int ThirtyMinuteSoakCancelAfterMilliseconds = 33 * 60 * 1000;
        private const string SeedEnvironmentVariableName = "OPCUA_CHAOS_SEED";
        private const string SeedParameterName = "Seed";
        private static readonly TimeSpan DropMaximumInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DropMinimumInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SoakDuration = TimeSpan.FromMinutes(30);
    }
}
