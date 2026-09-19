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
            NodeStateReferenceAdded previousCallback = objects.OnReferenceAdded;
            objects.OnReferenceAdded = (_, _, _, target) => added.Add(target);
            try
            {
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], timeout.Token)
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
        public async Task PreparedBatchReferenceImageIsRetainedByAnInflightNativeRequestAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            TrackingLifecycleNodeManager survivor = null;
            await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, manager => survivor = manager),
                null, timeout.Token).ConfigureAwait(false);
            NodeState objects = await GetObjectsReferenceOwnerAsync(timeout.Token).ConfigureAwait(false);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            NodeId root = new(kRootNodeId, ns);
            NodeId value = new(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool before = true;
            bool after = true;
            survivor.ReadCallbackNodeId = value;
            survivor.ReadCallback = async token =>
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
            Assert.That(after, Is.False, "The same physical owner must use the request's original reference image.");
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
                ], timeout.Token).ConfigureAwait(false))
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
                    : NodeManagerBatchChange.Remove(first)], timeout.Token).ConfigureAwait(false))
            {
                Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, value), Is.True);
                if (publish)
                {
                    NodeManagerBatchResult result = await retired.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Retired, Is.EqualTo(1u));
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
                    [NodeManagerBatchChange.Remove(second)], timeout.Token).ConfigureAwait(false);
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
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, manager => originalManager = manager),
                null)
                .ConfigureAwait(false);
            ReadinessLifecycleNodeManager replacement = null;
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
            NodeStateReferenceAdded previous = objects.OnReferenceAdded;
            int callbacks = 0;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(original, factory.Object),
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ]).ConfigureAwait(false);
            NodeId root = new(kRootNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
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
                Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
                Assert.That(((AggregateException)result.CleanupFailure).Flatten().InnerExceptions,
                    Is.EqualTo(new[] { failure }));
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(replacement.ReadinessCount, Is.EqualTo(1));
                Assert.That(replacement.ReadinessCompletedCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                Assert.That(result.Retired, Is.EqualTo(1u));
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)]).ConfigureAwait(false);
            NodeState objects = await GetObjectsReferenceOwnerAsync(CancellationToken.None).ConfigureAwait(false);
            int decisions = 0;
            Assert.ThrowsAsync<NotSupportedException>(async () => await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }).ConfigureAwait(false));
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
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))]).ConfigureAwait(false))
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
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri))], timeout.Token)
                .ConfigureAwait(false);
            object handle = await prepared.Registrations[0].NodeManager
                .GetManagerHandleAsync(candidateRoot, timeout.Token)
                .ConfigureAwait(false);
            var node = ((NodeHandle)handle).Node;
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
                    [NodeManagerBatchChange.Remove(source)], timeout.Token).ConfigureAwait(false);
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
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri, parent))], timeout.Token)
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

        private async Task<NodeState> GetObjectsReferenceOwnerAsync(CancellationToken cancellationToken)
        {
            (object handle, _) = await m_server.CurrentInstance.NodeManager.GetManagerHandleAsync(
                ObjectIds.ObjectsFolder, cancellationToken).ConfigureAwait(false);
            Assert.That(handle, Is.TypeOf<NodeHandle>());
            return ((NodeHandle)handle).Node;
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

        private sealed class ReferenceSynchronousNodeManager : CustomNodeManager2
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
