// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Exercises intentionally retained synchronous compatibility.
// TODO: Remove the legacy cases when the synchronous API is removed.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.TestFramework;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Confirms the fixture's exact manager failure through its pre-start host recovery service.
        /// </summary>
        private async ValueTask ConfirmFixtureManagerCleanupAsync(
            NodeManagerGeneration generation,
            Func<NodeManagerCleanupResource, bool> verifyResource,
            CancellationToken cancellationToken = default)
        {
            m_fixtureCleanupVerifier.VerifyResource = verifyResource;
            ArrayOf<RetainedNodeManagerCleanupFailure> failures =
                await m_fixtureCleanupRecovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            RetainedNodeManagerCleanupFailure failure = failures.ToList().Single(entry =>
                entry.Handle.GenerationId == generation.CleanupRecoveryId &&
                entry.Failure.ResourceKind == "NodeManager");
            await m_fixtureCleanupRecovery.ConfirmAsync(failure.Handle, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Verified private abort recovery never invokes dependent disposal and preserves the prepared-batch failure.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PrivateAbortRecoveryPreservesOriginalFailureWithoutDisposalAsync(
            bool asynchronous,
            bool standardAdapter)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string namespaceUri = kModelNamespaceUri + ":PrivateAbortRecovery:" + Guid.NewGuid().ToString("N");
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(namespaceUri);
            var abortFailure = new IOException("Private address-space deletion did not complete.");
            manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Throws(abortFailure);
            var cleanup = new RetiredConsumerProbe(new IOException("Dependent disposal must not be invoked."));
            if (asynchronous)
            {
                manager.As<IAsyncDisposable>().Setup(value => value.DisposeAsync())
                    .Returns(() =>
                    {
                        cleanup.Dispose();
                        return default;
                    });
            }
            else
            {
                manager.As<IDisposable>().Setup(value => value.Dispose()).Callback(cleanup.Dispose);
            }
            IAsyncNodeManager registeredManager = manager.Object;
            if (standardAdapter)
            {
                var synchronous = new SyncNodeManagerAdapter(manager.Object);
                manager.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                registeredManager = new AsyncNodeManagerAdapter(synchronous);
            }
            var factory = new CountingNodeManagerFactory(
                [namespaceUri], (_, _, _) => new ValueTask<IAsyncNodeManager>(registeredManager));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IPreparedChanges? prepared = null;
            NodeManagerGeneration? generation = null;
            AggregateException? initialFailure = null;
            bool VerifyExactResource(NodeManagerCleanupResource resource)
            {
                return cleanup.IsExternallyClosed &&
                    (asynchronous
                        ? resource.TryGetAsyncDisposable(out IAsyncDisposable? asyncResource) &&
                            ReferenceEquals(asyncResource, manager.Object)
                        : resource.TryGetDisposable(out IDisposable? synchronousResource) &&
                            ReferenceEquals(synchronousResource, manager.Object));
            }
            try
            {
                prepared = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
                initialFailure = await Assert.ThrowsAsync<AggregateException>(
                    async () => await prepared.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(initialFailure!.Flatten().InnerExceptions, Does.Contain(abortFailure));
                generation = lifecycle.SnapshotCleanupGenerations().Single(candidate =>
                    ReferenceEquals(candidate.NodeManager, registeredManager));
                RetainedNodeManagerCleanupFailure retained =
                    (await m_fixtureCleanupRecovery.DiscoverAsync(timeout.Token).ConfigureAwait(false))
                        .ToList().Single(entry =>
                            entry.Handle.GenerationId == generation.CleanupRecoveryId &&
                            entry.Failure.ResourceKind == "NodeManager");
                Assert.That(retained.Failure.ResourceName, Is.Null,
                    "Discovery exposes a sanitized descriptor, not the internal cleanup-stage label.");
                Assert.That(cleanup.DisposeCount, Is.Zero);
                await Assert.ThatAsync(
                    () => prepared.DisposeAsync().AsTask(),
                    Throws.Exception.SameAs(initialFailure)).ConfigureAwait(false);
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                m_fixtureCleanupVerifier.VerifyResource = VerifyExactResource;
                await Assert.ThatAsync(
                    () => m_fixtureCleanupRecovery.ConfirmAsync(retained.Handle, timeout.Token).AsTask(),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(asynchronous
                    ? ReferenceEquals(m_fixtureCleanupVerifier.LastResource, manager.Object)
                    : ReferenceEquals(m_fixtureCleanupVerifier.LastSynchronousResource, manager.Object), Is.True);
                Assert.That(cleanup.DisposeCount, Is.Zero);

                cleanup.FinishExternalCleanup();
                await m_fixtureCleanupRecovery.ConfirmAsync(retained.Handle, timeout.Token).ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => prepared.DisposeAsync().AsTask(),
                    Throws.Exception.SameAs(initialFailure)).ConfigureAwait(false);
                Assert.That(cleanup.DisposeCount, Is.Zero);
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                Assert.That(lifecycle.SnapshotCleanupGenerations(), Does.Not.Contain(generation));
                Assert.That(generation.CleanupRecoveryGate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
                Assert.That(initialFailure.Flatten().InnerExceptions, Does.Contain(abortFailure));
            }
            finally
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                cleanup.FinishExternalCleanup();
                generation ??= lifecycle.SnapshotCleanupGenerations().FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.NodeManager, registeredManager));
                if (generation is not null)
                {
                    ArrayOf<RetainedNodeManagerCleanupFailure> remaining =
                        await m_fixtureCleanupRecovery.DiscoverAsync(cleanupTimeout.Token).ConfigureAwait(false);
                    if (remaining.ToList().Any(entry =>
                        entry.Handle.GenerationId == generation.CleanupRecoveryId &&
                        entry.Failure.ResourceKind == "NodeManager"))
                    {
                        await ConfirmFixtureManagerCleanupAsync(generation, VerifyExactResource, cleanupTimeout.Token)
                            .ConfigureAwait(false);
                    }
                }
                if (prepared is not null)
                {
                    if (initialFailure is null)
                    {
                        await prepared.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await Assert.ThatAsync(
                            () => prepared.DisposeAsync().AsTask(),
                            Throws.Exception.SameAs(initialFailure)).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Failed private and retired manager disposal retains the exact instance until verified recovery.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public async Task ManagerDisposalRequiresVerifiedRecoveryWithoutRepeatingCallbackAsync(
            bool privateCandidate,
            bool asynchronous,
            bool standardAdapter)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string namespaceUri = kModelNamespaceUri + ":ManagerRecovery:" + Guid.NewGuid().ToString("N");
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(namespaceUri);
            var failure = new IOException("The manager did not report completed disposal.");
            var cleanup = new RetiredConsumerProbe(failure);
            int failDisposal = 1;
            if (asynchronous)
            {
                manager.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(async () =>
                {
                    if (Volatile.Read(ref failDisposal) == 0)
                    {
                        return;
                    }
                    try
                    {
                        cleanup.Dispose();
                    }
                    catch (IOException disposeFailure)
                    {
                        await Task.FromException(disposeFailure).ConfigureAwait(false);
                    }
                });
            }
            else
            {
                manager.As<IDisposable>().Setup(value => value.Dispose()).Callback(() =>
                {
                    if (Volatile.Read(ref failDisposal) != 0)
                    {
                        cleanup.Dispose();
                    }
                });
            }
            IAsyncNodeManager registeredManager = manager.Object;
            if (standardAdapter)
            {
                var synchronous = new SyncNodeManagerAdapter(manager.Object);
                manager.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                registeredManager = new AsyncNodeManagerAdapter(synchronous);
            }
            var factory = new CountingNodeManagerFactory(
                [namespaceUri], (_, _, _) => new ValueTask<IAsyncNodeManager>(registeredManager));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IPreparedChanges? prepared = null;
            LifecycleResult? removed = null;
            NodeManagerGeneration? generation = null;
            Exception? initialFailure = null;
            bool confirmed = false;
            bool VerifyExactResource(NodeManagerCleanupResource resource)
            {
                return cleanup.IsExternallyClosed &&
                    (asynchronous
                        ? resource.TryGetAsyncDisposable(out IAsyncDisposable? asyncResource) &&
                            ReferenceEquals(asyncResource, manager.Object)
                        : resource.TryGetDisposable(out IDisposable? synchronousResource) &&
                            ReferenceEquals(synchronousResource, manager.Object));
            }
            m_fixtureCleanupVerifier.VerifyResource = VerifyExactResource;
            try
            {
                if (privateCandidate)
                {
                    prepared = await lifecycle.PrepareAsync(
                        [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
                    initialFailure = await Assert.ThrowsAsync<AggregateException>(
                        async () => await prepared.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(((AggregateException)initialFailure!).Flatten().InnerExceptions, Does.Contain(failure));
                    generation = lifecycle.SnapshotCleanupGenerations().Single(candidate =>
                        ReferenceEquals(candidate.NodeManager, registeredManager));
                    await Assert.ThatAsync(
                        () => prepared.DisposeAsync().AsTask(),
                        Throws.Exception.SameAs(initialFailure)).ConfigureAwait(false);
                }
                else
                {
                    LifecycleResult publicationResult1 = await CommitChangeAsync(
                        lifecycle, LifecycleChange.Add(factory), timeout.Token).ConfigureAwait(false);
                    NodeManagerRegistration registration = RequireRegistration(publicationResult1.Registrations[0]);
                    generation = registration.Identity;
                    removed = await CommitChangeAsync(
                        Capture(
                        lifecycle, registration), LifecycleChange.Remove(
                        registration.Slot), timeout.Token).ConfigureAwait(false);
                    while ((initialFailure = lifecycle.GetRetirementFailure(generation)) is null)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        Assert.That(removed.Retired.IsCompleted, Is.False);
                        await Task.Yield();
                    }
                    Assert.That(initialFailure, Is.SameAs(failure));
                    Assert.That(removed.Retired.IsCompleted, Is.False);
                }

                ArrayOf<RetainedNodeManagerCleanupFailure> failures =
                    await m_fixtureCleanupRecovery.DiscoverAsync(timeout.Token).ConfigureAwait(false);
                RetainedNodeManagerCleanupFailure retained = failures.ToList().Single(entry =>
                    entry.Handle.GenerationId == generation.CleanupRecoveryId &&
                    entry.Failure.ResourceKind == "NodeManager");
                Assert.That(retained.Handle.FailureId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(retained.Failure.ResourceName, Is.Null);
                Assert.That(cleanup.DisposeCount, Is.EqualTo(1));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);

                m_fixtureCleanupAuthorization.Allowed = false;
                await Assert.ThatAsync(
                    async () => await m_fixtureCleanupRecovery.ConfirmAsync(retained.Handle, timeout.Token)
                        .ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                Assert.That(m_fixtureCleanupVerifier.Count, Is.Zero);
                m_fixtureCleanupAuthorization.Allowed = true;
                await Assert.ThatAsync(
                    async () => await m_fixtureCleanupRecovery.ConfirmAsync(retained.Handle, timeout.Token)
                        .ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                if (asynchronous)
                {
                    Assert.That(m_fixtureCleanupVerifier.LastResource, Is.SameAs(manager.Object));
                }
                else
                {
                    Assert.That(m_fixtureCleanupVerifier.LastSynchronousResource, Is.SameAs(manager.Object));
                }
                Assert.That(cleanup.DisposeCount, Is.EqualTo(1));
                Assert.That((await m_fixtureCleanupRecovery.DiscoverAsync(timeout.Token).ConfigureAwait(false))
                    .ToList().Any(entry => entry.Handle == retained.Handle), Is.True);

                cleanup.FinishExternalCleanup();
                await m_fixtureCleanupRecovery.ConfirmAsync(retained.Handle, timeout.Token).ConfigureAwait(false);
                confirmed = true;
                if (removed is not null)
                {
                    await Assert.ThatAsync(
                        () => removed.Retired.WaitAsync(timeout.Token),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(
                        () => prepared!.DisposeAsync().AsTask(),
                        Throws.Exception.SameAs(initialFailure)).ConfigureAwait(false);
                }
                Assert.That(cleanup.DisposeCount, Is.EqualTo(1));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                Assert.That(m_fixtureCleanupAudit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.NotVerified &&
                    record.Handle == retained.Handle), Is.True);
                Assert.That(m_fixtureCleanupAudit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded &&
                    record.Handle == retained.Handle), Is.True);
            }
            finally
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                m_fixtureCleanupAuthorization.Allowed = true;
                Interlocked.Exchange(ref failDisposal, 0);
                cleanup.FinishExternalCleanup();
                generation ??= lifecycle.SnapshotCleanupGenerations().FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.NodeManager, registeredManager));
                if (!confirmed && generation is not null)
                {
                    if (generation.Gate.State == NodeManagerGenerationState.Retiring && cleanup.DisposeCount != 0)
                    {
                        while (generation.Gate.State == NodeManagerGenerationState.Retiring &&
                            removed?.Retired.IsCompleted != true &&
                            lifecycle.GetRetirementFailure(generation) is null)
                        {
                            cleanupTimeout.Token.ThrowIfCancellationRequested();
                            await Task.Yield();
                        }
                    }
                    ArrayOf<RetainedNodeManagerCleanupFailure> remaining =
                        await m_fixtureCleanupRecovery.DiscoverAsync(cleanupTimeout.Token).ConfigureAwait(false);
                    if (remaining.ToList().Any(entry =>
                        entry.Handle.GenerationId == generation.CleanupRecoveryId &&
                        entry.Failure.ResourceKind == "NodeManager"))
                    {
                        await ConfirmFixtureManagerCleanupAsync(generation, VerifyExactResource, cleanupTimeout.Token)
                            .ConfigureAwait(false);
                    }
                }
                if (prepared is not null)
                {
                    if (initialFailure is null)
                    {
                        await prepared.DisposeAsync().AsTask().WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await Assert.ThatAsync(
                            () => prepared.DisposeAsync().AsTask().WaitAsync(cleanupTimeout.Token),
                            Throws.Exception.SameAs(initialFailure)).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// A published file-system manager exposes its exact failed cursor only to the authorized verifier.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [Category("FileSystem")]
        [NoFixtureServer]
        public Task PublishedDirectoryCursorUsesAuthorizedRecoveryBeforeRetirementAsync()
        {
            return ExerciseDirectoryCursorRecoveryAsync(retireBeforeFailure: false);
        }

        /// <summary>
        /// A cursor failure after retirement starts retains the address space until authorized recovery.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [Category("FileSystem")]
        [NoFixtureServer]
        public Task PublishedDirectoryCursorFailureAfterRetirementUsesAuthorizedRecoveryAsync()
        {
            return ExerciseDirectoryCursorRecoveryAsync(retireBeforeFailure: true);
        }

        /// <summary>
        /// Exercises the real host recovery path before or during a published manager's retirement.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task ExerciseDirectoryCursorRecoveryAsync(bool retireBeforeFailure)
        {
            var authorization = new CleanupRecoveryAuthorization();
            var verifier = new CleanupRecoveryVerifier();
            var audit = new CleanupRecoveryAudit();
            NodeManagerCleanupRecovery? recovery = null;
            LifecycleTestServer? server = null;
            INodeBrowser? browser = null;
            GenerationGate.Lease? admission = null;
            NodeManagerGeneration? generation = null;
            LifecycleResult? removed = null;
            Exception? retirementFailure = null;
            bool recovered = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
            {
                var testServer = new LifecycleTestServer(telemetry);
                recovery = new NodeManagerCleanupRecovery(testServer, authorization, verifier, audit);
                return testServer;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                ReuseCachedCertificates = true,
                AutoAccept = true
            };
            var failure = new IOException("The provider cursor did not close.");
            var cursor = new Mock<IAsyncEnumerator<FileSystemEntry>>(MockBehavior.Strict);
            cursor.Setup(value => value.MoveNextAsync()).Returns(new ValueTask<bool>(true));
            cursor.SetupGet(value => value.Current).Returns(new FileSystemEntry(
                "entry", "entry", true, 0, false,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), string.Empty));
            cursor.Setup(value => value.DisposeAsync()).Returns(() => new ValueTask(Task.FromException(failure)));
            var entries = new Mock<IAsyncEnumerable<FileSystemEntry>>(MockBehavior.Strict);
            entries.Setup(value => value.GetAsyncEnumerator(It.IsAny<CancellationToken>())).Returns(cursor.Object);
            string mountName = "Recovery" + Guid.NewGuid().ToString("N");
            var provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.MountName).Returns(mountName);
            provider.Setup(value => value.EnumerateAsync(string.Empty, It.IsAny<CancellationToken>()))
                .Returns(entries.Object);
            FileSystemNodeManager? manager = null;
            var factory = new CountingNodeManagerFactory(
                [FileSystemNodeManager.NamespaceUriBase + "/" + mountName],
                (serverData, configuration, _) =>
                {
                    var created = new FileSystemNodeManager(serverData, configuration, provider.Object);
                    manager = created;
                    return new ValueTask<IAsyncNodeManager>(created);
                });
            try
            {
                server = await fixture.StartAsync(Path.Combine(m_pkiRoot, "cursor-" + Guid.NewGuid().ToString("N")))
                    .ConfigureAwait(false);
                INodeManagerLifecycle lifecycle = server.NodeManagerLifecycle;
                LifecycleResult added = await lifecycle.ApplyAsync([LifecycleChange.Add(factory)], timeout.Token)
                    .ConfigureAwait(false);
                NodeManagerRegistration registration = RequireRegistration(added.Registrations[0]);
                FileSystemNodeManager published = manager
                    ?? throw new InvalidOperationException("The file-system manager was not created.");
                NodeManagerCleanupRecovery service = recovery
                    ?? throw new InvalidOperationException("The cleanup recovery service was not bound.");
                var sentinel = new BaseObjectState(null)
                {
                    NodeId = new NodeId("RecoverySentinel", published.NamespaceIndex),
                    BrowseName = new QualifiedName("RecoverySentinel", published.NamespaceIndex)
                };
                await published.AddPredefinedNodeAsync(sentinel).ConfigureAwait(false);
                var directory = new DirectoryObjectState(
                    published.SystemContext,
                    FileSystemNodeId.BuildRoot(published.NamespaceIndex),
                    string.Empty,
                    mountName,
                    isRoot: true);
                INodeBrowser activeBrowser = directory.CreateBrowser(
                    published.SystemContext, null, ReferenceTypeIds.HasComponent, true, BrowseDirection.Forward,
                    default, null, false);
                browser = activeBrowser;
                IReference? reference;
                do
                {
                    reference = await activeBrowser.NextAsync(timeout.Token).ConfigureAwait(false);
                }
                while (reference is not null && reference.TargetId.NamespaceIndex != published.NamespaceIndex);
                Assert.That(reference, Is.Not.Null);
                if (retireBeforeFailure)
                {
                    generation = registration.Identity;
                    Assert.That(generation.Gate.TryEnter(out admission), Is.True);
                    Assert.That(admission, Is.Not.Null);
                    removed = await lifecycle.ApplyAsync(
                        [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                    Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                    Assert.That(generation.Gate.ActiveCount, Is.GreaterThan(0));
                    Assert.That(removed.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Pending));
                    Assert.That(registration.Slot.TryGetCurrent(out _), Is.False);
                    Assert.That(published.FindPredefinedNode<BaseObjectState>(sentinel.NodeId), Is.SameAs(sentinel));
                }
                await Assert.ThatAsync(
                    async () => await ((IAsyncDisposable)activeBrowser).DisposeAsync().ConfigureAwait(false),
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                if (retireBeforeFailure)
                {
                    admission!.Dispose();
                    admission = null;
                    var owner = (NodeManagerLifecycle)lifecycle;
                    while ((retirementFailure = owner.GetRetirementFailure(registration.Identity)) is null)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        Assert.That(removed!.Retired.IsCompleted, Is.False);
                        await Task.Yield();
                    }
                    Assert.That(removed!.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Pending));
                    Assert.That(published.FindPredefinedNode<BaseObjectState>(sentinel.NodeId), Is.SameAs(sentinel));
                }

                authorization.Allowed = false;
                await Assert.ThatAsync(
                    async () => await service.DiscoverAsync(timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                authorization.Allowed = true;
                ArrayOf<RetainedNodeManagerCleanupFailure> failures =
                    await service.DiscoverAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(failures.Count, Is.EqualTo(1));
                Assert.That(failures[0].Failure.ResourceKind, Is.EqualTo("DirectoryCursor"));
                Assert.That(failures[0].Handle.GenerationId, Is.EqualTo(registration.Identity.CleanupRecoveryId));
                NodeManagerCleanupFailureHandle handle = failures[0].Handle;

                authorization.Allowed = false;
                await Assert.ThatAsync(
                    async () => await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                Assert.That(verifier.Count, Is.Zero);
                authorization.Allowed = true;
                verifier.Verified = false;
                await Assert.ThatAsync(
                    async () => await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(verifier.LastResource, Is.SameAs(cursor.Object));
                Assert.That((await service.DiscoverAsync(timeout.Token).ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(published.FindPredefinedNode<BaseObjectState>(sentinel.NodeId), Is.SameAs(sentinel));
                cursor.Verify(value => value.DisposeAsync(), Times.Once);

                verifier.Verified = true;
                await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false);
                recovered = true;
                if (retireBeforeFailure)
                {
                    await Assert.ThatAsync(
                        () => removed!.Retired.WaitAsync(timeout.Token),
                        Throws.Exception.SameAs(retirementFailure)).ConfigureAwait(false);
                    Assert.That(
                        removed!.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.CompletedWithFailure));
                }
                Assert.That((await service.DiscoverAsync(timeout.Token).ConfigureAwait(false)).Count, Is.Zero);
                await Assert.ThatAsync(
                    async () => await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Discover &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Denied), Is.True);
                Assert.That(audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Denied &&
                    record.Handle == handle), Is.True);
                Assert.That(audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.NotVerified &&
                    record.Handle == handle), Is.True);
                Assert.That(audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.VerificationSucceeded &&
                    record.Handle == handle), Is.True);
                Assert.That(audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded &&
                    record.Handle == handle), Is.True);
                if (!retireBeforeFailure)
                {
                    removed = await lifecycle.ApplyAsync(
                        [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                    await removed.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(published.FindPredefinedNode<BaseObjectState>(sentinel.NodeId), Is.Null);
                cursor.Verify(value => value.DisposeAsync(), Times.Once);
            }
            finally
            {
                admission?.Dispose();
                authorization.Allowed = true;
                verifier.Verified = true;
                try
                {
                    if (browser is not null)
                    {
                        try
                        {
                            await ((IAsyncDisposable)browser).DisposeAsync().ConfigureAwait(false);
                        }
                        catch (IOException exception) when (ReferenceEquals(exception, failure))
                        {
                            // The retained cursor is resolved through the host below.
                        }
                    }
                    if (recovery is not null &&
                        server is not null &&
                        (!recovered || (removed is not null && !removed.Retired.IsCompleted)))
                    {
                        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        if (removed is not null && generation is not null)
                        {
                            var owner = (NodeManagerLifecycle)server.NodeManagerLifecycle;
                            while (owner.GetRetirementFailure(generation) is null && !removed.Retired.IsCompleted)
                            {
                                cleanupTimeout.Token.ThrowIfCancellationRequested();
                                await Task.Yield();
                            }
                        }
                        while (true)
                        {
                            cleanupTimeout.Token.ThrowIfCancellationRequested();
                            try
                            {
                                ArrayOf<RetainedNodeManagerCleanupFailure> remaining =
                                    await recovery.DiscoverAsync(cleanupTimeout.Token).ConfigureAwait(false);
                                for (int index = 0; index < remaining.Count; index++)
                                {
                                    await recovery.ConfirmAsync(remaining[index].Handle, cleanupTimeout.Token)
                                        .ConfigureAwait(false);
                                }
                                ArrayOf<NodeManagerCleanupFailureHandle> pending =
                                    await recovery.DiscoverPendingConfirmationsAsync(cleanupTimeout.Token)
                                        .ConfigureAwait(false);
                                for (int index = 0; index < pending.Count; index++)
                                {
                                    await recovery.ReconcileAsync(pending[index], cleanupTimeout.Token)
                                        .ConfigureAwait(false);
                                }
                                break;
                            }
                            catch (InvalidOperationException exception) when (
                                exception.Message == "The NodeManager generation is currently being cleaned up.")
                            {
                                await Task.Yield();
                            }
                        }
                    }
                }
                finally
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A failed synchronous retired consumer is retained until the real host verifies its exact instance.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task RetiredConsumerUsesAuthorizedRecoveryWithoutRepeatedDisposalAsync()
        {
            var authorization = new CleanupRecoveryAuthorization();
            var verifier = new CleanupRecoveryVerifier();
            var audit = new CleanupRecoveryAudit();
            NodeManagerCleanupRecovery? recovery = null;
            LifecycleTestServer? server = null;
            NodeManagerGeneration? generation = null;
            GenerationGate.Lease? admission = null;
            LifecycleResult? removed = null;
            bool deferred = false;
            bool recovered = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
            {
                var testServer = new LifecycleTestServer(telemetry);
                recovery = new NodeManagerCleanupRecovery(testServer, authorization, verifier, audit);
                return testServer;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                ReuseCachedCertificates = true,
                AutoAccept = true
            };
            string namespaceUri = kModelNamespaceUri + ":RetiredConsumer:" + Guid.NewGuid().ToString("N");
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(namespaceUri);
            Mock<IAsyncDisposable> managerDisposal = manager.As<IAsyncDisposable>();
            managerDisposal.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var factory = new CountingNodeManagerFactory(
                [namespaceUri], (_, _, _) => new ValueTask<IAsyncNodeManager>(manager.Object));
            var failure = new IOException("The retired consumer did not close.");
            var consumer = new RetiredConsumerProbe(failure);
            verifier.VerifyResource = static resource =>
                resource.TryGetDisposable(out IDisposable? disposable) &&
                disposable is RetiredConsumerProbe probe &&
                probe.IsExternallyClosed;
            try
            {
                server = await fixture.StartAsync(Path.Combine(m_pkiRoot, "consumer-" + Guid.NewGuid().ToString("N")))
                    .ConfigureAwait(false);
                INodeManagerLifecycle lifecycle = server.NodeManagerLifecycle;
                NodeManagerCleanupRecovery service = recovery ??
                    throw new InvalidOperationException("The cleanup recovery service was not bound.");
                LifecycleResult added = await lifecycle.ApplyAsync(
                    [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration registration = RequireRegistration(added.Registrations[0]);
                generation = registration.Identity;
                Assert.That(generation.Gate.TryEnter(out admission), Is.True);
                Assert.That(admission, Is.Not.Null);
                removed = await lifecycle.ApplyAsync(
                    [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                Assert.That(generation.TryDeferDisposal(consumer), Is.True);
                deferred = true;
                admission!.Dispose();
                admission = null;

                var owner = (NodeManagerLifecycle)lifecycle;
                Exception? retirementFailure;
                while ((retirementFailure = owner.GetRetirementFailure(generation)) is null)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    Assert.That(removed.Retired.IsCompleted, Is.False);
                    await Task.Yield();
                }
                Assert.That(retirementFailure, Is.SameAs(failure));
                Assert.That(removed.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Pending));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
                managerDisposal.Verify(value => value.DisposeAsync(), Times.Never);
                Assert.That(consumer.DisposeCount, Is.EqualTo(1));

                ArrayOf<RetainedNodeManagerCleanupFailure> failures =
                    await service.DiscoverAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(failures.Count, Is.EqualTo(1));
                Assert.That(failures[0].Failure.ResourceKind, Is.EqualTo("RetiredConsumer"));
                Assert.That(failures[0].Handle.GenerationId, Is.EqualTo(generation.CleanupRecoveryId));
                NodeManagerCleanupFailureHandle handle = failures[0].Handle;

                authorization.Allowed = false;
                await Assert.ThatAsync(
                    async () => await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                Assert.That(verifier.Count, Is.Zero);
                authorization.Allowed = true;
                await Assert.ThatAsync(
                    async () => await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(verifier.LastSynchronousResource, Is.SameAs(consumer));
                Assert.That(verifier.LastResource, Is.Null);
                Assert.That((await service.DiscoverAsync(timeout.Token).ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(consumer.DisposeCount, Is.EqualTo(1));
                managerDisposal.Verify(value => value.DisposeAsync(), Times.Never);

                consumer.FinishExternalCleanup();
                await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false);
                recovered = true;
                await Assert.ThatAsync(
                    () => removed.Retired.WaitAsync(timeout.Token),
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                Assert.That(removed.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.CompletedWithFailure));
                Assert.That((await service.DiscoverAsync(timeout.Token).ConfigureAwait(false)).Count, Is.Zero);
                await Assert.ThatAsync(
                    async () => await service.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(consumer.DisposeCount, Is.EqualTo(1));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                managerDisposal.Verify(value => value.DisposeAsync(), Times.Once);
                Assert.That(audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.NotVerified &&
                    record.Handle == handle), Is.True);
                Assert.That(audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded &&
                    record.Handle == handle), Is.True);
            }
            finally
            {
                admission?.Dispose();
                authorization.Allowed = true;
                consumer.FinishExternalCleanup();
                try
                {
                    if (deferred &&
                        !recovered &&
                        recovery is not null &&
                        server is not null &&
                        generation is not null &&
                        removed is not null)
                    {
                        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        var owner = (NodeManagerLifecycle)server.NodeManagerLifecycle;
                        while (owner.GetRetirementFailure(generation) is null && !removed.Retired.IsCompleted)
                        {
                            cleanupTimeout.Token.ThrowIfCancellationRequested();
                            await Task.Yield();
                        }
                        while (true)
                        {
                            cleanupTimeout.Token.ThrowIfCancellationRequested();
                            try
                            {
                                ArrayOf<RetainedNodeManagerCleanupFailure> remaining =
                                    await recovery.DiscoverAsync(cleanupTimeout.Token).ConfigureAwait(false);
                                for (int index = 0; index < remaining.Count; index++)
                                {
                                    if (remaining[index].Handle.GenerationId == generation.CleanupRecoveryId)
                                    {
                                        await recovery.ConfirmAsync(remaining[index].Handle, cleanupTimeout.Token)
                                            .ConfigureAwait(false);
                                    }
                                }
                                ArrayOf<NodeManagerCleanupFailureHandle> pending =
                                    await recovery.DiscoverPendingConfirmationsAsync(cleanupTimeout.Token)
                                        .ConfigureAwait(false);
                                for (int index = 0; index < pending.Count; index++)
                                {
                                    if (pending[index].GenerationId == generation.CleanupRecoveryId)
                                    {
                                        await recovery.ReconcileAsync(pending[index], cleanupTimeout.Token)
                                            .ConfigureAwait(false);
                                    }
                                }
                                break;
                            }
                            catch (InvalidOperationException exception) when (
                                exception.Message == "The NodeManager generation is currently being cleaned up.")
                            {
                                await Task.Yield();
                            }
                        }
                    }
                }
                finally
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Host-issued capabilities require authorization, exact-resource verification, and completed audit.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupRecoveryRequiresAuthorizedVerifiedAuditedConfirmationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            harness.Authorization.Allowed = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.DiscoverAsync().ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            harness.Authorization.Allowed = true;
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            Assert.That(failures.Count, Is.EqualTo(2));
            NodeManagerCleanupFailureHandle first = failures[0].Handle;
            NodeManagerCleanupFailureHandle second = failures[1].Handle;
            Assert.That(failures[0].Failure.ResourceKind, Is.EqualTo("Resource"));
            Assert.That(failures[0].Failure.ResourceName, Is.Null);
            Assert.That(first.GenerationId, Is.EqualTo(second.GenerationId));
            Assert.That(first.FailureId, Is.Not.EqualTo(second.FailureId));
            Assert.That(harness.State.LastAccess!.IsAuthorizedFor(harness.Participant), Is.False);
            Assert.That(harness.State.WrongParticipantWasAuthorized, Is.False);

            harness.Authorization.Allowed = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            harness.Authorization.Allowed = true;
            Assert.That(harness.Verifier.Count, Is.Zero);
            harness.Audit.FailNextOutcome = NodeManagerCleanupAuditOutcome.Requested;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Verifier.Count, Is.Zero);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(2));

            harness.Verifier.Verified = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Verifier.LastResource, Is.SameAs(harness.State.Resource(first.FailureId)));
            Assert.That(harness.State.LastVerification!.TryConsume(harness.Participant, out _), Is.False);
            Assert.That(harness.State.WrongParticipantConsumedVerification, Is.False);
            Assert.That(harness.State.ReplayedVerification, Is.False);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(2));

            harness.Verifier.Verified = true;
            harness.Audit.FailNextOutcome = NodeManagerCleanupAuditOutcome.VerificationSucceeded;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(2));
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false);
            Assert.That(harness.State.LastConfirmation!.TryConsume(harness.Participant, out _), Is.False);
            Assert.That(harness.State.WrongParticipantConsumedConfirmation, Is.False);
            Assert.That(harness.State.ReplayedConfirmation, Is.False);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            harness.AssertNoPhysicalCleanup();
            int verifications = harness.Verifier.Count;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Verifier.Count, Is.EqualTo(verifications));

            await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
            Assert.That(harness.Audit.Records.Any(record =>
                record.Operation == NodeManagerCleanupOperation.Confirm &&
                record.Outcome == NodeManagerCleanupAuditOutcome.VerificationSucceeded &&
                record.Handle == second), Is.True);
        }

        /// <summary>
        /// A failed participant snapshot does not hide another participant's exact retained resource.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupDiscoveryReportsParticipantFailureAndRecoversIndependentResourceAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            var disposalFailure = new InvalidOperationException("The retained resource did not close.");
            var discoveryFailure = new InvalidOperationException("A private participant failure.");
            var resource = new RetiredConsumerProbe(disposalFailure);
            Assert.That(() => resource.Dispose(), Throws.Exception.SameAs(disposalFailure));
            bool released = false;
            harness.Generation.RetainHistoryCleanupFailure(resource, disposalFailure, () =>
            {
                released = true;
                return null;
            });
            harness.State.DiscoveryFailure = discoveryFailure;
            try
            {
                int snapshots = harness.State.FailureSnapshots;
                harness.Authorization.Allowed = false;
                await Assert.ThatAsync(
                    async () => await harness.Recovery.DiscoverDetailedAsync().ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                Assert.That(harness.State.FailureSnapshots, Is.EqualTo(snapshots));
                harness.Authorization.Allowed = true;

                NodeManagerCleanupDiscoveryResult result =
                    await harness.Recovery.DiscoverDetailedAsync().ConfigureAwait(false);
                Assert.That(result.Failures.Count, Is.EqualTo(1));
                RetainedNodeManagerCleanupFailure retained = result.Failures[0];
                Assert.That(retained.Failure.ResourceKind, Is.EqualTo("HistoryContinuation"));
                Assert.That(result.Issues.Count, Is.EqualTo(1));
                Assert.That(result.Issues[0].GenerationId, Is.EqualTo(harness.Generation.CleanupRecoveryId));
                Assert.That(
                    result.Issues[0].ParticipantId,
                    Is.EqualTo(harness.Generation.GetCleanupParticipantId(harness.Participant)));
                Assert.That(result.Issues[0].Kind, Is.EqualTo(
                    NodeManagerCleanupDiscoveryIssueKind.FailureSnapshotUnavailable));
                Assert.That(harness.Audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Discover &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Partial), Is.True);
                await Assert.ThatAsync(
                    async () => await harness.Recovery.DiscoverAsync().ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

                harness.Audit.FailNextOperation = NodeManagerCleanupOperation.Discover;
                harness.Audit.FailNextOutcome = NodeManagerCleanupAuditOutcome.Partial;
                await Assert.ThatAsync(
                    async () => await harness.Recovery.DiscoverDetailedAsync().ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                harness.AssertNoPhysicalCleanup();

                resource.FinishExternalCleanup();
                harness.Verifier.VerifyResource = candidate =>
                    candidate.TryGetDisposable(out IDisposable? exact) &&
                    ReferenceEquals(exact, resource) &&
                    resource.IsExternallyClosed;
                NodeManagerCleanupPostConfirmationException? postFailure =
                    await Assert.ThrowsAsync<NodeManagerCleanupPostConfirmationException>(
                        async () => await harness.Recovery.ConfirmAsync(retained.Handle).ConfigureAwait(false))
                        .ConfigureAwait(false);
                Assert.That(postFailure!.Handle, Is.EqualTo(retained.Handle));
                Assert.That(postFailure.InnerException, Is.SameAs(discoveryFailure));
                Assert.That(harness.Verifier.LastSynchronousResource, Is.SameAs(resource));
                Assert.That(released, Is.True);
                Assert.That(resource.DisposeCount, Is.EqualTo(1));
                Assert.That((await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false))
                    .Span.IndexOf(retained.Handle), Is.GreaterThanOrEqualTo(0));
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                harness.Authorization.Allowed = true;
                harness.State.DiscoveryFailure = null;
                harness.Verifier.VerifyResource = null;
                resource.FinishExternalCleanup();
            }
        }

        /// <summary>
        /// An unavailable resource getter does not hide a generation-owned failure or authorize its destruction.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupDiscoveryRetainsKnownParticipantsWhenResourceGetterFailsAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(
                1, useSelfAdapter: true).ConfigureAwait(false);
            var disposalFailure = new InvalidOperationException("The retained resource did not close.");
            var getterFailure = new InvalidOperationException("A private resource getter failure.");
            var resource = new RetiredConsumerProbe(disposalFailure);
            Assert.That(() => resource.Dispose(), Throws.Exception.SameAs(disposalFailure));
            bool released = false;
            harness.Generation.RetainHistoryCleanupFailure(resource, disposalFailure, () =>
            {
                released = true;
                return null;
            });
            INodeManager original = harness.Manager.Object.SyncNodeManager;
            harness.Manager.SetupGet(manager => manager.SyncNodeManager).Throws(getterFailure);
            try
            {
                NodeManagerCleanupDiscoveryResult result =
                    await harness.Recovery.DiscoverDetailedAsync().ConfigureAwait(false);
                Assert.That(result.Failures.Count, Is.EqualTo(2));
                RetainedNodeManagerCleanupFailure retained = result.Failures.ToList().Single(
                    failure => failure.Failure.ResourceKind == "HistoryContinuation");
                Assert.That(result.Issues.Count, Is.EqualTo(1));
                Assert.That(result.Issues[0].GenerationId, Is.EqualTo(harness.Generation.CleanupRecoveryId));
                Assert.That(result.Issues[0].ParticipantId, Is.EqualTo(Guid.Empty));
                Assert.That(result.Issues[0].Kind, Is.EqualTo(
                    NodeManagerCleanupDiscoveryIssueKind.ParticipantUnavailable));

                resource.FinishExternalCleanup();
                harness.Verifier.VerifyResource = candidate =>
                    candidate.TryGetDisposable(out IDisposable? exact) &&
                    ReferenceEquals(exact, resource) &&
                    resource.IsExternallyClosed;
                NodeManagerCleanupPostConfirmationException? postFailure =
                    await Assert.ThrowsAsync<NodeManagerCleanupPostConfirmationException>(
                        async () => await harness.Recovery.ConfirmAsync(retained.Handle).ConfigureAwait(false))
                        .ConfigureAwait(false);
                Assert.That(postFailure!.InnerException, Is.SameAs(getterFailure));
                Assert.That(harness.Verifier.LastSynchronousResource, Is.SameAs(resource));
                Assert.That(released, Is.True);
                Assert.That(resource.DisposeCount, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                harness.Manager.SetupGet(manager => manager.SyncNodeManager).Returns(original);
                harness.Verifier.VerifyResource = null;
                resource.FinishExternalCleanup();
            }
        }

        /// <summary>
        /// Closed recovery admission is reported as an incomplete snapshot without consuming failures.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupDiscoveryReportsClosedGenerationAdmissionAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            harness.Generation.CleanupRecoveryGate.Retire();
            try
            {
                NodeManagerCleanupDiscoveryResult result =
                    await harness.Recovery.DiscoverDetailedAsync().ConfigureAwait(false);
                Assert.That(result.Failures.IsEmpty, Is.True);
                Assert.That(result.Issues.Count, Is.EqualTo(1));
                Assert.That(result.Issues[0].GenerationId, Is.EqualTo(harness.Generation.CleanupRecoveryId));
                Assert.That(result.Issues[0].ParticipantId, Is.EqualTo(Guid.Empty));
                Assert.That(result.Issues[0].Kind, Is.EqualTo(
                    NodeManagerCleanupDiscoveryIssueKind.GenerationUnavailable));
                await Assert.ThatAsync(
                    async () => await harness.Recovery.DiscoverAsync().ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.EqualTo(
                        "The NodeManager generation is currently being cleaned up.")).ConfigureAwait(false);
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                harness.Generation.CleanupRecoveryGate.Restore();
            }
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A failed history point stays recoverable after its session closes and alongside another failure.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ClosedSessionHistoryFailureRequiresExactVerifiedRecoveryAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new HistoryCleanupRecoveryPoint();
            try
            {
                points.SaveHistory(point);
                points.BindHistoryGeneration(new ByteString(point.Id.ToByteArray()), harness.Generation);
                AggregateException? cleanup = Assert.Throws<AggregateException>(
                    () => points.RemoveHistoryForGeneration(harness.Generation));
                Assert.That(cleanup!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(point.Failure));
                points.Clear();

                ArrayOf<RetainedNodeManagerCleanupFailure> failures =
                    await harness.DiscoverAsync().ConfigureAwait(false);
                Assert.That(failures.Count, Is.EqualTo(2));
                RetainedNodeManagerCleanupFailure history = failures.ToList().Single(
                    failure => failure.Failure.ResourceKind == "HistoryContinuation");
                Assert.That(history.Handle.GenerationId, Is.EqualTo(harness.Generation.CleanupRecoveryId));
                Assert.That(history.Failure.ResourceName, Is.Null);
                Assert.That(point.DisposeCount, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();

                harness.Verifier.VerifyResource = resource =>
                    resource.TryGetDisposable(out IDisposable? resolved) &&
                    ReferenceEquals(resolved, point) &&
                    point.IsExternallyClosed;
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(history.Handle).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(harness.Verifier.LastSynchronousResource, Is.SameAs(point));
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(2));
                Assert.That(points.HasHistoryForGeneration(harness.Generation), Is.True);
                Assert.That(point.DisposeCount, Is.EqualTo(1));

                point.FinishExternalCleanup();
                await harness.Recovery.ConfirmAsync(history.Handle).ConfigureAwait(false);
                Assert.That(points.HasHistoryForGeneration(harness.Generation), Is.False);
                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();
                Assert.That(harness.Audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.VerificationSucceeded &&
                    record.Handle == history.Handle), Is.True);
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(history.Handle).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            finally
            {
                point.FinishExternalCleanup();
                harness.Verifier.VerifyResource = null;
                points.Clear();
            }
        }

        /// <summary>
        /// A failing release notification cannot undo an already verified history cleanup.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task HistoryRecoveryReportsReleaseNotificationFailureAfterConfirmationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var disposalFailure = new InvalidOperationException("The history data did not close.");
            var notificationFailure = new InvalidOperationException("The release notification failed.");
            var data = new RetiredConsumerProbe(disposalFailure);
            var point = new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                NodeId = new NodeId("historized", 1),
                Provider = Mock.Of<IHistorianProvider>(),
                Kind = HistorianReadKind.Raw,
                ResumeToken = default
            };
            point.Ownership.Generation = harness.Generation.Reference;
            point.Ownership.Data = data;
            int notifications = 0;
            void NotifyReleased()
            {
                notifications++;
                throw notificationFailure;
            }
            points.HistoryContinuationPointsReleased += NotifyReleased;
            try
            {
                points.SaveHistory(point);
                AggregateException? cleanup = Assert.Throws<AggregateException>(
                    () => points.RemoveHistoryForGeneration(harness.Generation));
                Assert.That(cleanup!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(disposalFailure));
                Assert.That(notifications, Is.Zero);
                RetainedNodeManagerCleanupFailure history = (await harness.DiscoverAsync().ConfigureAwait(false))
                    .ToList().Single(failure => failure.Failure.ResourceKind == "HistoryContinuation");
                data.FinishExternalCleanup();
                harness.Verifier.VerifyResource = resource =>
                    resource.TryGetDisposable(out IDisposable? resolved) &&
                    ReferenceEquals(resolved, point) &&
                    data.IsExternallyClosed;

                NodeManagerCleanupPostConfirmationException? reported =
                    await Assert.ThrowsAsync<NodeManagerCleanupPostConfirmationException>(
                        async () => await harness.Recovery.ConfirmAsync(history.Handle).ConfigureAwait(false))
                        .ConfigureAwait(false);

                Assert.That(reported!.Handle, Is.EqualTo(history.Handle));
                Assert.That(reported.InnerException, Is.SameAs(notificationFailure));
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(data.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(harness.Generation), Is.False);
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(harness.Audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.ConfirmationAppliedWithPostFailure &&
                    record.Handle == history.Handle), Is.True);
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                data.FinishExternalCleanup();
                harness.Verifier.VerifyResource = null;
                points.HistoryContinuationPointsReleased -= NotifyReleased;
                points.Clear();
            }
        }

        /// <summary>
        /// Confirming one history point does not clear another failure held by the same generation.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task HistoryRecoveryConfirmsOnlyTheVerifiedPointAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var first = new HistoryCleanupRecoveryPoint();
            var second = new HistoryCleanupRecoveryPoint();
            try
            {
                points.SaveHistory(first);
                points.BindHistoryGeneration(new ByteString(first.Id.ToByteArray()), harness.Generation);
                points.SaveHistory(second);
                points.BindHistoryGeneration(new ByteString(second.Id.ToByteArray()), harness.Generation);
                AggregateException? cleanup = Assert.Throws<AggregateException>(
                    () => points.RemoveHistoryForGeneration(harness.Generation));
                Assert.That(cleanup!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(first.Failure));
                Assert.That(cleanup.Flatten().InnerExceptions, Has.Exactly(1).SameAs(second.Failure));
                points.Clear();

                RetainedNodeManagerCleanupFailure[] history = (await harness.DiscoverAsync().ConfigureAwait(false))
                    .ToList().Where(failure => failure.Failure.ResourceKind == "HistoryContinuation").ToArray();
                Assert.That(history, Has.Length.EqualTo(2));
                Assert.That(history[0].Handle.FailureId, Is.Not.EqualTo(history[1].Handle.FailureId));
                Assert.That(history[0].Handle.ParticipantId, Is.EqualTo(history[1].Handle.ParticipantId));
                harness.Verifier.VerifyResource = resource =>
                    resource.TryGetDisposable(out IDisposable? resolved) &&
                    resolved is HistoryCleanupRecoveryPoint candidate &&
                    candidate.IsExternallyClosed;
                NodeManagerCleanupFailureHandle firstHandle = default;
                NodeManagerCleanupFailureHandle secondHandle = default;
                foreach (RetainedNodeManagerCleanupFailure failure in history)
                {
                    await Assert.ThatAsync(
                        async () => await harness.Recovery.ConfirmAsync(failure.Handle).ConfigureAwait(false),
                        Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                    if (ReferenceEquals(harness.Verifier.LastSynchronousResource, first))
                    {
                        firstHandle = failure.Handle;
                    }
                    else
                    {
                        Assert.That(harness.Verifier.LastSynchronousResource, Is.SameAs(second));
                        secondHandle = failure.Handle;
                    }
                }
                Assert.That(firstHandle.FailureId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(secondHandle.FailureId, Is.Not.EqualTo(Guid.Empty));

                first.FinishExternalCleanup();
                await harness.Recovery.ConfirmAsync(firstHandle).ConfigureAwait(false);
                Assert.That(points.HasHistoryForGeneration(harness.Generation), Is.True);
                ArrayOf<RetainedNodeManagerCleanupFailure> remaining =
                    await harness.DiscoverAsync().ConfigureAwait(false);
                Assert.That(remaining.Count, Is.EqualTo(2));
                Assert.That(remaining.ToList().Any(failure => failure.Handle == secondHandle), Is.True);
                Assert.That(first.DisposeCount, Is.EqualTo(1));
                Assert.That(second.DisposeCount, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();

                second.FinishExternalCleanup();
                await harness.Recovery.ConfirmAsync(secondHandle).ConfigureAwait(false);
                Assert.That(points.HasHistoryForGeneration(harness.Generation), Is.False);
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(first.DisposeCount, Is.EqualTo(1));
                Assert.That(second.DisposeCount, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                first.FinishExternalCleanup();
                second.FinishExternalCleanup();
                harness.Verifier.VerifyResource = null;
                points.Clear();
            }
        }

        /// <summary>
        /// Discovery and verification expose sanitized metadata without changing the exact retained resource.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupRecoverySanitizesDescriptionsForDiscoveryAndVerificationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            harness.State.ResourceKind = "C:\\private\\resource";
            harness.State.ResourceName = "C:\\private\\name";
            harness.State.FailureStatus = new StatusCode(
                StatusCodes.BadUnexpectedError.Code | 1u, "untrusted status text");

            ArrayOf<RetainedNodeManagerCleanupFailure> failures =
                await harness.DiscoverAsync().ConfigureAwait(false);
            Assert.That(failures.Count, Is.EqualTo(1));
            NodeManagerCleanupFailureHandle handle = failures[0].Handle;
            NodeManagerCleanupFailure description = failures[0].Failure;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(description.Id, Is.EqualTo(handle.FailureId));
                Assert.That(description.ResourceKind, Is.EqualTo("Resource"));
                Assert.That(description.ResourceName, Is.Null);
                Assert.That(description.StatusCode.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
                Assert.That(description.StatusCode.SymbolicId, Is.Empty);
            }

            harness.Verifier.Verified = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            RetainedNodeManagerCleanupFailure? verified = harness.Verifier.LastFailure;
            Assert.That(verified, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(verified!.Handle, Is.EqualTo(handle));
                Assert.That(verified.Failure.ResourceKind, Is.EqualTo("Resource"));
                Assert.That(verified.Failure.ResourceName, Is.Null);
                Assert.That(verified.Failure.StatusCode.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
                Assert.That(verified.Failure.StatusCode.SymbolicId, Is.Empty);
                Assert.That(harness.Verifier.LastResource, Is.SameAs(harness.State.Resource(handle.FailureId)));
                Assert.That(harness.State.Resource(handle.FailureId).DisposeCount, Is.EqualTo(1));
            }
            harness.AssertNoPhysicalCleanup();

            harness.Verifier.Verified = true;
            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// Returning without consuming a host-issued capability cannot resolve or clear a retained failure.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupRecoveryRejectsUnconsumedParticipantCapabilitiesAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;

            harness.State.SkipNextVerificationConsumption = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The participant did not consume verification.")).ConfigureAwait(false);
            Assert.That(harness.State.LastVerification!.TryConsume(harness.Participant, out _), Is.False);
            Assert.That(harness.Verifier.Count, Is.Zero);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            harness.AssertNoPhysicalCleanup();

            harness.State.SkipNextConfirmationConsumption = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The participant did not consume confirmation.")).ConfigureAwait(false);
            Assert.That(harness.State.LastConfirmation!.TryConsume(harness.Participant, out _), Is.False);
            Assert.That(harness.Verifier.Count, Is.EqualTo(1));
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            Assert.That(harness.State.Resource(handle.FailureId).DisposeCount, Is.EqualTo(1));
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// An empty resource cannot authorize verification or clear the retained failure.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupRecoveryRejectsEmptyVerificationResourceAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.State.ReturnEmptyResourceAfterConsumption = true;

            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The participant returned no retained resource.")).ConfigureAwait(false);
            Assert.That(harness.State.LastVerification!.IsConsumed, Is.True);
            Assert.That(harness.State.LastVerification.TryConsume(harness.Participant, out _), Is.False);
            Assert.That(harness.Verifier.Count, Is.Zero);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            Assert.That(harness.State.Resource(handle.FailureId).DisposeCount, Is.EqualTo(1));
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// A callback failure before consumption leaves the retained failure and revokes the capability.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ConfirmationFailureBeforeConsumptionLeavesResourceRetainedAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.State.NextConfirmationBeforeConsumptionFailure =
                new InvalidOperationException("The participant rejected confirmation.");

            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(harness.State.LastConfirmation!.IsConsumed, Is.False);
            Assert.That(harness.State.LastConfirmation.TryConsume(harness.Participant, out _), Is.False);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.False);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            Assert.That(harness.State.Resource(handle.FailureId).DisposeCount, Is.EqualTo(1));
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// A callback failure after consumption is reconciled against the participant's actual retained state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ConfirmationFailureAfterConsumptionUsesRetainedStateAsync(bool keepFailure)
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.State.KeepNextConfirmedFailure = keepFailure;
            harness.State.NextConfirmationAfterConsumptionFailure =
                new InvalidOperationException("The participant failed after consuming confirmation.");

            if (keepFailure)
            {
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                    Throws.TypeOf<NodeManagerCleanupPostConfirmationException>()
                        .With.Property(nameof(NodeManagerCleanupPostConfirmationException.Handle))
                        .EqualTo(handle)).ConfigureAwait(false);
            }

            Assert.That(harness.State.LastConfirmation!.IsConsumed, Is.True);
            Assert.That(harness.State.LastConfirmation.TryConsume(harness.Participant, out _), Is.False);
            Assert.That(harness.State.Resource(handle.FailureId).DisposeCount, Is.EqualTo(1));
            if (keepFailure)
            {
                Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.False);
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();
                await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            }
            else
            {
                Assert.That(harness.Audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.ConfirmationAppliedWithPostFailure &&
                    record.Handle == handle), Is.True);
            }
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// A separate retained failure prevents reconciliation from resuming the private generation.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ReconciliationWaitsForOtherRetainedFailureAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = failures[0].Handle;
            NodeManagerCleanupFailureHandle second = failures[1].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await harness.Recovery.ReconcileAsync(first).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            harness.AssertNoPhysicalCleanup();
            await harness.Recovery.ReconcileAsync(first).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
        }

        /// <summary>
        /// Reconciliation drains admitted verifiers before inspecting retained failures.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ReconciliationWaitsForAdmittedVerifierAsync(bool cancelReconciliation)
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = failures[0].Handle;
            NodeManagerCleanupFailureHandle second = failures[1].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);

            harness.Verifier.Verified = false;
            harness.Verifier.PauseNext();
            Task verification = harness.Recovery.ConfirmAsync(second, timeout.Token).AsTask();
            Task? reconciliation = null;
            try
            {
                await harness.Verifier.VerificationEntered.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(harness.Verifier.LastResource, Is.SameAs(harness.State.Resource(second.FailureId)));
                int snapshots = harness.State.FailureSnapshots;
                Task pending = harness.Recovery.ReconcileAsync(first, cancellation.Token).AsTask();
                reconciliation = pending;
                bool admitted = harness.Generation.TryBeginCleanupVerification();
                if (admitted)
                {
                    harness.Generation.EndCleanupVerification();
                }
                Assert.That(admitted, Is.False);
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(harness.State.FailureSnapshots, Is.EqualTo(snapshots));
                Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
                Assert.That(harness.Audit.Records.Any(record =>
                    record.Operation == NodeManagerCleanupOperation.Reconcile &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded &&
                    record.Handle == first), Is.False);
                harness.AssertNoPhysicalCleanup();

                if (cancelReconciliation)
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    await Assert.ThatAsync(
                        async () => await pending.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(verification.IsCompleted, Is.False);
                    Assert.That(harness.State.FailureSnapshots, Is.EqualTo(snapshots));
                    Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
                    harness.AssertNoPhysicalCleanup();
                }

                harness.Verifier.Release();
                await Assert.ThatAsync(
                    async () => await verification.WaitAsync(timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contains("was not verified")).ConfigureAwait(false);
                if (!cancelReconciliation)
                {
                    await Assert.ThatAsync(
                        async () => await pending.WaitAsync(timeout.Token).ConfigureAwait(false),
                        Throws.TypeOf<InvalidOperationException>()
                            .With.Message.Contains("Other cleanup failures")).ConfigureAwait(false);
                    Assert.That(harness.State.FailureSnapshots, Is.GreaterThan(snapshots));
                }
                Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
                Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                harness.Verifier.Release();
                try
                {
                    await verification.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (InvalidOperationException failure) when (
                    failure.Message.Contains("was not verified", StringComparison.Ordinal))
                {
                }
                if (reconciliation is not null)
                {
                    try
                    {
                        await reconciliation.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    catch (InvalidOperationException failure) when (
                        failure.Message.Contains("Other cleanup failures", StringComparison.Ordinal))
                    {
                    }
                }
                harness.Verifier.Verified = true;
            }

            await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false);
            await harness.Recovery.ReconcileAsync(first).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
        }

        /// <summary>
        /// Verification cannot request recovery that would wait for that same operation to exit.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public Task VerificationRejectsReentrantRecoveryForItsGenerationAsync(
            bool confirmAgain,
            bool duringLookup)
        {
            return VerifyRecoveryReentryAsync(confirmAgain, duringLookup, duringDiscovery: false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public Task VerificationDiscoveryRejectsReentrantRecoveryForItsGenerationAsync(bool confirmAgain)
        {
            return VerifyRecoveryReentryAsync(confirmAgain, duringLookup: false, duringDiscovery: true);
        }

        private async Task VerifyRecoveryReentryAsync(bool confirmAgain, bool duringLookup, bool duringDiscovery)
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = failures[0].Handle;
            NodeManagerCleanupFailureHandle second = failures[1].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);

            InvalidOperationException? rejected = null;
            Task? lookupAttempt = null;
            Task StartNestedRecovery(
                NodeManagerCleanupVerificationContext context,
                CancellationToken cancellationToken)
            {
                return confirmAgain
                    ? harness.Recovery.ConfirmAsync(second, context, cancellationToken).AsTask()
                    : harness.Recovery.ReconcileAsync(first, context, cancellationToken).AsTask();
            }
            if (duringLookup)
            {
                harness.State.OnResourceLookup = verification =>
                    lookupAttempt = StartNestedRecovery(RequireLifecycleValue(verification.Context), timeout.Token);
            }
            if (duringDiscovery)
            {
                harness.State.OnFailureDiscovery = access =>
                    lookupAttempt = StartNestedRecovery(RequireLifecycleValue(access.Context), timeout.Token);
            }
            harness.Verifier.VerifyResourceAsync = async (_, resource, context, cancellationToken) =>
            {
                Assert.That(
                    resource.TryGetAsyncDisposable(out IAsyncDisposable? resolved) &&
                    ReferenceEquals(resolved, harness.State.Resource(second.FailureId)),
                    Is.True);
                Task nested = duringLookup || duringDiscovery
                    ? lookupAttempt ?? throw new InvalidOperationException("The resource lookup did not run.")
                    : StartNestedRecovery(context, cancellationToken);
                rejected = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await nested.ConfigureAwait(false)).ConfigureAwait(false);
                return false;
            };
            try
            {
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(second, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contains("was not verified")).ConfigureAwait(false);
                Assert.That(rejected!.Message, Does.Contain("during verification for the same generation"));
                Assert.That(harness.Verifier.Count, Is.EqualTo(2));
                Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
                Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                harness.State.OnResourceLookup = null;
                harness.State.OnFailureDiscovery = null;
                harness.Verifier.VerifyResourceAsync = null;
            }

            await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false);
            await harness.Recovery.ReconcileAsync(first).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// A retained verification context no longer blocks recovery after the callback exits.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ExitedVerifierDoesNotBlockInheritedRecoveryAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = failures[0].Handle;
            NodeManagerCleanupFailureHandle second = failures[1].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);

            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? inherited = null;
            harness.Verifier.VerifyResourceAsync = (_, _, context, cancellationToken) =>
            {
                inherited = Task.Run(async () =>
                {
                    await resume.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    await harness.Recovery.ReconcileAsync(first, context, cancellationToken).ConfigureAwait(false);
                }, cancellationToken);
                return new ValueTask<bool>(false);
            };
            try
            {
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(second, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contains("was not verified")).ConfigureAwait(false);
                Assert.That(inherited, Is.Not.Null);
                resume.TrySetResult(true);
                await Assert.ThatAsync(
                    async () => await inherited!.WaitAsync(timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contains("Other cleanup failures")).ConfigureAwait(false);
                Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                resume.TrySetResult(true);
                try
                {
                    if (inherited is not null)
                    {
                        try
                        {
                            await inherited.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        }
                        catch (InvalidOperationException failure) when (
                            failure.Message.Contains("Other cleanup failures", StringComparison.Ordinal))
                        {
                        }
                    }
                }
                finally
                {
                    harness.Verifier.VerifyResourceAsync = null;
                }
            }

            await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false);
            await harness.Recovery.ReconcileAsync(first).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// An explicit context still detects an active outer verifier after an inner verifier exits.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task NestedVerificationPreservesActiveAncestorAfterInnerExitAsync()
        {
            await using CleanupRecoveryHarness outer = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            await using CleanupRecoveryHarness inner = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ArrayOf<RetainedNodeManagerCleanupFailure> outerFailures = await outer.DiscoverAsync()
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = outerFailures[0].Handle;
            NodeManagerCleanupFailureHandle second = outerFailures[1].Handle;
            NodeManagerCleanupFailureHandle innerHandle = (await inner.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            outer.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await outer.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);

            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? inherited = null;
            bool rejected = false;
            inner.Verifier.VerifyResourceAsync = (_, _, context, cancellationToken) =>
            {
                inherited = Task.Run(async () =>
                {
                    await resume.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    await outer.Recovery.ReconcileAsync(first, context, cancellationToken).ConfigureAwait(false);
                }, cancellationToken);
                return new ValueTask<bool>(false);
            };
            outer.Verifier.VerifyResourceAsync = async (_, _, context, cancellationToken) =>
            {
                await Assert.ThatAsync(
                    async () => await inner.Recovery.ConfirmAsync(innerHandle, context, cancellationToken)
                        .ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contains("was not verified")).ConfigureAwait(false);
                Assert.That(inherited, Is.Not.Null);
                resume.TrySetResult(true);
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await inherited!.WaitAsync(cancellationToken).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(failure!.Message, Does.Contain("during verification for the same generation"));
                rejected = true;
                return false;
            };
            try
            {
                await Assert.ThatAsync(
                    async () => await outer.Recovery.ConfirmAsync(second, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contains("was not verified")).ConfigureAwait(false);
                Assert.That(rejected, Is.True);
                Assert.That(outer.Generation.HasPendingCleanupConfirmation, Is.True);
                outer.AssertNoPhysicalCleanup();
                inner.AssertNoPhysicalCleanup();
            }
            finally
            {
                resume.TrySetResult(true);
                try
                {
                    if (inherited is not null)
                    {
                        try
                        {
                            await inherited.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        }
                        catch (InvalidOperationException failure) when (
                            failure.Message.Contains(
                                "during verification for the same generation", StringComparison.Ordinal))
                        {
                        }
                        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                        {
                        }
                    }
                }
                finally
                {
                    outer.Verifier.VerifyResourceAsync = null;
                    inner.Verifier.VerifyResourceAsync = null;
                }
            }

            await outer.Recovery.ConfirmAsync(second).ConfigureAwait(false);
            await outer.Recovery.ReconcileAsync(first).ConfigureAwait(false);
            await inner.Recovery.ConfirmAsync(innerHandle).ConfigureAwait(false);
            outer.AssertPhysicalCleanupOnce();
            inner.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// A later failure can be confirmed while an earlier offer waits for explicit conformance ownership.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task NewFailureCanBeConfirmedWhileEarlierOfferWaitsAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(
                1, deferConformance: true).ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false);
            ArrayOf<NodeManagerCleanupFailureHandle> pending =
                await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
            Assert.That(pending.Count, Is.EqualTo(1));
            Assert.That(pending[0], Is.EqualTo(first));

            Guid added = await harness.State.AddFailedResourceAsync().ConfigureAwait(false);
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            Assert.That(failures.Count, Is.EqualTo(1));
            NodeManagerCleanupFailureHandle second = failures[0].Handle;
            Assert.That(second.FailureId, Is.EqualTo(added));
            Assert.That(second.GenerationId, Is.EqualTo(first.GenerationId));
            Assert.That(second.ParticipantId, Is.EqualTo(first.ParticipantId));
            await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false);

            pending = await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
            Assert.That(pending.Count, Is.EqualTo(1));
            Assert.That(pending[0], Is.EqualTo(second));
            Assert.That(harness.Verifier.LastResource, Is.SameAs(harness.State.Resource(added)));
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
            harness.AssertNoPhysicalCleanup();
            await harness.ReleaseConformanceAsync().ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That((await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false)).IsEmpty,
                Is.True);
        }

        /// <summary>
        /// An uncertain later confirmation preserves both handles until separately authorized reconciliation.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task UncertainLaterConfirmationKeepsEarlierOfferUntilReconciledAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(
                1, deferConformance: true).ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false);
            Guid added = await harness.State.AddFailedResourceAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle second = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            Assert.That(second.FailureId, Is.EqualTo(added));

            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);
            ArrayOf<NodeManagerCleanupFailureHandle> pending =
                await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
            Assert.That(pending.Count, Is.EqualTo(2));
            Assert.That(pending.Span.IndexOf(first), Is.GreaterThanOrEqualTo(0));
            Assert.That(pending.Span.IndexOf(second), Is.GreaterThanOrEqualTo(0));
            Assert.That(harness.Generation.CanAttemptCleanup, Is.False);
            harness.AssertNoPhysicalCleanup();

            harness.Authorization.Allowed = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ReconcileAsync(second).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            harness.Authorization.Allowed = true;
            await harness.Recovery.ReconcileAsync(second).ConfigureAwait(false);
            pending = await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
            Assert.That(pending.Count, Is.EqualTo(1));
            Assert.That(pending[0], Is.EqualTo(second));
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
            harness.AssertNoPhysicalCleanup();
            await harness.ReleaseConformanceAsync().ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// A rejected reconciliation leaves an offered handle available while the later failure is addressed.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task RejectedReconciliationKeepsEarlierOfferUntilLaterFailureIsConfirmedAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(
                1, deferConformance: true).ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false);
            Guid added = await harness.State.AddFailedResourceAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle second = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            Assert.That(second.FailureId, Is.EqualTo(added));

            await Assert.ThatAsync(
                async () => await harness.Recovery.ReconcileAsync(first).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            ArrayOf<NodeManagerCleanupFailureHandle> pending =
                await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
            Assert.That(pending.Count, Is.EqualTo(1));
            Assert.That(pending[0], Is.EqualTo(first));
            Assert.That(harness.Generation.CanAttemptCleanup, Is.False);
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false);
            pending = await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
            Assert.That(pending.Count, Is.EqualTo(1));
            Assert.That(pending[0], Is.EqualTo(second));
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
            await harness.ReleaseConformanceAsync().ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// An uncertain cleared confirmation requires authorized reconciliation without retrying disposal.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task UnknownClearedConfirmationRequiresReconciliationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()
                    .With.Property(nameof(NodeManagerCleanupConfirmationOutcomeUnknownException.Handle))
                    .EqualTo(handle)).ConfigureAwait(false);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).IsEmpty, Is.True);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            harness.AssertNoPhysicalCleanup();

            harness.Authorization.Allowed = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            harness.AssertNoPhysicalCleanup();
            harness.Authorization.Allowed = true;
            await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
            await Assert.ThatAsync(
                async () => await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancellation after a successful audit record does not interrupt reconciliation.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ReconciliationCompletesWhenSuccessAuditCancelsCallerAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            harness.Audit.AfterRecord = record =>
                record.Operation == NodeManagerCleanupOperation.Reconcile &&
                record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded
                    ? new ValueTask(cancellation.CancelAsync())
                    : default;

            try
            {
                await harness.Recovery.ReconcileAsync(handle, cancellation.Token).ConfigureAwait(false);

                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(harness.Audit.Records
                    .Where(record => record.Operation == NodeManagerCleanupOperation.Reconcile)
                    .Select(record => record.Outcome),
                    Is.EqualTo(new[]
                    {
                        NodeManagerCleanupAuditOutcome.Requested,
                        NodeManagerCleanupAuditOutcome.Succeeded
                    }));
                harness.AssertPhysicalCleanupOnce();
            }
            finally
            {
                harness.Audit.AfterRecord = null;
                if (harness.Generation.HasPendingCleanupConfirmation)
                {
                    await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Authorized discovery can recover a cleared failure's pending reconciliation handle.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task PendingConfirmationDiscoveryRequiresAuthorizationAndAuditAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            Assert.That((await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false)).IsEmpty,
                Is.True);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).IsEmpty, Is.True);

            harness.Authorization.Allowed = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            harness.Authorization.Allowed = true;
            harness.Audit.FailNextOperation = NodeManagerCleanupOperation.DiscoverPendingConfirmations;
            harness.Audit.FailNextOutcome = NodeManagerCleanupAuditOutcome.Succeeded;
            await Assert.ThatAsync(
                async () => await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            int priorAuditRecords = harness.Audit.Records.Length;
            ArrayOf<NodeManagerCleanupFailureHandle> pending =
                await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
            Assert.That(pending.Count, Is.EqualTo(1));
            Assert.That(pending[0], Is.EqualTo(handle));
            NodeManagerCleanupAuditRecord[] discoveryRecords =
                harness.Audit.Records.Skip(priorAuditRecords).ToArray();
            Assert.That(discoveryRecords, Has.Length.EqualTo(2));
            Assert.That(discoveryRecords[0].Outcome, Is.EqualTo(NodeManagerCleanupAuditOutcome.Requested));
            Assert.That(discoveryRecords[1].Outcome, Is.EqualTo(NodeManagerCleanupAuditOutcome.Succeeded));
            Assert.That(discoveryRecords[0].RequestId, Is.EqualTo(discoveryRecords[1].RequestId));
            Assert.That(harness.Audit.Records.Any(record =>
                record.Operation == NodeManagerCleanupOperation.DiscoverPendingConfirmations &&
                record.Outcome == NodeManagerCleanupAuditOutcome.Denied), Is.True);
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ReconcileAsync(pending[0]).ConfigureAwait(false);
            Assert.That((await harness.Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false)).IsEmpty,
                Is.True);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A consumed confirmation that did not clear its failure requires fresh verification.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task UnknownRetainedConfirmationRequiresFreshVerificationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.State.KeepNextConfirmedFailure = true;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            await Assert.ThatAsync(
                async () => await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.False);
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            Assert.That(harness.Verifier.Count, Is.EqualTo(2));
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// Each uncertain handle must be reconciled before the private generation can be destroyed.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task IndependentUnknownConfirmationsRequireSeparateReconciliationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle first = failures[0].Handle;
            NodeManagerCleanupFailureHandle second = failures[1].Handle;
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(first).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);
            harness.State.FailNextConfirmationSnapshot = true;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(second).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupConfirmationOutcomeUnknownException>()).ConfigureAwait(false);

            await harness.Recovery.ReconcileAsync(first).ConfigureAwait(false);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            harness.AssertNoPhysicalCleanup();
            await harness.Recovery.ReconcileAsync(second).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
        }

        /// <summary>
        /// Confirmation and physical cleanup wait until a concurrent verifier releases the retained resource.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ConcurrentVerificationRetainsPrivateGenerationUntilVerifierReturnsAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            IAsyncDisposable resource = harness.State.Resource(handle.FailureId);
            harness.Verifier.PauseNext();
            Task first = harness.Recovery.ConfirmAsync(handle, timeout.Token).AsTask();
            Task? second = null;
            try
            {
                await harness.Verifier.VerificationEntered.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(harness.Verifier.LastResource, Is.SameAs(resource));
                Assert.That(harness.Generation.CleanupRecoveryGate.ActiveCount, Is.EqualTo(1));
                second = harness.Recovery.ConfirmAsync(handle, timeout.Token).AsTask();
                Assert.That(harness.Verifier.Count, Is.EqualTo(2));
                Assert.That(harness.Generation.CleanupRecoveryGate.ActiveCount, Is.EqualTo(2));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(harness.State.Resource(handle.FailureId), Is.SameAs(resource));
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                harness.Verifier.Release();
                try
                {
                    if (second is not null)
                    {
                        await second.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        await first.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        // The first confirmation is stale after the second clears the same failure.
                    }
                }
            }
            await Assert.ThatAsync(
                async () => await first.ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Verifier.Count, Is.EqualTo(2));
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// An admitted verification can confirm its resource after cleanup closes new recovery admission.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task AdmittedVerificationCanConfirmAfterRecoveryGateClosesAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.Verifier.PauseNext();
            Task confirmation = harness.Recovery.ConfirmAsync(handle, timeout.Token).AsTask();
            try
            {
                await harness.Verifier.VerificationEntered.WaitAsync(timeout.Token).ConfigureAwait(false);
                harness.Generation.CleanupRecoveryGate.Retire();
                Task drain = harness.Generation.CleanupRecoveryGate.WaitForDrainAsync();
                Assert.That(drain.IsCompleted, Is.False);
                harness.AssertNoPhysicalCleanup();

                harness.Verifier.Release();
                await confirmation.WaitAsync(timeout.Token).ConfigureAwait(false);
                await drain.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
                harness.AssertPhysicalCleanupOnce();
            }
            finally
            {
                harness.Verifier.Release();
                try
                {
                    await confirmation.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                finally
                {
                    if (harness.Generation.CleanupRecoveryGate.State == NodeManagerGenerationState.Retiring)
                    {
                        harness.Generation.CleanupRecoveryGate.Restore();
                    }
                }
            }
        }

        /// <summary>
        /// Cancelling a waiting confirmation leaves the active verifier and retained failure untouched.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CancelledConfirmationDoesNotRemoveResourceWhileAnotherVerifierRunsAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            IAsyncDisposable resource = harness.State.Resource(handle.FailureId);
            harness.Verifier.PauseNext();
            Task first = harness.Recovery.ConfirmAsync(handle, timeout.Token).AsTask();
            Task? waiting = null;
            try
            {
                await harness.Verifier.VerificationEntered.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task waitingConfirmation = harness.Recovery.ConfirmAsync(handle, cancellation.Token).AsTask();
                waiting = waitingConfirmation;
                Assert.That(harness.Verifier.Count, Is.EqualTo(2));
                Assert.That(waitingConfirmation.IsCompleted, Is.False);
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(handle, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contains("Another cleanup recovery")).ConfigureAwait(false);
                Assert.That(harness.Verifier.Count, Is.EqualTo(2));

                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await waitingConfirmation.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(first.IsCompleted, Is.False);
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(harness.State.Resource(handle.FailureId), Is.SameAs(resource));
                Assert.That(harness.Generation.CleanupRecoveryGate.ActiveCount, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                harness.Verifier.Release();
                if (waiting is not null)
                {
                    try
                    {
                        await waiting.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                await first.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Cancelling a verifier releases its admission without clearing or retrying the resource.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CancelledVerificationReleasesPrivateGenerationAdmissionAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.Verifier.PauseNext();
            Task confirmation = harness.Recovery.ConfirmAsync(handle, cancellation.Token).AsTask();
            try
            {
                await harness.Verifier.VerificationEntered.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(harness.Generation.CleanupRecoveryGate.ActiveCount, Is.EqualTo(1));
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await confirmation.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(harness.Generation.CleanupRecoveryGate.ActiveCount, Is.Zero);
                Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                harness.Verifier.Release();
                try
                {
                    await confirmation.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // This request was cancelled before it could confirm the resource.
                }
            }
            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A private cleanup retry cannot overtake the confirmation outcome audit.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task CleanupWaitsForConfirmationOutcomeAuditAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(2)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = failures[0].Handle;
            NodeManagerCleanupFailureHandle second = failures[1].Handle;
            harness.Audit.PauseConfirmationOutcome = true;
            Task confirmation = harness.Recovery.ConfirmAsync(handle, timeout.Token).AsTask();
            try
            {
                await harness.Audit.OutcomeEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
                await Assert.ThatAsync(
                    async () => await harness.Owner
                        .ResumePrivateCleanupAfterConfirmationAsync(harness.Generation).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await harness.Recovery.ConfirmAsync(second, timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                harness.AssertNoPhysicalCleanup();
            }
            finally
            {
                harness.Audit.ReleaseOutcome();
                await confirmation.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            harness.AssertNoPhysicalCleanup();
            await harness.Recovery.ConfirmAsync(second, timeout.Token).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources.All(resource => resource.DisposeCount == 1), Is.True);
        }

        /// <summary>
        /// An outcome audit failure retains reconciliation ownership after the participant cleared its record.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task FailedOutcomeAuditRetainsCleanupUntilReconciledAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            harness.Audit.FailNextOutcome = NodeManagerCleanupAuditOutcome.Succeeded;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<NodeManagerCleanupPostConfirmationException>()
                    .With.Property(nameof(NodeManagerCleanupPostConfirmationException.Handle))
                    .EqualTo(handle)).ConfigureAwait(false);
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            await Assert.ThatAsync(
                async () => await harness.Owner
                    .ResumePrivateCleanupAfterConfirmationAsync(harness.Generation).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A failed resumed shutdown keeps its claimed confirmation until authorized reconciliation.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task FailedResumptionRetainsClaimedConfirmationUntilReconciledAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            var failure = new InvalidOperationException("The resumed shutdown failed.");
            harness.State.NextShutdownFailure = failure;

            NodeManagerCleanupPostConfirmationException? reported =
                await Assert.ThrowsAsync<NodeManagerCleanupPostConfirmationException>(
                    async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false))
                    .ConfigureAwait(false);

            Assert.That(reported!.Handle, Is.EqualTo(handle));
            Assert.That(reported.InnerException, Is.SameAs(failure));
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            Assert.That(harness.Generation.CanAttemptCleanup, Is.False);
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).IsEmpty, Is.True);
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
            harness.AssertNoPhysicalCleanup();
            Assert.That(harness.Audit.Records.Any(record =>
                record.Operation == NodeManagerCleanupOperation.Confirm &&
                record.Outcome == NodeManagerCleanupAuditOutcome.ResumptionFailed &&
                record.Handle == handle), Is.True);

            await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A fatal shutdown callback still releases its claim and completes the private cleanup waiter.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        // This verifies fatal resumption without causing a real allocation failure.
        // TODO: Use a shared fault-injection facility if the test infrastructure adds one.
        [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
            Justification = "Fatal shutdown must release confirmation ownership without losing the original failure.")]
        public async Task FatalResumptionFailureCompletesWaiterAndRetainsConfirmationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(1)
                .ConfigureAwait(false);
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;
            var failure = new OutOfMemoryException("The resumed shutdown failed.");
            harness.State.NextShutdownFailure = failure;

            OutOfMemoryException? reported = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(reported, Is.SameAs(failure));
            Assert.That(harness.Generation.CleanupRecoveryGate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(harness.Generation.HasPendingCleanupConfirmation, Is.True);
            Assert.That(harness.Generation.CanAttemptCleanup, Is.False);
            harness.AssertNoPhysicalCleanup();

            await harness.Recovery.ReconcileAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A private candidate retains failures created by completed cleanup callbacks until verified.
        /// </summary>
        [TestCase("Abort")]
        [TestCase("Disposal")]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task PrivateCleanupRecoversFailuresCreatedByCompletedStagesAsync(string stage)
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(
                0, lateFailureStage: stage).ConfigureAwait(false);
            Assert.That(harness.InitialCleanupFailure, Is.TypeOf<AggregateException>());
            harness.AssertCleanupCounts(1, stage == "Disposal" ? 1 : 0);
            Assert.That(harness.Owner.SnapshotCleanupGenerations(), Does.Contain(harness.Generation));

            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            Assert.That(failures.Count, Is.EqualTo(1));
            NodeManagerCleanupFailureHandle handle = failures[0].Handle;
            CleanupRecoveryResource resource = harness.State.Resource(handle.FailureId);
            Assert.That(resource.DisposeCount, Is.EqualTo(1));

            harness.Authorization.Allowed = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            harness.Authorization.Allowed = true;
            harness.Verifier.Verified = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Verifier.LastResource, Is.SameAs(resource));
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            harness.AssertCleanupCounts(1, stage == "Disposal" ? 1 : 0);
            Assert.That(resource.DisposeCount, Is.EqualTo(1));

            harness.Verifier.Verified = true;
            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            Assert.That(harness.Owner.SnapshotCleanupGenerations(), Does.Not.Contain(harness.Generation));
            Assert.That((await harness.DiscoverAsync().ConfigureAwait(false)).IsEmpty, Is.True);
            Assert.That(harness.Audit.Records.Any(record =>
                record.Operation == NodeManagerCleanupOperation.Confirm &&
                record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded &&
                record.Handle == handle), Is.True);
        }

        /// <summary>
        /// A private candidate does not restart cleanup of a failure retained before producer shutdown.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task PrivatePreexistingFailureBlocksProducerShutdownUntilConfirmationAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(
                0, preexistingFailure: true).ConfigureAwait(false);
            Assert.That(harness.State.ShutdownCount, Is.Zero);
            harness.AssertNoPhysicalCleanup();
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await harness.DiscoverAsync().ConfigureAwait(false);
            Assert.That(failures.Count, Is.EqualTo(1));
            NodeManagerCleanupFailureHandle handle = failures[0].Handle;
            CleanupRecoveryResource resource = harness.State.Resource(handle.FailureId);
            Assert.That(resource.DisposeCount, Is.EqualTo(1));

            harness.Verifier.Verified = false;
            await Assert.ThatAsync(
                async () => await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(harness.Verifier.LastResource, Is.SameAs(resource));
            Assert.That(harness.State.ShutdownCount, Is.Zero);
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            harness.AssertNoPhysicalCleanup();

            harness.Verifier.Verified = true;
            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            Assert.That(harness.State.ShutdownCount, Is.EqualTo(1));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            harness.AssertPhysicalCleanupOnce();
        }

        /// <summary>
        /// Verified private cleanup releases the writer's unit while retaining the original failure.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task VerifiedPrivateCleanupReleasesWriterUnitAndPreservesFailureAsync()
        {
            await using CleanupRecoveryHarness harness = await CreateCleanupRecoveryHarnessAsync(
                1, useWriter: true).ConfigureAwait(false);
            ILifecycleWriter writer = harness.Writer ??
                throw new AssertionException("The test did not retain the lifecycle writer.");
            IPreparedChanges prepared = harness.Prepared ??
                throw new AssertionException("The test did not retain the prepared view.");
            WeakReference factory = harness.Factory ??
                throw new AssertionException("The test did not capture the factory reference.");
            Exception cleanupFailure = harness.InitialCleanupFailure ??
                throw new AssertionException("The test did not retain the initial cleanup failure.");
            NodeManagerCleanupFailureHandle handle = (await harness.DiscoverAsync().ConfigureAwait(false))[0].Handle;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(factory.IsAlive, Is.True);
            harness.AssertNoPhysicalCleanup();
            await Assert.ThatAsync(
                () => prepared.DisposeAsync().AsTask(),
                Throws.Exception.SameAs(cleanupFailure)).ConfigureAwait(false);

            await harness.Recovery.ConfirmAsync(handle).ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            await Task.Yield();
            await CollectUntilReleasedAsync(() => !factory.IsAlive).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.IsAlive, Is.False);
                Assert.That(writer.IsCurrent, Is.True);
                Assert.That(harness.State.Resources[0].DisposeCount, Is.EqualTo(1));
            }
            await Assert.ThatAsync(
                () => prepared.DisposeAsync().AsTask(),
                Throws.Exception.SameAs(cleanupFailure)).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => writer.DisposeAsync().AsTask(),
                Throws.TypeOf<AggregateException>().With.InnerException.SameAs(cleanupFailure))
                .ConfigureAwait(false);
            harness.AssertPhysicalCleanupOnce();
            GC.KeepAlive(prepared);
            GC.KeepAlive(writer);
        }

        /// <summary>
        /// Starts a real host with recovery adapters bound before startup and retains a failed private candidate.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="AggregateException"></exception>
        private async Task<CleanupRecoveryHarness> CreateCleanupRecoveryHarnessAsync(
            int failureCount,
            bool deferConformance = false,
            bool useWriter = false,
            string? lateFailureStage = null,
            bool preexistingFailure = false,
            bool useSelfAdapter = false)
        {
            var authorization = new CleanupRecoveryAuthorization();
            var verifier = new CleanupRecoveryVerifier();
            var audit = new CleanupRecoveryAudit();
            NodeManagerCleanupRecovery? recovery = null;
            ConformanceUnitsManager? conformance = null;
            IConformanceContributor? registeredContributor = null;
            ILifecycleWriter? writer = null;
            IPreparedChanges? writerPrepared = null;
            Exception? initialCleanupFailure = null;
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
            {
                var server = new LifecycleTestServer(telemetry);
                recovery = new NodeManagerCleanupRecovery(server, authorization, verifier, audit);
                return server;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                ReuseCachedCertificates = true,
                AutoAccept = true
            };
            try
            {
                LifecycleTestServer testServer = await fixture.StartAsync(Path.Combine(m_pkiRoot, "cleanup-" + Guid.NewGuid().ToString("N")))
                    .ConfigureAwait(false);
                var owner = (NodeManagerLifecycle)testServer.NodeManagerLifecycle;
                string namespaceUri = kModelNamespaceUri + ":Cleanup:" + Guid.NewGuid().ToString("N");
                Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(namespaceUri);
                Mock<INodeManagerShutdown> shutdown = manager.As<INodeManagerShutdown>();
                Mock<IAsyncDisposable> disposable = manager.As<IAsyncDisposable>();
                Mock<INodeManagerCleanupParticipant> participantMock = manager.As<INodeManagerCleanupParticipant>();
                Mock<IConformanceContributor>? contributorMock = deferConformance
                    ? manager.As<IConformanceContributor>()
                    : null;
                INodeManagerCleanupParticipant participant = participantMock.Object;
                if (useSelfAdapter)
                {
                    var synchronous = new SyncNodeManagerAdapter(manager.Object);
                    manager.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                }
                var state = new CleanupRecoveryParticipantState(failureCount, participant);
                if (preexistingFailure)
                {
                    await state.AddFailedResourceAsync().ConfigureAwait(false);
                }
                shutdown.Setup(value => value.PrepareForShutdownAsync()).Returns(state.PrepareForShutdownAsync);
                disposable.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
                participantMock.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                    .Returns(state.GetFailures);
                participantMock.Setup(value => value.GetCleanupResource(It.IsAny<NodeManagerCleanupVerification>()))
                    .Returns(state.GetResource);
                participantMock.Setup(value => value.ConfirmCleanup(It.IsAny<NodeManagerCleanupConfirmation>()))
                    .Callback<NodeManagerCleanupConfirmation>(state.Confirm);

                async ValueTask RetainLateFailureAsync() => await state.AddFailedResourceAsync().ConfigureAwait(false);

                if (lateFailureStage == "Abort")
                {
                    manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                        .Returns((CancellationToken _) => RetainLateFailureAsync());
                }
                else if (lateFailureStage == "Disposal")
                {
                    disposable.Setup(value => value.DisposeAsync()).Returns(RetainLateFailureAsync);
                }
                else if (lateFailureStage is not null)
                {
                    throw new ArgumentOutOfRangeException(nameof(lateFailureStage));
                }
                if (contributorMock is not null)
                {
                    contributorMock.SetupGet(value => value.ConformanceUnits).Returns(ArrayOf<QualifiedName>.Empty);
                    contributorMock.SetupGet(value => value.ServerProfiles).Returns(ArrayOf<string>.Empty);
                    await Assert.ThatAsync(
                        async () => await state.PrepareForShutdownAsync().ConfigureAwait(false),
                        Throws.TypeOf<AggregateException>()).ConfigureAwait(false);
                    conformance = (testServer.CurrentInstance as IConformanceUnitsManagerProvider)?
                        .ConformanceUnitsManager ??
                        throw new InvalidOperationException("The live server has no conformance catalog.");
                    registeredContributor = contributorMock.Object;
                    conformance.Register(registeredContributor);
                }
                var factory = new CountingNodeManagerFactory(
                    [namespaceUri],
                    (_, _, _) => new ValueTask<IAsyncNodeManager>(manager.Object));
                IAsyncDisposable prepared;
                if (useWriter)
                {
                    writer = await owner.BeginAsync().ConfigureAwait(false);
                    writerPrepared = await writer.PrepareAsync([LifecycleChange.Add(factory)]).ConfigureAwait(false);
                    prepared = writerPrepared;
                }
                else
                {
                    prepared = await owner.PrepareAsync(
                        [LifecycleChange.Add(factory)]).ConfigureAwait(false);
                }
                Task? deferredCompletion = null;
                if (deferConformance)
                {
                    NodeManagerCleanupDeferredException? deferred =
                        await Assert.ThrowsAsync<NodeManagerCleanupDeferredException>(
                            async () => await prepared.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    deferredCompletion = deferred!.Completion;
                    initialCleanupFailure = deferred;
                }
                else
                {
                    initialCleanupFailure = await Assert.ThrowsAsync<AggregateException>(
                        async () => await prepared.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                }
                NodeManagerGeneration generation = owner.SnapshotCleanupGenerations().Single(
                    candidate => ReferenceEquals(candidate.NodeManager, manager.Object));
                return new CleanupRecoveryHarness(
                    fixture, recovery!, owner, generation, manager, disposable, participant,
                    state, authorization, verifier, audit, conformance, registeredContributor, deferredCompletion,
                    writer, writerPrepared, useWriter ? new WeakReference(factory) : null, initialCleanupFailure);
            }
            catch (Exception setupFailure) when (setupFailure is not OutOfMemoryException)
            {
                try
                {
                    try
                    {
                        if (conformance is not null && registeredContributor is not null)
                        {
                            conformance.Unregister(registeredContributor);
                        }
                        if (writer is not null)
                        {
                            try
                            {
                                await writer.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (AggregateException failure) when (
                                initialCleanupFailure is not null &&
                                ReferenceEquals(failure.InnerException, initialCleanupFailure))
                            {
                            }
                        }
                    }
                    finally
                    {
                        await fixture.StopAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "Cleanup recovery fixture setup and teardown both failed.", setupFailure, cleanupFailure);
                }
                throw;
            }
        }

        /// <summary>
        /// Owns the real test host and finishes its retained cleanup through the same recovery service.
        /// </summary>
        private sealed class CleanupRecoveryHarness(
            ServerFixture<LifecycleTestServer> fixture,
            NodeManagerCleanupRecovery recovery,
            NodeManagerLifecycle owner,
            NodeManagerGeneration generation,
            Mock<IAsyncNodeManager> manager,
            Mock<IAsyncDisposable> disposable,
            INodeManagerCleanupParticipant participant,
            CleanupRecoveryParticipantState state,
            CleanupRecoveryAuthorization authorization,
            CleanupRecoveryVerifier verifier,
            CleanupRecoveryAudit audit,
            ConformanceUnitsManager? conformance,
            IConformanceContributor? contributor,
            Task? deferredCompletion,
            ILifecycleWriter? writer,
            IPreparedChanges? prepared,
            WeakReference? factory,
            Exception? initialCleanupFailure) : IAsyncDisposable
        {
            internal NodeManagerCleanupRecovery Recovery { get; } = recovery;
            internal NodeManagerLifecycle Owner { get; } = owner;
            internal NodeManagerGeneration Generation { get; } = generation;
            internal Mock<IAsyncNodeManager> Manager { get; } = manager;
            internal INodeManagerCleanupParticipant Participant { get; } = participant;
            internal CleanupRecoveryParticipantState State { get; } = state;
            internal CleanupRecoveryAuthorization Authorization { get; } = authorization;
            internal CleanupRecoveryVerifier Verifier { get; } = verifier;
            internal CleanupRecoveryAudit Audit { get; } = audit;

            /// <summary>
            /// Gets the optional writer retained by the test.
            /// </summary>
            internal ILifecycleWriter? Writer { get; } = writer;

            /// <summary>
            /// Gets the optional caller-held prepared view.
            /// </summary>
            internal IPreparedChanges? Prepared { get; } = prepared;

            /// <summary>
            /// Gets a non-owning reference to the writer's factory.
            /// </summary>
            internal WeakReference? Factory { get; } = factory;

            /// <summary>
            /// Gets the initially reported cleanup failure.
            /// </summary>
            internal Exception? InitialCleanupFailure { get; } = initialCleanupFailure;

            /// <summary>
            /// Withdraws the host-owned registration and joins its deferred physical cleanup.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            internal async ValueTask ReleaseConformanceAsync()
            {
                IConformanceContributor? current = Interlocked.Exchange(ref m_contributor, null);
                if (current is null)
                {
                    return;
                }
                if (conformance is null || !conformance.Unregister(current))
                {
                    throw new InvalidOperationException("The test contributor was not registered.");
                }
                if (deferredCompletion is not null)
                {
                    await deferredCompletion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Discovers failures through the authorized host service.
            /// </summary>
            internal ValueTask<ArrayOf<RetainedNodeManagerCleanupFailure>> DiscoverAsync()
            {
                return Recovery.DiscoverAsync();
            }

            /// <summary>
            /// Asserts that neither address-space deletion nor manager disposal has started.
            /// </summary>
            internal void AssertNoPhysicalCleanup()
            {
                Manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
                disposable.Verify(value => value.DisposeAsync(), Times.Never);
            }

            /// <summary>
            /// Asserts that the completed private cleanup executed both physical stages exactly once.
            /// </summary>
            internal void AssertPhysicalCleanupOnce()
            {
                AssertCleanupCounts(1, 1);
            }

            /// <summary>
            /// Asserts the completed private cleanup callback counts.
            /// </summary>
            internal void AssertCleanupCounts(int deleteCount, int disposeCount)
            {
                Manager.Verify(
                    value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()),
                    Times.Exactly(deleteCount));
                disposable.Verify(value => value.DisposeAsync(), Times.Exactly(disposeCount));
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Audit.ReleaseOutcome();
                Audit.FailNextOutcome = null;
                Authorization.Allowed = true;
                Verifier.Verified = true;
                State.ResetFaults();
                try
                {
                    ArrayOf<RetainedNodeManagerCleanupFailure> failures = await DiscoverAsync().ConfigureAwait(false);
                    for (int index = 0; index < failures.Count; index++)
                    {
                        await Recovery.ConfirmAsync(failures[index].Handle).ConfigureAwait(false);
                    }
                    ArrayOf<NodeManagerCleanupFailureHandle> pending =
                        await Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
                    while (!pending.IsEmpty)
                    {
                        if (m_contributor is not null && Generation.CanAttemptCleanup)
                        {
                            await ReleaseConformanceAsync().ConfigureAwait(false);
                            pending = await Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
                            continue;
                        }
                        NodeManagerCleanupFailureHandle handle = pending[0];
                        await Recovery.ReconcileAsync(handle).ConfigureAwait(false);
                        pending = await Recovery.DiscoverPendingConfirmationsAsync().ConfigureAwait(false);
                        if (pending.Span.IndexOf(handle) >= 0 &&
                            !(m_contributor is not null && Generation.CanAttemptCleanup))
                        {
                            throw new InvalidOperationException("Test cleanup could not reconcile the confirmation.");
                        }
                    }
                }
                finally
                {
                    try
                    {
                        await ReleaseConformanceAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            if (Writer is not null)
                            {
                                try
                                {
                                    await Writer.DisposeAsync().ConfigureAwait(false);
                                }
                                catch (AggregateException failure) when (
                                    InitialCleanupFailure is not null &&
                                    ReferenceEquals(failure.InnerException, InitialCleanupFailure))
                                {
                                }
                            }
                        }
                        finally
                        {
                            await fixture.StopAsync().ConfigureAwait(false);
                        }
                    }
                }
            }

            private IConformanceContributor? m_contributor = contributor;
        }

        /// <summary>
        /// Supplies a test-owned participant registry that never retries a failed resource disposal.
        /// </summary>
        private sealed class CleanupRecoveryParticipantState
        {
            internal CleanupRecoveryParticipantState(int count, INodeManagerCleanupParticipant participant)
            {
                m_participant = participant;
                for (int index = 0; index < count; index++)
                {
                    var resource = new CleanupRecoveryResource();
                    m_resources.Add(Guid.NewGuid(), resource);
                }
            }

            internal bool FailNextConfirmationSnapshot { get; set; }
            internal bool KeepNextConfirmedFailure { get; set; }

            /// <summary>
            /// Gets or sets a persistent failure for participant discovery.
            /// </summary>
            internal Exception? DiscoveryFailure { get; set; }

            /// <summary>
            /// Gets or sets whether to return a resource without consuming the next verification.
            /// </summary>
            internal bool SkipNextVerificationConsumption { get; set; }

            /// <summary>
            /// Gets or sets whether to return an empty resource after consuming the next verification.
            /// </summary>
            internal bool ReturnEmptyResourceAfterConsumption { get; set; }

            /// <summary>
            /// Gets or sets whether to return without consuming the next confirmation.
            /// </summary>
            internal bool SkipNextConfirmationConsumption { get; set; }

            /// <summary>
            /// Gets or sets a confirmation failure raised before capability consumption.
            /// </summary>
            internal Exception? NextConfirmationBeforeConsumptionFailure { get; set; }

            /// <summary>
            /// Gets or sets a confirmation failure raised after capability consumption.
            /// </summary>
            internal Exception? NextConfirmationAfterConsumptionFailure { get; set; }

            internal Exception? NextShutdownFailure { get; set; }

            /// <summary>
            /// Gets or sets an action run while resolving a retained test resource.
            /// </summary>
            internal Action<NodeManagerCleanupVerification>? OnResourceLookup { get; set; }

            /// <summary>
            /// Runs during the next inspection of this participant's retained failures.
            /// </summary>
            internal Action<NodeManagerCleanupAccess>? OnFailureDiscovery { get; set; }

            /// <summary>
            /// Gets or sets the participant-supplied resource kind.
            /// </summary>
            internal string ResourceKind { get; set; } = "TestResource";

            /// <summary>
            /// Gets or sets the participant-supplied resource name.
            /// </summary>
            internal string? ResourceName { get; set; }

            /// <summary>
            /// Gets or sets the participant-supplied status.
            /// </summary>
            internal StatusCode FailureStatus { get; set; } = StatusCodes.BadUnexpectedError;

            internal int ShutdownCount => Volatile.Read(ref m_shutdownCount);
            internal int FailureSnapshots => Volatile.Read(ref m_failureSnapshots);
            internal bool WrongParticipantWasAuthorized { get; private set; }
            internal bool WrongParticipantConsumedVerification { get; private set; }
            internal bool ReplayedVerification { get; private set; }
            internal bool WrongParticipantConsumedConfirmation { get; private set; }
            internal bool ReplayedConfirmation { get; private set; }
            internal NodeManagerCleanupAccess? LastAccess { get; private set; }
            internal NodeManagerCleanupVerification? LastVerification { get; private set; }
            internal NodeManagerCleanupConfirmation? LastConfirmation { get; private set; }

            internal CleanupRecoveryResource[] Resources
            {
                get
                {
                    lock (m_gate)
                    {
                        return m_resources.Values.ToArray();
                    }
                }
            }

            internal CleanupRecoveryResource Resource(Guid id)
            {
                lock (m_gate)
                {
                    return m_resources[id];
                }
            }

            /// <summary>
            /// Adds a distinct resource only after its first cleanup attempt failed.
            /// </summary>
            internal async ValueTask<Guid> AddFailedResourceAsync()
            {
                var resource = new CleanupRecoveryResource();
                try
                {
                    await resource.DisposeAsync().ConfigureAwait(false);
                    throw new InvalidOperationException("The test resource unexpectedly closed.");
                }
                catch (InvalidOperationException failure) when (ReferenceEquals(failure, resource.Failure))
                {
                }
                var id = Guid.NewGuid();
                lock (m_gate)
                {
                    m_resources.Add(id, resource);
                    m_retained.Add(id, resource);
                }
                return id;
            }

            /// <summary>
            /// Attempts each resource once and reports the retained original failures on later calls.
            /// </summary>
            /// <exception cref="AggregateException"></exception>
            internal async ValueTask PrepareForShutdownAsync()
            {
                Interlocked.Increment(ref m_shutdownCount);
                if (Interlocked.Exchange(ref m_attempted, 1) == 0)
                {
                    KeyValuePair<Guid, CleanupRecoveryResource>[] resources;
                    lock (m_gate)
                    {
                        resources = [.. m_resources];
                    }
                    foreach (KeyValuePair<Guid, CleanupRecoveryResource> entry in resources)
                    {
                        CleanupRecoveryResource resource = entry.Value;
                        if (resource.DisposeCount != 0)
                        {
                            continue;
                        }
                        try
                        {
                            await resource.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (InvalidOperationException failure) when (ReferenceEquals(failure, resource.Failure))
                        {
                            lock (m_gate)
                            {
                                m_retained.Add(entry.Key, resource);
                            }
                        }
                    }
                }
                lock (m_gate)
                {
                    if (m_retained.Count != 0)
                    {
                        throw new AggregateException(m_retained.Values.Select(resource => resource.Failure));
                    }
                    if (NextShutdownFailure is { } failure)
                    {
                        NextShutdownFailure = null;
                        throw failure;
                    }
                }
            }

            /// <summary>
            /// Captures descriptors only while the host-issued capability is active.
            /// </summary>
            /// <exception cref="UnauthorizedAccessException"></exception>
            /// <exception cref="InvalidOperationException"></exception>
            internal ArrayOf<NodeManagerCleanupFailure> GetFailures(NodeManagerCleanupAccess access)
            {
                Interlocked.Increment(ref m_failureSnapshots);
                lock (m_gate)
                {
                    LastAccess = access;
                    WrongParticipantWasAuthorized |= access.IsAuthorizedFor(m_otherParticipant);
                    if (!access.IsAuthorizedFor(m_participant))
                    {
                        throw new UnauthorizedAccessException();
                    }
                    Action<NodeManagerCleanupAccess>? onFailureDiscovery = OnFailureDiscovery;
                    OnFailureDiscovery = null;
                    onFailureDiscovery?.Invoke(access);
                    if (DiscoveryFailure is { } discoveryFailure)
                    {
                        throw discoveryFailure;
                    }
                    if (m_failSnapshot)
                    {
                        m_failSnapshot = false;
                        throw new InvalidOperationException("The participant snapshot is unavailable.");
                    }
                    return new ArrayOf<NodeManagerCleanupFailure>(m_retained.Keys.Select(id =>
                        new NodeManagerCleanupFailure(id, ResourceKind, ResourceName, FailureStatus)).ToArray());
                }
            }

            /// <summary>
            /// Resolves one exact retained resource using the host-issued one-use capability.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            internal NodeManagerCleanupResource GetResource(NodeManagerCleanupVerification verification)
            {
                lock (m_gate)
                {
                    LastVerification = verification;
                    Action<NodeManagerCleanupVerification>? onResourceLookup = OnResourceLookup;
                    OnResourceLookup = null;
                    onResourceLookup?.Invoke(verification);
                    if (SkipNextVerificationConsumption)
                    {
                        SkipNextVerificationConsumption = false;
                        return NodeManagerCleanupResource.FromAsyncDisposable(m_retained.Values.Single());
                    }
                    WrongParticipantConsumedVerification |= verification.TryConsume(m_otherParticipant, out _);
                    if (!verification.TryConsume(m_participant, out Guid id) ||
                        !m_retained.TryGetValue(id, out CleanupRecoveryResource? resource))
                    {
                        throw new InvalidOperationException("The retained resource is unavailable.");
                    }
                    ReplayedVerification |= verification.TryConsume(m_participant, out _);
                    if (ReturnEmptyResourceAfterConsumption)
                    {
                        ReturnEmptyResourceAfterConsumption = false;
                        return default;
                    }
                    return NodeManagerCleanupResource.FromAsyncDisposable(resource);
                }
            }

            /// <summary>
            /// Atomically consumes confirmation and updates the participant's registry.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            internal void Confirm(NodeManagerCleanupConfirmation confirmation)
            {
                lock (m_gate)
                {
                    LastConfirmation = confirmation;
                    if (SkipNextConfirmationConsumption)
                    {
                        SkipNextConfirmationConsumption = false;
                        return;
                    }
                    if (NextConfirmationBeforeConsumptionFailure is { } beforeFailure)
                    {
                        NextConfirmationBeforeConsumptionFailure = null;
                        throw beforeFailure;
                    }
                    WrongParticipantConsumedConfirmation |= confirmation.TryConsume(m_otherParticipant, out _);
                    if (!confirmation.TryConsume(m_participant, out Guid id) || !m_retained.ContainsKey(id))
                    {
                        throw new InvalidOperationException("The retained failure is unavailable.");
                    }
                    ReplayedConfirmation |= confirmation.TryConsume(m_participant, out _);
                    if (!KeepNextConfirmedFailure)
                    {
                        m_retained.Remove(id);
                    }
                    KeepNextConfirmedFailure = false;
                    if (FailNextConfirmationSnapshot)
                    {
                        m_failSnapshot = true;
                        FailNextConfirmationSnapshot = false;
                    }
                    if (NextConfirmationAfterConsumptionFailure is { } afterFailure)
                    {
                        NextConfirmationAfterConsumptionFailure = null;
                        throw afterFailure;
                    }
                }
            }

            /// <summary>
            /// Removes test fault injection without changing any retained failure.
            /// </summary>
            internal void ResetFaults()
            {
                lock (m_gate)
                {
                    FailNextConfirmationSnapshot = false;
                    KeepNextConfirmedFailure = false;
                    DiscoveryFailure = null;
                    SkipNextVerificationConsumption = false;
                    ReturnEmptyResourceAfterConsumption = false;
                    SkipNextConfirmationConsumption = false;
                    NextConfirmationBeforeConsumptionFailure = null;
                    NextConfirmationAfterConsumptionFailure = null;
                    NextShutdownFailure = null;
                    OnResourceLookup = null;
                    m_failSnapshot = false;
                }
            }

            private readonly Lock m_gate = new();
            private readonly INodeManagerCleanupParticipant m_participant;

            private readonly INodeManagerCleanupParticipant m_otherParticipant =
                Mock.Of<INodeManagerCleanupParticipant>();

            private readonly Dictionary<Guid, CleanupRecoveryResource> m_resources = [];
            private readonly Dictionary<Guid, CleanupRecoveryResource> m_retained = [];
            private int m_attempted;
            private int m_shutdownCount;
            private int m_failureSnapshots;
            private bool m_failSnapshot;
        }

        /// <summary>
        /// Records the original failed resource disposal.
        /// </summary>
        private sealed class CleanupRecoveryResource : IAsyncDisposable
        {
            internal InvalidOperationException Failure { get; } = new("The test resource did not close.");
            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref m_disposeCount);
                return new ValueTask(Task.FromException(Failure));
            }

            private int m_disposeCount;
        }

        /// <summary>
        /// Represents the trusted host authorization policy for the real recovery service.
        /// </summary>
        private sealed class CleanupRecoveryAuthorization : INodeManagerCleanupAuthorization
        {
            internal bool Allowed { get; set; } = true;

            /// <inheritdoc/>
            public ValueTask<NodeManagerCleanupAuthorization> AuthorizeAsync(
                NodeManagerCleanupOperation operation,
                NodeManagerCleanupFailureHandle handle,
                CancellationToken cancellationToken)
            {
                return new ValueTask<NodeManagerCleanupAuthorization>(
                    new NodeManagerCleanupAuthorization(Allowed, "test-operator"));
            }
        }

        /// <summary>
        /// Models a consumer whose failed disposal can be completed through an external owner.
        /// </summary>
        private sealed class RetiredConsumerProbe(Exception failure) : IDisposable
        {
            /// <summary>
            /// Gets the number of attempted disposal callbacks.
            /// </summary>
            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            /// <summary>
            /// Gets whether the external owner completed cleanup.
            /// </summary>
            internal bool IsExternallyClosed => Volatile.Read(ref m_externallyClosed) != 0;

            /// <summary>
            /// Throws the original failure from the single lifecycle disposal attempt.
            /// </summary>
            public void Dispose()
            {
                Interlocked.Increment(ref m_disposeCount);
                throw failure;
            }

            /// <summary>
            /// Completes cleanup without invoking the failed disposal callback again.
            /// </summary>
            internal void FinishExternalCleanup()
            {
                Volatile.Write(ref m_externallyClosed, 1);
            }

            private int m_disposeCount;
            private int m_externallyClosed;
        }

        /// <summary>
        /// Represents a failed history point whose actual cleanup can be verified independently.
        /// </summary>
        private sealed class HistoryCleanupRecoveryPoint : IHistoryContinuationPoint
        {
            /// <inheritdoc/>
            public Guid Id { get; } = Guid.NewGuid();

            /// <summary>
            /// Gets the original disposal failure.
            /// </summary>
            internal InvalidOperationException Failure { get; } = new("The history point did not close.");

            /// <summary>
            /// Gets the number of attempted disposal callbacks.
            /// </summary>
            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            /// <summary>
            /// Gets whether the external owner completed cleanup.
            /// </summary>
            internal bool IsExternallyClosed => Volatile.Read(ref m_externallyClosed) != 0;

            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Increment(ref m_disposeCount);
                throw Failure;
            }

            /// <summary>
            /// Completes cleanup without invoking the failed disposal callback again.
            /// </summary>
            internal void FinishExternalCleanup()
            {
                Volatile.Write(ref m_externallyClosed, 1);
            }

            private int m_disposeCount;
            private int m_externallyClosed;
        }

        /// <summary>
        /// Represents the trusted verifier and records the exact resource it receives.
        /// </summary>
        private sealed class CleanupRecoveryVerifier : INodeManagerCleanupVerifier
        {
            internal bool Verified { get; set; } = true;
            internal int Count => Volatile.Read(ref m_count);

            /// <summary>
            /// Gets or sets an optional exact-resource verification used by the test.
            /// </summary>
            internal Func<NodeManagerCleanupResource, bool>? VerifyResource { get; set; }

            /// <summary>
            /// Gets or sets an optional asynchronous verification used by the test.
            /// </summary>
            internal Func<
                RetainedNodeManagerCleanupFailure,
                NodeManagerCleanupResource,
                NodeManagerCleanupVerificationContext,
                CancellationToken,
                ValueTask<bool>>? VerifyResourceAsync
            { get; set; }

            /// <summary>
            /// Gets the most recent sanitized description passed to the verifier.
            /// </summary>
            internal RetainedNodeManagerCleanupFailure? LastFailure => Volatile.Read(ref m_lastFailure);

            internal IAsyncDisposable? LastResource
            {
                get
                {
                    WeakReference<IAsyncDisposable>? reference = Volatile.Read(ref m_lastResource);
                    return reference is not null && reference.TryGetTarget(out IAsyncDisposable? resource)
                        ? resource
                        : null;
                }
            }

            /// <summary>
            /// Gets the most recent synchronous resource without retaining it.
            /// </summary>
            internal IDisposable? LastSynchronousResource
            {
                get
                {
                    WeakReference<IDisposable>? reference = Volatile.Read(ref m_lastSynchronousResource);
                    return reference is not null && reference.TryGetTarget(out IDisposable? resource)
                        ? resource
                        : null;
                }
            }

            internal Task VerificationEntered => m_entered.Task;

            /// <summary>
            /// Holds the next verification until the test releases it or cancels its request.
            /// </summary>
            internal void PauseNext()
            {
                Interlocked.Exchange(ref m_pauseNext, 1);
            }

            /// <summary>
            /// Releases a verification held by this test.
            /// </summary>
            internal void Release()
            {
                m_release.TrySetResult(true);
            }

            /// <inheritdoc/>
            public async ValueTask<bool> VerifyAsync(
                RetainedNodeManagerCleanupFailure failure,
                NodeManagerCleanupResource resource,
                NodeManagerCleanupVerificationContext context,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref m_count);
                Volatile.Write(ref m_lastFailure, failure);
                if (resource.TryGetAsyncDisposable(out IAsyncDisposable? asyncResource))
                {
                    Volatile.Write(ref m_lastResource, new WeakReference<IAsyncDisposable>(asyncResource));
                    Volatile.Write(ref m_lastSynchronousResource, null);
                }
                else if (resource.TryGetDisposable(out IDisposable? synchronousResource))
                {
                    Volatile.Write(ref m_lastSynchronousResource, new WeakReference<IDisposable>(synchronousResource));
                    Volatile.Write(ref m_lastResource, null);
                }
                else
                {
                    throw new InvalidOperationException("The test received no verification resource.");
                }
                try
                {
                    if (Interlocked.Exchange(ref m_pauseNext, 0) != 0)
                    {
                        m_entered.TrySetResult(true);
                        await m_release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    if (VerifyResourceAsync is { } verifyAsync)
                    {
                        return await verifyAsync(failure, resource, context, cancellationToken).ConfigureAwait(false);
                    }
                    return VerifyResource?.Invoke(resource) ?? Verified;
                }
                finally
                {
                    resource = default;
                }
            }

            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private RetainedNodeManagerCleanupFailure? m_lastFailure;
            private WeakReference<IAsyncDisposable>? m_lastResource;
            private WeakReference<IDisposable>? m_lastSynchronousResource;
            private int m_count;
            private int m_pauseNext;
        }

        /// <summary>
        /// Records host audit events and can hold or reject a selected confirmation outcome.
        /// </summary>
        private sealed class CleanupRecoveryAudit : INodeManagerCleanupAudit
        {
            internal NodeManagerCleanupAuditOutcome? FailNextOutcome { get; set; }
            internal NodeManagerCleanupOperation FailNextOperation { get; set; } = NodeManagerCleanupOperation.Confirm;
            internal bool PauseConfirmationOutcome { get; set; }

            /// <summary>
            /// Runs after the audit record has been stored.
            /// </summary>
            internal Func<NodeManagerCleanupAuditRecord, ValueTask>? AfterRecord { get; set; }

            internal TaskCompletionSource<bool> OutcomeEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal NodeManagerCleanupAuditRecord[] Records
            {
                get
                {
                    lock (m_gate)
                    {
                        return [.. m_records];
                    }
                }
            }

            /// <inheritdoc/>
            public async ValueTask RecordAsync(
                NodeManagerCleanupAuditRecord record, CancellationToken cancellationToken)
            {
                if (record.Operation == NodeManagerCleanupOperation.Confirm &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded &&
                    PauseConfirmationOutcome)
                {
                    OutcomeEntered.TrySetResult(true);
                    await m_release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (record.Operation == FailNextOperation && record.Outcome == FailNextOutcome)
                {
                    FailNextOutcome = null;
                    throw new InvalidOperationException("The test audit is unavailable.");
                }
                lock (m_gate)
                {
                    m_records.Add(record);
                }
                if (AfterRecord is { } afterRecord)
                {
                    await afterRecord(record).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Releases an outcome held by this test's audit adapter.
            /// </summary>
            internal void ReleaseOutcome()
            {
                m_release.TrySetResult(true);
            }

            private readonly Lock m_gate = new();
            private readonly List<NodeManagerCleanupAuditRecord> m_records = [];

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
