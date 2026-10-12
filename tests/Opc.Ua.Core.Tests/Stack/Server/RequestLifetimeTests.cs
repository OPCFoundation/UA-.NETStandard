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

        [TestCase(0)]
        [TestCase(1)]
        public void TimeoutConstructorIsCancelledByEitherToken(int cancelled)
        {
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(TimeSpan.Zero, first.Token, second.Token);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
            (cancelled == 0 ? first : second).Cancel();

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void TimeoutConstructorIsCancelledWhenTheTimeoutElapses()
        {
            using var lifetime = new RequestLifetime(
                TimeSpan.FromMilliseconds(20), CancellationToken.None, CancellationToken.None);

            Assert.That(
                lifetime.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void TimeoutConstructorWithoutTimeoutOrTokensIsOnlyCancelledExplicitly()
        {
            using var first = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(Timeout.InfiniteTimeSpan, first.Token, first.Token);
            using var unlinked = new RequestLifetime(TimeSpan.Zero, default, default);

            Assert.That(lifetime.CancellationToken.WaitHandle.WaitOne(50), Is.False);
            first.Cancel();
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);

            Assert.That(unlinked.CancellationToken.CanBeCanceled, Is.True);
            Assert.That(unlinked.TryCancel(StatusCodes.BadRequestCancelledByClient), Is.True);
            Assert.That(unlinked.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
        }

        [Test]
        public void TimeoutConstructorRejectsNegativeTimeouts()
        {
            Assert.That(
                () => new RequestLifetime(TimeSpan.FromMilliseconds(-2), default, default),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

#if NET
        /// <summary>
        /// One token source replaces the token array, the timeout source and its registration
        /// that the params constructor needs for the same three inputs.
        /// </summary>
        [Test]
        public void TimeoutConstructorAllocatesLessThanLinkingATimeoutSource()
        {
            using var request = new CancellationTokenSource();
            using var transport = new CancellationTokenSource();
            var timeout = TimeSpan.FromMinutes(5);
            for (int ii = 0; ii < 2; ii++)
            {
                using var warm = new RequestLifetime(timeout, request.Token, transport.Token);
                using var warmTimeout = new CancellationTokenSource(timeout);
                using var warmOld = new RequestLifetime(request.Token, transport.Token, warmTimeout.Token);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            using (var timeoutSource = new CancellationTokenSource(timeout))
            using (new RequestLifetime(request.Token, transport.Token, timeoutSource.Token))
            {
            }
            long linked = GC.GetAllocatedBytesForCurrentThread() - before;

            before = GC.GetAllocatedBytesForCurrentThread();
            using (new RequestLifetime(timeout, request.Token, transport.Token))
            {
            }
            long single = GC.GetAllocatedBytesForCurrentThread() - before;

            TestContext.Out.WriteLine($"params + timeout source: {linked} B, timeout constructor: {single} B");
            Assert.That(single, Is.LessThan(linked));
        }
#endif
    }
}
