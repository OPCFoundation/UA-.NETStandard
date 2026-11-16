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
using Microsoft.Extensions.Logging.Abstractions;
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
            int deliveries = 0;
            Assert.That(generation.TryDeferEvent(1, () =>
            {
                Interlocked.Increment(ref deliveries);
                return Task.CompletedTask;
            }, 1, out _), Is.True);

            InvalidOperationException? notReady = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generation.FlushDeferredEventsAsync()).ConfigureAwait(false);
            Assert.That(notReady!.Message, Does.Contain("readiness succeeds"));
            Assert.That(generation.IsDeferringEvents, Is.True);
            Assert.That(Volatile.Read(ref deliveries), Is.Zero);

            generation.ReadinessPending = false;

            Task flush = generation.FlushDeferredEventsAsync();

            Assert.That(flush.IsCompleted, Is.False);
            Assert.That(Volatile.Read(ref deliveries), Is.Zero);
            generation.ReleaseSourceEmission(1);
            Assert.That(flush.IsCompleted, Is.False);
            Assert.That(Volatile.Read(ref deliveries), Is.Zero);
            generation.ReleaseSourceEmission(1);
            await flush.ConfigureAwait(false);
            Assert.That(generation.IsDeferringEvents, Is.False);
            Assert.That(Volatile.Read(ref deliveries), Is.EqualTo(1));
        }

        /// <summary>
        /// Custom resource facades retain distinct continuation owners until explicitly registered as aliases.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SharedSynchronousFacadesRetainDistinctUnregisteredGenerations(bool standardAdapter)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager owner = CreateResourceFacade(resource, standardAdapter);
            IAsyncNodeManager alias = CreateResourceFacade(resource, false);

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner);

            NodeManagerGeneration aliased = NodeManagerGenerationRegistry.GetOrCreate(alias);

            Assert.That(aliased, Is.Not.SameAs(generation));
            Assert.That(aliased.NodeManager, Is.SameAs(alias));
            Assert.That(NodeManagerGenerationRegistry.GetIdentity(alias),
                Is.SameAs(NodeManagerGenerationRegistry.GetIdentity(owner)));
            Assert.That(
                NodeManagerGenerationRegistry.TryGet(
                    resource.ToAsyncNodeManager(), out NodeManagerGeneration? found),
                Is.EqualTo(standardAdapter));
            Assert.That(found, standardAdapter ? Is.SameAs(generation) : Is.Null);
            Assert.That(generation.NodeManager, Is.SameAs(owner));
            Assert.That(generation.Slot.Current, Is.SameAs(generation));
            using var point = new ContinuationPoint { Manager = alias };
            Assert.That(point.Manager, Is.SameAs(alias));
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
        /// Prepared custom facades remain independent owners of a shared physical resource.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PreparedResourceFacadesKeepExactGenerations(bool standardAdapter)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager owner = CreateResourceFacade(resource, standardAdapter);
            IAsyncNodeManager alias = CreateResourceFacade(resource, false);

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(owner);
            NodeManagerGeneration aliased = NodeManagerGenerationRegistry.CreateAddition(alias);
            var prepared = new PreparedNodeManager(alias, [], aliased);
            ArgumentException? wrongOwner = Assert.Throws<ArgumentException>(
                () => new PreparedNodeManager(alias, [], generation));

            Assert.That(aliased, Is.Not.SameAs(generation));
            Assert.That(prepared.Generation, Is.SameAs(aliased));
            Assert.That(wrongOwner!.ParamName, Is.EqualTo("generation"));
            Assert.That(aliased.Slot, Is.Not.SameAs(generation.Slot));
            Assert.That(generation.NodeManager, Is.SameAs(owner));
            Assert.That(aliased.NodeManager, Is.SameAs(alias));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.That(aliased.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(alias), Is.SameAs(aliased));
            Assert.That(generation.ResourceOwnership, Is.SameAs(aliased.ResourceOwnership));
        }

        /// <summary>
        /// An independent facade can replace another slot without changing an existing shared resource owner.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void IndependentFacadeCanReplaceUnrelatedSlotWhileResourceRemainsLive(bool standardAdapter)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager owner = CreateResourceFacade(resource, standardAdapter);
            IAsyncNodeManager alias = CreateResourceFacade(resource, false);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner);
            NodeManagerGeneration unrelated = NodeManagerGenerationRegistry.GetOrCreate(
                CreateResourceFacade(Mock.Of<INodeManager>(), false));

            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(unrelated, alias);
            Assert.That(replacement, Is.Not.SameAs(generation));
            Assert.That(replacement.NodeManager, Is.SameAs(alias));
            Assert.That(replacement.Slot, Is.SameAs(unrelated.Slot));
            Assert.That(replacement.Number, Is.EqualTo(unrelated.Number + 1));
            Assert.That(replacement.ResourceOwnership, Is.SameAs(generation.ResourceOwnership));
            Assert.That(replacement.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(generation.Slot.Current, Is.SameAs(generation));
            Assert.That(unrelated.Slot.Current, Is.SameAs(unrelated));
        }

        /// <summary>
        /// Independent facades retain shared producers and admissions in either retirement order.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedFacadeRetirementWaitsForLiveOwnerAndAllAdmissionsAsync(bool retireSecondFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var routing = new NodeManagerRoutingTable();
            var resource = new Mock<INodeManager>();
            resource.As<IDisposable>();
            resource.SetupGet(value => value.NamespaceUris).Returns(s_sharedResourceNamespaceUris);
            var first = new Mock<IAsyncNodeManager>();
            var second = new Mock<IAsyncNodeManager>();
            var producersStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int preparations = 0;
            foreach (Mock<IAsyncNodeManager> manager in new[] { first, second })
            {
                manager.SetupGet(value => value.SyncNodeManager).Returns(resource.Object);
                manager.As<IAsyncDisposable>();
                manager.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
                {
                    if (Interlocked.Increment(ref preparations) == 2)
                    {
                        producersStopped.TrySetResult(true);
                    }
                    return default;
                });
            }
            var server = new Mock<IServerInternal>();
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
            server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
            var host = new Mock<IDynamicNodeManagerHost>();
            routing.Add(first.Object, [1]);
            routing.Add(second.Object, [1]);
            NodeManagerGeneration removed = routing.GetGeneration(retireSecondFirst ? second.Object : first.Object);
            NodeManagerGeneration retained = routing.GetGeneration(retireSecondFirst ? first.Object : second.Object);
            Assert.That(removed, Is.Not.SameAs(retained));
            Assert.That(removed.ResourceOwnership, Is.SameAs(retained.ResourceOwnership));
            Assert.That(retained.Gate.TryEnter(out GenerationGate.Lease? admission), Is.True);
            var firstRetirement = new GenerationReclaimer.RetiredGeneration(
                server.Object, host.Object, removed, [], needsDetachment: false);
            var lastRetirement = new GenerationReclaimer.RetiredGeneration(
                server.Object, host.Object, retained, [], needsDetachment: false);
            try
            {
                routing.Remove(removed.NodeManager);
                reclaimer.Enqueue([firstRetirement]);
                reclaimer.Activate([firstRetirement]);
                await reclaimer.ReclaimReadyAsync([firstRetirement]).AsTask().WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(firstRetirement.Retirement.IsCompleted, Is.False);
                Assert.That(retained.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(preparations, Is.Zero);
                host.Verify(value => value.DestroyAddressSpaceAsync(
                    It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()), Times.Never);
                first.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Never);
                second.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Never);

                reclaimer.Schedule();
                routing.Remove(retained.NodeManager);
                reclaimer.Enqueue([lastRetirement]);
                reclaimer.Activate([lastRetirement]);
                Task completion = reclaimer.CompleteShutdownAsync(timeout.Token).AsTask();
                await producersStopped.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(completion.IsCompleted, Is.False);
                host.Verify(value => value.DestroyAddressSpaceAsync(
                    It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()), Times.Never);
                first.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Never);
                second.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Never);
                admission!.Dispose();
                await completion.ConfigureAwait(false);
                await Task.WhenAll(firstRetirement.Retirement, lastRetirement.Retirement).ConfigureAwait(false);
                Assert.That(reclaimer.Count, Is.Zero);
                Assert.That(removed.Gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
                Assert.That(retained.Gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
                first.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
                second.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
                first.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Once);
                second.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Once);
                host.Verify(value => value.DestroyAddressSpaceAsync(
                    first.Object, It.IsAny<CancellationToken>()), Times.Once);
                host.Verify(value => value.DestroyAddressSpaceAsync(
                    second.Object, It.IsAny<CancellationToken>()), Times.Once);
                resource.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
                Assert.That(resource.Object.NamespaceUris, Is.EqualTo(s_sharedResourceNamespaceUris));
                Assert.That(() => NodeManagerGenerationRegistry.CreateAddition(removed.NodeManager),
                    Throws.InvalidOperationException);
                Assert.That(() => NodeManagerGenerationRegistry.CreateAddition(retained.NodeManager),
                    Throws.InvalidOperationException);
            }
            finally
            {
                admission?.Dispose();
            }
        }

        /// <summary>
        /// A borrowed physical resource keeps its tombstone, not the cleaned facade or generation.
        /// </summary>
        [Test]
        public async Task CompletedFacadeReleasesPhysicalAssociationWithoutDroppingTombstoneAsync()
        {
            (INodeManager resource, WeakReference<IAsyncNodeManager> facade,
                WeakReference<NodeManagerGeneration> generation) = await CleanupBorrowingFacadeAsync()
                .ConfigureAwait(false);
            await Task.Yield();
            CollectGenerationTargets();
            Assert.That(facade.TryGetTarget(out _), Is.False);
            Assert.That(generation.TryGetTarget(out _), Is.False);
            Assert.That(resource.NamespaceUris, Is.EqualTo(s_borrowedResourceNamespaceUris));
            Mock.Get(resource).As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
            IAsyncNodeManager anotherFacade = CreateResourceFacade(resource, false);
            Assert.That(() => NodeManagerGenerationRegistry.CreateAddition(anotherFacade),
                Throws.InvalidOperationException);
            Assert.That(() => NodeManagerGenerationRegistry.GetOrCreate(resource.ToAsyncNodeManager()),
                Throws.InvalidOperationException);
            GC.KeepAlive(resource);
        }

        /// <summary>
        /// Physical cleanup waits for a sibling verifier and excludes new recovery until its exact attempt exits.
        /// </summary>
        [Test]
        public async Task SharedPhysicalCleanupWaitsForSiblingRecoveryAdmissionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            INodeManager resource = Mock.Of<INodeManager>();
            NodeManagerGeneration first = NodeManagerGenerationRegistry.GetOrCreate(
                CreateResourceFacade(resource, false));
            NodeManagerGeneration second = NodeManagerGenerationRegistry.GetOrCreate(
                CreateResourceFacade(resource, false));
            Assert.That(second.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? verifier), Is.True);
            first.Gate.Retire();
            second.Gate.Retire();
            Task<NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease> pending =
                first.ResourceOwnership.EnterCleanupAsync(first).AsTask();
            try
            {
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(second.CleanupRecoveryGate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Assert.That(first.ResourceOwnership.CleanupStarted, Is.True);
                Assert.That(first.TryRestoreBeforeShutdown(), Is.False);
                verifier!.Dispose();
                using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease cleanup =
                    await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(second.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? denied), Is.False);
                Assert.That(denied, Is.Null);
                await cleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
                first.CleanupRecoveryGate.DisposeGeneration();
                NodeManagerGenerationRegistry.CompleteCleanup(first);
            }
            finally
            {
                verifier?.Dispose();
            }
            Assert.That(second.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? recovery), Is.True);
            recovery!.Dispose();
            using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease finalCleanup =
                await second.ResourceOwnership.EnterCleanupAsync(second).ConfigureAwait(false);
            await finalCleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
            second.CleanupRecoveryGate.DisposeGeneration();
            NodeManagerGenerationRegistry.CompleteCleanup(second);
        }

        /// <summary>
        /// An accepted private factory result protects its resource before generation assignment consumes the claim.
        /// </summary>
        [Test]
        public async Task AcceptedFactoryReservationProtectsResourceBeforeGenerationAssignmentAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            INodeManager resource = Mock.Of<INodeManager>();
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(
                CreateResourceFacade(resource, false));
            IAsyncNodeManager candidate = CreateResourceFacade(resource, false);
            NodeManagerGenerationRegistry.ReserveCandidate(candidate);
            original.Gate.Retire();
            Task<NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease> pending =
                original.ResourceOwnership.EnterCleanupAsync(original).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(original.ResourceOwnership.IsCleanupReady, Is.False);
            Assert.That(original.ResourceOwnership.CleanupStarted, Is.False);
            NodeManagerGeneration prepared = NodeManagerGenerationRegistry.CreateAddition(candidate);
            Assert.That(prepared, Is.Not.SameAs(original));
            Assert.That(prepared.NodeManager, Is.SameAs(candidate));
            Assert.That(prepared.ResourceOwnership, Is.SameAs(original.ResourceOwnership));
            Assert.That(prepared.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.That(pending.IsCompleted, Is.False);
            prepared.Gate.Abort();
            using (NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease cleanup =
                await pending.WaitAsync(timeout.Token).ConfigureAwait(false))
            {
                await cleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
                original.CleanupRecoveryGate.DisposeGeneration();
                NodeManagerGenerationRegistry.CompleteCleanup(original);
            }
            using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease finalCleanup =
                await prepared.ResourceOwnership.EnterCleanupAsync(prepared).ConfigureAwait(false);
            await finalCleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
            prepared.CleanupRecoveryGate.DisposeGeneration();
            NodeManagerGenerationRegistry.CompleteCleanup(prepared);
        }

        /// <summary>
        /// Terminal synchronization disposal waits for an active cleanup lease and rejects its stale queued waiter.
        /// </summary>
        [Test]
        public async Task TerminalPhysicalDisposalWaitsForActiveLeaseAndPendingWaiterAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(CreateNodeManager().Object);
            NodeManagerGenerationRegistry.ResourceLifetime resource = generation.ResourceOwnership;
            Assert.That(resource.Dispose, Throws.InvalidOperationException);
            generation.Gate.Abort();
            using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease cleanup =
                await resource.EnterCleanupAsync(generation).ConfigureAwait(false);
            Task<NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease> pending =
                resource.EnterCleanupAsync(generation).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            await cleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
            generation.CleanupRecoveryGate.DisposeGeneration();
            NodeManagerGenerationRegistry.CompleteCleanup(generation);
            Assert.That(resource.Dispose, Throws.Nothing);
            cleanup.Dispose();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease stale =
                    await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            Assert.That(resource.Dispose, Throws.Nothing);
            Assert.That(resource.Dispose, Throws.Nothing);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resource.EnterCleanupAsync(generation).AsTask()).ConfigureAwait(false);
            Assert.That(() => NodeManagerGenerationRegistry.CreateAddition(generation.NodeManager),
                Throws.InvalidOperationException);
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
        /// Custom adapter subclasses remain distinct logical facades over the same physical resource.
        /// </summary>
        [Test]
        public void DerivedAdaptersDoNotBecomeLogicalAliases()
        {
            INodeManager synchronous = Mock.Of<INodeManager>();
            var first = new DerivedAsyncAdapter(synchronous);
            var second = new DerivedAsyncAdapter(synchronous);
            IAsyncNodeManager firstAlias = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(first));

            var native = new Mock<IAsyncNodeManager>();
            var derivedSync = new DerivedSyncAdapter(native.Object);
            IAsyncNodeManager adaptedDerivedSync = new AsyncNodeManagerAdapter(derivedSync);
            IAsyncNodeManager canonicalDerivedSync = derivedSync.ToAsyncNodeManager();

            Assert.Multiple(() =>
            {
                Assert.That(NodeManagerGenerationRegistry.HasSameGenerationIdentity(first, second), Is.False);
                Assert.That(NodeManagerGenerationRegistry.HasSameGenerationIdentity(first, firstAlias), Is.True);
                Assert.That(NodeManagerGenerationRegistry.GetIdentity(first),
                    Is.SameAs(NodeManagerGenerationRegistry.GetIdentity(second)));
                Assert.That(NodeManagerGenerationRegistry.HasSameGenerationIdentity(
                    adaptedDerivedSync, canonicalDerivedSync), Is.True);
                Assert.That(NodeManagerGenerationRegistry.HasSameGenerationIdentity(
                    adaptedDerivedSync, native.Object), Is.False);
                Assert.That(NodeManagerGenerationRegistry.GetIdentity(adaptedDerivedSync), Is.SameAs(native.Object));
            });
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
        /// A claimed confirmation remains owned until cleanup succeeds or reconciliation is needed.
        /// </summary>
        [Test]
        public void CleanupConfirmationClaimRetainsEvidenceAfterFailure()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(handle);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();

            Assert.That(generation.HasPendingCleanupConfirmation, Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.True);
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(handle));
            Assert.That(generation.CanAttemptCleanup, Is.False);
            Assert.That(generation.TryBeginCleanupRecovery(), Is.False);

            generation.FinishCleanupConfirmationClaim(handle, completed: false);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.False);
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.HasCleanupConfirmation(handle), Is.True);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryClaimCleanupConfirmation(out claim), Is.True);
            Assert.That(claim, Is.EqualTo(handle));
            generation.FinishCleanupConfirmationClaim(handle, completed: true);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
        }

        /// <summary>
        /// An unclaimed confirmation cannot pass cleanup after its resumption request fails.
        /// </summary>
        [Test]
        public void WithdrawnCleanupConfirmationOfferKeepsCleanupBlocked()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(handle);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.WithdrawCleanupConfirmationOffer(handle);
            Assert.That(generation.TryClaimCleanupConfirmation(out _), Is.False);
            generation.EndCleanupRecovery();

            Assert.That(generation.HasPendingCleanupConfirmation, Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.False);
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.HasCleanupConfirmation(handle), Is.True);
            var later = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, handle.ParticipantId, Guid.NewGuid());
            generation.RememberCleanupConfirmation(later);
            Assert.That(generation.OfferCleanupConfirmation(later, otherFailures: false), Is.False);
            generation.EndCleanupRecovery();
            Assert.That(generation.HasCleanupConfirmation(handle), Is.True);
            Assert.That(generation.HasCleanupConfirmation(later), Is.False);
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(handle));
            generation.FinishCleanupConfirmationClaim(handle, completed: true);
        }

        /// <summary>
        /// A later recovery attempt retains an offered confirmation until its outcome is reconciled.
        /// </summary>
        [Test]
        public void NewRecoveryCannotWithdrawAnOfferedConfirmation()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(handle);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.HasCleanupConfirmation(handle), Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.False);
            Assert.That(generation.TryClaimCleanupConfirmation(out _), Is.False);
            generation.EndCleanupRecovery();
            Assert.That(generation.HasCleanupConfirmation(handle), Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.False);

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.CanAttemptCleanup, Is.True);
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(handle));
            generation.FinishCleanupConfirmationClaim(handle, completed: true);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
        }

        /// <summary>
        /// A newly confirmed failure can replace a suspended offer without losing its audited predecessor.
        /// </summary>
        [Test]
        public void NewConfirmedFailureCanSupersedeSuspendedOffer()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var participantId = Guid.NewGuid();
            var first = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            var second = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(first);
            Assert.That(generation.OfferCleanupConfirmation(first, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.False);
            generation.RememberCleanupConfirmation(second);
            Assert.That(generation.OfferCleanupConfirmation(second, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.HasCleanupConfirmation(first), Is.False);
            Assert.That(generation.HasCleanupConfirmation(second), Is.True);
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(second));
            generation.FinishCleanupConfirmationClaim(second, completed: true);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
        }

        /// <summary>
        /// A new confirmation with another retained failure cannot release a suspended offer.
        /// </summary>
        [Test]
        public void RetainedFailuresKeepSuspendedConfirmationBlocked()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var participantId = Guid.NewGuid();
            var first = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            var second = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            var last = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(first);
            Assert.That(generation.OfferCleanupConfirmation(first, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(second);
            Assert.That(generation.OfferCleanupConfirmation(second, otherFailures: true), Is.False);
            generation.EndCleanupRecovery();
            Assert.That(generation.HasCleanupConfirmation(first), Is.True);
            Assert.That(generation.HasCleanupConfirmation(second), Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.False);

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(last);
            Assert.That(generation.OfferCleanupConfirmation(last, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.HasCleanupConfirmation(first), Is.False);
            Assert.That(generation.HasCleanupConfirmation(second), Is.False);
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(last));
            generation.FinishCleanupConfirmationClaim(last, completed: true);
        }

        /// <summary>
        /// Forgetting a suspended confirmation removes its stale offer as well as its evidence.
        /// </summary>
        [Test]
        public void ForgettingSuspendedConfirmationClearsItsOffer()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(handle);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.ForgetCleanupConfirmation(handle);
            generation.EndCleanupRecovery();
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
            Assert.That(generation.CanAttemptCleanup, Is.True);
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim.HasValue, Is.False);
        }

        /// <summary>
        /// A failed claimed cleanup needs reconciliation even if another failure is later confirmed.
        /// </summary>
        [Test]
        public void FailedCleanupClaimRequiresSeparateReconciliation()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var participantId = Guid.NewGuid();
            var first = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            var second = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(first);
            Assert.That(generation.OfferCleanupConfirmation(first, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(first));
            generation.FinishCleanupConfirmationClaim(first, completed: false);

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(second);
            Assert.That(generation.OfferCleanupConfirmation(second, otherFailures: false), Is.False);
            generation.EndCleanupRecovery();
            Assert.That(generation.HasCleanupConfirmation(first), Is.True);
            Assert.That(generation.HasCleanupConfirmation(second), Is.False);
            Assert.That(generation.CanAttemptCleanup, Is.False);

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.OfferCleanupConfirmation(first, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryClaimCleanupConfirmation(out claim), Is.True);
            Assert.That(claim, Is.EqualTo(first));
            generation.FinishCleanupConfirmationClaim(first, completed: true);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
        }

        /// <summary>
        /// An attempt failure cannot leave a suspended offer eligible for another worker.
        /// </summary>
        [Test]
        public void FailedAttemptInvalidatesSuspendedOfferWithoutDroppingEvidence()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(handle);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.InvalidateOfferedCleanupConfirmation();
            generation.EndCleanupRecovery();
            Assert.That(generation.HasCleanupConfirmation(handle), Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.False);
            Assert.That(generation.TryClaimCleanupConfirmation(out _), Is.False);

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(handle));
            generation.FinishCleanupConfirmationClaim(handle, completed: true);
        }

        /// <summary>
        /// A deferred worker returns its confirmation without requiring another verification.
        /// </summary>
        [Test]
        public void DeferredCleanupReturnsItsConfirmationToTheScheduler()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(handle);
            Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(handle));

            generation.FinishCleanupConfirmationClaim(handle, completed: false, allowRetry: true);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.True);
            Assert.That(generation.CanAttemptCleanup, Is.True);
            Assert.That(generation.TryClaimCleanupConfirmation(out claim), Is.True);
            Assert.That(claim, Is.EqualTo(handle));
            generation.FinishCleanupConfirmationClaim(handle, completed: true);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
        }

        /// <summary>
        /// Audited confirmations wait for remaining failures without clearing uncertain confirmations.
        /// </summary>
        [Test]
        public void RemainingFailuresKeepCleanupBlockedUntilUncertainEvidenceIsReconciled()
        {
            NodeManagerGeneration generation = CreateGeneration();
            var participantId = Guid.NewGuid();
            var first = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            var unknown = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            var last = new NodeManagerCleanupFailureHandle(generation.CleanupRecoveryId, participantId, Guid.NewGuid());
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(first);
            Assert.That(generation.OfferCleanupConfirmation(first, otherFailures: true), Is.False);
            generation.EndCleanupRecovery();
            Assert.That(generation.CanAttemptCleanup, Is.False);
            Assert.That(generation.TryClaimCleanupConfirmation(out _), Is.False);

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(unknown);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            generation.RememberCleanupConfirmation(last);
            Assert.That(generation.OfferCleanupConfirmation(last, otherFailures: false), Is.False);
            Assert.That(generation.HasCleanupConfirmation(first), Is.False);
            Assert.That(generation.HasCleanupConfirmation(last), Is.False);
            Assert.That(generation.HasCleanupConfirmation(unknown), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.CanAttemptCleanup, Is.False);

            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(generation.OfferCleanupConfirmation(unknown, otherFailures: false), Is.True);
            generation.EndCleanupRecovery();
            Assert.That(generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim), Is.True);
            Assert.That(claim, Is.EqualTo(unknown));
            generation.FinishCleanupConfirmationClaim(unknown, completed: true);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
        }

        /// <summary>
        /// Unpublishing an old generation cannot clear its replacement from the shared slot.
        /// </summary>
        [Test]
        public void StaleUnpublishDoesNotWithdrawReplacement()
        {
            var slot = new NodeManagerSlot();
            IAsyncNodeManager originalManager = CreateNodeManager().Object;
            IAsyncNodeManager replacementManager = CreateNodeManager().Object;
            var original = new NodeManagerGeneration(slot, 1, originalManager, NodeManagerGenerationState.Live);
            var replacement = new NodeManagerGeneration(slot, 2, replacementManager, NodeManagerGenerationState.Live);

            Assert.That(slot.TryGetCurrent(out _), Is.False);
            slot.Publish(original);
            Assert.That(slot.TryGetCurrent(out NodeManagerSlotSnapshot captured), Is.True);

            slot.Publish(replacement);
            slot.Unpublish(original);

            Assert.That(slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(current.Slot, Is.SameAs(slot));
                Assert.That(current.Generation, Is.EqualTo(2));
                Assert.That(current.NodeManager, Is.SameAs(replacementManager));
                Assert.That(captured.Slot, Is.SameAs(slot));
                Assert.That(captured.Generation, Is.EqualTo(1));
                Assert.That(captured.NodeManager, Is.SameAs(originalManager));
            }

            slot.Unpublish(replacement);
            slot.Unpublish(original);
            Assert.That(slot.TryGetCurrent(out _), Is.False);
        }

        /// <summary>
        /// A generation token follows the assigned slot identifier and distinguishes other generations.
        /// </summary>
        [Test]
        public void GenerationReferenceTracksAssignedSlotAndExactGeneration()
        {
            var slot = new NodeManagerSlot();
            var otherSlot = new NodeManagerSlot();
            var original = new NodeManagerGeneration(
                slot, 1, CreateNodeManager().Object, NodeManagerGenerationState.Live);
            var replacement = new NodeManagerGeneration(
                slot, 2, CreateNodeManager().Object, NodeManagerGenerationState.Live);
            var sameNumberCandidate = new NodeManagerGeneration(
                slot, 1, original.NodeManager, NodeManagerGenerationState.Prepared);
            var unrelated = new NodeManagerGeneration(
                otherSlot, 1, CreateNodeManager().Object, NodeManagerGenerationState.Live);
            NodeManagerGenerationReference reference = original.Reference;
            Guid initialId = slot.Id;
            var registrationId = Guid.Parse("36df9618-23ff-44aa-bdcb-72fcaa291f6e");
            var ignoredId = Guid.Parse("5d539f6b-d940-4db4-b4b2-95e42fc43a45");
            otherSlot.SetRegistrationId(ignoredId);

            Assert.That(reference.SlotId, Is.EqualTo(initialId));
            slot.SetRegistrationId(registrationId);
            slot.SetRegistrationId(ignoredId);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(slot.Id, Is.EqualTo(registrationId));
                Assert.That(reference.SlotId, Is.EqualTo(registrationId));
                Assert.That(reference.Number, Is.EqualTo(1));
                Assert.That(reference.Identifies(original), Is.True);
                Assert.That(reference.Identifies(replacement), Is.False);
                Assert.That(reference.Identifies(sameNumberCandidate), Is.False);
                Assert.That(sameNumberCandidate.Reference.Identifies(original), Is.False);
                Assert.That(reference.Identifies(unrelated), Is.False);
                Assert.That(reference.TryGetGeneration(out NodeManagerGeneration? resolved), Is.True);
                Assert.That(resolved, Is.SameAs(original));
            }
        }

        /// <summary>
        /// A retained slot and generation token do not own an unpublished generation or its manager.
        /// </summary>
        [Test]
        public void UnpublishedGenerationReferenceDoesNotRetainManager()
        {
            (
                NodeManagerSlot slot,
                NodeManagerGenerationReference reference,
                WeakReference<IAsyncNodeManager> manager,
                WeakReference<NodeManagerGeneration> generation) = CreateUnpublishedGenerationReference();

            CollectGenerationTargets();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(slot.TryGetCurrent(out _), Is.False);
                Assert.That(reference.SlotId, Is.EqualTo(slot.Id));
                Assert.That(reference.Number, Is.EqualTo(1));
                Assert.That(reference.TryGetGeneration(out _), Is.False);
                Assert.That(generation.TryGetTarget(out _), Is.False);
                Assert.That(manager.TryGetTarget(out _), Is.False);
            }
            GC.KeepAlive(slot);
            GC.KeepAlive(reference);
        }

        /// <summary>
        /// A captured slot snapshot retains the manager, but not the generation, after unpublication.
        /// </summary>
        [Test]
        public void CapturedSlotSnapshotRetainsManagerAfterUnpublication()
        {
            (
                NodeManagerSlotSnapshot snapshot,
                WeakReference<IAsyncNodeManager> manager,
                WeakReference<NodeManagerGeneration> generation) = CaptureUnpublishedSlotSnapshot();

            CollectGenerationTargets();

            Assert.That(manager.TryGetTarget(out IAsyncNodeManager? retained), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.False);
                Assert.That(snapshot.Generation, Is.EqualTo(1));
                Assert.That(snapshot.NodeManager, Is.SameAs(retained));
                Assert.That(generation.TryGetTarget(out _), Is.False);
            }
            GC.KeepAlive(snapshot);
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
        /// Completes exact private cleanup outside the frame that checks borrowed-resource retention.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(INodeManager Resource, WeakReference<IAsyncNodeManager> Facade,
            WeakReference<NodeManagerGeneration> Generation)> CleanupBorrowingFacadeAsync()
        {
            var resource = new Mock<INodeManager>();
            resource.As<IDisposable>();
            resource.SetupGet(value => value.NamespaceUris).Returns(s_borrowedResourceNamespaceUris);
            var manager = new Mock<IAsyncNodeManager>();
            manager.As<IDisposable>();
            manager.SetupGet(value => value.SyncNodeManager).Returns(resource.Object);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(manager.Object);
            var facadeReference = new WeakReference<IAsyncNodeManager>(manager.Object);
            var generationReference = new WeakReference<NodeManagerGeneration>(generation);
            generation.Gate.Abort();
            using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease cleanup =
                await generation.ResourceOwnership.EnterCleanupAsync(generation).ConfigureAwait(false);
            await cleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
            bool disposed = false;
            await NodeManagerCleanupRecovery.DisposeManagerAsync(manager.Object, generation, () => disposed = true)
                .ConfigureAwait(false);
            Assert.That(disposed, Is.True);
            manager.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
            generation.CleanupRecoveryGate.DisposeGeneration();
            NodeManagerGenerationRegistry.CompleteCleanup(generation);
            return (resource.Object, facadeReference, generationReference);
        }

        /// <summary>
        /// Returns a slot and token after removing their published generation.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            NodeManagerSlot Slot,
            NodeManagerGenerationReference Reference,
            WeakReference<IAsyncNodeManager> Manager,
            WeakReference<NodeManagerGeneration> Generation) CreateUnpublishedGenerationReference()
        {
            var slot = new NodeManagerSlot();
            IAsyncNodeManager manager = CreateNodeManager().Object;
            var generation = new NodeManagerGeneration(slot, 1, manager, NodeManagerGenerationState.Live);
            slot.Publish(generation);
            slot.Unpublish(generation);
            return (slot, generation.Reference, new(manager), new(generation));
        }

        /// <summary>
        /// Captures a slot snapshot before removing its published generation.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            NodeManagerSlotSnapshot Snapshot,
            WeakReference<IAsyncNodeManager> Manager,
            WeakReference<NodeManagerGeneration> Generation) CaptureUnpublishedSlotSnapshot()
        {
            var slot = new NodeManagerSlot();
            IAsyncNodeManager manager = CreateNodeManager().Object;
            var generation = new NodeManagerGeneration(slot, 1, manager, NodeManagerGenerationState.Live);
            slot.Publish(generation);
            Assert.That(slot.TryGetCurrent(out NodeManagerSlotSnapshot snapshot), Is.True);
            slot.Unpublish(generation);
            return (snapshot, new(manager), new(generation));
        }

        /// <summary>
        /// Collects generation targets after their construction frames have returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectGenerationTargets()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
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
        /// Represents a customized asynchronous facade over a synchronous manager.
        /// </summary>
        private sealed class DerivedAsyncAdapter(INodeManager manager) : AsyncNodeManagerAdapter(manager)
        {
        }

        /// <summary>
        /// Represents a customized synchronous facade over an asynchronous manager.
        /// </summary>
        private sealed class DerivedSyncAdapter(IAsyncNodeManager manager) : SyncNodeManagerAdapter(manager)
        {
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

        /// <summary>The usable namespace retained by the independent shared producer.</summary>
        private static readonly string[] s_sharedResourceNamespaceUris = ["urn:shared-resource"];

        /// <summary>The usable namespace retained by the independently borrowed resource.</summary>
        private static readonly string[] s_borrowedResourceNamespaceUris = ["urn:borrowed-resource"];
    }
}
