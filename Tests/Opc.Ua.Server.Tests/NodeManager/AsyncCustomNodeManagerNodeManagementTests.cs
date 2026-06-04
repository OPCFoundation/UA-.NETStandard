/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000

#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Unit tests for the <see cref="INodeManagementAsyncNodeManager"/>
    /// implementation on <see cref="AsyncCustomNodeManager"/>.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("NodeManagement")]
    [Parallelizable(ParallelScope.All)]
    public class AsyncCustomNodeManagerNodeManagementTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/NodeManagement/";

        [Test]
        public void AllowNodeManagement_DefaultsToFalse()
        {
            using Harness h = CreateHarness();
            Assert.That(((INodeManagementAsyncNodeManager)h.Manager).AllowNodeManagement, Is.False);
        }

        [Test]
        public void AllowNodeManagement_IsTrueWhenOptedIn()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            Assert.That(((INodeManagementAsyncNodeManager)h.Manager).AllowNodeManagement, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_BrowseNameInvalid_ReturnsBadBrowseNameInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new AddNodesItem
            {
                ParentNodeId = ObjectIds.ObjectsFolder,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = default,
                NodeClass = NodeClass.Object,
                RequestedNewNodeId = new NodeId("MyObj", ns)
            };

            (ServiceResult result, NodeId added) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameInvalid));
            Assert.That(added.IsNull, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_ParentNodeIdInvalid_ReturnsBadParentNodeIdInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new AddNodesItem
            {
                ParentNodeId = default,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("MyObj", ns),
                NodeClass = NodeClass.Object,
                RequestedNewNodeId = new NodeId("MyObj", ns)
            };

            (ServiceResult result, _) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadParentNodeIdInvalid));
        }

        [Test]
        public async Task AddNodeAsync_RequestedNewNodeIdInOtherNamespace_ReturnsBadNodeIdRejectedAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            // Add a parent locally so the inverse-edge path runs but the
            // RequestedNewNodeId is in a foreign namespace.
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Child", ns),
                NodeClass = NodeClass.Object,
                RequestedNewNodeId = new NodeId("Foreign", 0) // ns=0 not in this manager
            };

            (ServiceResult result, _) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdRejected));
        }

        [Test]
        public async Task AddNodeAsync_RequestedNodeIdAlreadyExists_ReturnsBadNodeIdExistsAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            NodeId existingId = await h.AddObjectAsync("Existing").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Child", ns),
                NodeClass = NodeClass.Object,
                RequestedNewNodeId = existingId
            };

            (ServiceResult result, _) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdExists));
        }

        [Test]
        public async Task AddNodeAsync_DuplicateBrowseNameUnderParent_ReturnsBadBrowseNameDuplicatedAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            // First child succeeds.
            var first = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("SameName", ns),
                NodeClass = NodeClass.Object
            };

            (ServiceResult firstResult, NodeId firstId) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, first).ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(firstResult), Is.True);
            Assume.That(firstId.IsNull, Is.False);

            // Second child under same parent with same browse name fails.
            var second = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("SameName", ns),
                NodeClass = NodeClass.Object
            };

            (ServiceResult secondResult, NodeId secondId) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, second).ConfigureAwait(false);

            Assert.That(secondResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(secondId.IsNull, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_AllocatesNewNodeIdWhenRequestedIsNullAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("AutoNode", ns),
                NodeClass = NodeClass.Object
            };

            (ServiceResult result, NodeId added) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(added.IsNull, Is.False);
            Assert.That(added.NamespaceIndex, Is.EqualTo(ns));
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(added), Is.True);
        }

        [Test]
        public async Task AddNodeAsync_HonoursRequestedNewNodeIdAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            var requested = new NodeId("ExplicitId", ns);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("ExplicitId", ns),
                NodeClass = NodeClass.Object,
                RequestedNewNodeId = requested
            };

            (ServiceResult result, NodeId added) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(added, Is.EqualTo(requested));
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(requested), Is.True);
        }

        [Test]
        public async Task AddNodeAsync_VariableWithAttributes_AppliesAttributesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var attributes = new VariableAttributes
            {
                SpecifiedAttributes =
                    (uint)NodeAttributesMask.DisplayName |
                    (uint)NodeAttributesMask.DataType |
                    (uint)NodeAttributesMask.ValueRank |
                    (uint)NodeAttributesMask.AccessLevel,
                DisplayName = new LocalizedText("Test Variable"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead
            };

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                BrowseName = new QualifiedName("AttrVar", ns),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(attributes)
            };

            (ServiceResult result, NodeId added) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            var variable = h.Manager.PredefinedNodes[added] as BaseDataVariableState;
            Assert.That(variable, Is.Not.Null);
            Assert.That(variable!.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(variable.DisplayName.Text, Is.EqualTo("Test Variable"));
        }

        [Test]
        public async Task AddNodeAsync_UnsupportedNodeClass_ReturnsBadNodeClassInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                BrowseName = new QualifiedName("MethodNode", ns),
                NodeClass = NodeClass.Method
            };

            (ServiceResult result, _) = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeClassInvalid));
        }

        [Test]
        public async Task DeleteNodeAsync_UnknownNodeId_ReturnsBadNodeIdUnknownAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new DeleteNodesItem
            {
                NodeId = new NodeId("DoesNotExist", ns),
                DeleteTargetReferences = false
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task DeleteNodeAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);

            var item = new DeleteNodesItem
            {
                NodeId = default,
                DeleteTargetReferences = false
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task DeleteNodeAsync_RemovesNodeFromPredefinedNodesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            NodeId nodeId = await h.AddObjectAsync("Deletable").ConfigureAwait(false);

            var item = new DeleteNodesItem
            {
                NodeId = nodeId,
                DeleteTargetReferences = false
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(nodeId), Is.False);
        }

        [Test]
        public async Task DeleteNodeAsync_DeleteTargetReferencesTrue_RequestsRemoveReferencesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            // Set up a node with an inverse reference so RemovePredefinedNodeAsync
            // collects a LocalReference to remove on the target.
            NodeId nodeId = await h.AddObjectAsync("WithRefs").ConfigureAwait(false);
            NodeState node = h.Manager.PredefinedNodes[nodeId];
            // Add an inverse hierarchical reference pointing at a foreign target.
            var foreign = new NodeId("ForeignTarget", (ushort)(ns + 100));
            node.AddReference(ReferenceTypeIds.Organizes, true, foreign);

            var item = new DeleteNodesItem
            {
                NodeId = nodeId,
                DeleteTargetReferences = true
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            h.MockMasterNodeManager.Verify(
                m => m.RemoveReferencesAsync(
                    It.Is<List<LocalReference>>(l => l.Count > 0),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Test]
        public async Task DeleteNodeAsync_DeleteTargetReferencesFalse_DoesNotRequestRemoveReferencesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId nodeId = await h.AddObjectAsync("KeepRefs").ConfigureAwait(false);
            NodeState node = h.Manager.PredefinedNodes[nodeId];
            var foreign = new NodeId("ForeignTarget", (ushort)(ns + 100));
            node.AddReference(ReferenceTypeIds.Organizes, true, foreign);

            var item = new DeleteNodesItem
            {
                NodeId = nodeId,
                DeleteTargetReferences = false
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            h.MockMasterNodeManager.Verify(
                m => m.RemoveReferencesAsync(
                    It.IsAny<List<LocalReference>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddReferenceAsync_SourceUnknown_ReturnsBadSourceNodeIdInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new AddReferencesItem
            {
                SourceNodeId = new NodeId("DoesNotExist", ns),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = new NodeId("Anything", ns)
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSourceNodeIdInvalid));
        }

        [Test]
        public async Task AddReferenceAsync_DuplicateReference_ReturnsBadDuplicateReferenceNotAllowedAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);
            var targetId = new NodeId("Target", ns);

            var item = new AddReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = targetId
            };

            ServiceResult first = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(first), Is.True);

            ServiceResult second = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(second.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDuplicateReferenceNotAllowed));
        }

        [Test]
        public async Task AddReferenceAsync_AddsReferenceToSourceNodeAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);
            var targetId = new NodeId("Target", ns);

            var item = new AddReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = targetId
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            NodeState source = h.Manager.PredefinedNodes[sourceId];
            Assert.That(source.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId), Is.True);
        }

        [Test]
        public async Task DeleteReferenceAsync_NoMatch_ReturnsBadNoMatchAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);

            var item = new DeleteReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = new NodeId("NoSuchTarget", ns)
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .DeleteReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoMatch));
        }

        [Test]
        public async Task DeleteReferenceAsync_RemovesExistingReferenceAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);
            var targetId = new NodeId("Target", ns);
            h.Manager.PredefinedNodes[sourceId].AddReference(ReferenceTypeIds.Organizes, false, targetId);

            var item = new DeleteReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = targetId
            };

            ServiceResult result = await ((INodeManagementAsyncNodeManager)h.Manager)
                .DeleteReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(
                h.Manager.PredefinedNodes[sourceId].ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                Is.False);
        }

        private static Harness CreateHarness(bool allowNodeManagement = false)
        {
            var mockServer = new Mock<IServerInternal>();
            var mockLogger = new Mock<ILogger>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            mockSession.Setup(s => s.PreferredLocales).Returns([]);

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfigurationNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(monitoredItemQueueFactory);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            NodeManagementTestNodeManager manager = allowNodeManagement
                ? new OptedInTestNodeManager(mockServer.Object, configuration, mockLogger.Object, TestNamespaceUri)
                : new NodeManagementTestNodeManager(mockServer.Object, configuration, mockLogger.Object, TestNamespaceUri);

            // Ensure cross-NodeManager add-reference path can find local nodes via the master.
            mockMasterNodeManager
                .Setup(m => m.AddReferencesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<IList<IReference>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            mockMasterNodeManager
                .Setup(m => m.RemoveReferencesAsync(
                    It.IsAny<List<LocalReference>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            var opContext = new OperationContext(
                new RequestHeader(), null!, RequestType.AddNodes, RequestLifetime.None, mockSession.Object);

            return new Harness(manager, serverSystemContext, opContext, monitoredItemQueueFactory, mockMasterNodeManager);
        }

        private sealed class Harness : System.IDisposable
        {
            public NodeManagementTestNodeManager Manager { get; }
            public ServerSystemContext Context { get; }
            public OperationContext OperationContext { get; }
            public Mock<IMasterNodeManager> MockMasterNodeManager { get; }

            private readonly MonitoredItemQueueFactory m_queueFactory;

            public Harness(
                NodeManagementTestNodeManager manager,
                ServerSystemContext context,
                OperationContext operationContext,
                MonitoredItemQueueFactory queueFactory,
                Mock<IMasterNodeManager> mockMasterNodeManager)
            {
                Manager = manager;
                Context = context;
                OperationContext = operationContext;
                m_queueFactory = queueFactory;
                MockMasterNodeManager = mockMasterNodeManager;
            }

            public async ValueTask<NodeId> AddObjectAsync(string name)
            {
                ushort ns = Manager.NamespaceIndexes[0];
                var obj = new BaseObjectState(null);
                obj.CreateAsPredefinedNode(Context);
                obj.NodeId = new NodeId(name, ns);
                obj.BrowseName = new QualifiedName(name, ns);
                await Manager.AddPredefinedNodeAsyncPublic(Context, obj).ConfigureAwait(false);
                return obj.NodeId;
            }

            public void Dispose()
            {
                m_queueFactory.Dispose();
                Manager.Dispose();
            }
        }

        private class NodeManagementTestNodeManager : AsyncCustomNodeManager
        {
            public NodeManagementTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

            public ValueTask AddPredefinedNodeAsyncPublic(ISystemContext context, NodeState node, CancellationToken ct = default)
            {
                return AddPredefinedNodeAsync(context, node, ct);
            }
        }

        private sealed class OptedInTestNodeManager : NodeManagementTestNodeManager
        {
            public OptedInTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public override bool AllowNodeManagement => true;
        }
    }
}
