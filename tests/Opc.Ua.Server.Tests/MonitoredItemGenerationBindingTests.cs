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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests exact-generation membership, slot-local recovery and terminal invalidation of subscription-owned items.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class MonitoredItemGenerationBindingTests
    {
        /// <summary>
        /// Concurrent retry refuses recursive waiting and never duplicates a provider callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingSourceCleanupCoordinatesConcurrentRetryWithoutRecursiveWaitingAsync(
            bool independentReclamation)
        {
            using MonitoredItemQueueFactory queues = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queues));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            generation.RetainSourceRelease(item, 0, async ct =>
            {
                calls++;
                entered.TrySetResult(true);
                await finish.Task.WaitAsync(ct).ConfigureAwait(false);
            });
            Task active = generation.CompleteSourceReleasesAsync(item, CancellationToken.None).AsTask();
            Task? joined = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (independentReclamation)
                {
                    joined = generation.CompleteSourceReleasesAsync(
                        null, CancellationToken.None, joinActiveCleanup: true).AsTask();
                    Assert.That(joined.IsCompleted, Is.False);
                }
                else
                {
                    ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await generation.CompleteSourceReleasesAsync(item, CancellationToken.None)
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                }
                Assert.Multiple(() =>
                {
                    Assert.That(active.IsCompleted, Is.False);
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(generation.HasPendingSourceReleases, Is.True);
                });
            }
            finally
            {
                finish.TrySetResult(true);
                await active.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (joined is not null)
                {
                    await joined.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }
            await generation.CompleteSourceReleasesAsync(item, CancellationToken.None).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(generation.HasPendingSourceReleases, Is.False);
        }

        /// <summary>
        /// Joined cleanup observes the exact provider error and leaves the failed stage retryable.
        /// </summary>
        [Test]
        public async Task PendingSourceCleanupJoinedFailureRemainsOwnedForRetryAsync()
        {
            using MonitoredItemQueueFactory queues = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queues));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Injected joined provider-cleanup failure.");
            int calls = 0;
            generation.RetainSourceRelease(item, 0, async ct =>
            {
                if (++calls == 1)
                {
                    entered.TrySetResult(true);
                    await finish.Task.WaitAsync(ct).ConfigureAwait(false);
                    throw failure;
                }
            });
            Task owner = generation.CompleteSourceReleasesAsync(item, CancellationToken.None).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task joined = generation.CompleteSourceReleasesAsync(
                null, CancellationToken.None, joinActiveCleanup: true).AsTask();
            finish.TrySetResult(true);
            InvalidOperationException? ownerFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await owner.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                .ConfigureAwait(false);
            InvalidOperationException? joinedFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await joined.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ownerFailure, Is.SameAs(failure));
                Assert.That(joinedFailure, Is.SameAs(failure));
                Assert.That(generation.HasPendingSourceReleases, Is.True);
                Assert.That(calls, Is.EqualTo(1));
            });
            await generation.CompleteSourceReleasesAsync(null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(generation.HasPendingSourceReleases, Is.False);
        }

        /// <summary>
        /// Cancellation before retry does not invoke the provider or discard its cleanup.
        /// </summary>
        [Test]
        public async Task PendingSourceCleanupCancellationPreservesWorkWithoutInvokingProviderAsync()
        {
            using MonitoredItemQueueFactory queues = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queues));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            int calls = 0;
            generation.RetainSourceRelease(item, 0, _ =>
            {
                calls++;
                return default;
            });
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await generation.CompleteSourceReleasesAsync(item, new CancellationToken(true))
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(calls, Is.Zero);
            Assert.That(generation.HasPendingSourceReleases, Is.True);
            await generation.CompleteSourceReleasesAsync(item, CancellationToken.None).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(generation.HasPendingSourceReleases, Is.False);
        }

        /// <summary>
        /// Distinct failed callbacks on one incarnation retain independent cleanup progress.
        /// </summary>
        [Test]
        public async Task PendingSourceCleanupPreservesDistinctWorkOnTheSameRevisionAsync()
        {
            using MonitoredItemQueueFactory queues = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queues));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            var completed = new List<int>();
            generation.RetainSourceRelease(item, 0, _ =>
            {
                completed.Add(1);
                return default;
            });
            generation.RetainSourceRelease(item, 0, _ =>
            {
                completed.Add(2);
                return default;
            });
            await generation.CompleteSourceReleasesAsync(item, CancellationToken.None).ConfigureAwait(false);
            await generation.CompleteSourceReleasesAsync(item, CancellationToken.None).ConfigureAwait(false);
            Assert.That(completed, Has.Count.EqualTo(2));
            Assert.That(completed[0], Is.EqualTo(1));
            Assert.That(completed[1], Is.EqualTo(2));
            Assert.That(generation.HasPendingSourceReleases, Is.False);
        }

        /// <summary>
        /// One failing provider cannot prevent independent cleanup progress within its generation.
        /// </summary>
        [Test]
        public async Task PendingSourceCleanupRetainsFailuresWithoutStarvingOtherItemsAsync()
        {
            using MonitoredItemQueueFactory queues = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queues));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            var other = new Mock<IMonitoredItem>().Object;
            var failure = new InvalidOperationException("Injected source-provider cleanup failure.");
            bool fail = true;
            int failedCalls = 0;
            int otherCalls = 0;
            generation.RetainSourceRelease(item, 0, _ =>
            {
                failedCalls++;
                if (fail)
                {
                    throw failure;
                }
                return default;
            });
            generation.RetainSourceRelease(other, 0, _ =>
            {
                otherCalls++;
                return default;
            });
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await generation.CompleteSourceReleasesAsync(null, CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(observed, Is.SameAs(failure));
                Assert.That(otherCalls, Is.EqualTo(1));
                Assert.That(failedCalls, Is.EqualTo(1));
                Assert.That(generation.CapturePendingSourceItems(), Is.EqualTo(new[] { item }));
            });
            fail = false;
            await generation.CompleteSourceReleasesAsync(null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(otherCalls, Is.EqualTo(1));
            Assert.That(failedCalls, Is.EqualTo(2));
            Assert.That(generation.HasPendingSourceReleases, Is.False);
        }

        /// <summary>
        /// Construction alone does not enroll an item in slot-local recovery.
        /// </summary>
        [Test]
        public void NewItemHasNoUnresolvedClaim()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));

            Assert.That(GetBoundGeneration(item).Slot.UnresolvedItems, Is.Empty);
            Assert.That(((IMonitoredItemGenerationBinding)item).IsRemoved, Is.False);
        }

        /// <summary>
        /// Generation membership begins at subscription ownership, not item construction.
        /// </summary>
        [Test]
        public void SubscriptionClaimTracksTheExactGenerationAndOwner()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            var owner = new Mock<ISubscription>().Object;
            var binding = (IMonitoredItemGenerationBinding)item;

            Assert.That(generation.MonitoredItems, Is.Empty);
            item.SubscriptionCallback = owner;
            Assert.That(generation.MonitoredItems, Is.Empty);
            binding.BindSubscription(owner);
            binding.BindSubscription(owner);

            Assert.That(generation.MonitoredItems, Has.Count.EqualTo(1));
            Assert.That(generation.MonitoredItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
            Assert.That(actualOwner, Is.SameAs(owner));
            binding.UnbindSubscription(new Mock<ISubscription>().Object);
            Assert.That(generation.MonitoredItems.ContainsKey(item), Is.True);
            Assert.That(binding.IsRemoved, Is.False);
        }

        /// <summary>
        /// Removal also closes an unclaimed item, while another subscription cannot release its ownership.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void UnclaimedItemReleaseUsesExactSubscriptionIdentity(bool failedClaim, bool matchesOwner)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = GetBoundGeneration(item);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            if (failedClaim)
            {
                Assert.That(() => binding.BindSubscription(new Mock<ISubscription>().Object),
                    Throws.InvalidOperationException);
            }

            binding.UnbindSubscription(matchesOwner ? owner : new Mock<ISubscription>().Object);
            Assert.That(binding.IsRemoved, Is.EqualTo(matchesOwner));
            Assert.That(binding.IsDisposed, Is.False);
            Assert.That(item.SubscriptionCallback, Is.SameAs(owner));
            if (matchesOwner)
            {
                ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                    ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object()));
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                Assert.That(() => binding.BindSubscription(owner), Throws.InvalidOperationException);
                Assert.That(item.NodeManager, Is.SameAs(original.NodeManager));
                Assert.That(original.MonitoredItems, Is.Empty);
                Assert.That(replacement.MonitoredItems, Is.Empty);
            }
            else
            {
                ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object());
                binding.BindSubscription(owner);
                Assert.That(replacement.MonitoredItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
                Assert.That(actualOwner, Is.SameAs(owner));
                Assert.That(binding.IsRemoved, Is.False);
            }
        }

        /// <summary>
        /// A handoff moves the subscription claim without changing item identity.
        /// </summary>
        [Test]
        public void RebindTransfersSubscriptionMembershipToTheReplacementGeneration()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = GetBoundGeneration(item);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            uint id = item.Id;

            ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object());

            Assert.That(original.MonitoredItems, Is.Empty);
            Assert.That(replacement.MonitoredItems, Has.Count.EqualTo(1));
            Assert.That(replacement.MonitoredItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
            Assert.That(actualOwner, Is.SameAs(owner));
            Assert.That(item.Generation!.Identifies(replacement), Is.True);
            Assert.That(item.Id, Is.EqualTo(id));
        }

        /// <summary>
        /// Preparation and rollback cannot overwrite a newer binding, including within the same generation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SourceRevisionRejectsStaleBindingAndCleanup(bool sameGeneration)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = GetBoundGeneration(item);
            NodeManagerGeneration replacement = sameGeneration
                ? original
                : NodeManagerGenerationRegistry.CreateReplacement(original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            long preparedRevision = binding.SourceRevision;
            var handle = new object();
            Assert.That(
                binding.TryBindSource(preparedRevision, replacement.NodeManager, handle, out long boundRevision),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(boundRevision, Is.Not.EqualTo(preparedRevision));
                Assert.That(binding.SourceRevision, Is.EqualTo(boundRevision));
                Assert.That(binding.TryClearSource(preparedRevision), Is.False);
                Assert.That(binding.TryBindSource(preparedRevision, original.NodeManager, new object(), out _),
                    Is.False);
                Assert.That(item.NodeManager, Is.SameAs(replacement.NodeManager));
                Assert.That(item.ManagerHandle, Is.SameAs(handle));
                Assert.That(replacement.MonitoredItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
                Assert.That(actualOwner, Is.SameAs(owner));
            });
            Assert.That(binding.TryClearSource(boundRevision), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(binding.SourceRevision, Is.EqualTo(boundRevision));
                Assert.That(item.NodeManager, Is.Null);
                Assert.That(item.ManagerHandle, Is.Null);
                Assert.That(replacement.MonitoredItems, Is.Empty);
                Assert.That(replacement.Slot.UnresolvedItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
                Assert.That(actualOwner, Is.SameAs(owner));
                Assert.That(binding.TryBindSource(preparedRevision, original.NodeManager, new object(), out _),
                    Is.False);
            });
            Assert.That(binding.TryBindSource(boundRevision, replacement.NodeManager, handle, out _), Is.True);
            Assert.That(replacement.Slot.UnresolvedItems, Is.Empty);
            Assert.That(item.ManagerHandle, Is.SameAs(handle));
        }

        /// <summary>
        /// Neither ordering of deletion and handoff can restore a removed subscription claim.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SubscriptionReleaseAndRebindCannotResurrectMembership(bool releaseFirst)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = GetBoundGeneration(item);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            if (releaseFirst)
            {
                binding.UnbindSubscription(owner);
                ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                    ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object()));
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                Assert.That(item.NodeManager, Is.SameAs(original.NodeManager));
            }
            else
            {
                ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object());
            }

            binding.UnbindSubscription(owner);

            Assert.That(original.MonitoredItems, Is.Empty);
            Assert.That(replacement.MonitoredItems, Is.Empty);
            Assert.That(binding.IsDisposed, Is.False);
            Assert.That(binding.IsRemoved, Is.True);
            Assert.That(() => binding.BindSubscription(owner), Throws.InvalidOperationException);
            Assert.That(item.SubscriptionCallback, Is.SameAs(owner),
                "Manager cleanup still needs the callback after subscription ownership ends.");
        }

        /// <summary>
        /// Concurrent handoff and subscription removal leave neither generation retaining the item.
        /// </summary>
        [Test]
        public async Task SubscriptionReleaseRacingRebindLeavesNoGenerationMembershipAsync()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration original = GetBoundGeneration(item);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);

            await Task.WhenAll(
                Task.Run(() =>
                {
                    try
                    {
                        ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object());
                    }
                    catch (ServiceResultException failure) when (
                        failure.StatusCode == StatusCodes.BadMonitoredItemIdInvalid)
                    {
                        Assert.That(item.NodeManager, Is.SameAs(original.NodeManager));
                    }
                }),
                Task.Run(() => binding.UnbindSubscription(owner))).ConfigureAwait(false);

            Assert.That(original.MonitoredItems, Is.Empty);
            Assert.That(replacement.MonitoredItems, Is.Empty);
            Assert.That(binding.IsDisposed, Is.False);
            Assert.That(binding.IsRemoved, Is.True);
            Assert.That(() => binding.BindSubscription(owner), Throws.InvalidOperationException);
        }

        /// <summary>
        /// Disposal releases the generation claim and refuses later attempts to reclaim the item.
        /// </summary>
        [Test]
        public void DisposedItemCannotReenterGenerationMembership()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);

            item.Dispose();

            Assert.That(generation.MonitoredItems, Is.Empty);
            Assert.That(binding.IsDisposed, Is.True);
            Assert.That(binding.IsRemoved, Is.True);
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                ((IDetachableMonitoredItem)item).Rebind(new Mock<IAsyncNodeManager>().Object, new object()));
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(item.NodeManager, Is.SameAs(generation.NodeManager));
            Assert.That(() => binding.BindSubscription(owner), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(generation.MonitoredItems, Is.Empty);
        }

        /// <summary>
        /// An item detached from its node does not count as an active source binding.
        /// </summary>
        [Test]
        public void DetachedSubscriptionItemDoesNotBlockItsOriginalGeneration()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queueFactory);
            using MonitoredItem item = CreateItem(server);
            NodeManagerGeneration generation = GetBoundGeneration(item);
            IAsyncNodeManager manager = generation.NodeManager;
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            Assert.That(NodeManagerLifecycle.HasActiveMonitoredItems(server.Object, manager), Is.True);

            var binding = (IMonitoredItemGenerationBinding)item;
            Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);

            Assert.That(generation.MonitoredItems, Is.Empty);
            Assert.That(NodeManagerLifecycle.HasActiveMonitoredItems(server.Object, manager), Is.False);
            ((IDetachableMonitoredItem)item).Rebind(manager, new object());
            Assert.That(NodeManagerLifecycle.HasActiveMonitoredItems(server.Object, manager), Is.True);
        }

        /// <summary>
        /// A lost item belongs to its stable slot until a compatible generation adopts it.
        /// </summary>
        [Test]
        public void LostItemRemainsOnItsSlotUntilRebound()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queueFactory);
            server.Setup(value => value.CoreNodeManager).Returns(new Mock<ICoreNodeManager>().Object);
            using MonitoredItem item = CreateItem(server);
            NodeManagerGeneration original = GetBoundGeneration(item);
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                original, new Mock<IAsyncNodeManager>().Object);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.Detach(server.Object);
            ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);
            ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);

            Assert.That(original.Slot.UnresolvedItems, Has.Count.EqualTo(1));
            Assert.That(original.Slot.UnresolvedItems.TryGetValue(item, out ISubscription? actualOwner), Is.True);
            Assert.That(actualOwner, Is.SameAs(owner));
            Assert.That(item.NodeManager, Is.Null, "A lost item has no synthetic parking manager.");
            Assert.That(item.Generation, Is.Null);

            lifecycle.Rebind(replacement.NodeManager, new object());

            Assert.That(original.Slot.UnresolvedItems, Is.Empty);
            Assert.That(replacement.MonitoredItems.ContainsKey(item), Is.True);
        }

        /// <summary>
        /// Subscription removal releases unresolved ownership as well as generation membership.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LostItemReleasesSlotOwnershipWhenDeleted(bool dispose)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerSlot slot = GetBoundGeneration(item).Slot;
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            binding.Invalidate(terminal: false);
            Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);

            if (dispose)
            {
                item.Dispose();
            }
            else
            {
                binding.UnbindSubscription(owner);
            }

            Assert.That(slot.UnresolvedItems, Is.Empty);
        }

        /// <summary>
        /// Terminal invalidation removes even an existing recovery claim and permanently refuses adoption.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TerminalInvalidationRejectsRebindingAndRemovesRecoveryClaim(bool alreadyLost)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerGeneration generation = GetBoundGeneration(item);
            NodeManagerSlot slot = generation.Slot;
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            if (alreadyLost)
            {
                binding.Invalidate(terminal: false);
                Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);
            }

            binding.Invalidate(terminal: true);
            binding.Invalidate(terminal: false);

            Assert.That(binding.IsTerminal, Is.True);
            Assert.That(((IDetachableMonitoredItem)item).IsDetached, Is.True);
            Assert.That(((IDetachableMonitoredItem)item).IsDeleted, Is.True);
            Assert.That(slot.UnresolvedItems, Is.Empty);
            Assert.That(
                () => ((IDetachableMonitoredItem)item).Rebind(new Mock<IAsyncNodeManager>().Object, new object()),
                Throws.InvalidOperationException);
            IStoredMonitoredItem stored = item.ToStorableMonitoredItem();
            Assert.That(((IStoredMonitoredItemInvalidation)stored).IsTerminal, Is.True);
            using var restored = new MonitoredItem(
                CreateServer(telemetry, queueFactory).Object,
                generation.NodeManager,
                new object(),
                stored,
                null);
            restored.SubscriptionCallback = owner;
            var restoredBinding = (IMonitoredItemGenerationBinding)restored;
            restoredBinding.BindSubscription(owner);
            Assert.That(restoredBinding.IsTerminal, Is.True);
            Assert.That(slot.UnresolvedItems, Is.Empty);
            Assert.That(restoredBinding.IsDisposed, Is.False);
            Assert.That(
                () => ((IDetachableMonitoredItem)restored).Rebind(generation.NodeManager, new object()),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// Existing persistence implementations restore without having to implement the new optional capability.
        /// </summary>
        [Test]
        public void LegacyStoredItemRestoresWithoutTerminalInvalidation()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            var stored = new TestStoredMonitoredItem
            {
                Id = 2,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId(1),
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.All,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(new Variant(0)),
                IsDeleted = true,
                IsDetached = true
            };
            Assert.That(stored, Is.Not.InstanceOf<IStoredMonitoredItemInvalidation>());
            using var item = new MonitoredItem(
                CreateServer(telemetry, queueFactory).Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored,
                null);

            Assert.That(((IMonitoredItemGenerationBinding)item).IsTerminal, Is.False);
            var lifecycle = (IDetachableMonitoredItem)item;
            var replacement = new Mock<IAsyncNodeManager>().Object;
            var handle = new object();
            lifecycle.Rebind(replacement, handle);
            Assert.Multiple(() =>
            {
                Assert.That(item.NodeManager, Is.SameAs(replacement));
                Assert.That(item.ManagerHandle, Is.SameAs(handle));
                Assert.That(lifecycle.IsDetached, Is.False);
                Assert.That(lifecycle.IsDeleted, Is.False);
            });
        }

        /// <summary>
        /// Repeated reconciliation reports one lost-node notification without changing the client handle.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedInvalidationPublishesOneBadNodeIdUnknown(bool terminal)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.Invalidate(terminal);
            binding.Invalidate(terminal);
            using var context = new OperationContext(item);
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            bool more = item.Publish(context, notifications, diagnostics, 10, telemetry.CreateLogger<MonitoredItem>());

            Assert.That(more, Is.False);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications.Peek().ClientHandle, Is.EqualTo(3));
            Assert.That(notifications.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            binding.Invalidate(terminal);
            notifications.Clear();
            diagnostics.Clear();
            more = item.Publish(context, notifications, diagnostics, 10, telemetry.CreateLogger<MonitoredItem>());
            Assert.That(more, Is.False);
            Assert.That(notifications, Is.Empty);
        }

        /// <summary>
        /// Repeated lost-node transitions keep one recovery claim until the item is rebound.
        /// </summary>
        [Test]
        public void RepeatedDeletionKeepsOneUnresolvedClaimUntilRebound()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            var lifecycle = (IDetachableMonitoredItem)item;
            NodeManagerSlot slot = GetBoundGeneration(item).Slot;
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);

            ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);
            Assert.That(slot.UnresolvedItems.TryGetValue(item, out ISubscription? subscription), Is.True);
            Assert.That(subscription, Is.SameAs(owner));

            lifecycle.QueueNodeIdUnknown();
            Assert.That(slot.UnresolvedItems, Has.Count.EqualTo(1));

            lifecycle.Rebind(new Mock<IAsyncNodeManager>().Object, new object());
            Assert.That(slot.UnresolvedItems, Is.Empty);
        }

        /// <summary>
        /// Private lost items enter recovery only when a subscription takes ownership.
        /// </summary>
        [Test]
        public void PrivateDetachWaitsForSubscriptionOwnershipBeforeRecovery()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queueFactory);
            server.Setup(s => s.CoreNodeManager).Returns(new Mock<ICoreNodeManager>().Object);
            using MonitoredItem item = CreateItem(server);
            var lifecycle = (IDetachableMonitoredItem)item;
            NodeManagerSlot slot = GetBoundGeneration(item).Slot;

            var binding = (IMonitoredItemGenerationBinding)item;
            Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);
            Assert.That(slot.UnresolvedItems, Is.Empty);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            Assert.That(slot.UnresolvedItems, Has.Count.EqualTo(1));

            lifecycle.Detach(server.Object);
            Assert.That(lifecycle.IsDetached, Is.True);
            Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);

            lifecycle.Rebind(new Mock<IAsyncNodeManager>().Object, new object());
            Assert.That(lifecycle.IsDetached, Is.False);
            Assert.That(slot.UnresolvedItems, Is.Empty);
        }

        /// <summary>
        /// Disposed items cannot regain slot ownership after repeated lost-node transitions.
        /// </summary>
        [Test]
        public void DisposedItemCannotReclaimUnresolvedOwnership()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            NodeManagerSlot slot = GetBoundGeneration(item).Slot;
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);

            ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);
            Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);

            item.Dispose();
            Assert.That(slot.UnresolvedItems, Is.Empty);

            ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);
            Assert.That(slot.UnresolvedItems, Is.Empty);
        }

        /// <summary>
        /// Restored lost items enroll on their own slot only after a subscription claims them.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void RestoredItemClaimsOnlyItsOwnSlotsRecovery(bool isDetached, bool isDeleted)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            var stored = new StoredMonitoredItem
            {
                Id = 2,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId(1),
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.All,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(new Variant(0)),
                IsDetached = isDetached,
                IsDeleted = isDeleted
            };

            using var item = new MonitoredItem(
                CreateServer(telemetry, queueFactory).Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored,
                null);

            NodeManagerSlot slot = GetBoundGeneration(item).Slot;
            Assert.That(slot.UnresolvedItems, Is.Empty);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(owner);
            Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.EqualTo(isDetached || isDeleted));
            Assert.That(slot.UnresolvedItems, Has.Count.EqualTo(isDetached || isDeleted ? 1 : 0));
        }

        /// <summary>
        /// Requires the exact live binding rather than manufacturing an owner for an unresolved item.
        /// </summary>
        private static NodeManagerGeneration GetBoundGeneration(MonitoredItem item)
        {
            Assert.That(item.Generation, Is.Not.Null);
            Assert.That(item.Generation!.TryGetGeneration(out NodeManagerGeneration? generation), Is.True);
            Assert.That(generation, Is.Not.Null);
            return generation!;
        }

        private static MonitoredItemQueueFactory CreateQueueFactory(out ITelemetryContext telemetry)
        {
            telemetry = NUnitTelemetryContext.Create();
            return new MonitoredItemQueueFactory(telemetry);
        }

        private static Mock<IServerInternal> CreateServer(
            ITelemetryContext telemetry,
            MonitoredItemQueueFactory queueFactory)
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            server.Setup(s => s.SubscriptionStore).Returns(new Mock<ISubscriptionStore>().Object);
            server.Setup(s => s.DiagnosticsNodeManager).Returns(new Mock<IDiagnosticsNodeManager>().Object);
            return server;
        }

        private static MonitoredItem CreateItem(Mock<IServerInternal> server)
        {
            return new MonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                null!,
                1,
                2,
                new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.Value },
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                null,
                new MonitoringFilter(),
                null,
                1000,
                10,
                true,
                1000);
        }
    }
}
