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
