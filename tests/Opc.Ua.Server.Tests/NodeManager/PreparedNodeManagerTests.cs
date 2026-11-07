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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies private candidate identity and single-owner publication and abort transitions.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class PreparedNodeManagerTests
    {
        /// <summary>
        /// Capturing an already-bound image must not create a generation for an unregistered owner.
        /// </summary>
        [Test]
        public void MetadataOnlyChangesRejectUnregisteredImageOwners()
        {
            IAsyncNodeManager owner = new Mock<IAsyncNodeManager>().Object;
            var image = new Mock<INodeManagerReadImage>(MockBehavior.Strict);
            image.SetupGet(value => value.Owner).Returns(owner);
            var provider = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            provider.SetupGet(value => value.Registrations).Returns([]);

            Assert.That(NodeManagerGenerationRegistry.TryGet(owner, out _), Is.False);
            Assert.That(
                () => new PreparedLifecycleChanges(provider.Object, [image.Object]),
                Throws.InvalidOperationException);
            Assert.That(NodeManagerGenerationRegistry.TryGet(owner, out _), Is.False);
        }

        /// <summary>
        /// A retained disposed adapter keeps its exposed facts but releases its unpublished provider.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposedChangesReleaseProviderAndPreserveCandidateFactsAsync(bool committed)
        {
            (IPreparedChanges changes,
                WeakReference provider,
                WeakReference<IAsyncNodeManager> candidate,
                NodeManagerSlot slot,
                Guid id,
                long generation,
                string[] namespaceUris) = await CreateDisposedChangesAsync(committed).ConfigureAwait(false);
            try
            {
                CollectFully();
                NodeManagerRegistrationSnapshot snapshot = changes.Registrations[0];
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(provider.IsAlive, Is.False);
                    Assert.That(candidate.TryGetTarget(out _), Is.False);
                    Assert.That(snapshot.TryGetNodeManager(out _), Is.False);
                    Assert.That(snapshot.TryGetRegistration(out _), Is.False);
                    Assert.That(changes.IsCommitted, Is.EqualTo(committed));
                    Assert.That(changes.Registrations.Count, Is.EqualTo(1));
                    Assert.That(snapshot.Slot, Is.SameAs(slot));
                    Assert.That(snapshot.Id, Is.EqualTo(id));
                    Assert.That(snapshot.Generation, Is.EqualTo(generation));
                    Assert.That(snapshot.NamespaceUris.ToArray(), Is.EqualTo(namespaceUris));
                    Assert.That(changes.Slots[0], Is.SameAs(slot));
                    Assert.That(slot.TryGetCurrent(out _), Is.False);
                }
                await changes.DisposeAsync().ConfigureAwait(false);
                Assert.That(changes.IsCommitted, Is.EqualTo(committed));
                Assert.That(changes.Registrations[0].Slot, Is.SameAs(slot));
                Assert.That(changes.Registrations[0].Id, Is.EqualTo(id));
                Assert.That(changes.Registrations[0].Generation, Is.EqualTo(generation));
                Assert.That(changes.Registrations[0].NamespaceUris.ToArray(), Is.EqualTo(namespaceUris));
            }
            finally
            {
                GC.KeepAlive(changes);
            }
        }

        /// <summary>
        /// Weak snapshot access succeeds only while a separate registration owns the candidate.
        /// </summary>
        [Test]
        public async Task SnapshotReacquiresManagerAndRegistrationWhileIndependentOwnerRemainsAliveAsync()
        {
            (IPreparedChanges changes, NodeManagerRegistration owner, WeakReference provider) =
                await CreateDisposedChangesWithIndependentOwnerAsync().ConfigureAwait(false);
            CollectFully();

            NodeManagerRegistrationSnapshot snapshot = changes.Registrations[0];
            bool acquiredManager = snapshot.TryGetNodeManager(out IAsyncNodeManager? manager);
            bool acquiredRegistration = snapshot.TryGetRegistration(out NodeManagerRegistration? registration);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(provider.IsAlive, Is.False);
                Assert.That(acquiredManager, Is.True);
                Assert.That(manager, Is.SameAs(owner.NodeManager));
                Assert.That(acquiredRegistration, Is.True);
                Assert.That(registration, Is.SameAs(owner));
                Assert.That(registration!.NodeManager, Is.SameAs(owner.NodeManager));
                Assert.That(registration.Id, Is.EqualTo(owner.Id));
                Assert.That(registration.Generation, Is.EqualTo(owner.Generation));
                Assert.That(snapshot.Slot, Is.SameAs(owner.Slot));
            }
            GC.KeepAlive(registration);
            GC.KeepAlive(owner);
            GC.KeepAlive(changes);
        }

        /// <summary>
        /// Failed provider cleanup remains observable and retryable instead of dropping the outstanding owner.
        /// </summary>
        [Test]
        public async Task FailedChangesDisposalRetainsProviderUntilRetryAsync()
        {
            var failure = new InvalidOperationException("The private provider cleanup failed.");
            var provider = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            provider.SetupGet(value => value.Registrations).Returns([]);
            provider.SetupGet(value => value.IsCommitted).Returns(false);
            provider.SetupSequence(value => value.DisposeAsync())
                .Throws(failure)
                .Returns(default(ValueTask));
            var changes = new PreparedLifecycleChanges(provider.Object, ArrayOf<LifecycleChange>.Empty);
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await changes.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(failure));
            Assert.That(changes.IsCommitted, Is.False);
            await changes.DisposeAsync().ConfigureAwait(false);
            await changes.DisposeAsync().ConfigureAwait(false);
            provider.Verify(value => value.DisposeAsync(), Times.Exactly(2));
            Assert.That(changes.Registrations.IsEmpty, Is.True);
        }

        /// <summary>
        /// Escaped inspection metadata and its completed task do not retain the provider after cleanup.
        /// </summary>
        [TestCase("Success")]
        [TestCase("Inspection")]
        [TestCase("Validation")]
        public async Task RetainedInspectionReleasesProviderAfterValidationAsync(string outcome)
        {
            (IPreparedChanges changes,
                WeakReference provider,
                WeakReference<IAsyncNodeManager> candidate,
                Guid id,
                Task validation,
                Exception failure) = await CreateRetainedInspectionAsync(outcome).ConfigureAwait(false);
            await Task.Yield();
            CollectFully();
            try
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(provider.IsAlive, Is.False);
                    Assert.That(candidate.TryGetTarget(out _), Is.False);
                    Assert.That(changes.Registrations[0].TryGetNodeManager(out _), Is.False);
                    Assert.That(changes.Registrations[0].TryGetRegistration(out _), Is.False);
                    Assert.That(changes.Registrations[0].Id, Is.EqualTo(id));
                    Assert.That(changes.IsCommitted, Is.False);
                    Assert.That(validation.Status, Is.EqualTo(
                        outcome == "Success" ? TaskStatus.RanToCompletion : TaskStatus.Faulted));
                    if (outcome != "Success")
                    {
                        Assert.That(validation.Exception?.InnerException, Is.SameAs(failure));
                    }
                }
                await changes.DisposeAsync().ConfigureAwait(false);
                Assert.That(changes.Registrations[0].Id, Is.EqualTo(id));
            }
            finally
            {
                GC.KeepAlive(changes);
                GC.KeepAlive(validation);
                GC.KeepAlive(failure);
            }
        }

        /// <summary>
        /// Runs the real inspection adapter while the publication retains sole cleanup ownership.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            IPreparedChanges Changes,
            WeakReference Provider,
            WeakReference<IAsyncNodeManager> Candidate,
            Guid Id,
            Task Validation,
            Exception Failure)> CreateRetainedInspectionAsync(string outcome)
        {
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            manager.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(manager.Object));
            manager.SetupGet(value => value.NamespaceUris).Returns(["urn:opcua:tests:inspection"]);
            var registration = new NodeManagerRegistration(Guid.NewGuid(), 1, manager.Object);
            var provider = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            provider.SetupGet(value => value.Registrations)
                .Returns(new ArrayOf<NodeManagerRegistration>([registration]));
            provider.SetupGet(value => value.IsCommitted).Returns(false);
            provider.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var failure = new InvalidOperationException(outcome);
            var publication = new Mock<INodeManagerPublication>(MockBehavior.Strict);
            IPreparedNodeManagerBatch? publicationOwner = provider.Object;
            publication.Setup(value => value.ValidateAsync(
                    It.IsAny<ArrayOf<NodeManagerBatchChange>>(),
                    It.IsAny<Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<NodeManagerBatchChange> _,
                    Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspect,
                    CancellationToken ct) => RunInspectionValidationAsync(
                        publicationOwner ?? throw new InvalidOperationException("The publication owner was released."),
                        inspect,
                        outcome,
                        failure,
                        ct));
            publication.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            await using var writer = new LifecyclePublicationWriter(publication.Object);
            IPreparedChanges retained;
            var entered = new TaskCompletionSource<IPreparedChanges>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task validation = writer.ValidateAsync(
                [LifecycleChange.Add(Mock.Of<IAsyncNodeManagerFactory>())],
                CreateInspectionCallback(entered, release.Task, outcome == "Inspection", failure)).AsTask();
            try
            {
                retained = await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(validation.IsCompleted, Is.False);
                provider.Verify(value => value.DisposeAsync(), Times.Never);
            }
            finally
            {
                release.TrySetResult(true);
            }

            InvalidOperationException? observed = null;
            try
            {
                await validation.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                observed = exception;
            }
            if (outcome == "Success")
            {
                Assert.That(observed, Is.Null);
            }
            else
            {
                Assert.That(observed, Is.SameAs(failure));
            }
            provider.Verify(value => value.DisposeAsync(), Times.Once);
            registration.Slot.Unpublish(registration.Identity);
            publicationOwner = null;
            return (
                retained,
                new WeakReference(provider.Object),
                new WeakReference<IAsyncNodeManager>(manager.Object),
                registration.Id,
                validation,
                failure);
        }

        /// <summary>
        /// Exercises the publication-owned cleanup around the actual inspection adapter.
        /// </summary>
        private static async ValueTask RunInspectionValidationAsync(
            IPreparedNodeManagerBatch prepared,
            Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspect,
            string outcome,
            Exception failure,
            CancellationToken cancellationToken)
        {
            try
            {
                await inspect(prepared, cancellationToken).ConfigureAwait(false);
                Mock.Get(prepared).Verify(value => value.DisposeAsync(), Times.Never);
                if (outcome == "Validation")
                {
                    throw failure;
                }
            }
            finally
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates an inspection callback without capturing the provider or its candidate.
        /// </summary>
        private static Func<IPreparedChanges, CancellationToken, ValueTask> CreateInspectionCallback(
            TaskCompletionSource<IPreparedChanges> entered,
            Task release,
            bool failInspection,
            Exception failure)
        {
            return async (changes, _) =>
            {
                entered.TrySetResult(changes);
                await release.ConfigureAwait(false);
                if (failInspection)
                {
                    throw failure;
                }
            };
        }

        /// <summary>
        /// Separates provider construction and completed disposal from the collection assertion.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            IPreparedChanges Changes,
            WeakReference Provider,
            WeakReference<IAsyncNodeManager> Candidate,
            NodeManagerSlot Slot,
            Guid Id,
            long Generation,
            string[] NamespaceUris)> CreateDisposedChangesAsync(bool committed)
        {
            string[] namespaceUris = ["urn:opcua:tests:prepared-snapshot"];
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            manager.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(manager.Object));
            manager.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            var registration = new NodeManagerRegistration(Guid.NewGuid(), 1, manager.Object);
            var provider = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            provider.SetupGet(value => value.Registrations).Returns(new ArrayOf<NodeManagerRegistration>([registration]));
            provider.SetupGet(value => value.IsCommitted).Returns(committed);
            provider.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var changes = new PreparedLifecycleChanges(
                provider.Object, [LifecycleChange.Add(Mock.Of<IAsyncNodeManagerFactory>())]);
            var providerReference = new WeakReference(provider.Object);
            var candidateReference = new WeakReference<IAsyncNodeManager>(manager.Object);
            registration.Slot.Unpublish(registration.Identity);
            await changes.DisposeAsync().ConfigureAwait(false);
            provider.Verify(value => value.DisposeAsync(), Times.Once);
            return (
                changes,
                providerReference,
                candidateReference,
                registration.Slot,
                registration.Id,
                registration.Generation,
                namespaceUris);
        }

        /// <summary>
        /// Disposes the provider but deliberately leaves a separate registration as the candidate owner.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(IPreparedChanges Changes, NodeManagerRegistration Owner, WeakReference Provider)>
            CreateDisposedChangesWithIndependentOwnerAsync()
        {
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            manager.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(manager.Object));
            manager.SetupGet(value => value.NamespaceUris).Returns(["urn:opcua:tests:prepared-owner"]);
            var owner = new NodeManagerRegistration(Guid.NewGuid(), 1, manager.Object);
            var provider = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            provider.SetupGet(value => value.Registrations)
                .Returns(new ArrayOf<NodeManagerRegistration>([owner]));
            provider.SetupGet(value => value.IsCommitted).Returns(false);
            provider.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var changes = new PreparedLifecycleChanges(
                provider.Object, [LifecycleChange.Add(Mock.Of<IAsyncNodeManagerFactory>())]);
            var providerReference = new WeakReference(provider.Object);
            await changes.DisposeAsync().ConfigureAwait(false);
            provider.Verify(value => value.DisposeAsync(), Times.Once);
            return (changes, owner, providerReference);
        }

        /// <summary>
        /// Performs a full collection cycle without extending any target's lifetime.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectFully()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Every required construction input is checked before candidate ownership is established.
        /// </summary>
        [TestCase("nodeManager")]
        [TestCase("externalReferences")]
        [TestCase("generation")]
        public void ConstructorRejectsMissingOwnershipInputs(string parameter)
        {
            PreparedNodeManager candidate = CreateCandidate();
            Assert.That(() => new PreparedNodeManager(
                parameter == "nodeManager" ? null! : candidate.NodeManager,
                parameter == "externalReferences" ? null! : candidate.ExternalReferences,
                parameter == "generation" ? null! : candidate.Generation),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo(parameter));
        }

        /// <summary>
        /// A candidate cannot borrow another resource's identity.
        /// </summary>
        [Test]
        public void ConstructorRejectsForeignGeneration()
        {
            PreparedNodeManager candidate = CreateCandidate();
            Assert.That(() => new PreparedNodeManager(Mock.Of<IAsyncNodeManager>(), [], candidate.Generation),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("generation"));
        }

        /// <summary>
        /// Replacement requires the exact preceding generation in the assigned slot.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorRejectsForeignOrSkippedPredecessor(bool sameSlot)
        {
            IAsyncNodeManager original = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration predecessor = NodeManagerGenerationRegistry.GetOrCreate(original);
            IAsyncNodeManager replacement = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration next = NodeManagerGenerationRegistry.CreateReplacement(predecessor, replacement);
            IAsyncNodeManager later = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration laterGeneration = sameSlot
                ? NodeManagerGenerationRegistry.CreateReplacement(next, later)
                : NodeManagerGenerationRegistry.CreateAddition(later);
            Assert.That(() => new PreparedNodeManager(later, [], laterGeneration, original),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("replacedNodeManager"));
        }

        /// <summary>
        /// Routing preparation consumes the already assigned identity rather than creating another one.
        /// </summary>
        [Test]
        public void PrivateCandidateRetainsIdentityAndRejectsPrematurePublication()
        {
            PreparedNodeManager candidate = CreateCandidate();
            candidate.ExternalReferences.Add(new NodeId(1), []);
            Assert.DoesNotThrow(candidate.ValidateForPublication);
            Assert.That(candidate.Publish, Throws.InvalidOperationException);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Published, Is.False);
                Assert.That(candidate.Generation, Is.SameAs(
                    NodeManagerGenerationRegistry.GetOrCreate(candidate.NodeManager)));
                Assert.That(candidate.ExternalReferences.ContainsKey(new NodeId(1)), Is.True);
                Assert.That(candidate.Generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            }
        }

        /// <summary>
        /// Concurrent private abort callers obtain exactly one destruction claim.
        /// </summary>
        [Test]
        public async Task ConcurrentAbortClaimsExactlyOneOwnerAsync()
        {
            PreparedNodeManager candidate = CreateCandidate();
            bool[] claims = await Task.WhenAll(Enumerable.Range(0, 16)
                .Select(_ => Task.Run(candidate.TryBeginAbort))).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(claims.Count(claim => claim), Is.EqualTo(1));
                Assert.That(candidate.TryBeginAbort(), Is.False);
                Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
                Assert.That(candidate.Publish, Throws.InvalidOperationException);
                Assert.That(candidate.Published, Is.False);
            }
        }

        /// <summary>
        /// A released routing reservation may close admission before the host claims private destruction.
        /// </summary>
        [Test]
        public void AbortedAdmissionStillAllowsExactlyOnePrivateCleanup()
        {
            PreparedNodeManager candidate = CreateCandidate();
            candidate.Generation.Gate.Abort();
            Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
            Assert.That(candidate.TryBeginAbort(), Is.True);
            Assert.That(candidate.TryBeginAbort(), Is.False);
        }

        /// <summary>
        /// Publication releases the predecessor and never returns destruction to private preparation.
        /// </summary>
        [Test]
        public void PublishedReplacementReleasesPredecessorAndRejectsAbortAfterRetirement()
        {
            IAsyncNodeManager original = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration previous = NodeManagerGenerationRegistry.GetOrCreate(original);
            IAsyncNodeManager replacement = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration next = NodeManagerGenerationRegistry.CreateReplacement(previous, replacement);
            var candidate = new PreparedNodeManager(replacement, [], next, original);
            Assert.That(candidate.ReplacedNodeManager, Is.SameAs(original));
            next.Gate.Activate();
            candidate.Publish();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Published, Is.True);
                Assert.That(candidate.ReplacedNodeManager, Is.Null);
                Assert.That(candidate.Generation, Is.SameAs(next));
                Assert.That(candidate.Publish, Throws.InvalidOperationException);
                Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
                Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
            }
            next.Gate.Retire();
            Assert.That(candidate.Published, Is.True);
            Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
            next.Gate.DisposeGeneration();
            Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
        }

        /// <summary>
        /// A facade that never received publication bookkeeping still cannot abort a retired identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RetiredIdentityRejectsPrivateAbortWithoutPublicationBookkeeping(bool disposed)
        {
            PreparedNodeManager candidate = CreateCandidate();
            candidate.Generation.Gate.Activate();
            candidate.Generation.Gate.Retire();
            if (disposed)
            {
                candidate.Generation.Gate.DisposeGeneration();
            }
            Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
            Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
        }

        /// <summary>
        /// Creates an isolated private candidate with its identity assigned first.
        /// </summary>
        /// <returns>The private candidate.</returns>
        private static PreparedNodeManager CreateCandidate()
        {
            IAsyncNodeManager manager = Mock.Of<IAsyncNodeManager>();
            return new PreparedNodeManager(manager, [], NodeManagerGenerationRegistry.CreateAddition(manager));
        }
    }
}
