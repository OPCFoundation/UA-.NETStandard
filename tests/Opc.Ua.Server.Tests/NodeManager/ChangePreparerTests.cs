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

#pragma warning disable CS0618, UA0031 // Legacy adapter coverage; TODO: remove with synchronous manager support.

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
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ChangePreparerTests
    {
        /// <summary>
        /// Capturing empty metadata preserves the standard namespace and default encodeable types.
        /// </summary>
        [Test]
        public void ConstructorCapturesUnchangedAndEmptyMetadata()
        {
            Mock<IServerInternal> server = CreateServer();
            NamespaceTable namespaces = server.Object.NamespaceUris;
            StringTable serverUris = server.Object.ServerUris;
            var preparation = new ChangePreparer(server.Object);

            Assert.Multiple(() =>
            {
                Assert.That(preparation.NamespaceRevision, Is.EqualTo(namespaces.Version));
                Assert.That(preparation.NamespaceUris.ToArray(), Is.EqualTo(s_expectedDefaultNamespaces));
                Assert.That(preparation.NamespaceUris.GetIndex("http://opcfoundation.org/UA/"), Is.Zero);
                Assert.That(preparation.ServerRevision, Is.EqualTo(serverUris.Version));
                Assert.That(preparation.ServerUris.Count, Is.Zero);
                Assert.That(preparation.TypeRevision, Is.Zero);
                Assert.That(preparation.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(
                    preparation.Factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? rangeType),
                    Is.True);
                Assert.That(rangeType, Is.Not.Null);
            });
        }

        /// <summary>
        /// Serving metadata changes remain outside the preparation snapshot until binding.
        /// </summary>
        [Test]
        public void ConstructorSnapshotsNamespaceServerTypeAndFactoryMutationsUntilBind()
        {
            Mock<IServerInternal> server = CreateServer();
            server.Object.NamespaceUris.Append(kBaseNamespaceUri);
            server.Object.ServerUris.Append(kBaseServerUri);
            EncodeableFactory originalFactory = (EncodeableFactory)server.Object.Factory;
            Assert.That(originalFactory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? rangeType), Is.True);

            var preparation = new ChangePreparer(server.Object);
            ChangePreparer.StagingServer candidate = preparation.CreateServer();
            long namespaceRevision = preparation.NamespaceRevision;
            long serverRevision = preparation.ServerRevision;
            long typeRevision = preparation.TypeRevision;
            long factoryRevision = preparation.FactoryRevision;

            server.Object.NamespaceUris.Append(kSourceMutationNamespaceUri);
            server.Object.ServerUris.Append(kCandidateServerUri);
            server.Object.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            server.Object.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
            originalFactory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();

            Assert.Multiple(() =>
            {
                Assert.That(preparation.NamespaceRevision, Is.EqualTo(namespaceRevision));
                Assert.That(preparation.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(-1));
                Assert.That(candidate.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.NamespaceUris.Version, Is.EqualTo(namespaceRevision + 1));
                Assert.That(preparation.ServerRevision, Is.EqualTo(serverRevision));
                Assert.That(preparation.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(-1));
                Assert.That(candidate.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(-1));
                Assert.That(server.Object.ServerUris.Version, Is.EqualTo(serverRevision + 1));
                Assert.That(preparation.TypeRevision, Is.EqualTo(typeRevision));
                Assert.That(preparation.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(server.Object.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(preparation.FactoryRevision, Is.EqualTo(factoryRevision));
                Assert.That(preparation.Factory.TryGetEncodeableType(s_registrationId, out _), Is.False);
                Assert.That(candidate.Factory.TryGetEncodeableType(s_registrationId, out _), Is.False);
                Assert.That(
                    server.Object.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? canonicalType),
                    Is.True);
                Assert.That(canonicalType, Is.SameAs(rangeType));
                Assert.That(candidate.IsStaging, Is.True);
            });

            candidate.Bind();

            Assert.Multiple(() =>
            {
                Assert.That(candidate.IsStaging, Is.False);
                Assert.That(candidate.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(2));
                Assert.That(candidate.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(1));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(
                    candidate.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? boundType),
                    Is.True);
                Assert.That(boundType, Is.SameAs(rangeType));
            });
        }

        /// <summary>
        /// Candidate metadata changes do not affect serving registries before publication and binding.
        /// </summary>
        [Test]
        public void StagingServerKeepsCandidateMetadataPrivateUntilCanonicalBinding()
        {
            Mock<IServerInternal> server = CreateServer();
            server.Object.NamespaceUris.Append(kBaseNamespaceUri);
            server.Object.ServerUris.Append(kBaseServerUri);
            EncodeableFactory originalFactory = (EncodeableFactory)server.Object.Factory;
            Assert.That(originalFactory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? rangeType), Is.True);

            ChangePreparer.StagingServer candidate = new ChangePreparer(server.Object).CreateServer();
            ushort namespaceIndex = candidate.NamespaceUris.GetIndexOrAppend(kStagedNamespaceUri);
            ushort serverIndex = candidate.ServerUris.GetIndexOrAppend(kStagedServerUri);
            candidate.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            candidate.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
            candidate.Factory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();

            Assert.Multiple(() =>
            {
                Assert.That(namespaceIndex, Is.EqualTo(2));
                Assert.That(candidate.NamespaceUris.GetString(namespaceIndex), Is.EqualTo(kStagedNamespaceUri));
                Assert.That(serverIndex, Is.EqualTo(1));
                Assert.That(candidate.ServerUris.GetString(serverIndex), Is.EqualTo(kStagedServerUri));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(
                    candidate.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? stagedType),
                    Is.True);
                Assert.That(stagedType, Is.SameAs(rangeType));
                Assert.That(server.Object.NamespaceUris.Count, Is.EqualTo(2));
                Assert.That(server.Object.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.ServerUris.Count, Is.EqualTo(1));
                Assert.That(server.Object.ServerUris.GetIndex(kStagedServerUri), Is.EqualTo(-1));
                Assert.That(server.Object.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(server.Object.Factory.TryGetEncodeableType(s_registrationId, out _), Is.False);
                Assert.That(candidate.IsStaging, Is.True);
            });

            server.Object.NamespaceUris.Append(kStagedNamespaceUri);
            server.Object.ServerUris.Append(kStagedServerUri);
            server.Object.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            server.Object.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
            originalFactory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();
            candidate.Bind();

            Assert.Multiple(() =>
            {
                Assert.That(candidate.IsStaging, Is.False);
                Assert.That(candidate.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(namespaceIndex));
                Assert.That(candidate.ServerUris.GetIndex(kStagedServerUri), Is.EqualTo(serverIndex));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(
                    candidate.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? boundType),
                    Is.True);
                Assert.That(boundType, Is.SameAs(rangeType));
            });
        }

        /// <summary>
        /// Cleanup for an earlier target does not discard a same-identifier replacement's staged contribution.
        /// </summary>
        [Test]
        public void StagedReferenceCapturePreservesReplacementTargetIdentity()
        {
            Mock<IServerInternal> server = CreateServer();
            ChangePreparer.StagingServer candidate = new ChangePreparer(server.Object).CreateServer();
            using var owner = new ServingNodeManager(candidate);
            var oldTarget = new BaseObjectState(null)
            {
                NodeId = new NodeId("target", owner.NamespaceIndex)
            };
            var replacement = new BaseObjectState(null)
            {
                NodeId = oldTarget.NodeId
            };
            var references = new Dictionary<NodeId, IList<IReference>>();
            var reference = new LocalReference(
                ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, oldTarget.NodeId);
            candidate.TrackExternalReferences(references);
            Assert.That(candidate.TryStageExternalReference(owner, oldTarget, reference), Is.True);
            candidate.CaptureExternalReferences(references);
            Assert.That(candidate.TryStageExternalReference(owner, replacement, reference), Is.True);
            Assert.That(candidate.TryStageRootNotifierReference(replacement), Is.True);
            Assert.That(references[ObjectIds.Server], Has.Count.EqualTo(1));
            Assert.That(references[ObjectIds.Server][0], Is.InstanceOf<OwnedExternalReference>());
            var captured = (OwnedExternalReference)references[ObjectIds.Server][0];
            Assert.That(captured.TargetNode, Is.SameAs(replacement));
            Assert.That(captured.Target, Is.Null);

            var oldRemoval = new LocalReference(
                ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, oldTarget.NodeId, oldTarget);
            candidate.DiscardNodeReferences(oldTarget);
            Assert.That(references[ObjectIds.Server], Has.Count.EqualTo(1));
            var oldCleanup = new List<LocalReference> { oldRemoval };
            candidate.ExcludePreparedReferences(oldCleanup);
            Assert.That(oldCleanup, Has.Count.EqualTo(1));
            candidate.DiscardCapturedReferences(oldCleanup);
            var roots = new Dictionary<NodeId, IList<IReference>>();
            candidate.AppendRootNotifierReferences(roots);
            Assert.That(roots[ObjectIds.Server], Has.Count.EqualTo(1));

            var replacementRemoval = new LocalReference(
                ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, replacement.NodeId, replacement);
            var replacementCleanup = new List<LocalReference> { replacementRemoval };
            candidate.ExcludePreparedReferences(replacementCleanup);
            Assert.That(replacementCleanup, Is.Empty);
            candidate.DiscardCapturedReferences([replacementRemoval]);
            replacementCleanup.Add(replacementRemoval);
            candidate.ExcludePreparedReferences(replacementCleanup);
            Assert.That(replacementCleanup, Has.Count.EqualTo(1));
            roots.Clear();
            candidate.AppendRootNotifierReferences(roots);
            Assert.That(roots.ContainsKey(ObjectIds.Server), Is.False);
        }

        /// <summary>
        /// A staged root survives cleanup for an older node with the same identifier.
        /// </summary>
        [Test]
        public void StagedRootNotifierPreservesReplacementBeforeCapture()
        {
            Mock<IServerInternal> server = CreateServer();
            ChangePreparer.StagingServer candidate = new ChangePreparer(server.Object).CreateServer();
            var oldTarget = new BaseObjectState(null) { NodeId = new NodeId("root", 1) };
            var replacement = new BaseObjectState(null) { NodeId = oldTarget.NodeId };
            Assert.That(candidate.TryStageRootNotifierReference(replacement), Is.True);
            var oldRemoval = new LocalReference(
                ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, oldTarget.NodeId, oldTarget);

            candidate.DiscardCapturedReferences([oldRemoval]);
            candidate.UnstageRootNotifierReference(oldTarget);

            var references = new Dictionary<NodeId, IList<IReference>>();
            candidate.AppendRootNotifierReferences(references);
            Assert.That(references[ObjectIds.Server], Has.Count.EqualTo(1));
            Assert.That(
                ((OwnedExternalReference)references[ObjectIds.Server][0]).TargetNode,
                Is.SameAs(replacement));
            Assert.That(candidate.GetWithdrawnRootNotifierReferences(), Is.Empty);

            candidate.UnstageRootNotifierReference(replacement);
            references.Clear();
            candidate.AppendRootNotifierReferences(references);
            Assert.That(references.ContainsKey(ObjectIds.Server), Is.False);
            Assert.That(candidate.GetWithdrawnRootNotifierReferences(), Is.EquivalentTo(new[] { replacement.NodeId }));
        }

        /// <summary>
        /// Pruning a late stale addition restores the currently indexed target's identity.
        /// </summary>
        [Test]
        public void StagedReferencePruningRestoresCurrentTargetIdentity()
        {
            Mock<IServerInternal> server = CreateServer();
            ChangePreparer.StagingServer candidate = new ChangePreparer(server.Object).CreateServer();
            using var owner = new ServingNodeManager(candidate);
            var oldTarget = new BaseObjectState(null)
            {
                NodeId = new NodeId("target", owner.NamespaceIndex)
            };
            var replacement = new BaseObjectState(null) { NodeId = oldTarget.NodeId };
            owner.Index(replacement);
            var references = new Dictionary<NodeId, IList<IReference>>();
            var reference = new LocalReference(
                ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, oldTarget.NodeId);
            candidate.TrackExternalReferences(references);
            Assert.That(candidate.TryStageExternalReference(owner, replacement, reference), Is.True);
            candidate.CaptureExternalReferences(references);
            Assert.That(candidate.TryStageExternalReference(owner, oldTarget, reference), Is.True);
            Assert.That(
                ((OwnedExternalReference)references[ObjectIds.Server][0]).TargetNode,
                Is.SameAs(oldTarget));

            candidate.PruneStagedExternalReferences();

            Assert.That(references[ObjectIds.Server], Has.Count.EqualTo(1));
            Assert.That(
                ((OwnedExternalReference)references[ObjectIds.Server][0]).TargetNode,
                Is.SameAs(replacement));
            var staleCleanup = new List<LocalReference>
            {
                new(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, oldTarget.NodeId, oldTarget)
            };
            var replacementCleanup = new List<LocalReference>
            {
                new(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, replacement.NodeId, replacement)
            };
            candidate.ExcludePreparedReferences(staleCleanup);
            candidate.ExcludePreparedReferences(replacementCleanup);
            Assert.That(staleCleanup, Has.Count.EqualTo(1));
            Assert.That(replacementCleanup, Is.Empty);
        }

        /// <summary>
        /// Contributions without target identity retain their existing cleanup behavior.
        /// </summary>
        [Test]
        public void StagedReferenceCapturePreservesUnknownTargetCleanup()
        {
            Mock<IServerInternal> server = CreateServer();
            ChangePreparer.StagingServer candidate = new ChangePreparer(server.Object).CreateServer();
            var target = new BaseObjectState(null) { NodeId = new NodeId("target", 1) };
            var references = new Dictionary<NodeId, IList<IReference>>
            {
                [ObjectIds.Server] =
                [
                    new NodeStateReference(ReferenceTypeIds.HasNotifier, false, target.NodeId)
                ]
            };
            candidate.CaptureExternalReferences(references);
            var removal = new LocalReference(
                ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, target.NodeId, target);
            var cleanup = new List<LocalReference> { removal };
            candidate.ExcludePreparedReferences(cleanup);
            Assert.That(cleanup, Is.Empty);
            candidate.DiscardCapturedReferences([removal]);
            cleanup.Add(removal);
            candidate.ExcludePreparedReferences(cleanup);
            Assert.That(cleanup, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Detached candidates release target references after their ownership cleanup completes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DetachedReferenceMetadataIsReleasedAfterCleanupAsync(bool published)
        {
            Mock<IServerInternal> server = CreateServer();
            ChangePreparer.StagingServer candidate = new ChangePreparer(server.Object).CreateServer();
            var target = new BaseObjectState(null) { NodeId = new NodeId("root", 1) };
            Assert.That(candidate.TryStageRootNotifierReference(target), Is.True);
            var references = new Dictionary<NodeId, IList<IReference>>();
            candidate.AppendRootNotifierReferences(references);
            candidate.CaptureExternalReferences(references);
            if (published)
            {
                candidate.Bind();
                var pendingRoots = new Dictionary<NodeId, IList<IReference>>();
                candidate.AppendRootNotifierReferences(pendingRoots);
                Assert.That(pendingRoots, Is.Empty);
            }
            var removal = new LocalReference(
                ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, target.NodeId, target);
            var cleanup = new List<LocalReference> { removal };
            candidate.ExcludePreparedReferences(cleanup);
            Assert.That(cleanup, Is.Empty);
            Assert.Throws<InvalidOperationException>(candidate.ReleaseCapturedReferences);

            candidate.Detach();
            await candidate.DisposeAsync().ConfigureAwait(false);
            if (published)
            {
                cleanup.Add(removal);
                candidate.ExcludePreparedReferences(cleanup);
                Assert.That(cleanup, Is.Empty);
                candidate.ReleaseCapturedReferences();
            }

            cleanup.Add(removal);
            candidate.ExcludePreparedReferences(cleanup);
            Assert.That(cleanup, Has.Count.EqualTo(1));
            references.Clear();
            candidate.AppendRootNotifierReferences(references);
            Assert.That(references, Is.Empty);
        }

        /// <summary>
        /// Borrowed resources remain scoped to their owning server.
        /// </summary>
        /// <param name="foreignServer">Whether the candidate targets a different server.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BorrowedResourceCannotCrossServersAsync(bool foreignServer)
        {
            Mock<IServerInternal> owner = DeterministicServerMock.Create(out MonitoredItemQueueFactory ownerQueues);
            Mock<IServerInternal> foreign = DeterministicServerMock.Create(out MonitoredItemQueueFactory foreignQueues);
            foreach (Mock<IServerInternal> server in new[] { owner, foreign })
            {
                server.SetupGet(value => value.MessageContext).Returns(new ServiceMessageContext(
                    server.Object.Telemetry,
                    server.Object.Factory)
                {
                    NamespaceUris = server.Object.NamespaceUris,
                    ServerUris = server.Object.ServerUris
                });
            }
            using (ownerQueues)
            using (foreignQueues)
            await using (var resource = new TestableAsyncCustomNodeManager(
                owner.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                false,
                NullLogger.Instance,
                DeterministicServerMock.TestNamespaceUri))
            {
                var node = new BaseObjectState(null)
                {
                    NodeId = new NodeId("borrowed-owner", resource.NamespaceIndex),
                    BrowseName = new QualifiedName("borrowed-owner", resource.NamespaceIndex)
                };
                await resource.AddNodeAsync(resource.SystemContext, NodeId.Null, node).ConfigureAwait(false);
                var facade = new Mock<IAsyncNodeManager>();
                Mock<IAsyncDisposable> asyncDisposable = facade.As<IAsyncDisposable>();
                Mock<IDisposable> disposable = facade.As<IDisposable>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(resource.SyncNodeManager);
                var lease = new Mock<INodeManagerBorrowedResourceLease>();
                lease.SetupGet(value => value.NodeManager).Returns(facade.Object);
                lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                var factory = new Mock<INodeManagerBorrowedResourceFactory>();
                factory.Setup(value => value.CreateBorrowedAsync(
                        It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                        It.IsAny<ApplicationConfiguration>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((INodeManagerBorrowedResourcePreparation candidate, ApplicationConfiguration _,
                        CancellationToken _) =>
                    {
                        candidate.DeclareCandidate(lease.Object);
                        return default;
                    });
                var preparation = new ChangePreparer((foreignServer ? foreign : owner).Object);
                Task<IAsyncNodeManager> creation = preparation.CreateNodeManagerAsync(
                    factory.Object,
                    new ApplicationConfiguration(),
                    false,
                    _ => false,
                    _ => false,
                    CancellationToken.None).AsTask();

                try
                {
                    if (foreignServer)
                    {
                        await Assert.ThatAsync(
                            () => creation,
                            Throws.TypeOf<NotSupportedException>().With.Message.Contains("another server"))
                            .ConfigureAwait(false);
                        Assert.That(ChangePreparer.GetBorrowedLease(facade.Object), Is.Null);
                        lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
                    }
                    else
                    {
                        Assert.That(await creation.ConfigureAwait(false), Is.SameAs(facade.Object));
                        Assert.That(ChangePreparer.GetBorrowedLease(facade.Object), Is.Not.Null);
                    }
                    facade.Verify(value => value.CreateAddressSpaceAsync(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
                    facade.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
                    asyncDisposable.Verify(value => value.DisposeAsync(), Times.Never);
                    disposable.Verify(value => value.Dispose(), Times.Never);
                    Assert.That(resource.Find(node.NodeId), Is.SameAs(node));
                    Assert.That(resource.Server, Is.SameAs(owner.Object));
                }
                finally
                {
                    if (creation.Status == TaskStatus.RanToCompletion)
                    {
                        IAsyncNodeManager candidate = await creation.ConfigureAwait(false);
                        BorrowedNodeManagerLeaseState? cleanup = ChangePreparer.GetBorrowedLease(candidate);
                        Assert.That(cleanup, Is.Not.Null);
                        await cleanup!.RollbackAsync().ConfigureAwait(false);
                    }
                }
                lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Successful creation associates the candidate with its isolated staging facade.
        /// </summary>
        [Test]
        public async Task CreateNodeManagerAsyncSuccessBindsCandidateIdentityAndMetadata()
        {
            Mock<IServerInternal> server = CreateServer();
            var preparation = new ChangePreparer(server.Object);
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            IServerInternal? suppliedServer = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    suppliedServer = candidate;
                    candidate.NamespaceUris.GetIndexOrAppend(kCandidateNamespaceUri);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer staging = ChangePreparer.GetServer(created)!;

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.SameAs(manager));
                Assert.That(suppliedServer, Is.SameAs(staging));
                Assert.That(staging.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(staging.IsStaging, Is.True);
                Assert.That(staging.IsDetached, Is.False);
                Assert.That(factory.Invocations, Has.Count.EqualTo(1));
            });

            ChangePreparer.Bind(created);

            Assert.Multiple(() =>
            {
                Assert.That(ChangePreparer.GetServer(created), Is.SameAs(staging));
                Assert.That(staging.IsStaging, Is.False);
                Assert.That(staging.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
            });
        }

        /// <summary>
        /// A known live resource is not claimed or disposed when its wrapper has no cleanup lease.
        /// </summary>
        /// <param name="adapter">Whether the wrapper is a stack adapter.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CreateNodeManagerAsyncRejectsKnownLiveResourceBeforeClaimingAsync(bool adapter)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> resourceDisposal = resource.As<IAsyncDisposable>();
            var resourceSync = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(resourceSync);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var facade = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> facadeDisposal = facade.As<IAsyncDisposable>();
            facade.SetupGet(value => value.SyncNodeManager).Returns(resourceSync);
            IAsyncNodeManager candidate = adapter
                ? new AsyncNodeManagerAdapter(resourceSync)
                : facade.Object;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(candidate));

            NodeManagerBorrowedResourceException? failure =
                await Assert.ThrowsAsync<NodeManagerBorrowedResourceException>(async () =>
                    await preparation.CreateNodeManagerAsync(
                        factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(failure!.Candidate, Is.SameAs(candidate));
                Assert.That(ChangePreparer.GetServer(resource.Object), Is.Null);
                Assert.That(ChangePreparer.GetServer(candidate), Is.Null);
                Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            });
            resourceDisposal.Verify(value => value.DisposeAsync(), Times.Never);
            facadeDisposal.Verify(value => value.DisposeAsync(), Times.Never);
            resource.Verify(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A factory cannot transfer a previously live manager without a cleanup lease.
        /// </summary>
        [Test]
        public async Task CreateNodeManagerAsyncRejectsKnownLiveDirectManagerAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(resource.Object));

            NodeManagerBorrowedResourceException? failure =
                await Assert.ThrowsAsync<NodeManagerBorrowedResourceException>(async () =>
                    await preparation.CreateNodeManagerAsync(
                        factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);

            Assert.That(failure!.Candidate, Is.SameAs(resource.Object));
            Assert.That(ChangePreparer.GetServer(resource.Object), Is.Null);
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
            resource.Verify(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A borrowed lease cannot use an already-live resource as its candidate identity.
        /// </summary>
        [Test]
        public async Task BorrowedLeaseRejectsLiveDirectCandidateBeforeAssociationAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(resource.Object);
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation candidate, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    candidate.DeclareCandidate(lease.Object);
                    return default;
                });

            InvalidOperationException? failure =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await preparation.CreateNodeManagerAsync(
                        factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);

            Assert.That(failure!.Message, Does.Contain("distinct facade"));
            Assert.That(ChangePreparer.GetServer(resource.Object), Is.Null);
            Assert.That(NodeManagerGenerationRegistry.TryGet(resource.Object, out NodeManagerGeneration? current),
                Is.True);
            Assert.That(current, Is.SameAs(original));
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            lease.Verify(value => value.RetireAsync(It.IsAny<CancellationToken>()), Times.Never);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
            resource.Verify(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The lifecycle rejects a lease for an exact manager registered by another lifecycle.
        /// </summary>
        [Test]
        public async Task BorrowedLeaseRejectsExactlyRegisteredDirectCandidateAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(resource.Object);
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation candidate, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    candidate.DeclareCandidate(lease.Object);
                    return default;
                });

            await Assert.ThrowsAsync<NodeManagerAlreadyRegisteredException>(async () =>
                await preparation.CreateNodeManagerAsync(
                    factory.Object,
                    null!,
                    false,
                    manager => ReferenceEquals(manager, resource.Object),
                    manager => ReferenceEquals(manager, resource.Object),
                    CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(ChangePreparer.GetServer(resource.Object), Is.Null);
            Assert.That(NodeManagerGenerationRegistry.TryGet(resource.Object, out NodeManagerGeneration? current),
                Is.True);
            Assert.That(current, Is.SameAs(original));
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            lease.Verify(value => value.RetireAsync(It.IsAny<CancellationToken>()), Times.Never);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
            resource.Verify(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A leased adapter keeps its private binding separate from the live resource it wraps.
        /// </summary>
        /// <param name="replace">Whether the candidate replaces an existing registration.</param>
        /// <param name="continuationFirst">Whether the continuation binds before the monitored node.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task BorrowedAdapterBindingKeepsItsPrivateGenerationAsync(bool replace, bool continuationFirst)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var adapter = new AsyncNodeManagerAdapter(synchronous);
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(adapter);
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            MonitoredNode2? monitored = null;
            using var continuation = new ContinuationPoint();
            NodeManagerGeneration? facadeSource = null;
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    context.DeclareCandidate(lease.Object);
                    if (continuationFirst)
                    {
                        continuation.Manager = adapter;
                    }
                    monitored = new MonitoredNode2(
                        adapter, context.Server, new BaseObjectState(null) { NodeId = new NodeId(42, 1) });
                    Assert.That(monitored.Generation.TryGetGeneration(out facadeSource), Is.True);
                    if (!continuationFirst)
                    {
                        continuation.Manager = adapter;
                    }
                    return default;
                });
            IAsyncNodeManager? created = null;
            NodeManagerGeneration? candidate = null;
            try
            {
                created = await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, replace, _ => false, CancellationToken.None).ConfigureAwait(false);
                NodeManagerGeneration previous = NodeManagerGenerationRegistry.GetOrCreate(
                    new Mock<IAsyncNodeManager>().Object);
                candidate = replace
                    ? NodeManagerGenerationRegistry.CreateReplacement(previous, created)
                    : NodeManagerGenerationRegistry.CreateAddition(created);
                Assert.Multiple(() =>
                {
                    Assert.That(facadeSource, Is.Not.Null);
                    Assert.That(facadeSource, Is.Not.SameAs(original));
                    Assert.That(facadeSource!.NodeManager, Is.SameAs(adapter));
                    Assert.That(facadeSource.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                    Assert.That(continuation.Manager, Is.SameAs(adapter));
                    Assert.That(continuation.Generation, Is.SameAs(facadeSource.Reference));
                    Assert.That(candidate.Gate, Is.SameAs(facadeSource.Gate));
                    Assert.That(candidate.Gate, Is.Not.SameAs(original.Gate));
                    Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                    Assert.That(ChangePreparer.GetServer(resource.Object), Is.Null);
                    if (replace)
                    {
                        Assert.That(candidate.PrivateFacadeGeneration, Is.SameAs(facadeSource));
                    }
                    else
                    {
                        Assert.That(candidate, Is.SameAs(facadeSource));
                    }
                });
            }
            finally
            {
                if (candidate is { Gate.State: NodeManagerGenerationState.Prepared })
                {
                    candidate.Gate.Abort();
                }
                if (monitored is not null)
                {
                    await monitored.DisposeAsync().ConfigureAwait(false);
                }
                if (created is not null && ChangePreparer.GetBorrowedLease(created) is { } borrowed)
                {
                    await borrowed.RollbackAsync().ConfigureAwait(false);
                }
            }
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Declaring a borrowed facade does not change an unrelated adapter over the same live resource.
        /// </summary>
        /// <param name="beforeDeclaration">Whether the unrelated continuation is bound first.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BorrowedDeclarationPreservesUnrelatedAdapterAsync(bool beforeDeclaration)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var facade = new AsyncNodeManagerAdapter(synchronous);
            var unrelated = new AsyncNodeManagerAdapter(synchronous);
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(facade);
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            using var unrelatedContinuation = new ContinuationPoint();
            using var facadeContinuation = new ContinuationPoint();
            MonitoredNode2? unrelatedNode = null;
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    if (beforeDeclaration)
                    {
                        unrelatedContinuation.Manager = unrelated;
                    }
                    context.DeclareCandidate(lease.Object);
                    if (!beforeDeclaration)
                    {
                        unrelatedContinuation.Manager = unrelated;
                    }
                    unrelatedNode = new MonitoredNode2(
                        unrelated, context.Server, new BaseObjectState(null) { NodeId = new NodeId(44, 1) });
                    facadeContinuation.Manager = facade;
                    return default;
                });

            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            NodeManagerGeneration privateGeneration = NodeManagerGenerationRegistry.GetOrCreate(created);
            try
            {
                Assert.That(unrelatedContinuation.Generation, Is.SameAs(original.Reference));
                Assert.That(unrelatedContinuation.Manager, Is.SameAs(resource.Object));
                Assert.That(unrelatedNode!.Generation, Is.SameAs(original.Reference));
                Assert.That(facadeContinuation.Generation, Is.SameAs(privateGeneration.Reference));
                Assert.That(facadeContinuation.Manager, Is.SameAs(facade));
                Assert.That(privateGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                Assert.That(privateGeneration.Gate, Is.Not.SameAs(original.Gate));
            }
            finally
            {
                if (unrelatedNode is not null)
                {
                    await unrelatedNode.DisposeAsync().ConfigureAwait(false);
                }
                await ChangePreparer.GetBorrowedLease(created)!.RollbackAsync().ConfigureAwait(false);
            }

            Assert.That(unrelatedContinuation.Generation, Is.SameAs(original.Reference));
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(privateGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(facade), Is.SameAs(privateGeneration));
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// A facade bound to a live resource before declaration cannot become a private candidate.
        /// </summary>
        /// <param name="stagedBinding">Whether a monitored node creates the earlier binding.</param>
        /// <param name="catchFailure">Whether the factory catches the declaration failure.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task BorrowedDeclarationRejectsEarlierAliasBindingAsync(bool stagedBinding, bool catchFailure)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var facade = new AsyncNodeManagerAdapter(synchronous);
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(facade);
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            using var continuation = new ContinuationPoint();
            MonitoredNode2? monitored = null;
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    if (stagedBinding)
                    {
                        monitored = new MonitoredNode2(
                            facade, context.Server, new BaseObjectState(null) { NodeId = new NodeId(45, 1) });
                    }
                    else
                    {
                        continuation.Manager = facade;
                    }
                    try
                    {
                        context.DeclareCandidate(lease.Object);
                    }
                    catch (InvalidOperationException) when (catchFailure)
                    {
                    }
                    return default;
                });

            try
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await preparation.CreateNodeManagerAsync(
                        factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(failure!.Message, Does.Contain("before its candidate lease was declared"));
                NodeManagerGenerationReference? binding = stagedBinding ? monitored!.Generation : continuation.Generation;
                Assert.That(binding, Is.SameAs(original.Reference));
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(resource.Object), Is.SameAs(original));
                Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(ChangePreparer.GetServer(facade), Is.Null);
            }
            finally
            {
                if (monitored is not null)
                {
                    await monitored.DisposeAsync().ConfigureAwait(false);
                }
            }
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
            resource.Verify(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A factory failure rolls back a declared facade without rebinding its continuation to the live resource.
        /// </summary>
        [Test]
        public async Task BorrowedFactoryFailureAbortsDeclaredFacadeAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var facade = new AsyncNodeManagerAdapter(synchronous);
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(facade);
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            using var continuation = new ContinuationPoint();
            NodeManagerGeneration? candidate = null;
            var expected = new InvalidOperationException("The borrowed factory failed.");
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    context.DeclareCandidate(lease.Object);
                    continuation.Manager = facade;
                    Assert.That(continuation.Generation!.TryGetGeneration(out candidate), Is.True);
                    throw expected;
                });

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(continuation.Generation, Is.SameAs(candidate.Reference));
            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(facade), Is.SameAs(candidate));
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// A borrowed factory must declare exactly one lease while its callback is active.
        /// </summary>
        [Test]
        public async Task BorrowedFactoryRejectsMissingRepeatedAndLateDeclarationsAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var missing = new Mock<INodeManagerBorrowedResourceFactory>();
            missing.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await preparation.CreateNodeManagerAsync(
                    missing.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failure!.Message, Does.Contain("declared no lease"));

            var resource = new Mock<IAsyncNodeManager>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            var facade = new AsyncNodeManagerAdapter(synchronous);
            var first = new Mock<INodeManagerBorrowedResourceLease>();
            first.SetupGet(value => value.NodeManager).Returns(facade);
            first.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            var second = new Mock<INodeManagerBorrowedResourceLease>();
            INodeManagerBorrowedResourcePreparation? captured = null;
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    context.DeclareCandidate(first.Object);
                    Assert.Throws<InvalidOperationException>(() => context.DeclareCandidate(second.Object));
                    captured = context;
                    return default;
                });
            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Throws<InvalidOperationException>(() => captured!.DeclareCandidate(second.Object));
                second.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                await ChangePreparer.GetBorrowedLease(created)!.RollbackAsync().ConfigureAwait(false);
            }
            first.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            second.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Factory completion waits for a declaration that started before the callback returned.
        /// </summary>
        [Test]
        public async Task BorrowedFactoryWaitsForInProgressDeclarationAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            var facade = new AsyncNodeManagerAdapter(synchronous);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(() =>
            {
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The declaration was not released.");
                }
                return facade;
            });
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            Task? declaration = null;
            var factory = new Mock<INodeManagerBorrowedResourceFactory>();
            factory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    declaration = Task.Run(
                        () => context.DeclareCandidate(lease.Object),
                        CancellationToken.None);
                    return new ValueTask(entered.Task);
                });
            Task<IAsyncNodeManager> creation = preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).AsTask();
            IAsyncNodeManager? created = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(creation.IsCompleted, Is.False);
                lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
                release.Set();
                created = await creation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await declaration!.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(created, Is.SameAs(facade));
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(created).Gate.State,
                    Is.EqualTo(NodeManagerGenerationState.Prepared));
            }
            finally
            {
                release.Set();
                if (created is not null)
                {
                    await ChangePreparer.GetBorrowedLease(created)!.RollbackAsync().ConfigureAwait(false);
                }
            }
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A competing declaration cannot roll back the lease or facade owned by another factory.
        /// </summary>
        /// <param name="sameLease">Whether both factories declare the same lease instance.</param>
        /// <param name="failOwner">Whether the first factory fails after the competing declaration.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task BorrowedCompetingDeclarationPreservesExistingOwnerAsync(bool sameLease, bool failOwner)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = resource.As<IAsyncDisposable>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var facade = new AsyncNodeManagerAdapter(synchronous);
            var first = new Mock<INodeManagerBorrowedResourceLease>();
            first.SetupGet(value => value.NodeManager).Returns(facade);
            first.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            var second = sameLease ? first : new Mock<INodeManagerBorrowedResourceLease>();
            if (!sameLease)
            {
                second.SetupGet(value => value.NodeManager).Returns(facade);
                second.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            }
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ownerFailure = new InvalidOperationException("The owning factory failed.");
            INodeManagerBorrowedResourcePreparation? owner = null;
            var firstFactory = new Mock<INodeManagerBorrowedResourceFactory>();
            firstFactory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    context.DeclareCandidate(first.Object);
                    owner = context;
                    return new ValueTask(FinishOwnerFactoryAsync());
                });
            Task<IAsyncNodeManager> ownerCreation = preparation.CreateNodeManagerAsync(
                firstFactory.Object, null!, false, _ => false, CancellationToken.None).AsTask();
            var competingFactory = new Mock<INodeManagerBorrowedResourceFactory>();
            competingFactory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    context.DeclareCandidate(second.Object);
                    return default;
                });
            try
            {
                await Assert.ThrowsAsync<NodeManagerAlreadyRegisteredException>(
                    async () => await preparation.CreateNodeManagerAsync(
                        competingFactory.Object, null!, false, _ => false, CancellationToken.None)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(ownerCreation.IsCompleted, Is.False);
                Assert.That(ChangePreparer.GetServer(facade), Is.SameAs(owner!.Server));
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(facade).Gate.State,
                    Is.EqualTo(NodeManagerGenerationState.Prepared));
                Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                first.VerifyGet(value => value.NodeManager, Times.Once);
                first.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
                second.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
                disposal.Verify(value => value.DisposeAsync(), Times.Never);
            }
            finally
            {
                release.TrySetResult(true);
                if (failOwner)
                {
                    InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await ownerCreation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(failure, Is.SameAs(ownerFailure));
                }
                else
                {
                    IAsyncNodeManager created =
                        await ownerCreation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    await ChangePreparer.GetBorrowedLease(created)!.RollbackAsync().ConfigureAwait(false);
                }
            }
            first.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            if (!sameLease)
            {
                second.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
            Assert.That(original.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            disposal.Verify(value => value.DisposeAsync(), Times.Never);

            async Task FinishOwnerFactoryAsync()
            {
                await release.Task.ConfigureAwait(false);
                if (failOwner)
                {
                    throw ownerFailure;
                }
            }
        }

        /// <summary>
        /// A lease whose candidate cannot be read remains with the factory and can be declared again.
        /// </summary>
        /// <param name="returnNull">Whether the candidate getter returns null instead of throwing.</param>
        /// <param name="catchFailure">Whether the factory catches the declaration failure.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task BorrowedDeclarationDoesNotAcceptUnreadableCandidateAsync(bool returnNull, bool catchFailure)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            var facade = new AsyncNodeManagerAdapter(synchronous);
            var expected = new InvalidOperationException("The candidate getter failed.");
            int reads = 0;
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.SetupGet(value => value.NodeManager).Returns(() =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    if (returnNull)
                    {
                        return null!;
                    }
                    throw expected;
                }
                return facade;
            });
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            var firstFactory = new Mock<INodeManagerBorrowedResourceFactory>();
            firstFactory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    try
                    {
                        context.DeclareCandidate(lease.Object);
                    }
                    catch (InvalidOperationException) when (catchFailure)
                    {
                    }
                    return default;
                });
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await preparation.CreateNodeManagerAsync(
                    firstFactory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            if (returnNull)
            {
                Assert.That(failure!.Message, Does.Contain("returned no candidate"));
            }
            else
            {
                Assert.That(failure, Is.SameAs(expected));
            }
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(ChangePreparer.GetServer(facade), Is.Null);

            var retryFactory = new Mock<INodeManagerBorrowedResourceFactory>();
            retryFactory.Setup(value => value.CreateBorrowedAsync(
                    It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                    CancellationToken _) =>
                {
                    context.DeclareCandidate(lease.Object);
                    return default;
                });
            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                retryFactory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(created, Is.SameAs(facade));
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(created).Gate.State,
                    Is.EqualTo(NodeManagerGenerationState.Prepared));
            }
            finally
            {
                await ChangePreparer.GetBorrowedLease(created)!.RollbackAsync().ConfigureAwait(false);
            }
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A serving resource remains live while its distinct staged facade remains private.
        /// </summary>
        [Test]
        public async Task StagedBindingDoesNotClaimKnownServingResourceAsync()
        {
            Mock<IServerInternal> serving = CreateServer();
            await using var resource = new ServingNodeManager(serving.Object);
            Assert.That(NodeManagerGenerationRegistry.TryGet(resource, out _), Is.False);
            var preparation = new ChangePreparer(serving.Object);
            var facade = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> facadeDisposal = facade.As<IAsyncDisposable>();
            facade.SetupGet(value => value.SyncNodeManager).Returns(resource.SyncNodeManager);
            MonitoredNode2? monitored = null;
            MonitoredNode2? facadeMonitored = null;
            NodeManagerGeneration? source = null;
            NodeManagerGeneration? facadeSource = null;
            NodeManagerGenerationState stateDuringFactory = default;
            NodeManagerGenerationState facadeStateDuringFactory = default;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration _, CancellationToken _) =>
                {
                    monitored = new MonitoredNode2(
                        resource,
                        server,
                        new BaseObjectState(null) { NodeId = new NodeId(42, 1) });
                    source = NodeManagerGenerationRegistry.GetOrCreate(resource);
                    stateDuringFactory = source.Gate.State;
                    facadeMonitored = new MonitoredNode2(
                        facade.Object,
                        server,
                        new BaseObjectState(null) { NodeId = new NodeId(43, 1) });
                    facadeSource = NodeManagerGenerationRegistry.GetOrCreate(facade.Object, server);
                    facadeStateDuringFactory = facadeSource.Gate.State;
                    return new ValueTask<IAsyncNodeManager>(facade.Object);
                });

            try
            {
                NodeManagerBorrowedResourceException? failure =
                    await Assert.ThrowsAsync<NodeManagerBorrowedResourceException>(async () =>
                        await preparation.CreateNodeManagerAsync(
                            factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                        .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(failure!.Candidate, Is.SameAs(facade.Object));
                    Assert.That(stateDuringFactory, Is.EqualTo(NodeManagerGenerationState.Live));
                    Assert.That(monitored!.Generation.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                    Assert.That(bound, Is.SameAs(source));
                    Assert.That(source!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                    Assert.That(facadeStateDuringFactory, Is.EqualTo(NodeManagerGenerationState.Prepared));
                    Assert.That(facadeMonitored!.Generation.TryGetGeneration(
                        out NodeManagerGeneration? facadeBound), Is.True);
                    Assert.That(facadeBound, Is.SameAs(facadeSource));
                    Assert.That(facadeSource!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
                    Assert.That(ChangePreparer.GetServer(resource), Is.Null);
                    Assert.That(resource.DisposeCount, Is.Zero);
                });
                facadeDisposal.Verify(value => value.DisposeAsync(), Times.Never);
            }
            finally
            {
                if (monitored is not null)
                {
                    await monitored.DisposeAsync().ConfigureAwait(false);
                }
                if (facadeMonitored is not null)
                {
                    await facadeMonitored.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A raw source created inside a private factory remains private when its facade is prepared.
        /// </summary>
        [Test]
        public async Task FactoryCreatedGenericSourceSharesPrivateFacadeGateAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            NodeManagerGeneration? source = null;
            MonitoredNode2? monitored = null;
            NodeManagerGenerationState stateDuringFactory = default;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration _, CancellationToken _) =>
                {
                    var resource = new Mock<IAsyncNodeManager>();
                    var resourceSync = new SyncNodeManagerAdapter(resource.Object);
                    resource.SetupGet(value => value.SyncNodeManager).Returns(resourceSync);
                    monitored = new MonitoredNode2(
                        resource.Object,
                        server,
                        new BaseObjectState(null) { NodeId = new NodeId(42, 1) });
                    source = NodeManagerGenerationRegistry.GetOrCreate(resource.Object, server);
                    stateDuringFactory = source.Gate.State;
                    var facade = new Mock<IAsyncNodeManager>();
                    facade.SetupGet(value => value.SyncNodeManager).Returns(resourceSync);
                    return new ValueTask<IAsyncNodeManager>(facade.Object);
                });

            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            try
            {
                NodeManagerGeneration candidate = NodeManagerGenerationRegistry.CreateAddition(created);
                Assert.Multiple(() =>
                {
                    Assert.That(stateDuringFactory, Is.EqualTo(NodeManagerGenerationState.Prepared));
                    Assert.That(source, Is.Not.Null);
                    Assert.That(monitored!.Generation.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                    Assert.That(bound, Is.SameAs(source));
                    Assert.That(source!.Gate, Is.SameAs(candidate.Gate));
                    Assert.That(candidate.NodeManager, Is.SameAs(created));
                });
            }
            finally
            {
                if (monitored is not null)
                {
                    await monitored.DisposeAsync().ConfigureAwait(false);
                }
                await ChangePreparer.DisposeAsync(created).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Both raw and facade bindings created by a private factory retain their original identities.
        /// </summary>
        /// <param name="facadeFirst">Whether the facade is bound before the raw source.</param>
        /// <param name="replace">Whether the candidate replaces an existing registration.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task FactoryRetainsRawAndFacadeBindingsCreatedBeforeReturnAsync(bool facadeFirst, bool replace)
        {
            Mock<IServerInternal> serving = CreateServer();
            using var queues = new MonitoredItemQueueFactory(serving.Object.Telemetry);
            serving.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            var preparation = new ChangePreparer(serving.Object);
            var previous = new Mock<IAsyncNodeManager>();
            NodeManagerGeneration previousGeneration = NodeManagerGenerationRegistry.GetOrCreate(previous.Object);
            MonitoredNode2? rawBinding = null;
            MonitoredNode2? facadeBinding = null;
            MonitoredItem? rawItem = null;
            MonitoredItem? facadeItem = null;
            NodeManagerGeneration? rawSource = null;
            NodeManagerGeneration? facadeSource = null;
            IAsyncNodeManager? created = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration _, CancellationToken _) =>
                {
                    var raw = new Mock<IAsyncNodeManager>();
                    var rawSync = new SyncNodeManagerAdapter(raw.Object);
                    raw.SetupGet(value => value.SyncNodeManager).Returns(rawSync);
                    var facade = new Mock<IAsyncNodeManager>();
                    facade.SetupGet(value => value.SyncNodeManager).Returns(rawSync);
                    var node = new BaseObjectState(null) { NodeId = new NodeId(42, 1) };
                    if (facadeFirst)
                    {
                        facadeItem = CreateItem(server, facade.Object, node.NodeId, 1);
                        facadeBinding = new MonitoredNode2(facade.Object, server, node);
                        rawItem = CreateItem(server, raw.Object, node.NodeId, 2);
                        rawBinding = new MonitoredNode2(raw.Object, server, node);
                    }
                    else
                    {
                        rawItem = CreateItem(server, raw.Object, node.NodeId, 2);
                        rawBinding = new MonitoredNode2(raw.Object, server, node);
                        facadeItem = CreateItem(server, facade.Object, node.NodeId, 1);
                        facadeBinding = new MonitoredNode2(facade.Object, server, node);
                    }
                    rawSource = NodeManagerGenerationRegistry.GetOrCreate(raw.Object, server);
                    facadeSource = NodeManagerGenerationRegistry.GetOrCreate(facade.Object, server);
                    return new ValueTask<IAsyncNodeManager>(facade.Object);
                });

            try
            {
                created = await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, replace, _ => false, CancellationToken.None).ConfigureAwait(false);
                NodeManagerGeneration candidate = replace
                    ? NodeManagerGenerationRegistry.CreateReplacement(previousGeneration, created)
                    : NodeManagerGenerationRegistry.CreateAddition(created);
                Assert.Multiple(() =>
                {
                    Assert.That(rawBinding!.Generation.TryGetGeneration(out NodeManagerGeneration? raw), Is.True);
                    Assert.That(raw, Is.SameAs(rawSource));
                    Assert.That(facadeBinding!.Generation.TryGetGeneration(out NodeManagerGeneration? facade), Is.True);
                    Assert.That(facade, Is.SameAs(facadeSource));
                    Assert.That(rawItem!.Generation!.TryGetGeneration(out NodeManagerGeneration? rawItemSource), Is.True);
                    Assert.That(rawItemSource, Is.SameAs(rawSource));
                    Assert.That(
                        facadeItem!.Generation!.TryGetGeneration(out NodeManagerGeneration? facadeItemSource), Is.True);
                    Assert.That(facadeItemSource, Is.SameAs(facadeSource));
                    Assert.That(rawSource!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                    Assert.That(facadeSource!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                    Assert.That(rawSource.Gate, Is.SameAs(candidate.Gate));
                    Assert.That(facadeSource.Gate, Is.SameAs(candidate.Gate));
                    Assert.That(candidate.PrivateSourceGeneration, Is.SameAs(rawSource));
                    Assert.That(candidate.Gate.TryEnter(out int _), Is.False);
                    if (replace)
                    {
                        Assert.That(candidate.Slot, Is.SameAs(previousGeneration.Slot));
                        Assert.That(candidate.Number, Is.EqualTo(previousGeneration.Number + 1));
                        Assert.That(facadeSource.Reference.Identifies(candidate), Is.False);
                        Assert.That(candidate.PrivateFacadeGeneration, Is.SameAs(facadeSource));
                    }
                    else
                    {
                        Assert.That(candidate, Is.SameAs(facadeSource));
                    }
                });
            }
            finally
            {
                rawItem?.Dispose();
                facadeItem?.Dispose();
                if (rawBinding is not null)
                {
                    await rawBinding.DisposeAsync().ConfigureAwait(false);
                }
                if (facadeBinding is not null)
                {
                    await facadeBinding.DisposeAsync().ConfigureAwait(false);
                }
                if (created is not null)
                {
                    await ChangePreparer.DisposeAsync(created).ConfigureAwait(false);
                }
            }

            static MonitoredItem CreateItem(IServerInternal server, IAsyncNodeManager manager, NodeId nodeId, uint id)
            {
                return new MonitoredItem(
                    server,
                    manager,
                    null!,
                    1,
                    id,
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    MonitoringMode.Reporting,
                    1,
                    null,
                    null,
                    null,
                    0,
                    1,
                    true,
                    0);
            }
        }

        /// <summary>
        /// A provisional source unrelated to the returned facade does not retain a private generation.
        /// </summary>
        [Test]
        public async Task FactoryReleasesUnselectedProvisionalSourceAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var unused = new Mock<IAsyncNodeManager>();
            NodeManagerGeneration? provisional = null;
            var facade = new Mock<IAsyncNodeManager>();
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration _, CancellationToken _) =>
                {
                    provisional = NodeManagerGenerationRegistry.GetOrCreate(unused.Object, server);
                    return new ValueTask<IAsyncNodeManager>(facade.Object);
                });

            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(provisional, Is.Not.Null);
                Assert.That(provisional!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
                Assert.That(NodeManagerGenerationRegistry.TryGet(unused.Object, out _), Is.False);
                NodeManagerGeneration current = NodeManagerGenerationRegistry.GetOrCreate(unused.Object);
                Assert.That(current, Is.Not.SameAs(provisional));
                Assert.That(current.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            }
            finally
            {
                await ChangePreparer.DisposeAsync(created).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A failed factory releases provisional sources without retiring their later generations.
        /// </summary>
        [Test]
        public async Task FailedFactoryReleasesProvisionalSourceAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            var resource = new Mock<IAsyncNodeManager>();
            NodeManagerGeneration? provisional = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration _, CancellationToken _) =>
                {
                    provisional = NodeManagerGenerationRegistry.GetOrCreate(resource.Object, server);
                    throw new InvalidOperationException("Factory failed.");
                });

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(provisional, Is.Not.Null);
            Assert.That(provisional!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(NodeManagerGenerationRegistry.TryGet(resource.Object, out _), Is.False);
            NodeManagerGeneration current = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            Assert.That(current, Is.Not.SameAs(provisional));
            Assert.That(current.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// A reserved publication completes before a concurrent rollback can enter provider cleanup.
        /// </summary>
        /// <param name="commit">Whether publication commits instead of releasing its reservation.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BorrowedPublicationReservationSerializesRollbackAsync(bool commit)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            await using ChangePreparer.StagingServer server = preparation.CreateServer();
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            var state = new BorrowedNodeManagerLeaseState(lease.Object, server);
            using BorrowedNodeManagerLeaseState.PublicationReservation reservation = state.ReservePublication();

            Task rollback = state.RollbackAsync().AsTask();
            Assert.That(rollback.IsCompleted, Is.False);
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            if (commit)
            {
                reservation.Commit();
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await rollback.ConfigureAwait(false)).ConfigureAwait(false);
                await state.RetireAsync().ConfigureAwait(false);
                lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
                lease.Verify(value => value.RetireAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            else
            {
                reservation.Dispose();
                await rollback.ConfigureAwait(false);
                lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
                lease.Verify(value => value.RetireAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        /// <summary>
        /// A consumed reservation cannot release a later publication's ownership.
        /// </summary>
        [Test]
        public async Task ConsumedPublicationReservationCannotCancelSuccessorAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            await using ChangePreparer.StagingServer server = preparation.CreateServer();
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            var state = new BorrowedNodeManagerLeaseState(lease.Object, server);
            using BorrowedNodeManagerLeaseState.PublicationReservation first = state.ReservePublication();
            first.Dispose();
            using BorrowedNodeManagerLeaseState.PublicationReservation second = state.ReservePublication();

            Task rollback = state.RollbackAsync().AsTask();
            first.Dispose();
            Assert.Throws<InvalidOperationException>(first.Commit);
            Assert.Throws<InvalidOperationException>(state.MarkPublished);
            Assert.That(rollback.IsCompleted, Is.False);
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);

            second.Commit();
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await rollback.ConfigureAwait(false)).ConfigureAwait(false);
            state.MarkPublished();
            await state.RetireAsync().ConfigureAwait(false);
            lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            lease.Verify(value => value.RetireAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A publication cannot reserve a lease after provider rollback has started.
        /// </summary>
        [Test]
        public async Task BorrowedPublicationRejectsAnExistingRollbackAsync()
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            await using ChangePreparer.StagingServer server = preparation.CreateServer();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lease = new Mock<INodeManagerBorrowedResourceLease>();
            lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(release.Task));
            var state = new BorrowedNodeManagerLeaseState(lease.Object, server);

            Task rollback = state.RollbackAsync().AsTask();
            try
            {
                Assert.That(rollback.IsCompleted, Is.False);
                Assert.Throws<InvalidOperationException>(() => state.ReservePublication());
                lease.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
                lease.Verify(value => value.RetireAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                release.TrySetResult(true);
                await rollback.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A conflicting claim cannot replace the winning candidate's facade or metadata.
        /// </summary>
        [Test]
        public async Task CreateNodeManagerAsyncCompetingCandidateConflictLeavesWinnerIsolated()
        {
            Mock<IServerInternal> server = CreateServer();
            var firstPreparation = new ChangePreparer(server.Object);
            var secondPreparation = new ChangePreparer(server.Object);
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            IServerInternal? firstCandidate = null;
            IServerInternal? competingCandidate = null;
            int calls = 0;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        firstCandidate = candidate;
                        candidate.NamespaceUris.GetIndexOrAppend(kBaseNamespaceUri);
                    }
                    else
                    {
                        competingCandidate = candidate;
                        candidate.NamespaceUris.GetIndexOrAppend(kStagedNamespaceUri);
                    }
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            IAsyncNodeManager winner = await firstPreparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer owner = ChangePreparer.GetServer(winner)!;
            NodeManagerAlreadyRegisteredException conflict = (await Assert.ThrowsAsync<NodeManagerAlreadyRegisteredException>(
                async () => await secondPreparation.CreateNodeManagerAsync(
                    factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(conflict, Is.Not.Null);
                Assert.That(firstCandidate, Is.SameAs(owner));
                Assert.That(competingCandidate, Is.Not.SameAs(owner));
                Assert.That(ChangePreparer.GetServer(manager), Is.SameAs(owner));
                Assert.That(owner.NamespaceUris.GetIndex(kBaseNamespaceUri), Is.EqualTo(1));
                Assert.That(owner.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(-1));
                Assert.That(((IServerInternal)competingCandidate!).NamespaceUris.GetIndex(kStagedNamespaceUri),
                    Is.EqualTo(1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kBaseNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(-1));
                Assert.That(calls, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Factory failures propagate without publishing staged namespace changes.
        /// </summary>
        [Test]
        public async Task CreateNodeManagerAsyncFactoryFailureDoesNotClaimOrPublishCandidate()
        {
            Mock<IServerInternal> server = CreateServer();
            var preparation = new ChangePreparer(server.Object);
            IAsyncNodeManager unclaimedManager = new Mock<IAsyncNodeManager>().Object;
            IServerInternal? suppliedServer = null;
            var expected = new InvalidOperationException("candidate factory failed");
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    suppliedServer = candidate;
                    candidate.NamespaceUris.GetIndexOrAppend(kCandidateNamespaceUri);
                    throw expected;
                });

            InvalidOperationException actual = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(suppliedServer, Is.Not.Null);
                Assert.That(((IServerInternal)suppliedServer!).NamespaceUris.GetIndex(kCandidateNamespaceUri),
                    Is.EqualTo(1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(ChangePreparer.GetServer(unclaimedManager), Is.Null);
                Assert.That(factory.Invocations, Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// Null factories report the add or replacement failure without changing serving metadata.
        /// </summary>
        [TestCase(false, "The NodeManager factory returned null.")]
        [TestCase(true, "The replacement NodeManager factory returned null.")]
        public async Task CreateNodeManagerAsyncNullFactoryResultReportsExactFailure(
            bool replacement,
            string expectedMessage)
        {
            Mock<IServerInternal> server = CreateServer();
            var preparation = new ChangePreparer(server.Object);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>((IAsyncNodeManager)null!));

            InvalidOperationException actual = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, replacement, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(actual.Message, Is.EqualTo(expectedMessage));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(factory.Invocations, Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// Preparation rejects encodeable factories that cannot isolate staged registrations.
        /// </summary>
        [Test]
        public void ConstructorRejectsFactoryThatCannotStageRegistrations()
        {
            Mock<IServerInternal> server = CreateServer();
            server.SetupGet(value => value.Factory).Returns(new Mock<IEncodeableFactory>().Object);

            NotSupportedException actual = Assert.Throws<NotSupportedException>(
                () => new ChangePreparer(server.Object))!;

            Assert.That(actual.Message, Is.EqualTo("The server factory does not support staged registrations."));
        }

        /// <summary>
        /// Detachment keeps candidate metadata isolated from later serving registry changes.
        /// </summary>
        [Test]
        public async Task DetachPreservesCandidateSnapshotAfterCanonicalMetadataChanges()
        {
            Mock<IServerInternal> server = CreateServer();
            server.Object.NamespaceUris.Append(kBaseNamespaceUri);
            server.Object.ServerUris.Append(kBaseServerUri);
            EncodeableFactory originalFactory = (EncodeableFactory)server.Object.Factory;
            Assert.That(originalFactory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? rangeType), Is.True);
            var preparation = new ChangePreparer(server.Object);
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    candidate.NamespaceUris.GetIndexOrAppend(kCandidateNamespaceUri);
                    candidate.ServerUris.GetIndexOrAppend(kCandidateServerUri);
                    candidate.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
                    candidate.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
                    candidate.Factory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer staging = ChangePreparer.GetServer(created)!;
            ChangePreparer.Detach(created);

            server.Object.NamespaceUris.Append(kSourceMutationNamespaceUri);
            server.Object.ServerUris.Append(kStagedServerUri);
            server.Object.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            server.Object.TypeTree.AddSubtype(new NodeId(99004), s_supertypeId);
            originalFactory.Builder.AddEncodeableType(s_postDetachRegistrationId, rangeType!).Commit();

            Assert.Multiple(() =>
            {
                Assert.That(staging.IsDetached, Is.True);
                Assert.That(staging.IsStaging, Is.True);
                Assert.That(staging.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(2));
                Assert.That(staging.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(-1));
                Assert.That(staging.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(1));
                Assert.That(staging.ServerUris.GetIndex(kStagedServerUri), Is.EqualTo(-1));
                Assert.That(staging.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(staging.TypeTree.FindSuperType(new NodeId(99004)), Is.EqualTo(NodeId.Null));
                Assert.That(
                    staging.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? stagedType),
                    Is.True);
                Assert.That(stagedType, Is.SameAs(rangeType));
                Assert.That(staging.Factory.TryGetEncodeableType(s_postDetachRegistrationId, out _), Is.False);
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(-1));
                Assert.That(server.Object.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(
                    server.Object.Factory.TryGetEncodeableType(s_postDetachRegistrationId, out IEncodeableType? sourceType),
                    Is.True);
                Assert.That(sourceType, Is.SameAs(rangeType));
                Assert.That(ChangePreparer.GetServer(created), Is.SameAs(staging));
            });
        }

        /// <summary>
        /// Retaining an aborted or retired staging server does not retain its released facade or resource.
        /// </summary>
        /// <param name="bound">Whether the candidate reached canonical binding before cleanup.</param>
        /// <param name="dispose">Whether cleanup disposes rather than detaches the staging server.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task RetainedStagingServerReleasesFacadeAndResourceAfterCleanupAsync(bool bound, bool dispose)
        {
            (ChangePreparer.StagingServer retainedServer,
                WeakReference<IAsyncNodeManager> facade,
                WeakReference<IAsyncNodeManager> resource) =
                await CreateRetainedServerAfterCleanupAsync(bound, dispose).ConfigureAwait(false);

            CollectReleasedManagers();

            Assert.Multiple(() =>
            {
                Assert.That(retainedServer.RegisteredFacade, Is.Null);
                Assert.That(facade.TryGetTarget(out _), Is.False);
                Assert.That(resource.TryGetTarget(out _), Is.False);
            });
            GC.KeepAlive(retainedServer);
        }

        /// <summary>
        /// Separates candidate construction and cleanup from the retained-server collection assertions.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            ChangePreparer.StagingServer Server,
            WeakReference<IAsyncNodeManager> Facade,
            WeakReference<IAsyncNodeManager> Resource)> CreateRetainedServerAfterCleanupAsync(bool bound, bool dispose)
        {
            var preparation = new ChangePreparer(CreateServer().Object);
            IAsyncNodeManager resource = new Mock<IAsyncNodeManager>().Object;
            var facade = new Mock<IAsyncNodeManager>();
            facade.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(facade.Object));
            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer staging = ChangePreparer.GetServer(created)!;
            Assert.That(staging.RegisteredFacade, Is.SameAs(created));
            if (bound)
            {
                ChangePreparer.Bind(created);
                Assert.That(staging.RegisteredFacade, Is.SameAs(created));
            }
            if (dispose)
            {
                await ChangePreparer.DisposeAsync(created).ConfigureAwait(false);
            }
            else
            {
                ChangePreparer.Detach(created);
            }
            return (
                staging,
                new WeakReference<IAsyncNodeManager>(created),
                new WeakReference<IAsyncNodeManager>(resource));
        }

        /// <summary>
        /// Collects candidate cycles without retaining their targets in the assertion frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectReleasedManagers()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Creates isolated serving metadata and context without starting a server.
        /// </summary>
        private static Mock<IServerInternal> CreateServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            var serverUris = new StringTable();
            var typeTree = new TypeTable(namespaces);
            EncodeableFactory factory = (EncodeableFactory)EncodeableFactory.Create();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(serverUris);
            server.SetupGet(value => value.TypeTree).Returns(typeTree);
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MessageContext).Returns(new ServiceMessageContext(telemetry, factory)
            {
                NamespaceUris = namespaces,
                ServerUris = serverUris
            });
            server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(server.Object));
            return server;
        }

        /// <summary>
        /// A serving resource that records disposal attempts.
        /// </summary>
        private sealed class ServingNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates a resource owned by the serving server.
            /// </summary>
            public ServingNodeManager(IServerInternal server)
                : base(server, NullLogger.Instance, kBaseNamespaceUri)
            {
            }

            /// <summary>
            /// Gets the number of disposal attempts.
            /// </summary>
            public int DisposeCount { get; private set; }

            /// <summary>
            /// Indexes a target for staged reference ownership.
            /// </summary>
            internal void Index(NodeState target)
            {
                PredefinedNodes[target.NodeId] = target;
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>The namespace already present in serving metadata.</summary>
        private const string kBaseNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:Base";

        /// <summary>The namespace added through a staging facade.</summary>
        private const string kStagedNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:Staged";

        /// <summary>The namespace added to serving metadata after snapshot capture.</summary>
        private const string kSourceMutationNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:SourceMutation";

        /// <summary>The namespace added by a candidate factory.</summary>
        private const string kCandidateNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:Candidate";

        /// <summary>The server URI already present in serving metadata.</summary>
        private const string kBaseServerUri = "urn:opcfoundation.org:Tests:ChangePreparer:BaseServer";

        /// <summary>The server URI added through a staging facade.</summary>
        private const string kStagedServerUri = "urn:opcfoundation.org:Tests:ChangePreparer:StagedServer";

        /// <summary>The server URI added by a candidate factory.</summary>
        private const string kCandidateServerUri = "urn:opcfoundation.org:Tests:ChangePreparer:CandidateServer";

        /// <summary>The type whose parent differs between serving and staged metadata.</summary>
        private static readonly NodeId s_subtypeId = new(99001);

        /// <summary>The parent type registered by the tests.</summary>
        private static readonly NodeId s_supertypeId = new(99002);

        /// <summary>The standard namespace present in a new namespace table.</summary>
        private static readonly string[] s_expectedDefaultNamespaces = ["http://opcfoundation.org/UA/"];

        /// <summary>The encodeable registration captured or added during preparation.</summary>
        private static readonly ExpandedNodeId s_registrationId =
            new(99003, "urn:opcfoundation.org:Tests:ChangePreparer:Encodeables");

        /// <summary>The serving registration added after candidate detachment.</summary>
        private static readonly ExpandedNodeId s_postDetachRegistrationId =
            new(99004, "urn:opcfoundation.org:Tests:ChangePreparer:Encodeables");
    }
}
