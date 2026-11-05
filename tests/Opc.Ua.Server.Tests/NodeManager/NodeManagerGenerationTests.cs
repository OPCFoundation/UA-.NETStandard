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
 *
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies generation admission, retirement, quiescence, and deferred event behavior
    /// without starting a server.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerGenerationTests
    {
        /// <summary>
        /// A prepared generation refuses admission until activated, then admits a lease.
        /// </summary>
        [Test]
        public void PreparedGenerationRejectsAdmissionUntilActivated()
        {
            NodeManagerGeneration generation = CreateGeneration(NodeManagerGenerationState.Prepared);

            Assert.That(generation.Gate.TryEnter(out GenerationGate.Lease? rejected), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(generation.Gate.ActiveCount, Is.Zero);

            generation.Gate.Activate();

            Assert.That(generation.Gate.TryEnter(out GenerationGate.Lease? lease), Is.True);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(generation.Gate.ActiveCount, Is.EqualTo(1));
            lease!.Dispose();
            Assert.That(generation.IsQuiescent, Is.True);
        }

        /// <summary>
        /// Retiring with no admitted work closes the gate and makes the drain immediately complete.
        /// </summary>
        [Test]
        public async Task RetiringIdleGenerationClosesAdmissionAndDrainsImmediatelyAsync()
        {
            NodeManagerGeneration generation = CreateGeneration();

            generation.Gate.Retire();
            generation.Gate.Retire();
            Task drained = generation.Gate.WaitForDrainAsync();

            Assert.Multiple(() =>
            {
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Assert.That(generation.Gate.TryEnter(out GenerationGate.Lease? rejected), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(generation.Gate.ActiveCount, Is.Zero);
                Assert.That(generation.IsQuiescent, Is.True);
                Assert.That(drained.IsCompleted, Is.True);
            });

            await drained.ConfigureAwait(false);
        }

        /// <summary>
        /// Every admitted lease keeps retirement non-quiescent until the exact zero-active boundary.
        /// </summary>
        [Test]
        public async Task RetiringGenerationWaitsForEveryAdmittedLeaseAsync()
        {
            NodeManagerGeneration generation = CreateGeneration();
            Assert.That(generation.Gate.TryEnter(out GenerationGate.Lease? first), Is.True);
            Assert.That(generation.Gate.TryEnter(out GenerationGate.Lease? second), Is.True);
            generation.Gate.Retire();

            Task drained = generation.Gate.WaitForDrainAsync();
            bool admittedAfterRetirement = generation.Gate.TryEnter(out GenerationGate.Lease? rejected);
            Assert.Multiple(() =>
            {
                Assert.That(generation.Gate.ActiveCount, Is.EqualTo(2));
                Assert.That(admittedAfterRetirement, Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(generation.IsQuiescent, Is.False);
                Assert.That(drained.IsCompleted, Is.False);
            });

            first!.Dispose();
            first.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(generation.Gate.ActiveCount, Is.EqualTo(1));
                Assert.That(generation.IsQuiescent, Is.False);
                Assert.That(drained.IsCompleted, Is.False);
            });

            second!.Dispose();
            await drained.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(generation.Gate.ActiveCount, Is.Zero);
                Assert.That(generation.IsQuiescent, Is.True);
                Assert.That(generation.Gate.WaitForDrainAsync().IsCompleted, Is.True);
            });
        }

        /// <summary>
        /// A failed retained event is retried before later events to preserve emission order.
        /// </summary>
        [Test]
        public async Task RetainedEventDeliveryFailureRetriesBeforeLaterEventsAsync()
        {
            Mock<IAsyncNodeManager> manager = CreateNodeManager();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            var failure = new InvalidOperationException("The retained delivery failed.");
            var delivered = new List<int>();
            int failedDeliveryAttempts = 0;
            generation.ReadinessPending = true;
            Assert.That(generation.TryDeferEvent(1, () =>
            {
                if (Interlocked.Increment(ref failedDeliveryAttempts) == 1)
                {
                    return Task.FromException(failure);
                }
                delivered.Add(1);
                return Task.CompletedTask;
            }, 1, out _), Is.True);
            Assert.That(generation.TryDeferEvent(2, () =>
            {
                delivered.Add(2);
                return Task.CompletedTask;
            }, 1, out _), Is.True);
            generation.ReadinessPending = false;

            AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(
                () => generation.FlushDeferredEventsAsync()).ConfigureAwait(false);

            Assert.That(observed!.InnerExceptions, Has.Exactly(1).SameAs(failure));
            Assert.That(delivered, Is.Empty);
            Assert.That(generation.TryDeferEvent(3, () =>
            {
                delivered.Add(3);
                return Task.CompletedTask;
            }, 1, out _), Is.True);
            await generation.FlushDeferredEventsAsync().ConfigureAwait(false);
            Assert.That(failedDeliveryAttempts, Is.EqualTo(2));
            Assert.That(delivered, Has.Count.EqualTo(3));
            Assert.That(delivered[0], Is.EqualTo(1));
            Assert.That(delivered[1], Is.EqualTo(2));
            Assert.That(delivered[2], Is.EqualTo(3));
        }

        /// <summary>
        /// Retention counts event identities, preserves delivery order and drops the oldest event at capacity.
        /// </summary>
        [Test]
        public async Task DeferredEventCapacityCountsEventsAndRetainsAllItemDeliveriesAsync()
        {
            Mock<IAsyncNodeManager> manager = CreateNodeManager();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            generation.ReadinessPending = true;
            var delivered = new List<(int EventId, int DeliveryCount)>();

            for (int eventId = 1; eventId <= NodeManagerGeneration.MaxDeferredEvents; eventId++)
            {
                int retainedEventId = eventId;
                Assert.That(
                    generation.TryDeferEvent(
                        retainedEventId,
                        () =>
                        {
                            delivered.Add((retainedEventId, 2));
                            return Task.CompletedTask;
                        },
                        2,
                        out _),
                    Is.True);
                Assert.That(
                    generation.TryDeferEvent(
                        retainedEventId,
                        () =>
                        {
                            delivered.Add((retainedEventId, 3));
                            return Task.CompletedTask;
                        },
                        3,
                        out _),
                    Is.True);
            }

            int overflowEventId = NodeManagerGeneration.MaxDeferredEvents + 1;
            Assert.That(
                generation.TryDeferEvent(
                    overflowEventId,
                    () =>
                    {
                        delivered.Add((overflowEventId, 4));
                        return Task.CompletedTask;
                    },
                    4,
                    out bool overflowed),
                Is.True);
            Assert.That(overflowed, Is.True);
            Assert.That(generation.DroppedDeferredEvents, Is.EqualTo(1));
            Assert.That(generation.DroppedDeferredDeliveries, Is.EqualTo(5));

            generation.ReadinessPending = false;
            await generation.FlushDeferredEventsAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(generation.IsDeferringEvents, Is.False);
                Assert.That(
                    delivered,
                    Has.Count.EqualTo((NodeManagerGeneration.MaxDeferredEvents * 2) - 1));
                for (int eventId = 2; eventId <= NodeManagerGeneration.MaxDeferredEvents; eventId++)
                {
                    int deliveryIndex = (eventId - 2) * 2;
                    Assert.That(delivered[deliveryIndex], Is.EqualTo((eventId, 2)));
                    Assert.That(delivered[deliveryIndex + 1], Is.EqualTo((eventId, 3)));
                }
                Assert.That(delivered[delivered.Count - 1], Is.EqualTo((overflowEventId, 4)));
                Assert.That(delivered.Exists(delivery => delivery.EventId == 1), Is.False);
                Assert.That(
                    delivered.Exists(delivery => delivery.EventId == NodeManagerGeneration.MaxDeferredEvents),
                    Is.True);
                Assert.That(
                    generation.TryDeferEvent(overflowEventId + 1, () => Task.CompletedTask, 1, out _),
                    Is.False);
            });
        }

        /// <summary>
        /// Flushing waits for every admitted readiness emission and rejects premature readiness completion.
        /// </summary>
        [Test]
        public async Task DeferredEventFlushWaitsForAdmittedReadinessEmissionsAsync()
        {
            Mock<IAsyncNodeManager> manager = CreateNodeManager();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            generation.ReadinessPending = true;
            Assert.That(generation.CaptureSourceEmission(1, out bool readinessPending), Is.True);
            Assert.That(readinessPending, Is.True);
            Assert.That(generation.CaptureSourceEmission(1, out readinessPending), Is.True);
            Assert.That(readinessPending, Is.True);
            Assert.That(generation.TryDeferEvent(1, () => Task.CompletedTask, 1, out _), Is.True);

            InvalidOperationException? notReady = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generation.FlushDeferredEventsAsync()).ConfigureAwait(false);
            Assert.That(notReady!.Message, Does.Contain("readiness succeeds"));
            Assert.That(generation.IsDeferringEvents, Is.True);

            generation.ReadinessPending = false;

            Task flush = generation.FlushDeferredEventsAsync();

            Assert.That(flush.IsCompleted, Is.False);
            generation.ReleaseSourceEmission(1);
            Assert.That(flush.IsCompleted, Is.False);
            generation.ReleaseSourceEmission(1);
            await flush.ConfigureAwait(false);
            Assert.That(generation.IsDeferringEvents, Is.False);
        }

        /// <summary>
        /// Resource aliases resolve the same generation without replacing its owning facade.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SharedSynchronousFacadesResolveOneGeneration(bool standardAdapter)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager owner = CreateResourceFacade(resource, standardAdapter);
            IAsyncNodeManager alias = CreateResourceFacade(resource, false);

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner);

            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(alias), Is.SameAs(generation));
            Assert.That(
                NodeManagerGenerationRegistry.TryGet(
                    resource.ToAsyncNodeManager(), out NodeManagerGeneration? found),
                Is.True);
            Assert.That(found, Is.SameAs(generation));
            Assert.That(generation.NodeManager, Is.SameAs(owner));
            Assert.That(generation.Slot.Current, Is.SameAs(generation));
        }

        /// <summary>
        /// An alias cannot register a resource whose generation is already retiring.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RetiredResourceCannotBeRegisteredThroughAnotherFacade(bool standardAdapter)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager owner = CreateResourceFacade(resource, standardAdapter);
            IAsyncNodeManager alias = CreateResourceFacade(resource, false);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner);
            generation.Gate.Retire();

            Assert.That(
                () => NodeManagerGenerationRegistry.CreateAddition(alias),
                Throws.InvalidOperationException);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
        }

        /// <summary>
        /// A prepared resource retains one generation across different accepted facade types.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PreparedResourceAliasesShareOneGeneration(bool standardAdapter)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager owner = CreateResourceFacade(resource, standardAdapter);
            IAsyncNodeManager alias = CreateResourceFacade(resource, false);

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(owner);
            NodeManagerGeneration aliased = NodeManagerGenerationRegistry.CreateAddition(alias);
            var prepared = new PreparedNodeManager(alias, [], generation);

            Assert.That(aliased, Is.SameAs(generation));
            Assert.That(prepared.Generation, Is.SameAs(generation));
            Assert.That(generation.NodeManager, Is.SameAs(owner));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
        }

        /// <summary>
        /// An alias of a live resource cannot assign that resource to another slot.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LiveResourceCannotReplaceAnUnrelatedGenerationThroughAnotherFacade(bool standardAdapter)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager owner = CreateResourceFacade(resource, standardAdapter);
            IAsyncNodeManager alias = CreateResourceFacade(resource, false);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner);
            NodeManagerGeneration unrelated = NodeManagerGenerationRegistry.GetOrCreate(
                CreateResourceFacade(Mock.Of<INodeManager>(), false));

            Assert.That(
                () => NodeManagerGenerationRegistry.CreateReplacement(unrelated, alias),
                Throws.InvalidOperationException);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(generation.Slot.Current, Is.SameAs(generation));
            Assert.That(unrelated.Slot.Current, Is.SameAs(unrelated));
        }

        /// <summary>
        /// A synchronous round trip still identifies the native asynchronous owner.
        /// </summary>
        [Test]
        public void RoundTripAdaptersRetainNativeGenerationIdentity()
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(manager.Object));
            IAsyncNodeManager alias = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(manager.Object));

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);

            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(alias), Is.SameAs(generation));
            Assert.That(NodeManagerGenerationRegistry.GetIdentity(alias), Is.SameAs(manager.Object));
            Assert.That(generation.NodeManager, Is.SameAs(manager.Object));
        }

        /// <summary>
        /// Different synchronous resources never share a generation or slot.
        /// </summary>
        [Test]
        public void UnrelatedResourcesHaveDifferentGenerationIdentities()
        {
            NodeManagerGeneration first = NodeManagerGenerationRegistry.GetOrCreate(
                CreateResourceFacade(Mock.Of<INodeManager>(), false));
            NodeManagerGeneration second = NodeManagerGenerationRegistry.GetOrCreate(
                CreateResourceFacade(Mock.Of<INodeManager>(), false));

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Slot, Is.Not.SameAs(second.Slot));
        }

        /// <summary>
        /// Registry and facade caches do not retain a resource after its last owner is released.
        /// </summary>
        [Test]
        public void ResourceIdentityCachesDoNotRetainUnownedFacades()
        {
            (
                WeakReference<IAsyncNodeManager> facade,
                WeakReference<INodeManager> resource,
                WeakReference<NodeManagerGeneration> generation) = CreateCollectibleResourceIdentity();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(facade.TryGetTarget(out _), Is.False);
            Assert.That(resource.TryGetTarget(out _), Is.False);
            Assert.That(generation.TryGetTarget(out _), Is.False);
        }

        /// <summary>
        /// One blocked facade getter does not prevent an unrelated resource from resolving its identity.
        /// </summary>
        [Test]
        public async Task IdentityResolutionDoesNotSerializeUnrelatedResourceGettersAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = new Mock<IAsyncNodeManager>();
            blocked.SetupGet(value => value.SyncNodeManager).Returns(() =>
            {
                entered.TrySetResult(true);
                release.Wait(timeout.Token);
                return new SyncNodeManagerAdapter(blocked.Object);
            });
            Task<IAsyncNodeManager> first = Task.Factory.StartNew(
                () => NodeManagerGenerationRegistry.GetIdentity(blocked.Object),
                timeout.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                IAsyncNodeManager unrelated = CreateResourceFacade(Mock.Of<INodeManager>(), false);
                Task<IAsyncNodeManager> second = Task.Factory.StartNew(
                    () => NodeManagerGenerationRegistry.GetIdentity(unrelated),
                    timeout.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    IAsyncNodeManager resolved = await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(resolved.SyncNodeManager, Is.SameAs(unrelated.SyncNodeManager));
                }
                finally
                {
                    release.Set();
                    await second.ConfigureAwait(false);
                }
            }
            finally
            {
                release.Set();
                await first.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Ends the resource owner's stack frame before checking weak identity cache ownership.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            WeakReference<IAsyncNodeManager> Facade,
            WeakReference<INodeManager> Resource,
            WeakReference<NodeManagerGeneration> Generation) CreateCollectibleResourceIdentity()
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager facade = CreateResourceFacade(resource, false);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(facade);
            return (new(facade), new(resource), new(generation));
        }

        /// <summary>
        /// Creates a standard adapter or a custom facade for the same synchronous resource.
        /// </summary>
        private static IAsyncNodeManager CreateResourceFacade(INodeManager resource, bool standardAdapter)
        {
            if (standardAdapter)
            {
                return new AsyncNodeManagerAdapter(resource);
            }
            var facade = new Mock<IAsyncNodeManager>();
            facade.SetupGet(value => value.SyncNodeManager).Returns(resource);
            return facade.Object;
        }

        /// <summary>
        /// Creates an isolated generation in the requested initial state.
        /// </summary>
        private static NodeManagerGeneration CreateGeneration(
            NodeManagerGenerationState state = NodeManagerGenerationState.Live)
        {
            return new NodeManagerGeneration(
                new NodeManagerSlot(),
                1,
                CreateNodeManager().Object,
                state);
        }

        /// <summary>
        /// Creates a strict manager mock for server-free generation checks.
        /// </summary>
        private static Mock<IAsyncNodeManager> CreateNodeManager()
        {
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            manager.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(manager.Object));
            return manager;
        }
    }
}
