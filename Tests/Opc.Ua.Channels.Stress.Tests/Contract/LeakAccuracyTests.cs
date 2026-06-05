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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Helpers;

namespace Opc.Ua.Channels.Stress.Tests.Contract
{
    /// <summary>
    /// L1 contract tests that exercise the leak-counter and quiescence
    /// invariants of <see cref="ClientChannelManager"/> against the
    /// in-memory <see cref="Fakes.FakeTransport"/>.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [Parallelizable(ParallelScope.None)]
    public sealed class LeakAccuracyTests : ContractTestBase
    {
        /// <summary>
        /// L1-LEAK1: 1,000 sequential get/dispose cycles, each on a
        /// distinct endpoint and participant, must leave the manager
        /// completely empty (zero tolerance) once it has quiesced.
        /// </summary>
        [Test]
        public async Task ThousandGetAsyncDisposeCyclesProducesNoLeakAsync()
        {
            ContractHarness harness = CreateHarness();
            await using (harness.ConfigureAwait(false))
            {
                LeakCounters.Snapshot before = LeakCounters.Capture(harness.Manager);

                for (int i = 0; i < 1000; i++)
                {
                    IReconnectParticipant participant = MakeParticipant(
                        string.Create(CultureInfo.InvariantCulture, $"p{i}"),
                        MakeEndpoint());

                    IManagedTransportChannel channel = await harness.Manager
                        .GetAsync(participant, default)
                        .ConfigureAwait(false);

                    channel.Dispose();
                }

                await WaitForQuiescence
                    .ForManagerAsync(harness.Manager, TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                LeakCounters.Snapshot after = LeakCounters.Capture(harness.Manager);
                LeakCounters.AssertNoLeaks(before, after, "L1-LEAK1", tolerance: 0);
            }
        }

        /// <summary>
        /// L1-LEAK2: a single lease survives 100 sequential reconnect
        /// cycles. Entry count must stay at one throughout, the final
        /// refcount must be one, and the thread count must not grow
        /// unboundedly.
        /// </summary>
        [Test]
        public async Task SingleLeaseSurvivesHundredReconnectsWithoutThreadLeakAsync()
        {
            ContractHarness harness = CreateHarness();
            await using (harness.ConfigureAwait(false))
            {
                int threadBaseline = Process.GetCurrentProcess().Threads.Count;

                ConfiguredEndpoint endpoint = MakeEndpoint();
                IReconnectParticipant participant = MakeParticipant("p0", endpoint);

                IManagedTransportChannel channel = await harness.Manager
                    .GetAsync(participant, default)
                    .ConfigureAwait(false);
                ManagedChannelKey key = channel.Key;
                try
                {
                    Assert.That(
                        harness.Manager.GetChannelDiagnostics(),
                        Has.Count.EqualTo(1),
                        "Manager should track exactly one entry after the initial GetAsync.");

                    for (int i = 0; i < 100; i++)
                    {
                        await harness.Manager
                            .ReconnectAsync(channel, default)
                            .ConfigureAwait(false);

                        Assert.That(
                            await WaitForQuiescence
                                .EntryRefcountReachesAsync(harness.Manager, key, 1, TimeSpan.FromSeconds(10))
                                .ConfigureAwait(false),
                            Is.True,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Entry refcount did not return to 1 after reconnect iteration {i}."));

                        IReadOnlyList<ManagedChannelDiagnostic> diagnostics =
                            harness.Manager.GetChannelDiagnostics();
                        Assert.That(
                            diagnostics,
                            Has.Count.EqualTo(1),
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Manager entry count drifted on reconnect iteration {i}."));
                    }

                    await WaitForQuiescence
                        .ForManagerAsync(harness.Manager, TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);

                    IReadOnlyList<ManagedChannelDiagnostic> finalDiagnostics =
                        harness.Manager.GetChannelDiagnostics();
                    Assert.Multiple(() =>
                    {
                        Assert.That(finalDiagnostics, Has.Count.EqualTo(1));
                        Assert.That(finalDiagnostics[0].Refcount, Is.EqualTo(1));
                        Assert.That(finalDiagnostics[0].Key, Is.EqualTo(key));
                    });
                }
                finally
                {
                    channel.Dispose();
                }

                Assert.That(
                    await WaitForQuiescence
                        .EntryGoneAsync(harness.Manager, key, TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false),
                    Is.True,
                    "Entry was not removed after lease disposal.");
                Assert.That(harness.Manager.GetChannelDiagnostics(), Is.Empty);

                int threadFinal = Process.GetCurrentProcess().Threads.Count;
                // ThreadPool flex: allow a small headroom over the baseline.
                Assert.That(
                    threadFinal,
                    Is.LessThanOrEqualTo(threadBaseline + 5),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Thread count grew from {threadBaseline} to {threadFinal} across 100 reconnects."));
            }
        }

        /// <summary>
        /// L1-LEAK3: 50 leases on 50 distinct keys. Disposing half of
        /// them leaves exactly the remaining 25; disposing the rest
        /// drains the manager to zero.
        /// </summary>
        [Test]
        public async Task FiftyLeasesDisposedInHalvesDrainToZeroAsync()
        {
            const int totalLeases = 50;
            const int firstWave = 25;

            ContractHarness harness = CreateHarness();
            await using (harness.ConfigureAwait(false))
            {
                var channels = new List<IManagedTransportChannel>(totalLeases);
                try
                {
                    for (int i = 0; i < totalLeases; i++)
                    {
                        IReconnectParticipant participant = MakeParticipant(
                            string.Create(CultureInfo.InvariantCulture, $"p{i}"),
                            MakeEndpoint());
                        channels.Add(await harness.Manager
                            .GetAsync(participant, default)
                            .ConfigureAwait(false));
                    }

                    Assert.That(
                        harness.Manager.GetChannelDiagnostics(),
                        Has.Count.EqualTo(totalLeases),
                        "All 50 leases must be visible before any are disposed.");

                    for (int i = 0; i < firstWave; i++)
                    {
                        channels[i].Dispose();
                    }

                    await WaitForQuiescence
                        .ForManagerAsync(harness.Manager, TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);

                    Assert.That(
                        harness.Manager.GetChannelDiagnostics(),
                        Has.Count.EqualTo(totalLeases - firstWave),
                        "After disposing the first 25 leases, 25 entries must remain.");

                    for (int i = firstWave; i < totalLeases; i++)
                    {
                        channels[i].Dispose();
                    }

                    await WaitForQuiescence
                        .ForManagerAsync(harness.Manager, TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);

                    Assert.That(
                        harness.Manager.GetChannelDiagnostics(),
                        Is.Empty,
                        "All entries must be removed after disposing every lease.");
                }
                catch
                {
                    foreach (IManagedTransportChannel channel in channels)
                    {
                        try
                        {
                            channel.Dispose();
                        }
                        catch
                        {
                            // best-effort cleanup on failure
                        }
                    }

                    throw;
                }
            }
        }

        private static ConfiguredEndpoint MakeEndpoint()
        {
            int id = Interlocked.Increment(ref s_endpointCounter);
            string url = string.Create(
                CultureInfo.InvariantCulture,
                $"opc.tcp://leak-accuracy-{id}.localhost:4840/");
            return CreateEndpoint(url);
        }

        private static LeakAccuracyParticipant MakeParticipant(string id, ConfiguredEndpoint endpoint)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            return new LeakAccuracyParticipant(id, endpoint);
        }

        private sealed class LeakAccuracyParticipant : IReconnectParticipant
        {
            internal LeakAccuracyParticipant(string id, ConfiguredEndpoint endpoint)
            {
                Id = id;
                Endpoint = endpoint;
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                _ = channel;
                _ = reconnectAttempt;
                _ = ct;
                return new ValueTask<ParticipantReconnectResult>(
                    ParticipantReconnectResult.Reactivated);
            }
        }

        private static int s_endpointCounter;
    }
}
