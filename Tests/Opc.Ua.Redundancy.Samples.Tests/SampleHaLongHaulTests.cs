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

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Long-haul integration tests that repeatedly exercise the redundant sample
    /// applications' high-availability paths for a configurable duration (default one
    /// hour, set via the <c>SAMPLE_HA_DURATION_MINUTES</c> environment variable) and
    /// verify the high-availability behavior they report holds across many failovers.
    /// <para>
    /// These are marked <see cref="ExplicitAttribute"/> so they never run as part of the
    /// normal pull-request test pass; they are executed by the dedicated
    /// <c>sample-ha-longhaul</c> GitHub Actions workflow and the equivalent Azure DevOps
    /// pipeline, or locally with <c>--filter Category=SampleHaLongHaul</c>.
    /// </para>
    /// <para>
    /// The multi-replica leader-election failover topologies (strong Raft and eventual
    /// active/active) that require DNS-based endpoint indirection are demonstrated by the
    /// docker-compose setups under <c>Applications/RedundantServer</c> and
    /// <c>Applications/RedundantClient</c>; these process-level soak tests focus on the
    /// failover and data-loss-visibility behavior that runs deterministically inside a CI
    /// runner without container networking.
    /// </para>
    /// </summary>
    [TestFixture]
    [Explicit("Long-haul soak test; run via the sample-ha-longhaul CI job or an explicit filter.")]
    [Category("SampleHaLongHaul")]
    [NonParallelizable]
    internal sealed class SampleHaLongHaulTests
    {
        /// <summary>
        /// Repeatedly runs the single-process PubSub high-availability demo in hot and cold
        /// modes for the configured duration and verifies that every iteration reports the
        /// expected failover continuity (hot: no data loss) and data loss (cold).
        /// </summary>
        [Test]
        public async Task PubSubFailoverSoakAsync()
        {
            TimeSpan duration = SampleTestEnvironment.LongHaulDuration(60);
            using var cts = new CancellationTokenSource(duration + TimeSpan.FromMinutes(5));
            CancellationToken cancellationToken = cts.Token;

            int iterations = 0;
            DateTime deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                await RunDemoIterationAsync("hot", cancellationToken);
                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                await RunDemoIterationAsync("cold", cancellationToken);
                iterations++;
                TestContext.Progress.WriteLine($"[test] PubSub demo hot+cold iteration {iterations} completed.");
            }

            Assert.That(iterations, Is.GreaterThanOrEqualTo(1), "At least one hot+cold demo iteration must run.");
        }

        /// <summary>
        /// Runs a RedundantClient against a RedundantServer for the configured duration and
        /// repeatedly kills and restarts the server, verifying that the client transparently
        /// detects the loss, reconnects when the server returns, and never crashes.
        /// </summary>
        [Test]
        public async Task ClientReconnectSoakAsync()
        {
            TimeSpan duration = SampleTestEnvironment.LongHaulDuration(60);
            using var cts = new CancellationTokenSource(duration + TimeSpan.FromMinutes(10));
            CancellationToken cancellationToken = cts.Token;

            await using RedundantServerCluster cluster = await RedundantServerCluster.StartSingleEventualAsync(
                startupTimeout: TimeSpan.FromSeconds(60),
                cancellationToken);
            RedundantServerReplica server = cluster.Replicas[0];

            await using var client = new SampleAppProcess(
                "client",
                "RedundantClient",
                "RedundantClient",
                [
                    "--server", cluster.BootstrapServerUrl,
                    "--autoaccept",
                    "--nosecurity",
                    "--duration", "00:00:00"
                ],
                SampleTestEnvironment.IndependentClient);

            await client.WaitForLineAsync("Connected replica:", TimeSpan.FromSeconds(90), cancellationToken);

            int cycles = 0;
            DateTime deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                Assert.That(client.HasExited, Is.False, "The client must not crash during the soak.");

                int lostBefore = client.CountLinesContaining("FAILOVER: connection lost");
                int reconnectBefore = client.CountLinesContaining("CONNECTED: session (re)connected");

                TestContext.Progress.WriteLine($"[test] Restarting the server (cycle {cycles + 1}).");
                await RedundantServerCluster.RestartReplicaAsync(
                    server, TimeSpan.FromSeconds(60), cancellationToken);

                bool detectedLoss = await client.WaitForCountAsync(
                    "FAILOVER: connection lost", lostBefore + 1, TimeSpan.FromSeconds(60), cancellationToken);
                Assert.That(detectedLoss, Is.True, "The client must detect the server going away.");

                bool reconnected = await client.WaitForCountAsync(
                    "CONNECTED: session (re)connected",
                    reconnectBefore + 1,
                    TimeSpan.FromSeconds(120),
                    cancellationToken);
                Assert.That(reconnected, Is.True, "The client must transparently reconnect after the server returns.");

                cycles++;
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            Assert.That(cycles, Is.GreaterThanOrEqualTo(1), "At least one restart/reconnect cycle must have run.");
            Assert.That(client.HasExited, Is.False, "The client must survive the entire soak without crashing.");
        }

        private static async Task RunDemoIterationAsync(string mode, CancellationToken cancellationToken)
        {
            await using var demo = new SampleAppProcess(
                "pubsub-demo-" + mode,
                "RedundantPubSub",
                "RedundantPubSub",
                ["--role", "demo", "--ha-mode", mode],
                SampleTestEnvironment.FastDemo);

            await demo.WaitForLineAsync(
                "FAILOVER: stopping publisher-a; publisher-b is promoted.",
                TimeSpan.FromSeconds(60),
                cancellationToken);

            if (string.Equals(mode, "hot", StringComparison.Ordinal))
            {
                await demo.WaitForLineAsync(
                    "SIMULATED: HA OK: sequence continued", TimeSpan.FromSeconds(30), cancellationToken);
                Assert.That(
                    demo.ContainsLine("SIMULATED: DATA LOSS:"),
                    Is.False,
                    "Hot mode must not report a SequenceNumber reset (data loss).");
            }
            else
            {
                await demo.WaitForLineAsync(
                    "SIMULATED: DATA LOSS: sequence reset", TimeSpan.FromSeconds(30), cancellationToken);
            }

            Assert.That(
                await demo.WaitForExitAsync(TimeSpan.FromSeconds(20)),
                Is.True,
                "The PubSub demo should terminate on its own after the failover narrative.");
        }
    }
}
