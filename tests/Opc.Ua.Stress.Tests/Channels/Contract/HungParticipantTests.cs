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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
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
            var participantTimeout = TimeSpan.FromMilliseconds(200);
            var fakeTime = new FakeTimeProvider();
            TimeProvider timeProvider = ObserveParticipantTimeout(
                fakeTime,
                participantTimeout,
                out Task timeoutScheduled);
            using Certificate applicationCertificate = CreateCertificate("hung-participant-timeout");
            ContractTestEnvironment environment = CreateEnvironment(
                applicationCertificate,
                reconnectPolicy: CreateParticipantTimeoutPolicy(participantTimeout, maxAttempts: 2),
                timeProvider: timeProvider);
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("hung-participant-shared");
            var participant1 = new FakeParticipant(endpoint);
            var releaseParticipant = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            participant1.ConfigureOnReconnect(async (attempt, reconnectCt) =>
            {
                if (attempt == 0)
                {
                    await releaseParticipant.Task.WaitAsync(reconnectCt).ConfigureAwait(false);
                }

                return ParticipantReconnectResult.Reactivated;
            });
            FakeParticipant participant2 = CreateParticipant(endpoint);

            IManagedTransportChannel ch1 = await environment.Manager.GetAsync(participant1, ct)
                .ConfigureAwait(false);
            IManagedTransportChannel ch2 = await environment.Manager.GetAsync(participant2, ct)
                .ConfigureAwait(false);
            Assert.That(ch2.Key, Is.EqualTo(ch1.Key));

            try
            {
                Task reconnectTask = environment.Manager.ReconnectAsync(ch1, ct).AsTask();
                await timeoutScheduled.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);
                await WaitUntilAsync(
                    () => participant2.NotificationCount == 1,
                    "The other participant did not receive the initial reconnect notification.",
                    ct).ConfigureAwait(false);

                fakeTime.Advance(participantTimeout - TimeSpan.FromTicks(1));
                Assert.Multiple(() =>
                {
                    Assert.That(reconnectTask.IsCompleted, Is.False);
                    Assert.That(ch1.State, Is.EqualTo(ChannelState.TransportConnectedSessionReactivating));
                    Assert.That(participant1.NotificationCount, Is.EqualTo(1));
                    Assert.That(participant2.NotificationCount, Is.EqualTo(1));
                });

                fakeTime.Advance(TimeSpan.FromTicks(1));
                await reconnectTask.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);

                ManagedChannelDiagnostic diagnostic = GetDiagnostic(environment.Manager, ch1.Key);
                Assert.Multiple(() =>
                {
                    Assert.That(fakeTime.GetUtcNow() - fakeTime.Start, Is.EqualTo(participantTimeout));
                    Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(ch1.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(ch2.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(participant1.NotificationCount, Is.EqualTo(2));
                    Assert.That(participant2.NotificationCount, Is.EqualTo(2));
                    Assert.That(participant1.LastAttempt, Is.EqualTo(1));
                    Assert.That(participant2.LastAttempt, Is.EqualTo(1));
                    Assert.That(releaseParticipant.Task.IsCompleted, Is.False);
                });
            }
            finally
            {
                releaseParticipant.TrySetResult(true);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("A single hung participant transitions out of reactivation after the bounded timeout.")]
        public async Task HungParticipantTimesOutAfterBoundedWaitAsync(CancellationToken ct)
        {
            var participantTimeout = TimeSpan.FromMilliseconds(200);
            var fakeTime = new FakeTimeProvider();
            TimeProvider timeProvider = ObserveParticipantTimeout(
                fakeTime,
                participantTimeout,
                out Task timeoutScheduled);
            using Certificate applicationCertificate = CreateCertificate("hung-participant-bounded-wait");
            ContractTestEnvironment environment = CreateEnvironment(
                applicationCertificate,
                reconnectPolicy: CreateParticipantTimeoutPolicy(participantTimeout, maxAttempts: 1),
                timeProvider: timeProvider);
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("hung-participant-bounded-wait");
            var participant = new FakeParticipant(endpoint);
            var releaseParticipant = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            participant.ConfigureOnReconnect(async (attempt, reconnectCt) =>
            {
                if (attempt == 0)
                {
                    await releaseParticipant.Task.WaitAsync(reconnectCt).ConfigureAwait(false);
                }

                return ParticipantReconnectResult.Reactivated;
            });

            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);

            try
            {
                Task reconnectTask = environment.Manager.ReconnectAsync(channel, ct).AsTask();
                await timeoutScheduled.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);

                fakeTime.Advance(participantTimeout - TimeSpan.FromTicks(1));
                Assert.Multiple(() =>
                {
                    Assert.That(reconnectTask.IsCompleted, Is.False);
                    Assert.That(channel.State, Is.EqualTo(ChannelState.TransportConnectedSessionReactivating));
                    Assert.That(participant.NotificationCount, Is.EqualTo(1));
                });

                fakeTime.Advance(TimeSpan.FromTicks(1));
                await reconnectTask.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);

                ManagedChannelDiagnostic diagnostic = GetDiagnostic(environment.Manager, channel.Key);
                Assert.Multiple(() =>
                {
                    Assert.That(fakeTime.GetUtcNow() - fakeTime.Start, Is.EqualTo(participantTimeout));
                    Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Faulted));
                    Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
                    Assert.That(participant.NotificationCount, Is.EqualTo(2));
                    Assert.That(participant.LastAttempt, Is.EqualTo(-1));
                    Assert.That(releaseParticipant.Task.IsCompleted, Is.False);
                });
            }
            finally
            {
                releaseParticipant.TrySetResult(true);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("Participant timeout does not produce false positives for callbacks that complete in time.")]
        public async Task BoundedParticipantTimeoutHonorsTimeoutAsync(CancellationToken ct)
        {
            var participantTimeout = TimeSpan.FromSeconds(5);
            var fakeTime = new FakeTimeProvider();
            TimeProvider timeProvider = ObserveParticipantTimeout(
                fakeTime,
                participantTimeout,
                out Task timeoutScheduled);
            using Certificate applicationCertificate = CreateCertificate("participant-timeout-positive");
            ContractTestEnvironment environment = CreateEnvironment(
                applicationCertificate,
                reconnectPolicy: CreateParticipantTimeoutPolicy(participantTimeout, maxAttempts: 1),
                timeProvider: timeProvider);
            await using ConfiguredAsyncDisposable environmentAsyncDisposable = environment.ConfigureAwait(false);
            ConfiguredEndpoint endpoint = CreateEndpoint("participant-timeout-positive");
            var participant = new FakeParticipant(endpoint);
            var releaseParticipant = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            participant.ConfigureOnReconnect(async (_, reconnectCt) =>
            {
                await releaseParticipant.Task.WaitAsync(reconnectCt).ConfigureAwait(false);
                return ParticipantReconnectResult.Reactivated;
            });

            IManagedTransportChannel channel = await environment.Manager.GetAsync(participant, ct)
                .ConfigureAwait(false);

            try
            {
                Task reconnectTask = environment.Manager.ReconnectAsync(channel, ct).AsTask();
                await timeoutScheduled.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);
                fakeTime.Advance(participantTimeout - TimeSpan.FromTicks(1));

                Assert.That(reconnectTask.IsCompleted, Is.False);
                releaseParticipant.TrySetResult(true);
                await reconnectTask.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(participant.NotificationCount, Is.EqualTo(1));
                    Assert.That(participant.LastAttempt, Is.Zero);
                });

                fakeTime.Advance(TimeSpan.FromTicks(1));
                Assert.Multiple(() =>
                {
                    Assert.That(fakeTime.GetUtcNow() - fakeTime.Start, Is.EqualTo(participantTimeout));
                    Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(participant.NotificationCount, Is.EqualTo(1));
                });
            }
            finally
            {
                releaseParticipant.TrySetResult(true);
            }
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

            using var ctsA = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task taskA = IgnoreOperationCanceledAsync(
                environment.Manager.ReconnectAsync(chA, ctsA.Token).AsTask());
            try
            {
                await WaitForHungReconnectAsync(environment.Manager, chA.Key, hungParticipant, ct)
                    .ConfigureAwait(false);
                await environment.Manager.ReconnectAsync(chB, ct).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(taskA.IsCompleted, Is.False);
                    Assert.That(chB.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(normalParticipant.NotificationCount, Is.EqualTo(1));
                });
            }
            finally
            {
                await ctsA.CancelAsync().ConfigureAwait(false);
                await taskA.WaitAsync(AssertionTimeout, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static FakeParticipant CreateParticipant(ConfiguredEndpoint endpoint)
        {
            return new FakeParticipant(endpoint);
        }

        private static TimeProvider ObserveParticipantTimeout(
            FakeTimeProvider fakeTime,
            TimeSpan participantTimeout,
            out Task timeoutScheduled)
        {
            var timerCreated = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var timeProvider = new Mock<TimeProvider>(MockBehavior.Strict);
            timeProvider.Setup(provider => provider.GetUtcNow()).Returns(fakeTime.GetUtcNow);
            timeProvider.Setup(provider => provider.GetTimestamp()).Returns(fakeTime.GetTimestamp);
            timeProvider.SetupGet(provider => provider.TimestampFrequency).Returns(fakeTime.TimestampFrequency);
            timeProvider.Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                {
                    ITimer timer = fakeTime.CreateTimer(callback, state, dueTime, period);
                    if (dueTime == participantTimeout)
                    {
                        timerCreated.TrySetResult(true);
                    }
                    return timer;
                });

            timeoutScheduled = timerCreated.Task;
            return timeProvider.Object;
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
