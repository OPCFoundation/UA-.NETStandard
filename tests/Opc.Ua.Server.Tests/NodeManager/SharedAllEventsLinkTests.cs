/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that facades sharing one physical all-events subscription keep it without a delivery gap.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SharedAllEventsLinkTests
    {
        /// <summary>
        /// Two facades share one physical subscribe, and the first release keeps the link for the other.
        /// </summary>
        [Test]
        public async Task ReleasingOneFacadeKeepsPhysicalLinkForOtherFacadeAsync()
        {
            var physical = new PhysicalSubscription();
            using var link = new SharedAllEventsLink<object>(physical.ChangeAsync);
            object facadeA = new();
            object facadeB = new();
            IEventMonitoredItem item = CreateItem();

            await link.ChangeAsync(facadeA, item, unsubscribe: false).ConfigureAwait(false);
            await link.ChangeAsync(facadeB, item, unsubscribe: false).ConfigureAwait(false);
            ServiceResult releaseA = await link.ChangeAsync(facadeA, item, unsubscribe: true)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(releaseA), Is.True);
                Assert.That(physical.IsLinked(item), Is.True);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false }));
                Assert.That(link.HasClaim(facadeA, item), Is.False);
                Assert.That(link.HasClaim(facadeB, item), Is.True);
            }

            ServiceResult releaseB = await link.ChangeAsync(facadeB, item, unsubscribe: true)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(releaseB), Is.True);
                Assert.That(physical.IsLinked(item), Is.False);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false, true }));
                Assert.That(link.IsPhysicallyHeld(item), Is.False);
            }
        }

        /// <summary>
        /// A facade that fails after its link release succeeded is retried without consuming the other
        /// facade's claim, and both facade callbacks are invoked for their own releases.
        /// </summary>
        [Test]
        public async Task FacadeFailureAfterLinkReleaseRetriesWithoutConsumingOtherClaimAsync()
        {
            var physical = new PhysicalSubscription();
            using var link = new SharedAllEventsLink<object>(physical.ChangeAsync);
            var facadeA = new Facade(link) { ReleaseResult = StatusCodes.BadInternalError };
            var facadeB = new Facade(link);
            IEventMonitoredItem item = CreateItem();
            await facadeA.ChangeAsync(item, unsubscribe: false).ConfigureAwait(false);
            await facadeB.ChangeAsync(item, unsubscribe: false).ConfigureAwait(false);

            ServiceResult firstRelease = await facadeA.ChangeAsync(item, unsubscribe: true).ConfigureAwait(false);
            facadeA.ReleaseResult = StatusCodes.Good;
            ServiceResult retriedRelease = await facadeA.ChangeAsync(item, unsubscribe: true)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstRelease.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
                Assert.That(ServiceResult.IsGood(retriedRelease), Is.True);
                Assert.That(facadeA.Releases, Is.EqualTo(2));
                Assert.That(physical.IsLinked(item), Is.True);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false }));
                Assert.That(link.HasClaim(facadeB, item), Is.True);
            }

            ServiceResult releaseB = await facadeB.ChangeAsync(item, unsubscribe: true).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(releaseB), Is.True);
                Assert.That(facadeB.Releases, Is.EqualTo(1));
                Assert.That(physical.IsLinked(item), Is.False);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false, true }));
            }
        }

        /// <summary>
        /// A failed last physical release is retried only by its exact owner.
        /// </summary>
        [Test]
        public async Task FailedLastPhysicalReleaseIsRetriedOnlyByExactOwnerAsync()
        {
            var physical = new PhysicalSubscription();
            using var link = new SharedAllEventsLink<object>(physical.ChangeAsync);
            object facadeA = new();
            object facadeB = new();
            IEventMonitoredItem item = CreateItem();
            await link.ChangeAsync(facadeA, item, unsubscribe: false).ConfigureAwait(false);
            await link.ChangeAsync(facadeB, item, unsubscribe: false).ConfigureAwait(false);
            await link.ChangeAsync(facadeA, item, unsubscribe: true).ConfigureAwait(false);
            physical.NextResult = StatusCodes.BadInternalError;

            ServiceResult failedRelease = await link.ChangeAsync(facadeB, item, unsubscribe: true)
                .ConfigureAwait(false);
            ServiceResult otherOwnerRelease = await link.ChangeAsync(facadeA, item, unsubscribe: true)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failedRelease.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
                Assert.That(ServiceResult.IsGood(otherOwnerRelease), Is.True);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false, true }));
                Assert.That(physical.IsLinked(item), Is.True);
                Assert.That(link.IsPhysicallyHeld(item), Is.True);
            }

            ServiceResult retriedRelease = await link.ChangeAsync(facadeB, item, unsubscribe: true)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(retriedRelease), Is.True);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false, true, true }));
                Assert.That(physical.IsLinked(item), Is.False);
                Assert.That(link.IsPhysicallyHeld(item), Is.False);
            }
        }

        /// <summary>
        /// A new claim after a failed last release settles the pending release, so its owner's retry
        /// cannot remove the new claim's subscription.
        /// </summary>
        [Test]
        public async Task NewClaimAfterFailedLastReleaseSettlesPendingReleaseAsync()
        {
            var physical = new PhysicalSubscription();
            using var link = new SharedAllEventsLink<object>(physical.ChangeAsync);
            object facadeA = new();
            object facadeC = new();
            IEventMonitoredItem item = CreateItem();
            await link.ChangeAsync(facadeA, item, unsubscribe: false).ConfigureAwait(false);
            physical.NextResult = StatusCodes.BadInternalError;
            await link.ChangeAsync(facadeA, item, unsubscribe: true).ConfigureAwait(false);

            ServiceResult claimC = await link.ChangeAsync(facadeC, item, unsubscribe: false).ConfigureAwait(false);
            ServiceResult retryA = await link.ChangeAsync(facadeA, item, unsubscribe: true).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(claimC), Is.True);
                Assert.That(ServiceResult.IsGood(retryA), Is.True);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false, true, false }));
                Assert.That(physical.IsLinked(item), Is.True);
                Assert.That(link.HasClaim(facadeC, item), Is.True);
            }
        }

        /// <summary>
        /// A rejected first physical subscribe records no claim.
        /// </summary>
        [Test]
        public async Task RejectedFirstSubscribeRecordsNoClaimAsync()
        {
            var physical = new PhysicalSubscription { NextResult = StatusCodes.BadNotSupported };
            using var link = new SharedAllEventsLink<object>(physical.ChangeAsync);
            object facadeA = new();
            IEventMonitoredItem item = CreateItem();

            ServiceResult result = await link.ChangeAsync(facadeA, item, unsubscribe: false).ConfigureAwait(false);
            ServiceResult release = await link.ChangeAsync(facadeA, item, unsubscribe: true).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(ServiceResult.IsGood(release), Is.True);
                Assert.That(link.HasClaim(facadeA, item), Is.False);
                Assert.That(link.IsPhysicallyHeld(item), Is.False);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false }));
            }
        }

        /// <summary>
        /// A first subscribe that throws leaves a pending release for its owner, because the physical state
        /// is unknown.
        /// </summary>
        [Test]
        public async Task ThrowingFirstSubscribeLeavesPendingReleaseForOwnerAsync()
        {
            var physical = new PhysicalSubscription { ThrowNext = true };
            using var link = new SharedAllEventsLink<object>(physical.ChangeAsync);
            object facadeA = new();
            IEventMonitoredItem item = CreateItem();

            await Assert.ThatAsync(
                async () => await link.ChangeAsync(facadeA, item, unsubscribe: false).ConfigureAwait(false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            Assert.That(link.IsPhysicallyHeld(item), Is.True);

            ServiceResult release = await link.ChangeAsync(facadeA, item, unsubscribe: true).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(release), Is.True);
                Assert.That(physical.Calls, Is.EqualTo(new[] { false, true }));
                Assert.That(link.IsPhysicallyHeld(item), Is.False);
            }
        }

        /// <summary>
        /// A claim that arrives while the last physical release is in flight waits and resubscribes after it,
        /// so the release cannot overtake the claim.
        /// </summary>
        [Test]
        public async Task ClaimDuringInFlightLastReleaseResubscribesAfterReleaseAsync()
        {
            var physical = new PhysicalSubscription();
            using var link = new SharedAllEventsLink<object>(physical.ChangeAsync);
            object facadeA = new();
            object facadeB = new();
            IEventMonitoredItem item = CreateItem();
            await link.ChangeAsync(facadeA, item, unsubscribe: false).ConfigureAwait(false);
            var releaseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            physical.ReleaseGate = releaseGate.Task;

            Task<ServiceResult> release = link.ChangeAsync(facadeA, item, unsubscribe: true).AsTask();
            Task<ServiceResult> claim = link.ChangeAsync(facadeB, item, unsubscribe: false).AsTask();
            await physical.ReleaseEntered.Task.ConfigureAwait(false);
            Assert.That(claim.IsCompleted, Is.False);
            releaseGate.SetResult(true);
            await Task.WhenAll(release, claim).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(physical.Calls, Is.EqualTo(new[] { false, true, false }));
                Assert.That(physical.IsLinked(item), Is.True);
                Assert.That(link.HasClaim(facadeB, item), Is.True);
            }
        }

        /// <summary>
        /// Invalid arguments are rejected.
        /// </summary>
        [Test]
        public async Task InvalidArgumentsAreRejectedAsync()
        {
            Assert.That(
                () => new SharedAllEventsLink<object>(null!),
                Throws.ArgumentNullException);
            using var link = new SharedAllEventsLink<object>(new PhysicalSubscription().ChangeAsync);
            await Assert.ThatAsync(
                async () => await link.ChangeAsync(null!, CreateItem(), false).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await link.ChangeAsync(new object(), null!, false).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        private static IEventMonitoredItem CreateItem()
        {
            return new Mock<IEventMonitoredItem>().Object;
        }

        /// <summary>
        /// A raw manager whose physical subscription is keyed only by the item.
        /// </summary>
        private sealed class PhysicalSubscription
        {
            public List<bool> Calls { get; } = [];

            public StatusCode? NextResult { get; set; }

            public bool ThrowNext { get; set; }

            public Task? ReleaseGate { get; set; }

            public TaskCompletionSource<bool> ReleaseEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsLinked(IEventMonitoredItem item)
            {
                return m_linked.Contains(item);
            }

            public async ValueTask<ServiceResult> ChangeAsync(
                IEventMonitoredItem item,
                bool unsubscribe,
                CancellationToken cancellationToken)
            {
                Calls.Add(unsubscribe);
                if (ThrowNext)
                {
                    ThrowNext = false;
                    throw new InvalidOperationException("Physical subscription state is unknown.");
                }
                if (unsubscribe && ReleaseGate is Task gate)
                {
                    ReleaseGate = null;
                    ReleaseEntered.TrySetResult(true);
                    await gate.ConfigureAwait(false);
                }
                if (NextResult is StatusCode result)
                {
                    NextResult = null;
                    return result;
                }
                if (unsubscribe)
                {
                    m_linked.Remove(item);
                }
                else
                {
                    m_linked.Add(item);
                }
                return ServiceResult.Good;
            }

            private readonly HashSet<IEventMonitoredItem> m_linked = [];
        }

        /// <summary>
        /// A facade that routes its raw changes through the shared link and may fail afterwards.
        /// </summary>
        private sealed class Facade
        {
            public Facade(SharedAllEventsLink<object> link)
            {
                m_link = link;
            }

            public StatusCode ReleaseResult { get; set; } = StatusCodes.Good;

            public int Releases { get; private set; }

            public async ValueTask<ServiceResult> ChangeAsync(IEventMonitoredItem item, bool unsubscribe)
            {
                ServiceResult raw = await m_link.ChangeAsync(this, item, unsubscribe).ConfigureAwait(false);
                if (!unsubscribe || ServiceResult.IsBad(raw))
                {
                    return raw;
                }
                Releases++;
                return ReleaseResult;
            }

            private readonly SharedAllEventsLink<object> m_link;
        }
    }
}
