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
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RequestLifetimeTests
    {
        [Test]
        public void Constructor_Default_CreatesInstance()
        {
            using var lifetime = new RequestLifetime();

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void Constructor_WithExternalToken_CreatesLinkedTokenSource()
        {
            using var cts = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(cts.Token);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);

            cts.Cancel();

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void TryCancel_WhenNotCancelled_ReturnsTrue_SetsStatusCode_CancelsToken()
        {
            using var lifetime = new RequestLifetime();

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.True);
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void TryCancel_WhenAlreadyCancelled_ReturnsFalse_LeavesStatusCode()
        {
            using var lifetime = new RequestLifetime();

            bool firstResult = lifetime.TryCancel(StatusCodes.BadTimeout);
            bool secondResult = lifetime.TryCancel(StatusCodes.BadUnexpectedError);

            Assert.That(firstResult, Is.True);
            Assert.That(secondResult, Is.False);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void TryCancelConcurrentCallersFirstStatusWins()
        {
            // a client Cancel, a Session close and the request timeout race to cancel the same
            // request; exactly one of them may claim it, and its status must be the one reported.
            for (int iteration = 0; iteration < 2000; iteration++)
            {
                using var lifetime = new RequestLifetime();
                using var barrier = new Barrier(2);
                bool timeoutWon = false;
                bool cancelWon = false;

                var timeout = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    timeoutWon = lifetime.TryCancel(StatusCodes.BadTimeout);
                });
                var cancel = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    cancelWon = lifetime.TryCancel(StatusCodes.BadRequestCancelledByClient);
                });
                timeout.Start();
                cancel.Start();
                timeout.Join();
                cancel.Join();

                Assert.That(timeoutWon ^ cancelWon, Is.True, $"Iteration {iteration}: exactly one caller must win.");
                Assert.That(
                    lifetime.StatusCode,
                    Is.EqualTo(timeoutWon ? StatusCodes.BadTimeout : StatusCodes.BadRequestCancelledByClient),
                    $"Iteration {iteration}: the winner's status must be reported.");
            }
        }

        [Test]
        public void TryCancel_AfterDispose_ReturnsFalse()
        {
            var lifetime = new RequestLifetime();
            lifetime.Dispose();

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.False);
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public void MarkCompleted_DisposesToken_CannotCancel()
        {
            var lifetime = new RequestLifetime();
            lifetime.MarkCompleted();

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.False);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public void None_Property_ReturnsCompletedInstance()
        {
            RequestLifetime lifetime = RequestLifetime.None;

            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.False);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        [NonParallelizable]
        public async Task ConcurrentCancellationHasOneTerminalOwnerAsync(bool completeInsteadOfCancel)
        {
            await AssertTerminalRaceAsync(
                completeInsteadOfCancel ? CompetingTransition.Complete : CompetingTransition.Cancel)
                .ConfigureAwait(false);
        }

        [Test]
        [NonParallelizable]
        public async Task ExternalCancellationHasOneTerminalOwnerAsync()
        {
            await AssertTerminalRaceAsync(CompetingTransition.ExternalCancel).ConfigureAwait(false);
        }

        [Test]
        public void CompletedLifetimeReleasesLinksWithoutOwningExternalSources()
        {
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(first.Token, second.Token);
            lifetime.MarkCompleted();

            first.Cancel();
            second.Cancel();

            Assert.That(first.IsCancellationRequested, Is.True);
            Assert.That(second.IsCancellationRequested, Is.True);
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void AlreadyCancelledExternalTokenKeepsGoodTerminalStatus()
        {
            using var external = new CancellationTokenSource();
            external.Cancel();
            using var lifetime = new RequestLifetime(external.Token);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(lifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [Test]
        public async Task ExternalCancellationCanCompleteInsideItsCallbackAsync()
        {
            using var external = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(external.Token);
            int calls = 0;
            using CancellationTokenRegistration registration = lifetime.CancellationToken.Register(() =>
            {
                Interlocked.Increment(ref calls);
                lifetime.MarkCompleted();
            });

            await Task.Run(external.Cancel).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(lifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancellationCallbackCanCompleteWithoutReplacingItsWinningStatus(bool dispose)
        {
            using var lifetime = new RequestLifetime();
            bool nestedCancellation = true;
            StatusCode observed = StatusCodes.Good;
            using CancellationTokenRegistration registration = lifetime.CancellationToken.Register(() =>
            {
                observed = lifetime.StatusCode;
                nestedCancellation = lifetime.TryCancel(StatusCodes.BadUnexpectedError);
                if (dispose)
                {
                    lifetime.Dispose();
                }
                else
                {
                    lifetime.MarkCompleted();
                }
            });

            Assert.That(lifetime.TryCancel(StatusCodes.BadTimeout), Is.True);
            Assert.That(nestedCancellation, Is.False);
            Assert.That(observed, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(lifetime.TryCancel(StatusCodes.BadUnexpectedError), Is.False);
        }

        private static async Task AssertTerminalRaceAsync(CompetingTransition competingTransition)
        {
            const int attempts = 16_384;
            var lifetimes = new RequestLifetime[attempts];
            var externalSources = new CancellationTokenSource[attempts];
            var firstWon = new bool[attempts];
            var secondWon = new bool[attempts];
            var observedStatus = new StatusCode[attempts];
            var callbackCounts = new int[attempts];
            using var barrier = new Barrier(2);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (int i = 0; i < attempts; i++)
            {
                int index = i;
                if (competingTransition == CompetingTransition.ExternalCancel)
                {
                    externalSources[i] = new CancellationTokenSource();
                    lifetimes[i] = new RequestLifetime(externalSources[i].Token);
                }
                else
                {
                    lifetimes[i] = new RequestLifetime();
                }
                lifetimes[i].CancellationToken.Register(() =>
                {
                    observedStatus[index] = lifetimes[index].StatusCode;
                    Interlocked.Increment(ref callbackCounts[index]);
                });
            }

            try
            {
                Task first = Task.Factory.StartNew(() =>
                {
                    for (int i = 0; i < attempts; i++)
                    {
                        barrier.SignalAndWait(timeout.Token);
                        firstWon[i] = lifetimes[i].TryCancel(StatusCodes.BadTimeout);
                        barrier.SignalAndWait(timeout.Token);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Task second = Task.Factory.StartNew(() =>
                {
                    for (int i = 0; i < attempts; i++)
                    {
                        barrier.SignalAndWait(timeout.Token);
                        if (competingTransition == CompetingTransition.Complete)
                        {
                            lifetimes[i].MarkCompleted();
                        }
                        else if (competingTransition == CompetingTransition.ExternalCancel)
                        {
                            externalSources[i].Cancel();
                        }
                        else
                        {
                            secondWon[i] = lifetimes[i].TryCancel(StatusCodes.BadRequestCancelledByRequest);
                        }
                        barrier.SignalAndWait(timeout.Token);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                await Task.WhenAll(first, second).ConfigureAwait(false);

                int duplicateOwners = 0;
                int statusMismatches = 0;
                int callbackMismatches = 0;
                for (int i = 0; i < attempts; i++)
                {
                    int owners = (firstWon[i] ? 1 : 0) + (secondWon[i] ? 1 : 0);
                    if (owners > 1 || (competingTransition == CompetingTransition.Cancel && owners != 1))
                    {
                        duplicateOwners++;
                    }
                    StatusCode expected = firstWon[i]
                        ? StatusCodes.BadTimeout
                        : secondWon[i] ? StatusCodes.BadRequestCancelledByRequest : StatusCodes.Good;
                    int expectedCallbacks = competingTransition == CompetingTransition.ExternalCancel ? 1 : owners;
                    if (lifetimes[i].StatusCode != expected ||
                        lifetimes[i].CancellationToken.IsCancellationRequested != (expectedCallbacks == 1))
                    {
                        statusMismatches++;
                    }
                    if (callbackCounts[i] != expectedCallbacks ||
                        (expectedCallbacks == 1 && observedStatus[i] != expected))
                    {
                        callbackMismatches++;
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(duplicateOwners, Is.Zero, "Each request has one winning terminal transition.");
                    Assert.That(statusMismatches, Is.Zero, "Completion must not race a cancellation status overwrite.");
                    Assert.That(callbackMismatches, Is.Zero, "The single callback observes the winning status.");
                });
            }
            finally
            {
                foreach (RequestLifetime lifetime in lifetimes)
                {
                    lifetime.Dispose();
                }
                if (competingTransition == CompetingTransition.ExternalCancel)
                {
                    foreach (CancellationTokenSource source in externalSources)
                    {
                        source.Dispose();
                    }
                }
            }
        }

        private enum CompetingTransition
        {
            Cancel,
            Complete,
            ExternalCancel
        }
    }
}
