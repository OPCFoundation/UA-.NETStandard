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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Verifies that destruction removes dangling runtime links without changing a replacement's links.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredCleanupPreservesReplacementAndPrunesRemovedRuntimeReferencesAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null, timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId rootId = new(kRootNodeId, ns);
            NodeId valueId = new(kValueNodeId, ns);
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            var oldRoot = (NodeHandle)await original.NodeManager.GetManagerHandleAsync(rootId, timeout.Token)
                .ConfigureAwait(false);
            var oldValue = (NodeHandle)await original.NodeManager.GetManagerHandleAsync(valueId, timeout.Token)
                .ConfigureAwait(false);
            oldRoot.Node.AddReference(ReferenceTypeIds.HasComponent, false, valueId);
            oldValue.Node.AddReference(ReferenceTypeIds.HasComponent, true, rootId);
            oldRoot.Node.AddReference(ReferenceTypeIds.HasComponent, true, ObjectIds.ObjectsFolder);
            objects.AddReference(ReferenceTypeIds.HasComponent, false, rootId);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [replace
                    ? NodeManagerBatchChange.Replace(original, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kSecondRegistrationValue)))
                    : NodeManagerBatchChange.Remove(original)], null, timeout.Token).ConfigureAwait(false);
            NodeState? replacementRoot = null;
            NodeState? replacementValue = null;
            if (replace)
            {
                IAsyncNodeManager replacement = prepared.Registrations[0].NodeManager;
                var root = (NodeHandle)await replacement.GetManagerHandleAsync(rootId, timeout.Token)
                    .ConfigureAwait(false);
                var value = (NodeHandle)await replacement.GetManagerHandleAsync(valueId, timeout.Token)
                    .ConfigureAwait(false);
                replacementRoot = root.Node;
                replacementValue = value.Node;
                replacementRoot.AddReference(ReferenceTypeIds.HasComponent, false, valueId);
                replacementValue.AddReference(ReferenceTypeIds.HasComponent, true, rootId);
                replacementRoot.AddReference(ReferenceTypeIds.HasComponent, true, ObjectIds.ObjectsFolder);
            }

            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, rootId), Is.EqualTo(replace));
            if (replace)
            {
                Assert.That(replacementRoot!.ReferenceExists(
                    ReferenceTypeIds.HasComponent, false, valueId), Is.True);
                Assert.That(replacementValue!.ReferenceExists(
                    ReferenceTypeIds.HasComponent, true, rootId), Is.True);
            }
        }

        /// <summary>
        /// Removes a preserved runtime backlink when the last replacement no longer serves its target.
        /// </summary>
        [TestCase(1)]
        [TestCase(3)]
        public async Task RetiredCleanupPrunesInheritedRuntimeReferencesAfterReplacementRemovalAsync(
            int replacementCount)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            NodeManagerRegistration current = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null, timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId rootId = new(kRootNodeId, ns);
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            var oldRoot = (NodeHandle)await current.NodeManager.GetManagerHandleAsync(rootId, timeout.Token)
                .ConfigureAwait(false);
            oldRoot.Node.AddReference(ReferenceTypeIds.HasComponent, true, ObjectIds.ObjectsFolder);
            objects.AddReference(ReferenceTypeIds.HasComponent, false, rootId);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            for (int i = 0; i < replacementCount; i++)
            {
                await using IPreparedNodeManagerBatch replacement = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(current, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kSecondRegistrationValue)))], null, timeout.Token)
                    .ConfigureAwait(false);
                NodeManagerBatchResult replaced = await replacement.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                await replaced.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                current = replacement.Registrations[0];
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, rootId), Is.True);
            }
            await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(current)], null, timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult removed = await removal.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            await removed.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, rootId), Is.False);
        }

        /// <summary>
        /// Retains failed reciprocal observers and owner admissions without replaying successful observers.
        /// </summary>
        [Test]
        public async Task RetiredCleanupRetriesOnlyFailedReferenceObserversAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            NodeManagerRegistration source = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null, timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId rootId = new(kRootNodeId, ns);
            ushort sourceNamespace = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri);
            NodeId sourceId = new(kRootNodeId, sourceNamespace);
            var sourceHandle = (NodeHandle)await source.NodeManager.GetManagerHandleAsync(sourceId, timeout.Token)
                .ConfigureAwait(false);
            NodeState objects = sourceHandle.Node;
            var oldRoot = (NodeHandle)await original.NodeManager.GetManagerHandleAsync(rootId, timeout.Token)
                .ConfigureAwait(false);
            oldRoot.Node.AddReference(ReferenceTypeIds.HasComponent, true, sourceId);
            objects.AddReference(ReferenceTypeIds.HasComponent, false, rootId);
            NodeManagerGeneration owner = NodeManagerGenerationRegistry.GetOrCreate(source.NodeManager);
            long originalAdmissions = owner.Gate.ActiveCount;
            NodeStateReferenceRemoved? previous = objects.OnReferenceRemoved;
            int successful = 0;
            int attempts = 0;
            bool fail = true;
            var failure = new SentinelException("Reciprocal reference notification failed.");
            objects.OnReferenceRemoved += (_, type, _, target) =>
            {
                if (type == ReferenceTypeIds.HasComponent && target == rootId)
                {
                    successful++;
                }
            };
            objects.OnReferenceRemoved += (_, type, _, target) =>
            {
                if (type == ReferenceTypeIds.HasComponent && target == rootId)
                {
                    attempts++;
                    if (fail)
                    {
                        throw failure;
                    }
                }
            };
            try
            {
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(original)], null, timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                AggregateException? notificationFailure = await Assert.ThrowsAsync<AggregateException>(
                    async () => await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(notificationFailure!.InnerExceptions, Is.EqualTo(new[] { failure }));
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, rootId), Is.False);
                Assert.That(successful, Is.EqualTo(1));
                Assert.That(attempts, Is.EqualTo(1));
                Assert.That(owner.Gate.ActiveCount, Is.EqualTo(originalAdmissions + 1));
                fail = false;
                var host = (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
                await host.RemoveDestroyedExternalReferencesAsync(original.NodeManager, timeout.Token)
                    .ConfigureAwait(false);
                await host.RemoveDestroyedExternalReferencesAsync(original.NodeManager, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(successful, Is.EqualTo(1));
                Assert.That(attempts, Is.EqualTo(2));
                Assert.That(owner.Gate.ActiveCount, Is.EqualTo(originalAdmissions));
            }
            finally
            {
                fail = false;
                objects.OnReferenceRemoved = previous;
            }
        }

        /// <summary>
        /// Distinguishes a lazily resolved replacement node from an absent node in the same namespace.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredCleanupValidatesLazyReplacementTargetsAsync(bool targetExists)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null, timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId rootId = new(kRootNodeId, ns);
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            var oldRoot = (NodeHandle)await original.NodeManager.GetManagerHandleAsync(rootId, timeout.Token)
                .ConfigureAwait(false);
            oldRoot.Node.AddReference(ReferenceTypeIds.HasComponent, true, ObjectIds.ObjectsFolder);
            objects.AddReference(ReferenceTypeIds.HasComponent, false, rootId);
            Mock<IAsyncNodeManager> replacement = CreateLifecycleNodeManager(kModelNamespaceUri);
            var handle = new NodeHandle { NodeId = rootId, Validated = false };
            replacement.Setup(value => value.GetManagerHandleAsync(rootId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(handle);
            replacement.Setup(value => value.GetNodeMetadataAsync(
                It.IsAny<OperationContext>(), handle, It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(replacement.Object).Gate.ActiveCount,
                        Is.GreaterThan(0), "Lazy validation must enter the exact replacement generation.");
                    return new ValueTask<NodeMetadata>(targetExists ? new NodeMetadata(handle, rootId) : null!);
                });
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacement.Object);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(original), NodeManagerBatchChange.Add(factory.Object)],
                null, timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasComponent, false, rootId),
                Is.EqualTo(targetExists));
            replacement.Verify(value => value.GetNodeMetadataAsync(
                It.IsAny<OperationContext>(), handle, It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2), "The node is validated independently as a target and a reciprocal source.");
        }

        /// <summary>
        /// Cleanup waits outside owner admissions and retries against the decided publication.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredCleanupWaitsForPublicationWithoutHoldingSourceAdmissionAsync(bool removeSource)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            const string ownerUri = kModelNamespaceUri + ":ReservedCleanupOwner";
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(ownerUri);
            ApplicationConfiguration? configuration = null;
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal _, ApplicationConfiguration supplied, CancellationToken _) =>
                {
                    configuration = supplied;
                    return new ValueTask<IAsyncNodeManager>(owner.Object);
                });
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(ownerFactory.Object, timeout.Token).ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            var source = new NodeId(9902, (ushort)server.NamespaceUris.GetIndex(ownerUri));
            object handle = new();
            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .ReturnsAsync(handle);
            owner.Setup(value => value.GetNodeMetadataAsync(
                It.IsAny<OperationContext>(), handle, It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NodeMetadata(handle, source));
            owner.Setup(value => value.DeleteReferenceAsync(
                handle, ReferenceTypeIds.HasComponent, false, It.IsAny<ExpandedNodeId>(), false,
                It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Good);
            Mock<IAsyncNodeManager> addition = CreateLifecycleNodeManager(
                kModelNamespaceUri + ":ReservedCleanupAddition");
            var additionFactory = new Mock<IAsyncNodeManagerFactory>();
            additionFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(addition.Object);
            using var destroyed = new NodeManagementLifecycleNodeManager(
                server, configuration!, m_logger, kModelNamespaceUri + ":ReservedCleanupDestroyed");
            destroyed.GetRemovedExternalReferences().Add(
                new LocalReference(source, ReferenceTypeIds.HasComponent, false, new NodeId(9903)));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                removeSource
                    ? [NodeManagerBatchChange.Add(additionFactory.Object), NodeManagerBatchChange.Remove(registration)]
                    : [NodeManagerBatchChange.Add(additionFactory.Object)],
                null, timeout.Token).ConfigureAwait(false);
            var deciding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchResult> publication = prepared.CommitAsync(async cancellationToken =>
            {
                deciding.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            await deciding.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                Task cleanup = ((IDynamicNodeManagerHost)server.NodeManager)
                    .RemoveDestroyedExternalReferencesAsync(destroyed, timeout.Token).AsTask();
                Assert.That(cleanup.IsCompleted, Is.False, "A reserved publication is contention, not cleanup failure.");
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(owner.Object).Gate.ActiveCount, Is.Zero,
                    "Waiting cleanup must release its exact source admission before a publication can drain it.");
                owner.Verify(value => value.DeleteReferenceAsync(
                    handle, ReferenceTypeIds.HasComponent, false, It.IsAny<ExpandedNodeId>(), false,
                    It.IsAny<CancellationToken>()), Times.Never);
                release.TrySetResult(true);
                NodeManagerBatchResult result = await publication.ConfigureAwait(false);
                await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                await cleanup.WaitAsync(timeout.Token).ConfigureAwait(false);
                owner.Verify(value => value.DeleteReferenceAsync(
                    handle, ReferenceTypeIds.HasComponent, false, It.IsAny<ExpandedNodeId>(), false,
                    It.IsAny<CancellationToken>()), removeSource ? Times.Never() : Times.Once());
                Assert.That(destroyed.GetRemovedExternalReferences(), Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
                await publication.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validates a lazy replacement source before pruning its backlink or skipping an absent source.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiredCleanupValidatesLazyReplacementSourcesAsync(bool sourceExists)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            NodeManagerRegistration source = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration target = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null, timeout.Token).ConfigureAwait(false);
            ushort sourceNamespace = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri);
            ushort targetNamespace = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId sourceId = new(kRootNodeId, sourceNamespace);
            NodeId targetId = new(kRootNodeId, targetNamespace);
            var oldSource = (NodeHandle)await source.NodeManager.GetManagerHandleAsync(sourceId, timeout.Token)
                .ConfigureAwait(false);
            var oldTarget = (NodeHandle)await target.NodeManager.GetManagerHandleAsync(targetId, timeout.Token)
                .ConfigureAwait(false);
            oldSource.Node.AddReference(ReferenceTypeIds.HasComponent, false, targetId);
            oldTarget.Node.AddReference(ReferenceTypeIds.HasComponent, true, sourceId);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                    new ValueTask<IAsyncNodeManager>(
                        new LazyReferenceSourceNodeManager(server, configuration, targetId, sourceExists)));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(target), NodeManagerBatchChange.Replace(source, factory.Object)],
                null, timeout.Token).ConfigureAwait(false);
            var replacement = (LazyReferenceSourceNodeManager)prepared.Registrations[0].NodeManager;
            int removed = 0;
            if (replacement.Source is { } node)
            {
                node.OnReferenceRemoved += (_, type, _, id) =>
                {
                    if (type == ReferenceTypeIds.HasComponent && id == targetId)
                    {
                        removed++;
                    }
                };
            }
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            try
            {
                await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(replacement.ValidationCount, Is.GreaterThan(0));
                Assert.That(removed, Is.EqualTo(sourceExists ? 1 : 0));
                if (replacement.Source is { } remaining)
                {
                    Assert.That(remaining.ReferenceExists(ReferenceTypeIds.HasComponent, false, targetId), Is.False);
                    Assert.That(remaining.ReferenceExists(
                        ReferenceTypeIds.HasComponent, false, ObjectIds.Server), Is.True);
                }
            }
            finally
            {
                var cleanupNode = new BaseObjectState(null) { NodeId = sourceId };
                replacement.ValidateIssuedHandlesForCleanup(replacement.Source ?? cleanupNode);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var host = (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
                try
                {
                    await host.RemoveDestroyedExternalReferencesAsync(target.NodeManager, cleanup.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await host.RemoveDestroyedExternalReferencesAsync(source.NodeManager, cleanup.Token)
                        .ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchReferencesStayPrivateDuringDurableDecisionAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kModelNamespaceUri);
            NodeId root = new(kRootNodeId, ns);
            NodeId concurrent = new(8401, ns);
            var added = new List<ExpandedNodeId>();
            NodeStateReferenceAdded? previousCallback = objects.OnReferenceAdded;
            objects.OnReferenceAdded = (_, _, _, target) => added.Add(target);
            try
            {
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], null, timeout.Token)
                    .ConfigureAwait(false);
                await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
                await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                    .ConfigureAwait(false);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
                {
                    Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.False);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    if (rejectDecision)
                    {
                        throw new IOException("Confirmed reference noncommit.");
                    }
                }, timeout.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(added, Is.Empty);
                    Assert.Throws<InvalidOperationException>(() =>
                        objects.AddReference(ReferenceTypeIds.Organizes, false, concurrent));
                    await AssertNativeReferenceAsync(
                        session, ObjectIds.ObjectsFolder, root, ReferenceTypeIds.Organizes, false, timeout.Token)
                        .ConfigureAwait(false);
                    var memory = new List<IReference>();
                    objects.GetReferences(m_server.CurrentInstance.DefaultSystemContext, memory);
                    Assert.That(memory.Any(reference => reference.TargetId == root), Is.False);
                }
                finally
                {
                    release.TrySetResult(true);
                }
                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
                else
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                await AssertNativeReferenceAsync(
                    session, ObjectIds.ObjectsFolder, root, ReferenceTypeIds.Organizes, !rejectDecision, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, concurrent), Is.False);
                Assert.That(added, Is.EqualTo(
                    rejectDecision ? Array.Empty<ExpandedNodeId>() : new[] { new ExpandedNodeId(root) }));
                objects.AddReference(ReferenceTypeIds.Organizes, false, concurrent);
                Assert.That(objects.RemoveReference(ReferenceTypeIds.Organizes, false, concurrent), Is.True);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                objects.OnReferenceAdded = previousCallback;
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchDecisionDoesNotHoldDynamicMutationSemaphoreAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], null, timeout.Token)
                .ConfigureAwait(false);

            await prepared.CommitAsync(async token =>
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var requestLifetime = new RequestLifetime(requestTimeout.Token);
                RequestHeader requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                AddNodesResponse response = await m_server.AddNodesAsync(
                    m_secureChannelContext,
                    requestHeader,
                    [
                        new AddNodesItem
                        {
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            BrowseName = new QualifiedName("Unreachable", 0),
                            NodeClass = NodeClass.Object
                        }
                    ],
                    requestLifetime).ConfigureAwait(false);
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadParentNodeIdInvalid));
            }, timeout.Token).ConfigureAwait(false);

            Assert.That(prepared.IsCommitted, Is.True);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchDecisionWaitsForValidParentAddNodesMutationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            TrackingLifecycleNodeManager? targetManager = null;
            NodeManagerRegistration targetRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue,
                    manager => targetManager = manager,
                    kSecondModelNamespaceUri),
                null,
                timeout.Token).ConfigureAwait(false);
            Assert.That(targetManager, Is.Not.Null);
            ushort targetNamespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                targetRegistration.NamespaceUris[0]);
            NodeId addedNodeId = new(8402, targetNamespaceIndex);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kSecondRegistrationValue)))],
                null,
                timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();

            AddNodesResponse? response = null;
            bool requestCompletedBeforeDecision = false;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                RequestHeader requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                Task<AddNodesResponse> request = m_server.AddNodesAsync(
                    m_secureChannelContext,
                    requestHeader,
                    [
                        new AddNodesItem
                        {
                            ParentNodeId = ObjectIds.ObjectsFolder,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            RequestedNewNodeId = addedNodeId,
                            BrowseName = new QualifiedName("DuringDecision", targetNamespaceIndex),
                            NodeClass = NodeClass.Object,
                            TypeDefinition = ObjectTypeIds.BaseObjectType
                        }
                    ],
                    RequestLifetime.None).AsTask();
                requestCompletedBeforeDecision = request.IsCompleted;
                release.TrySetResult(true);
                NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                response = await request.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }

            Assert.That(response!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].AddedNodeId, Is.EqualTo(addedNodeId));
            Assert.That(requestCompletedBeforeDecision, Is.False,
                "A valid-parent client AddNodes operation must wait for the in-flight durable decision.");
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task AddNodesCallbackCanCommitNestedLifecycleMutationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var rescue = new CancellationTokenSource();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token, rescue.Token);
            var lifecycle = m_server.NodeManagerLifecycle;
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedCreated = new TaskCompletionSource<TrackingLifecycleNodeManager>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue,
                manager => nestedCreated.TrySetResult(manager),
                kSecondModelNamespaceUri);
            const string targetNamespaceUri = kModelNamespaceUri + ":AddNodesReentry";
            IAsyncNodeManagerFactory targetFactory = CreateLifecycleCallbackNodeManagerFactory(
                targetNamespaceUri,
                async cancellationToken =>
                {
                    callbackEntered.TrySetResult(true);
                    await lifecycle.AddAsync(nestedFactory, null, cancellationToken).ConfigureAwait(false);
                },
                sessionClosing: null);
            await lifecycle.AddAsync(targetFactory, null, timeout.Token).ConfigureAwait(false);
            ushort targetNamespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                targetNamespaceUri);
            var addedNodeId = new NodeId(8403, targetNamespaceIndex);
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            using var requestLifetime = new RequestLifetime(requestCancellation.Token);
            Task<AddNodesResponse>? request = null;
            AddNodesResponse? response = null;
            TrackingLifecycleNodeManager? nestedManager = null;
            bool completedBeforeRescue = false;

            try
            {
                request = m_server.AddNodesAsync(
                    m_secureChannelContext,
                    requestHeader,
                    [
                        new AddNodesItem
                        {
                            ParentNodeId = ObjectIds.ObjectsFolder,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            RequestedNewNodeId = addedNodeId,
                            BrowseName = new QualifiedName("AddNodesReentry", targetNamespaceIndex),
                            NodeClass = NodeClass.Object,
                            TypeDefinition = ObjectTypeIds.BaseObjectType
                        }
                    ],
                    requestLifetime).AsTask();
                await callbackEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                completedBeforeRescue = await Task.WhenAny(
                    request,
                    Task.Delay(TimeSpan.FromSeconds(3), timeout.Token)).ConfigureAwait(false) == request;
                if (!completedBeforeRescue)
                {
                    rescue.Cancel();
                }
                try
                {
                    response = await request.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (rescue.IsCancellationRequested)
                {
                }
            }
            finally
            {
                rescue.Cancel();
                if (request is not null)
                {
                    try
                    {
                        await request.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (rescue.IsCancellationRequested)
                    {
                    }
                }
            }

            Assert.That(completedBeforeRescue, Is.True,
                "A lifecycle mutation from AddNodeAsync must not wait on the AddNodes callback's own semaphore.");
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(nestedCreated.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            nestedManager = await nestedCreated.Task.ConfigureAwait(false);
            Assert.That(nestedManager, Is.Not.Null);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task SessionClosingCallbackCanCommitNestedLifecycleMutationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var rescue = new CancellationTokenSource();
            CancellationToken rescueToken = rescue.Token;
            var lifecycle = m_server.NodeManagerLifecycle;
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedCreated = new TaskCompletionSource<TrackingLifecycleNodeManager>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int sessionClosingCallbackCount = 0;
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue,
                manager => nestedCreated.TrySetResult(manager),
                kSecondModelNamespaceUri);
            IAsyncNodeManagerFactory targetFactory = CreateLifecycleCallbackNodeManagerFactory(
                kModelNamespaceUri + ":SessionClosingReentry",
                addNode: null,
                async cancellationToken =>
                {
                    if (Interlocked.Increment(ref sessionClosingCallbackCount) == 1)
                    {
                        callbackEntered.TrySetResult(true);
                        using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            rescueToken);
                        try
                        {
                            await lifecycle.AddAsync(
                                nestedFactory,
                                null,
                                callbackCancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (rescueToken.IsCancellationRequested)
                        {
                        }
                    }
                });
            await lifecycle.AddAsync(targetFactory, null, timeout.Token).ConfigureAwait(false);
            Task<CloseSessionResponse>? close = null;
            CloseSessionResponse? response = null;
            TrackingLifecycleNodeManager? nestedManager = null;
            bool completedBeforeRescue = false;

            try
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                close = m_server.CloseSessionAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    true,
                    RequestLifetime.None).AsTask();
                await callbackEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                completedBeforeRescue = await Task.WhenAny(
                    close,
                    Task.Delay(TimeSpan.FromSeconds(3), timeout.Token)).ConfigureAwait(false) == close;
                if (!completedBeforeRescue)
                {
                    rescue.Cancel();
                }
                try
                {
                    response = await close.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (rescue.IsCancellationRequested)
                {
                }
            }
            finally
            {
                rescue.Cancel();
                if (close is not null)
                {
                    try
                    {
                        await close.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (rescue.IsCancellationRequested)
                    {
                    }
                }
            }

            m_requestHeader = null!;
            if (nestedCreated.Task.Status == TaskStatus.RanToCompletion)
            {
                nestedManager = await nestedCreated.Task.ConfigureAwait(false);
            }
            Assert.That(completedBeforeRescue, Is.True,
                "A lifecycle mutation from SessionClosingAsync must not wait on the callback's own semaphore.");
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(nestedManager, Is.Not.Null);
        }

        /// <summary>
        /// A callback revoked during admission must reacquire both capabilities.
        /// </summary>
        [Category("Integration")]
        [TestCase(true)]
        [TestCase(false)]
        public async Task RevokedCallbackAdmissionReacquiresBothCapabilitiesAsync(
            bool inheritMutation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            using INodeManagerBatchAdmission opposing =
                await host.AcquireCommitAdmissionAsync(null, timeout.Token).ConfigureAwait(false);
            var current = new AsyncLocal<NodeManagerCallbackPermit?>();
            NodeManagerCallbackPermit callbackPermit = new(
                current,
                inheritMutation
                    ? NodeManagerAdmissionCapability.AddressSpaceMutation
                    : NodeManagerAdmissionCapability.ServerLifecycle);
            Task<INodeManagerBatchAdmission> pending =
                host.AcquireCommitAdmissionAsync(callbackPermit, timeout.Token).AsTask();
            try
            {
                Assert.That(pending.IsCompleted, Is.False);
                ValueTask disposal = callbackPermit.DisposeAsync();
                Assert.That(current.Value, Is.Null, "Disposal must restore the caller's context before returning.");
                await disposal.ConfigureAwait(false);
            }
            finally
            {
                await callbackPermit.DisposeAsync().ConfigureAwait(false);
                opposing.Dispose();
            }
            using INodeManagerBatchAdmission acquired = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(acquired.Includes(NodeManagerAdmissionCapability.AddressSpaceMutation), Is.True);
            Assert.That(acquired.Includes(NodeManagerAdmissionCapability.ServerLifecycle), Is.True);
        }

        /// <summary>
        /// Concurrent service requests serialize callbacks while preserving nested lifecycle publication.
        /// </summary>
        [Category("Integration")]
        [Category("NativeTcp")]
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task ConcurrentAddNodeAndSessionClosingCallbacksCanBothReenterLifecycleAsync(
            bool forkNestedPublication,
            bool cancelSessionClosing)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var rescue = new CancellationTokenSource();
            CancellationToken rescueToken = rescue.Token;
            var lifecycle = m_server.NodeManagerLifecycle;
            int callbacksEnabled = 1;
            var addNodeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAddNode = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var addNested = new TaskCompletionSource<NodeManagerRegistration>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sessionClosingNested = new TaskCompletionSource<NodeManagerRegistration>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var addNestedStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sessionClosingNestedStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IAsyncNodeManagerFactory addNestedFactory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue,
                _ => { },
                kSecondModelNamespaceUri + ":AddNodeConcurrentReentry");
            IAsyncNodeManagerFactory sessionClosingNestedFactory = CreateTrackingNodeManagementFactory(
                kFirstRegistrationValue,
                _ => { },
                kSecondModelNamespaceUri + ":SessionClosingConcurrentReentry");
            const string targetNamespaceUri = kModelNamespaceUri + ":ConcurrentCallbackReentry";
            int sessionClosingCount = 0;
            int addNodeBodyActive = 0;
            bool callbacksOverlapped = false;
            bool sessionCallbackOwnsBothCapabilities = false;
            IAsyncNodeManagerFactory targetFactory = CreateLifecycleCallbackNodeManagerFactory(
                targetNamespaceUri,
                async cancellationToken =>
                {
                    if (Volatile.Read(ref callbacksEnabled) == 0)
                    {
                        return;
                    }
                    Volatile.Write(ref addNodeBodyActive, 1);
                    addNodeEntered.TrySetResult(true);
                    using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        rescueToken);
                    try
                    {
                        await releaseAddNode.Task.WaitAsync(callbackCancellation.Token).ConfigureAwait(false);
                        Task<NodeManagerRegistration> publication = forkNestedPublication
                            ? Task.Run(async () => await lifecycle.AddAsync(
                                addNestedFactory, null, callbackCancellation.Token).ConfigureAwait(false))
                            : lifecycle.AddAsync(addNestedFactory, null, callbackCancellation.Token).AsTask();
                        addNestedStarted.TrySetResult(true);
                        NodeManagerRegistration registration = await publication.ConfigureAwait(false);
                        addNested.TrySetResult(registration);
                    }
                    catch (OperationCanceledException) when (rescueToken.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        Volatile.Write(ref addNodeBodyActive, 0);
                    }
                },
                async cancellationToken =>
                {
                    if (Volatile.Read(ref callbacksEnabled) == 0)
                    {
                        return;
                    }
                    callbacksOverlapped |= Volatile.Read(ref addNodeBodyActive) != 0;
                    NodeManagerCallbackPermit? permit =
                        ((MasterNodeManager)m_server.CurrentInstance.NodeManager).CaptureLifecycleCallbackPermit();
                    sessionCallbackOwnsBothCapabilities = permit is { IsActive: true } &&
                        permit.Capabilities == (NodeManagerAdmissionCapability.ServerLifecycle |
                            NodeManagerAdmissionCapability.AddressSpaceMutation);
                    int callbackCount = Interlocked.Increment(ref sessionClosingCount);
                    using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        rescueToken);
                    try
                    {
                        if (callbackCount == 1)
                        {
                            ValueTask<NodeManagerRegistration> publication = lifecycle.AddAsync(
                                sessionClosingNestedFactory, null, callbackCancellation.Token);
                            sessionClosingNestedStarted.TrySetResult(true);
                            NodeManagerRegistration registration = await publication.ConfigureAwait(false);
                            sessionClosingNested.TrySetResult(registration);
                        }
                    }
                    catch (OperationCanceledException) when (rescueToken.IsCancellationRequested)
                    {
                    }
                });
            NodeManagerRegistration targetRegistration = await lifecycle.AddAsync(
                targetFactory, null, timeout.Token).ConfigureAwait(false);
            ushort targetNamespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                targetNamespaceUri);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession clientSession = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"),
                SecurityPolicies.None).ConfigureAwait(false);
            Task<AddNodesResponse>? addNodes = null;
            Task? firstSessionClosing = null;
            AddNodesResponse? addNodesResponse = null;
            var cleanupFailures = new List<Exception>();
            bool bothReentriesCompletedBeforeRescue = false;
            bool addNodeReentryCompletedBeforeRescue = false;
            bool sessionClosingReentryCompletedBeforeRescue = false;
            using var firstContext = new OperationContext(
                new RequestHeader(), null, RequestType.CloseSession, RequestLifetime.None);
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            using var closeCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

            try
            {
                addNodes = clientSession.AddNodesAsync(
                    null,
                    [
                        new AddNodesItem
                        {
                            ParentNodeId = ObjectIds.ObjectsFolder,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            RequestedNewNodeId = new NodeId(8406, targetNamespaceIndex),
                            BrowseName = new QualifiedName("ConcurrentCallbackReentry", targetNamespaceIndex),
                            NodeClass = NodeClass.Object,
                            TypeDefinition = ObjectTypeIds.BaseObjectType
                        }
                    ],
                    timeout.Token).AsTask();
                await addNodeEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                firstSessionClosing = master.SessionClosingAsync(
                    firstContext, new NodeId(8407), true, closeCancellation.Token).AsTask();
                if (cancelSessionClosing)
                {
                    closeCancellation.Cancel();
                    try
                    {
                        await firstSessionClosing.ConfigureAwait(false);
                        Assert.Fail("Session close admission must observe cancellation.");
                    }
                    catch (OperationCanceledException) when (closeCancellation.IsCancellationRequested)
                    {
                    }
                    firstSessionClosing = null;
                }
                releaseAddNode.TrySetResult(true);
                if (cancelSessionClosing)
                {
                    firstSessionClosing = master.SessionClosingAsync(
                        firstContext, new NodeId(8407), true, timeout.Token).AsTask();
                }
                await Task.WhenAll(addNestedStarted.Task, sessionClosingNestedStarted.Task)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                Task bothNestedPublications = Task.WhenAll(addNested.Task, sessionClosingNested.Task);
                bothReentriesCompletedBeforeRescue = await Task.WhenAny(
                    bothNestedPublications,
                    Task.Delay(TimeSpan.FromSeconds(3), timeout.Token)).ConfigureAwait(false) ==
                    bothNestedPublications;
                addNodeReentryCompletedBeforeRescue = addNested.Task.Status == TaskStatus.RanToCompletion;
                sessionClosingReentryCompletedBeforeRescue =
                    sessionClosingNested.Task.Status == TaskStatus.RanToCompletion;
            }
            finally
            {
                rescue.Cancel();
                releaseAddNode.TrySetResult(true);
                if (addNodes is not null)
                {
                    try
                    {
                        addNodesResponse = await addNodes.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                if (firstSessionClosing is not null)
                {
                    try
                    {
                        await firstSessionClosing.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                Volatile.Write(ref callbacksEnabled, 0);
            }

            if (addNested.Task.Status == TaskStatus.RanToCompletion)
            {
                await lifecycle.RemoveAsync(await addNested.Task.ConfigureAwait(false), null, timeout.Token)
                    .ConfigureAwait(false);
            }
            if (sessionClosingNested.Task.Status == TaskStatus.RanToCompletion)
            {
                await lifecycle.RemoveAsync(await sessionClosingNested.Task.ConfigureAwait(false), null, timeout.Token)
                    .ConfigureAwait(false);
            }
            await lifecycle.RemoveAsync(targetRegistration, null, timeout.Token).ConfigureAwait(false);

            Assert.That(bothReentriesCompletedBeforeRescue, Is.True,
                $"Both callbacks must complete nested lifecycle publication without an admission cycle. " +
                $"AddNode={addNodeReentryCompletedBeforeRescue}, " +
                $"SessionClosing={sessionClosingReentryCompletedBeforeRescue}.");
            Assert.That(cleanupFailures, Is.Empty);
            Assert.That(callbacksOverlapped, Is.False);
            Assert.That(sessionCallbackOwnsBothCapabilities, Is.True);
            Assert.That(addNodesResponse!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(addNested.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(sessionClosingNested.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        /// <summary>
        /// Concurrent publication admission cannot hold the writer while a session callback re-enters lifecycle.
        /// </summary>
        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task ConcurrentPublicationDoesNotDeadlockSessionClosingReentryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var rescue = new CancellationTokenSource();
            CancellationToken rescueToken = rescue.Token;
            int callbacksEnabled = 1;
            var lifecycle = m_server.NodeManagerLifecycle;
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowNested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedAdded = new TaskCompletionSource<NodeManagerRegistration>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var concurrentAdmissionEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue,
                _ => { },
                kSecondModelNamespaceUri + ":SessionClosingConcurrentNested");
            IAsyncNodeManagerFactory targetFactory = CreateLifecycleCallbackNodeManagerFactory(
                kModelNamespaceUri + ":SessionClosingConcurrentPublication",
                addNode: null,
                async cancellationToken =>
                {
                    if (Volatile.Read(ref callbacksEnabled) == 0)
                    {
                        return;
                    }
                    callbackEntered.TrySetResult(true);
                    using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        rescueToken);
                    try
                    {
                        await allowNested.Task.WaitAsync(callbackCancellation.Token).ConfigureAwait(false);
                        NodeManagerRegistration registration = await lifecycle.AddAsync(
                            nestedFactory, null, callbackCancellation.Token).ConfigureAwait(false);
                        nestedAdded.TrySetResult(registration);
                    }
                    catch (OperationCanceledException) when (rescueToken.IsCancellationRequested)
                    {
                    }
                });
            NodeManagerRegistration targetRegistration = await lifecycle.AddAsync(
                targetFactory, null, timeout.Token).ConfigureAwait(false);
            var server = (ServerInternalData)m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            PropertyInfo nodeManagerProperty = typeof(ServerInternalData).GetProperty(
                nameof(ServerInternalData.NodeManager)) ??
                throw new AssertionException("The bound master NodeManager property was not found.");
            Mock<IMasterNodeManager>? proxy = null;
            Task<CloseSessionResponse>? close = null;
            Task<NodeManagerRegistration>? concurrentAdd = null;
            NodeManagerRegistration? nestedRegistration = null;
            NodeManagerRegistration? concurrentRegistration = null;
            bool nestedCompletedBeforeRescue = false;

            try
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                close = m_server.CloseSessionAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    true,
                    RequestLifetime.None).AsTask();
                await callbackEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                proxy = CreatePostSwapPublicationFailureHost(
                    master, () => false, notifyBeforeFailure: false,
                    new InvalidOperationException("Unused publication failure."),
                    onReferencePreparationAttempt: () => concurrentAdmissionEntered.TrySetResult(true));
                nodeManagerProperty.SetValue(server, proxy.Object);
                concurrentAdd = lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue,
                        _ => { },
                        kSecondModelNamespaceUri + ":SessionClosingConcurrentOther"),
                    null,
                    timeout.Token).AsTask();
                await concurrentAdmissionEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                allowNested.TrySetResult(true);
                nestedCompletedBeforeRescue = await Task.WhenAny(
                    nestedAdded.Task,
                    Task.Delay(TimeSpan.FromSeconds(3), timeout.Token)).ConfigureAwait(false) == nestedAdded.Task;
            }
            finally
            {
                allowNested.TrySetResult(true);
                rescue.Cancel();
                try
                {
                    if (close is not null)
                    {
                        await close.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (concurrentAdd is not null)
                    {
                        concurrentRegistration = await concurrentAdd.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    Volatile.Write(ref callbacksEnabled, 0);
                    nodeManagerProperty.SetValue(server, master);
                }
            }

            m_requestHeader = null!;
            if (nestedAdded.Task.Status == TaskStatus.RanToCompletion)
            {
                nestedRegistration = await nestedAdded.Task.ConfigureAwait(false);
            }
            Assert.That(nestedCompletedBeforeRescue, Is.True,
                "The callback-owned startup semaphore must not deadlock an unrelated writer-held publication.");
            Assert.That(nestedRegistration, Is.Not.Null);
            Assert.That(concurrentRegistration, Is.Not.Null);
            Assert.That(close!.Result.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            await lifecycle.RemoveAsync(nestedRegistration!, null, timeout.Token).ConfigureAwait(false);
            await lifecycle.RemoveAsync(concurrentRegistration!, null, timeout.Token).ConfigureAwait(false);
            await lifecycle.RemoveAsync(targetRegistration, null, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// A SessionClosing callback may enter during a durable decision, but nested publication waits for its writer.
        /// </summary>
        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task SessionClosingReentryDuringDurableDecisionWaitsForWriterReleaseAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var rescue = new CancellationTokenSource();
            var lifecycle = m_server.NodeManagerLifecycle;
            var decisionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sessionClosingEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedPublicationStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedPublicationCompleted = new TaskCompletionSource<NodeManagerRegistration>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            const string targetNamespaceUri = kModelNamespaceUri + ":SessionClosingDuringDecision";
            IAsyncNodeManagerFactory nestedFactory = CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue,
                _ => { },
                kSecondModelNamespaceUri + ":SessionClosingDecisionNested");
            int sessionClosingCount = 0;
            IAsyncNodeManagerFactory targetFactory = CreateLifecycleCallbackNodeManagerFactory(
                targetNamespaceUri,
                addNode: null,
                async cancellationToken =>
                {
                    if (Interlocked.Increment(ref sessionClosingCount) != 1)
                    {
                        return;
                    }
                    sessionClosingEntered.TrySetResult(true);
                    using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        rescue.Token);
                    try
                    {
                        ValueTask<NodeManagerRegistration> publication = lifecycle.AddAsync(
                            nestedFactory, null, callbackCancellation.Token);
                        nestedPublicationStarted.TrySetResult(true);
                        NodeManagerRegistration registration = await publication.ConfigureAwait(false);
                        nestedPublicationCompleted.TrySetResult(registration);
                    }
                    catch (OperationCanceledException) when (rescue.IsCancellationRequested)
                    {
                    }
                });
            NodeManagerRegistration targetRegistration = await lifecycle.AddAsync(
                targetFactory, null, timeout.Token).ConfigureAwait(false);
            ushort targetNamespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                targetNamespaceUri);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession clientSession = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"),
                SecurityPolicies.None).ConfigureAwait(false);
            NodeId addedNodeId = new(8405, targetNamespaceIndex);
            var server = (ServerInternalData)m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            PropertyInfo nodeManagerProperty = typeof(ServerInternalData).GetProperty(
                nameof(ServerInternalData.NodeManager)) ??
                throw new AssertionException("The bound master NodeManager property was not found.");
            var addNodesEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IMasterNodeManager> proxy = CreatePostSwapPublicationFailureHost(
                master, () => false, notifyBeforeFailure: false,
                new InvalidOperationException("Unused publication failure."));
            proxy.Setup(value => value.AddNodesAsync(
                It.IsAny<OperationContext>(), It.IsAny<ArrayOf<AddNodesItem>>(), It.IsAny<CancellationToken>()))
                .Returns<OperationContext, ArrayOf<AddNodesItem>, CancellationToken>((context, nodes, token) =>
                {
                    var response = master.AddNodesAsync(context, nodes, token);
                    addNodesEntered.TrySetResult(true);
                    return response;
                });
            var batchLifecycle = (INodeManagerBatchLifecycle)lifecycle;
            await using IPreparedNodeManagerBatch prepared = await batchLifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue,
                        _ => { },
                        kModelNamespaceUri + ":DecisionWindowOther"))
                ],
                null,
                timeout.Token).ConfigureAwait(false);

            Task<NodeManagerBatchResult>? commit = null;
            Task<AddNodesResponse>? addNodes = null;
            Task<CloseSessionResponse>? close = null;
            NodeManagerBatchResult? commitResult = null;
            AddNodesResponse? addNodesResponse = null;
            CloseSessionResponse? closeResponse = null;
            NodeManagerRegistration? nestedRegistration = null;
            bool validAddNodesWaitedForDecision = false;
            bool nestedPublicationWaitedForWriter = false;
            bool sessionClosingEnteredBeforeDecision = false;

            try
            {
                commit = prepared.CommitAsync(async token =>
                {
                    decisionEntered.TrySetResult(true);
                    await releaseDecision.Task.WaitAsync(token).ConfigureAwait(false);
                }, timeout.Token).AsTask();
                await decisionEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                nodeManagerProperty.SetValue(server, proxy.Object);
                addNodes = clientSession.AddNodesAsync(
                    null,
                    [
                        new AddNodesItem
                        {
                            ParentNodeId = ObjectIds.ObjectsFolder,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            RequestedNewNodeId = addedNodeId,
                            BrowseName = new QualifiedName("DuringDurableDecision", targetNamespaceIndex),
                            NodeClass = NodeClass.Object,
                            TypeDefinition = ObjectTypeIds.BaseObjectType
                        }
                    ],
                    timeout.Token).AsTask();
                await addNodesEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                nodeManagerProperty.SetValue(server, master);
                validAddNodesWaitedForDecision = !addNodes.IsCompleted;

                m_requestHeader.Timestamp = DateTimeUtc.Now;
                close = m_server.CloseSessionAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    true,
                    RequestLifetime.None).AsTask();
                m_requestHeader = null!;
                sessionClosingEnteredBeforeDecision = await Task.WhenAny(
                    sessionClosingEntered.Task,
                    Task.Delay(TimeSpan.FromSeconds(10), timeout.Token)).ConfigureAwait(false) ==
                    sessionClosingEntered.Task;
                if (sessionClosingEnteredBeforeDecision)
                {
                    await nestedPublicationStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    nestedPublicationWaitedForWriter = !nestedPublicationCompleted.Task.IsCompleted;
                }

                releaseDecision.TrySetResult(true);
                commitResult = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                addNodesResponse = await addNodes.WaitAsync(timeout.Token).ConfigureAwait(false);
                closeResponse = await close.WaitAsync(timeout.Token).ConfigureAwait(false);
                nestedRegistration = await nestedPublicationCompleted.Task.WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                nodeManagerProperty.SetValue(server, master);
                releaseDecision.TrySetResult(true);
                rescue.Cancel();
                if (commit is not null)
                {
                    commitResult ??= await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (addNodes is not null)
                {
                    addNodesResponse ??= await addNodes.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (close is not null)
                {
                    closeResponse = await close.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (nestedPublicationCompleted.Task.Status == TaskStatus.RanToCompletion)
                {
                    nestedRegistration ??= await nestedPublicationCompleted.Task.ConfigureAwait(false);
                }
            }

            m_requestHeader = null!;
            Assert.That(sessionClosingEnteredBeforeDecision, Is.True,
                "A decision-blocked AddNodes request must not retain admission needed by SessionClosing.");
            Assert.That(validAddNodesWaitedForDecision, Is.True,
                "A valid AddNodes request must wait until the durable decision is complete.");
            Assert.That(nestedPublicationWaitedForWriter, Is.True,
                "A callback that enters during the decision cannot publish until the current writer is released.");
            Assert.That(commitResult!.CleanupFailure, Is.Null);
            Assert.That(addNodesResponse!.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(closeResponse!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(nestedRegistration, Is.Not.Null);
            await lifecycle.RemoveAsync(nestedRegistration!, null, timeout.Token).ConfigureAwait(false);
            await lifecycle.RemoveAsync(commitResult.Registrations[0], null, timeout.Token).ConfigureAwait(false);
            await lifecycle.RemoveAsync(targetRegistration, null, timeout.Token).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchNodeOwnedReferencesBecomeVisibleToAnInflightNativeRequestAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            TrackingLifecycleNodeManager? survivor = null;
            await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, manager => survivor = manager),
                null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(survivor);
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            NodeId root = new(kRootNodeId, ns);
            NodeId value = new(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], null, timeout.Token)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool before = true;
            bool after = true;
            survivor.ReadCallbackNodeId = value;
            survivor.ReadCallback = async (_, token) =>
            {
                before = objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                after = objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root);
            };
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            Task<ReadResponse> read = session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = value, AttributeId = Attributes.Value }], timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(read.IsCompleted, Is.False);
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.True);
            }
            finally
            {
                release.TrySetResult(true);
            }
            ReadResponse response = await read.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].WrappedValue, Is.EqualTo(new Variant(kFirstRegistrationValue)));
            Assert.That(before, Is.False);
            Assert.That(after, Is.True, "Node-owned references are shared even while application values stay pinned.");
            await AssertNativeReferenceAsync(
                session, ObjectIds.ObjectsFolder, root, ReferenceTypeIds.Organizes, true, timeout.Token)
                .ConfigureAwait(false);
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchReferenceRetirementPreservesOtherContributorsAsync(bool replace, bool publish)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null, timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId root = new(kRootNodeId, ns);
            NodeId value = new(kValueNodeId, ns);
            var shared = new LocalReference(ObjectIds.ObjectsFolder, ReferenceTypeIds.HasComponent, false, root);
            var unique = new LocalReference(ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, false, value);
            const string firstUri = kModelNamespaceUri + ":FirstContributor";
            const string secondUri = kModelNamespaceUri + ":SecondContributor";
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            NodeManagerRegistration first;
            NodeManagerRegistration second;
            await using (IPreparedNodeManagerBatch added = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateReferenceContributorFactory(firstUri, [shared, shared, unique])),
                    NodeManagerBatchChange.Add(CreateReferenceContributorFactory(secondUri, [shared]))
                ], null, timeout.Token).ConfigureAwait(false))
            {
                NodeManagerBatchResult result = await added.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                first = result.Registrations[0];
                second = result.Registrations[1];
            }
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, value), Is.True);
            await using (IPreparedNodeManagerBatch retired = await lifecycle.PrepareAsync(
                [replace
                    ? NodeManagerBatchChange.Replace(first, CreateReferenceContributorFactory(firstUri, []))
                    : NodeManagerBatchChange.Remove(first)], null, timeout.Token).ConfigureAwait(false))
            {
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, value), Is.True);
                if (publish)
                {
                    NodeManagerBatchResult result = await retired.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, value), Is.EqualTo(!publish));
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.True);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            await AssertNativeReferenceAsync(
                session, ObjectIds.ObjectsFolder, root, ReferenceTypeIds.HasComponent, true, timeout.Token)
                .ConfigureAwait(false);
            if (publish)
            {
                await using IPreparedNodeManagerBatch removed = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(second)], null, timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await removed.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                await AssertNativeReferenceAsync(
                    session, ObjectIds.ObjectsFolder, root, ReferenceTypeIds.HasComponent, false, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.True);
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task PreparedBatchReferenceCallbacksDoNotPreventReadinessAndRetirementAsync()
        {
            TrackingLifecycleNodeManager? originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, manager => originalManager = manager),
                null)
                .ConfigureAwait(false);
            ReadinessLifecycleNodeManager? replacement = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    replacement = new ReadinessLifecycleNodeManager(
                        server, configuration, m_logger, kGeneration2Value);
                    return new ValueTask<IAsyncNodeManager>(replacement);
                });
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            NodeState objects = await GetObjectsReferenceOwnerAsync(CancellationToken.None).ConfigureAwait(false);
            var failure = new IOException("Committed reference callback failed.");
            NodeStateReferenceAdded? previous = objects.OnReferenceAdded;
            int callbacks = 0;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(original, factory.Object),
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ], null).ConfigureAwait(false);
            AssertLifecycleValue(originalManager);
            AssertLifecycleValue(replacement);
            NodeId root = new(kRootNodeId,
                (ushort)replacement.Server.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
            objects.OnReferenceAdded = (_, _, _, target) =>
            {
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(target, Is.EqualTo(new ExpandedNodeId(root)));
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.True);
                callbacks++;
                throw failure;
            };
            try
            {
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                await result.Retirement.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
                var cleanupFailure = RequireLifecycleValue(result.CleanupFailure) as AggregateException;
                Assert.That(RequireLifecycleValue(cleanupFailure).Flatten().InnerExceptions,
                    Is.EqualTo(new[] { failure }));
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(replacement.ReadinessCount, Is.EqualTo(1));
                Assert.That(replacement.ReadinessCompletedCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                Assert.That(result.Retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.True);
            }
            finally
            {
                objects.OnReferenceAdded = previous;
            }
        }

        [Test]
        public async Task PreparedBatchRejectsUnsupportedReferenceOwnersBeforeDecisionOrCallbacksAsync()
        {
            (Mock<IAsyncNodeManager> manager, BaseObjectState node, IAsyncNodeManagerFactory factory) =
                CreateUnsupportedReferenceFactory();
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], null).ConfigureAwait(false);
            NodeState objects = await GetObjectsReferenceOwnerAsync(CancellationToken.None).ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThatAsync(async () => await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }).ConfigureAwait(false), Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(8402, 1)), Is.False);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(8402, 1)), Is.False);
            manager.Verify(value => value.AddReferencesAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Preserves unrelated surviving-source writes and rejects writes overlapping a retiring contribution.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchReferenceReservationsRespectChangedContributionSourcesAsync(bool removeContributor)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null, timeout.Token).ConfigureAwait(false);
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            NodeId target = new("ConcurrentNotifier",
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            NodeId root = new(kRootNodeId, target.NamespaceIndex);
            var captured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var mutated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int armed = 0;
            Mock<IAsyncNodeManager> probe = CreateLifecycleNodeManager(kModelNamespaceUri + ":ReferenceMutationProbe");
            probe.As<IDisposable>();
            probe.Setup(value => value.GetManagerHandleAsync(ObjectIds.ObjectsFolder, It.IsAny<CancellationToken>()))
                .Returns(async (NodeId _, CancellationToken token) =>
                {
                    if (Interlocked.Exchange(ref armed, 0) != 0)
                    {
                        captured.TrySetResult(true);
                        await mutated.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                    return null!;
                });
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(probe.Object);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            ArrayOf<NodeManagerBatchChange> changes = removeContributor
                ? [NodeManagerBatchChange.Remove(registration), NodeManagerBatchChange.Add(factory.Object)]
                : [NodeManagerBatchChange.Add(factory.Object)];
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                changes, null, timeout.Token).ConfigureAwait(false);
            Task mutation = MutateAsync();
            int decisions = 0;
            try
            {
                Volatile.Write(ref armed, 1);
                ValueTask DecideAsync(CancellationToken _)
                {
                    decisions++;
                    return default;
                }
                if (removeContributor)
                {
                    await Assert.ThatAsync(() => prepared.CommitAsync(DecideAsync, timeout.Token).AsTask(),
                        Throws.TypeOf<InvalidOperationException>()
                            .With.Message.StartsWith("Serving references changed during preparation."))
                        .ConfigureAwait(false);
                    Assert.That(decisions, Is.Zero);
                    Assert.That(prepared.IsCommitted, Is.False);
                }
                else
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(DecideAsync, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(decisions, Is.EqualTo(1));
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(objects.ReferenceExists(ReferenceTypeIds.HasNotifier, false, target), Is.True,
                        "Publishing an unchanged contribution must not install a stale full reference image.");
                }
                await mutation.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root),
                    Is.EqualTo(!removeContributor));
                Assert.That(lifecycle.Registrations.Find(value => value.Id == registration.Id), Is.SameAs(registration));
            }
            finally
            {
                captured.TrySetResult(true);
                await mutation.WaitAsync(timeout.Token).ConfigureAwait(false);
                objects.RemoveReference(ReferenceTypeIds.HasNotifier, false, target);
                if (prepared.IsCommitted)
                {
                    NodeManagerRegistration published = lifecycle.Registrations.ToList()
                        .Single(value => ReferenceEquals(value.NodeManager, probe.Object));
                    await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, published, timeout.Token)
                        .ConfigureAwait(false);
                }
                await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, registration, timeout.Token)
                    .ConfigureAwait(false);
            }

            async Task MutateAsync()
            {
                await captured.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    if (removeContributor)
                    {
                        Assert.That(objects.RemoveReference(ReferenceTypeIds.Organizes, false, root), Is.True);
                    }
                    else
                    {
                        objects.AddReference(ReferenceTypeIds.HasNotifier, false, target);
                    }
                }
                finally
                {
                    mutated.TrySetResult(true);
                }
            }
        }

        [Test]
        public async Task PreparedBatchReferenceImagesSurviveOrdinaryRoutingChangesAsync()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], null).ConfigureAwait(false))
            {
                Assert.That((await prepared.CommitAsync(_ => default).ConfigureAwait(false)).CleanupFailure, Is.Null);
            }
            NodeState objects = await GetObjectsReferenceOwnerAsync(CancellationToken.None).ConfigureAwait(false);
            NodeId root = new(kRootNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            NodeManagerRegistration ordinary = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null).ConfigureAwait(false);
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.True);
            await m_server.NodeManagerLifecycle.RemoveAsync(ordinary, null).ConfigureAwait(false);
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, root), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchCandidateReferenceCallbacksWaitForPublicationAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            ushort candidateNamespace =
                m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            NodeId candidateRoot = new(kRootNodeId, candidateNamespace);
            NodeManagerRegistration source = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, _ => { }, kModelNamespaceUri, candidateRoot),
                null, timeout.Token).ConfigureAwait(false);
            NodeId sourceRoot = new(kRootNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri))], null, timeout.Token)
                .ConfigureAwait(false);
            object? handle = await prepared.Registrations[0].NodeManager
                .GetManagerHandleAsync(candidateRoot, timeout.Token)
                .ConfigureAwait(false);
            NodeState node = RequireLifecycleValue(handle as NodeHandle).Node;
            int callbacks = 0;
            node.OnReferenceAdded = (_, type, inverse, target) =>
            {
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(type, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(inverse, Is.False);
                Assert.That(target, Is.EqualTo(new ExpandedNodeId(sourceRoot)));
                callbacks++;
            };
            Assert.That(node.ReferenceExists(ReferenceTypeIds.HasComponent, false, sourceRoot), Is.False);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(_ =>
            {
                Assert.That(node.ReferenceExists(ReferenceTypeIds.HasComponent, false, sourceRoot), Is.False);
                Assert.That(callbacks, Is.Zero);
                if (rejectDecision)
                {
                    throw new IOException("Confirmed candidate-reference noncommit.");
                }
                return default;
            }, timeout.Token).AsTask();
            if (rejectDecision)
            {
                await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
            }
            else
            {
                Assert.That((await commit.ConfigureAwait(false)).CleanupFailure, Is.Null);
            }
            Assert.That(callbacks, Is.EqualTo(rejectDecision ? 0 : 1));
            Assert.That(node.ReferenceExists(ReferenceTypeIds.HasComponent, false, sourceRoot),
                Is.EqualTo(!rejectDecision));
            if (!rejectDecision)
            {
                await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(source)], null, timeout.Token).ConfigureAwait(false);
                Assert.That(
                    (await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false)).CleanupFailure,
                    Is.Null);
                Assert.That(node.ReferenceExists(ReferenceTypeIds.HasComponent, false, sourceRoot), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchReferencesUseAdaptedSynchronousOwnersAsync(bool publish)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                    new ValueTask<IAsyncNodeManager>(
                        new ReferenceSynchronousNodeManager(server, configuration).ToAsyncNodeManager()));
            await m_server.NodeManagerLifecycle.AddAsync(factory.Object, null, timeout.Token).ConfigureAwait(false);
            NodeId parent = new(kRootNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            ushort candidateNamespace =
                m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            NodeId root = new(kRootNodeId, candidateNamespace);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri, parent))], null, timeout.Token)
                .ConfigureAwait(false))
            {
                await AssertNativeReferenceAsync(
                    session, parent, root, ReferenceTypeIds.HasComponent, false, timeout.Token).ConfigureAwait(false);
                if (publish)
                {
                    Assert.That((await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false))
                        .CleanupFailure, Is.Null);
                }
            }
            await AssertNativeReferenceAsync(
                session, parent, root, ReferenceTypeIds.HasComponent, publish, timeout.Token).ConfigureAwait(false);
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        private static (Mock<IAsyncNodeManager> Manager, BaseObjectState Node, IAsyncNodeManagerFactory Factory)
            CreateUnsupportedReferenceFactory()
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.SetupGet(value => value.NamespaceUris).Returns(new[] { kModelNamespaceUri });
            var node = new BaseObjectState(null) { NodeId = ObjectIds.ObjectsFolder };
            manager.Setup(value => value.GetManagerHandleAsync(ObjectIds.ObjectsFolder, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(new NodeHandle(ObjectIds.ObjectsFolder, node)));
            manager.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback((IDictionary<NodeId, IList<IReference>> references, CancellationToken _) =>
                    MasterNodeManager.CreateExternalReference(
                        references, ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, false, new NodeId(8402, 1)));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(manager.Object));
            return (manager, node, factory.Object);
        }

        private async Task<NodeState> GetObjectsReferenceOwnerAsync(CancellationToken cancellationToken)
        {
            (object? handle, _) = await m_server.CurrentInstance.NodeManager.GetManagerHandleAsync(
                ObjectIds.ObjectsFolder, cancellationToken).ConfigureAwait(false);
            Assert.That(handle, Is.TypeOf<NodeHandle>());
            return RequireLifecycleValue(handle as NodeHandle).Node;
        }

        private IAsyncNodeManagerFactory CreateReferenceContributorFactory(
            string namespaceUri,
            ArrayOf<LocalReference> references)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                    new ValueTask<IAsyncNodeManager>(
                        new ReferenceContributorNodeManager(
                            server, configuration, m_logger, namespaceUri, references)));
            return factory.Object;
        }

        private IAsyncNodeManagerFactory CreateLifecycleCallbackNodeManagerFactory(
            string namespaceUri,
            Func<CancellationToken, ValueTask>? addNode,
            Func<CancellationToken, ValueTask>? sessionClosing)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                    new ValueTask<IAsyncNodeManager>(
                        new ReentrantLifecycleNodeManager(
                            server,
                            configuration,
                            m_logger,
                            namespaceUri,
                            addNode,
                            sessionClosing)));
            return factory.Object;
        }

        private static async Task AssertNativeReferenceAsync(
            Opc.Ua.Client.ISession session,
            NodeId source,
            NodeId target,
            NodeId referenceType,
            bool expected,
            CancellationToken cancellationToken)
        {
            BrowseResponse browse = await session.BrowseAsync(null, new ViewDescription(), 1,
                [
                    new BrowseDescription
                    {
                        NodeId = source,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = referenceType,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false);
            BrowseResult page = browse.Results[0];
            int count = 0;
            while (true)
            {
                Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                foreach (ReferenceDescription reference in page.References)
                {
                    if (reference.NodeId == new ExpandedNodeId(target))
                    {
                        Assert.That(reference.ReferenceTypeId, Is.EqualTo(referenceType));
                        Assert.That(reference.IsForward, Is.True);
                        count++;
                    }
                }
                if (page.ContinuationPoint.Length == 0)
                {
                    break;
                }
                BrowseNextResponse next = await session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], cancellationToken).ConfigureAwait(false);
                page = next.Results[0];
            }
            Assert.That(count, Is.EqualTo(expected ? 1 : 0));
            TranslateBrowsePathsToNodeIdsResponse translated = await session.TranslateBrowsePathsToNodeIdsAsync(null,
                [
                    new BrowsePath
                    {
                        StartingNode = source,
                        RelativePath = new RelativePath
                        {
                            Elements =
                            [
                                new RelativePathElement
                                {
                                    ReferenceTypeId = referenceType,
                                    IncludeSubtypes = false,
                                    IsInverse = false,
                                    TargetName = new QualifiedName(kRootBrowseName, target.NamespaceIndex)
                                }
                            ]
                        }
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(translated.Results[0].StatusCode,
                Is.EqualTo(expected ? StatusCodes.Good : StatusCodes.BadNoMatch));
            if (expected)
            {
                Assert.That(translated.Results[0].Targets.Count, Is.EqualTo(1));
                Assert.That(translated.Results[0].Targets[0].TargetId, Is.EqualTo(new ExpandedNodeId(target)));
                Assert.That(translated.Results[0].Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            }
        }

        /// <summary>
        /// Resolves an existing or absent replacement source through the native lazy-validation path.
        /// </summary>
        private sealed class LazyReferenceSourceNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates a replacement with an optional source and a backlink to a retiring target.
            /// </summary>
            public LazyReferenceSourceNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                NodeId targetId,
                bool sourceExists)
                : base(server, configuration, kSecondModelNamespaceUri)
            {
                m_targetId = targetId;
                m_sourceExists = sourceExists;
            }

            /// <summary>
            /// Gets the actual source, if this generation contains it.
            /// </summary>
            public NodeState? Source { get; private set; }

            /// <summary>
            /// Gets the number of source validations performed under owner admission.
            /// </summary>
            public int ValidationCount => Volatile.Read(ref m_validationCount);

            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                if (m_sourceExists)
                {
                    var source = new BaseObjectState(null)
                    {
                        NodeId = new NodeId(kRootNodeId, NamespaceIndexes[0]),
                        BrowseName = new QualifiedName(kRootBrowseName, NamespaceIndexes[0]),
                        DisplayName = LocalizedText.From(kRootBrowseName)
                    };
                    source.AddReference(ReferenceTypeIds.HasComponent, false, m_targetId);
                    source.AddReference(ReferenceTypeIds.HasComponent, false, ObjectIds.Server);
                    Source = source;
                    await AddPredefinedNodeAsync(SystemContext, source, cancellationToken).ConfigureAwait(false);
                }
            }

            /// <inheritdoc/>
            public override ValueTask<object> GetManagerHandleAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (nodeId == new NodeId(kRootNodeId, NamespaceIndexes[0]))
                {
                    var handle = new NodeHandle { NodeId = nodeId, Validated = false };
                    m_handles.Enqueue(handle);
                    return new ValueTask<object>(handle);
                }
                return base.GetManagerHandleAsync(nodeId, cancellationToken);
            }

            /// <summary>
            /// Resolves issued placeholders so a failing regression cannot strand cached retirement cleanup.
            /// </summary>
            public void ValidateIssuedHandlesForCleanup(NodeState source)
            {
                foreach (NodeHandle handle in m_handles)
                {
                    handle.Node = source;
                    handle.Validated = true;
                }
            }

            /// <inheritdoc/>
            protected override ValueTask<NodeState> ValidateNodeAsync(
                ServerSystemContext context,
                NodeHandle handle,
                IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_validationCount);
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(this).Gate.ActiveCount, Is.GreaterThan(0));
                return new ValueTask<NodeState>(Source is { } source
                    ? ValidationComplete(context, handle, source, cache)
                    : null!);
            }

            /// <summary>Identifies the retiring target of the source's runtime backlink.</summary>
            private readonly NodeId m_targetId;

            /// <summary>Determines whether the replacement actually contains the source.</summary>
            private readonly bool m_sourceExists;

            /// <summary>Retains issued placeholders until regression cleanup releases any cached deletion plan.</summary>
            private readonly ConcurrentQueue<NodeHandle> m_handles = new();

            /// <summary>Counts source metadata validations.</summary>
            private int m_validationCount;
        }

        /// <summary>
        /// Adds controllable service callbacks to exercise lifecycle re-entry from host callbacks.
        /// </summary>
        private sealed class ReentrantLifecycleNodeManager : NodeManagementLifecycleNodeManager
        {
            /// <summary>
            /// Creates a node manager with optional AddNode and SessionClosing callbacks.
            /// </summary>
            public ReentrantLifecycleNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                string namespaceUri,
                Func<CancellationToken, ValueTask>? addNode,
                Func<CancellationToken, ValueTask>? sessionClosing)
                : base(server, configuration, logger, namespaceUri)
            {
                m_addNode = addNode;
                m_sessionClosing = sessionClosing;
            }

            /// <inheritdoc/>
            public override async ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
                OperationContext context,
                AddNodesItem item,
                CancellationToken cancellationToken = default)
            {
                if (m_addNode is { } addNode)
                {
                    await addNode(cancellationToken).ConfigureAwait(false);
                }
                return await base.AddNodeAsync(context, item, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public override async ValueTask SessionClosingAsync(
                OperationContext context,
                NodeId sessionId,
                bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
            {
                if (m_sessionClosing is { } sessionClosing)
                {
                    await sessionClosing(cancellationToken).ConfigureAwait(false);
                }
                await base.SessionClosingAsync(
                    context, sessionId, deleteSubscriptions, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Nested lifecycle operation invoked from AddNodeAsync.
            /// </summary>
            private readonly Func<CancellationToken, ValueTask>? m_addNode;

            /// <summary>
            /// Nested lifecycle operation invoked from SessionClosingAsync.
            /// </summary>
            private readonly Func<CancellationToken, ValueTask>? m_sessionClosing;
        }

        private sealed class ReferenceContributorNodeManager : NodeManagementLifecycleNodeManager
        {
            public ReferenceContributorNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                string namespaceUri,
                ArrayOf<LocalReference> references)
                : base(server, configuration, logger, namespaceUri)
            {
                m_references = references;
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                foreach (LocalReference reference in m_references)
                {
                    MasterNodeManager.CreateExternalReference(
                        externalReferences, reference.SourceId, reference.ReferenceTypeId,
                        reference.IsInverse, reference.TargetId);
                }
            }

            private readonly ArrayOf<LocalReference> m_references;
        }

        private sealed partial class ReferenceSynchronousNodeManager : CustomNodeManager2
        {
            public ReferenceSynchronousNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(server, configuration, kModelNamespaceUri)
            {
            }

            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId(kRootNodeId, NamespaceIndexes[0]),
                    BrowseName = new QualifiedName(kRootBrowseName, NamespaceIndexes[0]),
                    DisplayName = LocalizedText.From(kRootBrowseName)
                };
                root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                AddPredefinedNode(SystemContext, root);
                MasterNodeManager.CreateExternalReference(
                    externalReferences, ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, false, root.NodeId);
            }
        }
    }
}
