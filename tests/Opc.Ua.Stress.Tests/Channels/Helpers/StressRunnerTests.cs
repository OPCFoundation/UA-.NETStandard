// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Smoke tests for L3 stress workload helpers.
    /// </summary>
    [TestFixture]
    [Category("StressRunner")]
    [Parallelizable]
    public class StressRunnerTests
    {
        /// <summary>
        /// Verifies the stress runner roughly honors the aggregate target rate.
        /// </summary>
        [Test]
        public async Task StressRunnerHonorsTargetRateAsync()
        {
            Func<CancellationToken, Task>[] operations =
            [
                static _ => Task.CompletedTask
            ];

            var runner = new StressRunner(
                operations,
                concurrency: 4,
                targetOpsPerSecond: 100);
            try
            {
                await runner.StartAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                await runner.StopAsync().ConfigureAwait(false);

                Assert.That(runner.TotalOpsAttempted, Is.InRange(50L, 200L));
            }
            finally
            {
                await runner.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies identical seeds produce identical chaos event schedules.
        /// </summary>
        [Test]
        public void ChaosScheduleDeterministicForSeed()
        {
            ChaosEventKind[] kinds =
            [
                ChaosEventKind.DropAllConnections,
                ChaosEventKind.BlockAccept,
                ChaosEventKind.StallForwarding,
                ChaosEventKind.ReconnectAllAsync
            ];

            var first = new ChaosSchedule(
                seed: 42,
                totalDuration: TimeSpan.FromSeconds(10),
                kinds: kinds,
                minInterval: TimeSpan.FromMilliseconds(100),
                maxInterval: TimeSpan.FromMilliseconds(500));

            var second = new ChaosSchedule(
                seed: 42,
                totalDuration: TimeSpan.FromSeconds(10),
                kinds: kinds,
                minInterval: TimeSpan.FromMilliseconds(100),
                maxInterval: TimeSpan.FromMilliseconds(500));

            Assert.That(second.Events, Is.EqualTo(first.Events));
        }
    }
}
