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
            return manager;
        }
    }
}
