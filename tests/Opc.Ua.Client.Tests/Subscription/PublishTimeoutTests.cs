// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Verifies Publish budgets and acknowledgement results with explicitly ordered responses.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    [Category("SubscriptionManager")]
    public sealed class PublishTimeoutTests
    {
        /// <summary>
        /// Every initial worker budgets all queued keepalives and an explicit scheduling margin.
        /// </summary>
        [TestCase(1, 100, 10u, 2000u)]
        [TestCase(2, 100, 10u, 3000u)]
        [TestCase(5, 100, 10u, 6000u)]
        [TestCase(2, 1, 1u, 1002u)]
        [TestCase(2, 0, 10u, 1000u)]
        [TestCase(2, 0.1, 1u, 1001u)]
        [TestCase(2, 3_600_000, uint.MaxValue, 1_800_000u)]
        [TestCase(15, 600_000, 1u, 1_800_000u)]
        [CancelAfter(10_000)]
        public async Task InitialBudgetIncludesConcurrencyAndMarginAsync(
            int workers, double intervalMs, uint keepAliveCount, uint expected, CancellationToken ct)
        {
            await using var harness = new PublishHarness(workers, intervalMs, keepAliveCount);
            harness.Manager.Resume();

            for (int i = 0; i < workers; i++)
            {
                PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
                Assert.That(attempt.TimeoutHint, Is.EqualTo(expected));
            }
            Assert.That(harness.Manager.PublishWorkerCount, Is.EqualTo(workers));
        }

        /// <summary>
        /// Existing workers refresh their budget when the pool grows or the server revises settings.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task BudgetTracksPoolGrowthAndRevisedKeepAliveAsync(CancellationToken ct)
        {
            await using var harness = new PublishHarness();
            harness.Manager.Resume();
            PublishAttempt first = await harness.NextAsync(ct).ConfigureAwait(false);
            _ = await harness.NextAsync(ct).ConfigureAwait(false);

            harness.Manager.MaxPublishWorkerCount = 4;
            harness.Manager.MinPublishWorkerCount = 4;
            for (int i = 0; i < 2; i++)
            {
                PublishAttempt added = await harness.NextAsync(ct).ConfigureAwait(false);
                Assert.That(added.TimeoutHint, Is.EqualTo(5000u));
            }

            first.Succeed();
            PublishAttempt replacement = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(replacement.TimeoutHint, Is.EqualTo(5000u));

            harness.Subscription.CurrentPublishingInterval = TimeSpan.FromMilliseconds(500);
            replacement.Succeed();
            PublishAttempt revised = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(revised.TimeoutHint, Is.EqualTo(25_000u));

            harness.Subscription.CurrentPublishingInterval = TimeSpan.FromMilliseconds(100);
            revised.Succeed();
            PublishAttempt shorter = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(shorter.TimeoutHint, Is.EqualTo(25_000u), "A worker retains its larger budget.");
        }

        /// <summary>
        /// A policy callback may revise settings after the worker calculated its acknowledgement wait.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task BudgetIsRefreshedAfterAcknowledgementPreparationAsync(CancellationToken ct)
        {
            await using var harness = new PublishHarness(workers: 1);
            harness.Context.OnPrepareAcknowledgements = acknowledgements =>
            {
                harness.Subscription.CurrentPublishingInterval = TimeSpan.FromMilliseconds(500);
                return (acknowledgements, []);
            };
            await harness.QueueAcknowledgementAsync(ct).ConfigureAwait(false);
            harness.Manager.Resume();

            PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(attempt.Acknowledgements.Count, Is.EqualTo(1));
            Assert.That(attempt.TimeoutHint, Is.EqualTo(10_000u));
        }

        /// <summary>
        /// An acknowledgement already processed by the server still needs its queued response
        /// to survive two keepalive periods and dispatch jitter.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task QuietAcknowledgementResponseSurvivesOldDeadlineAsync(CancellationToken ct)
        {
            await using var harness = new PublishHarness();
            await harness.QueueAcknowledgementAsync(ct).ConfigureAwait(false);
            harness.Manager.Resume();
            PublishAttempt first = await harness.NextAsync(ct).ConfigureAwait(false);
            PublishAttempt second = await harness.NextAsync(ct).ConfigureAwait(false);
            PublishAttempt acknowledgement = first.Acknowledgements.Count == 1 ? first : second;
            PublishAttempt keepAlive = first.Acknowledgements.Count == 0 ? first : second;
            Assert.That(acknowledgement.Acknowledgements.Count, Is.EqualTo(1));
            Assert.That(keepAlive.Acknowledgements.Count, Is.Zero);

            harness.Clock.Advance(TimeSpan.FromSeconds(1));
            keepAlive.Succeed(moreNotifications: false);
            _ = await harness.NextAsync(ct).ConfigureAwait(false);

            harness.Clock.Advance(TimeSpan.FromMilliseconds(1025));
            Assert.That(acknowledgement.Completion.Task.IsCompleted, Is.False,
                "The enclosing request deadline must allow the second keepalive plus scheduling headroom.");
            Assert.That(harness.Outcomes, Is.Empty, "Sending an acknowledgement is not confirmation.");

            acknowledgement.Succeed(moreNotifications: false);
            AcknowledgementOutcome outcome = await harness.NextOutcomeAsync(ct).ConfigureAwait(false);
            _ = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(outcome.Disposition, Is.EqualTo(AcknowledgementDisposition.Acknowledged));
            Assert.That(harness.Outcomes, Has.Count.EqualTo(1));
            Assert.That(harness.Manager.BadPublishRequestCount, Is.Zero);
            Assert.That(harness.Manager.PublishWorkerCount, Is.EqualTo(2));
        }

        /// <summary>
        /// An explicit negative acknowledgement result is not converted into success.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task UnretainedAcknowledgementRemainsUnsuccessfulAsync(CancellationToken ct)
        {
            await using var harness = new PublishHarness(workers: 1);
            await harness.QueueAcknowledgementAsync(ct).ConfigureAwait(false);
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
            attempt.Succeed(StatusCodes.BadSequenceNumberUnknown);

            AcknowledgementOutcome outcome = await harness.NextOutcomeAsync(ct).ConfigureAwait(false);
            Assert.That(outcome.Disposition, Is.EqualTo(AcknowledgementDisposition.NotRetained));
            Assert.That(harness.Outcomes, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A genuine deadline expiry does not invent a successful acknowledgement response.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task ExpiredRequestDoesNotConfirmAcknowledgementAsync(CancellationToken ct)
        {
            await using var harness = new PublishHarness(workers: 1);
            await harness.QueueAcknowledgementAsync(ct).ConfigureAwait(false);
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);

            harness.Clock.Advance(TimeSpan.FromMilliseconds(attempt.TimeoutHint));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await attempt.Completion.Task.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            harness.Manager.Pause();
            await harness.Manager.DrainAsync(ct).ConfigureAwait(false);
            Assert.That(harness.Outcomes, Is.Empty);
            Assert.That(harness.Manager.QueuedAcknowledgementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A larger Publish budget does not prevent quiescing or lose the cancelled batch.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task CancellationRequeuesAcknowledgementsWithoutSuccessAsync(CancellationToken ct)
        {
            await using var harness = new PublishHarness();
            await harness.QueueAcknowledgementAsync(ct).ConfigureAwait(false);
            harness.Manager.Resume();
            PublishAttempt first = await harness.NextAsync(ct).ConfigureAwait(false);
            PublishAttempt second = await harness.NextAsync(ct).ConfigureAwait(false);

            harness.Manager.Pause();
            await harness.Manager.DrainAsync(ct).ConfigureAwait(false);
            Assert.That(first.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(second.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(harness.Outcomes, Is.Empty);
            Assert.That(harness.Manager.QueuedAcknowledgementCount, Is.EqualTo(1));

            harness.Manager.Resume();
            first = await harness.NextAsync(ct).ConfigureAwait(false);
            second = await harness.NextAsync(ct).ConfigureAwait(false);
            PublishAttempt retry = first.Acknowledgements.Count == 1 ? first : second;
            Assert.That(retry.Acknowledgements.Count, Is.EqualTo(1));
            retry.Succeed();
            AcknowledgementOutcome outcome = await harness.NextOutcomeAsync(ct).ConfigureAwait(false);
            Assert.That(outcome.Disposition, Is.EqualTo(AcknowledgementDisposition.Acknowledged));
            Assert.That(harness.Outcomes, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Adaptive retries retain headroom without exceeding the maximum operation timeout.
        /// </summary>
        [TestCase(1000, false, 2000u, 3000u)]
        [TestCase(899750, false, 1_799_500u, 1_800_000u)]
        [TestCase(900000, true, 1_800_000u, 1_800_000u)]
        [CancelAfter(10_000)]
        public async Task AdaptiveRetrySaturatesAtMaximumAsync(
            double intervalMs, bool requestTimeout, uint initial, uint expected, CancellationToken ct)
        {
            await using var harness = new PublishHarness(1, intervalMs, 1, useSystemTime: true);
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(attempt.TimeoutHint, Is.EqualTo(initial));
            attempt.Fail(requestTimeout ? StatusCodes.BadRequestTimeout : StatusCodes.BadTimeout);

            PublishAttempt retry = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(retry.TimeoutHint, Is.EqualTo(expected));
            Assert.That(harness.Manager.BadPublishRequestCount, Is.EqualTo(1));
        }

        /// <summary>
        /// The hint stays within half the channel's operation timeout, so the server
        /// answers or expires a request before the client abandons it. Below that
        /// limit the budget is unchanged.
        /// </summary>
        [TestCase(5000, 1000, 5u, 2500u)]
        [TestCase(15_000, 1000, 10u, 7500u)]
        [TestCase(120_000, 100, 10u, 3000u)]
        [TestCase(0, 1000, 5u, 15_000u)]
        [CancelAfter(10_000)]
        public async Task TimeoutHintStaysWithinHalfTheOperationTimeoutAsync(
            int operationTimeout, double intervalMs, uint keepAliveCount, uint expected, CancellationToken ct)
        {
            await using var harness = new PublishHarness(
                2, intervalMs, keepAliveCount, operationTimeout: operationTimeout);
            harness.Manager.Resume();
            for (int i = 0; i < 2; i++)
            {
                PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
                Assert.That(attempt.TimeoutHint, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// A timed-out request does not raise the next hint beyond the operation timeout.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(10_000)]
        public async Task TimeoutRetryStaysWithinHalfTheOperationTimeoutAsync(bool requestTimeout, CancellationToken ct)
        {
            await using var harness = new PublishHarness(
                1, 1000, 5, useSystemTime: true, operationTimeout: 5000);
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(attempt.TimeoutHint, Is.EqualTo(2500u));
            attempt.Fail(requestTimeout ? StatusCodes.BadRequestTimeout : StatusCodes.BadTimeout);

            PublishAttempt retry = await harness.NextAsync(ct).ConfigureAwait(false);
            Assert.That(retry.TimeoutHint, Is.EqualTo(2500u));
        }

        /// <summary>
        /// With a keep-alive time beyond half the operation timeout, the server
        /// expires every queued Publish at its TimeoutHint with Bad_Timeout. That
        /// is the normal life of a quiet queue: the worker re-issues the Publish at
        /// once instead of backing off, so the server is never left without one.
        /// </summary>
        [Test]
        [CancelAfter(20_000)]
        public async Task ServerTimeoutOfQueuedPublishIsNotBackedOffAsync(CancellationToken ct)
        {
            // keep-alive 10 s x 10 = 100 s, clamped to 30 s by the 60 s operation timeout.
            await using var harness = new PublishHarness(1, 10_000, 10, operationTimeout: 60_000);
            harness.Manager.Resume();
            for (int i = 0; i < 4; i++)
            {
                PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
                Assert.That(attempt.TimeoutHint, Is.EqualTo(30_000u));

                // the server answers with Bad_Timeout when the hint passes.
                harness.Clock.Advance(TimeSpan.FromMilliseconds(attempt.TimeoutHint));
                Assert.That(attempt.Completion.Task.IsFaulted, Is.True);

                // the next Publish follows without any time passing.
                using var noTimePasses = CancellationTokenSource.CreateLinkedTokenSource(ct);
                noTimePasses.CancelAfter(TimeSpan.FromSeconds(3));
                PublishAttempt next = await harness.NextAsync(noTimePasses.Token).ConfigureAwait(false);
                next.Succeed();
            }
            Assert.That(harness.Manager.BadPublishRequestCount, Is.Zero);
        }

        /// <summary>
        /// A server that answers a Publish with Bad_Timeout right away is still
        /// backed off, so a broken server cannot make the worker spin.
        /// </summary>
        [Test]
        [CancelAfter(20_000)]
        public async Task ImmediateBadTimeoutIsStillBackedOffAsync(CancellationToken ct)
        {
            await using var harness = new PublishHarness(1, 10_000, 10, operationTimeout: 60_000);
            harness.Manager.Resume();
            PublishAttempt attempt = await harness.NextAsync(ct).ConfigureAwait(false);
            attempt.Fail(StatusCodes.BadTimeout);

            Task<PublishAttempt> next = harness.NextAsync(ct).AsTask();
            await Task.Delay(500, ct).ConfigureAwait(false);
            Assert.That(next.IsCompleted, Is.False, "The retry waits for the backoff.");
            harness.Clock.Advance(TimeSpan.FromSeconds(1));
            PublishAttempt retry = await next.ConfigureAwait(false);
            retry.Succeed();
            Assert.That(harness.Manager.BadPublishRequestCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Runs real Publish workers against controlled request deadlines and responses.
        /// </summary>
        private sealed class PublishHarness : IAsyncDisposable, ISubscriptionAcknowledgementObserver
        {
            /// <summary>
            /// Creates one quiet subscription and the requested worker pool.
            /// </summary>
            public PublishHarness(
                int workers = 2, double intervalMs = 100, uint keepAliveCount = 10, bool useSystemTime = false,
                int operationTimeout = 0)
            {
                Subscription = new FakeManagedSubscription
                {
                    Id = 7,
                    Created = true,
                    CurrentPublishingInterval = TimeSpan.FromTicks(
                        (long)(intervalMs * TimeSpan.TicksPerMillisecond)),
                    CurrentKeepAliveCount = keepAliveCount
                };
                m_timeProvider = useSystemTime ? TimeProvider.System : Clock;
                Context = new FakeSubscriptionManagerContext
                {
                    CreateSubscriptionFactory = (_, _, _) => Subscription,
                    OnPublishAsync = PublishAsync,
                    AcknowledgementObserver = this,
                    OperationTimeout = operationTimeout
                };
                Manager = new SubscriptionManager(
                    Context, NUnitTelemetryContext.Create().LoggerFactory, DiagnosticsMasks.None, m_timeProvider)
                {
                    MinPublishWorkerCount = workers,
                    MaxPublishWorkerCount = workers
                };
                Manager.Add(Mock.Of<ISubscriptionNotificationHandler>(),
                    new OptionsMonitor<SubscriptionOptions>(
                        new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }

            /// <summary>
            /// Gets the clock used to advance queued request deadlines.
            /// </summary>
            public FakeTimeProvider Clock { get; } = new();

            /// <summary>
            /// Gets the session callbacks.
            /// </summary>
            public FakeSubscriptionManagerContext Context { get; }

            /// <summary>
            /// Gets the manager under test.
            /// </summary>
            public SubscriptionManager Manager { get; }

            /// <summary>
            /// Gets the subscription with server-revised settings.
            /// </summary>
            public FakeManagedSubscription Subscription { get; }

            /// <summary>
            /// Gets all terminal acknowledgement outcomes.
            /// </summary>
            public ConcurrentQueue<AcknowledgementOutcome> Outcomes { get; } = new();

            /// <summary>
            /// Queues a retained message for acknowledgement.
            /// </summary>
            public ValueTask QueueAcknowledgementAsync(CancellationToken ct)
            {
                Manager.TrackAcknowledgement(Subscription, Subscription.MessageGeneration, 42);
                return Manager.QueueAsync(Subscription, Subscription.MessageGeneration,
                    new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 42 }, ct);
            }

            /// <summary>
            /// Waits until a request has armed its deadline and reached the server.
            /// </summary>
            public ValueTask<PublishAttempt> NextAsync(CancellationToken ct)
            {
                return m_attempts.Reader.ReadAsync(ct);
            }

            /// <summary>
            /// Waits for acknowledgement completion.
            /// </summary>
            public ValueTask<AcknowledgementOutcome> NextOutcomeAsync(CancellationToken ct)
            {
                return m_outcomes.Reader.ReadAsync(ct);
            }

            /// <inheritdoc/>
            public ValueTask OnCompletedAsync(ArrayOf<AcknowledgementOutcome> outcomes, CancellationToken ct = default)
            {
                foreach (AcknowledgementOutcome outcome in outcomes)
                {
                    Outcomes.Enqueue(outcome);
                    if (!m_outcomes.Writer.TryWrite(outcome))
                    {
                        throw new InvalidOperationException("The acknowledgement channel is closed.");
                    }
                }
                return default;
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return Manager.DisposeAsync();
            }

            private async ValueTask<PublishResponse> PublishAsync(
                RequestHeader? header, ArrayOf<SubscriptionAcknowledgement> acknowledgements, CancellationToken ct)
            {
                var attempt = new PublishAttempt(header!.TimeoutHint, acknowledgements, ct);
                using ITimer timer = m_timeProvider.CreateTimer(
                    _ => attempt.Fail(StatusCodes.BadTimeout), null,
                    TimeSpan.FromMilliseconds(attempt.TimeoutHint), Timeout.InfiniteTimeSpan);
                await m_attempts.Writer.WriteAsync(attempt, ct).ConfigureAwait(false);
                return await attempt.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            private readonly TimeProvider m_timeProvider;
            private readonly Channel<PublishAttempt> m_attempts = Channel.CreateUnbounded<PublishAttempt>();

            private readonly Channel<AcknowledgementOutcome> m_outcomes =
                Channel.CreateUnbounded<AcknowledgementOutcome>();
        }

        /// <summary>
        /// A request whose deadline or response can be completed independently of other workers.
        /// </summary>
        private sealed class PublishAttempt(
            uint timeoutHint, ArrayOf<SubscriptionAcknowledgement> acknowledgements, CancellationToken ct)
        {
            /// <summary>
            /// Gets the actual wire budget.
            /// </summary>
            public uint TimeoutHint { get; } = timeoutHint;

            /// <summary>
            /// Gets the acknowledgement batch already accepted by the server.
            /// </summary>
            public ArrayOf<SubscriptionAcknowledgement> Acknowledgements { get; } = acknowledgements;

            /// <summary>
            /// Gets cancellation from the worker or a publishing drain.
            /// </summary>
            public CancellationToken CancellationToken { get; } = ct;

            /// <summary>
            /// Gets the controlled response completion.
            /// </summary>
            public TaskCompletionSource<PublishResponse> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Completes a keepalive response with per-acknowledgement results.
            /// </summary>
            public void Succeed(StatusCode acknowledgementStatus = default, bool moreNotifications = true)
            {
                Completion.SetResult(new PublishResponse
                {
                    SubscriptionId = 7,
                    NotificationMessage = new NotificationMessage { SequenceNumber = 43 },
                    AvailableSequenceNumbers = [],
                    Results = Acknowledgements.ConvertAll(_ => acknowledgementStatus),
                    MoreNotifications = moreNotifications,
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            }

            /// <summary>
            /// Fails the request without delivering acknowledgement results.
            /// </summary>
            public void Fail(StatusCode status)
            {
                Completion.TrySetException(new ServiceResultException(status));
            }
        }
    }
}
