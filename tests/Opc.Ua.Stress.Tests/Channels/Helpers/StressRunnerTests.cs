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
