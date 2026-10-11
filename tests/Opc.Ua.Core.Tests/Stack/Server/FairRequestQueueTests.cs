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
        public void StoppedQueueCancelsANewReader()
        {
            var provider = new TestProvider();
            FairRequestQueue queue = CreateQueue(provider);
            queue.Dispose();

            Assert.ThrowsAsync<OperationCanceledException>(
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

        [TestCase(false)]
        [TestCase(true)]
        public void UnknownCostsRemainChargedDuringExecutionAndReleaseExactlyOnce(bool useFairScheduling)
        {
            var provider = new TestProvider(costCapacity: 20) { UseFairScheduling = useFairScheduling };
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

        [TestCase(false)]
        [TestCase(true)]
        public void QueuedCancellationCompletesOnceAndReturnsAllLeases(bool useFairScheduling)
        {
            var provider = new TestProvider { UseFairScheduling = useFairScheduling };
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
                    Assert.ThrowsAsync<OperationCanceledException>(async () =>
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
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
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
        private sealed class TestProvider : IServerResourceIsolationProvider, IResourceIsolationRevalidationProvider
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
            /// Distinguishes same-channel replacement from a missing or transferred binding.
            /// </summary>
            public StatusCode GetRevalidationStatus(
                ResourceIsolationOwner owner,
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
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
        }
    }
}
