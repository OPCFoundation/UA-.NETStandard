// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for the server-signal-aware backoff of <see cref="ReconnectPolicy"/>.
    /// </summary>
    [TestFixture]
    [Category("RateLimiting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AdaptiveReconnectPolicyTests
    {
        private static ReconnectPolicy CreateDeterministicPolicy()
        {
            // Constant strategy with no jitter so delays are exact and assertable.
            return new ReconnectPolicy(new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                JitterFactor = 0.0
            });
        }

        private static TimeSpan? AdaptiveDelay(
            ReconnectPolicy policy,
            int attempt,
            StatusCode lastStatus,
            TimeSpan? serverRetryAfter)
        {
            // The default policy always reports adaptive behavior.
            Assert.That(
                policy.TryGetNextDelay(attempt, lastStatus, serverRetryAfter, out TimeSpan? delay),
                Is.True);
            return delay;
        }

        [Test]
        public void IsServerBusySignalClassifiesOverloadCodes()
        {
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadServerTooBusy), Is.True);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadTcpServerTooBusy), Is.True);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadTooManySessions), Is.True);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadTooManyOperations), Is.True);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadTooManyPublishRequests), Is.True);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadRequestTimeout), Is.True);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadTimeout), Is.True);
        }

        [Test]
        public void IsServerBusySignalIgnoresNonOverloadCodes()
        {
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.Good), Is.False);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadCertificateInvalid), Is.False);
            Assert.That(ReconnectPolicy.IsServerBusySignal(StatusCodes.BadIdentityTokenRejected), Is.False);
        }

        [Test]
        public void GoodStatusUsesBaseDelay()
        {
            ReconnectPolicy policy = CreateDeterministicPolicy();

            TimeSpan? delay = AdaptiveDelay(policy, 0, StatusCodes.Good, serverRetryAfter: null);

            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void ServerBusyStatusBacksOffMoreThanBaseDelay()
        {
            ReconnectPolicy policy = CreateDeterministicPolicy();

            TimeSpan? busy = AdaptiveDelay(policy, 0, StatusCodes.BadServerTooBusy, serverRetryAfter: null);

            // 1s base * 4 (ServerBusyBackoffMultiplier) = 4s, below the 30s cap.
            Assert.That(busy, Is.EqualTo(TimeSpan.FromSeconds(4)));
        }

        [Test]
        public void ServerBusyBackoffIsClampedToMaxDelay()
        {
            var policy = new ReconnectPolicy(new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(20),
                MaxDelay = TimeSpan.FromSeconds(30),
                JitterFactor = 0.0
            });

            TimeSpan? busy = AdaptiveDelay(policy, 0, StatusCodes.BadServerTooBusy, serverRetryAfter: null);

            // 20s * 4 = 80s, clamped to the 30s max delay.
            Assert.That(busy, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void ServerRetryAfterHintIsHonoredAsLowerBound()
        {
            ReconnectPolicy policy = CreateDeterministicPolicy();

            TimeSpan? delay = AdaptiveDelay(
                policy,
                0,
                StatusCodes.Good,
                serverRetryAfter: TimeSpan.FromSeconds(10));

            // Base 1s raised to the 10s hint.
            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void ServerRetryAfterHintIsClampedToMaxDelay()
        {
            ReconnectPolicy policy = CreateDeterministicPolicy();

            TimeSpan? delay = AdaptiveDelay(
                policy,
                0,
                StatusCodes.Good,
                serverRetryAfter: TimeSpan.FromSeconds(100));

            // 100s hint clamped to the 30s max delay.
            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void RetriesExhaustedReturnsNull()
        {
            var policy = new ReconnectPolicy(new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                JitterFactor = 0.0,
                MaxRetries = 2
            });

            Assert.That(
                AdaptiveDelay(policy, 2, StatusCodes.BadServerTooBusy, serverRetryAfter: null),
                Is.Null);
        }

        [Test]
        public void ParseServerRetryAfterReadsToken()
        {
            Assert.That(
                ReconnectPolicy.ParseServerRetryAfter("RetryAfterMs=2000"),
                Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void ParseServerRetryAfterReadsEmbeddedToken()
        {
            Assert.That(
                ReconnectPolicy.ParseServerRetryAfter(
                    "The server is too busy to establish a session. RetryAfterMs=1500."),
                Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
        }

        [Test]
        public void ParseServerRetryAfterReturnsNullWhenAbsent()
        {
            Assert.That(ReconnectPolicy.ParseServerRetryAfter(null), Is.Null);
            Assert.That(ReconnectPolicy.ParseServerRetryAfter(string.Empty), Is.Null);
            Assert.That(ReconnectPolicy.ParseServerRetryAfter("no hint here"), Is.Null);
        }

        [Test]
        public void ParseServerRetryAfterReturnsNullForZero()
        {
            Assert.That(ReconnectPolicy.ParseServerRetryAfter("RetryAfterMs=0"), Is.Null);
        }

        [Test]
        public void ParseServerRetryAfterCapsPathologicalHint()
        {
            // A hint larger than one day is clamped to one day (86_400_000 ms).
            Assert.That(
                ReconnectPolicy.ParseServerRetryAfter("RetryAfterMs=999999999999"),
                Is.EqualTo(TimeSpan.FromMilliseconds(86_400_000)));
        }
    }
}
