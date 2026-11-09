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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Distinct equal managers retain separate lifecycle registrations and routing ownership.
        /// </summary>
        [Test]
        public async Task BatchLifecyclePreservesEqualManagerIdentityAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var firstFactory = new EqualLifecycleFactory(kModelNamespaceUri + ":equal-first", ObjectIds.Server);
            var peerFactory = new EqualLifecycleFactory(kModelNamespaceUri + ":equal-peer", ObjectIds.RootFolder);
            var owned = new List<NodeManagerRegistration>();
            try
            {
                NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(
                    objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, firstFactory.Target), Is.False);
                Assert.That(
                    objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, peerFactory.Target), Is.False);
                await using (IPreparedNodeManagerBatch batch = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(firstFactory), NodeManagerBatchChange.Add(peerFactory)],
                    timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await batch.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    foreach (NodeManagerRegistration registration in result.Registrations)
                    {
                        owned.Add(registration);
                    }
                    Assert.That(result.CleanupFailure, Is.Null);
                }

                Assert.That(owned, Has.Count.EqualTo(2));
                EqualLifecycleManager first = RequireLifecycleValue(firstFactory.Created);
                EqualLifecycleManager peer = RequireLifecycleValue(peerFactory.Created);
                Assert.That(owned[0].NodeManager, Is.SameAs(first));
                Assert.That(owned[1].NodeManager, Is.SameAs(peer));
                var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
                NodeManagerRoutingTable.RoutingSnapshot published = host.RoutingRevision;
                Assert.That(published.NodeManagers.Any(manager => ReferenceEquals(manager, first)), Is.True);
                Assert.That(published.NodeManagers.Any(manager => ReferenceEquals(manager, peer)), Is.True);
                Assert.That(
                    objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, firstFactory.Target), Is.True);
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, peerFactory.Target), Is.True);
                NodeManagerGeneration peerGeneration = published.GetGeneration(peer);

                await using (IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(owned[0])], timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await removal.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    owned.RemoveAt(0);
                    Assert.That(result.CleanupFailure, Is.Null);
                    await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                }

                NodeManagerRoutingTable.RoutingSnapshot remaining = host.RoutingRevision;
                Assert.That(remaining.NodeManagers.Any(manager => ReferenceEquals(manager, first)), Is.False);
                Assert.That(remaining.NodeManagers.Any(manager => ReferenceEquals(manager, peer)), Is.True);
                Assert.That(peerGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(
                    objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, firstFactory.Target), Is.False);
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, peerFactory.Target), Is.True);
                Assert.That(lifecycle.Registrations.ToList().Any(registration =>
                    ReferenceEquals(registration.NodeManager, peer)), Is.True);
            }
            finally
            {
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Prepared external references distinguish equal source node instances.
        /// </summary>
        [Test]
        public async Task PreparedExternalReferencesPreserveEqualSourceNodesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var factory = new EqualNodeOwnerFactory(kModelNamespaceUri + ":equal-node-owner");
            var owned = new List<NodeManagerRegistration>();
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, factory, timeout.Token).ConfigureAwait(false));
                EqualNodeOwnerManager owner = RequireLifecycleValue(factory.Created);
                ArrayOf<LocalReference> references =
                [
                    new(owner.First.NodeId, ReferenceTypeIds.HasComponent, false, ObjectIds.Server),
                    new(owner.Second.NodeId, ReferenceTypeIds.HasComponent, false, ObjectIds.Server)
                ];
                owned.Add(await AddIdentityManagerAsync(
                    lifecycle,
                    CreateReferenceContributorFactory(kModelNamespaceUri + ":equal-node-contributor", references),
                    timeout.Token).ConfigureAwait(false));

                Assert.That(owner.First, Is.Not.SameAs(owner.Second));
                Assert.That(owner.First, Is.EqualTo(owner.Second));
                Assert.That(owner.First.ReferenceExists(ReferenceTypeIds.HasComponent, false, ObjectIds.Server),
                    Is.True);
                Assert.That(owner.Second.ReferenceExists(ReferenceTypeIds.HasComponent, false, ObjectIds.Server),
                    Is.True);

                await RemoveLastIdentityRegistrationAsync(lifecycle, owned, timeout.Token).ConfigureAwait(false);
                Assert.That(owner.First.ReferenceExists(ReferenceTypeIds.HasComponent, false, ObjectIds.Server),
                    Is.False);
                Assert.That(owner.Second.ReferenceExists(ReferenceTypeIds.HasComponent, false, ObjectIds.Server),
                    Is.False);
            }
            finally
            {
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Keeps live contributions separate when equal managers own copies of the same source node.
        /// </summary>
        [Test]
        public async Task LiveExternalReferencesPreserveEqualManagerAndSourceIdentityAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":equal-shared-source";
            var firstFactory = new EqualSharedSourceFactory(kModelNamespaceUri + ":equal-live-first", sharedNamespace);
            var peerFactory = new EqualSharedSourceFactory(kModelNamespaceUri + ":equal-live-peer", sharedNamespace);
            var joiningFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":equal-live-joining", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, firstFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, peerFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager first = RequireLifecycleValue(firstFactory.Created);
                EqualSharedSourceManager peer = RequireLifecycleValue(peerFactory.Created);
                Assert.That(first, Is.Not.SameAs(peer));
                Assert.That(first, Is.EqualTo(peer));
                Assert.That(first.Source, Is.Not.SameAs(peer.Source));
                Assert.That(first.Source.NodeId, Is.EqualTo(peer.Source.NodeId));

                NodeState firstNode = await first.AddProbeAsync("first", ct).ConfigureAwait(false);
                NodeState peerNode = await peer.AddProbeAsync("peer", ct).ConfigureAwait(false);
                ChangePreparer.StagingServer firstStaging = ChangePreparer.GetServer(first)
                    ?? throw new InvalidOperationException("The first manager has no staging server.");
                var firstCleanup = new List<LocalReference>
                {
                    new(first.Source.NodeId, ReferenceTypeIds.Organizes, false, firstNode.NodeId, firstNode)
                };
                firstStaging.ExcludePreparedReferences(firstCleanup);
                Assert.That(firstCleanup, Is.Empty);
                AssertSharedSourceReference(first.Source, firstNode.NodeId, expected: true);
                AssertSharedSourceReference(first.Source, peerNode.NodeId, expected: true);
                AssertSharedSourceReference(peer.Source, firstNode.NodeId, expected: true);
                AssertSharedSourceReference(peer.Source, peerNode.NodeId, expected: true);

                Assert.That(await first.DeleteNodeAsync(first.SystemContext, firstNode.NodeId, ct)
                    .ConfigureAwait(false), Is.True);
                AssertSharedSourceReference(first.Source, firstNode.NodeId, expected: false);
                AssertSharedSourceReference(peer.Source, firstNode.NodeId, expected: false);
                AssertSharedSourceReference(first.Source, peerNode.NodeId, expected: true);
                AssertSharedSourceReference(peer.Source, peerNode.NodeId, expected: true);

                NodeState replacement = await first.AddProbeAsync("first", ct).ConfigureAwait(false);
                Assert.That(replacement, Is.Not.SameAs(firstNode));
                Assert.That(replacement.NodeId, Is.EqualTo(firstNode.NodeId));
                var replacementRemoval = new LocalReference(
                    first.Source.NodeId, ReferenceTypeIds.Organizes, false, replacement.NodeId, replacement);
                firstCleanup.Add(replacementRemoval);
                firstStaging.ExcludePreparedReferences(firstCleanup);
                Assert.That(firstCleanup, Is.Empty);
                AssertSharedSourceReference(first.Source, replacement.NodeId, expected: true);
                AssertSharedSourceReference(peer.Source, replacement.NodeId, expected: true);

                owned.Add(await AddIdentityManagerAsync(lifecycle, joiningFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager joining = RequireLifecycleValue(joiningFactory.Created);
                Assert.That(joining, Is.Not.SameAs(first).And.Not.SameAs(peer));
                Assert.That(joining, Is.EqualTo(first));
                AssertSharedSourceReference(joining.Source, replacement.NodeId, expected: true);
                AssertSharedSourceReference(joining.Source, peerNode.NodeId, expected: true);

                await using (IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(owned[1])], ct).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await removal.CommitAsync(_ => default, ct).ConfigureAwait(false);
                    owned.RemoveAt(1);
                    Assert.That(result.CleanupFailure, Is.Null);
                    await result.Retirement.WaitAsync(ct).ConfigureAwait(false);
                }
                AssertSharedSourceReference(first.Source, replacement.NodeId, expected: true);
                AssertSharedSourceReference(joining.Source, replacement.NodeId, expected: true);
                AssertSharedSourceReference(first.Source, peerNode.NodeId, expected: false);
                AssertSharedSourceReference(joining.Source, peerNode.NodeId, expected: false);

                await using (IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(owned[0])], ct).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await removal.CommitAsync(_ => default, ct).ConfigureAwait(false);
                    owned.RemoveAt(0);
                    Assert.That(result.CleanupFailure, Is.Null);
                    await result.Retirement.WaitAsync(ct).ConfigureAwait(false);
                }
                AssertSharedSourceReference(joining.Source, replacement.NodeId, expected: false);
                AssertSharedSourceReference(joining.Source, peerNode.NodeId, expected: false);
                firstCleanup.Add(replacementRemoval);
                firstStaging.ExcludePreparedReferences(firstCleanup);
                Assert.That(firstCleanup, Has.Count.EqualTo(1));
            }
            finally
            {
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rolls back a live addition when a source owner fails after changing its reference storage.
        /// </summary>
        /// <param name="cancel">Whether the owner cancels the addition instead of throwing a callback error.</param>
        /// <param name="preexisting">Whether the first source already has the requested reference.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task LiveExternalReferenceFailureRollsBackEverySourceAsync(bool cancel, bool preexisting)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":equal-failed-source";
            var firstFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":equal-failed-first", sharedNamespace);
            var peerFactory = new EqualSharedSourceFactory(kModelNamespaceUri + ":equal-failed-peer", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? peer = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, firstFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, peerFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager first = RequireLifecycleValue(firstFactory.Created);
                EqualSharedSourceManager currentPeer = RequireLifecycleValue(peerFactory.Created);
                peer = currentPeer;
                NodeState retained = await currentPeer.AddProbeAsync("retained", ct).ConfigureAwait(false);
                var target = new BaseObjectState(null)
                {
                    NodeId = new NodeId("failed", first.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("failed", first.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("failed"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await first.AddPredefinedNodeAsync(target, ct).ConfigureAwait(false);
                if (preexisting)
                {
                    Assert.That(first.Source.AddReferenceIfMissing(
                        ReferenceTypeIds.Organizes, false, target.NodeId), Is.True);
                }
                int callbacks = 0;
                currentPeer.AfterReferenceAddAsync = (references, token) =>
                {
                    if (references.TryGetValue(currentPeer.Source.NodeId, out IList<IReference>? added) &&
                        added.Any(reference => reference.TargetId == target.NodeId))
                    {
                        callbacks++;
                        if (cancel)
                        {
                            operation.Cancel();
                            token.ThrowIfCancellationRequested();
                        }
                        throw new SentinelException("The source failed after adding the reference.");
                    }
                    return default;
                };

                if (cancel)
                {
                    await Assert.ThatAsync(
                        async () => await first.AddReferenceFromNodeAsync(
                            target, first.Source.NodeId, ReferenceTypeIds.Organizes, false, operation.Token)
                            .ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThrowsAsync<SentinelException>(async () => await first.AddReferenceFromNodeAsync(
                        target, first.Source.NodeId, ReferenceTypeIds.Organizes, false, operation.Token)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                }

                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(first.Find(target.NodeId), Is.SameAs(target));
                Assert.That(target.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, first.Source.NodeId), Is.False);
                AssertSharedSourceReference(first.Source, target.NodeId, expected: preexisting);
                AssertSharedSourceReference(currentPeer.Source, target.NodeId, expected: false);
                AssertSharedSourceReference(first.Source, retained.NodeId, expected: true);
                AssertSharedSourceReference(currentPeer.Source, retained.NodeId, expected: true);
                ChangePreparer.StagingServer firstStaging = ChangePreparer.GetServer(first)
                    ?? throw new InvalidOperationException("The first manager has no staging server.");
                var cleanup = new List<LocalReference>
                {
                    new(first.Source.NodeId, ReferenceTypeIds.Organizes, false, target.NodeId, target)
                };
                firstStaging.ExcludePreparedReferences(cleanup);
                Assert.That(cleanup, Has.Count.EqualTo(1));
            }
            finally
            {
                if (peer is not null)
                {
                    peer.AfterReferenceAddAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Allows a rollback callback to add a different live reference without waiting on its own admission.
        /// </summary>
        [Test]
        public async Task LiveReferenceRollbackAllowsReentrantAdditionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":reentrant-rollback-source";
            var firstFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":reentrant-rollback-first", sharedNamespace);
            var peerFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":reentrant-rollback-peer", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? first = null;
            EqualSharedSourceManager? peer = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, firstFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, peerFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentFirst = RequireLifecycleValue(firstFactory.Created);
                EqualSharedSourceManager currentPeer = RequireLifecycleValue(peerFactory.Created);
                first = currentFirst;
                peer = currentPeer;
                var failed = new BaseObjectState(null)
                {
                    NodeId = new NodeId("failed", currentPeer.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("failed", currentPeer.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("failed"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                var nested = new BaseObjectState(null)
                {
                    NodeId = new NodeId("nested", currentPeer.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("nested", currentPeer.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("nested"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await currentPeer.AddPredefinedNodeAsync(failed, ct).ConfigureAwait(false);
                await currentPeer.AddPredefinedNodeAsync(nested, ct).ConfigureAwait(false);
                var expected = new SentinelException("The source failed after adding the reference.");
                currentPeer.AfterReferenceAddAsync = (references, _) =>
                {
                    if (references.TryGetValue(currentPeer.Source.NodeId, out IList<IReference>? added) &&
                        added.Any(reference => reference.TargetId == failed.NodeId))
                    {
                        throw expected;
                    }
                    return default;
                };
                int callbacks = 0;
                currentFirst.BeforeReferenceDeleteAsync = async _ =>
                {
                    currentFirst.BeforeReferenceDeleteAsync = null;
                    callbacks++;
                    using var reentry = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    reentry.CancelAfter(TimeSpan.FromSeconds(5));
                    await currentPeer.AddReferenceFromNodeAsync(
                        nested, currentFirst.Source.NodeId, ReferenceTypeIds.Organizes, false, reentry.Token)
                        .ConfigureAwait(false);
                };

                SentinelException? failure = await Assert.ThrowsAsync<SentinelException>(async () =>
                    await currentPeer.AddReferenceFromNodeAsync(
                        failed, currentFirst.Source.NodeId, ReferenceTypeIds.Organizes, false, ct)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                currentPeer.AfterReferenceAddAsync = null;
                currentFirst.BeforeReferenceDeleteAsync = null;

                Assert.That(failure, Is.SameAs(expected));
                Assert.That(callbacks, Is.EqualTo(1));
                AssertSharedSourceReference(currentFirst.Source, failed.NodeId, expected: false);
                AssertSharedSourceReference(currentPeer.Source, failed.NodeId, expected: false);
                Assert.That(failed.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentFirst.Source.NodeId), Is.False);
                AssertSharedSourceReference(currentFirst.Source, nested.NodeId, expected: true);
                AssertSharedSourceReference(currentPeer.Source, nested.NodeId, expected: true);
                Assert.That(nested.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentFirst.Source.NodeId), Is.True);
            }
            finally
            {
                if (first is not null)
                {
                    first.BeforeReferenceDeleteAsync = null;
                }
                if (peer is not null)
                {
                    peer.AfterReferenceAddAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gives each source callback its own collection of reference additions.
        /// </summary>
        [Test]
        public async Task LiveReferenceCallbacksDoNotShareMutableAdditionsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":independent-reference-input";
            var firstFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":independent-reference-first", sharedNamespace);
            var peerFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":independent-reference-peer", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? first = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, firstFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, peerFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentFirst = RequireLifecycleValue(firstFactory.Created);
                EqualSharedSourceManager peer = RequireLifecycleValue(peerFactory.Created);
                first = currentFirst;
                int callbacks = 0;
                currentFirst.AfterReferenceAddAsync = (references, _) =>
                {
                    if (references.ContainsKey(currentFirst.Source.NodeId))
                    {
                        callbacks++;
                        references.Clear();
                    }
                    return default;
                };
                NodeState target = await currentFirst.AddProbeAsync("target", ct).ConfigureAwait(false);
                currentFirst.AfterReferenceAddAsync = null;

                Assert.That(callbacks, Is.EqualTo(1));
                AssertSharedSourceReference(currentFirst.Source, target.NodeId, expected: true);
                AssertSharedSourceReference(peer.Source, target.NodeId, expected: true);
                Assert.That(target.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentFirst.Source.NodeId), Is.True);
            }
            finally
            {
                if (first is not null)
                {
                    first.AfterReferenceAddAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports a source replacement that occurs while its addition callback runs.
        /// </summary>
        [Test]
        public async Task LiveReferenceAdditionRejectsSourceReplacementDuringCallbackAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":replaced-reference-source";
            var firstFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":replaced-reference-first", sharedNamespace);
            var peerFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":replaced-reference-peer", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? first = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, firstFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, peerFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentFirst = RequireLifecycleValue(firstFactory.Created);
                EqualSharedSourceManager peer = RequireLifecycleValue(peerFactory.Created);
                first = currentFirst;
                var target = new BaseObjectState(null)
                {
                    NodeId = new NodeId("target", peer.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("target", peer.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("target"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await peer.AddPredefinedNodeAsync(target, ct).ConfigureAwait(false);
                var replacement = new FolderState(null)
                {
                    NodeId = currentFirst.Source.NodeId,
                    BrowseName = currentFirst.Source.BrowseName,
                    DisplayName = currentFirst.Source.DisplayName,
                    TypeDefinitionId = ObjectTypeIds.FolderType
                };
                currentFirst.AfterReferenceAddAsync = (references, _) =>
                {
                    if (references.ContainsKey(currentFirst.Source.NodeId))
                    {
                        currentFirst.ReplaceIndexedSource(replacement);
                    }
                    return default;
                };
                InvalidOperationException? rejected = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await peer.AddReferenceFromNodeAsync(
                        target, currentFirst.Source.NodeId, ReferenceTypeIds.Organizes, false, ct)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                currentFirst.AfterReferenceAddAsync = null;

                Assert.That(rejected!.Message, Does.Contain("source was removed or replaced"));
                Assert.That(currentFirst.Find(replacement.NodeId), Is.SameAs(replacement));
                AssertSharedSourceReference(replacement, target.NodeId, expected: false);
                AssertSharedSourceReference(peer.Source, target.NodeId, expected: false);
                Assert.That(target.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentFirst.Source.NodeId), Is.False);
            }
            finally
            {
                if (first is not null)
                {
                    first.AfterReferenceAddAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports a source callback that returns without retaining the requested reference.
        /// </summary>
        /// <param name="preexisting">Whether the source already has the requested reference.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LiveReferenceAdditionRejectsSourceThatDoesNotRetainReferenceAsync(bool preexisting)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":rejected-reference-source";
            var firstFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":rejected-reference-first", sharedNamespace);
            var peerFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":rejected-reference-peer", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? first = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, firstFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, peerFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentFirst = RequireLifecycleValue(firstFactory.Created);
                EqualSharedSourceManager peer = RequireLifecycleValue(peerFactory.Created);
                first = currentFirst;
                var target = new BaseObjectState(null)
                {
                    NodeId = new NodeId("target", peer.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("target", peer.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("target"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await peer.AddPredefinedNodeAsync(target, ct).ConfigureAwait(false);
                if (preexisting)
                {
                    Assert.That(currentFirst.Source.AddReferenceIfMissing(
                        ReferenceTypeIds.Organizes, false, target.NodeId), Is.True);
                }
                currentFirst.AfterReferenceAddAsync = (references, _) =>
                {
                    if (references.ContainsKey(currentFirst.Source.NodeId))
                    {
                        currentFirst.AfterReferenceAddAsync = null;
                        currentFirst.Source.RemoveReference(ReferenceTypeIds.Organizes, false, target.NodeId);
                    }
                    return default;
                };
                InvalidOperationException? rejected = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await peer.AddReferenceFromNodeAsync(
                        target, currentFirst.Source.NodeId, ReferenceTypeIds.Organizes, false, ct)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                currentFirst.AfterReferenceAddAsync = null;

                Assert.That(rejected!.Message, Does.Contain("did not retain"));
                AssertSharedSourceReference(currentFirst.Source, target.NodeId, expected: preexisting);
                AssertSharedSourceReference(peer.Source, target.NodeId, expected: false);
                Assert.That(target.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentFirst.Source.NodeId), Is.False);
            }
            finally
            {
                if (first is not null)
                {
                    first.AfterReferenceAddAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Retains first-owner behavior when a source exposes an opaque handle and its callback fails.
        /// </summary>
        [Test]
        public async Task LiveReferenceFailurePreservesOpaqueSourceBehaviorAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":opaque-reference-source";
            var firstFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":opaque-reference-first", sharedNamespace);
            var peerFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":opaque-reference-peer", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? first = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, firstFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, peerFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentFirst = RequireLifecycleValue(firstFactory.Created);
                EqualSharedSourceManager peer = RequireLifecycleValue(peerFactory.Created);
                first = currentFirst;
                currentFirst.UseOpaqueSourceHandle = true;
                var target = new BaseObjectState(null)
                {
                    NodeId = new NodeId("target", peer.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("target", peer.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("target"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await peer.AddPredefinedNodeAsync(target, ct).ConfigureAwait(false);
                var expected = new SentinelException("The opaque source failed after adding the reference.");
                currentFirst.AfterReferenceAddAsync = (references, _) =>
                {
                    if (references.TryGetValue(currentFirst.Source.NodeId, out IList<IReference>? added) &&
                        added.Any(reference => reference.TargetId == target.NodeId))
                    {
                        throw expected;
                    }
                    return default;
                };
                SentinelException? failure = await Assert.ThrowsAsync<SentinelException>(
                    async () => await peer.AddReferenceFromNodeAsync(
                        target, currentFirst.Source.NodeId, ReferenceTypeIds.Organizes, false, ct)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                currentFirst.AfterReferenceAddAsync = null;

                Assert.That(failure, Is.SameAs(expected));
                AssertSharedSourceReference(currentFirst.Source, target.NodeId, expected: true);
                AssertSharedSourceReference(peer.Source, target.NodeId, expected: true);
                Assert.That(target.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentFirst.Source.NodeId), Is.True);
                ChangePreparer.StagingServer staging = ChangePreparer.GetServer(peer)
                    ?? throw new InvalidOperationException("The target owner has no staging server.");
                var cleanup = new List<LocalReference>
                {
                    new(currentFirst.Source.NodeId, ReferenceTypeIds.Organizes, false, target.NodeId, target)
                };
                staging.ExcludePreparedReferences(cleanup);
                Assert.That(cleanup, Has.Count.EqualTo(1));
            }
            finally
            {
                if (first is not null)
                {
                    first.AfterReferenceAddAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rejects reentrant addition instead of waiting for a decision that is awaiting the deletion callback.
        /// </summary>
        [Test]
        public async Task LiveReferenceAdditionFromUnadmittedDeletionCallbackDoesNotWaitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":reentrant-deletion-source";
            var sourceFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":reentrant-deletion-owner", sharedNamespace);
            var targetFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":reentrant-deletion-target", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? source = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, sourceFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, targetFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentSource = RequireLifecycleValue(sourceFactory.Created);
                EqualSharedSourceManager targetOwner = RequireLifecycleValue(targetFactory.Created);
                source = currentSource;
                NodeState removed = await targetOwner.AddProbeAsync("removed", ct).ConfigureAwait(false);
                var addition = new BaseObjectState(null)
                {
                    NodeId = new NodeId("addition", targetOwner.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("addition", targetOwner.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("addition"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await targetOwner.AddPredefinedNodeAsync(addition, ct).ConfigureAwait(false);
                int callbacks = 0;
                currentSource.BeforeReferenceDeleteAsync = async token =>
                {
                    currentSource.BeforeReferenceDeleteAsync = null;
                    Interlocked.Increment(ref callbacks);
                    using var reentry = CancellationTokenSource.CreateLinkedTokenSource(token);
                    reentry.CancelAfter(TimeSpan.FromSeconds(5));
                    InvalidOperationException? rejected = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await targetOwner.AddReferenceFromNodeAsync(
                            addition,
                            currentSource.Source.NodeId,
                            ReferenceTypeIds.Organizes,
                            false,
                            reentry.Token).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(rejected!.Message, Does.Contain("deletion callback"));
                };
                var decidingFactory = new EqualLifecycleFactory(
                    kModelNamespaceUri + ":reentrant-deletion-decision", ObjectIds.RootFolder);
                await using (IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await deciding.CommitAsync(async token =>
                    {
                        Task<bool> deletion;
                        using (ExecutionContext.SuppressFlow())
                        {
                            deletion = Task.Run(async () =>
                                await targetOwner.DeleteNodeAsync(targetOwner.SystemContext, removed.NodeId, token)
                                    .ConfigureAwait(false), token);
                        }
                        Assert.That(await deletion.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false),
                            Is.True);
                    }, ct).ConfigureAwait(false);
                    owned.Add(result.Registrations[0]);
                    Assert.That(result.CleanupFailure, Is.Null);
                }

                currentSource.BeforeReferenceDeleteAsync = null;
                Assert.That(callbacks, Is.EqualTo(1));
                AssertSharedSourceReference(currentSource.Source, addition.NodeId, expected: false);
                AssertSharedSourceReference(targetOwner.Source, addition.NodeId, expected: false);
                Assert.That(addition.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentSource.Source.NodeId), Is.False);
                ChangePreparer.StagingServer staging = ChangePreparer.GetServer(targetOwner)
                    ?? throw new InvalidOperationException("The target owner has no staging server.");
                var cleanup = new List<LocalReference>
                {
                    new(currentSource.Source.NodeId, ReferenceTypeIds.Organizes, false, addition.NodeId, addition)
                };
                staging.ExcludePreparedReferences(cleanup);
                Assert.That(cleanup, Has.Count.EqualTo(1));
            }
            finally
            {
                if (source is not null)
                {
                    source.BeforeReferenceDeleteAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lets a source callback publish its retirement while retaining the source until the callback returns.
        /// </summary>
        [Test]
        public async Task LiveReferenceSourceRetirementCompletesAfterItsCallbackAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":retiring-reference-source";
            var sourceFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":retiring-reference-owner", sharedNamespace);
            var targetFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":retiring-reference-target", sharedNamespace);
            var joiningFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":retiring-reference-joining", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? source = null;
            NodeManagerBatchResult? retired = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, sourceFactory, ct).ConfigureAwait(false));
                owned.Add(await AddIdentityManagerAsync(lifecycle, targetFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentSource = RequireLifecycleValue(sourceFactory.Created);
                EqualSharedSourceManager targetOwner = RequireLifecycleValue(targetFactory.Created);
                source = currentSource;
                var target = new BaseObjectState(null)
                {
                    NodeId = new NodeId("target", targetOwner.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("target", targetOwner.NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("target"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await targetOwner.AddPredefinedNodeAsync(target, ct).ConfigureAwait(false);
                currentSource.AfterReferenceAddAsync = async (references, token) =>
                {
                    if (references.TryGetValue(currentSource.Source.NodeId, out IList<IReference>? added) &&
                        added.Any(reference => reference.TargetId == target.NodeId))
                    {
                        await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                            [NodeManagerBatchChange.Remove(owned[0])], token).ConfigureAwait(false);
                        NodeManagerBatchResult result = await removal.CommitAsync(_ => default, token)
                            .ConfigureAwait(false);
                        retired = result;
                        owned.RemoveAt(0);
                        Assert.That(result.CleanupFailure, Is.Null);
                        Assert.That(result.Retirement.IsCompleted, Is.False);
                    }
                };

                await targetOwner.AddReferenceFromNodeAsync(
                    target, currentSource.Source.NodeId, ReferenceTypeIds.Organizes, false, ct)
                    .ConfigureAwait(false);
                currentSource.AfterReferenceAddAsync = null;
                NodeManagerBatchResult removalResult = retired
                    ?? throw new InvalidOperationException("The source retirement was not published.");
                await removalResult.Retirement.WaitAsync(ct).ConfigureAwait(false);
                Assert.That(target.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, currentSource.Source.NodeId), Is.True);
                AssertSharedSourceReference(targetOwner.Source, target.NodeId, expected: true);

                owned.Add(await AddIdentityManagerAsync(lifecycle, joiningFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager joining = RequireLifecycleValue(joiningFactory.Created);
                AssertSharedSourceReference(joining.Source, target.NodeId, expected: true);
                Assert.That(await targetOwner.DeleteNodeAsync(targetOwner.SystemContext, target.NodeId, ct)
                    .ConfigureAwait(false), Is.True);
                AssertSharedSourceReference(targetOwner.Source, target.NodeId, expected: false);
                AssertSharedSourceReference(joining.Source, target.NodeId, expected: false);
            }
            finally
            {
                if (source is not null)
                {
                    source.AfterReferenceAddAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Applies deferred pruning and retains captured metadata when the removal fails.
        /// </summary>
        /// <param name="failRemoval">Whether the source rejects the removal.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DeferredPrunePreservesCapturedReferenceUntilOutcomeAsync(bool failRemoval)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":failed-prune-source";
            var sourceFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":failed-prune-owner", sharedNamespace);
            var targetFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":failed-prune-target", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            EqualSharedSourceManager? source = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, sourceFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentSource = RequireLifecycleValue(sourceFactory.Created);
                source = currentSource;
                EqualSharedSourceManager targetOwner;
                NodeState target;
                await using (IPreparedNodeManagerBatch candidate = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(targetFactory)], ct).ConfigureAwait(false))
                {
                    targetOwner = RequireLifecycleValue(targetFactory.Created);
                    target = await targetOwner.AddProbeAsync("captured-target", ct).ConfigureAwait(false);
                    NodeManagerBatchResult result = await candidate.CommitAsync(_ => default, ct).ConfigureAwait(false);
                    owned.Add(result.Registrations[0]);
                    Assert.That(result.CleanupFailure, Is.Null);
                }

                var reference = new LocalReference(
                    currentSource.Source.NodeId, ReferenceTypeIds.Organizes, false, target.NodeId, target);
                ChangePreparer.StagingServer staging = ChangePreparer.GetServer(targetOwner)
                    ?? throw new InvalidOperationException("The target owner has no staging server.");
                var cleanup = new List<LocalReference> { reference };
                staging.ExcludePreparedReferences(cleanup);
                Assert.That(cleanup, Is.Empty);
                AssertSharedSourceReference(currentSource.Source, target.NodeId, expected: true);

                var expected = new SentinelException("The source rejected the removal.");
                int callbacks = 0;
                currentSource.BeforeReferenceDeleteAsync = _ =>
                {
                    callbacks++;
                    if (failRemoval)
                    {
                        throw expected;
                    }
                    return default;
                };
                var decidingFactory = new EqualLifecycleFactory(
                    kModelNamespaceUri + ":failed-prune-decision", ObjectIds.RootFolder);
                await using (IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await deciding.CommitAsync(async token =>
                    {
                        Task removal;
                        using (ExecutionContext.SuppressFlow())
                        {
                            removal = Task.Run(async () =>
                                await ((MasterNodeManager)m_server.CurrentInstance.NodeManager)
                                    .RemoveManagedNodeReferencesAsync(targetOwner, [reference], token)
                                    .ConfigureAwait(false), token);
                        }
                        if (failRemoval)
                        {
                            SentinelException? failure = await Assert.ThrowsAsync<SentinelException>(async () =>
                                await removal.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false))
                                .ConfigureAwait(false);
                            Assert.That(failure, Is.SameAs(expected));
                        }
                        else
                        {
                            await removal.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                        }
                        Assert.That(callbacks, Is.EqualTo(1));
                        cleanup.Add(reference);
                        staging.ExcludePreparedReferences(cleanup);
                        Assert.That(cleanup, Is.Empty);
                        AssertSharedSourceReference(currentSource.Source, target.NodeId, expected: failRemoval);
                    }, ct).ConfigureAwait(false);
                    owned.Add(result.Registrations[0]);
                    Assert.That(result.CleanupFailure, Is.Null);
                }

                currentSource.BeforeReferenceDeleteAsync = null;
                cleanup.Add(reference);
                staging.ExcludePreparedReferences(cleanup);
                if (failRemoval)
                {
                    Assert.That(cleanup, Is.Empty);
                }
                else
                {
                    Assert.That(cleanup, Has.Count.EqualTo(1));
                }
                await RemoveLastIdentityRegistrationAsync(lifecycle, owned, ct).ConfigureAwait(false);
                await RemoveLastIdentityRegistrationAsync(lifecycle, owned, ct).ConfigureAwait(false);
                AssertSharedSourceReference(currentSource.Source, target.NodeId, expected: false);
            }
            finally
            {
                if (source is not null)
                {
                    source.BeforeReferenceDeleteAsync = null;
                }
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Retries a removal that fails after publication has already applied its deferred prune.
        /// </summary>
        [Test]
        public async Task DeferredPruneBeforeRemovalFailureQueuesRetryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            string sharedNamespace = kModelNamespaceUri + ":applied-prune-source";
            var sourceFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":applied-prune-owner", sharedNamespace);
            var targetFactory = new EqualSharedSourceFactory(
                kModelNamespaceUri + ":applied-prune-target", sharedNamespace);
            var owned = new List<NodeManagerRegistration>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var removed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EqualSharedSourceManager? source = null;
            Task<bool>? deletion = null;
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, sourceFactory, ct).ConfigureAwait(false));
                EqualSharedSourceManager currentSource = RequireLifecycleValue(sourceFactory.Created);
                source = currentSource;
                EqualSharedSourceManager targetOwner;
                NodeState target;
                await using (IPreparedNodeManagerBatch candidate = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(targetFactory)], ct).ConfigureAwait(false))
                {
                    targetOwner = RequireLifecycleValue(targetFactory.Created);
                    target = await targetOwner.AddProbeAsync("deleted-target", ct).ConfigureAwait(false);
                    NodeManagerBatchResult result = await candidate.CommitAsync(_ => default, ct).ConfigureAwait(false);
                    owned.Add(result.Registrations[0]);
                    Assert.That(result.CleanupFailure, Is.Null);
                }

                var reference = new LocalReference(
                    currentSource.Source.NodeId, ReferenceTypeIds.Organizes, false, target.NodeId, target);
                ChangePreparer.StagingServer staging = ChangePreparer.GetServer(targetOwner)
                    ?? throw new InvalidOperationException("The target owner has no staging server.");
                var expected = new SentinelException("The source failed after the prune was applied.");
                currentSource.BeforeReferenceDeleteAsync = async token =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    throw expected;
                };
                currentSource.AfterReferenceDelete = targetId =>
                {
                    if (targetId == target.NodeId)
                    {
                        removed.TrySetResult(true);
                    }
                };

                var decidingFactory = new EqualLifecycleFactory(
                    kModelNamespaceUri + ":applied-prune-decision", ObjectIds.RootFolder);
                await using (IPreparedNodeManagerBatch deciding = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(decidingFactory)], ct).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await deciding.CommitAsync(async token =>
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            deletion = Task.Run(async () =>
                                await targetOwner.DeleteNodeAsync(targetOwner.SystemContext, target.NodeId, token)
                                    .ConfigureAwait(false), token);
                        }
                        await entered.Task.WaitAsync(token).ConfigureAwait(false);
                    }, ct).ConfigureAwait(false);
                    owned.Add(result.Registrations[0]);
                    Assert.That(result.CleanupFailure, Is.Null);
                }

                var cleanup = new List<LocalReference> { reference };
                staging.ExcludePreparedReferences(cleanup);
                Assert.That(cleanup, Has.Count.EqualTo(1));
                AssertSharedSourceReference(currentSource.Source, target.NodeId, expected: true);
                currentSource.BeforeReferenceDeleteAsync = null;
                release.TrySetResult(true);
                Task<bool> deletionTask = deletion
                    ?? throw new InvalidOperationException("The deletion was not started.");
                SentinelException? failure = await Assert.ThrowsAsync<SentinelException>(async () =>
                    await deletionTask.WaitAsync(ct).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(expected));
                await removed.Task.WaitAsync(ct).ConfigureAwait(false);
                Assert.That(targetOwner.Find(target.NodeId), Is.Null);
                AssertSharedSourceReference(currentSource.Source, target.NodeId, expected: false);
            }
            finally
            {
                release.TrySetResult(true);
                if (source is not null)
                {
                    source.BeforeReferenceDeleteAsync = null;
                    source.AfterReferenceDelete = null;
                }
                try
                {
                    if (deletion is not null)
                    {
                        try
                        {
                            await deletion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        }
                        catch (SentinelException)
                        {
                        }
                    }
                }
                finally
                {
                    await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Destroyed-reference cleanup groups removals by physical node rather than node-defined equality.
        /// </summary>
        [Test]
        public async Task DestroyedReferenceCleanupPreservesEqualSourceNodeIdentityAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var factory = new EqualNodeOwnerFactory(kModelNamespaceUri + ":equal-cleanup-owner");
            var owned = new List<NodeManagerRegistration>();
            try
            {
                owned.Add(await AddIdentityManagerAsync(lifecycle, factory, timeout.Token).ConfigureAwait(false));
                EqualNodeOwnerManager owner = RequireLifecycleValue(factory.Created);
                NodeId missingTarget = new("removed-target", owner.NamespaceIndexes[0]);
                owner.First.AddReference(ReferenceTypeIds.HasComponent, false, missingTarget);
                owner.Second.AddReference(ReferenceTypeIds.HasComponent, false, missingTarget);

                using var destroyed = new NodeManagementLifecycleNodeManager(
                    m_server.CurrentInstance,
                    RequireLifecycleValue(factory.Configuration),
                    m_logger,
                    kModelNamespaceUri + ":equal-cleanup-destroyed");
                destroyed.GetRemovedExternalReferences().Add(
                    new LocalReference(owner.First.NodeId, ReferenceTypeIds.HasComponent, false, missingTarget));
                destroyed.GetRemovedExternalReferences().Add(
                    new LocalReference(owner.Second.NodeId, ReferenceTypeIds.HasComponent, false, missingTarget));
                var host = (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
                await host.RemoveDestroyedExternalReferencesAsync(destroyed, timeout.Token).ConfigureAwait(false);

                Assert.That(owner.First, Is.Not.SameAs(owner.Second));
                Assert.That(owner.First, Is.EqualTo(owner.Second));
                Assert.That(owner.First.ReferenceExists(ReferenceTypeIds.HasComponent, false, missingTarget), Is.False);
                Assert.That(
                    owner.Second.ReferenceExists(ReferenceTypeIds.HasComponent, false, missingTarget), Is.False);
                Assert.That(destroyed.GetRemovedExternalReferences(), Is.Empty);
            }
            finally
            {
                await RemoveIdentityRegistrationsAsync(lifecycle, owned).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks one source copy without relying on node-defined or manager-defined equality.
        /// </summary>
        private static void AssertSharedSourceReference(FolderState source, NodeId targetId, bool expected)
        {
            Assert.That(
                source.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// Adds one manager through a prepared lifecycle transaction.
        /// </summary>
        private static async Task<NodeManagerRegistration> AddIdentityManagerAsync(
            INodeManagerLifecycle lifecycle,
            IAsyncNodeManagerFactory factory,
            CancellationToken cancellationToken)
        {
            await using IPreparedNodeManagerBatch batch = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], cancellationToken).ConfigureAwait(false);
            NodeManagerBatchResult result = await batch.CommitAsync(_ => default, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(result.Registrations.Count, Is.EqualTo(1));
            return result.Registrations[0];
        }

        /// <summary>
        /// Retires the remaining managers in reverse publication order.
        /// </summary>
        private static async Task RemoveIdentityRegistrationsAsync(
            INodeManagerLifecycle lifecycle,
            List<NodeManagerRegistration> owned)
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (owned.Count != 0)
            {
                await RemoveLastIdentityRegistrationAsync(lifecycle, owned, cleanupTimeout.Token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Retires the last manager and waits for its cleanup.
        /// </summary>
        private static async Task RemoveLastIdentityRegistrationAsync(
            INodeManagerLifecycle lifecycle,
            List<NodeManagerRegistration> owned,
            CancellationToken cancellationToken)
        {
            int index = owned.Count - 1;
            await using IPreparedNodeManagerBatch batch = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(owned[index])], cancellationToken).ConfigureAwait(false);
            NodeManagerBatchResult result = await batch.CommitAsync(_ => default, cancellationToken)
                .ConfigureAwait(false);
            owned.RemoveAt(index);
            Assert.That(result.CleanupFailure, Is.Null);
            await result.Retirement.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a manager that owns two distinct but equal source nodes.
        /// </summary>
        private sealed class EqualNodeOwnerFactory(string namespaceUri) : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Gets the candidate produced by this factory.
            /// </summary>
            public EqualNodeOwnerManager? Created { get; private set; }

            /// <summary>
            /// Gets the configuration supplied during candidate creation.
            /// </summary>
            public ApplicationConfiguration? Configuration { get; private set; }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [namespaceUri];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Configuration = configuration;
                Created = new EqualNodeOwnerManager(server, configuration, namespaceUri);
                return new ValueTask<IAsyncNodeManager>(Created);
            }
        }

        /// <summary>
        /// Publishes two source nodes with different identifiers and equal values.
        /// </summary>
        private sealed class EqualNodeOwnerManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the distinct source nodes for this manager.
            /// </summary>
            public EqualNodeOwnerManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri)
                : base(server, configuration, NullLogger.Instance, namespaceUri)
            {
                First = new EqualReferenceNode("equal-first", NamespaceIndexes[0]);
                Second = new EqualReferenceNode("equal-second", NamespaceIndexes[0]);
            }

            /// <summary>
            /// Gets the first source node.
            /// </summary>
            public EqualReferenceNode First { get; }

            /// <summary>
            /// Gets the second source node.
            /// </summary>
            public EqualReferenceNode Second { get; }

            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                await AddPredefinedNodeAsync(SystemContext, First, cancellationToken).ConfigureAwait(false);
                await AddPredefinedNodeAsync(SystemContext, Second, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Models a node subclass that defines value equality independently of instance identity.
        /// </summary>
        private sealed class EqualReferenceNode : BaseObjectState
        {
            /// <summary>
            /// Creates a source node with the supplied identifier.
            /// </summary>
            public EqualReferenceNode(string name, ushort namespaceIndex)
                : base(null)
            {
                NodeId = new NodeId(name, namespaceIndex);
                BrowseName = new QualifiedName(name, namespaceIndex);
                DisplayName = new LocalizedText(name);
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is EqualReferenceNode;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 0;
            }
        }

        /// <summary>
        /// Creates an equal manager with its own copy of a shared source node.
        /// </summary>
        private sealed class EqualSharedSourceFactory(string namespaceUri, string sourceNamespaceUri)
            : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Gets the candidate produced by this factory.
            /// </summary>
            public EqualSharedSourceManager? Created { get; private set; }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [namespaceUri, sourceNamespaceUri];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Created = new EqualSharedSourceManager(server, configuration, namespaceUri, sourceNamespaceUri);
                return new ValueTask<IAsyncNodeManager>(Created);
            }
        }

        /// <summary>
        /// Compares equal to peers while retaining independently indexed nodes and contributions.
        /// </summary>
        private sealed class EqualSharedSourceManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates an owner with a source node shared by NodeId, not by instance.
            /// </summary>
            public EqualSharedSourceManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri,
                string sourceNamespaceUri)
                : base(server, configuration, NullLogger.Instance, namespaceUri, sourceNamespaceUri)
            {
                Source = new FolderState(null)
                {
                    NodeId = new NodeId("shared-source", NamespaceIndexes[1]),
                    BrowseName = new QualifiedName("shared-source", NamespaceIndexes[1]),
                    DisplayName = new LocalizedText("shared-source"),
                    TypeDefinitionId = ObjectTypeIds.FolderType
                };
            }

            /// <summary>
            /// Gets this manager's physical copy of the shared source.
            /// </summary>
            public FolderState Source { get; }

            /// <summary>
            /// Gets or sets whether source lookups return an opaque handle.
            /// </summary>
            public bool UseOpaqueSourceHandle { get; set; }

            /// <summary>
            /// Gets or sets a callback invoked after references are added to this owner.
            /// </summary>
            public Func<IDictionary<NodeId, IList<IReference>>, CancellationToken, ValueTask>?
                AfterReferenceAddAsync { get; set; }

            /// <summary>
            /// Gets or sets a callback invoked before a reference is removed from this owner.
            /// </summary>
            public Func<CancellationToken, ValueTask>? BeforeReferenceDeleteAsync { get; set; }

            /// <summary>
            /// Gets or sets a callback invoked after a reference is removed from this owner.
            /// </summary>
            public Action<ExpandedNodeId>? AfterReferenceDelete { get; set; }

            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                await AddPredefinedNodeAsync(Source, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public override async ValueTask<object> GetManagerHandleAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                object handle = await base.GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false);
                return UseOpaqueSourceHandle && nodeId == Source.NodeId && handle is NodeHandle
                    ? new OpaqueSourceHandle(handle)
                    : handle;
            }

            /// <inheritdoc/>
            public override async ValueTask AddReferencesAsync(
                IDictionary<NodeId, IList<IReference>> references,
                CancellationToken cancellationToken = default)
            {
                await base.AddReferencesAsync(references, cancellationToken).ConfigureAwait(false);
                if (AfterReferenceAddAsync is { } callback)
                {
                    await callback(references, cancellationToken).ConfigureAwait(false);
                }
            }

            /// <inheritdoc/>
            public override async ValueTask<ServiceResult> DeleteReferenceAsync(
                object sourceHandle,
                NodeId referenceTypeId,
                bool isInverse,
                ExpandedNodeId targetId,
                bool deleteBidirectional,
                CancellationToken cancellationToken = default)
            {
                if (BeforeReferenceDeleteAsync is { } callback)
                {
                    await callback(cancellationToken).ConfigureAwait(false);
                }
                if (sourceHandle is OpaqueSourceHandle opaque)
                {
                    sourceHandle = opaque.Handle;
                }
                ServiceResult result = await base.DeleteReferenceAsync(
                    sourceHandle, referenceTypeId, isInverse, targetId, deleteBidirectional, cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsGood(result))
                {
                    AfterReferenceDelete?.Invoke(targetId);
                }
                return result;
            }

            /// <summary>
            /// Indexes a probe and adds a managed reference from the shared source.
            /// </summary>
            public async ValueTask<NodeState> AddProbeAsync(string name, CancellationToken cancellationToken)
            {
                var probe = new BaseObjectState(null)
                {
                    NodeId = new NodeId(name, NamespaceIndexes[0]),
                    BrowseName = new QualifiedName(name, NamespaceIndexes[0]),
                    DisplayName = new LocalizedText(name),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                await AddPredefinedNodeAsync(probe, cancellationToken).ConfigureAwait(false);
                await AddReferenceFromNodeAsync(
                    probe, Source.NodeId, ReferenceTypeIds.Organizes, false, cancellationToken)
                    .ConfigureAwait(false);
                return probe;
            }

            /// <summary>
            /// Replaces the indexed source while preserving its identifier.
            /// </summary>
            public void ReplaceIndexedSource(NodeState replacement)
            {
                PredefinedNodes[Source.NodeId] = replacement;
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is EqualSharedSourceManager;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 0;
            }

            /// <summary>
            /// Holds a source handle without exposing its node to the master manager.
            /// </summary>
            private sealed record OpaqueSourceHandle(object Handle);
        }

        /// <summary>
        /// Creates one equality-comparing manager and its independent external reference.
        /// </summary>
        private sealed class EqualLifecycleFactory(string namespaceUri, NodeId target) : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Gets the candidate produced by this factory.
            /// </summary>
            public EqualLifecycleManager? Created { get; private set; }

            /// <summary>
            /// Gets the target of this candidate's external reference.
            /// </summary>
            public NodeId Target => target;

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [namespaceUri];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Created = new EqualLifecycleManager(server, configuration, namespaceUri, target);
                return new ValueTask<IAsyncNodeManager>(Created);
            }
        }

        /// <summary>
        /// Contributes an external reference while comparing equal to its peers.
        /// </summary>
        private sealed class EqualLifecycleManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string namespaceUri,
            NodeId target)
            : AsyncCustomNodeManager(server, configuration, NullLogger.Instance, namespaceUri)
        {
            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                MasterNodeManager.CreateExternalReference(
                    externalReferences, ObjectIds.ObjectsFolder, ReferenceTypeIds.HasComponent, false, target);
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is EqualLifecycleManager;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
