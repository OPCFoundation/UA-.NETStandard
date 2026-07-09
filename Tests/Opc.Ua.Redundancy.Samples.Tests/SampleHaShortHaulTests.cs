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
                "RedundantPubSub",
                "RedundantPubSub",
                ["--role", "demo", "--ha-mode", "hot"],
                SampleTestEnvironment.FastDemo);

            await demo.WaitForLineAsync(
                "FAILOVER: stopping publisher-a; publisher-b is promoted.",
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await demo.WaitForLineAsync(
                "SIMULATED: HA OK: sequence continued",
                TimeSpan.FromSeconds(30),
                cancellationToken);

            Assert.That(
                demo.ContainsLine("SIMULATED: DATA LOSS:"),
                Is.False,
                "Hot mode must not report a SequenceNumber reset (data loss).");
            Assert.That(
                await demo.WaitForExitAsync(TimeSpan.FromSeconds(20)),
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
                "RedundantPubSub",
                "RedundantPubSub",
                ["--role", "demo", "--ha-mode", "cold"],
                SampleTestEnvironment.FastDemo);

            await demo.WaitForLineAsync(
                "FAILOVER: stopping publisher-a; publisher-b is promoted.",
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await demo.WaitForLineAsync(
                "SIMULATED: DATA LOSS: sequence reset",
                TimeSpan.FromSeconds(30),
                cancellationToken);

            Assert.That(
                await demo.WaitForExitAsync(TimeSpan.FromSeconds(20)),
                Is.True,
                "The PubSub demo should terminate on its own after the failover narrative.");
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
                cancellationToken);

            await using var client = new SampleAppProcess(
                "client",
                "RedundantClient",
                "RedundantClient",
                [
                    "--server", cluster.BootstrapServerUrl,
                    "--autoaccept",
                    "--nosecurity",
                    "--duration", "00:02:00"
                ],
                SampleTestEnvironment.IndependentClient);

            await client.WaitForLineAsync("Connected replica:", TimeSpan.FromSeconds(75), cancellationToken);
        }
    }
}
