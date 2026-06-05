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

namespace Opc.Ua.Channels.Stress.Tests.Contract
{
    /// <summary>
    /// Layer-1 participant-result aggregation tests for managed channel reconnect cycles.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ParticipantResultAggregationTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        public async Task MixedParticipantResultsDetachFatalParticipantAndRetryTransientAsync(
            CancellationToken ct)
        {
            ContractHarness harness = CreateHarness();
            await using (harness.ConfigureAwait(false))
            {
                FakeParticipant[] participants = CreateParticipants(harness.Endpoint, 5);
                ConfigureInitialResult(participants[0], ParticipantReconnectResult.Reactivated);
                ConfigureInitialResult(participants[1], ParticipantReconnectResult.Reactivated);
                ConfigureInitialThenReactivated(participants[2], ParticipantReconnectResult.RequiresSessionRecreate);
                ConfigureInitialThenReactivated(participants[3], ParticipantReconnectResult.TransientFailure);
                ConfigureInitialResult(participants[4], ParticipantReconnectResult.FatalForParticipant);

                IManagedTransportChannel[] channels = await AttachParticipantsAsync(
                        harness.Manager,
                        participants,
                        ct)
                    .ConfigureAwait(false);
                ManagedChannelKey key = channels[0].Key;

                ManagedChannelDiagnostic initial = GetDiagnostic(harness.Manager, key);
                Assert.Multiple(() =>
                {
                    Assert.That(initial.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(initial.ParticipantCount, Is.EqualTo(5));
                    Assert.That(initial.Refcount, Is.EqualTo(5));
                });

                await harness.Manager.ReconnectAsync(channels[0], ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(harness.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                ManagedChannelDiagnostic completed = GetDiagnostic(harness.Manager, key);
                Assert.Multiple(() =>
                {
                    Assert.That(completed.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(completed.ParticipantCount, Is.EqualTo(4));
                    Assert.That(completed.Refcount, Is.EqualTo(4));
                    Assert.That(participants[0].NotificationCount, Is.EqualTo(2));
                    Assert.That(participants[1].NotificationCount, Is.EqualTo(2));
                    Assert.That(participants[2].NotificationCount, Is.EqualTo(2));
                    Assert.That(participants[3].NotificationCount, Is.EqualTo(2));
                    Assert.That(participants[4].NotificationCount, Is.EqualTo(1));
                    Assert.That(participants[4].LastAttempt, Is.Zero);
                });

                for (int index = 0; index < 4; index++)
                {
                    await AssertCanSendReadRequestAsync(channels[index], ct).ConfigureAwait(false);
                }
            }
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task FatalForChannelFaultsChannelAndClosesServiceGateAsync(CancellationToken ct)
        {
            ContractHarness harness = CreateHarness();
            await using (harness.ConfigureAwait(false))
            {
                var attempts = new List<int>();
                FakeParticipant participant = CreateParticipant(harness.Endpoint);
                participant.ConfigureOnReconnect((attempt, _) =>
                {
                    lock (attempts)
                    {
                        attempts.Add(attempt);
                    }

                    return new ValueTask<ParticipantReconnectResult>(
                        attempt == -1
                            ? ParticipantReconnectResult.Reactivated
                            : ParticipantReconnectResult.FatalForChannel);
                });
                IManagedTransportChannel channel = await harness.Manager.GetAsync(participant, ct)
                    .ConfigureAwait(false);
                ManagedChannelKey key = channel.Key;

                await harness.Manager.ReconnectAsync(channel, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(harness.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                ManagedChannelDiagnostic diagnostic = GetDiagnostic(harness.Manager, key);
                Assert.Multiple(() =>
                {
                    Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Faulted));
                    Assert.That(participant.LastAttempt, Is.EqualTo(-1));
                    Assert.That(SnapshotAttempts(attempts).Count(attempt => attempt == -1), Is.EqualTo(1));
                });

                ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await channel
                        .SendRequestAsync(CreateServerStatusReadRequest(), ct)
                        .AsTask()
                        .ConfigureAwait(false));
                Assert.That(exception, Is.Not.Null);
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
            }
        }

        [Test]
        [Explicit("Documents production gap: RequiresSessionRecreate plumbing not wired through to Session.RecreateAsync.")]
        [CancelAfter(30_000)]
        public async Task AllParticipantsRequireRecreateDoesNotTriggerRecreateAsync(CancellationToken ct)
        {
            ContractHarness harness = CreateHarness();
            await using (harness.ConfigureAwait(false))
            {
                FakeParticipant[] participants = CreateParticipants(harness.Endpoint, 3);
                foreach (FakeParticipant participant in participants)
                {
                    ConfigureInitialResult(participant, ParticipantReconnectResult.RequiresSessionRecreate);
                }

                IManagedTransportChannel[] channels = await AttachParticipantsAsync(
                        harness.Manager,
                        participants,
                        ct)
                    .ConfigureAwait(false);
                ManagedChannelKey key = channels[0].Key;

                await harness.Manager.ReconnectAsync(channels[0], ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(harness.Manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);

                ManagedChannelDiagnostic diagnostic = GetDiagnostic(harness.Manager, key);
                Assert.Multiple(() =>
                {
                    Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(diagnostic.ParticipantCount, Is.EqualTo(3));
                    Assert.That(participants.Select(participant => participant.NotificationCount), Is.All.EqualTo(1));
                    Assert.That(participants.Select(participant => participant.LastAttempt), Is.All.Zero);
                });
            }
        }

        private static FakeParticipant CreateParticipant(ConfiguredEndpoint endpoint)
        {
            return new FakeParticipant(endpoint);
        }

        private static FakeParticipant[] CreateParticipants(ConfiguredEndpoint endpoint, int count)
        {
            var participants = new FakeParticipant[count];
            for (int index = 0; index < participants.Length; index++)
            {
                participants[index] = CreateParticipant(endpoint);
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

        private static void ConfigureInitialResult(
            FakeParticipant participant,
            ParticipantReconnectResult result)
        {
            participant.ConfigureOnReconnect(
                (_, _) => new ValueTask<ParticipantReconnectResult>(result));
        }

        private static void ConfigureInitialThenReactivated(
            FakeParticipant participant,
            ParticipantReconnectResult initialResult)
        {
            participant.ConfigureOnReconnect(
                (attempt, _) => new ValueTask<ParticipantReconnectResult>(
                    attempt == 0 ? initialResult : ParticipantReconnectResult.Reactivated));
        }

        private static int[] SnapshotAttempts(List<int> attempts)
        {
            lock (attempts)
            {
                return [.. attempts];
            }
        }
    }
}
