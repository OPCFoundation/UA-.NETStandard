// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Exercises intentionally retained synchronous compatibility.
// TODO: Remove the legacy cases when the synchronous API is removed.

using System;
using System.Diagnostics.CodeAnalysis;
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
        public async Task MetadataOnlyChangesRejectUnregisteredImageOwners()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            await using ILifecycleWriter writer = await harness.Lifecycle.CaptureChanges().BeginAsync()
                .ConfigureAwait(false);
            IAsyncNodeManager owner = new Mock<IAsyncNodeManager>().Object;
            var image = new Mock<INodeManagerReadImage>(MockBehavior.Strict);
            image.SetupGet(value => value.Owner).Returns(owner);
            long revision = harness.Lifecycle.Revision;
            Assert.That(NodeManagerGenerationRegistry.TryGet(owner, out _), Is.False);
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await writer.PrepareReadImagesAsync([image.Object]).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(NodeManagerGenerationRegistry.TryGet(owner, out _), Is.False);
            Assert.That(harness.Lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(writer.IsCurrent, Is.True);
        }

        /// <summary>
        /// A retained terminal native unit keeps its facts without retaining its provider or cleaned candidates.
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
            await Task.Yield();
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
        /// A disposed changes unit can recover its original registration while its manager remains available.
        /// </summary>
        [Test]
        public async Task DisposedChangesReconstructOriginalRegistrationFromRetainedManagerAsync()
        {
            string[] namespaces = ["urn:opcua:tests:original"];
            var managerMock = new Mock<IAsyncNodeManager>();
            managerMock.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            IAsyncNodeManager manager = managerMock.Object;
            (NodeManagerRegistrationSnapshot snapshot, WeakReference<NodeManagerRegistration> original) =
                await CreateDisposedChangesWithRetainedManagerAsync(manager, committed: true).ConfigureAwait(false);

            await Task.Yield();
            CollectFully();
            namespaces[0] = "urn:opcua:tests:changed";

            Assert.That(original.TryGetTarget(out _), Is.False);
            Assert.That(snapshot.Slot.Current!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.True);
            Assert.That(snapshot.TryGetRegistration(out NodeManagerRegistration? recovered), Is.True);
            Assert.That(recovered, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(recovered!.Slot, Is.SameAs(snapshot.Slot));
                Assert.That(recovered.Id, Is.EqualTo(snapshot.Id));
                Assert.That(recovered.Generation, Is.EqualTo(snapshot.Generation));
                Assert.That(recovered.NodeManager, Is.SameAs(manager));
                Assert.That(recovered.NamespaceUris.ToArray(), Is.EqualTo(s_originalNamespaceUris));
            }
            GC.KeepAlive(manager);
            GC.KeepAlive(snapshot);
        }

        /// <summary>
        /// Disposed changes cannot acquire a later candidate's slot or change its registration identifier.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposedChangesSnapshotDoesNotRebindToReusedCandidateSlotAsync(bool registerNewCandidate)
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager manager = CreateResourceFacade(resource);
            IAsyncNodeManager nextManager = CreateResourceFacade(resource);
            NodeManagerGeneration originalGeneration = NodeManagerGenerationRegistry.CreateAddition(manager);
            NodeManagerGeneration newGeneration = NodeManagerGenerationRegistry.CreateAddition(nextManager);
            (NodeManagerRegistrationSnapshot snapshot, WeakReference<NodeManagerRegistration> original) =
                await CreateDisposedChangesWithRetainedManagerAsync(manager, committed: false).ConfigureAwait(false);

            await Task.Yield();
            CollectFully();
            Assert.That(original.TryGetTarget(out _), Is.False);
            Assert.That(snapshot.Slot.Current, Is.Null);
            Assert.That(snapshot.Slot.TryGetCurrent(out _), Is.False);
            Assert.That(snapshot.TryGetNodeManager(out IAsyncNodeManager? retained), Is.True);
            Assert.That(retained, Is.SameAs(manager));

            NodeManagerGeneration oldGeneration = NodeManagerGenerationRegistry.GetOrCreate(manager);
            Assert.That(oldGeneration, Is.SameAs(originalGeneration));
            Assert.That(oldGeneration.Slot, Is.SameAs(snapshot.Slot));
            Assert.That(oldGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(() => NodeManagerGenerationRegistry.CreateAddition(manager), Throws.InvalidOperationException);
            Assert.That(newGeneration.Slot, Is.Not.SameAs(snapshot.Slot));
            Assert.That(newGeneration.Number, Is.EqualTo(snapshot.Generation));
            Assert.That(newGeneration.NodeManager, Is.SameAs(nextManager));
            Assert.That(newGeneration.ResourceOwnership, Is.SameAs(oldGeneration.ResourceOwnership));

            NodeManagerRegistration? newRegistration = registerNewCandidate
                ? new NodeManagerRegistration(
                    Guid.Parse("1741ba1f-6eee-4df8-a896-47f58a24ef0c"), newGeneration.Number, nextManager)
                : null;
            Guid newSlotId = newGeneration.Slot.Id;

            bool acquired = snapshot.TryGetRegistration(out NodeManagerRegistration? recovered);
            Assert.That(acquired, Is.True);
            Assert.That(recovered, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(newGeneration.Slot.Id, Is.EqualTo(newSlotId));
                Assert.That(recovered!.Identity, Is.SameAs(oldGeneration));
                Assert.That(recovered.Slot, Is.SameAs(snapshot.Slot));
                Assert.That(recovered.Id, Is.EqualTo(snapshot.Id));
                Assert.That(recovered.Generation, Is.EqualTo(snapshot.Generation));
                Assert.That(recovered.NodeManager, Is.SameAs(manager));
                if (newRegistration is not null)
                {
                    Assert.That(newRegistration.Slot, Is.SameAs(newGeneration.Slot));
                    Assert.That(newRegistration.Id, Is.EqualTo(newSlotId));
                }
            }
            GC.KeepAlive(oldGeneration);
            GC.KeepAlive(newRegistration);
            GC.KeepAlive(newGeneration);
            GC.KeepAlive(manager);
            GC.KeepAlive(snapshot);
        }

        /// <summary>
        /// Independent facades with matching slot and number never replace the original registration or its metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SnapshotReconstructsOnlyExactReplacementGeneration(bool retainOriginal)
        {
            IAsyncNodeManager manager = Mock.Of<IAsyncNodeManager>();
            (NodeManagerRegistrationSnapshot snapshot,
                WeakReference<NodeManagerRegistration> originalRegistration,
                WeakReference<NodeManagerGeneration> originalGeneration,
                NodeManagerRegistration? retainedRegistration,
                NodeManagerGeneration replacement) = CreateDistinctReplacementSnapshot(manager, retainOriginal);

            CollectFully();
            Assert.That(originalRegistration.TryGetTarget(out NodeManagerRegistration? registration),
                Is.EqualTo(retainOriginal));
            Assert.That(originalGeneration.TryGetTarget(out NodeManagerGeneration? original), Is.True,
                "The retained manager's exact tombstone still owns its original generation metadata.");
            Assert.That(replacement.Slot, Is.SameAs(snapshot.Slot));
            Assert.That(replacement.Number, Is.EqualTo(snapshot.Generation));
            Assert.That(replacement.NodeManager, Is.Not.SameAs(manager));
            Assert.That(replacement.ResourceOwnership, Is.SameAs(original!.ResourceOwnership));
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(replacement.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            Assert.That(snapshot.TryGetNodeManager(out IAsyncNodeManager? acquiredManager), Is.True);
            Assert.That(acquiredManager, Is.SameAs(manager));

            bool acquired = snapshot.TryGetRegistration(out NodeManagerRegistration? recovered);
            Assert.That(acquired, Is.True);
            Assert.That(recovered, Is.Not.Null);
            Assert.That(recovered!.Identity, Is.SameAs(original));
            Assert.That(recovered.Identity, Is.Not.SameAs(replacement));
            Assert.That(recovered.NodeManager, Is.SameAs(manager));
            Assert.That(recovered.Id, Is.EqualTo(snapshot.Id));
            Assert.That(recovered.Slot, Is.SameAs(snapshot.Slot));
            Assert.That(recovered.Generation, Is.EqualTo(snapshot.Generation));
            if (retainOriginal)
            {
                Assert.That(registration, Is.SameAs(retainedRegistration));
                Assert.That(recovered, Is.SameAs(retainedRegistration));
            }
            else
            {
                Assert.That(registration, Is.Null);
                Assert.That(retainedRegistration, Is.Null);
            }
            GC.KeepAlive(retainedRegistration);
            GC.KeepAlive(replacement);
            GC.KeepAlive(manager);
        }

        /// <summary>
        /// A generation mismatch cannot assign an identifier to the unrelated slot.
        /// </summary>
        [Test]
        public void RegistrationGenerationMismatchLeavesSlotIdentifierUnassigned()
        {
            IAsyncNodeManager manager = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(manager);
            Guid initialId = generation.Slot.Id;
            var rejectedId = Guid.NewGuid();

            Assert.That(
                () => new NodeManagerRegistration(rejectedId, generation.Number + 1, manager),
                Throws.InvalidOperationException);
            Assert.That(generation.Slot.Id, Is.EqualTo(initialId));

            var acceptedId = Guid.NewGuid();
            var registration = new NodeManagerRegistration(acceptedId, generation.Number, manager);
            Assert.That(registration.Id, Is.EqualTo(acceptedId));
        }

        /// <summary>
        /// An independently retained registration remains the snapshot's owner after candidate reuse.
        /// </summary>
        [Test]
        public void SnapshotReturnsRetainedOriginalRegistrationAfterCandidateReuse()
        {
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager manager = CreateResourceFacade(resource);
            IAsyncNodeManager nextManager = CreateResourceFacade(resource);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.CreateAddition(manager);
            var originalRegistration = new NodeManagerRegistration(
                Guid.Parse("df5469f5-314b-47bd-91ae-7dd0171d49a0"), original.Number, manager);
            var snapshot = new NodeManagerRegistrationSnapshot(originalRegistration);

            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateAddition(nextManager);
            original.Gate.Abort();
            Assert.That(() => NodeManagerGenerationRegistry.CreateAddition(manager), Throws.InvalidOperationException);
            var replacementRegistration = new NodeManagerRegistration(
                Guid.Parse("089b9070-e582-4b13-bb86-133048dc365c"), replacement.Number, nextManager);
            Guid replacementId = replacement.Slot.Id;

            Assert.That(snapshot.TryGetRegistration(out NodeManagerRegistration? recovered), Is.True);
            Assert.That(recovered, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(replacement.Slot, Is.Not.SameAs(original.Slot));
                Assert.That(replacement.Number, Is.EqualTo(original.Number));
                Assert.That(replacement.NodeManager, Is.SameAs(nextManager));
                Assert.That(replacement.ResourceOwnership, Is.SameAs(original.ResourceOwnership));
                Assert.That(replacementId, Is.Not.EqualTo(snapshot.Id));
                Assert.That(recovered, Is.SameAs(originalRegistration));
                Assert.That(recovered!.Slot, Is.SameAs(snapshot.Slot));
                Assert.That(recovered.Id, Is.EqualTo(snapshot.Id));
                Assert.That(recovered.NodeManager, Is.SameAs(manager));
                Assert.That(replacement.Slot.Id, Is.EqualTo(replacementId));
                Assert.That(replacementRegistration.Id, Is.EqualTo(replacementId));
            }
            GC.KeepAlive(originalRegistration);
            GC.KeepAlive(replacementRegistration);
        }

        /// <summary>
        /// Failed provider cleanup remains observable and retryable instead of dropping the outstanding owner.
        /// </summary>
        [Test]
        public async Task FailedChangesDisposalRetainsProviderUntilRetryAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var failure = new InvalidOperationException("The private candidate cleanup failed.");
            Mock<IAsyncNodeManager> candidate = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:opcua:tests:failed-native-cleanup");
            candidate.As<IDisposable>().Setup(value => value.Dispose()).Throws(failure);
            IPreparedChanges changes = await harness.Lifecycle.PrepareAsync(
                [LifecycleChange.Add(NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(candidate))])
                .ConfigureAwait(false);
            Exception? observed = await Assert.CatchAsync<Exception>(
                async () => await changes.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(observed, Is.TypeOf<AggregateException>());
            Assert.That(((AggregateException)observed!).Flatten().InnerExceptions, Does.Contain(failure));
            Assert.That(changes.IsCommitted, Is.False);
            Exception? repeated = await Assert.CatchAsync<Exception>(
                async () => await changes.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(repeated, Is.SameAs(observed));
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
            Assert.That(harness.Lifecycle.HasPendingShutdownCleanup, Is.True);
            Assert.That(harness.Lifecycle.SnapshotCleanupGenerations().Any(
                generation => ReferenceEquals(generation.NodeManager, candidate.Object)), Is.True);
            Assert.That(changes.Registrations.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A fatal allocation failure does not enter the retained-cleanup transfer path.
        /// </summary>
        [Test]
        // This verifies the fatal-error branch without exhausting the host's memory.
        // TODO: Use a shared fault-injection facility if the test infrastructure adds one.
        [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
            Justification = "The test must inject OutOfMemoryException to verify the fatal cleanup branch.")]
        public async Task OutOfMemoryDuringChangesDisposalDoesNotTransferFailureAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var failure = new OutOfMemoryException("The private candidate cleanup exhausted memory.");
            Mock<IAsyncNodeManager> candidate = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:opcua:tests:fatal-native-cleanup");
            candidate.As<IDisposable>().Setup(value => value.Dispose()).Throws(failure);
            IPreparedChanges changes = await harness.Lifecycle.PrepareAsync(
                [LifecycleChange.Add(NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(candidate))])
                .ConfigureAwait(false);

            OutOfMemoryException? observed = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await changes.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(failure));
            Assert.That(changes.IsCommitted, Is.False);
            OutOfMemoryException? repeated = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await changes.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(repeated, Is.SameAs(failure));
            candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
            Assert.That(harness.Lifecycle.HasPendingShutdownCleanup, Is.True);
        }

        /// <summary>
        /// Escaped inspection, completed task, original failure and disposed writer release the provider after cleanup.
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
                Exception failure,
                WeakReference inspection,
                WeakReference writerOwner,
                ILifecycleWriter writer) = await CreateRetainedInspectionAsync(outcome).ConfigureAwait(false);
            // Completed callbacks can still be unwinding on the thread that resumed this construction.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Yield();
                CollectFully();
            }
            try
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(provider.IsAlive, Is.False);
                    Assert.That(candidate.TryGetTarget(out _), Is.False);
                    Assert.That(inspection.IsAlive, Is.False);
                    Assert.That(writerOwner.IsAlive, Is.False);
                    Assert.That(changes.Registrations[0].TryGetNodeManager(out _), Is.False);
                    Assert.That(changes.Registrations[0].TryGetRegistration(out _), Is.False);
                    Assert.That(changes.Registrations[0].Id, Is.EqualTo(id));
                    Assert.That(changes.IsCommitted, Is.False);
                    Assert.That(writer.IsCurrent, Is.False);
                    Assert.That(validation.Status, Is.EqualTo(
                        outcome == "Success" ? TaskStatus.RanToCompletion : TaskStatus.Faulted));
                    if (outcome != "Success")
                    {
                        Assert.That(validation.Exception?.InnerException, Is.SameAs(failure));
                    }
                }
                var rejectedFactory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
                await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writer.PrepareAsync(
                    [LifecycleChange.Add(rejectedFactory.Object)]).ConfigureAwait(false)).ConfigureAwait(false);
                rejectedFactory.Verify(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                    Times.Never);
                await writer.DisposeAsync().ConfigureAwait(false);
                await changes.DisposeAsync().ConfigureAwait(false);
                Assert.That(changes.Registrations[0].Id, Is.EqualTo(id));
            }
            finally
            {
                GC.KeepAlive(changes);
                GC.KeepAlive(validation);
                GC.KeepAlive(failure);
                GC.KeepAlive(writer);
            }
        }

        /// <summary>
        /// Runs native validation while the prepared owner retains sole cleanup ownership.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            IPreparedChanges Changes,
            WeakReference Provider,
            WeakReference<IAsyncNodeManager> Candidate,
            Guid Id,
            Task Validation,
            Exception Failure,
            WeakReference Inspection,
            WeakReference WriterOwner,
            ILifecycleWriter Writer)> CreateRetainedInspectionAsync(string outcome)
        {
            NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness? harness = new();
            Mock<IAsyncNodeManager>? manager = null;
            Func<IPreparedChanges, CancellationToken, ValueTask>? inspect = null;
            try
            {
                manager = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                    "urn:opcua:tests:inspection");
                var failure = new InvalidOperationException(outcome);
                await using ILifecycleWriter writer = await harness.Lifecycle.CaptureChanges().BeginAsync()
                    .ConfigureAwait(false);
                IPreparedChanges retained;
                var entered = new TaskCompletionSource<IPreparedChanges>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                inspect = CreateInspectionCallback(
                    entered,
                    release.Task,
                    outcome == "Inspection",
                    failure,
                    harness.Lifecycle,
                    outcome == "Validation" ? harness.Data.TypeTree : null);
                var inspectionReference = new WeakReference(inspect);
                Task validation = writer.ValidateAsync(
                    [LifecycleChange.Add(NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(manager))],
                    inspect).AsTask();
                try
                {
                    retained = await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(validation.IsCompleted, Is.False);
                    manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
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
                else if (outcome == "Inspection")
                {
                    Assert.That(observed, Is.SameAs(failure));
                }
                else
                {
                    Assert.That(observed, Is.TypeOf<OptimisticPublicationConflictException>());
                    failure = observed!;
                }
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                manager.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                manager.Invocations.Clear();
                harness.ReleaseHostRecorderReferences();
                return (
                    retained,
                    new WeakReference(harness.Lifecycle),
                    new WeakReference<IAsyncNodeManager>(manager.Object),
                    retained.Registrations[0].Id,
                    validation,
                    failure,
                    inspectionReference,
                    new WeakReference(harness.Writer),
                    writer);
            }
            finally
            {
                try
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    harness.ReleaseHostRecorderReferences();
                    // The observing continuation can run before this construction frame has unwound.
                    inspect = null;
                    manager = null;
                    harness = null;
                }
            }
        }

        /// <summary>
        /// Owns the provider only during inspection so retained validation tasks must release callback ownership.
        /// </summary>
        private static Func<IPreparedChanges, CancellationToken, ValueTask> CreateInspectionCallback(
            TaskCompletionSource<IPreparedChanges> entered,
            Task release,
            bool failInspection,
            Exception failure,
            INodeManagerLifecycle provider,
            TypeTable? validationTypes)
        {
            return async (changes, _) =>
            {
                try
                {
                    entered.TrySetResult(changes);
                    await release.ConfigureAwait(false);
                    if (failInspection)
                    {
                        throw failure;
                    }
                    validationTypes?.AddSubtype(
                        new NodeId("inspection-type-conflict", 0), DataTypeIds.BaseDataType);
                }
                finally
                {
                    GC.KeepAlive(provider);
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
            NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness? harness = new();
            Mock<IAsyncNodeManager>? manager = null;
            try
            {
                manager = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(namespaceUris[0]);
                IPreparedChanges changes = await harness.Lifecycle.PrepareAsync(
                    [LifecycleChange.Add(NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(manager))])
                    .ConfigureAwait(false);
                NodeManagerRegistrationSnapshot snapshot = changes.Registrations[0];
                var providerReference = new WeakReference(harness.Lifecycle);
                var candidateReference = new WeakReference<IAsyncNodeManager>(manager.Object);
                if (committed)
                {
                    LifecycleResult published = await changes.CommitAsync().ConfigureAwait(false);
                    Assert.That(
                        published.Issues,
                        Is.Empty,
                        string.Join(
                            Environment.NewLine,
                            published.Issues.ToList().Select(issue => $"{issue.Stage}: {issue.InitialFailure}")));
                    LifecycleResult retired = await harness.Lifecycle.ApplyAsync(
                        [LifecycleChange.Remove(snapshot.Slot)]).ConfigureAwait(false);
                    Assert.That(retired.Issues, Is.Empty);
                    await retired.Retired.ConfigureAwait(false);
                }
                await changes.DisposeAsync().ConfigureAwait(false);
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                manager.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                manager.Invocations.Clear();
                harness.ReleaseHostRecorderReferences();
                return (
                    changes,
                    providerReference,
                    candidateReference,
                    snapshot.Slot,
                    snapshot.Id,
                    snapshot.Generation,
                    namespaceUris);
            }
            finally
            {
                try
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    harness.ReleaseHostRecorderReferences();
                    // Release fixture-only owners before completing the task observed by the GC assertions.
                    manager = null;
                    harness = null;
                }
            }
        }

        /// <summary>
        /// Disposes the provider but deliberately leaves a separate registration as the candidate owner.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(IPreparedChanges Changes, NodeManagerRegistration Owner, WeakReference Provider)>
            CreateDisposedChangesWithIndependentOwnerAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            Mock<IAsyncNodeManager> manager = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:opcua:tests:prepared-owner");
            IPreparedChanges changes = await harness.Lifecycle.PrepareAsync(
                [LifecycleChange.Add(NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(manager))])
                .ConfigureAwait(false);
            NodeManagerRegistration owner = LifecycleInspection.Registration(changes.Registrations[0]);
            var providerReference = new WeakReference(harness.Lifecycle);
            await changes.DisposeAsync().ConfigureAwait(false);
            manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            manager.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
            manager.Invocations.Clear();
            harness.ReleaseHostRecorderReferences();
            return (changes, owner, providerReference);
        }

        /// <summary>
        /// Disposes the prepared provider while a separate owner retains its manager.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Task<(
            NodeManagerRegistrationSnapshot Snapshot,
            WeakReference<NodeManagerRegistration> Registration)>
            CreateDisposedChangesWithRetainedManagerAsync(IAsyncNodeManager manager, bool committed)
        {
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(manager);
            var registration = new NodeManagerRegistration(
                Guid.Parse("1bd46abf-ceb4-4a81-8243-51bab3b9d1c8"), generation.Number, manager);
            if (committed)
            {
                generation.Gate.Activate();
                generation.Slot.Publish(generation);
            }
            var snapshot = new NodeManagerRegistrationSnapshot(registration);
            if (!committed)
            {
                generation.Gate.Abort();
            }
            return Task.FromResult((snapshot, new WeakReference<NodeManagerRegistration>(registration)));
        }

        /// <summary>
        /// Prepares two independent facades over one resource with matching slot and number metadata.
        /// Retaining the original registration is distinct from retaining only its manager and exact tombstone.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            NodeManagerRegistrationSnapshot Snapshot,
            WeakReference<NodeManagerRegistration> Registration,
            WeakReference<NodeManagerGeneration> Generation,
            NodeManagerRegistration? RetainedRegistration,
            NodeManagerGeneration Replacement) CreateDistinctReplacementSnapshot(
                IAsyncNodeManager manager,
                bool retainOriginal)
        {
            NodeManagerGeneration current = NodeManagerGenerationRegistry.CreateAddition(Mock.Of<IAsyncNodeManager>());
            NodeManagerGeneration original = NodeManagerGenerationRegistry.CreateReplacement(current, manager);
            var registration = new NodeManagerRegistration(Guid.NewGuid(), original.Number, manager);
            var snapshot = new NodeManagerRegistrationSnapshot(registration);
            var registrationReference = new WeakReference<NodeManagerRegistration>(registration);
            var generationReference = new WeakReference<NodeManagerGeneration>(original);
            IAsyncNodeManager replacementManager = CreateResourceFacade(new SyncNodeManagerAdapter(manager));
            NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                current, replacementManager);
            original.Gate.Abort();
            Assert.That(() => NodeManagerGenerationRegistry.CreateReplacement(current, manager),
                Throws.InvalidOperationException);
            return (snapshot, registrationReference, generationReference, retainOriginal ? registration : null,
                replacement);
        }

        /// <summary>
        /// Creates an opaque logical facade without making it a transparent alias of its physical resource.
        /// </summary>
        private static IAsyncNodeManager CreateResourceFacade(INodeManager resource)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.SetupGet(value => value.SyncNodeManager).Returns(resource);
            return manager.Object;
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

        /// <summary>The namespace retained in the original registration snapshot.</summary>
        private static readonly string[] s_originalNamespaceUris = ["urn:opcua:tests:original"];
    }
}
