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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Controls concurrent Publish completions to verify recovery notification ordering.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class PublishRecoverySignalingTests
    {
        /// <summary>
        /// Recovery may drain publishing as soon as the notification is delivered.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task PublishFailureRollsBackBeforeSignalingRecoveryAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness();
            await harness.Manager.QueueAsync(
                harness.Subscription, harness.Subscription.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 42 },
                testCt).ConfigureAwait(false);
            int queuedAtNotification = 0;
            bool drainedAtNotification = false;
            harness.OnRecovery = () =>
            {
                harness.Manager.Pause();
                queuedAtNotification = harness.Manager.QueuedAcknowledgementCount;
                drainedAtNotification = harness.Manager.DrainAsync(testCt).Status == TaskStatus.RanToCompletion;
            };
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(attempt.Acknowledgements, Has.Count.EqualTo(1));

            attempt.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(queuedAtNotification, Is.EqualTo(1));
                Assert.That(drainedAtNotification, Is.True,
                    "The notifying worker must not be part of the drain it triggers.");
            });
        }

        /// <summary>
        /// The recovery owner can cancel and drain other in-flight workers without joining the notifier.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task PublishRecoveryCanDrainOtherWorkersAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness(workerCount: 2);
            Task? drain = null;
            harness.OnRecovery = () =>
            {
                harness.Manager.Pause();
                drain = harness.Manager.DrainAsync(testCt);
            };
            harness.Manager.Resume();
            PublishAttempt failure = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            failure.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            Assert.That(drain, Is.Not.Null);
            await drain!.WaitAsync(testCt).ConfigureAwait(false);

            harness.Manager.Resume();
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Concurrent failures share one notification; retired attempts cannot start another recovery.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task PublishFailuresCoalesceAndIgnoreRetiredEpochAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness(workerCount: 2);
            harness.Manager.Resume();
            PublishAttempt first = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            PublishAttempt second = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            first.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            second.Fail(StatusCodes.BadConnectionClosed);
            PublishAttempt retiredFirst = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            PublishAttempt retiredSecond = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(1));

            harness.Manager.Pause();
            harness.Manager.Resume();
            retiredFirst.Fail(StatusCodes.BadSecureChannelClosed);
            retiredSecond.Fail(StatusCodes.BadSecureChannelClosed);
            PublishAttempt currentFirst = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(1),
                "Failures from before pause/resume must not report a new outage.");

            currentFirst.Fail(StatusCodes.BadNotConnected);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(2),
                "An independent outage after recovery must be reported.");
        }

        /// <summary>
        /// A success already in flight at failure time cannot clear the outage notification.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task LateSuccessfulPublishDoesNotRearmRecoveryAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness(workerCount: 2);
            harness.Manager.Resume();
            PublishAttempt first = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            PublishAttempt oldSuccess = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            first.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            PublishAttempt retry = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            oldSuccess.Succeed();
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            retry.Fail(StatusCodes.BadSecureChannelClosed);
            PublishAttempt healthy = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(1));

            healthy.Succeed();
            PublishAttempt nextOutage = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            nextOutage.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(2),
                "A successful retry must rearm recovery even without a pause/resume.");
        }

        /// <summary>
        /// A failure from an attempt begun before the reported outage cannot report it again,
        /// even after a newer success has rearmed reporting in the same epoch.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task DelayedFailureAfterRearmDoesNotReportRecoveredOutageAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness(workerCount: 2);
            harness.Manager.Resume();
            PublishAttempt first = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            PublishAttempt delayed = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            first.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            PublishAttempt retry = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            retry.Succeed();
            PublishAttempt healthy = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            delayed.Fail(StatusCodes.BadConnectionClosed);
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(1),
                "A delayed failure from the recovered outage must not report it again.");

            healthy.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(2),
                "A failure from an attempt begun after the rearming success must still be reported.");
        }

        /// <summary>
        /// A successful retry retires failures from earlier retries, not only the original outage.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task DelayedRetryFailureAfterNewerSuccessDoesNotReportRecoveryAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness(workerCount: 2);
            harness.Manager.Resume();
            PublishAttempt first = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            PublishAttempt second = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            first.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            PublishAttempt delayedRetry = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            second.Fail(StatusCodes.BadConnectionClosed);
            PublishAttempt successfulRetry = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            successfulRetry.Succeed();
            PublishAttempt nextOutage = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            delayedRetry.Fail(StatusCodes.BadConnectionClosed);
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(1),
                "A retry older than the successful retry cannot report the recovered outage again.");

            nextOutage.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(2),
                "A request begun after the successful retry must report an independent outage.");
        }

        /// <summary>
        /// Session reactivation retires old attempts even when no Publish failure has reported an outage.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task ExternalReactivationRetiresUnreportedPublishFailureAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness(workerCount: 2);
            harness.Manager.Resume();
            PublishAttempt delayedFailure = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            PublishAttempt delayedSuccess = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            harness.Manager.OnSessionReactivated();
            delayedFailure.Fail(StatusCodes.BadConnectionClosed);
            PublishAttempt current = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Is.Empty,
                "Reactivation must retire old transport failures without relying on a prior recovery report.");

            current.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);
            PublishAttempt retry = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            delayedSuccess.Succeed();
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            retry.Fail(StatusCodes.BadConnectionClosed);
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(harness.Recoveries, Has.Count.EqualTo(1),
                "A retired transport success must not rearm the current transport's outage.");
        }

        /// <summary>
        /// Recovery ordering follows dispatch even when an older reservation waits for acknowledgement policy.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task DelayedAcknowledgementPreparationDoesNotRetireNewDispatchFailureAsync(CancellationToken testCt)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var decision = new TaskCompletionSource<AcknowledgementDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<ISubscriptionAcknowledgementProvider>();
            provider.Setup(value => value.DecideAsync(
                It.IsAny<ArrayOf<AcknowledgementHandle>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<AcknowledgementHandle> _, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    return await decision.Task.WaitAsync(ct).ConfigureAwait(false);
                });
            await using var harness = new PublishHarness(acknowledgementProvider: provider.Object);
            await harness.Manager.QueueAsync(
                harness.Subscription, harness.Subscription.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 42 },
                testCt).ConfigureAwait(false);
            harness.Manager.Resume();
            await entered.Task.WaitAsync(testCt).ConfigureAwait(false);
            harness.Manager.MinPublishWorkerCount = 2;
            harness.Manager.MaxPublishWorkerCount = 2;
            harness.Manager.Update();
            PublishAttempt overtaking = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            overtaking.Succeed();
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            decision.SetResult(default);
            PublishAttempt delayedDispatch = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            Assert.That(delayedDispatch.Acknowledgements, Has.Count.EqualTo(1));
            delayedDispatch.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.NextRecoveryAsync(testCt).ConfigureAwait(false);

            Assert.That(harness.Recoveries, Has.Count.EqualTo(1),
                "A request sent after a successful response must report its failure despite an older reservation.");
        }

        /// <summary>
        /// Ordinary retryable service errors and cancellation do not indicate a lost session.
        /// </summary>
        [TestCase("busy")]
        [TestCase("timeout")]
        [TestCase("cancel")]
        [CancelAfter(10_000)]
        public async Task OtherPublishFailuresDoNotSignalRecoveryAsync(string failure, CancellationToken testCt)
        {
            await using var harness = new PublishHarness();
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            if (failure == "cancel")
            {
                attempt.Cancel();
            }
            else
            {
                attempt.Fail(failure == "busy" ? StatusCodes.BadServerTooBusy : StatusCodes.BadTimeout);
            }
            _ = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);

            Assert.That(harness.Recoveries, Is.Empty);
        }

        /// <summary>
        /// Shutdown must not treat a final transport error as a fresh outage.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task PublishFailureDuringShutdownDoesNotSignalRecoveryAsync(CancellationToken testCt)
        {
            await using var harness = new PublishHarness();
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAttemptAsync(testCt).ConfigureAwait(false);
            harness.Manager.Pause();
            attempt.Fail(StatusCodes.BadSecureChannelClosed);
            await harness.Manager.DrainAsync(testCt).ConfigureAwait(false);
            await harness.Manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(harness.Recoveries, Is.Empty);
        }

        /// <summary>
        /// Supplies gated Publish attempts and records synchronous recovery notifications.
        /// </summary>
        private sealed class PublishHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates a paused manager with the specified number of workers.
            /// </summary>
            public PublishHarness(
                int workerCount = 1, ISubscriptionAcknowledgementProvider? acknowledgementProvider = null)
            {
                var context = new FakeSubscriptionManagerContext
                {
                    CreateSubscriptionFactory = (_, _, _) => Subscription,
                    OnPublishAsync = PublishAsync,
                    AcknowledgementProvider = acknowledgementProvider,
                    OnRecoveryError = (_, error) =>
                    {
                        OnRecovery?.Invoke();
                        Recoveries.Enqueue(error);
                        m_recoveries.Writer.TryWrite(error);
                    }
                };
                Manager = new SubscriptionManager(
                    context, NUnitTelemetryContext.Create().LoggerFactory, DiagnosticsMasks.None)
                {
                    MinPublishWorkerCount = workerCount,
                    MaxPublishWorkerCount = workerCount
                };
                Manager.Add(Mock.Of<ISubscriptionNotificationHandler>(),
                    new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions()));
            }

            /// <summary>
            /// Gets the manager under test.
            /// </summary>
            public SubscriptionManager Manager { get; }

            /// <summary>
            /// Gets the subscription whose acknowledgements are owned by this manager.
            /// </summary>
            public FakeManagedSubscription Subscription { get; } = new()
            {
                Id = 7,
                Created = true,
                CurrentPublishingInterval = TimeSpan.FromMilliseconds(10),
                CurrentKeepAliveCount = 100
            };

            /// <summary>
            /// Gets recovery notifications in arrival order.
            /// </summary>
            public ConcurrentQueue<ServiceResult> Recoveries { get; } = new();

            /// <summary>
            /// Gets or sets an action executed before a recovery notification is recorded.
            /// </summary>
            public Action? OnRecovery { get; set; }

            /// <summary>
            /// Waits until a worker sends another request.
            /// </summary>
            public ValueTask<PublishAttempt> NextAttemptAsync(CancellationToken ct)
            {
                return m_attempts.Reader.ReadAsync(ct);
            }

            /// <summary>
            /// Waits for the next recovery notification.
            /// </summary>
            public ValueTask<ServiceResult> NextRecoveryAsync(CancellationToken ct)
            {
                return m_recoveries.Reader.ReadAsync(ct);
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return Manager.DisposeAsync();
            }

            /// <summary>
            /// Blocks a real worker until the test completes or cancels its request.
            /// </summary>
            private async ValueTask<PublishResponse> PublishAsync(
                RequestHeader? header, ArrayOf<SubscriptionAcknowledgement> acknowledgements, CancellationToken ct)
            {
                var attempt = new PublishAttempt(acknowledgements);
                m_attempts.Writer.TryWrite(attempt);
                return await attempt.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            /// <summary>Requests waiting for completion by the test.</summary>
            private readonly Channel<PublishAttempt> m_attempts = Channel.CreateUnbounded<PublishAttempt>();

            /// <summary>Recovery notifications awaiting test assertions.</summary>
            private readonly Channel<ServiceResult> m_recoveries = Channel.CreateUnbounded<ServiceResult>();
        }

        /// <summary>
        /// One request that the test can complete independently of other workers.
        /// </summary>
        private sealed class PublishAttempt(ArrayOf<SubscriptionAcknowledgement> acknowledgements)
        {
            /// <summary>
            /// Gets the acknowledgements sent with this attempt.
            /// </summary>
            public ArrayOf<SubscriptionAcknowledgement> Acknowledgements { get; } = acknowledgements;

            /// <summary>
            /// Gets the completion supplied to the worker.
            /// </summary>
            public TaskCompletionSource<PublishResponse> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Fails this request with a service status.
            /// </summary>
            public void Fail(StatusCode statusCode)
            {
                Completion.SetException(new ServiceResultException(statusCode));
            }

            /// <summary>
            /// Cancels this attempt without cancelling the worker's lifetime.
            /// </summary>
            public void Cancel()
            {
                Completion.SetCanceled();
            }

            /// <summary>
            /// Completes this request with a valid keep-alive response.
            /// </summary>
            public void Succeed()
            {
                Completion.SetResult(new PublishResponse
                {
                    SubscriptionId = 7,
                    NotificationMessage = new NotificationMessage(),
                    AvailableSequenceNumbers = [],
                    Results = Acknowledgements.ConvertAll(_ => StatusCodes.Good),
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            }
        }
    }
}
