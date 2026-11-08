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
    /// Verifies conflict classification without retaining prepared owners or routing images.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerConflictExceptionTests
    {
        /// <summary>
        /// A retained bound conflict and its host failure do not own the batch, routing view, or manager.
        /// </summary>
        [Test]
        public void RoutingConflictReleasesBatchAndRoutingGraph()
        {
            (NodeManagerRoutingConflictException conflict,
                WeakReference<IPreparedNodeManagerBatch> batch,
                WeakReference<NodeManagerRoutingTable.RoutingSnapshot> snapshot,
                WeakReference<IAsyncNodeManager> manager) = CreateRoutingRetentionControl();

            CollectTargets();

            var host = (NodeManagerRoutingConflictException)conflict.InnerException!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsAlive(batch), Is.False);
                Assert.That(IsAlive(snapshot), Is.False);
                Assert.That(IsAlive(manager), Is.False);
                Assert.That(conflict.IsBound, Is.True);
                Assert.That(conflict.Batch, Is.Null);
                Assert.That(conflict.RoutingRevision, Is.Null);
                Assert.That(host.IsBound, Is.False);
                Assert.That(host.Batch, Is.Null);
                Assert.That(host.RoutingRevision, Is.Null);
            }
            GC.KeepAlive(conflict);
        }

        /// <summary>
        /// A retained optimistic conflict does not own its prepared batch.
        /// </summary>
        [Test]
        public void PreparedConflictReleasesBatch()
        {
            (NodeManagerPreparedConflictException conflict,
                WeakReference<IPreparedNodeManagerBatch> batch) = CreatePreparedRetentionControl();

            CollectTargets();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsAlive(batch), Is.False);
                Assert.That(conflict.Batch, Is.Null);
                Assert.That(conflict.InnerException, Is.TypeOf<OptimisticPublicationConflictException>());
                Assert.That(conflict.Message, Is.EqualTo("Serving revision changed."));
            }
            GC.KeepAlive(conflict);
        }

        /// <summary>
        /// A classified conflict rejects a missing routing view or prepared owner.
        /// </summary>
        [Test]
        public void ConflictConstructorsRejectMissingIdentity()
        {
            var host = new NodeManagerRoutingConflictException("Routing view changed.");
            var failure = new OptimisticPublicationConflictException("Serving revision changed.");

            Assert.That(
                () => new NodeManagerRoutingConflictException(
                    "Routing view changed.", (NodeManagerRoutingTable.RoutingSnapshot)null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("routingRevision"));
            Assert.That(
                () => new NodeManagerRoutingConflictException(host, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("batch"));
            Assert.That(
                () => new NodeManagerPreparedConflictException(failure, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("batch"));
        }

        /// <summary>
        /// The compatibility adapter retries only a conflict bound to its current live candidate.
        /// </summary>
        [TestCase("Routing", "Same")]
        [TestCase("Prepared", "Same")]
        [TestCase("Routing", "Different")]
        [TestCase("Prepared", "Different")]
        [TestCase("Routing", "Collected")]
        [TestCase("Prepared", "Collected")]
        [TestCase("Routing", "Unbound")]
        [TestCase("Prepared", "Unbound")]
        public async Task CompatibilityRetriesOnlyExactLiveOwnerAsync(string kind, string ownerState)
        {
            var current = new Mock<IPreparedNodeManagerBatch>();
            var retry = new Mock<IPreparedNodeManagerBatch>();
            IPreparedNodeManagerBatch currentBatch = current.Object;
            NodeManagerRoutingTable.RoutingSnapshot snapshot = CreateSnapshot(new Mock<IAsyncNodeManager>().Object);
            IPreparedNodeManagerBatch? otherBatch = null;
            InvalidOperationException marker;
            if (ownerState == "Same")
            {
                marker = CreateConflict(kind, currentBatch, snapshot);
            }
            else if (ownerState == "Different")
            {
                otherBatch = new Mock<IPreparedNodeManagerBatch>().Object;
                marker = CreateConflict(kind, otherBatch, snapshot);
            }
            else if (ownerState == "Unbound")
            {
                marker = kind == "Routing"
                    ? new NodeManagerRoutingConflictException("Routing view changed.", snapshot)
                    : new NodeManagerPreparedConflictException("Serving revision changed.");
            }
            else
            {
                (marker, WeakReference<IPreparedNodeManagerBatch> former) =
                    CreateCollectibleConflict(kind, snapshot);
                CollectTargets();
                Assert.That(IsAlive(former), Is.False);
            }

            IPreparedNodeManagerBatch? expectedOwner = ownerState switch
            {
                "Same" => currentBatch,
                "Different" => otherBatch,
                _ => null
            };
            Assert.That(GetConflictBatch(marker), Is.SameAs(expectedOwner));
            if (marker is NodeManagerRoutingConflictException routing)
            {
                Assert.That(routing.IsBound, Is.EqualTo(ownerState != "Unbound"));
                Assert.That(routing.RoutingRevision, Is.SameAs(snapshot));
            }

            var failure = new InvalidOperationException("The prepared candidate is stale.", marker);
            current.SetupGet(value => value.IsCommitted).Returns(false);
            current.Setup(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            current.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));

            var manager = new Mock<IAsyncNodeManager>();
            manager.SetupGet(value => value.NamespaceUris).Returns(["urn:opcua:tests:conflict"]);
            var registration = new NodeManagerRegistration(Guid.NewGuid(), 1, manager.Object);
            var result = new NodeManagerBatchResult([registration], 0, null, 1, [], Task.CompletedTask);
            retry.Setup(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerBatchResult>(result));
            retry.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var lifecycle = new Mock<INodeManagerLifecycle>();
            int preparations = 0;
            lifecycle.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IPreparedNodeManagerBatch>(
                    ++preparations == 1 ? currentBatch : retry.Object));
            IAsyncNodeManagerFactory factory = new Mock<IAsyncNodeManagerFactory>().Object;

            if (ownerState == "Same")
            {
                NodeManagerRegistration actual = await lifecycle.Object.AddAsync(factory).ConfigureAwait(false);
                Assert.That(actual, Is.SameAs(registration));
            }
            else
            {
                await Assert.ThatAsync(
                    () => lifecycle.Object.AddAsync(factory).AsTask(),
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            }

            int expectedPreparations = ownerState == "Same" ? 2 : 1;
            Assert.That(preparations, Is.EqualTo(expectedPreparations));
            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(expectedPreparations));
            current.Verify(value => value.DisposeAsync(), Times.Once);
            retry.Verify(value => value.DisposeAsync(), ownerState == "Same" ? Times.Once : Times.Never);
            GC.KeepAlive(currentBatch);
            GC.KeepAlive(otherBatch);
            GC.KeepAlive(snapshot);
            GC.KeepAlive(marker);
        }

        /// <summary>
        /// A prepared conflict does not replay a decision or a committed batch.
        /// </summary>
        [TestCase("Decision")]
        [TestCase("Committed")]
        public async Task CompatibilityDoesNotReplayPreparedConflictAfterDecisionOrCommitAsync(string stage)
        {
            int decisions = 0;
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            var marker = new NodeManagerPreparedConflictException(
                new OptimisticPublicationConflictException("Serving revision changed."), prepared.Object);
            var failure = new InvalidOperationException(
                "Serving revision changed after an irreversible stage.",
                marker);
            Assert.That(marker.Batch, Is.SameAs(prepared.Object));
            prepared.SetupGet(value => value.IsCommitted).Returns(stage == "Committed");
            prepared.Setup(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Func<CancellationToken, ValueTask> decideAsync, CancellationToken token) =>
                {
                    if (stage == "Decision")
                    {
                        decisions++;
                        await decideAsync(token).ConfigureAwait(false);
                    }
                    throw failure;
                });
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);

            await Assert.ThatAsync(
                () => lifecycle.Object.AddAsync(new Mock<IAsyncNodeManagerFactory>().Object).AsTask(),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(decisions, Is.EqualTo(stage == "Decision" ? 1 : 0));
            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A bound conflict cannot be attributed to a distinct batch even if the batches compare equal.
        /// </summary>
        [TestCase("Routing")]
        [TestCase("Prepared")]
        public async Task CompatibilityDoesNotRetryValueEqualDistinctOwnerAsync(string kind)
        {
            NodeManagerRoutingTable.RoutingSnapshot snapshot = CreateSnapshot(new Mock<IAsyncNodeManager>().Object);
            var current = new ValueEqualBatch();
            var other = new ValueEqualBatch();
            InvalidOperationException marker = CreateConflict(kind, other, snapshot);
            var failure = new InvalidOperationException("Another candidate is stale.", marker);
            current.Failure = failure;
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IPreparedNodeManagerBatch>(current));

            Assert.That(current.Equals(other), Is.True);
            Assert.That(current, Is.Not.SameAs(other));
            Assert.That(GetConflictBatch(marker), Is.SameAs(other));
            await Assert.ThatAsync(
                () => lifecycle.Object.AddAsync(new Mock<IAsyncNodeManagerFactory>().Object).AsTask(),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);

            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(current.Disposals, Is.EqualTo(1));
            GC.KeepAlive(other);
            GC.KeepAlive(snapshot);
        }

        /// <summary>
        /// Captures a routing conflict without returning a strong reference to its graph.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (NodeManagerRoutingConflictException Conflict,
            WeakReference<IPreparedNodeManagerBatch> Batch,
            WeakReference<NodeManagerRoutingTable.RoutingSnapshot> Snapshot,
            WeakReference<IAsyncNodeManager> Manager) CreateRoutingRetentionControl()
        {
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            NodeManagerRoutingTable.RoutingSnapshot snapshot = CreateSnapshot(manager);
            IPreparedNodeManagerBatch batch = new Mock<IPreparedNodeManagerBatch>().Object;
            var host = new NodeManagerRoutingConflictException("Routing view changed.", snapshot);
            var conflict = new NodeManagerRoutingConflictException(host, batch);

            Assert.That(host.IsBound, Is.False);
            Assert.That(host.Batch, Is.Null);
            Assert.That(host.RoutingRevision, Is.SameAs(snapshot));
            Assert.That(conflict.IsBound, Is.True);
            Assert.That(conflict.Batch, Is.SameAs(batch));
            Assert.That(conflict.RoutingRevision, Is.SameAs(snapshot));
            Assert.That(conflict.InnerException, Is.SameAs(host));
            Assert.That(snapshot.NodeManagers[0], Is.SameAs(manager));

            return (conflict, new WeakReference<IPreparedNodeManagerBatch>(batch),
                new WeakReference<NodeManagerRoutingTable.RoutingSnapshot>(snapshot),
                new WeakReference<IAsyncNodeManager>(manager));
        }

        /// <summary>
        /// Captures an optimistic conflict without returning its prepared owner.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (NodeManagerPreparedConflictException Conflict, WeakReference<IPreparedNodeManagerBatch> Batch)
            CreatePreparedRetentionControl()
        {
            IPreparedNodeManagerBatch batch = new Mock<IPreparedNodeManagerBatch>().Object;
            var original = new OptimisticPublicationConflictException("Serving revision changed.");
            var conflict = new NodeManagerPreparedConflictException(original, batch);
            Assert.That(conflict.Batch, Is.SameAs(batch));
            Assert.That(conflict.InnerException, Is.SameAs(original));
            return (conflict, new WeakReference<IPreparedNodeManagerBatch>(batch));
        }

        /// <summary>
        /// Creates a conflict whose former owner can be collected while another candidate stays live.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (InvalidOperationException Conflict, WeakReference<IPreparedNodeManagerBatch> Batch)
            CreateCollectibleConflict(string kind, NodeManagerRoutingTable.RoutingSnapshot snapshot)
        {
            IPreparedNodeManagerBatch batch = new Mock<IPreparedNodeManagerBatch>().Object;
            InvalidOperationException conflict = CreateConflict(kind, batch, snapshot);
            Assert.That(GetConflictBatch(conflict), Is.SameAs(batch));
            return (conflict, new WeakReference<IPreparedNodeManagerBatch>(batch));
        }

        /// <summary>
        /// Creates either conflict kind with a specified owner.
        /// </summary>
        private static InvalidOperationException CreateConflict(
            string kind,
            IPreparedNodeManagerBatch batch,
            NodeManagerRoutingTable.RoutingSnapshot snapshot)
        {
            return kind switch
            {
                "Routing" => new NodeManagerRoutingConflictException(
                    new NodeManagerRoutingConflictException("Routing view changed.", snapshot), batch),
                "Prepared" => new NodeManagerPreparedConflictException(
                    new OptimisticPublicationConflictException("Serving revision changed."), batch),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Gets the live owner from either conflict kind.
        /// </summary>
        private static IPreparedNodeManagerBatch? GetConflictBatch(InvalidOperationException conflict)
        {
            return conflict switch
            {
                NodeManagerRoutingConflictException routing => routing.Batch,
                NodeManagerPreparedConflictException prepared => prepared.Batch,
                _ => throw new ArgumentOutOfRangeException(nameof(conflict))
            };
        }

        /// <summary>
        /// Creates a routing image with a real generation for the supplied manager.
        /// </summary>
        private static NodeManagerRoutingTable.RoutingSnapshot CreateSnapshot(IAsyncNodeManager manager)
        {
            return new NodeManagerRoutingTable.RoutingSnapshot(
                [manager],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>> { [2] = [manager] },
                []);
        }

        /// <summary>
        /// Runs bounded full collections after graph-creation frames have returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectTargets()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        /// <summary>
        /// Checks a weak target in a frame that cannot retain it across collection.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsAlive<T>(WeakReference<T> reference)
            where T : class
        {
            return reference.TryGetTarget(out _);
        }

        /// <summary>
        /// A prepared batch whose value equality differs from its reference identity.
        /// </summary>
        private sealed class ValueEqualBatch : IPreparedNodeManagerBatch
        {
            /// <summary>
            /// Gets or sets the failure returned when this batch commits.
            /// </summary>
            public InvalidOperationException? Failure { get; set; }

            /// <summary>
            /// Gets the number of disposal calls.
            /// </summary>
            public int Disposals { get; private set; }

            /// <summary>
            /// Gets the registrations produced by this batch.
            /// </summary>
            public ArrayOf<NodeManagerRegistration> Registrations => [];

            /// <summary>
            /// Gets whether the batch committed.
            /// </summary>
            public bool IsCommitted => false;

            /// <summary>
            /// Rejects unused read-image binding.
            /// </summary>
            public void BindReadImages(ArrayOf<INodeManagerReadImage> images)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Throws the configured conflict before the durable decision.
            /// </summary>
            public ValueTask<NodeManagerBatchResult> CommitAsync(
                Func<CancellationToken, ValueTask> decideAsync,
                CancellationToken cancellationToken = default)
            {
                throw Failure ?? new InvalidOperationException("No conflict was configured.");
            }

            /// <summary>
            /// Rejects the unused publication callback overload.
            /// </summary>
            public ValueTask<NodeManagerBatchResult> CommitAsync(
                Func<CancellationToken, ValueTask> decideAsync,
                Action publishCommittedState,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Records release of the prepared owner.
            /// </summary>
            public ValueTask DisposeAsync()
            {
                Disposals++;
                return default;
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is ValueEqualBatch;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
