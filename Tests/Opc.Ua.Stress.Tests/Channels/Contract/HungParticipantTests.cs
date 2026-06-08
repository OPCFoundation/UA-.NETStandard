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
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Stress.Tests.Channels.Fakes;

// CA2000: contract-test disposables are transferred to the environment or released by cleanup paths.
// CA2007: NUnit invokes test code without requiring ConfigureAwait on framework calls.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Layer-1 hung-participant contract tests for managed channel reconnect cycles.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [Category("ChannelManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class HungParticipantTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        [Description("L1-HUNG1: participant timeout prevents a hung callback from blocking reconnect forever.")]
        public async Task HungParticipantTimesOutAndOtherParticipantsRecoverAsync(
            CancellationToken ct)
        {
            TimeSpan participantTimeout = TimeSpan.FromMilliseconds(200);
            using Certificate applicationCertificate = CreateCertificate("hung-participant-timeout");
            ContractTestEnvironment environment = CreateEnvironment(
                applicationCertificate,
                reconnectPolicy: CreateParticipantTimeoutPolicy(participantTimeout, maxAttempts: 2));
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("hung-participant-shared");
            var participant1 = new FakeParticipant(endpoint);
            participant1.ConfigureOnReconnect(async (attempt, reconnectCt) =>
            {
                if (attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), reconnectCt).ConfigureAwait(false);
                }

                return ParticipantReconnectResult.Reactivated;
            });
            FakeParticipant participant2 = CreateParticipant(endpoint);

            IManagedTransportChannel ch1 = await environment.Manager.GetAsync(participant1, ct)
                .ConfigureAwait(false);
            IManagedTransportChannel ch2 = await environment.Manager.GetAsync(participant2, ct)
                .ConfigureAwait(false);
            Assert.That(ch2.Key, Is.EqualTo(ch1.Key));

            Stopwatch sw = Stopwatch.StartNew();
            await environment.Manager.ReconnectAsync(ch1, ct).ConfigureAwait(false);
            sw.Stop();

            ManagedChannelDiagnostic diagnostic = GetDiagnostic(environment.Manager, ch1.Key);
            Assert.Multiple(() =>
            {
                Assert.That(sw.Elapsed, Is.GreaterThanOrEqualTo(participantTimeout));
                Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(participant1.NotificationCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(participant2.NotificationCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(participant2.LastAttempt, Is.GreaterThanOrEqualTo(1));
            });
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("A single hung participant transitions out of reactivation after the bounded timeout.")]
        public async Task HungParticipantTimesOutAfterBoundedWaitAsync(CancellationToken ct)
        {
            TimeSpan participantTimeout = TimeSpan.FromMilliseconds(200);
            using Certificate applicationCertificate = CreateCertificate("hung-participant-bounded-wait");
            ContractTestEnvironment environment = CreateEnvironment(
                applicationCertificate,
                reconnectPolicy: CreateParticipantTimeoutPolicy(participantTimeout, maxAttempts: 1));
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("hung-participant-bounded-wait");
            var participant = new FakeParticipant(endpoint);
            var participantEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            participant.ConfigureOnReconnect(async (attempt, reconnectCt) =>
            {
                if (attempt == 0)
                {
                    participantEntered.TrySetResult(true);
                    await Task.Delay(TimeSpan.FromSeconds(2), reconnectCt).ConfigureAwait(false);
                }

                return ParticipantReconnectResult.Reactivated;
            });

            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);

            Stopwatch sw = Stopwatch.StartNew();
            Task reconnectTask = environment.Manager.ReconnectAsync(channel, ct).AsTask();
            await participantEntered.Task.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);
            await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            sw.Stop();

            ManagedChannelDiagnostic diagnostic = GetDiagnostic(environment.Manager, channel.Key);
            Assert.Multiple(() =>
            {
                Assert.That(sw.Elapsed, Is.GreaterThanOrEqualTo(participantTimeout));
                Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Faulted));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
            });
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("Participant timeout does not produce false positives for callbacks that complete in time.")]
        public async Task BoundedParticipantTimeoutHonorsTimeoutAsync(CancellationToken ct)
        {
            TimeSpan participantTimeout = TimeSpan.FromSeconds(5);
            using Certificate applicationCertificate = CreateCertificate("participant-timeout-positive");
            ContractTestEnvironment environment = CreateEnvironment(
                applicationCertificate,
                reconnectPolicy: CreateParticipantTimeoutPolicy(participantTimeout, maxAttempts: 1));
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("participant-timeout-positive");
            var participant = new FakeParticipant(endpoint);
            participant.ConfigureOnReconnect(async (_, reconnectCt) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), reconnectCt).ConfigureAwait(false);
                return ParticipantReconnectResult.Reactivated;
            });

            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);

            Stopwatch sw = Stopwatch.StartNew();
            await environment.Manager.ReconnectAsync(channel, ct).ConfigureAwait(false);
            sw.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(participant.NotificationCount, Is.EqualTo(1));
            });
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-HUNG2: hung participant on one channel does not block reconnect of another channel.")]
        public async Task HungParticipantOnOneChannelDoesNotBlockOtherChannelAsync(
            CancellationToken ct)
        {
            using Certificate applicationCertificate = CreateCertificate("hung-participant-independent");
            ContractTestEnvironment environment = CreateEnvironment(applicationCertificate);
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            var hungParticipant = new FakeParticipant(CreateEndpoint("hung-channel-a"))
            {
                HangFor = TimeSpan.FromMinutes(10)
            };
            FakeParticipant normalParticipant = CreateParticipant(CreateEndpoint("normal-channel-b"));

            IManagedTransportChannel chA = await environment.Manager.GetAsync(hungParticipant, ct)
                .ConfigureAwait(false);
            IManagedTransportChannel chB = await environment.Manager.GetAsync(normalParticipant, ct)
                .ConfigureAwait(false);

            using var ctsA = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            Task taskA = IgnoreOperationCanceledAsync(
                environment.Manager.ReconnectAsync(chA, ctsA.Token).AsTask());
            await WaitForHungReconnectAsync(environment.Manager, chA.Key, hungParticipant, ct)
                .ConfigureAwait(false);

            Stopwatch sw = Stopwatch.StartNew();
            await environment.Manager.ReconnectAsync(chB, ct).ConfigureAwait(false);
            sw.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
                Assert.That(chB.State, Is.EqualTo(ChannelState.Ready));
            });

            await taskA.ConfigureAwait(false);
        }

        private static FakeParticipant CreateParticipant(ConfiguredEndpoint endpoint)
        {
            return new FakeParticipant(endpoint);
        }

        private static ExponentialBackoffChannelReconnectPolicy CreateParticipantTimeoutPolicy(
            TimeSpan participantTimeout,
            int maxAttempts)
        {
            return new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                MaxAttempts = maxAttempts,
                ParticipantTimeout = participantTimeout
            };
        }

        private static async Task IgnoreOperationCanceledAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static ManagedChannelDiagnostic GetDiagnostic(
            ClientChannelManager manager,
            ManagedChannelKey key)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return manager.GetChannelDiagnostics().Single(diagnostic => diagnostic.Key.Equals(key));
        }

        private static async Task WaitForHungReconnectAsync(
            ClientChannelManager manager,
            ManagedChannelKey key,
            FakeParticipant participant,
            CancellationToken ct)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            await WaitUntilAsync(
                    () => participant.NotificationCount > 0 &&
                        GetDiagnostic(manager, key).State == ChannelState.TransportConnectedSessionReactivating,
                    "Hung participant did not enter the reconnect notification path before timeout.",
                    ct)
                .ConfigureAwait(false);
        }
    }
}
