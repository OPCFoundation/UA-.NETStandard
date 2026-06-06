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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Fakes;
using Opc.Ua.Channels.Stress.Tests.Helpers;
using Opc.Ua.Security.Certificates;

// CA2007: NUnit invokes test code without requiring ConfigureAwait on framework disposal.
#pragma warning disable CA2007

namespace Opc.Ua.Channels.Stress.Tests.Contract
{
    /// <summary>
    /// Layer-1 lease lifecycle tests for managed channel reconnect cycles.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class LeaseLifecycleTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        [Description("L1-DISP1: dispose one lease while an in-flight reconnect cycle is blocked.")]
        public async Task L1Disp1DisposeOneLeaseDuringReconnectKeepsRemainingLeasesHealthyAsync(
            CancellationToken ct)
        {
            using Certificate certificate = CreateCertificate("lease-lifecycle-disp1");
            await using ContractTestEnvironment environment = CreateEnvironment(certificate);
            ConfiguredEndpoint endpoint = CreateEndpoint("lease-lifecycle-disp1");
            FakeParticipant[] participants = CreateParticipants(endpoint, LeaseCount);
            IManagedTransportChannel[] channels = await AttachParticipantsAsync(
                    environment.Manager,
                    participants,
                    ct)
                .ConfigureAwait(false);
            ChaosBarrier barrier = new(expectedParticipants: 1);
            Task reconnectTask = Task.CompletedTask;

            try
            {
                ManagedChannelKey key = channels[0].Key;
                AssertSingleDiagnostic(environment.Manager, key, expectedRefcount: LeaseCount);
                FakeTransport transport = GetSingleTransport(environment);
                transport.ReconnectBarrier = barrier;

                reconnectTask = environment.Manager.ReconnectAsync(channels[0], ct).AsTask();
                await WaitForBarrierArrivalAsync(barrier, ct).ConfigureAwait(false);

                channels[0].Dispose();
                Assert.That(
                    await WaitForQuiescence.EntryRefcountReachesAsync(
                        environment.Manager,
                        key,
                        expectedRefcount: LeaseCount - 1,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Disposing one lease during reconnect should reduce the entry refcount immediately.");

                barrier.Release();
                await reconnectTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(environment.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                AssertSingleDiagnostic(environment.Manager, key, expectedRefcount: LeaseCount - 1);
                await AssertCanSendReadRequestAsync(channels[1], ct).ConfigureAwait(false);
                await AssertCanSendReadRequestAsync(channels[2], ct).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(participants[0].NotificationCount, Is.Zero);
                    Assert.That(participants[1].NotificationCount, Is.EqualTo(1));
                    Assert.That(participants[2].NotificationCount, Is.EqualTo(1));
                });
            }
            finally
            {
                barrier.Release();
                DisposeChannels(channels);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-DISP2: dispose all leases while an in-flight reconnect cycle is blocked.")]
        public async Task L1Disp2DisposeAllLeasesDuringReconnectTearsEntryDownAfterCycleAsync(
            CancellationToken ct)
        {
            using Certificate certificate = CreateCertificate("lease-lifecycle-disp2");
            await using ContractTestEnvironment environment = CreateEnvironment(certificate);
            ConfiguredEndpoint endpoint = CreateEndpoint("lease-lifecycle-disp2");
            FakeParticipant[] participants = CreateParticipants(endpoint, LeaseCount);
            IManagedTransportChannel[] channels = await AttachParticipantsAsync(
                    environment.Manager,
                    participants,
                    ct)
                .ConfigureAwait(false);
            ChaosBarrier barrier = new(expectedParticipants: 1);
            Task reconnectTask = Task.CompletedTask;

            try
            {
                ManagedChannelKey key = channels[0].Key;
                AssertSingleDiagnostic(environment.Manager, key, expectedRefcount: LeaseCount);
                FakeTransport transport = GetSingleTransport(environment);
                transport.ReconnectBarrier = barrier;

                reconnectTask = environment.Manager.ReconnectAsync(channels[0], ct).AsTask();
                await WaitForBarrierArrivalAsync(barrier, ct).ConfigureAwait(false);

                DisposeChannels(channels);
                Assert.That(
                    await WaitForQuiescence.EntryRefcountReachesAsync(
                        environment.Manager,
                        key,
                        expectedRefcount: 0,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The entry should remain registered with refcount zero until the reconnect operation exits.");

                barrier.Release();
                await reconnectTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);

                Assert.That(
                    await WaitForQuiescence.EntryGoneAsync(environment.Manager, key, DefaultWait, ct)
                        .ConfigureAwait(false),
                    Is.True,
                    "The entry should be removed after the reconnect operation releases its internal refcount.");
                Assert.That(environment.Manager.GetChannelDiagnostics(), Is.Empty);
            }
            finally
            {
                barrier.Release();
                DisposeChannels(channels);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-CANCEL1: cancel callers waiting on WaitForReadyAsync while reconnect is blocked.")]
        public async Task L1Cancel1CancelQueuedServiceCallsDuringReconnectAllowsLaterCallsAsync(
            CancellationToken ct)
        {
            using Certificate certificate = CreateCertificate("lease-lifecycle-cancel1");
            ChaosBarrier openBarrier = new(expectedParticipants: 1);
            int transportCreateCount = 0;
            await using ContractTestEnvironment environment = CreateEnvironment(certificate, _ =>
            {
                FakeTransport transport = new();
                int ordinal = Interlocked.Increment(ref transportCreateCount);
                if (ordinal == 1)
                {
                    transport.SupportedFeatures = TransportChannelFeatures.None;
                }
                else
                {
                    transport.OpenBarrier = openBarrier;
                }

                return transport;
            });
            ConfiguredEndpoint endpoint = CreateEndpoint("lease-lifecycle-cancel1");
            FakeParticipant participant = new(endpoint);
            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);
            Task reconnectTask = Task.CompletedTask;

            try
            {
                ManagedChannelKey key = channel.Key;
                AssertSingleDiagnostic(environment.Manager, key, expectedRefcount: 1);

                reconnectTask = environment.Manager.ReconnectAsync(channel, ct).AsTask();
                await WaitForBarrierArrivalAsync(openBarrier, ct).ConfigureAwait(false);

                using var callerCts = new CancellationTokenSource();
                Task<IServiceResponse>[] queuedCalls = CreateQueuedServiceCalls(channel, callerCts.Token);

                callerCts.Cancel();
                foreach (Task<IServiceResponse> queuedCall in queuedCalls)
                {
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await queuedCall.ConfigureAwait(false));
                }

                openBarrier.Release();
                await reconnectTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(environment.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                AssertSingleDiagnostic(environment.Manager, key, expectedRefcount: 1);
                await AssertCanSendReadRequestAsync(channel, ct).ConfigureAwait(false);
            }
            finally
            {
                openBarrier.Release();
                channel.Dispose();
            }
        }

        private static FakeParticipant[] CreateParticipants(ConfiguredEndpoint endpoint, int count)
        {
            var participants = new FakeParticipant[count];
            for (int index = 0; index < participants.Length; index++)
            {
                participants[index] = new FakeParticipant(endpoint);
            }

            return participants;
        }

        private static async Task<IManagedTransportChannel[]> AttachParticipantsAsync(
            ClientChannelManager manager,
            FakeParticipant[] participants,
            CancellationToken ct)
        {
            var channels = new IManagedTransportChannel[participants.Length];
            for (int index = 0; index < participants.Length; index++)
            {
                channels[index] = await manager.GetAsync(participants[index], ct)
                    .ConfigureAwait(false);
            }

            return channels;
        }

        private static Task<IServiceResponse>[] CreateQueuedServiceCalls(
            IManagedTransportChannel channel,
            CancellationToken ct)
        {
            return [.. Enumerable.Range(0, QueuedCallCount).Select(_ => channel
                .SendRequestAsync(CreateServerStatusReadRequest(), ct)
                .AsTask())];
        }

        private static void DisposeChannels(params IManagedTransportChannel?[] channels)
        {
            foreach (IManagedTransportChannel? channel in channels)
            {
                channel?.Dispose();
            }
        }


        private static FakeTransport GetSingleTransport(ContractTestEnvironment environment)
        {
            IReadOnlyList<FakeTransport> transports = environment.Bindings.Created;
            Assert.That(transports, Has.Count.EqualTo(1));
            return transports[0];
        }

        private const int LeaseCount = 3;
        private const int QueuedCallCount = 4;
    }
}
