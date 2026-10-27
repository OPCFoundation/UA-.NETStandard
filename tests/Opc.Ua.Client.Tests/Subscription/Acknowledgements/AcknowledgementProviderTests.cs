/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers acknowledgement handles, the session acknowledgement configuration and the
    /// built-in confirmation provider.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class AcknowledgementProviderTests
    {
        private static readonly int[] s_bothDeferred = [0, 1];
        private static readonly int[] s_secondDeferred = [1];
        private static readonly int[] s_firstDeferred = [0];
        private static readonly uint[] s_firstTwo = [1u, 2u];

        [Test]
        public void HandlesCompareByLifetimeAndSequenceNumber()
        {
            object owner = new();
            var scope = new AcknowledgementScope(owner, 1, 7);
            var sameLifetime = new AcknowledgementHandle(scope, 3);
            var otherLifetime = new AcknowledgementHandle(new AcknowledgementScope(owner, 2, 7), 3);

            Assert.That(sameLifetime, Is.EqualTo(new AcknowledgementHandle(scope, 3)));
            bool equalByOperator = sameLifetime == new AcknowledgementHandle(scope, 3);
            Assert.That(equalByOperator, Is.True);
            Assert.That(sameLifetime, Is.Not.EqualTo(otherLifetime), "A reused id is another lifetime.");
            bool differentByOperator = sameLifetime != new AcknowledgementHandle(scope, 4);
            Assert.That(differentByOperator, Is.True);
            Assert.That(sameLifetime.GetHashCode(), Is.EqualTo(new AcknowledgementHandle(scope, 3).GetHashCode()));
            Assert.That(sameLifetime.SubscriptionId, Is.EqualTo(7u));
            Assert.That(sameLifetime.ToString(), Is.EqualTo("7:3"));
            Assert.That(sameLifetime.IsNull, Is.False);
            Assert.That(AcknowledgementHandle.Null.IsNull, Is.True);
            Assert.That(AcknowledgementHandle.Null.SubscriptionId, Is.Zero);
            Assert.That(AcknowledgementHandle.Null.ToString(), Is.EqualTo("(null)"));
            bool equalAsObject = sameLifetime.Equals((object)new AcknowledgementHandle(scope, 3));
            bool equalToOther = sameLifetime.Equals(new object());
            Assert.That(equalAsObject, Is.True);
            Assert.That(equalToOther, Is.False);
        }

        [Test]
        public void ScopeIsReusedWhileTheLifetimeIsCurrent()
        {
            object owner = new();
            AcknowledgementScope? cached = null;

            var first = AcknowledgementScope.GetOrCreate(ref cached, owner, 1, 7);
            var again = AcknowledgementScope.GetOrCreate(ref cached, owner, 1, 7);
            var recreated = AcknowledgementScope.GetOrCreate(ref cached, owner, 2, 7);
            var reassigned = AcknowledgementScope.GetOrCreate(ref cached, owner, 2, 9);

            Assert.That(again, Is.SameAs(first));
            Assert.That(recreated, Is.Not.SameAs(first));
            Assert.That(reassigned, Is.Not.SameAs(recreated));
            Assert.That(cached, Is.SameAs(reassigned));
        }

        [Test]
        public void AStaleLookupDoesNotReplaceTheScopeOfANewerLifetime()
        {
            object owner = new();
            AcknowledgementScope? cached = null;
            var current = AcknowledgementScope.GetOrCreate(ref cached, owner, 3, 7);

            var stale = AcknowledgementScope.GetOrCreate(ref cached, owner, 2, 7);

            Assert.That(cached, Is.SameAs(current), "The newer lifetime stays cached.");
            Assert.That(AcknowledgementScope.GetOrCreate(ref cached, owner, 3, 7), Is.SameAs(current));
            Assert.That(stale.Generation, Is.EqualTo(2));
        }

        [Test]
        public void HandlesOfTheSameLifetimeAreEqualAcrossScopeInstances()
        {
            object owner = new();
            var one = new AcknowledgementHandle(new AcknowledgementScope(owner, 1, 7), 3);
            var other = new AcknowledgementHandle(new AcknowledgementScope(owner, 1, 7), 3);
            var foreign = new AcknowledgementHandle(new AcknowledgementScope(new object(), 1, 7), 3);

            Assert.That(one, Is.EqualTo(other));
            Assert.That(one.GetHashCode(), Is.EqualTo(other.GetHashCode()));
            Assert.That(one, Is.Not.EqualTo(foreign), "Another subscription instance is another lifetime.");
            Assert.That(AcknowledgementHandle.Null.IsNull, Is.True);
            Assert.That(one, Is.Not.EqualTo(AcknowledgementHandle.Null));
        }

        [Test]
        public async Task OutcomesReportedAfterCompletionAreStillDeliveredAsync()
        {
            var dispatcher = new AcknowledgementOutcomeDispatcher(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var observer = new CollectingObserver();
            var handle = new AcknowledgementHandle(new AcknowledgementScope(new object(), 1, 7), 1);
            List<AcknowledgementOutcome> Outcomes(uint sequenceNumber) =>
            [
                new AcknowledgementOutcome(
                    new AcknowledgementHandle(handle.Scope!, sequenceNumber),
                    AcknowledgementDisposition.Abandoned,
                    StatusCodes.BadShutdown)
            ];

            dispatcher.Report(observer, Outcomes(1));
            await dispatcher.CompleteAsync().ConfigureAwait(false);
            dispatcher.Report(observer, Outcomes(2));
            await dispatcher.CompleteAsync().ConfigureAwait(false);
            dispatcher.Report(null, Outcomes(3));
            dispatcher.Report(observer, null);

            Assert.That(observer.Outcomes.Select(outcome => outcome.Handle.SequenceNumber), Is.EqualTo(s_firstTwo));
        }

        [Test]
        public void ConfirmationModeCannotBeCombinedWithAProvider()
        {
            Assert.That(
                () => new SessionAcknowledgements(
                    AcknowledgementMode.AfterConfirmation, new SendAllProvider(), null),
                Throws.ArgumentException);
        }

        [Test]
        public void ImmediateModeUsesTheApplicationProviders()
        {
            var provider = new SendAllProvider();
            var observer = new CollectingObserver();
            var acknowledgements = new SessionAcknowledgements(AcknowledgementMode.Immediate, provider, observer);

            Assert.That(acknowledgements.Provider, Is.SameAs(provider));
            Assert.That(acknowledgements.Observer, Is.SameAs(observer));
            Assert.That(acknowledgements.Mode, Is.EqualTo(AcknowledgementMode.Immediate));
            Assert.DoesNotThrow(() => acknowledgements.Confirm(AcknowledgementHandle.Null));
        }

        [Test]
        public async Task ConfirmationModeDefersUntilConfirmedAndForgetsEndedAcknowledgementsAsync()
        {
            var observer = new CollectingObserver();
            var acknowledgements = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            var scope = new AcknowledgementScope(new object(), 1, 7);
            var first = new AcknowledgementHandle(scope, 1);
            var second = new AcknowledgementHandle(scope, 2);
            ISubscriptionAcknowledgementProvider provider = acknowledgements.Provider!;
            var confirmations = (ConfirmedAcknowledgementProvider)provider;
            confirmations.Track(first);
            confirmations.Track(second);

            AcknowledgementDecision decision = await provider.DecideAsync(new[] { first, second }).ConfigureAwait(false);
            Assert.That(decision.Deferred.ToArray(), Is.EqualTo(s_bothDeferred), "Nothing is confirmed yet.");

            acknowledgements.Confirm(first);
            acknowledgements.Confirm(AcknowledgementHandle.Null);
            decision = await provider.DecideAsync(new[] { first, second }).ConfigureAwait(false);
            Assert.That(decision.Deferred.ToArray(), Is.EqualTo(s_secondDeferred));

            acknowledgements.Confirm(second);
            decision = await provider.DecideAsync(new[] { first, second }).ConfigureAwait(false);
            Assert.That(decision.Deferred.IsEmpty, Is.True);
            Assert.That(confirmations.PendingConfirmations, Is.EqualTo(2));

            await acknowledgements.Observer!.OnCompletedAsync(new[]
            {
                new AcknowledgementOutcome(first, AcknowledgementDisposition.Acknowledged, StatusCodes.Good)
            }).ConfigureAwait(false);
            Assert.That(confirmations.PendingConfirmations, Is.EqualTo(1));
            Assert.That(confirmations.TrackedAcknowledgements, Is.EqualTo(1));
            Assert.That(observer.Outcomes.Single().Handle, Is.EqualTo(first), "The application observer is told too.");
        }

        [Test]
        public async Task ConfirmationsOfEndedOrUntrackedAcknowledgementsHaveNoEffectAsync()
        {
            var acknowledgements = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, null);
            var confirmations = (ConfirmedAcknowledgementProvider)acknowledgements.Provider!;
            var scope = new AcknowledgementScope(new object(), 1, 7);
            var ended = new AcknowledgementHandle(scope, 1);
            var untracked = new AcknowledgementHandle(scope, 2);
            confirmations.Track(ended);

            await acknowledgements.Observer!.OnCompletedAsync(new[]
            {
                new AcknowledgementOutcome(ended, AcknowledgementDisposition.NotRetained,
                    StatusCodes.BadSequenceNumberUnknown)
            }).ConfigureAwait(false);
            acknowledgements.Confirm(ended);
            acknowledgements.Confirm(ended);
            acknowledgements.Confirm(untracked);

            Assert.That(confirmations.TrackedAcknowledgements, Is.Zero, "Late confirmations are not kept.");
            AcknowledgementDecision decision = await confirmations.DecideAsync(new[] { untracked })
                .ConfigureAwait(false);
            Assert.That(decision.Deferred.ToArray(), Is.EqualTo(s_firstDeferred),
                "An untracked acknowledgement is never approved.");
        }

        [Test]
        public void TrackingIsIdempotentAndKeepsTheConfirmation()
        {
            var acknowledgements = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, null);
            var confirmations = (ConfirmedAcknowledgementProvider)acknowledgements.Provider!;
            var handle = new AcknowledgementHandle(new AcknowledgementScope(new object(), 1, 7), 1);

            confirmations.Track(handle);
            acknowledgements.Confirm(handle);
            confirmations.Track(handle);
            confirmations.Track(AcknowledgementHandle.Null);

            Assert.That(confirmations.TrackedAcknowledgements, Is.EqualTo(1));
            Assert.That(confirmations.PendingConfirmations, Is.EqualTo(1));
        }

        [Test]
        public void SessionsExposeTheAcknowledgementFacet()
        {
            var plain = new Moq.Mock<ISession>();
            Assert.That(plain.Object.TryGetAcknowledgements(out ISessionAcknowledgements? none), Is.False);
            Assert.That(none, Is.Null);

            var session = new Moq.Mock<ISession>();
            Moq.Mock<ISessionAcknowledgements> facet = session.As<ISessionAcknowledgements>();
            facet.SetupGet(value => value.Mode).Returns(AcknowledgementMode.AfterConfirmation);
            Assert.That(session.Object.TryGetAcknowledgements(out ISessionAcknowledgements? found), Is.True);
            Assert.That(found!.Mode, Is.EqualTo(AcknowledgementMode.AfterConfirmation));
        }

        private sealed class SendAllProvider : ISubscriptionAcknowledgementProvider
        {
            public ValueTask<AcknowledgementDecision> DecideAsync(
                ArrayOf<AcknowledgementHandle> acknowledgements,
                System.Threading.CancellationToken ct = default)
            {
                return new ValueTask<AcknowledgementDecision>(AcknowledgementDecision.SendAll);
            }
        }

        private sealed class CollectingObserver : ISubscriptionAcknowledgementObserver
        {
            public System.Collections.Concurrent.ConcurrentQueue<AcknowledgementOutcome> Outcomes { get; } = new();

            public ValueTask OnCompletedAsync(
                ArrayOf<AcknowledgementOutcome> outcomes,
                System.Threading.CancellationToken ct = default)
            {
                foreach (AcknowledgementOutcome outcome in outcomes)
                {
                    Outcomes.Enqueue(outcome);
                }
                return default;
            }
        }
    }
}
