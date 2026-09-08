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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Short-haul integration tests that launch the redundant sample applications in
    /// their supported setups and assert on the high-availability behavior they log.
    /// These are deterministic and complete quickly so they can run as part of pull
    /// request validation. Longer, repeated failover soak scenarios live in
    /// <see cref="SampleHaLongHaulTests"/>.
    /// </summary>
    [TestFixture]
    [Category("SampleHaShortHaul")]
    [NonParallelizable]
    internal sealed class SampleHaShortHaulTests
    {
        /// <summary>
        /// Verifies that the single-process PubSub demo in hot mode fails the active
        /// publisher over to the standby and reports continuity with no data loss.
        /// </summary>
        [Test]
        [CancelAfter(90_000)]
        public async Task PubSubDemoHotShowsFailoverContinuityAsync(CancellationToken cancellationToken)
        {
            await using var demo = new SampleAppProcess(
                "pubsub-demo-hot",
                "Redundancy/RedundantPubSub",
                "RedundantPubSub",
                ["--role", "demo", "--ha-mode", "hot"],
                SampleTestEnvironment.BuildFastDemo());

            await demo.WaitForLineAsync(
                "FAILOVER: stopping publisher-a; publisher-b is promoted.",
                TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);
            await demo.WaitForLineAsync(
                "SIMULATED: HA OK: sequence continued",
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            Assert.That(
                demo.ContainsLine("SIMULATED: DATA LOSS:"),
                Is.False,
                "Hot mode must not report a SequenceNumber reset (data loss).");
            Assert.That(
                await demo.WaitForExitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false),
                Is.True,
                "The PubSub demo should terminate on its own after the failover narrative.");
        }

        /// <summary>
        /// Verifies that the single-process PubSub demo in cold mode makes the
        /// SequenceNumber reset (data loss) visible after failover.
        /// </summary>
        [Test]
        [CancelAfter(90_000)]
        public async Task PubSubDemoColdShowsDataLossAsync(CancellationToken cancellationToken)
        {
            await using var demo = new SampleAppProcess(
                "pubsub-demo-cold",
                "Redundancy/RedundantPubSub",
                "RedundantPubSub",
                ["--role", "demo", "--ha-mode", "cold"],
                SampleTestEnvironment.BuildFastDemo());

            await demo.WaitForLineAsync(
                "FAILOVER: stopping publisher-a; publisher-b is promoted.",
                TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);
            await demo.WaitForLineAsync(
                "SIMULATED: DATA LOSS: sequence reset",
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            Assert.That(
                await demo.WaitForExitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false),
                Is.True,
                "The PubSub demo should terminate on its own after the failover narrative.");
        }

        /// <summary>
        /// Verifies that raw, event, and processed HistoryRead continuation points created
        /// on the active Raft replica resume through a promoted replica without duplicates
        /// or gaps after the active process is terminated.
        /// </summary>
        [Test]
        [CancelAfter(300_000)]
        public async Task StrongHistorianContinuationsSurviveActiveReplicaFailureAsync(
            CancellationToken cancellationToken)
        {
            await using RedundantServerCluster cluster = await RedundantServerCluster.StartStrongAsync(
                count: 3,
                startupTimeout: TimeSpan.FromSeconds(90),
                cancellationToken).ConfigureAwait(false);
            RedundantServerReplica active = await WaitForActiveReplicaAsync(
                cluster,
                TimeSpan.FromSeconds(90),
                cancellationToken).ConfigureAwait(false);

            await using var client = new SampleAppProcess(
                "history-client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                [
                    "--server", active.ServerUrl,
                    "--autoaccept",
                    "--nosecurity",
                    "--history",
                    "--history-failover-delay", "00:00:15"
                ],
                SampleTestEnvironment.IndependentClient);

            await client.WaitForLineAsync(
                "HISTORY: portable continuations ready",
                TimeSpan.FromSeconds(120),
                cancellationToken).ConfigureAwait(false);
            Assert.That(
                client.ContainsLine("RedundancySupport=None"),
                Is.False,
                "The client must observe initialized redundancy metadata before the failover exercise.");
            Assert.That(
                client.ContainsLine("HISTORY: write/read marker -4390 visible on active replica."),
                Is.True,
                "A HistoryUpdate written through the active replica must be immediately readable.");

            active.Process.Kill();
            Assert.That(
                await active.Process.WaitForExitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false),
                Is.True,
                "The active server process must terminate during the failover exercise.");

            await client.WaitForLineAsync(
                "HISTORY HA OK:",
                TimeSpan.FromSeconds(120),
                cancellationToken).ConfigureAwait(false);
            Assert.That(
                client.ContainsLine("without duplicates or gaps"),
                Is.True,
                "Raw, event, and processed continuations must all resume on the promoted replica.");
            Assert.That(
                client.ContainsLine("HISTORY: write/read marker -4390 visible on promoted replica."),
                Is.True,
                "History written through the former active replica must be visible after promotion.");
            Assert.That(
                client.ContainsLine("HISTORY: promoted writer added shared raw and event history"),
                Is.True,
                "The promoted replica must append new raw and event history to the shared archive.");
            Assert.That(
                client.ContainsLine("BadContinuationPointInvalid"),
                Is.False,
                "Portable history continuations must remain valid across active-server loss.");
        }

        /// <summary>
        /// Verifies that the ordered distributed historian cannot be enabled in
        /// an eventual active/active multi-writer topology.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task ActiveActiveHistorianConfigurationFailsClosedAsync(
            CancellationToken cancellationToken)
        {
            await AssertHistorianTopologyFailsClosedAsync(
                "aa",
                "eventual",
                "hotandmirrored",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that selecting a strong store does not make an active/active
        /// multi-writer historian topology valid.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task StrongActiveActiveHistorianConfigurationFailsClosedAsync(
            CancellationToken cancellationToken)
        {
            await AssertHistorianTopologyFailsClosedAsync(
                "aa",
                "strong",
                "hotandmirrored",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that the distributed historian cannot use an eventual store,
        /// even when the server topology is otherwise active/passive.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task EventualActivePassiveHistorianConfigurationFailsClosedAsync(
            CancellationToken cancellationToken)
        {
            await AssertHistorianTopologyFailsClosedAsync(
                "ap",
                "eventual",
                "hot",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that the RedundantClient sample connects to a RedundantServer set and
        /// begins high-availability monitoring. This is a lightweight connectivity smoke
        /// test of the server and client apps running together; the actual server
        /// failover scenarios are exercised by the long-haul tests.
        /// </summary>
        [Test]
        [CancelAfter(150_000)]
        public async Task RedundantServerAndClientConnectAsync(CancellationToken cancellationToken)
        {
            await using RedundantServerCluster cluster = await RedundantServerCluster.StartSingleEventualAsync(
                startupTimeout: TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);

            await using var client = new SampleAppProcess(
                "client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                [
                    "--server", cluster.BootstrapServerUrl,
                    "--autoaccept",
                    "--nosecurity",
                    "--duration", "00:02:00"
                ],
                SampleTestEnvironment.IndependentClient);

            await client.WaitForLineAsync("Connected replica:", TimeSpan.FromSeconds(75), cancellationToken).ConfigureAwait(false);
        }

        private static async Task<RedundantServerReplica> WaitForActiveReplicaAsync(
            RedundantServerCluster cluster,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (RedundantServerReplica replica in cluster.LiveReplicas())
                {
                    int activeIndex = replica.Process.LastLineIndexContaining(
                        "became ACTIVE writer");
                    int standbyIndex = replica.Process.LastLineIndexContaining(
                        "became STANDBY");
                    if (activeIndex > standbyIndex)
                    {
                        return replica;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"The strong server cluster did not elect an active writer within {timeout}.");
        }

        private static async Task AssertHistorianTopologyFailsClosedAsync(
            string mode,
            string consistency,
            string redundancyMode,
            CancellationToken cancellationToken)
        {
            int port = TestPorts.GetFreePort();
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["HA_MODE"] = mode,
                ["HA_CONSISTENCY"] = consistency,
                ["REDUNDANCY_MODE"] = redundancyMode,
                ["HA_HISTORIAN"] = "true",
                ["HA_INSECURE"] = "true",
                ["HA_HOST"] = "127.0.0.1",
                ["HA_NODE_ID"] = "invalid-history-topology"
            };

            await using var server = new SampleAppProcess(
                "invalid-history-topology",
                "Redundancy/RedundantServer",
                "RedundantServer",
                ["--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                environment);

            await server.WaitForLineAsync(
                "distributed historian supports only a strongly consistent active/passive topology",
                TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);
            Assert.That(
                await server.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                Is.True,
                "An unsupported historian configuration must terminate startup.");
            Assert.That(
                server.ContainsLine("listening at"),
                Is.False,
                "The unsupported topology must fail before the OPC UA server starts listening.");
        }
    }
}
