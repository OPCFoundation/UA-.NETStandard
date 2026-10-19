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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.MachineTool.Server;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using static Opc.Ua.MachineTool.Tests.MachineToolJobSourceTests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises bounded backpressure, independent subscribers and outside-gate delivery/lifetime behavior.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class MachineToolJobSourceLifetimeTests
    {
        /// <summary>
        /// A full revision history refuses mutations rather than skipping canonical transitions.
        /// </summary>
        [Test]
        public async Task BoundedRevisionHistoryRequiresDrainAndReservesBothCancellationSnapshotsAsync()
        {
            await using var source = new MachineToolJobSource(Options() with { MaximumPendingRevisions = 2 });
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order()).ConfigureAwait(false));
            Assert.That((await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Start, Order())
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await source.SetMonitoringAsync(new MachineToolSnapshot()).ConfigureAwait(false));
            var revisions = new List<ulong>();
            await source.DrainAsync(async ct =>
                revisions.Add((await source.ReadAsync(ct).ConfigureAwait(false)).Revision)).ConfigureAwait(false);
            Assert.That(revisions, Is.EqualTo(new ulong[] { 0, 1 }));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Start, Order()).ConfigureAwait(false));
            Assert.That((await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Cancel, Order())
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
            Assert.That((await source.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(2));
            await source.DrainAsync(static _ => default).ConfigureAwait(false);
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Cancel, Order()).ConfigureAwait(false));
            Assert.That((await source.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(4));
            Assert.That(await source.GetJobOrdersV2Async().ConfigureAwait(false), Is.Empty);
        }

        /// <summary>
        /// Async publication reads a pinned coherent capture while other requests can commit without a callback gate.
        /// </summary>
        [Test]
        public async Task PublicationCanReadAndConcurrentRequestsDoNotChangeItsPinnedRevisionAsync()
        {
            await using var source = new MachineToolJobSource(Options());
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var revisions = new List<ulong>();
            Task drain = source.DrainAsync(async ct =>
            {
                MachineToolJobSourceCapture before = await source.CaptureAsync(ct).ConfigureAwait(false);
                if (before.Snapshot.Revision == 0)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    MachineToolJobSourceCapture after = await source.CaptureAsync(ct).ConfigureAwait(false);
                    Assert.That(after.Snapshot.Revision, Is.Zero);
                    Assert.That(after.JobOrders, Is.Empty);
                }
                revisions.Add(before.Snapshot.Revision);
            }).AsTask();
            try
            {
                await entered.Task.ConfigureAwait(false);
                Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order())
                    .ConfigureAwait(false));
                Assert.That((await source.CaptureAsync().ConfigureAwait(false)).Snapshot.Revision, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await drain.ConfigureAwait(false);
            }
            Assert.That(revisions, Is.EqualTo(new ulong[] { 0, 1 }));
        }

        /// <summary>
        /// All callback reentrant mutations fail promptly, while reads succeed and failed delivery remains retryable.
        /// </summary>
        [Test]
        public async Task ReentrancyIsRejectedAndFailedDeliveryRetainsItsExactHeadAsync()
        {
            await using var source = new MachineToolJobSource(Options());
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order()).ConfigureAwait(false));
            MachineToolSnapshot? failed = null;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.DrainAsync(async ct =>
            {
                failed = await source.ReadAsync(ct).ConfigureAwait(false);
                await Assert.ThrowsAsync<ServiceResultException>(async () => await source.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Start, Order(), cancellationToken: ct).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () => await source.TransitionAsync(
                    "Job-1", Isa95JobExecutionTransition.BeginExecution, ct).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await source.SetMonitoringAsync(new MachineToolSnapshot(), ct).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await source.RecordPartsAsync("Job-1", default, ct).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await source.CompleteRunAsync("Job-1", ct).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await source.ReceiveJobResponseAsync(new V2.ISA95JobResponseDataType(), ct).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await source.DrainAsync(static _ => default, ct).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await source.DisposeAsync().ConfigureAwait(false));
                throw new InvalidOperationException("Simulated publication failure.");
            }).ConfigureAwait(false));
            int delivered = 0;
            await source.DrainAsync(async ct =>
            {
                MachineToolSnapshot snapshot = await source.ReadAsync(ct).ConfigureAwait(false);
                if (delivered++ == 0)
                {
                    Assert.That(snapshot, Is.SameAs(failed));
                }
            }).ConfigureAwait(false);
            Assert.That(delivered, Is.EqualTo(2));
        }

        /// <summary>
        /// Pre-cancellation does not commit state; cancellation during delivery does not discard its accepted head.
        /// </summary>
        [Test]
        public async Task CancellationPreservesAcceptedStateAndPendingPublicationAsync()
        {
            await using var source = new MachineToolJobSource(Options());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await source.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store, Order(), cancellationToken: cancelled.Token).ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await source.ReadAsync(cancelled.Token).ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await source.CaptureAsync(cancelled.Token).ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await source.DrainAsync(static _ => default, cancelled.Token).ConfigureAwait(false));
            using var cancelDelivery = new CancellationTokenSource();
            await Assert.CatchAsync<OperationCanceledException>(async () => await source.DrainAsync(_ =>
            {
                cancelDelivery.Cancel();
                return default;
            }, cancelDelivery.Token).ConfigureAwait(false));
            int retry = 0;
            await source.DrainAsync(_ =>
            {
                retry++;
                return default;
            }).ConfigureAwait(false);
            Assert.That(retry, Is.EqualTo(1));
            Assert.That((await source.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
        }

        /// <summary>
        /// Disposal cancels and joins an active callback rather than retaining its resources indefinitely.
        /// </summary>
        [Test]
        public async Task DisposalCancelsAndJoinsDeliveryWithoutOwningTheApplicationClockAsync()
        {
            using var clock = new BorrowedClock();
            await using var source = new MachineToolJobSource(Options(), timeProvider: clock);
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order()).ConfigureAwait(false));
            int calls = clock.Calls;
            await source.ReadAsync().ConfigureAwait(false);
            await source.CaptureAsync().ConfigureAwait(false);
            await source.GetJobResponsesV2Async().ConfigureAwait(false);
            Assert.That(clock.Calls, Is.EqualTo(calls), "Observation reads cannot initiate execution or acquisition.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task drain = source.DrainAsync(async ct =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }).AsTask();
            await entered.Task.ConfigureAwait(false);
            Task first = source.DisposeAsync().AsTask();
            Task second = source.DisposeAsync().AsTask();
            await Task.WhenAll(first, second).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await drain.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await source.ReadAsync().ConfigureAwait(false));
            Assert.That(clock.Disposed, Is.False);
        }

        /// <summary>
        /// A slow status subscriber applies bounded backpressure but neither steals nor duplicates another's events.
        /// </summary>
        [Test]
        public async Task IndependentStatusSubscriptionsApplyLosslessBackpressureAsync()
        {
            await using var source = new MachineToolJobSource(Options() with { MaximumPendingChanges = 1 });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using IAsyncEnumerator<Isa95JobStatusNotificationV2> slow =
                source.SubscribeAsync(timeout.Token).GetAsyncEnumerator();
            await using IAsyncEnumerator<Isa95JobStatusNotificationV2> fast =
                source.SubscribeAsync(timeout.Token).GetAsyncEnumerator();
            Task<bool> slowFirst = slow.MoveNextAsync().AsTask();
            Task<bool> fastFirst = fast.MoveNextAsync().AsTask();
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order()).ConfigureAwait(false));
            Assert.That(await slowFirst.ConfigureAwait(false), Is.True);
            Assert.That(await fastFirst.ConfigureAwait(false), Is.True);
            Assert.That(slow.Current.SequenceNumber, Is.EqualTo(fast.Current.SequenceNumber));
            slow.Current.JobOrder.JobOrderID = "tampered";
            Assert.That(fast.Current.JobOrder.JobOrderID, Is.EqualTo("Job-1"));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Start, Order()).ConfigureAwait(false));
            Assert.That(await fast.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(fast.Current.SequenceNumber, Is.EqualTo(2));
            Assert.That((await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.RevokeStart, Order())
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
            await slow.DisposeAsync().ConfigureAwait(false);
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.RevokeStart, Order())
                .ConfigureAwait(false));
            Assert.That(await fast.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(fast.Current.SequenceNumber, Is.EqualTo(3));
            timeout.Cancel();
            Assert.That(await fast.MoveNextAsync().ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Catalog and response streams independently broadcast exactly the underlying engine's changes.
        /// </summary>
        [Test]
        public async Task CatalogAndResponseChangesRemainIndependentForEverySubscriberAsync()
        {
            await using var source = new MachineToolJobSource(Options());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using IAsyncEnumerator<Isa95JobOrderCatalogChange> left =
                source.SubscribeCatalogChangesAsync(timeout.Token).GetAsyncEnumerator();
            await using IAsyncEnumerator<Isa95JobOrderCatalogChange> right =
                source.SubscribeCatalogChangesAsync(timeout.Token).GetAsyncEnumerator();
            await using IAsyncEnumerator<Isa95JobResponseChange> first =
                source.SubscribeResponseChangesAsync(timeout.Token).GetAsyncEnumerator();
            await using IAsyncEnumerator<Isa95JobResponseChange> second =
                source.SubscribeResponseChangesAsync(timeout.Token).GetAsyncEnumerator();
            Task<bool> leftWait = left.MoveNextAsync().AsTask();
            Task<bool> rightWait = right.MoveNextAsync().AsTask();
            Task<bool> firstWait = first.MoveNextAsync().AsTask();
            Task<bool> secondWait = second.MoveNextAsync().AsTask();
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order()).ConfigureAwait(false));
            Assert.That(leftWait.IsCompleted, Is.False, "Store has a status event, not a fabricated catalog change.");
            V2.ISA95JobOrderDataType update = Order();
            update.Priority = 4;
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Update, update).ConfigureAwait(false));
            Assert.That(await leftWait.ConfigureAwait(false), Is.True);
            Assert.That(await rightWait.ConfigureAwait(false), Is.True);
            Assert.That(left.Current.Kind, Is.EqualTo(Isa95JobOrderCatalogChangeKind.Updated));
            Assert.That(left.Current.SequenceNumber, Is.EqualTo(right.Current.SequenceNumber));
            left.Current.Order!.JobOrder.Priority = 99;
            Assert.That(right.Current.Order!.JobOrder.Priority, Is.EqualTo(4));
            MachineToolJobSourceCapture captured = await source.CaptureAsync().ConfigureAwait(false);
            var response = new V2.ISA95JobResponseDataType
            {
                JobResponseID = "R",
                JobOrderID = "Job-1",
                JobState = captured.JobOrders[0].State
            };
            Assert.That((await source.ReceiveJobResponseAsync(response).ConfigureAwait(false)).ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(await firstWait.ConfigureAwait(false), Is.True);
            Assert.That(await secondWait.ConfigureAwait(false), Is.True);
            Assert.That(first.Current, Is.EqualTo(second.Current));
            Assert.That(first.Current.Removed, Is.False);
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Cancel, Order()).ConfigureAwait(false));
            Assert.That(await left.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(await right.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(left.Current.Kind, Is.EqualTo(Isa95JobOrderCatalogChangeKind.Removed));
            Assert.That(left.Current.SequenceNumber, Is.EqualTo(2));
            Assert.That(right.Current.SequenceNumber, Is.EqualTo(2));
            await source.DisposeAsync().ConfigureAwait(false);
            Assert.That(await first.MoveNextAsync().ConfigureAwait(false), Is.False);
            Assert.That(await second.MoveNextAsync().ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Subscriber registration is bounded, and cancelling one iterator frees its admission slot.
        /// </summary>
        [Test]
        public async Task SubscriberLimitIsReleasedWhenAnIndependentSubscriptionCancelsAsync()
        {
            await using var source = new MachineToolJobSource(Options() with { MaximumSubscribers = 1 });
            using var stop = new CancellationTokenSource();
            await using IAsyncEnumerator<Isa95JobStatusNotificationV2> first =
                source.SubscribeAsync(stop.Token).GetAsyncEnumerator();
            Task<bool> pending = first.MoveNextAsync().AsTask();
            await using IAsyncEnumerator<Isa95JobResponseChange> refused =
                source.SubscribeResponseChangesAsync().GetAsyncEnumerator();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await refused.MoveNextAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            stop.Cancel();
            Assert.That(await pending.ConfigureAwait(false), Is.False);
            using var nextStop = new CancellationTokenSource();
            await using IAsyncEnumerator<Isa95JobOrderCatalogChange> next =
                source.SubscribeCatalogChangesAsync(nextStop.Token).GetAsyncEnumerator();
            Task<bool> nextPending = next.MoveNextAsync().AsTask();
            nextStop.Cancel();
            Assert.That(await nextPending.ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// A test application clock whose ownership and use remain observable.
        /// </summary>
        private sealed class BorrowedClock : TimeProvider, IDisposable
        {
            /// <summary>
            /// Gets the number of acquisition requests.
            /// </summary>
            public int Calls { get; private set; }

            /// <summary>
            /// Gets whether the application clock was disposed.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                Calls++;
                return new DateTimeOffset(2026, 10, 1, 1, 2, 3, TimeSpan.Zero);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
