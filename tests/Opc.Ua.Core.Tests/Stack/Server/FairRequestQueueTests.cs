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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Exercises fair ordering, bounded admission, worker wakeups and classification lifetimes.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public sealed class FairRequestQueueTests
    {
        [Test]
        public void WeightedRoundsPreserveOwnerFifo()
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("A", weight: 2);
            NodeId b = provider.AddOwner("B");
            using var queue = CreateQueue(provider);
            for (uint ii = 1; ii <= 6; ii++)
            {
                Enqueue(queue, new TestRequest(ii <= 4 ? a : b, ii));
            }

            List<uint> order = [];
            while (queue.TryDequeue(out FairRequestQueue.Entry? entry))
            {
                order.Add(entry.Request.Request.RequestHeader.RequestHandle);
                entry.Dispose();
            }
            Assert.That(order, Is.EqualTo(new uint[] { 1, 2, 5, 3, 4, 6 }));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.Zero);
        }

        /// <summary>
        /// A stopped queue rejects admission before classifying or charging the request.
        /// </summary>
        [Test]
        public void StoppedQueueRejectsAdmissionWithoutChargingTheProvider()
        {
            var provider = new TestProvider();
            NodeId token = provider.AddOwner("A");
            FairRequestQueue queue = CreateQueue(provider);
            queue.Dispose();

            Assert.That(queue.TryEnqueue(new TestRequest(token), default, out StatusCode status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A reader that arrives after the queue stopped is cancelled instead of waiting.
        /// </summary>
        [Test]
        public async Task StoppedQueueCancelsANewReader()
        {
            var provider = new TestProvider();
            FairRequestQueue queue = CreateQueue(provider);
            queue.Dispose();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await queue.DequeueAsync(CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// FIFO ordering ignores owner weights and preserves interleaved global admission order.
        /// </summary>
        [Test]
        public async Task FifoPreservesGlobalOrderAcrossWeightedOwnersAsync()
        {
            var provider = new TestProvider { UseFairScheduling = false };
            NodeId a = provider.AddOwner("A", weight: 3);
            NodeId b = provider.AddOwner("B", weight: 2);
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(a, 1));
            Enqueue(queue, new TestRequest(b, 2));
            Enqueue(queue, new TestRequest(a, 3));
            Enqueue(queue, new TestRequest(b, 4));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            for (uint handle = 1; handle <= 4; handle++)
            {
                using FairRequestQueue.Entry entry = await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(entry.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(handle));
            }
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A blocked FIFO head retains its place even when later protected owners could execute.
        /// </summary>
        [Test]
        public async Task FifoBlockedHeadCannotBeOvertakenAndResumesOnReleaseAsync()
        {
            var provider = new TestProvider(executionCapacity: 2) { UseFairScheduling = false };
            NodeId a = provider.AddOwner("A", executionLimit: 1);
            NodeId b = provider.AddOwner("B", weight: 3, ownerClass: ResourceIsolationClass.Trusted);
            using var queue = CreateQueue(provider);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Enqueue(queue, new TestRequest(a));
            using FairRequestQueue.Entry running = await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
            Enqueue(queue, new TestRequest(a, 2));
            Enqueue(queue, new TestRequest(b, 3));
            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(provider.ExecutionAttempts, Is.EqualTo(2));
            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(provider.ExecutionAttempts, Is.EqualTo(2), "An unchanged blocked head must not spin.");
            Enqueue(queue, new TestRequest(b, 4));
            Task<FairRequestQueue.Entry> waiting = queue.DequeueAsync(deadline.Token).AsTask();
            Assert.That(waiting.IsCompleted, Is.False);
            Assert.That(provider.ExecutionGrants, Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(3));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(40));
            running.Dispose();
            using FairRequestQueue.Entry head = await waiting.ConfigureAwait(false);
            Assert.That(head.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
            using FairRequestQueue.Entry next = await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(next.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(3));
            head.Dispose();
            next.Dispose();
            using FairRequestQueue.Entry last = await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(last.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(4));
            last.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Cancelling a blocked FIFO head releases its retention and wakes the next owner's request.
        /// </summary>
        [Test]
        public async Task FifoCancellationOfBlockedHeadWakesNextOwnerAsync()
        {
            var provider = new TestProvider(executionCapacity: 2) { UseFairScheduling = false };
            NodeId a = provider.AddOwner("A", executionLimit: 1);
            NodeId b = provider.AddOwner("B");
            using var queue = CreateQueue(provider);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            Enqueue(queue, new TestRequest(a));
            using FairRequestQueue.Entry running = await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
            var cancelled = new TestRequest(a, 2, park: true);
            Enqueue(queue, cancelled, cancellation.Token);
            var next = new TestRequest(b, 3);
            Enqueue(queue, next);
            Task<FairRequestQueue.Entry> waiting = queue.DequeueAsync(deadline.Token).AsTask();
            Assert.That(waiting.IsCompleted, Is.False);
            cancellation.Cancel();
            using FairRequestQueue.Entry executing = await waiting.ConfigureAwait(false);
            Assert.That(executing.Request, Is.SameAs(next));
            Assert.That(cancelled.CompletionCount, Is.EqualTo(1));
            Assert.That(cancelled.Status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(2));
            executing.Dispose();
            running.Dispose();
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// FIFO preserves the provider's protected byte reservation while retaining all admitted requests.
        /// </summary>
        [Test]
        public async Task FifoHonorsProviderReservedRequestBytesAsync()
        {
            var provider = new TestProvider(costCapacity: 30, reservedCostCapacity: 10)
            {
                UseFairScheduling = false
            };
            NodeId a = provider.AddOwner("A");
            NodeId b = provider.AddOwner("B");
            NodeId trusted = provider.AddOwner("trusted", ownerClass: ResourceIsolationClass.Trusted);
            using var queue = CreateQueue(provider);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Enqueue(queue, new TestRequest(a, 1));
            using FairRequestQueue.Entry running = await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
            Enqueue(queue, new TestRequest(b, 2));
            Assert.That(queue.TryEnqueue(new TestRequest(a, 3), default, out StatusCode status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
            Enqueue(queue, new TestRequest(trusted, 4));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(30));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(2));
            Assert.That(queue.TryEnqueue(new TestRequest(trusted, 5), default, out status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            using FairRequestQueue.Entry shared = await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(shared.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
            using FairRequestQueue.Entry protectedRequest =
                await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(protectedRequest.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(4));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(30));
            running.Dispose();
            shared.Dispose();
            protectedRequest.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [Test]
        public void FloodCannotFillAnotherOwnersQueueOrExecutionShare()
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("A", queueLimit: 2, executionLimit: 1);
            NodeId b = provider.AddOwner("B", queueLimit: 2, executionLimit: 1);
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(a));
            using FairRequestQueue.Entry runningA = Dequeue(queue);
            Enqueue(queue, new TestRequest(a, 2));
            Enqueue(queue, new TestRequest(a, 3));
            Assert.That(queue.TryEnqueue(new TestRequest(a, 4), default, out StatusCode status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            var requestB = new TestRequest(b);
            Enqueue(queue, requestB);
            using FairRequestQueue.Entry runningB = Dequeue(queue);
            Assert.That(runningB.Request, Is.SameAs(requestB));
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(2));
            Assert.That(queue.TryDequeue(out _), Is.False);
        }

        [Test]
        public void OneOwnerUsesIdleUnreservedSlotsUpToItsHardCeiling()
        {
            var provider = new TestProvider(executionCapacity: 3);
            NodeId a = provider.AddOwner("A", executionLimit: 3);
            using var queue = CreateQueue(provider);
            for (uint ii = 1; ii <= 4; ii++)
            {
                Enqueue(queue, new TestRequest(a, ii));
            }
            using FairRequestQueue.Entry first = Dequeue(queue);
            using FairRequestQueue.Entry second = Dequeue(queue);
            using FairRequestQueue.Entry third = Dequeue(queue);
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(3));
            Assert.That(queue.TryDequeue(out _), Is.False);
            first.Dispose();
            using FairRequestQueue.Entry fourth = Dequeue(queue);
            Assert.That(fourth.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(4));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void UnknownCostsRemainChargedDuringExecutionAndReleaseExactlyOnce(
            bool useFairScheduling,
            bool reservations)
        {
            TestProvider provider = reservations
                ? new ReservingTestProvider(costCapacity: 20)
                : new TestProvider(costCapacity: 20);
            provider.UseFairScheduling = useFairScheduling;
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider, requestCost: 10);
            Enqueue(queue, new TestRequest(token));
            using FairRequestQueue.Entry executing = Dequeue(queue);
            Enqueue(queue, new TestRequest(token));
            Assert.That(queue.TryEnqueue(new TestRequest(token), default, out StatusCode status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
            executing.Dispose();
            executing.Dispose();
            Enqueue(queue, new TestRequest(token));
            queue.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
            AssertReservationsReleasedOnce(provider);
        }

        [Test]
        public void UnknownOwnerChurnCannotGrowQueueStatePastTheCountLimit()
        {
            var provider = new TestProvider();
            using var queue = CreateQueue(provider, maxQueued: 3);
            for (int round = 0; round < 20; round++)
            {
                using var cancellation = new CancellationTokenSource();
                for (int ii = 0; ii < 3; ii++)
                {
                    NodeId token = provider.AddOwner($"owner-{round}-{ii}");
                    Enqueue(queue, new TestRequest(token), cancellation.Token);
                }
                NodeId excess = provider.AddOwner($"excess-{round}");
                Assert.That(queue.TryEnqueue(new TestRequest(excess), default, out _), Is.False);
                Assert.That(queue.OwnerCount, Is.EqualTo(3));
                cancellation.Cancel();
                Assert.That(queue.OwnerCount, Is.Zero);
                Assert.That(queue.Count, Is.Zero);
                Assert.That(provider.TotalUsed, Is.Zero);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void QueuedCancellationCompletesOnceAndReturnsAllLeases(bool useFairScheduling, bool reservations)
        {
            TestProvider provider = reservations ? new ReservingTestProvider() : new TestProvider();
            provider.UseFairScheduling = useFairScheduling;
            NodeId token = provider.AddOwner("A");
            using var cancellation = new CancellationTokenSource();
            using var queue = CreateQueue(provider);
            var request = new TestRequest(token, park: true);
            Enqueue(queue, request, cancellation.Token);
            cancellation.Cancel();
            cancellation.Cancel();
            queue.Dispose();
            Assert.That(request.CompletionCount, Is.EqualTo(1));
            Assert.That(request.Status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(provider.TotalUsed, Is.Zero);
            Assert.That(queue.TryDequeue(out _), Is.False);
            AssertReservationsReleasedOnce(provider);
        }

        /// <summary>
        /// A request rejected at a later stage returns the reservations of the earlier stages,
        /// and a parked request returns its execution, parked and cost reservations once each.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ReservationsReturnOnceOnRejectionAndParking(bool useFairScheduling)
        {
            var provider = new ReservingTestProvider(costCapacity: 10) { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider, requestCost: 10);
            var parked = new TestRequest(token, park: true);
            Enqueue(queue, parked);
            Assert.That(queue.TryEnqueue(new TestRequest(token), default, out StatusCode status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));

            FairRequestQueue.Entry entry = Dequeue(queue);
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            entry.ReleaseExecution();
            entry.ReleaseExecution();
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
            entry.Dispose();
            entry.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
            AssertReservationsReleasedOnce(provider);
        }

        /// <summary>
        /// Response handoff moves the existing lease or value reservation and outlives entry and queue disposal.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ResponseCostTransferPreservesExactlyOneOwner(bool reservations, bool disposedFirst)
        {
            TestProvider provider = reservations ? new ReservingTestProvider() : new TestProvider();
            NodeId owner = provider.AddOwner("A");
            using FairRequestQueue queue = CreateQueue(provider, requestCost: 10);
            Enqueue(queue, new TestRequest(owner, park: true));
            using FairRequestQueue.Entry entry = Dequeue(queue);
            int grants = provider.Grants;
            if (disposedFirst)
            {
                entry.Dispose();
            }
            using IDisposable? retained = entry.TransferResponseCost();
            Assert.That(retained == null, Is.EqualTo(disposedFirst));
            Assert.That(entry.TransferResponseCost(), Is.Null);
            Assert.That(provider.Grants, Is.EqualTo(grants), "Handoff must not acquire a second reservation.");
            entry.Dispose();
            entry.Dispose();
            queue.Dispose();
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(disposedFirst ? 0 : 10));
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.Zero);
            retained?.Dispose();
            retained?.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
            AssertReservationsReleasedOnce(provider);
        }

        [Test]
        public void PrecancelledRequestDoesNotClassifyOrAcquire()
        {
            var provider = new TestProvider();
            using var queue = CreateQueue(provider);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(
                queue.TryEnqueue(new TestRequest(NodeId.Null), cancellation.Token, out StatusCode status),
                Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(provider.ClassificationCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancellationDuringAdmissionReturnsEveryAcquiredLease(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var cancellation = new CancellationTokenSource();
            using var queue = CreateQueue(provider);
            provider.BeforeCostGrant = cancellation.Cancel;
            var request = new TestRequest(token, park: true);
            Assert.That(queue.TryEnqueue(request, cancellation.Token, out StatusCode status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(request.CompletionCount, Is.Zero, "The rejected admission is completed by its caller.");
            Assert.That(queue.Count, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QueueLimitRaceDisposesUnpublishedAdmissionWithoutRevokingQueuedWork(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider, maxQueued: 1);
            var admitted = new TestRequest(token);
            provider.BeforeCostGrant = () =>
            {
                provider.BeforeCostGrant = null!;
                Enqueue(queue, admitted);
            };

            var rejected = new TestRequest(token, park: true);
            Assert.That(queue.TryEnqueue(rejected, default, out StatusCode status), Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.Zero);
            Assert.That(rejected.CompletionCount, Is.Zero);
            using FairRequestQueue.Entry entry = Dequeue(queue);
            Assert.That(entry.Request, Is.SameAs(admitted));
            entry.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StopDuringAdmissionReleasesUnpublishedEntry(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            provider.BeforeCostGrant = queue.Dispose;
            Assert.That(
                queue.TryEnqueue(new TestRequest(token, park: true), default, out StatusCode status),
                Is.False);
            Assert.That(status, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancellationDuringGrantCannotExecuteOrDoubleComplete(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var cancellation = new CancellationTokenSource();
            using var queue = CreateQueue(provider);
            var request = new TestRequest(token);
            Enqueue(queue, request, cancellation.Token);
            provider.BeforeExecutionGrant = cancellation.Cancel;
            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(request.CompletionCount, Is.EqualTo(1));
            Assert.That(request.Status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// In weighted mode a worker selects the next request while another worker's admission
        /// is still running; global FIFO mode keeps one selection at a time.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WeightedModeSelectsWhileAnotherAdmissionRuns(bool useFairScheduling)
        {
            var provider = new TestProvider(executionCapacity: 2) { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(token, 1));
            Enqueue(queue, new TestRequest(token, 2));
            bool nested = false;
            FairRequestQueue.Entry? second = null;
            provider.BeforeExecutionGrant = () =>
            {
                provider.BeforeExecutionGrant = null!;
                nested = queue.TryDequeue(out second);
            };

            using FairRequestQueue.Entry first = Dequeue(queue);

            Assert.That(first.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(1));
            Assert.That(nested, Is.EqualTo(useFairScheduling));
            if (useFairScheduling)
            {
                Assert.That(second!.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
                second.Dispose();
            }
            else
            {
                using FairRequestQueue.Entry next = Dequeue(queue);
                Assert.That(next.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
            }
            first.Dispose();
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Concurrent selections of one owner that both find execution capacity exhausted
        /// return to the owner's FIFO in admission order, whichever fails last.
        /// </summary>
        [Test]
        public async Task ConcurrentlyBlockedHeadsKeepAdmissionOrderAsync()
        {
            var provider = new TestProvider(executionCapacity: 1);
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(token, 1));
            FairRequestQueue.Entry running = Dequeue(queue);
            Enqueue(queue, new TestRequest(token, 2));
            Enqueue(queue, new TestRequest(token, 3));
            using var laterInGrant = new ManualResetEventSlim();
            using var releaseLater = new ManualResetEventSlim();
            Task<bool>? later = null;
            int grants = 0;
            provider.BeforeExecutionGrant = () =>
            {
                if (Interlocked.Increment(ref grants) == 1)
                {
                    // request 2 is selected; a second worker selects request 3 and holds its grant
                    later = Task.Run(() => queue.TryDequeue(out _));
                    Assert.That(laterInGrant.Wait(TimeSpan.FromSeconds(5)), Is.True);
                }
                else
                {
                    laterInGrant.Set();
                    Assert.That(releaseLater.Wait(TimeSpan.FromSeconds(5)), Is.True);
                }
            };

            // request 2 fails first and is relinked before request 3 fails
            Assert.That(queue.TryDequeue(out _), Is.False);
            releaseLater.Set();
            Assert.That(await later!.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.False);
            provider.BeforeExecutionGrant = null!;
            Assert.That(provider.ExecutionAttempts, Is.EqualTo(3));
            Assert.That(queue.Count, Is.EqualTo(2));

            running.Dispose();
            using (FairRequestQueue.Entry next = Dequeue(queue))
            {
                Assert.That(next.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
            }
            using (FairRequestQueue.Entry last = Dequeue(queue))
            {
                Assert.That(last.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(3));
            }
            Assert.That(provider.TotalUsed, Is.Zero);
        }
        /// <summary>
        /// A queue stopped while a selected request's admission runs completes that request
        /// once with Bad_ServerHalted and returns all of its accounting.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StopDuringGrantCompletesTheSelectedRequestOnce(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            FairRequestQueue queue = CreateQueue(provider);
            var request = new TestRequest(token);
            Enqueue(queue, request);
            provider.BeforeExecutionGrant = queue.Dispose;

            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(request.CompletionCount, Is.EqualTo(1));
            Assert.That(request.Status, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunningCancellationKeepsAccountingUntilTheHandlerFinishes(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var cancellation = new CancellationTokenSource();
            using var queue = CreateQueue(provider);
            var request = new TestRequest(token);
            Enqueue(queue, request, cancellation.Token);
            using FairRequestQueue.Entry entry = Dequeue(queue);
            cancellation.Cancel();
            Assert.That(request.CompletionCount, Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
            entry.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A live replacement is retryable, while transfer rejects execution on the original channel.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void StaleClassificationOrChannelTransferIsFaultedBeforeExecution(bool transfer, bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("trusted", ownerClass: ResourceIsolationClass.Trusted);
            using var queue = CreateQueue(provider);
            var request = new TestRequest(token);
            Enqueue(queue, request);
            provider.ReplaceBinding(token, transfer ? "new-channel" : "channel");
            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(request.Status, Is.EqualTo(
                transfer ? StatusCodes.BadSessionIdInvalid : StatusCodes.BadServerTooBusy));
            Assert.That(request.CompletionCount, Is.EqualTo(1));
            Assert.That(provider.ExecutionGrants, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A deleted session is rejected as missing rather than offered a retryable classification refresh.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RemovedSessionIsRejectedBeforeExecution(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("trusted", ownerClass: ResourceIsolationClass.Trusted);
            using var queue = CreateQueue(provider);
            var request = new TestRequest(token);
            Enqueue(queue, request);
            provider.RemoveBinding(token);
            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(request.Status, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(request.CompletionCount, Is.EqualTo(1));
            Assert.That(provider.ExecutionGrants, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [Test]
        public void MultipleValidatedOwnersOnOneLogicalChannelKeepSeparateShares()
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("tenant-A", executionLimit: 1);
            NodeId b = provider.AddOwner("tenant-B", executionLimit: 1);
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(a));
            using FairRequestQueue.Entry runningA = Dequeue(queue);
            Enqueue(queue, new TestRequest(a));
            var requestB = new TestRequest(b);
            Enqueue(queue, requestB);
            using FairRequestQueue.Entry runningB = Dequeue(queue);
            Assert.That(runningA.Request.SecureChannelContext.SecureChannelId,
                Is.EqualTo(runningB.Request.SecureChannelContext.SecureChannelId));
            Assert.That(runningA.Owner.Key, Is.Not.EqualTo(runningB.Owner.Key));
            Assert.That(runningB.Request, Is.SameAs(requestB));
        }

        [TestCase("CreateSession", true, false)]
        [TestCase("ActivateSession", true, false)]
        [TestCase("Cancel", false, true)]
        [TestCase("CloseSession", false, true)]
        [TestCase("Read", false, false)]
        [TestCase("Publish", false, false)]
        public void RecoveryIntentDoesNotPromoteAnUnknownToken(
            string service,
            bool recoveryIntent,
            bool controlIntent)
        {
            IServiceRequest request = service switch
            {
                "CreateSession" => new CreateSessionRequest(),
                "ActivateSession" => new ActivateSessionRequest(),
                "Cancel" => new CancelRequest(),
                "CloseSession" => new CloseSessionRequest(),
                "Read" => new ReadRequest(),
                "Publish" => new PublishRequest(),
                _ => throw new ArgumentException("Unexpected test service.", nameof(service))
            };
            var provider = new TestProvider();
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(new NodeId(999), serviceRequest: request));
            Assert.That(provider.LastRecoveryIntent, Is.EqualTo(recoveryIntent));
            Assert.That(provider.LastControlIntent, Is.EqualTo(controlIntent));
            using FairRequestQueue.Entry entry = Dequeue(queue);
            Assert.That(entry.IsCurrent(), Is.True);
            Assert.That(provider.LastRecoveryIntent, Is.EqualTo(recoveryIntent));
            Assert.That(provider.LastControlIntent, Is.EqualTo(controlIntent));
            Assert.That(entry.Owner.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(entry.Owner.Key, Is.EqualTo("unknown"));
        }

        [Test]
        public void ParkedOwnerCannotLeakWorkerOrHeldRequestSlots()
        {
            var provider = new TestProvider(executionCapacity: 1);
            NodeId a = provider.AddOwner("A", parkedLimit: 1);
            NodeId b = provider.AddOwner("B", parkedLimit: 1);
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(a, park: true));
            using FairRequestQueue.Entry parked = Dequeue(queue);
            parked.ReleaseExecution();
            parked.ReleaseExecution();
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            Assert.That(queue.TryEnqueue(new TestRequest(a, park: true), default, out _), Is.False);
            var requestB = new TestRequest(b);
            Enqueue(queue, requestB);
            using FairRequestQueue.Entry executingB = Dequeue(queue);
            Assert.That(executingB.Request, Is.SameAs(requestB));
            parked.Dispose();
            parked.Dispose();
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.Zero);
            executingB.Dispose();
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [Test]
        public void IneligibleOwnerDoesNotSpinOrPreventAnotherOwnerFromRunning()
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("A", executionLimit: 1);
            NodeId b = provider.AddOwner("B");
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(a));
            using FairRequestQueue.Entry running = Dequeue(queue);
            Enqueue(queue, new TestRequest(a));
            Assert.That(queue.TryDequeue(out _), Is.False);
            int attempts = provider.ExecutionAttempts;
            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(provider.ExecutionAttempts, Is.EqualTo(attempts));
            var next = new TestRequest(b);
            Enqueue(queue, next);
            using FairRequestQueue.Entry other = Dequeue(queue);
            Assert.That(other.Request, Is.SameAs(next));
        }

        [TestCase(ResourceIsolationClass.Bootstrap)]
        [TestCase(ResourceIsolationClass.Reconnect)]
        [TestCase(ResourceIsolationClass.Trusted)]
        [TestCase(ResourceIsolationClass.Control)]
        public void ProtectedOwnerRunsWithoutEvictingAdmittedSharedWork(ResourceIsolationClass protectedClass)
        {
            var provider = new TestProvider { BlockSharedExecution = true };
            NodeId shared = provider.AddOwner("shared");
            NodeId protectedOwner = provider.AddOwner("protected", ownerClass: protectedClass);
            using var queue = CreateQueue(provider);
            var pending = new TestRequest(shared);
            Enqueue(queue, pending);
            var protectedRequest = new TestRequest(protectedOwner);
            Enqueue(queue, protectedRequest);
            using FairRequestQueue.Entry executing = Dequeue(queue);
            Assert.That(executing.Request, Is.SameAs(protectedRequest));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(pending.CompletionCount, Is.Zero);
            executing.Dispose();
            Assert.That(queue.TryDequeue(out _), Is.False);
            Assert.That(pending.CompletionCount, Is.Zero, "Protected releases cannot evict or admit shared work.");
        }

        [Test]
        public void CancellationDoesNotSpendAnOwnersWeightedTurn()
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("A", weight: 2);
            NodeId b = provider.AddOwner("B");
            using var queue = CreateQueue(provider);
            using var cancellation = new CancellationTokenSource();
            Enqueue(queue, new TestRequest(a, 1), cancellation.Token);
            Enqueue(queue, new TestRequest(a, 2));
            Enqueue(queue, new TestRequest(a, 3));
            Enqueue(queue, new TestRequest(b, 4));
            cancellation.Cancel();
            using FairRequestQueue.Entry first = Dequeue(queue);
            using FairRequestQueue.Entry second = Dequeue(queue);
            using FairRequestQueue.Entry third = Dequeue(queue);
            Assert.That(first.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
            Assert.That(second.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(3));
            Assert.That(third.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(4));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedProviderReleaseWakesAnotherQueuesWaitingWorkerAsync(bool useFairScheduling)
        {
            var provider = new TestProvider(executionCapacity: 1) { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var firstQueue = CreateQueue(provider);
            using var secondQueue = CreateQueue(provider);
            Enqueue(firstQueue, new TestRequest(token));
            using FairRequestQueue.Entry first = Dequeue(firstQueue);
            Enqueue(secondQueue, new TestRequest(token, 2));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<FairRequestQueue.Entry> waiting = secondQueue.DequeueAsync(deadline.Token).AsTask();
            Assert.That(waiting.IsCompleted, Is.False);
            first.Dispose();
            using FairRequestQueue.Entry second = await waiting.ConfigureAwait(false);
            Assert.That(second.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
        }

        /// <summary>
        /// Unrelated release bursts do not make execution-blocked workers retry admission.
        /// </summary>
        [Test]
        public async Task ReassemblyReleasesDoNotWakeExecutionWaitersAsync()
        {
            var provider = new TestProvider(executionCapacity: 1);
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(token));
            using FairRequestQueue.Entry running = Dequeue(queue);
            Enqueue(queue, new TestRequest(token, 2));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var waitingCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var waiters = new Task<FairRequestQueue.Entry>[8];
            for (int ii = 0; ii < waiters.Length; ii++)
            {
                waiters[ii] = queue.DequeueAsync(waitingCancellation.Token).AsTask();
            }
            int attempts = provider.ExecutionAttempts;
            long wakeSignals = queue.WakeSignalCount;
            for (int ii = 0; ii < 100; ii++)
            {
                Assert.That(provider.TryAcquire(
                    ResourceIsolationStage.ReassemblyBytes,
                    running.Owner, 1, out IDisposable lease, out _), Is.True);
                lease.Dispose();
                Assert.That(queue.TryDequeue(out _), Is.False);
            }
            Assert.That(provider.ExecutionAttempts, Is.EqualTo(attempts));
            Assert.That(queue.WakeSignalCount, Is.EqualTo(wakeSignals));
            running.ReleaseExecution();
            Task<FairRequestQueue.Entry> completed = await Task.WhenAny(waiters).WaitAsync(deadline.Token)
                .ConfigureAwait(false);
            using FairRequestQueue.Entry next = await completed.ConfigureAwait(false);
            Assert.That(next.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
            Assert.That(provider.ExecutionGrants, Is.EqualTo(2));
            Assert.That(provider.ExecutionAttempts, Is.EqualTo(attempts + 1));
            Assert.That(queue.WakeSignalCount, Is.EqualTo(wakeSignals + 1));
            waitingCancellation.Cancel();
            foreach (Task<FairRequestQueue.Entry> waiter in waiters)
            {
                if (waiter != completed)
                {
                    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                        await waiter.ConfigureAwait(false));
                }
            }
        }

        /// <summary>
        /// Empty queues never wake readers for released capacity, and shutdown cancels every reader.
        /// </summary>
        [TestCase(ResourceIsolationStage.ReassemblyBytes)]
        [TestCase(ResourceIsolationStage.RequestExecution)]
        public async Task EmptyQueueIgnoresReleaseBurstsAndStopsEveryWaiterAsync(ResourceIsolationStage stage)
        {
            var provider = new TestProvider();
            NodeId token = provider.AddOwner("A");
            ResourceIsolationOwner owner = provider.Classify(
                new SecureChannelContext("channel", null, RequestEncoding.Binary), token);
            using var queue = CreateQueue(provider);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var waiters = new Task<FairRequestQueue.Entry>[8];
            for (int ii = 0; ii < waiters.Length; ii++)
            {
                waiters[ii] = queue.DequeueAsync(deadline.Token).AsTask();
            }
            for (int ii = 0; ii < 100; ii++)
            {
                Assert.That(provider.TryAcquire(stage, owner, 1, out IDisposable lease, out _), Is.True);
                lease.Dispose();
            }
            Assert.That(queue.WakeSignalCount, Is.Zero);
            queue.Dispose();
            foreach (Task<FairRequestQueue.Entry> waiter in waiters)
            {
                try
                {
                    using FairRequestQueue.Entry unexpected = await waiter.ConfigureAwait(false);
                    Assert.Fail("Stopping an empty queue must cancel its readers.");
                }
                catch (OperationCanceledException)
                {
                    Assert.That(deadline.IsCancellationRequested, Is.False);
                }
            }
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A release consumed by a competing reader during admission is not lost on rejection.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReleaseDuringRejectedAdmissionStillWakesACompetingReaderAsync(bool useFairScheduling)
        {
            var provider = new TestProvider(executionCapacity: 1) { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            Enqueue(queue, new TestRequest(token));
            using FairRequestQueue.Entry running = Dequeue(queue);
            Enqueue(queue, new TestRequest(token, 2));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<FairRequestQueue.Entry>? competing = null;
            provider.BeforeExecutionRejection = () =>
            {
                provider.BeforeExecutionRejection = null!;
                running.ReleaseExecution();
                competing = queue.DequeueAsync(deadline.Token).AsTask();
                Assert.That(competing.IsCompleted, Is.False);
            };
            Task<FairRequestQueue.Entry> original = queue.DequeueAsync(deadline.Token).AsTask();
            Task<FairRequestQueue.Entry> completed = await Task.WhenAny(original, competing!)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            using FairRequestQueue.Entry next = await completed.ConfigureAwait(false);
            Assert.That(next.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
            Assert.That(provider.ExecutionGrants, Is.EqualTo(2));
            deadline.Cancel();
            Task<FairRequestQueue.Entry> cancelled = (completed == original ? competing : original)!;
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await cancelled!.ConfigureAwait(false));
        }

        /// <summary>
        /// Queued work admitted during the first grant grows workers without any capacity release.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WorkersGrowForBacklogAdmittedBeforeTheFirstDispatchAsync(bool useFairScheduling)
        {
            var provider = new TestProvider(executionCapacity: 4) { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var server = new QueueServer(provider, workers: 4, minWorkers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var requests = new TestRequest[4];
            for (int ii = 0; ii < requests.Length; ii++)
            {
                requests[ii] = new TestRequest(token, (uint)ii + 1);
            }
            provider.BeforeExecutionGrant = () =>
            {
                provider.BeforeExecutionGrant = null!;
                for (int ii = 1; ii < requests.Length; ii++)
                {
                    server.Enqueue(requests[ii]);
                }
            };
            server.Enqueue(requests[0]);
            await Task.WhenAll(Array.ConvertAll(requests, request => request.Started.Task))
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.ExecutionGrants, Is.EqualTo(4));
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(4));
            foreach (TestRequest request in requests)
            {
                request.Release();
            }
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RealWorkersReleaseAtParkAndBoundTheNoisyPublishOwnerAsync(bool useFairScheduling)
        {
            var provider = new TestProvider(executionCapacity: 1) { UseFairScheduling = useFairScheduling };
            NodeId a = provider.AddOwner("A", parkedLimit: 1);
            NodeId b = provider.AddOwner("B");
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var parked = new TestRequest(a, park: true);
            server.Enqueue(parked);
            await parked.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var excess = new TestRequest(a, park: true);
            server.Enqueue(excess);
            await excess.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(excess.Status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            var other = new TestRequest(b);
            server.Enqueue(other);
            await other.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(parked.Completed.Task.IsCompleted, Is.False);
            other.Release();
            parked.Release();
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
            Assert.That(parked.CompletionCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StopCancelsQueuedRunningAndParkedRequestsWithoutSyncWaitingAsync(bool useFairScheduling)
        {
            var provider = new TestProvider(executionCapacity: 2) { UseFairScheduling = useFairScheduling };
            NodeId a = provider.AddOwner("A", executionLimit: 1);
            NodeId b = provider.AddOwner("B", executionLimit: 1);
            using var server = new QueueServer(provider, workers: 2);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var parked = new TestRequest(a, park: true);
            server.Enqueue(parked);
            await parked.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var running = new TestRequest(b);
            server.Enqueue(running);
            await running.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var queued = new TestRequest(b);
            server.Enqueue(queued);
            server.StopQueue();
            var rejected = new TestRequest(a);
            server.Enqueue(rejected);
            Assert.That(rejected.Status, Is.EqualTo(StatusCodes.BadServerHalted));
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(queued.Status, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(running.Status, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(parked.Status, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(queued.CompletionCount, Is.EqualTo(1));
            Assert.That(running.CompletionCount, Is.EqualTo(1));
            Assert.That(parked.CompletionCount, Is.EqualTo(1));
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Reactivation during execution admission rejects stale privileged leases as retryable.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RevalidationAfterGrantStillPrecedesProtectedExecutionAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("trusted", ownerClass: ResourceIsolationClass.Trusted);
            provider.BeforeExecutionGrant = () => provider.ReplaceBinding(token, "channel");
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stale = new TestRequest(token);
            server.Enqueue(stale);
            await stale.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(stale.Status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(stale.Started.Task.IsCompleted, Is.False);
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Current admission evidence confirms the classification at dequeue and again before
        /// execution without the full revalidation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CurrentAdmissionEvidenceReplacesFullRevalidationAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling, IssueEvidence = true };
            NodeId token = provider.AddOwner("A");
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var request = new TestRequest(token);
            server.Enqueue(request);
            await request.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            request.Release();
            await request.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(request.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(provider.EvidenceChecks, Is.EqualTo(2), "dequeue and execution");
            Assert.That(provider.FullRevalidations, Is.Zero);
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Evidence that no longer holds at dequeue falls back to the full revalidation, which
        /// decides the status.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StaleAdmissionEvidenceAtDequeueUsesFullRevalidationStatusAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling, IssueEvidence = true };
            NodeId token = provider.AddOwner("A");
            provider.EvidenceCheck = _ =>
            {
                provider.RemoveBinding(token);
                return false;
            };
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var request = new TestRequest(token);
            server.Enqueue(request);
            await request.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(request.Status, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(request.Started.Task.IsCompleted, Is.False);
            Assert.That(provider.FullRevalidations, Is.EqualTo(1));
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A change between dequeue and execution is still caught by the check before execution.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StaleAdmissionEvidenceBeforeExecutionUsesFullRevalidationStatusAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling, IssueEvidence = true };
            NodeId token = provider.AddOwner("A");
            provider.EvidenceCheck = check =>
            {
                if (check == 1)
                {
                    return true;
                }
                provider.ReplaceBinding(token, "channel");
                return false;
            };
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var request = new TestRequest(token);
            server.Enqueue(request);
            await request.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(request.Status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(request.Started.Task.IsCompleted, Is.False);
            Assert.That(provider.EvidenceChecks, Is.EqualTo(2));
            Assert.That(provider.FullRevalidations, Is.EqualTo(1));
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ProviderFailureFaultsOnlyThatRequestAndKeepsTheWorkerAliveAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            provider.BeforeExecutionGrant = () =>
            {
                provider.BeforeExecutionGrant = null!;
                throw new InvalidOperationException("Classification provider failure.");
            };
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var failed = new TestRequest(token);
            server.Enqueue(failed);
            await failed.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(failed.Status, Is.EqualTo(StatusCodes.BadInternalError));
            var healthy = new TestRequest(token);
            server.Enqueue(healthy);
            await healthy.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            healthy.Release();
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Selecting FIFO does not bypass the installed provider's decoded-request accounting.
        /// </summary>
        [Test]
        public async Task CustomFifoProviderAccountsForRunningRequestsAsync()
        {
            var provider = new TestProvider { UseFairScheduling = false };
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var request = new TestRequest(NodeId.Null);
            server.Enqueue(request);
            await request.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.ClassificationCount, Is.GreaterThan(0));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
            request.Release();
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A custom FIFO policy bounds all four stages and retains bytes and parked slots after parking.
        /// </summary>
        [Test]
        public async Task CustomFifoProviderEnforcesEveryStageAndRetainsParkedCostsAsync()
        {
            var provider = new TestProvider(executionCapacity: 1, costCapacity: 20) { UseFairScheduling = false };
            NodeId a = provider.AddOwner("A", queueLimit: 1, executionLimit: 1, parkedLimit: 1);
            NodeId b = provider.AddOwner("B");
            using var server = new QueueServer(provider, workers: 1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var running = new TestRequest(a);
            server.Enqueue(running);
            await running.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var parked = new TestRequest(a, park: true);
            server.Enqueue(parked);
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            var queueExcess = new TestRequest(a);
            server.Enqueue(queueExcess);
            Assert.That(queueExcess.Status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(queueExcess.CompletionCount, Is.EqualTo(1));
            Assert.That(provider.Attempts(ResourceIsolationStage.RequestQueue), Is.EqualTo(3));
            Assert.That(provider.Attempts(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(2));
            var costExcess = new TestRequest(b);
            server.Enqueue(costExcess);
            Assert.That(costExcess.Status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(costExcess.CompletionCount, Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
            running.Release();
            await parked.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var parkExcess = new TestRequest(a, park: true);
            server.Enqueue(parkExcess);
            Assert.That(parkExcess.Status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(parkExcess.CompletionCount, Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.Zero);
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            var other = new TestRequest(b);
            server.Enqueue(other);
            await other.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(parked.Completed.Task.IsCompleted, Is.False);
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
            Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            var retainedCostExcess = new TestRequest(a);
            server.Enqueue(retainedCostExcess);
            Assert.That(retainedCostExcess.Status, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(retainedCostExcess.CompletionCount, Is.EqualTo(1));
            Assert.That(provider.Attempts(ResourceIsolationStage.RequestQueue), Is.EqualTo(7));
            Assert.That(provider.Attempts(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(6));
            Assert.That(provider.Attempts(ResourceIsolationStage.RequestExecution), Is.EqualTo(3));
            Assert.That(provider.Attempts(ResourceIsolationStage.ParkedRequest), Is.EqualTo(2));
            Assert.That(queueExcess.Started.Task.IsCompleted, Is.False);
            Assert.That(costExcess.Started.Task.IsCompleted, Is.False);
            Assert.That(parkExcess.Started.Task.IsCompleted, Is.False);
            Assert.That(retainedCostExcess.Started.Task.IsCompleted, Is.False);
            other.Release();
            parked.Release();
            await Task.WhenAll(running.Completed.Task, parked.Completed.Task, other.Completed.Task)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
            Assert.That(running.CompletionCount, Is.EqualTo(1));
            Assert.That(parked.CompletionCount, Is.EqualTo(1));
            Assert.That(other.CompletionCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A park notification without queue opt-in retains execution and does not reserve a parked slot.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ParkingWithoutOptInRetainsExecutionAndSkipsParkAdmissionAsync(bool useFairScheduling)
        {
            var provider = new TestProvider(executionCapacity: 1) { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var server = new QueueServer(provider, workers: 2, decoupleHeldPublishRequests: false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var rejectedExecution = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releasedCost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.BeforeExecutionRejection = () => rejectedExecution.TrySetResult(true);
            provider.CapacityAvailable += stage =>
            {
                if (stage == ResourceIsolationStage.RequestQueueBytes)
                {
                    releasedCost.TrySetResult(true);
                }
            };
            var held = new TestRequest(token, park: true);
            server.Enqueue(held);
            await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var next = new TestRequest(token, 2);
            server.Enqueue(next);
            await rejectedExecution.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(next.Started.Task.IsCompleted, Is.False);
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
            Assert.That(provider.Attempts(ResourceIsolationStage.ParkedRequest), Is.Zero);
            held.Release();
            await next.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            await releasedCost.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            next.Release();
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// With no installed provider, compatibility FIFO needs no admission cost or classification.
        /// </summary>
        [Test]
        public async Task NullProviderKeepsCompatibilityFifoWithoutRequestCostAsync()
        {
            using var server = new QueueServer(null!, workers: 1, requestCost: 0);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var first = new TestRequest(new NodeId(1));
            var second = new TestRequest(new NodeId(2));
            var third = new TestRequest(new NodeId(1), 3);
            server.Enqueue(first);
            await first.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            server.Enqueue(second);
            server.Enqueue(third);
            Assert.That(second.Started.Task.IsCompleted, Is.False);
            Assert.That(third.Started.Task.IsCompleted, Is.False);
            first.Release();
            await second.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(third.Started.Task.IsCompleted, Is.False);
            second.Release();
            await third.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            third.Release();
            await Task.WhenAll(first.Completed.Task, second.Completed.Task, third.Completed.Task)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(first.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(second.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(third.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.CompletionCount, Is.EqualTo(1));
            Assert.That(second.CompletionCount, Is.EqualTo(1));
            Assert.That(third.CompletionCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EnabledQueueRejectsMissingCostInsteadOfFallingBack(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using var queue = CreateQueue(provider, requestCost: 0);
            });
        }

        /// <summary>
        /// An owner's turn holds one token per queued request up to its weight, so a large
        /// weight costs no more scheduling state than the backlog.
        /// </summary>
        [Test]
        public void TurnTokensAreBoundedByTheBacklogNotTheWeight()
        {
            var provider = new TestProvider();
            NodeId heavy = provider.AddOwner("heavy", weight: 1_000_000);
            NodeId light = provider.AddOwner("light", weight: 2);
            using var queue = CreateQueue(provider);
            for (uint ii = 1; ii <= 3; ii++)
            {
                Enqueue(queue, new TestRequest(heavy, ii));
            }
            for (uint ii = 4; ii <= 6; ii++)
            {
                Enqueue(queue, new TestRequest(light, ii));
            }
            Assert.That(queue.ReadyTokenCount, Is.EqualTo(5));

            List<uint> order = [];
            while (queue.TryDequeue(out FairRequestQueue.Entry? entry))
            {
                order.Add(entry.Request.Request.RequestHeader.RequestHandle);
                entry.Dispose();
            }
            Assert.That(order, Is.EqualTo(new uint[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(queue.ReadyTokenCount, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
        }

        /// <summary>
        /// Cancelled requests leave no turn tokens behind once the queue is empty, even when no
        /// worker passes over them.
        /// </summary>
        [Test]
        public void CancellingTheLastQueuedRequestsDiscardsTheirTurnTokens()
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("A", weight: 3);
            NodeId b = provider.AddOwner("B");
            using var queue = CreateQueue(provider);
            using var cancellation = new CancellationTokenSource();
            for (uint ii = 1; ii <= 4; ii++)
            {
                Enqueue(queue, new TestRequest(ii % 2 == 0 ? a : b, ii), cancellation.Token);
            }
            Assert.That(queue.ReadyTokenCount, Is.EqualTo(3));
            cancellation.Cancel();
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(queue.ReadyTokenCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A turn granted just before its owner is retired, or before the queue stops, and
        /// published after their cleanup ran, is dropped instead of retaining the retired owner.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TurnPublishedAfterRetirementOrStopIsDiscarded(bool stop)
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("A");
            FairRequestQueue queue = CreateQueue(provider);
            using var cancellation = new CancellationTokenSource();
            var request = new TestRequest(a, 1);
            int hooked = 0;
            queue.BeforePublishTurn = () =>
            {
                // the turn is granted; retire the owner (or stop) before its token is published
                if (Interlocked.Increment(ref hooked) == 1)
                {
                    if (stop)
                    {
                        queue.Dispose();
                    }
                    else
                    {
                        cancellation.Cancel();
                    }
                }
            };

            Assert.That(queue.TryEnqueue(request, cancellation.Token, out _), Is.True);

            Assert.That(hooked, Is.GreaterThanOrEqualTo(1));
            Assert.That(request.CompletionCount, Is.EqualTo(1));
            Assert.That(request.Status, Is.EqualTo(
                stop ? StatusCodes.BadServerHalted : StatusCodes.BadRequestCancelledByClient));
            Assert.That(queue.ReadyTokenCount, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(queue.Count, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
            queue.Dispose();
        }

        /// <summary>
        /// A selector whose turn grant races the cancellation of the owner's last request leaves
        /// no turn token behind once the queue is empty.
        /// </summary>
        [Test]
        public void TurnGrantedBySelectionRacingCancellationIsDiscarded()
        {
            var provider = new TestProvider();
            NodeId a = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            using var cancellation = new CancellationTokenSource();
            Enqueue(queue, new TestRequest(a, 1));
            var cancelled = new TestRequest(a, 2);
            Enqueue(queue, cancelled, cancellation.Token);
            int hooked = 0;
            queue.BeforePublishTurn = () =>
            {
                // the selector took the last token and granted the next turn for request 2
                if (Interlocked.Increment(ref hooked) == 1)
                {
                    cancellation.Cancel();
                }
            };

            using FairRequestQueue.Entry first = Dequeue(queue);

            Assert.That(first.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(1));
            Assert.That(cancelled.CompletionCount, Is.EqualTo(1));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(queue.ReadyTokenCount, Is.Zero);
            Assert.That(queue.TryDequeue(out _), Is.False);
        }

        /// <summary>
        /// Concurrent workers keep serving available turns while another owner is delayed,
        /// and weighted service resumes without losing requests when that turn is published.
        /// </summary>
        /// <remarks>
        /// The controlled handoff separates work-conserving progress under preemption from
        /// weighted selection with both owners represented, rather than sampling OS scheduling.
        /// </remarks>
        [Test]
        public async Task WeightedServiceHoldsUnderConcurrentWorkersAsync()
        {
            const int perOwner = 480;
            const int concurrentWindow = 400;
            const int recoveredWindow = 240;
            var provider = new TestProvider(executionCapacity: 1000);
            NodeId light = provider.AddOwner("light", weight: 1, queueLimit: 2 * perOwner);
            NodeId heavy = provider.AddOwner("heavy", weight: 3, queueLimit: 2 * perOwner);
            using var queue = CreateQueue(provider, maxQueued: 2 * perOwner);
            for (uint ii = 0; ii < perOwner; ii++)
            {
                Enqueue(queue, new TestRequest(light, 2 * ii));
                Enqueue(queue, new TestRequest(heavy, (2 * ii) + 1));
            }

            var seen = new System.Collections.Concurrent.ConcurrentDictionary<uint, byte>();
            foreach (string owner in new[] { "light", "heavy", "heavy" })
            {
                using FairRequestQueue.Entry entry = Dequeue(queue);
                Assert.That(entry.Owner.Key, Is.EqualTo(owner));
                Observe(entry);
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var publishTurn = new ManualResetEventSlim();
            var turnGranted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int publications = 0;
            queue.BeforePublishTurn = () =>
            {
                if (Interlocked.Increment(ref publications) == 1)
                {
                    turnGranted.TrySetResult(true);
                    // Model a preempted selector at the existing synchronous publication hook.
                    publishTurn.Wait(deadline.Token);
                }
            };

            Task heldSelection = Task.Run(() =>
            {
                using FairRequestQueue.Entry entry = Dequeue(queue);
                Assert.That(entry.Owner.Key, Is.EqualTo("heavy"));
                Observe(entry);
            }, deadline.Token);

            try
            {
                await turnGranted.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                int reserved = 0;
                int lightServed = 0;
                int heavyServed = 0;
                Task[] workers = new Task[7];
                for (int w = 0; w < workers.Length; w++)
                {
                    workers[w] = Task.Run(async () =>
                    {
                        while (Interlocked.Increment(ref reserved) <= concurrentWindow)
                        {
                            using FairRequestQueue.Entry entry =
                                await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
                            Observe(entry);
                            if (entry.Owner.Key == "light")
                            {
                                Interlocked.Increment(ref lightServed);
                            }
                            else
                            {
                                Interlocked.Increment(ref heavyServed);
                            }
                        }
                    }, deadline.Token);
                }
                await Task.WhenAll(workers).WaitAsync(deadline.Token).ConfigureAwait(false);

                Assert.That(heldSelection.IsCompleted, Is.False);
                Assert.That(seen, Has.Count.EqualTo(concurrentWindow + 3));
                Assert.That(lightServed, Is.EqualTo(concurrentWindow));
                Assert.That(heavyServed, Is.Zero);
            }
            finally
            {
                publishTurn.Set();
                await heldSelection.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                queue.BeforePublishTurn = null;
            }

            Assert.That(seen, Has.Count.EqualTo(concurrentWindow + 4));
            Assert.That(queue.Count, Is.EqualTo((2 * perOwner) - concurrentWindow - 4));
            Assert.That(queue.OwnerCount, Is.EqualTo(2));
            Assert.That(queue.ReadyTokenCount, Is.EqualTo(4));

            int lightRecovered = 0;
            int heavyRecovered = 0;
            for (int ii = 0; ii < recoveredWindow; ii++)
            {
                using FairRequestQueue.Entry entry = Dequeue(queue);
                Observe(entry);
                if (entry.Owner.Key == "light")
                {
                    lightRecovered++;
                }
                else
                {
                    heavyRecovered++;
                }
            }
            Assert.That(lightRecovered, Is.EqualTo(recoveredWindow / 4));
            Assert.That(heavyRecovered, Is.EqualTo(3 * lightRecovered));

            while (queue.TryDequeue(out FairRequestQueue.Entry? rest))
            {
                using (rest)
                {
                    Observe(rest);
                }
            }
            Assert.That(seen, Has.Count.EqualTo(2 * perOwner));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(queue.ReadyTokenCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);

            void Observe(FairRequestQueue.Entry entry)
            {
                uint handle = entry.Request.Request.RequestHeader.RequestHandle;
                Assert.That(handle, Is.LessThan(2 * perOwner));
                Assert.That(seen.TryAdd(handle, 0), Is.True, $"Request {handle} was dispatched twice.");
            }
        }

        /// <summary>
        /// Producers and waiting workers race on many owners: every admitted request is
        /// dispatched exactly once and no worker is left waiting with work queued.
        /// </summary>
        [TestCase(true, -1, 0)]
        [TestCase(false, -1, 0)]
        [TestCase(true, 0, 0)]
        [TestCase(false, 0, 0)]
        [TestCase(true, 50, 1)]
        [TestCase(true, 50, 8)]
        [TestCase(false, 50, 2)]
        public async Task NoWakeupIsLostBetweenProducersAndWaitingWorkersAsync(
            bool useFairScheduling,
            int spinMicroseconds,
            int spinningReaders)
        {
            const int producers = 4;
            const int perProducer = 1500;
            var provider = new TestProvider(executionCapacity: 1000) { UseFairScheduling = useFairScheduling };
            NodeId[] owners = new NodeId[8];
            for (int ii = 0; ii < owners.Length; ii++)
            {
                owners[ii] = provider.AddOwner($"owner-{ii}", weight: 1 + (ii % 3), queueLimit: 1000);
            }
            using var queue = CreateQueue(provider, maxQueued: 1000);
            if (spinMicroseconds >= 0)
            {
                // -1 keeps the defaults
                queue.SpinDuration = System.Diagnostics.Stopwatch.Frequency * spinMicroseconds / 1_000_000;
                queue.MaxSpinningReaders = spinningReaders;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var seen = new System.Collections.Concurrent.ConcurrentDictionary<TestRequest, int>();
            int total = producers * perProducer;
            int dispatched = 0;
            var drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task[] workers = new Task[8];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = Task.Run(async () =>
                {
                    // reused for every wait, as a worker of the request queue does
                    WorkerWakeSignal.Waiter waiter = queue.CreateWaiter();
                    try
                    {
                        while (true)
                        {
                            FairRequestQueue.Entry entry =
                                await queue.DequeueAsync(waiter, deadline.Token).ConfigureAwait(false);
                            Assert.That(seen.TryAdd((TestRequest)entry.Request, 0), Is.True, "dispatched twice");
                            entry.Dispose();
                            if (Interlocked.Increment(ref dispatched) == total)
                            {
                                drained.TrySetResult(true);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // stopped
                    }
                });
            }

            Task[] producerTasks = new Task[producers];
            for (int p = 0; p < producers; p++)
            {
                int producer = p;
                producerTasks[p] = Task.Run(() =>
                {
                    for (int ii = 0; ii < perProducer; ii++)
                    {
                        var request = new TestRequest(
                            owners[(producer + ii) % owners.Length], (uint)((producer * perProducer) + ii));
                        StatusCode status;
                        while (!queue.TryEnqueue(request, default, out status))
                        {
                            // full: the workers make room
                            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
                            Thread.Yield();
                        }
                        if (ii % 64 == 0)
                        {
                            Thread.Yield();
                        }
                    }
                });
            }

            await Task.WhenAll(producerTasks).WaitAsync(deadline.Token).ConfigureAwait(false);
            await drained.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            queue.Dispose();
            await Task.WhenAll(workers).WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(dispatched, Is.EqualTo(total));
            Assert.That(seen, Has.Count.EqualTo(total));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Cancellation racing a dequeue completes each request at most once, never both
        /// dispatches and completes it, and returns every lease exactly once.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public async Task CancellationRacingDequeueReleasesLeasesExactlyOnceAsync(
            bool useFairScheduling,
            bool reservations)
        {
            TestProvider provider = reservations
                ? new ReservingTestProvider(executionCapacity: 1000)
                : new TestProvider(executionCapacity: 1000);
            provider.UseFairScheduling = useFairScheduling;
            NodeId a = provider.AddOwner("A", weight: 2, queueLimit: 1000);
            NodeId b = provider.AddOwner("B", queueLimit: 1000);
            using var queue = CreateQueue(provider, maxQueued: 1000);
            for (int round = 0; round < 200; round++)
            {
                var cancellations = new CancellationTokenSource[8];
                var requests = new TestRequest[8];
                for (int ii = 0; ii < requests.Length; ii++)
                {
                    cancellations[ii] = new CancellationTokenSource();
                    requests[ii] = new TestRequest(ii % 2 == 0 ? a : b, (uint)ii, park: ii % 3 == 0);
                    Enqueue(queue, requests[ii], cancellations[ii].Token);
                }
                var dispatched = new System.Collections.Concurrent.ConcurrentBag<FairRequestQueue.Entry>();
                Task cancel = Task.Run(() =>
                {
                    for (int ii = requests.Length - 1; ii >= 0; ii--)
                    {
                        cancellations[ii].Cancel();
                    }
                });
                Task[] dequeuers = new Task[3];
                for (int d = 0; d < dequeuers.Length; d++)
                {
                    dequeuers[d] = Task.Run(() =>
                    {
                        while (queue.TryDequeue(out FairRequestQueue.Entry? entry))
                        {
                            dispatched.Add(entry);
                        }
                    });
                }
                await Task.WhenAll(dequeuers).ConfigureAwait(false);
                await cancel.ConfigureAwait(false);
                while (queue.TryDequeue(out FairRequestQueue.Entry? late))
                {
                    dispatched.Add(late);
                }

                var dispatchedRequests = new HashSet<IEndpointIncomingRequest>();
                foreach (FairRequestQueue.Entry entry in dispatched)
                {
                    Assert.That(dispatchedRequests.Add(entry.Request), Is.True, "dispatched twice");
                    entry.Dispose();
                    entry.Dispose();
                }
                foreach (TestRequest request in requests)
                {
                    bool wasDispatched = dispatchedRequests.Contains(request);
                    Assert.That(request.CompletionCount, Is.EqualTo(wasDispatched ? 0 : 1), $"round {round}");
                    if (!wasDispatched)
                    {
                        Assert.That(request.Status, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                    }
                }
                foreach (CancellationTokenSource cancellation in cancellations)
                {
                    cancellation.Dispose();
                }
                Assert.That(queue.Count, Is.Zero, $"round {round}");
                Assert.That(provider.TotalUsed, Is.Zero, $"round {round}");
            }
            Assert.That(queue.OwnerCount, Is.Zero);
            AssertReservationsReleasedOnce(provider);
        }

        /// <summary>
        /// Stopping the queue while workers wait and admissions race completes every admitted
        /// request exactly once and wakes every waiting worker.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task StopWhileWorkersWaitAndAdmissionsRaceAsync(bool useFairScheduling)
        {
            for (int round = 0; round < 50; round++)
            {
                var provider = new TestProvider(executionCapacity: 1) { UseFairScheduling = useFairScheduling };
                NodeId a = provider.AddOwner("A");
                NodeId b = provider.AddOwner("B", weight: 2);
                FairRequestQueue queue = CreateQueue(provider, maxQueued: 1000);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                // one request runs, so the waiting workers find the rest blocked
                Enqueue(queue, new TestRequest(a, 0));
                FairRequestQueue.Entry running = Dequeue(queue);
                Task<FairRequestQueue.Entry>[] waiters = new Task<FairRequestQueue.Entry>[4];
                for (int w = 0; w < waiters.Length; w++)
                {
                    waiters[w] = queue.DequeueAsync(deadline.Token).AsTask();
                }
                var requests = new System.Collections.Concurrent.ConcurrentBag<(TestRequest Request, bool Admitted)>();
                Task[] producers = new Task[3];
                for (int p = 0; p < producers.Length; p++)
                {
                    int producer = p;
                    producers[p] = Task.Run(() =>
                    {
                        for (int ii = 0; ii < 20; ii++)
                        {
                            var request = new TestRequest(ii % 2 == 0 ? a : b, (uint)((producer * 100) + ii + 1));
                            requests.Add((request, queue.TryEnqueue(request, default, out _)));
                        }
                    });
                }
                Task stop = Task.Run(queue.Dispose);
                await Task.WhenAll(producers).WaitAsync(deadline.Token).ConfigureAwait(false);
                await stop.WaitAsync(deadline.Token).ConfigureAwait(false);

                foreach (Task<FairRequestQueue.Entry> waiter in waiters)
                {
                    await Assert.CatchAsync<OperationCanceledException>(
                        async () => await waiter.WaitAsync(deadline.Token).ConfigureAwait(false));
                    Assert.That(deadline.IsCancellationRequested, Is.False, "the stop must wake the waiter");
                }
                running.Dispose();
                foreach ((TestRequest request, bool admitted) in requests)
                {
                    // a rejected admission is completed by its caller, an admitted one by the queue
                    Assert.That(request.CompletionCount, Is.EqualTo(admitted ? 1 : 0), $"round {round}");
                    if (admitted)
                    {
                        Assert.That(request.Status, Is.EqualTo(StatusCodes.BadServerHalted));
                    }
                }
                Assert.That(queue.Count, Is.Zero, $"round {round}");
                Assert.That(provider.TotalUsed, Is.Zero, $"round {round}");
            }
        }

        /// <summary>
        /// A reader still looking for work takes an admitted request itself: no parked reader
        /// is woken for it.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task SpinningReaderTakesTheRequestWithoutWakingAParkedOneAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            // long enough that the spinning reader is still looking when the request arrives
            queue.SpinDuration = System.Diagnostics.Stopwatch.Frequency * 10;
            queue.MaxSpinningReaders = 1;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Task<FairRequestQueue.Entry> first = Task.Run(() => queue.DequeueAsync(deadline.Token).AsTask());
            Task<FairRequestQueue.Entry> second = Task.Run(() => queue.DequeueAsync(deadline.Token).AsTask());
            while (queue.ParkedReaderCount == 0)
            {
                await Task.Delay(1, deadline.Token).ConfigureAwait(false);
            }

            Enqueue(queue, new TestRequest(token, 7));
            Task<FairRequestQueue.Entry> taken = await Task.WhenAny(first, second).WaitAsync(deadline.Token)
                .ConfigureAwait(false);
            using FairRequestQueue.Entry entry = await taken.ConfigureAwait(false);
            Assert.That(entry.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(7));
            Assert.That(queue.WakeSignalCount, Is.Zero);
            Assert.That(queue.ParkedReaderCount, Is.EqualTo(1));

            queue.Dispose();
            Task<FairRequestQueue.Entry> parked = taken == first ? second : first;
            await Assert.CatchAsync<OperationCanceledException>(
                async () => await parked.WaitAsync(deadline.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(deadline.IsCancellationRequested, Is.False);
        }

        /// <summary>
        /// An admission that throws while a reader spins does not leave that reader counted as
        /// spinning: later requests still wake a parked reader.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task AdmissionFailureWhileSpinningKeepsWakingParkedReadersAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            queue.SpinDuration = System.Diagnostics.Stopwatch.Frequency * 10;
            queue.MaxSpinningReaders = 1;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Task<FairRequestQueue.Entry> first = Task.Run(() => queue.DequeueAsync(deadline.Token).AsTask());
            Task<FairRequestQueue.Entry> second = Task.Run(() => queue.DequeueAsync(deadline.Token).AsTask());
            while (queue.ParkedReaderCount == 0)
            {
                await Task.Delay(1, deadline.Token).ConfigureAwait(false);
            }
            provider.BeforeExecutionGrant = () =>
            {
                provider.BeforeExecutionGrant = null!;
                throw new InvalidOperationException("Provider failure while spinning.");
            };

            var failed = new TestRequest(token, 1);
            Enqueue(queue, failed);
            Task<FairRequestQueue.Entry> faulted = await Task.WhenAny(first, second).WaitAsync(deadline.Token)
                .ConfigureAwait(false);
            await Assert.CatchAsync<InvalidOperationException>(async () => await faulted.ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failed.Status, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(queue.ParkedReaderCount, Is.EqualTo(1));

            Enqueue(queue, new TestRequest(token, 2));
            Task<FairRequestQueue.Entry> parked = faulted == first ? second : first;
            using FairRequestQueue.Entry entry = await parked.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(entry.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(2));
        }

        /// <summary>
        /// Spins that run out lower the spin score until readers park at once, and a request
        /// admitted then still wakes a parked reader.
        /// </summary>
        [Test]
        public async Task SpinsThatRunOutStopSpinningWithoutLosingWakeupsAsync()
        {
            var provider = new TestProvider();
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            queue.SpinDuration = System.Diagnostics.Stopwatch.Frequency / 10_000;
            queue.MaxSpinningReaders = 1;
            Assert.That(queue.SpinScore, Is.EqualTo(FairRequestQueue.kMaxSpinScore));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            WorkerWakeSignal.Waiter waiter = queue.CreateWaiter();
            for (int ii = 0; ii < FairRequestQueue.kMaxSpinScore; ii++)
            {
                using var reader = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                Task<FairRequestQueue.Entry> wait = queue.DequeueAsync(waiter, reader.Token).AsTask();
                while (queue.ParkedReaderCount == 0)
                {
                    await Task.Yield();
                    Assert.That(deadline.IsCancellationRequested, Is.False);
                }
                reader.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await wait.WaitAsync(deadline.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            Assert.That(queue.SpinScore, Is.Zero);

            Task<FairRequestQueue.Entry> parked = queue.DequeueAsync(waiter, deadline.Token).AsTask();
            while (queue.ParkedReaderCount == 0)
            {
                await Task.Yield();
                Assert.That(deadline.IsCancellationRequested, Is.False);
            }
            Enqueue(queue, new TestRequest(token, 3));
            using FairRequestQueue.Entry entry = await parked.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(entry.Request.Request.RequestHeader.RequestHandle, Is.EqualTo(3));
        }

        /// <summary>
        /// A parked reader whose wait is cancelled while a request arrives never strands the
        /// request: another reader takes it, and its leases are returned exactly once.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public async Task CancelledParkedReaderRacingAnAdmissionStrandsNoRequestAsync(
            bool useFairScheduling,
            bool spin)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var queue = CreateQueue(provider);
            if (!spin)
            {
                queue.SpinDuration = 0;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            WorkerWakeSignal.Waiter stayerWaiter = queue.CreateWaiter();
            WorkerWakeSignal.Waiter leaverWaiter = queue.CreateWaiter();
            for (int round = 0; round < 300; round++)
            {
                using var leaving = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                Task<FairRequestQueue.Entry> stayer = queue.DequeueAsync(stayerWaiter, deadline.Token).AsTask();
                Task<FairRequestQueue.Entry> leaver = queue.DequeueAsync(leaverWaiter, leaving.Token).AsTask();
                while (queue.ParkedReaderCount < 2)
                {
                    await Task.Yield();
                    Assert.That(deadline.IsCancellationRequested, Is.False);
                }
                var request = new TestRequest(token, (uint)round);
                using var start = new Barrier(2);
                Task cancel = Task.Run(() =>
                {
                    start.SignalAndWait();
                    leaving.Cancel();
                });
                Task admit = Task.Run(() =>
                {
                    start.SignalAndWait();
                    Enqueue(queue, request);
                });
                await Task.WhenAll(cancel, admit).WaitAsync(deadline.Token).ConfigureAwait(false);

                FairRequestQueue.Entry? entry = null;
                try
                {
                    entry = await leaver.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                {
                    // the leaver gave up; the stayer has the request
                    entry = await stayer.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                Assert.That(entry.Request, Is.SameAs(request), $"round {round}");
                entry.Dispose();
                entry.Dispose();
                Assert.That(request.CompletionCount, Is.Zero, $"round {round}");
                Assert.That(provider.TotalUsed, Is.Zero, $"round {round}");
                if (!stayer.IsCompleted)
                {
                    // the leaver took the request: the stayer waits on, release it for the next round
                    Enqueue(queue, new TestRequest(token, 100_000));
                    using FairRequestQueue.Entry release = await stayer.WaitAsync(deadline.Token)
                        .ConfigureAwait(false);
                }
                Assert.That(queue.Count, Is.Zero, $"round {round}");
            }
        }

        /// <summary>
        /// The worker queue never runs more requests at once than the configured worker count,
        /// although execution capacity would allow more, and runs the backlog once released.
        /// </summary>
        [TestCase(true, 1)]
        [TestCase(true, 3)]
        [TestCase(false, 3)]
        public async Task ConfiguredWorkerCountBoundsConcurrentExecutionAsync(bool useFairScheduling, int workers)
        {
            var provider = new TestProvider(executionCapacity: 100) { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A", queueLimit: 100);
            using var server = new QueueServer(provider, workers: workers);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var requests = new TestRequest[workers + 5];
            for (int ii = 0; ii < requests.Length; ii++)
            {
                requests[ii] = new TestRequest(token, (uint)ii + 1);
                server.Enqueue(requests[ii]);
            }
            int Started()
            {
                return Array.FindAll(requests, request => request.Started.Task.IsCompleted).Length;
            }
            while (Started() < workers)
            {
                await Task.Delay(1, deadline.Token).ConfigureAwait(false);
            }
            // give an extra worker the time it would need to start a request
            await Task.Delay(200, deadline.Token).ConfigureAwait(false);
            Assert.That(Started(), Is.EqualTo(workers));
            Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(workers));

            for (int ii = 0; ii < requests.Length; ii++)
            {
                // release in order of start; each release lets exactly one more start
                TestRequest running = Array.Find(
                    requests, request => request.Started.Task.IsCompleted && !request.Completed.Task.IsCompleted)!;
                Assert.That(running, Is.Not.Null);
                running.Release();
                await running.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                int expected = Math.Min(requests.Length, workers + ii + 1);
                while (Started() < expected)
                {
                    await Task.Delay(1, deadline.Token).ConfigureAwait(false);
                }
                Assert.That(Started(), Is.EqualTo(expected));
            }
            await server.FinishAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Stopping the worker queue while its workers spin or park ends every worker loop.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task StopWhileWorkersParkEndsEveryWorkerAsync(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
            NodeId token = provider.AddOwner("A");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (int round = 0; round < 20; round++)
            {
                using var server = new QueueServer(provider, workers: 8);
                var request = new TestRequest(token, (uint)round);
                server.Enqueue(request);
                await request.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                request.Release();
                await request.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                // the workers are spinning or parked now; stop at varying points
                if (round % 2 == 0)
                {
                    await Task.Delay(round, deadline.Token).ConfigureAwait(false);
                }
                await server.FinishAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(provider.TotalUsed, Is.Zero, $"round {round}");
            }
        }

        /// <summary>
        /// Many owners, workers and cancellations: every request ends exactly once, all leases
        /// return and no queue or scheduling state remains.
        /// </summary>
        [Test]
        public async Task ManyOwnersAndWorkersStressLeavesNoStateAsync()
        {
            const int ownerCount = 50;
            const int total = 6000;
            var provider = new TestProvider(executionCapacity: 16);
            NodeId[] owners = new NodeId[ownerCount];
            for (int ii = 0; ii < ownerCount; ii++)
            {
                owners[ii] = provider.AddOwner($"owner-{ii}", weight: 1 + (ii % 4), queueLimit: 1000);
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var requests = new TestRequest[total];
            var cancellations = new CancellationTokenSource[total];
            int dispatched = 0;
            int ended = 0;
            var allEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void End()
            {
                if (Interlocked.Increment(ref ended) == total)
                {
                    allEnded.TrySetResult(true);
                }
            }
            // a request ends once: dispatched to a worker, or completed by the queue
            using var queue = new FairRequestQueue(
                provider, 1000, 10, true,
                (request, status) =>
                {
                    request.OperationCompleted(null, status);
                    End();
                });

            Task[] workers = new Task[16];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            FairRequestQueue.Entry entry =
                                await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
                            Interlocked.Increment(ref dispatched);
                            if (entry.Request is TestRequest { ParkSink: not null })
                            {
                                entry.ReleaseExecution();
                            }
                            await Task.Yield();
                            entry.Dispose();
                            End();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // stopped
                    }
                });
            }

            Task[] producers = new Task[4];
            for (int p = 0; p < producers.Length; p++)
            {
                int producer = p;
                producers[p] = Task.Run(() =>
                {
                    for (int ii = producer; ii < total; ii += producers.Length)
                    {
                        cancellations[ii] = new CancellationTokenSource();
                        requests[ii] = new TestRequest(owners[ii % ownerCount], (uint)ii, park: ii % 7 == 0);
                        StatusCode status;
                        while (!queue.TryEnqueue(requests[ii], cancellations[ii].Token, out status))
                        {
                            Assert.That(status, Is.EqualTo(StatusCodes.BadServerTooBusy));
                            Thread.Yield();
                        }
                        // cancel some requests that may be queued, selected or running
                        if (ii % 5 == 0)
                        {
                            CancellationTokenSource victim = cancellations[ii];
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                try
                                {
                                    victim.Cancel();
                                }
                                catch (ObjectDisposedException)
                                {
                                    // the request ended and the test finished first
                                }
                            });
                        }
                    }
                });
            }

            await Task.WhenAll(producers).WaitAsync(deadline.Token).ConfigureAwait(false);
            await allEnded.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            queue.Dispose();
            await Task.WhenAll(workers).WaitAsync(deadline.Token).ConfigureAwait(false);

            int cancelledInQueue = 0;
            for (int ii = 0; ii < total; ii++)
            {
                Assert.That(requests[ii].CompletionCount, Is.LessThanOrEqualTo(1), $"request {ii}");
                cancelledInQueue += requests[ii].CompletionCount;
                cancellations[ii].Dispose();
            }
            Assert.That(dispatched + cancelledInQueue, Is.EqualTo(total));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// Clients keep a fixed number of requests in flight per owner, refilled as they complete:
        /// with one in flight every owner FIFO is retired after almost every dispatch, with many
        /// in flight several workers select from one owner at once. No request may be stranded.
        /// </summary>
        [TestCase(64, 1, 300, 1000)]
        [TestCase(10, 100, 30, 1000)]
        [TestCase(10, 100, 30, 4)]
        [TestCase(64, 1, 300, 4)]
        [TestCase(1, 200, 20, 1000)]
        [TestCase(1, 200, 20, 4)]
        public async Task OwnersWithRequestsInFlightStrandNoRequestAsync(
            int ownerCount,
            int inFlight,
            int rounds,
            int executionCapacity)
        {
            var provider = new TestProvider(executionCapacity: executionCapacity);
            NodeId[] owners = new NodeId[ownerCount];
            for (int ii = 0; ii < ownerCount; ii++)
            {
                owners[ii] = provider.AddOwner($"owner-{ii}", weight: 1 + (ii % 3), queueLimit: 1000);
            }
            using var queue = CreateQueue(provider, maxQueued: 1000);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            int dispatched = 0;

            Task[] workers = new Task[16];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            FairRequestQueue.Entry entry =
                                await queue.DequeueAsync(deadline.Token).ConfigureAwait(false);
                            Interlocked.Increment(ref dispatched);
                            entry.Dispose();
                            ((TestRequest)entry.Request).OperationCompleted(null, StatusCodes.Good);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // stopped
                    }
                });
            }

            // each client keeps inFlight requests of its owner in flight, like a session pipeline
            Task[] clients = new Task[ownerCount * inFlight];
            for (int c = 0; c < clients.Length; c++)
            {
                NodeId owner = owners[c % ownerCount];
                clients[c] = Task.Run(async () =>
                {
                    for (int ii = 0; ii < rounds; ii++)
                    {
                        var request = new TestRequest(owner, (uint)ii);
                        Assert.That(queue.TryEnqueue(request, default, out StatusCode status), Is.True, status.ToString());
                        await request.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                    }
                });
            }

            await Task.WhenAll(clients).WaitAsync(deadline.Token).ConfigureAwait(false);
            queue.Dispose();
            await Task.WhenAll(workers).WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(dispatched, Is.EqualTo(ownerCount * inFlight * rounds));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.OwnerCount, Is.Zero);
            Assert.That(provider.TotalUsed, Is.Zero);
        }

        /// <summary>
        /// A provider that admits by value got every admission of the queue as a reservation
        /// and each reservation back exactly once.
        /// </summary>
        private static void AssertReservationsReleasedOnce(TestProvider provider)
        {
            if (provider is ReservingTestProvider reserving)
            {
                Assert.That(reserving.Reservations, Is.GreaterThan(0));
                Assert.That(reserving.Reservations, Is.EqualTo(provider.Grants));
                Assert.That(reserving.Releases, Is.EqualTo(reserving.Reservations));
                Assert.That(reserving.DuplicateReleases, Is.Zero);
            }
        }

        private static FairRequestQueue CreateQueue(
            IServerResourceIsolationProvider provider,
            int maxQueued = 100,
            long requestCost = 10)
        {
            return new FairRequestQueue(
                provider, maxQueued, requestCost, true,
                static (request, status) => request.OperationCompleted(null, status));
        }

        private static void Enqueue(
            FairRequestQueue queue,
            TestRequest request,
            CancellationToken cancellationToken = default)
        {
            Assert.That(queue.TryEnqueue(request, cancellationToken, out StatusCode status), Is.True);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
        }

        private static FairRequestQueue.Entry Dequeue(FairRequestQueue queue)
        {
            Assert.That(queue.TryDequeue(out FairRequestQueue.Entry? entry), Is.True);
            return entry!;
        }

        /// <summary>
        /// Executes controllable requests through the production worker queue.
        /// </summary>
        private sealed class QueueServer : ServerBase
        {
            /// <summary>
            /// Creates a real worker queue with independently configurable initial and maximum workers.
            /// </summary>
            public QueueServer(
                TestProvider provider,
                int workers,
                int? minWorkers = null,
                bool decoupleHeldPublishRequests = true,
                long requestCost = 10)
                : base(NUnitTelemetryContext.Create())
            {
                m_queue = new RequestQueue(
                    this, minWorkers ?? workers, workers, 100, decoupleHeldPublishRequests, provider, requestCost);
            }

            public void Enqueue(TestRequest request, CancellationToken cancellationToken = default)
            {
                m_queue.ScheduleIncomingRequest(request, cancellationToken);
            }

            public void StopQueue()
            {
                m_queue.Dispose();
            }

            public ValueTask FinishAsync(CancellationToken cancellationToken)
            {
                return m_queue.StopAsync(cancellationToken);
            }

            protected override Task ProcessRequestAsync(
                IEndpointIncomingRequest request,
                CancellationToken cancellationToken = default)
            {
                return request.CallAsync(cancellationToken).AsTask();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_queue.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly RequestQueue m_queue;
        }

        private sealed class TestRequest : IParkableIncomingRequest
        {
            public TestRequest(
                NodeId token,
                uint handle = 1,
                bool park = false,
                string channel = "channel",
                IServiceRequest? serviceRequest = null)
            {
                Request = serviceRequest ?? (park ? new PublishRequest() : new ReadRequest());
                Request.RequestHeader = new RequestHeader { AuthenticationToken = token, RequestHandle = handle };
                ParkSink = (Request is PublishRequest ? new RequestParkSink() : null)!;
                SecureChannelContext = new SecureChannelContext(channel, null, RequestEncoding.Binary);
            }

            public IServiceRequest Request { get; }
            public SecureChannelContext SecureChannelContext { get; }
            public RequestParkSink ParkSink { get; } = null!;
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Completed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int CompletionCount => Volatile.Read(ref m_completionCount);
            public StatusCode Status { get; private set; }

            public async ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                ParkSink?.NotifyParked();
                Started.TrySetResult(true);
                await m_released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                OperationCompleted(new ReadResponse(), StatusCodes.Good);
            }

            public void Release()
            {
                m_released.TrySetResult(true);
            }

            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
                Interlocked.Increment(ref m_completionCount);
                Status = error.StatusCode;
                Completed.TrySetResult(true);
            }

            private readonly TaskCompletionSource<bool> m_released =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_completionCount;
        }

        /// <summary>
        /// Provides deterministic owner limits and admission-race hooks for queue regressions.
        /// </summary>
        /// <summary>
        /// Admits by value: the queue holds a reservation per stage and must return each
        /// exactly once, which a lease would hide by being idempotent.
        /// </summary>
        private sealed class ReservingTestProvider(
            long executionCapacity = 100,
            long costCapacity = 10000) : TestProvider(executionCapacity, costCapacity), IResourceIsolationReservations
        {
            public int Reservations => Volatile.Read(ref m_reservations);
            public int Releases => Volatile.Read(ref m_releases);
            public int DuplicateReleases => Volatile.Read(ref m_duplicateReleases);

            public bool TryReserve(
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount,
                out ResourceIsolationReservation reservation)
            {
                if (!TryAcquire(stage, owner, amount, out IDisposable lease, out _))
                {
                    reservation = default;
                    return false;
                }
                Interlocked.Increment(ref m_reservations);
                reservation = new ResourceIsolationReservation(
                    new Reserved(lease, stage, owner, amount), ResourceIsolationClass.Established, 0);
                return true;
            }

            public void Release(
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount,
                in ResourceIsolationReservation reservation)
            {
                var reserved = (Reserved)reservation.Accounting;
                Assert.That(stage, Is.EqualTo(reserved.Stage));
                Assert.That(owner, Is.SameAs(reserved.Owner));
                Assert.That(amount, Is.EqualTo(reserved.Amount));
                if (Interlocked.Exchange(ref reserved.Released, 1) != 0)
                {
                    Interlocked.Increment(ref m_duplicateReleases);
                    return;
                }
                Interlocked.Increment(ref m_releases);
                reserved.Lease.Dispose();
            }

            private sealed class Reserved(
                IDisposable lease,
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount)
            {
                public IDisposable Lease { get; } = lease;
                public ResourceIsolationStage Stage { get; } = stage;
                public ResourceIsolationOwner Owner { get; } = owner;
                public long Amount { get; } = amount;
                public int Released;
            }

            private int m_reservations;
            private int m_releases;
            private int m_duplicateReleases;
        }

        private class TestProvider : IServerResourceIsolationProvider, IResourceIsolationRevalidationProvider,
            IResourceIsolationAdmissionEvidence
        {
            public TestProvider(
                long executionCapacity = 100,
                long costCapacity = 10000,
                long reservedCostCapacity = 0)
            {
                int stages = (int)ResourceIsolationStage.ParkedRequest + 1;
                m_capacity = new long[stages];
                m_used = new long[stages];
                m_attempts = new int[stages];
                for (int ii = 0; ii < stages; ii++)
                {
                    m_capacity[ii] = 1000;
                }
                m_capacity[(int)ResourceIsolationStage.RequestExecution] = executionCapacity;
                m_capacity[(int)ResourceIsolationStage.RequestQueueBytes] = costCapacity;
                m_reservedCostCapacity = reservedCostCapacity;
                m_unknownToken = AddOwner("unknown");
            }

            /// <summary>
            /// Reports exactly the stage whose lease was returned.
            /// </summary>
            public event Action<ResourceIsolationStage>? CapacityAvailable;
            public bool UseFairScheduling { get; set; } = true;
            public bool BlockSharedExecution { get; set; }
            public Action BeforeExecutionGrant { get; set; } = null!;

            /// <summary>
            /// Runs after a rejected execution decision, outside the provider accounting gate.
            /// </summary>
            public Action BeforeExecutionRejection { get; set; } = null!;

            public Action BeforeCostGrant { get; set; } = null!;
            public int ExecutionAttempts { get; private set; }
            public int ExecutionGrants { get; private set; }

            /// <summary>
            /// Successful admissions of every stage.
            /// </summary>
            public int Grants { get; private set; }
            public int ClassificationCount { get; private set; }
            public bool LastRecoveryIntent { get; private set; }
            public bool LastControlIntent { get; private set; }
            public long TotalUsed
            {
                get
                {
                    lock (m_gate)
                    {
                        long used = 0;
                        foreach (long amount in m_used)
                        {
                            used += amount;
                        }
                        return used;
                    }

                }
            }

            public NodeId AddOwner(
                string key,
                int weight = 1,
                long queueLimit = 100,
                long executionLimit = 100,
                long parkedLimit = 100,
                ResourceIsolationClass ownerClass = ResourceIsolationClass.Established)
            {
                long[] limits = new long[m_capacity.Length];
                for (int ii = 0; ii < limits.Length; ii++)
                {
                    limits[ii] = 10000;
                }
                limits[(int)ResourceIsolationStage.RequestQueue] = queueLimit;
                limits[(int)ResourceIsolationStage.RequestExecution] = executionLimit;
                limits[(int)ResourceIsolationStage.ParkedRequest] = parkedLimit;
                var owner = new ResourceIsolationOwner(key, ownerClass, weight, limits);
                var token = new NodeId((uint)m_bindings.Count + 1);
                m_bindings.Add(token, (owner, "channel"));
                return token;
            }

            public void ReplaceBinding(NodeId token, string channel)
            {
                ResourceIsolationOwner prior = m_bindings[token].Owner;
                long[] limits = new long[m_capacity.Length];
                for (int ii = 0; ii < limits.Length; ii++)
                {
                    limits[ii] = prior.GetHardLimit((ResourceIsolationStage)ii);
                }
                m_bindings[token] =
                    (new ResourceIsolationOwner(prior.Key, prior.Class, prior.Weight, limits), channel);
            }

            /// <summary>
            /// Removes a session binding without replacing it with a live classification.
            /// </summary>
            public void RemoveBinding(NodeId token)
            {
                Assert.That(m_bindings.Remove(token), Is.True);
            }

            public ResourceIsolationOwner ClassifyConnection(IPEndPoint? remoteEndpoint)
            {
                return m_bindings[m_unknownToken].Owner;
            }

            public ResourceIsolationOwner Classify(
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                ClassificationCount++;
                LastRecoveryIntent = sessionEstablishment;
                LastControlIntent = controlRequest;
                if (m_bindings.TryGetValue(authenticationToken, out var binding) &&
                    binding.Channel == channelContext.SecureChannelId)
                {
                    return binding.Owner;
                }
                return m_bindings[m_unknownToken].Owner;
            }

            public bool IsCurrent(
                ResourceIsolationOwner owner,
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                return ReferenceEquals(
                    owner, Classify(channelContext, authenticationToken, sessionEstablishment, controlRequest));
            }

            /// <summary>
            /// Returns admission evidence with each classification; off by default.
            /// </summary>
            public bool IssueEvidence { get; set; }

            /// <summary>
            /// Decides each evidence check; the argument counts the checks from one.
            /// </summary>
            public Func<int, bool> EvidenceCheck { get; set; } = _ => true;

            public int EvidenceChecks => Volatile.Read(ref m_evidenceChecks);

            public int FullRevalidations => Volatile.Read(ref m_fullRevalidations);

            ResourceIsolationOwner IResourceIsolationAdmissionEvidence.Classify(
                SecureChannelContext channelContext,
                NodeId authenticationToken,
                bool sessionEstablishment,
                bool controlRequest,
                out ResourceIsolationAdmissionEvidence? evidence)
            {
                ResourceIsolationOwner owner = Classify(
                    channelContext, authenticationToken, sessionEstablishment, controlRequest);
                evidence = IssueEvidence ? new TestEvidence(owner) : null;
                return owner;
            }

            bool IResourceIsolationAdmissionEvidence.IsAdmissionCurrent(
                ResourceIsolationAdmissionEvidence evidence,
                SecureChannelContext channelContext)
            {
                return EvidenceCheck(Interlocked.Increment(ref m_evidenceChecks));
            }

            /// <summary>
            /// Distinguishes same-channel replacement from a missing or transferred binding.
            /// </summary>
            public StatusCode GetRevalidationStatus(
                ResourceIsolationOwner owner,
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                Interlocked.Increment(ref m_fullRevalidations);
                if (IsCurrent(owner, channelContext, authenticationToken, sessionEstablishment, controlRequest))
                {
                    return StatusCodes.Good;
                }
                return m_bindings.TryGetValue(authenticationToken, out var binding) &&
                    binding.Channel == channelContext.SecureChannelId ?
                    StatusCodes.BadServerTooBusy : StatusCodes.BadSessionIdInvalid;
            }

            /// <summary>
            /// Acquires a bounded lease while keeping test callbacks outside the accounting gate.
            /// </summary>
            public bool TryAcquire(
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount,
                [NotNullWhen(true)] out IDisposable lease,
                out ResourceIsolationFailure failure)
            {
                if (stage == ResourceIsolationStage.RequestExecution)
                {
                    BeforeExecutionGrant?.Invoke();
                }
                else if (stage == ResourceIsolationStage.RequestQueueBytes)
                {
                    BeforeCostGrant?.Invoke();
                }
                bool acquired;
                lock (m_gate)
                {
                    m_attempts[(int)stage]++;
                    if (stage == ResourceIsolationStage.RequestExecution)
                    {
                        ExecutionAttempts++;
                    }
                    var key = (owner.Key, stage);
                    m_ownerUsed.TryGetValue(key, out long ownerUsed);
                    bool sharedCost = stage == ResourceIsolationStage.RequestQueueBytes &&
                        owner.Class == ResourceIsolationClass.Established;
                    if ((BlockSharedExecution && stage == ResourceIsolationStage.RequestExecution &&
                        owner.Class == ResourceIsolationClass.Established) ||
                        (sharedCost && amount > m_capacity[(int)stage] - m_reservedCostCapacity - m_sharedCostUsed) ||
                        amount > m_capacity[(int)stage] - m_used[(int)stage] ||
                        amount > owner.GetHardLimit(stage) - ownerUsed)
                    {
                        lease = null!;
                        failure = new ResourceIsolationFailure(
                            ResourceIsolationFailureReason.Capacity, TimeSpan.Zero);
                        acquired = false;
                    }
                    else
                    {
                        m_used[(int)stage] += amount;
                        m_ownerUsed[key] = ownerUsed + amount;
                        if (sharedCost)
                        {
                            m_sharedCostUsed += amount;
                        }
                        if (stage == ResourceIsolationStage.RequestExecution)
                        {
                            ExecutionGrants++;
                        }
                        Grants++;
                        lease = new TestLease(() => Release(owner.Key, stage, amount, sharedCost));
                        failure = default;
                        acquired = true;
                    }
                }
                if (!acquired && stage == ResourceIsolationStage.RequestExecution)
                {
                    BeforeExecutionRejection?.Invoke();
                }
                return acquired;
            }

            public long Used(ResourceIsolationStage stage)
            {
                lock (m_gate)
                {
                    return m_used[(int)stage];
                }
            }

            /// <summary>
            /// Gets admission calls, including rejected attempts, for the requested stage.
            /// </summary>
            public int Attempts(ResourceIsolationStage stage)
            {
                lock (m_gate)
                {
                    return m_attempts[(int)stage];
                }
            }

            /// <summary>
            /// Returns one lease and reports its stage after leaving the accounting gate.
            /// </summary>
            private void Release(string key, ResourceIsolationStage stage, long amount, bool sharedCost)
            {
                lock (m_gate)
                {
                    m_used[(int)stage] -= amount;
                    m_ownerUsed[(key, stage)] -= amount;
                    if (sharedCost)
                    {
                        m_sharedCostUsed -= amount;
                    }
                }
                CapacityAvailable?.Invoke(stage);
            }

            private sealed class TestLease(Action release) : IDisposable
            {
                public void Dispose()
                {
                    Interlocked.Exchange(ref m_release!, null)?.Invoke();
                }

                private Action m_release = release;
            }

            private readonly Lock m_gate = new();
            private readonly long[] m_capacity;
            private readonly long[] m_used;
            private readonly int[] m_attempts;
            private readonly long m_reservedCostCapacity;
            private readonly NodeId m_unknownToken;
            private readonly Dictionary<NodeId, (ResourceIsolationOwner Owner, string Channel)> m_bindings = [];
            private readonly Dictionary<(string Owner, ResourceIsolationStage Stage), long> m_ownerUsed = [];
            private long m_sharedCostUsed;
            /// <summary>
            /// The evidence the provider issues: the owner it classified.
            /// </summary>
            private sealed record TestEvidence(ResourceIsolationOwner Owner) : ResourceIsolationAdmissionEvidence;

            private int m_evidenceChecks;
            private int m_fullRevalidations;
        }
    }
}
