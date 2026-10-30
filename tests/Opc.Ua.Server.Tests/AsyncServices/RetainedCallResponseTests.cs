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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Pins whole-Call ownership independently of transport scheduling or an original Service implementation.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RetainedCallResponseTests
    {
        /// <summary>
        /// Empty, singleton and multi-result transfers retain every selection and the outer charge until reuse.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void WholeBatchTransfersOnlyOnceAndLivesUntilOuterResponseReuse(int count)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);
            var requestScope = new CountingLease();
            var delivery = new CountingLease();
            CountingLease[] outcomes = [.. Enumerable.Range(0, count).Select(static _ => new CountingLease())];
            context.AttachRequestScope(requestScope);
            foreach (CountingLease outcome in outcomes)
            {
                context.RetainResponseResource(outcome);
            }
            IDisposable? transferred = context.TakeResponseResources();
            Assert.That(transferred == null, Is.EqualTo(count == 0));
            Assert.That(context.TakeResponseResources(), Is.Null);
            var response = new RetainedCallResponse(transferred)
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 91, StringTable = ["retained"] },
                Results =
                [
                    new CallMethodResult { OutputArguments = [Variant.From(42)] },
                    new CallMethodResult { StatusCode = StatusCodes.BadInvalidArgument }
                ],
                DiagnosticInfos = [new DiagnosticInfo { SymbolicId = 0 }]
            };
            response.AttachRequestResources(delivery);
            try
            {
                context.Dispose();
                Assert.That(requestScope.Disposals, Is.EqualTo(1));
                Assert.That(outcomes.Select(static outcome => outcome.Disposals), Is.All.Zero);
                Assert.That(delivery.Disposals, Is.Zero);
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(91u));
                Assert.That(response.Results.Count, Is.EqualTo(2));
                Assert.That(response.Results[0].OutputArguments[0].TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(42));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.Zero);
            }
            finally
            {
                response.Reuse();
            }
            Assert.That(outcomes.Select(static outcome => outcome.Disposals), Is.All.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
            Assert.That(response.Results, Is.Empty);
            Assert.That(response.DiagnosticInfos, Is.Empty);
            Assert.That(response.ResponseHeader.RequestHandle, Is.Zero);
            response.Reuse();
            Assert.That(outcomes.Select(static outcome => outcome.Disposals), Is.All.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// A dispatch that fails or is cancelled before transfer leaves all selected results with its context.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FailedOrCancelledDispatchReleasesUntransferredBatch(bool cancel)
        {
            using var lifetime = new RequestLifetime();
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Call, lifetime);
            var first = new CountingLease();
            var second = new CountingLease();
            var requestScope = new CountingLease();
            var sessionless = new CountingLease();
            context.RetainResponseResource(first);
            context.RetainResponseResource(second);
            context.AttachRequestScope(requestScope);
            context.AttachSessionlessLease(sessionless);
            if (cancel)
            {
                Assert.That(lifetime.TryCancel(StatusCodes.BadRequestCancelledByClient), Is.True);
            }
            Assert.That(context.CancellationToken.IsCancellationRequested, Is.EqualTo(cancel));
            Assert.That(first.Disposals, Is.Zero);
            Assert.That(second.Disposals, Is.Zero);

            context.Dispose();
            context.Dispose();

            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(requestScope.Disposals, Is.EqualTo(1));
            Assert.That(sessionless.Disposals, Is.EqualTo(1));
            Assert.That(context.TakeResponseResources(), Is.Null);
        }

        /// <summary>
        /// Cancelling the completed exchange cannot revoke results already owned by the pending serializer.
        /// </summary>
        [Test]
        public void CancellationAfterTransferDoesNotReleaseTransportOwnedBatch()
        {
            using var lifetime = new RequestLifetime();
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Call, lifetime);
            var first = new CountingLease();
            var second = new CountingLease();
            var delivery = new CountingLease();
            context.RetainResponseResource(first);
            context.RetainResponseResource(second);
            var response = new RetainedCallResponse(context.TakeResponseResources());
            response.AttachRequestResources(delivery);
            try
            {
                Assert.That(lifetime.TryCancel(StatusCodes.BadRequestCancelledByClient), Is.True);
                context.Dispose();
                Assert.That(context.OperationStatus, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(context.CancellationToken.IsCancellationRequested, Is.True);
                Assert.That(first.Disposals, Is.Zero);
                Assert.That(second.Disposals, Is.Zero);
                Assert.That(delivery.Disposals, Is.Zero);
            }
            finally
            {
                response.Reuse();
            }
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Cleanup continues after either the first or an interior outcome fails, including the outer charge.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        public void ThrowingOutcomeStillReleasesEverySiblingAndOuterCharge(int failingIndex)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);
            var failure = new InvalidOperationException("Synthetic outcome disposal failure.");
            CountingLease[] outcomes =
            [
                new CountingLease(failingIndex == 0 ? failure : null),
                new CountingLease(failingIndex == 1 ? failure : null),
                new CountingLease()
            ];
            foreach (CountingLease outcome in outcomes)
            {
                context.RetainResponseResource(outcome);
            }
            var delivery = new CountingLease();
            var response = new RetainedCallResponse(context.TakeResponseResources());
            response.AttachRequestResources(delivery);
            context.Dispose();

            AggregateException error = Assert.Throws<AggregateException>(
                response.Reuse)!;

            Assert.That(error.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.SameAs(failure));
            Assert.That(outcomes.Select(static outcome => outcome.Disposals), Is.All.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
            Assert.DoesNotThrow(response.Reuse);
            Assert.That(outcomes.Select(static outcome => outcome.Disposals), Is.All.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Untransferred context cleanup continues across a failing request scope or result to the final lease.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ContextCleanupFailureStillReleasesRemainingSelectionsAndSessionlessLease(bool requestFailure)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);
            var failure = new InvalidOperationException("Synthetic selected-result failure.");
            var first = new CountingLease(requestFailure ? null : failure);
            var second = new CountingLease();
            var requestScope = new CountingLease(requestFailure ? failure : null);
            var sessionless = new CountingLease();
            context.RetainResponseResource(first);
            context.RetainResponseResource(second);
            context.AttachRequestScope(requestScope);
            context.AttachSessionlessLease(sessionless);

            if (requestFailure)
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(context.Dispose)!;
                Assert.That(error, Is.SameAs(failure));
            }
            else
            {
                AggregateException error = Assert.Throws<AggregateException>(context.Dispose)!;
                Assert.That(error.InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(error.InnerExceptions[0], Is.SameAs(failure));
            }
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(requestScope.Disposals, Is.EqualTo(1));
            Assert.That(sessionless.Disposals, Is.EqualTo(1));
            Assert.That(context.TakeResponseResources(), Is.Null);
            Assert.DoesNotThrow(context.Dispose);
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(requestScope.Disposals, Is.EqualTo(1));
            Assert.That(sessionless.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// A failed second attachment cannot replace or accidentally take ownership of a different queue charge.
        /// </summary>
        [Test]
        public void DuplicateDeliveryAttachmentPreservesTheFirstOwner()
        {
            var outcome = new CountingLease();
            var first = new CountingLease();
            var rejected = new CountingLease();
            var response = new RetainedCallResponse(outcome);
            response.AttachRequestResources(first);
            try
            {
                _ = Assert.Throws<InvalidOperationException>(() => response.AttachRequestResources(rejected));
                Assert.That(outcome.Disposals, Is.Zero);
                Assert.That(first.Disposals, Is.Zero);
                Assert.That(rejected.Disposals, Is.Zero);
            }
            finally
            {
                response.Reuse();
            }
            Assert.That(outcome.Disposals, Is.EqualTo(1));
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(rejected.Disposals, Is.Zero);
            rejected.Dispose();
            Assert.That(rejected.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// A delivery-charge failure does not retry outcome disposal or leave successful-reset payload fields set.
        /// </summary>
        [Test]
        public void ThrowingDeliveryChargeDoesNotRepeatOutcomeRelease()
        {
            var outcome = new CountingLease();
            var failure = new InvalidOperationException("Synthetic delivery disposal failure.");
            var delivery = new CountingLease(failure);
            var response = new RetainedCallResponse(outcome)
            {
                Results = [new CallMethodResult { OutputArguments = [Variant.From(42)] }]
            };
            response.AttachRequestResources(delivery);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                response.Reuse)!;

            Assert.That(error, Is.SameAs(failure));
            Assert.That(outcome.Disposals, Is.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
            Assert.That(response.Results, Is.Empty);
            Assert.DoesNotThrow(response.Reuse);
            Assert.That(outcome.Disposals, Is.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Reuse through the pooled interface remains one-shot when independent delivery paths race to finish.
        /// </summary>
        [Test]
        public async Task ConcurrentReuseReleasesBatchAndDeliveryExactlyOnce()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);
            var first = new CountingLease();
            var second = new CountingLease();
            var delivery = new CountingLease();
            context.RetainResponseResource(first);
            context.RetainResponseResource(second);
            var response = new RetainedCallResponse(context.TakeResponseResources());
            response.AttachRequestResources(delivery);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task[] callers = [.. Enumerable.Range(0, 8).Select(async _ =>
            {
                await start.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                response.Reuse();
            })];
            Assert.That(first.Disposals, Is.Zero);
            Assert.That(second.Disposals, Is.Zero);
            Assert.That(delivery.Disposals, Is.Zero);
            start.TrySetResult(true);
            await Task.WhenAll(callers).WaitAsync(timeout.Token).ConfigureAwait(false);
            context.Dispose();
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(delivery.Disposals, Is.EqualTo(1));
            Assert.That(context.TakeResponseResources(), Is.Null);
        }

        /// <summary>
        /// Counts every disposal attempt so a double release cannot be hidden by an idempotent test double.
        /// </summary>
        private sealed class CountingLease(Exception? failure = null) : IDisposable
        {
            public int Disposals => Volatile.Read(ref m_disposals);

            public void Dispose()
            {
                Interlocked.Increment(ref m_disposals);
                if (failure != null)
                {
                    throw failure;
                }
            }

            private int m_disposals;
        }
    }
}
