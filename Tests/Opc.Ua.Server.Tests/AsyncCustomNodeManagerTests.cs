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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture(AsyncCustomNodeManagerType.MonitoredNodeMonitoredItemManager)]
    [TestFixture(AsyncCustomNodeManagerType.SamplingGroupMonitoredItemManager)]
    [Category("AsyncCustomNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class AsyncCustomNodeManagerTests
    {
        private Mock<IServerInternal> m_mockServer;
        private ApplicationConfiguration m_configuration;
        private Mock<ILogger> m_mockLogger;
        private Mock<IMasterNodeManager> m_mockMasterNodeManager;
        private Mock<ISession> m_mockSession;
        private ServerSystemContext m_serverSystemContext;
        private NamespaceTable m_namespaceTable;
        private readonly string m_testNamespaceUri = "http://test.org/UA/Data/";
        private readonly bool m_useSamplingGroups;
        private static readonly double[] s_value = [10.0, 200.0, 30.0];
        private static readonly double[] s_expected = [10.0, 20.0, 30.0];

        public enum AsyncCustomNodeManagerType
        {
            MonitoredNodeMonitoredItemManager,
            SamplingGroupMonitoredItemManager
        }

        public AsyncCustomNodeManagerTests(AsyncCustomNodeManagerType managerType)
        {
            m_useSamplingGroups = managerType == AsyncCustomNodeManagerType.SamplingGroupMonitoredItemManager;
        }

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_mockLogger = new Mock<ILogger>();
            m_mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            m_mockSession = new Mock<ISession>();
            m_mockSession.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            m_mockSession.Setup(s => s.PreferredLocales).Returns([]);

            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(m_testNamespaceUri);

            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            m_mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(m_namespaceTable));
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.NodeManager).Returns(m_mockMasterNodeManager.Object);
            m_mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfigurationNodeManager.Object);

            // Mock Telemetry
            var mockTelemetry = new Mock<ITelemetryContext>();
            m_mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            m_mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(new MonitoredItemQueueFactory(mockTelemetry.Object));

            // Setup DefaultSystemContext 
            m_serverSystemContext = new ServerSystemContext(m_mockServer.Object);
            m_mockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };
        }

        [Test]
        public void Constructor_SetsPropertiesCorrectly()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            Assert.That(manager.MaxQueueSize, Is.EqualTo(100));
            Assert.That(manager.MaxDurableQueueSize, Is.EqualTo(200));
            Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(1));
            Assert.That(manager.NamespaceUris, Contains.Item(m_testNamespaceUri));
            Assert.That(manager.Logger, Is.EqualTo(m_mockLogger.Object));
            Assert.That(manager.SyncNodeManager, Is.Not.Null);
            Assert.That(manager.SystemContext.NodeIdFactory, Is.SameAs(manager));
        }

        [Test]
        public void NodeIDFactoryGeneratesNodesInTheRightNamespaceWithoutDuplicates()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            var generatedNodeIds = new HashSet<NodeId>(NodeIdComparer.Default);

            for (int i = 0; i < 100; i++)
            {
                var node = new BaseObjectState(null);
                NodeId nodeId = manager.New(context, node);

                Assert.That(nodeId, Is.Not.Null);
                Assert.That(nodeId, Is.Not.EqualTo(NodeId.Null));
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(manager.NamespaceIndexes[0]));
                Assert.That(generatedNodeIds.Add(nodeId), Is.True, "Duplicate NodeId generated");
            }
        }

        [Test]
        public async Task FindPredefinedNode_ReturnsNodeOnlyWhenTypeMatchesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("FindNode", nsIdx);
            baseObject.BrowseName = new QualifiedName("FindNode", nsIdx);

            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            BaseObjectState matching = manager.FindPredefinedNode<BaseObjectState>(baseObject.NodeId);
            Assert.That(matching, Is.SameAs(baseObject));

            BaseDataVariableState nonMatching = manager.FindPredefinedNode<BaseDataVariableState>(baseObject.NodeId);
            Assert.That(nonMatching, Is.Null);

            BaseObjectState nullResult = manager.FindPredefinedNode<BaseObjectState>(NodeId.Null);
            Assert.That(nullResult, Is.Null);
        }

        [Test]
        public async Task CreateNodeAsync_AddsNodeToPredefinedNodesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            baseObject.ReferenceTypeId = ReferenceTypeIds.Organizes;

            // Act
            NodeId resultNodeId = await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            // Assert
            Assert.That(resultNodeId, Is.EqualTo(baseObject.NodeId));
            NodeState storedNode = manager.Find(baseObject.NodeId);
            Assert.That(storedNode, Is.Not.Null);
            Assert.That(storedNode, Is.SameAs(baseObject));
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.True);
            Assert.That(baseObject.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
        }

        [Test]
        public async Task DeleteNodeAsync_RemovesNodeFromPredefinedNodesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);

            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);
            Assert.That(manager.Find(baseObject.NodeId), Is.Not.Null);

            // Act
            bool result = await manager.DeleteNodeAsync(context, baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(manager.Find(baseObject.NodeId), Is.Null);
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.False);
            bool secondResult = await manager.DeleteNodeAsync(context, baseObject.NodeId).ConfigureAwait(false);
            Assert.That(secondResult, Is.False);
        }

        [Test]
        public async Task CreateAddressSpaceAsync_LoadsNodesFromOverrideAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var folder = new FolderState(null);
            folder.CreateAsPredefinedNode(manager.SystemContext);
            folder.NodeId = new NodeId("Folder", nsIdx);
            folder.BrowseName = new QualifiedName("Folder", nsIdx);
            folder.DisplayName = new LocalizedText("Folder");

            manager.NodesToLoad = [folder];
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();

            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            Assert.That(manager.PredefinedNodes.ContainsKey(folder.NodeId), Is.True);
        }

        [Test]
        public async Task DeleteAddressSpaceAsync_DisposesAllNodesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(context);
            node.NodeId = new NodeId("Disposable", nsIdx);
            node.BrowseName = new QualifiedName("Disposable", nsIdx);

            await manager.AddNodeAsync(context, default, node).ConfigureAwait(false);
            Assert.That(manager.PredefinedNodes.ContainsKey(node.NodeId), Is.True);

            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);

            Assert.That(manager.PredefinedNodes, Is.Empty);
        }

        [Test]
        public async Task GetManagerHandleAsync_ReturnsHandleForExistingNodeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            baseObject.WriteMask = AttributeWriteMask.None;

            NodeId nodeID = await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            // Act
            object handle = await manager.GetManagerHandleAsync(baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(nodeID, Is.Not.EqualTo(NodeId.Null));
            Assert.That(handle, Is.InstanceOf<NodeHandle>());
            var nodeHandle = handle as NodeHandle;
            Assert.That(nodeHandle.NodeId, Is.EqualTo(baseObject.NodeId));
            Assert.That(nodeHandle.Node, Is.SameAs(baseObject));
            Assert.That(nodeHandle.Validated, Is.True);
            object invalidHandle = await manager.GetManagerHandleAsync(ObjectIds.Server).ConfigureAwait(false);
            Assert.That(invalidHandle, Is.Null);
        }

        [Test]
        public async Task GetNodeMetadataAsync_ReturnsMetadataForNodeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);

            variable.NodeId = new NodeId("MetaVar", nsIdx);
            variable.BrowseName = new QualifiedName("MetaVar", nsIdx);
            variable.DisplayName = new LocalizedText("MetaVar");
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.Value = 10;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(variable.NodeId).ConfigureAwait(false);

            NodeMetadata metadata = await manager.GetNodeMetadataAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read),
                handle,
                BrowseResultMask.All).ConfigureAwait(false);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(metadata.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(metadata.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(metadata.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public async Task ReadAsync_ReadsValueFromNodeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 42;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            variable.Value = 42;

            var readValueId = new ReadValueId
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value
            };
            var readValueId2 = new ReadValueId
            {
                NodeId = ObjectIds.Server,
                AttributeId = Attributes.Value
            };
            var nodesToRead = new List<ReadValueId> { readValueId, readValueId2 };
            var values = new List<DataValue> { null };
            var errors = new List<ServiceResult> { null };

            // Act
            await manager.ReadAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read),
                0,
                nodesToRead,
                values,
                errors).ConfigureAwait(false);

            // Assert
            Assert.That(nodesToRead[0].Processed, Is.True);
            //Node from other Namespace shall not be processed by this manager
            Assert.That(nodesToRead[1].Processed, Is.False);
            Assert.That(errors[0], Is.Not.Null);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(values[0].Value, Is.EqualTo(42));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].ServerTimestamp, Is.Not.EqualTo(DateTime.MinValue));
            Assert.That(values[0].ServerTimestamp, Is.EqualTo(values[0].SourceTimestamp));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task TranslateBrowsePathAsync_ResolvesTargetsAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("TranslateParent", nsIdx);
            parent.BrowseName = new QualifiedName("TranslateParent", nsIdx);
            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);

            var child = new BaseObjectState(null);
            child.CreateAsPredefinedNode(context);
            child.NodeId = new NodeId("TranslateChild", nsIdx);
            child.BrowseName = new QualifiedName("TranslateChild", nsIdx);
            await manager.AddNodeAsync(context, parent.NodeId, child).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(parent.NodeId).ConfigureAwait(false);
            var targetIds = new List<ExpandedNodeId>();
            var unresolved = new List<NodeId>();

            var relativePath = new RelativePathElement
            {
                IncludeSubtypes = true,
                IsInverse = false,
                TargetName = child.BrowseName
            };

            await manager.TranslateBrowsePathAsync(
                new OperationContext(new RequestHeader(), null, RequestType.TranslateBrowsePathsToNodeIds),
                handle,
                relativePath,
                targetIds,
                unresolved).ConfigureAwait(false);

            Assert.That(targetIds, Has.Count.EqualTo(1));
            Assert.That(targetIds[0], Is.EqualTo(new ExpandedNodeId(child.NodeId)));
            Assert.That(unresolved, Is.Empty);
        }

        [Test]
        public async Task BrowseAsync_ReturnsChildReferencesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("Parent", nsIdx);
            parent.BrowseName = new QualifiedName("Parent", nsIdx);
            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);

            var child = new BaseObjectState(null);
            child.CreateAsPredefinedNode(context);
            child.NodeId = new NodeId("Child", nsIdx);
            child.BrowseName = new QualifiedName("Child", nsIdx);
            await manager.AddNodeAsync(context, parent.NodeId, child).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(parent.NodeId).ConfigureAwait(false);
            var continuationPoint = new ContinuationPoint
            {
                NodeToBrowse = handle,
                Manager = manager,
                View = new ViewDescription(),
                BrowseDirection = BrowseDirection.Forward,
                IncludeSubtypes = true,
                ResultMask = BrowseResultMask.All
            };

            var references = new List<ReferenceDescription>();

            ContinuationPoint result = await manager.BrowseAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Browse),
                continuationPoint,
                references).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].BrowseName, Is.EqualTo(child.BrowseName));
            Assert.That(references[0].NodeId, Is.EqualTo(new ExpandedNodeId(child.NodeId)));
        }

        [Test]
        public async Task WriteAsync_WritesValueToNodeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 0;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(99))
            };

            var writeValue2 = new WriteValue
            {
                NodeId = ObjectIds.Server,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(99))
            };
            var nodesToWrite = new List<WriteValue> { writeValue, writeValue2 };
            var errors = new List<ServiceResult> { null };

            // Act
            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write),
                nodesToWrite,
                errors).ConfigureAwait(false);

            // Assert
            Assert.That(nodesToWrite[0].Processed, Is.True);
            //Node from other Namespace shall not be processed by this manager
            Assert.That(nodesToWrite[1].Processed, Is.False);
            Assert.That(errors[0], Is.Not.Null);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(variable.Value, Is.EqualTo(99));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task WriteAsync_WritesOutOfRangeScalarValueToAnalogItemReturnsBadOutOfRangeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new AnalogItemState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("AnalogScalarVar", nsIdx);
            variable.BrowseName = new QualifiedName("AnalogScalarVar", nsIdx);
            variable.Value = 50.0;
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange = new PropertyState<Range>(variable)
            {
                Value = new Range { Low = 0.0, High = 100.0 }
            };

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(200.0)) // out of range (> 100)
            };
            var nodesToWrite = new List<WriteValue> { writeValue };
            var errors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write),
                nodesToWrite,
                errors).ConfigureAwait(false);

            Assert.That(nodesToWrite[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(variable.Value, Is.EqualTo(50.0));
        }

        [Test]
        public async Task WriteAsync_WritesOutOfRangeArrayValueToAnalogItemReturnsBadOutOfRangeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new AnalogItemState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("AnalogArrayVar", nsIdx);
            variable.BrowseName = new QualifiedName("AnalogArrayVar", nsIdx);
            variable.Value = new double[] { 10.0, 20.0, 30.0 };
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.OneDimension;
            variable.ArrayDimensions = new ReadOnlyList<uint>([0]);
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange = new PropertyState<Range>(variable)
            {
                Value = new Range { Low = 0.0, High = 100.0 }
            };

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // 200.0 is above High (100.0)
            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(s_value))
            };
            var nodesToWrite = new List<WriteValue> { writeValue };
            var errors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write),
                nodesToWrite,
                errors).ConfigureAwait(false);

            Assert.That(nodesToWrite[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That((double[])variable.Value, Is.EqualTo(s_expected));
        }

        [Test]
        public async Task WriteAsync_PublishesValueToMonitoredItemQueueAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVarQueue", nsIdx);
            variable.BrowseName = new QualifiedName("MyVarQueue", nsIdx);
            variable.Value = 0;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            variable.Value = 0;

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 0, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var createErrors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                createErrors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(createErrors[0]), Is.True);
            var monitoredItem = monitoredItems[0] as IDataChangeMonitoredItem;
            Assert.That(monitoredItem, Is.Not.Null);

            var writeValue = new WriteValue
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(123))
            };

            var nodesToWrite = new List<WriteValue> { writeValue };
            var writeErrors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write),
                nodesToWrite,
                writeErrors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(writeErrors[0]), Is.True);
            Assert.That(variable.Value, Is.EqualTo(123));

            Assert.That(monitoredItem.IsReadyToPublish, Is.True);

            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            bool hadMore = monitoredItem.Publish(
                new OperationContext(new RequestHeader(), null, RequestType.Publish),
                notifications,
                diagnostics,
                10,
                m_mockLogger.Object);

            Assert.That(hadMore, Is.False);
            if (!m_useSamplingGroups)
            {
                // MonitoredNodeMonitoredItemManager propagates writes immediately via ClearChangeMasks
                Assert.That(notifications.Count, Is.EqualTo(2));
                MonitoredItemNotification notification = notifications.Dequeue();
                MonitoredItemNotification notificationAfterWrite = notifications.Dequeue();
                Assert.That(notification.Value.Value, Is.EqualTo(0));
                Assert.That(notificationAfterWrite.Value.Value, Is.EqualTo(123));
                Assert.That(diagnostics.Count, Is.EqualTo(2));
                Assert.That(monitoredItem.IsReadyToPublish, Is.False);
            }
            // For SamplingGroupMonitoredItemManager the background sampling timer fires
            // asynchronously, so notification count after a write is timing-dependent.
            // The write value is verified above via variable.Value == 123.
        }

        [Test]
        public async Task WriteEngineeringUnitsAsync_PublishesSemanticsChangedValueToMonitoredItemQueueAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new AnalogItemState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVarQueue", nsIdx);
            variable.BrowseName = new QualifiedName("MyVarQueue", nsIdx);
            variable.Value = 0;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            var euProperty = new PropertyState(null);
            euProperty.CreateAsPredefinedNode(context);
            euProperty.BrowseName = QualifiedName.From(BrowseNames.EURange);
            euProperty.Value = 0;
            euProperty.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            euProperty.AccessLevel = AccessLevels.CurrentReadOrWrite;
            euProperty.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            variable.AddChild(euProperty);

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            NodeId propertyNodeID = euProperty.NodeId;

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 0, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var createErrors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                createErrors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(createErrors[0]), Is.True);
            var monitoredItem = monitoredItems[0] as IDataChangeMonitoredItem;
            Assert.That(monitoredItem, Is.Not.Null);

            var writeValue = new WriteValue
            {
                NodeId = propertyNodeID,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(123))
            };

            var nodesToWrite = new List<WriteValue> { writeValue };
            var writeErrors = new List<ServiceResult> { null };

            await manager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write),
                nodesToWrite,
                writeErrors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(writeErrors[0]), Is.True);
            Assert.That(euProperty.Value, Is.EqualTo(123));

            Assert.That(monitoredItem.IsReadyToPublish, Is.True);

            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            bool hadMore = monitoredItem.Publish(
                new OperationContext(new RequestHeader(), null, RequestType.Publish),
                notifications,
                diagnostics,
                10,
                m_mockLogger.Object);

            Assert.That(hadMore, Is.False);
            Assert.That(notifications.Count, Is.EqualTo(2));
            MonitoredItemNotification notification = notifications.Dequeue();
            MonitoredItemNotification notificationAfterWrite = notifications.Dequeue();
            Assert.That(notification.Value.Value, Is.EqualTo(0));
            Assert.That(notification.Value.StatusCode.SemanticsChanged, Is.True);
            Assert.That(diagnostics.Count, Is.EqualTo(2));
            Assert.That(monitoredItem.IsReadyToPublish, Is.False);
        }

        [Test]
        public async Task AddReferencesAsync_AddsExternalReferencesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            var targetId = new NodeId("Target", 0); // External
            var references = new Dictionary<NodeId, IList<IReference>>
             {
                 {
                     baseObject.NodeId,
                     new List<IReference>
                     {
                         new ReferenceNode { ReferenceTypeId = ReferenceTypeIds.HasComponent, IsInverse = false, TargetId = targetId }
                     }
                 }
             };

            // Act
            await manager.AddReferencesAsync(references).ConfigureAwait(false);

            // Assert
            var refs = new List<IReference>();
            baseObject.GetReferences(context, refs);
            Assert.That(refs.Exists(r => r.TargetId == targetId && r.ReferenceTypeId == ReferenceTypeIds.HasComponent), Is.True);
            await manager.AddReferencesAsync(references).ConfigureAwait(false);
            refs.Clear();
            baseObject.GetReferences(context, refs);
            List<IReference> matchingRefs = refs.FindAll(r => r.TargetId == targetId && r.ReferenceTypeId == ReferenceTypeIds.HasComponent);
            Assert.That(matchingRefs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DeleteReferenceAsync_RemovesBidirectionalReferencesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var target = new BaseObjectState(null);
            target.CreateAsPredefinedNode(context);
            target.NodeId = new NodeId("Target", nsIdx);
            target.BrowseName = new QualifiedName("Target", nsIdx);
            await manager.AddNodeAsync(context, default, target).ConfigureAwait(false);

            source.AddReference(ReferenceTypeIds.Organizes, false, target.NodeId);
            target.AddReference(ReferenceTypeIds.Organizes, true, source.NodeId);

            object handle = await manager.GetManagerHandleAsync(source.NodeId).ConfigureAwait(false);

            ServiceResult result = await manager.DeleteReferenceAsync(
                handle,
                ReferenceTypeIds.Organizes,
                false,
                target.NodeId,
                true).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(source.ReferenceExists(ReferenceTypeIds.Organizes, false, target.NodeId), Is.False);
            Assert.That(target.ReferenceExists(ReferenceTypeIds.Organizes, true, source.NodeId), Is.False);
        }

        [Test]
        public async Task CreateMonitoredItemsAsync_CreatesItemAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            // Act
            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(monitoredItems[0], Is.Not.Null);
            Assert.That(monitoredItems[0].NodeId, Is.EqualTo(variable.NodeId));
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItems[0].Id), Is.True);
            Assert.That(manager.MonitoredItems[monitoredItems[0].Id], Is.SameAs(monitoredItems[0]));
            if (!m_useSamplingGroups)
            {
                // SamplingGroupMonitoredItemManager does not populate MonitoredNodes for data-change items
                Assert.That(manager.MonitoredNodes.ContainsKey(variable.NodeId), Is.True);
                Assert.That(manager.MonitoredNodes[variable.NodeId].DataChangeMonitoredItems.ContainsKey(monitoredItems[0].Id), Is.True);
            }
            Assert.That(monitoredItems[0].MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(monitoredItems[0].SamplingInterval, Is.EqualTo(100).Within(0.1));
        }

        [Test]
        public async Task ModifyMonitoredItemsAsync_ModifiesItemAsync()
        {
            // Setup manager and node
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // Create monitored item first
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };
            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(CreateMonitoredItemsContext(),
                                                    1,
                                                    1000,
                                                    TimestampsToReturn.Both,
                                                    itemsToCreate,
                                                    errors,
                                                    filterErrors,
                                                    monitoredItems,
                                                    false,
                                                    new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            IMonitoredItem item = monitoredItems[0];

            // Modify
            var modifyRequest = new MonitoredItemModifyRequest
            {
                MonitoredItemId = item.Id,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 500, QueueSize = 20 }
            };
            var itemsToModify = new List<MonitoredItemModifyRequest> { modifyRequest };
            var modifyErrors = new List<ServiceResult> { null };
            var modifyFilterErrors = new List<MonitoringFilterResult> { null };

            // Act
            await manager.ModifyMonitoredItemsAsync(
                 new OperationContext(new RequestHeader(), null, RequestType.ModifyMonitoredItems, m_mockSession.Object),
                 TimestampsToReturn.Both,
                 monitoredItems,
                 itemsToModify,
                 modifyErrors,
                 modifyFilterErrors).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(modifyErrors[0]), Is.True);
            Assert.That(item.SamplingInterval, Is.EqualTo(500));
            Assert.That(item is ISampledDataChangeMonitoredItem sampledItem && sampledItem.QueueSize == 20, Is.True);
            Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
        }

        [Test]
        public async Task SetMonitoringModeAsync_ChangesModeAsync()
        {
            // Setup manager and node
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // Create monitored item
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Disabled,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };
            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            IMonitoredItem item = monitoredItems[0];
            Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));

            // Act
            var processedItems = new List<bool> { false };
            var modeErrors = new List<ServiceResult> { null };
            await manager.SetMonitoringModeAsync(
                 new OperationContext(new RequestHeader(), null, RequestType.SetMonitoringMode),
                 MonitoringMode.Reporting,
                 monitoredItems,
                 processedItems,
                 modeErrors).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(modeErrors[0]), Is.True);
            Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(modeErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(processedItems[0], Is.True);
        }

        [Test]
        public async Task DeleteMonitoredItemsAsync_DeletesItemAsync()
        {
            // Setup manager and node
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("MyVar", nsIdx);
            variable.BrowseName = new QualifiedName("MyVar", nsIdx);
            variable.Value = 10;
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            // Create monitored item
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 100, QueueSize = 10 }
            };
            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                CreateMonitoredItemsContext(),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            IMonitoredItem monitoredItem = monitoredItems[0];
            Assert.That(monitoredItem, Is.Not.Null);

            // Act
            var processedItems = new List<bool> { false };
            var deleteErrors = new List<ServiceResult> { null };
            await manager.DeleteMonitoredItemsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.DeleteMonitoredItems),
                monitoredItems,
                processedItems,
                deleteErrors).ConfigureAwait(false);

            // Assert
            Assert.That(ServiceResult.IsGood(deleteErrors[0]), Is.True);
            Assert.That(deleteErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(processedItems[0], Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.False);
        }

        [Test]
        public async Task CallAsync_InvokesRegisteredMethodAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("CallParent", nsIdx);
            parent.BrowseName = new QualifiedName("CallParent", nsIdx);

            var method = new MethodState(parent)
            {
                NodeId = new NodeId("CallMethod", nsIdx),
                BrowseName = new QualifiedName("CallMethod", nsIdx)
            };

            method.InputArguments = new PropertyState<Argument[]>(method)
            {
                Value = []
            };
            method.OutputArguments = new PropertyState<Argument[]>(method)
            {
                Value =
                [
                    new Argument
                    {
                        Name = "Result",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };

            method.OnCallMethod = (systemContext, _, _, outputs) =>
            {
                outputs[0] = new Variant(321);
                return ServiceResult.Good;
            };

            parent.AddChild(method);

            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);

            var request = new CallMethodRequest
            {
                ObjectId = parent.NodeId,
                MethodId = method.NodeId,
                InputArguments = []
            };

            var requests = new List<CallMethodRequest> { request };
            var results = new List<CallMethodResult> { null };
            var errors = new List<ServiceResult> { null };
            var operationContext = new OperationContext(new RequestHeader(), null, RequestType.Call);

            await manager.CallAsync(operationContext, requests, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0].OutputArguments.Count, Is.EqualTo(1));
            Assert.That(results[0].OutputArguments[0].Value, Is.EqualTo(321));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncRequests = new List<CallMethodRequest>
            {
                new() {
                    ObjectId = parent.NodeId,
                    MethodId = method.NodeId,
                    InputArguments = []
                }
            };
            var syncResults = new List<CallMethodResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.Call(operationContext, syncRequests, syncResults, syncErrors);

            Assert.That(ServiceResult.IsGood(syncErrors[0]), Is.True);
        }

        [Test]
        public async Task HistoryReadAsync_ReturnsUnsupportedForNodesWithoutHistoryAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("HistVar", nsIdx);
            variable.BrowseName = new QualifiedName("HistVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.UtcNow.AddMinutes(-1),
                EndTime = DateTime.UtcNow
            };
            var nodesToRead = new List<HistoryReadValueId>
            {
                new() { NodeId = variable.NodeId }
            };
            var results = new List<HistoryReadResult> { null };
            var errors = new List<ServiceResult> { null };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.HistoryRead);

            await manager.HistoryReadAsync(opContext, details, TimestampsToReturn.Source, false, nodesToRead, results, errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncNodesToRead = new List<HistoryReadValueId>
            {
                new() { NodeId = variable.NodeId }
            };
            var syncResults = new List<HistoryReadResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.HistoryRead(opContext, details, TimestampsToReturn.Source, false, syncNodesToRead, syncResults, syncErrors);

            Assert.That(syncErrors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task HistoryUpdateAsync_ReturnsUnsupportedForNodesWithoutHistoryAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("HistUpdateVar", nsIdx);
            variable.BrowseName = new QualifiedName("HistUpdateVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.AccessLevel = AccessLevels.CurrentRead;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var updateDetails = new UpdateDataDetails
            {
                NodeId = variable.NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = []
            };
            var nodesToUpdate = new List<HistoryUpdateDetails> { updateDetails };
            var results = new List<HistoryUpdateResult> { null };
            var errors = new List<ServiceResult> { null };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.HistoryUpdate);

            await manager.HistoryUpdateAsync(opContext, typeof(UpdateDataDetails), nodesToUpdate, results, errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncNodesToUpdate = new List<HistoryUpdateDetails>
            {
                new UpdateDataDetails
                {
                    NodeId = variable.NodeId,
                    PerformInsertReplace = PerformUpdateType.Insert,
                    UpdateValues = []
                }
            };
            var syncResults = new List<HistoryUpdateResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.HistoryUpdate(opContext, typeof(UpdateDataDetails), syncNodesToUpdate, syncResults, syncErrors);

            Assert.That(syncErrors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task ConditionRefreshAsync_ReturnsGoodWhenMonitoringServerAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = ObjectIds.Server,
                Id = 1,
                ManagerHandle = new NodeHandle(ObjectIds.Server, null)
            };
            var items = new List<IEventMonitoredItem> { monitoredItem };
            var context = new OperationContext(new RequestHeader(), null, RequestType.Unknown);

            ServiceResult result = await manager.ConditionRefreshAsync(context, items).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            ServiceResult syncResult = syncManager.ConditionRefresh(context, items);
            Assert.That(ServiceResult.IsGood(syncResult), Is.True);
        }

        [Test]
        public async Task SubscribeToEventsAsync_ReturnsBadNodeIdInvalidForUnknownSourceAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem { NodeId = ObjectIds.Server };
            var context = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription);

            ServiceResult result = await manager.SubscribeToEventsAsync(context, new object(), 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            ServiceResult syncResult = syncManager.SubscribeToEvents(context, new object(), 1, monitoredItem, false);
            Assert.That(syncResult.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task SubscribeToAllEventsAsync_ReturnsGoodWhenNoRootNotifiersAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem { NodeId = ObjectIds.Server };
            var context = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription);

            ServiceResult result = await manager.SubscribeToAllEventsAsync(context, 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            ServiceResult syncResult = syncManager.SubscribeToAllEvents(context, 1, monitoredItem, false);
            Assert.That(ServiceResult.IsGood(syncResult), Is.True);
        }

        [Test]
        public async Task RestoreMonitoredItemsAsync_RestoresStoredItemsAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            m_mockServer.Setup(s => s.IsRunning).Returns(false);

            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("RestoreVar", nsIdx);
            variable.BrowseName = new QualifiedName("RestoreVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.Value = 0;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var storedItem = new TestStoredMonitoredItem
            {
                NodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                QueueSize = 1,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = 100,
                SubscriptionId = 1,
                Id = 1,
                DiagnosticsMasks = DiagnosticsMasks.All
            };

            var itemsToRestore = new List<IStoredMonitoredItem> { storedItem };
            var restoredItems = new List<IMonitoredItem> { null };
            IUserIdentity identity = new Mock<IUserIdentity>().Object;

            await manager.RestoreMonitoredItemsAsync(itemsToRestore, restoredItems, identity).ConfigureAwait(false);

            Assert.That(storedItem.IsRestored, Is.True);
            Assert.That(restoredItems[0], Is.Not.Null);
        }

        [Test]
        public async Task TransferMonitoredItemsAsync_MarksItemsProcessedAndTriggersResendAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId("TransferVar", nsIdx);
            variable.BrowseName = new QualifiedName("TransferVar", nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);

            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = variable.NodeId,
                ManagerHandle = new NodeHandle(variable.NodeId, variable),
                Id = 1
            };

            var monitoredItems = new List<IMonitoredItem> { monitoredItem };
            var processed = new List<bool> { false };
            var errors = new List<ServiceResult> { null };
            var operationContext = new OperationContext(new RequestHeader(), null, RequestType.TransferSubscriptions);

            await manager.TransferMonitoredItemsAsync(operationContext, true, monitoredItems, processed, errors).ConfigureAwait(false);

            Assert.That(processed[0], Is.True);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(monitoredItem.ResendDataRequested, Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var secondItem = new TestEventMonitoredItem
            {
                NodeId = variable.NodeId,
                ManagerHandle = new NodeHandle(variable.NodeId, variable),
                Id = 2
            };
            var syncProcessed = new List<bool> { false };
            var syncErrors = new List<ServiceResult> { null };
            var syncItems = new List<IMonitoredItem> { secondItem };

            syncManager.TransferMonitoredItems(operationContext, true, syncItems, syncProcessed, syncErrors);

            Assert.That(syncProcessed[0], Is.True);
            Assert.That(ServiceResult.IsGood(syncErrors[0]), Is.True);
        }

        [Test]
        public async Task SessionClosingAsync_CompletesWithoutErrorAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var context = new OperationContext(new RequestHeader(), null, RequestType.CloseSession);

            await manager.SessionClosingAsync(context, new NodeId(10), true).ConfigureAwait(false);

            var syncManager = (INodeManager2)manager.SyncNodeManager;
            syncManager.SessionClosing(context, new NodeId(11), false);
        }

        [Test]
        public async Task IsNodeInViewAsync_ReturnsFalseForUnknownHandleAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var context = new OperationContext(new RequestHeader(), null, RequestType.Browse);

            bool result = await manager.IsNodeInViewAsync(context, ObjectIds.Server, new object()).ConfigureAwait(false);
            Assert.That(result, Is.False);

            var syncManager = (INodeManager2)manager.SyncNodeManager;
            bool syncResult = syncManager.IsNodeInView(context, ObjectIds.Server, new object());
            Assert.That(syncResult, Is.False);
        }

        [Test]
        public async Task GetPermissionMetadataAsync_ReturnsAccessAndRoleInformationAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(context);
            node.NodeId = new NodeId("PermissionNode", nsIdx);
            node.BrowseName = new QualifiedName("PermissionNode", nsIdx);
            node.AccessRestrictions = AccessRestrictionType.SigningRequired;
            node.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)PermissionType.Browse
                }
            ];
            node.UserRolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)PermissionType.Read
                }
            ];

            await manager.AddNodeAsync(context, default, node).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(node.NodeId).ConfigureAwait(false);
            var cache = new Dictionary<NodeId, List<object>>
            {
                [node.NodeId] = []
            };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read);

            NodeMetadata metadata = await manager.GetPermissionMetadataAsync(opContext, handle, BrowseResultMask.All, cache, true).ConfigureAwait(false);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.AccessRestrictions, Is.EqualTo(AccessRestrictionType.SigningRequired));
            Assert.That(metadata.RolePermissions, Is.Not.Null);
            Assert.That(metadata.UserRolePermissions, Is.Not.Null);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            NodeMetadata syncMetadata = syncManager.GetPermissionMetadata(opContext, handle, BrowseResultMask.All, cache, true);
            Assert.That(syncMetadata, Is.Not.Null);
        }

        [Test]
        public async Task ValidateRolePermissionsAsync_ReturnsGoodWhenPermissionNotRequiredAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServiceResult result = await manager.ValidateRolePermissionsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read),
                NodeId.Null,
                PermissionType.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task ValidateEventRolePermissionsAsync_ReturnsGoodWhenEventInformationMissingAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem
            {
                EffectiveIdentity = Mock.Of<IUserIdentity>(),
                Session = Mock.Of<ISession>(s => s.Identity == Mock.Of<IUserIdentity>() && s.PreferredLocales == Array.Empty<string>())
            };

            var eventState = new BaseEventState(null)
            {
                EventType = new PropertyState<NodeId>(null)
                {
                    Value = NodeId.Null
                },
                SourceNode = new PropertyState<NodeId>(null)
                {
                    Value = NodeId.Null
                }
            };

            ServiceResult result = await manager.ValidateEventRolePermissionsAsync(monitoredItem, eventState).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task AddRootNotifierAsyncAddsNodeToRootNotifiersAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.True);
            Assert.That(manager.RootNotifiers[notifier.NodeId], Is.SameAs(notifier));
        }

        [Test]
        public async Task AddRootNotifierAsyncSetsOnReportEventCallbackAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(notifier.OnReportEvent, Is.Not.Null);
        }

        [Test]
        public async Task AddRootNotifierAsyncAddsHasNotifierReferenceToServerAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(
                notifier.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.True);
        }

        [Test]
        public async Task AddRootNotifierAsyncServerNodeSkipsCallbackAndReferenceAsync()
        {
            // The Server object itself must not get the HasNotifier→Server reference
            // to prevent infinite recursion in event reporting.
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var serverNode = new BaseObjectState(null)
            {
                NodeId = ObjectIds.Server,
                BrowseName = new QualifiedName("Server")
            };

            await manager.AddRootNotifierPublicAsync(serverNode).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(ObjectIds.Server), Is.True);
            Assert.That(serverNode.OnReportEvent, Is.Null);
            Assert.That(
                serverNode.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.False);
        }

        [Test]
        public async Task AddRootNotifierAsyncIsIdempotentAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.True);

            // HasNotifier reference to Server must not be duplicated
            var refs = new List<IReference>();
            notifier.GetReferences(manager.SystemContext, refs);
            int hasNotifierCount = refs.Count(r =>
                r.ReferenceTypeId == ReferenceTypeIds.HasNotifier &&
                r.IsInverse &&
                r.TargetId == new ExpandedNodeId(ObjectIds.Server));
            Assert.That(hasNotifierCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RemoveRootNotifierAsyncRemovesFromRootNotifiersAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.True);

            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.False);
        }

        [Test]
        public async Task RemoveRootNotifierAsyncClearsOnReportEventCallbackAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            Assert.That(notifier.OnReportEvent, Is.Not.Null);

            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(notifier.OnReportEvent, Is.Null);
        }

        [Test]
        public async Task RemoveRootNotifierAsyncRemovesHasNotifierReferenceAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);
            Assert.That(
                notifier.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.True);

            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(
                notifier.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.False);
        }

        [Test]
        public async Task RemoveRootNotifierAsyncIsNoopForUnknownNotifierAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("NeverAdded", nsIdx);
            notifier.BrowseName = new QualifiedName("NeverAdded", nsIdx);

            // Must not throw when the notifier was never registered
            await manager.RemoveRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(notifier.NodeId), Is.False);
        }

        [Test]
        public void OnReportEventDelegatesToServerReportEvent()
        {
            IFilterTarget capturedEvent = null;
            m_mockServer
                .Setup(s => s.ReportEvent(It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>()))
                .Callback<ISystemContext, IFilterTarget>((_, e) => capturedEvent = e);

            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("EventSource", nsIdx)
            };

            IFilterTarget mockEvent = new Mock<IFilterTarget>().Object;

            manager.InvokeOnReportEvent(manager.SystemContext, node, mockEvent);

            m_mockServer.Verify(
                s => s.ReportEvent(It.IsAny<ISystemContext>(), mockEvent),
                Times.Once);
            Assert.That(capturedEvent, Is.SameAs(mockEvent));
        }

        [Test]
        public async Task OnReportEventIsInvokedWhenNodeReportsEventAsync()
        {
            // Verifies that the callback wired by AddRootNotifierAsync routes events
            // through to IServerInternal.ReportEvent.
            IFilterTarget capturedEvent = null;
            m_mockServer
                .Setup(s => s.ReportEvent(It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>()))
                .Callback<ISystemContext, IFilterTarget>((_, e) => capturedEvent = e);

            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            IFilterTarget mockEvent = new Mock<IFilterTarget>().Object;
            notifier.ReportEvent(manager.SystemContext, mockEvent);

            Assert.That(capturedEvent, Is.SameAs(mockEvent));
        }

        [Test]
        public async Task SubscribeToEventsAsyncSucceedsForValidEventNotifierNodeAsyncAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("EventSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("EventSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = eventSource.NodeId,
                Id = 42,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            ServiceResult result = await manager.SubscribeToEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription),
                handle,
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);
            Assert.That(manager.MonitoredNodes.ContainsKey(eventSource.NodeId), Is.True);
            Assert.That(
                manager.MonitoredNodes[eventSource.NodeId].EventMonitoredItems.ContainsKey(monitoredItem.Id),
                Is.True);
        }

        [Test]
        public async Task SubscribeToEventsAsyncUnsubscribeRemovesEventMonitoredItemAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("EventSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("EventSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = eventSource.NodeId,
                Id = 43,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription);

            await manager.SubscribeToEventsAsync(opContext, handle, 1, monitoredItem, false).ConfigureAwait(false);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);

            ServiceResult unsubResult = await manager.SubscribeToEventsAsync(opContext, handle, 1, monitoredItem, true).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(unsubResult), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.False);
            Assert.That(
                manager.MonitoredNodes.ContainsKey(eventSource.NodeId),
                Is.False,
                "MonitoredNode entry should be cleaned up when no items remain");
        }

        [Test]
        public async Task SubscribeToEventsAsyncReturnsBadNotSupportedForNodeWithoutEventNotifierAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var nonEventSource = new BaseObjectState(null);
            nonEventSource.CreateAsPredefinedNode(context);
            nonEventSource.NodeId = new NodeId("NonEvent", nsIdx);
            nonEventSource.BrowseName = new QualifiedName("NonEvent", nsIdx);
            nonEventSource.EventNotifier = EventNotifiers.None; // no subscribe flag
            await manager.AddNodeAsync(context, default, nonEventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(nonEventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem { NodeId = nonEventSource.NodeId, Id = 77 };

            ServiceResult result = await manager.SubscribeToEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription),
                handle,
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task SubscribeToAllEventsAsyncSubscribesToRootNotifiersAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(context);
            notifier.NodeId = new NodeId("AreaNotifier", nsIdx);
            notifier.BrowseName = new QualifiedName("AreaNotifier", nsIdx);
            notifier.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, notifier).ConfigureAwait(false);
            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(notifier.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = notifier.NodeId,
                Id = 55,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            ServiceResult result = await manager.SubscribeToAllEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription),
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);
            Assert.That(manager.MonitoredNodes.ContainsKey(notifier.NodeId), Is.True);
        }

        [Test]
        public async Task ConditionRefreshAsyncReturnsGoodForManagedMonitoredItemAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("ConditionSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("ConditionSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = eventSource.NodeId,
                Id = 88,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            // Subscribe first so the item is tracked in MonitoredItems
            await manager.SubscribeToEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription),
                handle, 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);

            var items = new List<IEventMonitoredItem> { monitoredItem };
            var refreshContext = new OperationContext(new RequestHeader(), null, RequestType.Unknown);

            ServiceResult result = await manager.ConditionRefreshAsync(refreshContext, items).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task ConditionRefreshAsyncSkipsItemsNotManagedByThisNodeManagerAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            var externalItem = new TestEventMonitoredItem
            {
                NodeId = ObjectIds.RootFolder, // not in this manager's namespace
                Id = 999,
                ManagerHandle = new NodeHandle(ObjectIds.RootFolder, null)
            };

            ServiceResult result = await manager.ConditionRefreshAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Unknown),
                [externalItem]).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(externalItem.QueuedEvents, Is.Empty);
        }

        [Test]
        public async Task AddReverseReferencesAsyncCreatesRootNotifierForInverseHasNotifierToExternalNodeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // A node with an inverse HasNotifier reference to an external (namespace 0) node
            // indicates it is notified by an external area — making it a root notifier.
            var area = new BaseObjectState(null);
            area.CreateAsPredefinedNode(context);
            area.NodeId = new NodeId("Area", nsIdx);
            area.BrowseName = new QualifiedName("Area", nsIdx);
            area.AddReference(ReferenceTypeIds.HasNotifier, true, new NodeId("ExternalArea", 0));
            await manager.AddNodeAsync(context, default, area).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(area.NodeId), Is.True);
            Assert.That(manager.RootNotifiers[area.NodeId], Is.SameAs(area));
        }

        [Test]
        public async Task AddReverseReferencesAsyncDoesNotCreateRootNotifierForForwardHasNotifierAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // A forward HasNotifier reference (IsInverse = false) must NOT create a root notifier
            var area = new BaseObjectState(null);
            area.CreateAsPredefinedNode(context);
            area.NodeId = new NodeId("Area", nsIdx);
            area.BrowseName = new QualifiedName("Area", nsIdx);
            area.AddReference(ReferenceTypeIds.HasNotifier, false, new NodeId("ExternalArea", 0));
            await manager.AddNodeAsync(context, default, area).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(area.NodeId), Is.False);
        }

        [Test]
        public async Task AddReverseReferencesAsyncWithNodeHavingNoReferencesProducesNoExternalRefsAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // A node with no manually-added references should not generate any external references
            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("SourceNoRefs", nsIdx);
            source.BrowseName = new QualifiedName("SourceNoRefs", nsIdx);
            await manager.AddPredefinedNodePublicAsync(context, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        [Test]
        public async Task AddReverseReferencesAsyncSkipsReferenceWithAbsoluteTargetIdAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);
            source.AddReference(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId("AbsoluteTarget", 0, "http://absolute.example.org/", 0u));
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        [Test]
        public async Task AddReverseReferencesAsyncSkipsHasSubtypeReferencesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);
            var externalTarget = new NodeId("ExternalTarget", 0);
            source.AddReference(ReferenceTypeIds.HasSubtype, false, externalTarget);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        [Test]
        public async Task AddReverseReferencesAsyncAddsInverseHasEncodingToTypeTreeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var encodingTargetId = new NodeId("DataType", 0);
            // AddEncoding requires the data type to be registered in the TypeTree first
            m_mockServer.Object.TypeTree.AddSubtype(encodingTargetId, NodeId.Null);

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("DataTypeEncoding", nsIdx);
            source.BrowseName = new QualifiedName("DataTypeEncoding", nsIdx);
            source.AddReference(ReferenceTypeIds.HasEncoding, true, encodingTargetId);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            // AddEncoding(dataTypeId, encodingId) maps encodingId → dataTypeId;
            // FindDataTypeId(encodingId) should return the registered dataTypeId.
            Assert.That(
                m_mockServer.Object.TypeTree.FindDataTypeId(source.NodeId),
                Is.EqualTo(encodingTargetId));
        }

        [Test]
        public async Task AddReverseReferencesAsyncAddsReverseReferenceToInternalTargetAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            // Use AddPredefinedNodePublicAsync to bypass AssignNodeIds, which would
            // reassign NodeIds and break the PredefinedNodes lookup for the reference target.
            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            var target = new BaseObjectState(null);
            target.CreateAsPredefinedNode(context);
            target.NodeId = new NodeId("Target", nsIdx);
            target.BrowseName = new QualifiedName("Target", nsIdx);

            source.AddReference(ReferenceTypeIds.HasComponent, false, target.NodeId);
            await manager.AddPredefinedNodePublicAsync(context, source).ConfigureAwait(false);
            await manager.AddPredefinedNodePublicAsync(context, target).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(
                target.ReferenceExists(ReferenceTypeIds.HasComponent, true, source.NodeId),
                Is.True,
                "Target should have the inverse (IsInverse=true) HasComponent reference back to source");
            Assert.That(externalReferences, Is.Empty);
        }

        [Test]
        public async Task AddReverseReferencesAsyncDoesNotDuplicateReverseReferenceForInternalTargetAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            var target = new BaseObjectState(null);
            target.CreateAsPredefinedNode(context);
            target.NodeId = new NodeId("Target", nsIdx);
            target.BrowseName = new QualifiedName("Target", nsIdx);

            source.AddReference(ReferenceTypeIds.HasComponent, false, target.NodeId);
            await manager.AddPredefinedNodePublicAsync(context, source).ConfigureAwait(false);
            await manager.AddPredefinedNodePublicAsync(context, target).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            var refs = new List<IReference>();
            target.GetReferences(context, refs);
            int count = refs.Count(r =>
                r.ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                r.IsInverse &&
                r.TargetId == new ExpandedNodeId(source.NodeId));
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public async Task AddReverseReferencesAsyncSkipsExternalReferenceForTargetInSameNamespaceAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            // Reference to a target that is in the managed namespace but not in PredefinedNodes
            var inNamespaceTarget = new NodeId("NotInPredefinedNodes", nsIdx);
            source.AddReference(ReferenceTypeIds.HasComponent, false, inNamespaceTarget);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences, Is.Empty);
        }

        [Test]
        public async Task AddReverseReferencesAsyncAddsExternalReferenceForExternalNamespaceTargetAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source = new BaseObjectState(null);
            source.CreateAsPredefinedNode(context);
            source.NodeId = new NodeId("Source", nsIdx);
            source.BrowseName = new QualifiedName("Source", nsIdx);

            var externalTarget = new NodeId("ExternalTarget", 0);
            source.AddReference(ReferenceTypeIds.Organizes, false, externalTarget);
            await manager.AddNodeAsync(context, default, source).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences.ContainsKey(externalTarget), Is.True);
            IList<IReference> addedRefs = externalReferences[externalTarget];
            Assert.That(addedRefs, Has.Count.EqualTo(1));
            Assert.That(addedRefs[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
            Assert.That(addedRefs[0].IsInverse, Is.True);
            Assert.That(addedRefs[0].TargetId, Is.EqualTo(new ExpandedNodeId(source.NodeId)));
        }

        [Test]
        public async Task AddReverseReferencesAsyncAppendsToExistingExternalReferenceListAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var source1 = new BaseObjectState(null);
            source1.CreateAsPredefinedNode(context);
            source1.NodeId = new NodeId("Source1", nsIdx);
            source1.BrowseName = new QualifiedName("Source1", nsIdx);

            var source2 = new BaseObjectState(null);
            source2.CreateAsPredefinedNode(context);
            source2.NodeId = new NodeId("Source2", nsIdx);
            source2.BrowseName = new QualifiedName("Source2", nsIdx);

            var sharedExternalTarget = new NodeId("SharedTarget", 0);
            source1.AddReference(ReferenceTypeIds.Organizes, false, sharedExternalTarget);
            source2.AddReference(ReferenceTypeIds.HasComponent, false, sharedExternalTarget);
            await manager.AddNodeAsync(context, default, source1).ConfigureAwait(false);
            await manager.AddNodeAsync(context, default, source2).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.AddReverseReferencesPublicAsync(externalReferences).ConfigureAwait(false);

            Assert.That(externalReferences.ContainsKey(sharedExternalTarget), Is.True);
            Assert.That(externalReferences[sharedExternalTarget], Has.Count.EqualTo(2));
        }

        [Test]
        public void SetNamespacesUpdatesUrisAndIndexes()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/ns1/";
            const string ns2 = "http://test.org/ns2/";

            manager.SetNamespacesPublic(ns1, ns2);

            Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
            Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(2));
        }

        [Test]
        public void SetNamespacesRegistersUrisInServerNamespaceTable()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/ns1/";
            const string ns2 = "http://test.org/ns2/";

            manager.SetNamespacesPublic(ns1, ns2);

            ushort idx0 = manager.NamespaceIndexes[0];
            ushort idx1 = manager.NamespaceIndexes[1];
            Assert.That(m_namespaceTable.GetString(idx0), Is.EqualTo(ns1));
            Assert.That(m_namespaceTable.GetString(idx1), Is.EqualTo(ns2));
        }

        [Test]
        public void SetNamespacesEmptyArrayClearsNamespacesAndIndexes()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            manager.SetNamespacesPublic();

            Assert.That(manager.NamespaceUris, Is.Empty);
            Assert.That(manager.NamespaceIndexes, Is.Empty);
        }

        [Test]
        public void SetNamespacesReusesPreviouslyRegisteredUri()
        {
            // GetIndexOrAppend must not duplicate URIs already in the table
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort originalIndex = manager.NamespaceIndexes[0];

            manager.SetNamespacesPublic(m_testNamespaceUri);

            Assert.That(manager.NamespaceIndexes[0], Is.EqualTo(originalIndex));
        }

        [Test]
        public void SetNamespaceIndexesLooksUpUrisFromServerTable()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/idx1/";
            const string ns2 = "http://test.org/idx2/";
            ushort idx1 = m_namespaceTable.GetIndexOrAppend(ns1);
            ushort idx2 = m_namespaceTable.GetIndexOrAppend(ns2);

            manager.SetNamespaceIndexesPublic([idx1, idx2]);

            Assert.That(manager.NamespaceIndexes, Is.EqualTo([idx1, idx2]));
            Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
        }

        [Test]
        public void SetNamespaceIndexesEmptyArrayClearsNamespacesAndIndexes()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            manager.SetNamespaceIndexesPublic([]);

            Assert.That(manager.NamespaceIndexes, Is.Empty);
            Assert.That(manager.NamespaceUris, Is.Empty);
        }

        [Test]
        public void NamespaceUrisSetterAppendsToServerTableAndUpdatesIndexes()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            const string ns1 = "http://test.org/setter1/";
            const string ns2 = "http://test.org/setter2/";

            manager.SetNamespaceUrisPublic([ns1, ns2]);

            Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
            Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(2));
            Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[0]), Is.EqualTo(ns1));
            Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[1]), Is.EqualTo(ns2));
        }

        [Test]
        public void NamespaceUrisSetterNullThrowsArgumentNullException()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            Assert.Throws<ArgumentNullException>(() => manager.SetNamespaceUrisPublic(null));
        }

        [Test]
        public void NamespaceIndexReturnsFirstIndex()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort firstIndex = manager.NamespaceIndexes[0];

            Assert.That(manager.NamespaceIndex, Is.EqualTo(firstIndex));
        }

        [Test]
        public void ConstructorWithMultipleNamespacesRegistersAllInServerTable()
        {
            const string ns1 = "http://test.org/multi1/";
            const string ns2 = "http://test.org/multi2/";
            var manager = new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_mockLogger.Object,
                ns1, ns2);
            using (manager)
            {
                Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(2));
                Assert.That(manager.NamespaceUris, Is.EqualTo([ns1, ns2]));
                Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[0]), Is.EqualTo(ns1));
                Assert.That(m_namespaceTable.GetString(manager.NamespaceIndexes[1]), Is.EqualTo(ns2));
            }
        }

        [Test]
        public void IsNodeIdInNamespaceTrueForManagedNamespace()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("TestNode", nsIdx)), Is.True);
        }

        [Test]
        public void IsNodeIdInNamespaceFalseForUnmanagedNamespace()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            // Namespace 0 is the OPC UA built-in namespace, never managed by this node manager
            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("TestNode", 0)), Is.False);
        }

        [Test]
        public void IsNodeIdInNamespaceFalseForNullNodeId()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            Assert.That(manager.IsNodeIdInNamespacePublic(NodeId.Null), Is.False);
        }

        [Test]
        public void IsNodeIdInNamespaceAfterSetNamespacesReflectsNewNamespaces()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort oldIdx = manager.NamespaceIndexes[0];
            const string newNs = "http://test.org/new/";

            manager.SetNamespacesPublic(newNs);
            ushort newIdx = manager.NamespaceIndexes[0];

            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("Old", oldIdx)), Is.False);
            Assert.That(manager.IsNodeIdInNamespacePublic(new NodeId("New", newIdx)), Is.True);
        }

        [Test]
        public void IsHandleInNamespaceReturnsHandleForManagedNamespace()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("H", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            NodeHandle result = manager.IsHandleInNamespacePublic(handle);

            Assert.That(result, Is.SameAs(handle));
        }

        [Test]
        public void IsHandleInNamespaceReturnsNullForUnmanagedNamespace()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var node = new BaseObjectState(null) { NodeId = new NodeId("H", 0) };
            var handle = new NodeHandle(node.NodeId, node);

            Assert.That(manager.IsHandleInNamespacePublic(handle), Is.Null);
        }

        [Test]
        public void IsHandleInNamespaceReturnsNullForNonHandleObject()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            Assert.That(manager.IsHandleInNamespacePublic("not-a-handle"), Is.Null);
        }

        [Test]
        public void IsHandleInNamespaceReturnsNullForNullInput()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            Assert.That(manager.IsHandleInNamespacePublic(null), Is.Null);
        }

        [Test]
        public void SetNamespacesAffectsNewNodeIdGeneration()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            const string newNs = "http://test.org/newnodeid/";
            manager.SetNamespacesPublic(newNs);
            ushort newIdx = manager.NamespaceIndexes[0];

            NodeId generated = manager.New(manager.SystemContext, new BaseObjectState(null));

            Assert.That(generated.NamespaceIndex, Is.EqualTo(newIdx));
        }

        [Test]
        public void AddNodeToComponentCacheNullHandleReturnsNodeUnchanged()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            var node = new BaseObjectState(null) { NodeId = new NodeId("N", manager.NamespaceIndexes[0]) };

            NodeState result = manager.AddNodeToComponentCachePublic(manager.SystemContext, null, node);

            Assert.That(result, Is.SameAs(node));
        }

        [Test]
        public void AddNodeToComponentCacheFirstAddCreatesEntry()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("CacheNode", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            NodeState result = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);

            Assert.That(result, Is.SameAs(node));
            // Lookup must now return the cached node
            NodeState found = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(found, Is.SameAs(node));
        }

        [Test]
        public void AddNodeToComponentCacheSecondAddIncrementsRefCountAndReturnsCachedNode()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("CacheNode2", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);
            NodeState second = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);

            Assert.That(second, Is.SameAs(node));

            // One remove should keep the entry alive (RefCount 2 → 1)
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            NodeState afterOneRemove = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(afterOneRemove, Is.SameAs(node));

            // Second remove takes RefCount to 0 → evicted
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            NodeState afterTwoRemoves = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(afterTwoRemoves, Is.Null);
        }

        [Test]
        public void AddNodeToComponentCacheDistinctNodesStoredIndependently()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var nodeA = new BaseObjectState(null) { NodeId = new NodeId("A", nsIdx) };
            var nodeB = new BaseObjectState(null) { NodeId = new NodeId("B", nsIdx) };
            var handleA = new NodeHandle(nodeA.NodeId, nodeA);
            var handleB = new NodeHandle(nodeB.NodeId, nodeB);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handleA, nodeA);
            manager.AddNodeToComponentCachePublic(manager.SystemContext, handleB, nodeB);

            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handleA), Is.SameAs(nodeA));
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handleB), Is.SameAs(nodeB));
        }

        [Test]
        public void AddNodeToComponentCacheWithComponentPathStoresRootAtRootId()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle) = CreateComponentPathFixture(nsIdx);

            NodeState result = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);

            // First add returns the child node itself
            Assert.That(result, Is.SameAs(child));
            // Lookup via the component path handle finds the child
            NodeState found = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);
            Assert.That(found, Is.SameAs(child));
        }

        [Test]
        public void AddNodeToComponentCacheWithComponentPathSecondAddIncrementsRefCount()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle) = CreateComponentPathFixture(nsIdx);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);
            NodeState secondResult = manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);

            // Second add also returns child (found via FindChildBySymbolicName on root)
            Assert.That(secondResult, Is.SameAs(child));

            // One remove: RefCount 2 → 1, entry survives
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            Assert.That(
                manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle),
                Is.SameAs(child));

            // Second remove: RefCount 1 → 0, entry evicted
            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);
            Assert.That(
                manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle),
                Is.Null);
        }

        [Test]
        public void LookupNodeInComponentCacheBeforeAnyAddReturnsNull()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("NotCached", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            NodeState result = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void LookupNodeInComponentCacheUnknownNodeIdReturnsNull()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var knownNode = new BaseObjectState(null) { NodeId = new NodeId("Known", nsIdx) };
            var knownHandle = new NodeHandle(knownNode.NodeId, knownNode);
            manager.AddNodeToComponentCachePublic(manager.SystemContext, knownHandle, knownNode);

            var unknownHandle = new NodeHandle(new NodeId("Unknown", nsIdx), null);
            NodeState result = manager.LookupNodeInComponentCachePublic(manager.SystemContext, unknownHandle);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void LookupNodeInComponentCacheWithComponentPathUnknownRootIdReturnsNull()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            // Nothing added — lookup with a component path handle must return null
            var handle = new NodeHandle
            {
                NodeId = new NodeId("Child", nsIdx),
                RootId = new NodeId("UnknownRoot", nsIdx),
                ComponentPath = "SomePath",
                Validated = true
            };

            NodeState result = manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void RemoveNodeFromComponentCacheNullHandleIsNoop()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            // Must not throw
            Assert.DoesNotThrow(() =>
                manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, null));
        }

        [Test]
        public void RemoveNodeFromComponentCacheUnknownHandleIsNoop()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var handle = new NodeHandle(new NodeId("NeverAdded", nsIdx), null);

            // Must not throw even when the cache has never seen this node
            Assert.DoesNotThrow(() =>
                manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle));
        }

        [Test]
        public void RemoveNodeFromComponentCacheSingleAddThenRemoveEvictsEntry()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("Evict", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.Not.Null);

            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);

            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.Null);
        }

        [Test]
        public void RemoveNodeFromComponentCacheTwoAddsThenOneRemoveEntryRemains()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null) { NodeId = new NodeId("Shared", nsIdx) };
            var handle = new NodeHandle(node.NodeId, node);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);
            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, node);

            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);

            // RefCount 2 → 1: entry must still be there
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.SameAs(node));
        }

        [Test]
        public void RemoveNodeFromComponentCacheWithComponentPathUsesRootIdAsKey()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle) = CreateComponentPathFixture(nsIdx);

            manager.AddNodeToComponentCachePublic(manager.SystemContext, handle, child);
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.SameAs(child));

            manager.RemoveNodeFromComponentCachePublic(manager.SystemContext, handle);

            // Entry was keyed on RootId: after remove, lookup returns null
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, handle), Is.Null);

            // A simple handle for the parent NodeId must also return null (same underlying key)
            var parentHandle = new NodeHandle(parent.NodeId, parent);
            Assert.That(manager.LookupNodeInComponentCachePublic(manager.SystemContext, parentHandle), Is.Null);
        }

        /// <summary>
        /// Creates a parent/child pair wired up for component-path cache tests.
        /// The child has SymbolicName "ChildValue" and its Parent is set to the parent node.
        /// The returned handle has ComponentPath = "ChildValue" and RootId = parent.NodeId.
        /// </summary>
        private static (BaseObjectState parent, BaseDataVariableState child, NodeHandle handle)
            CreateComponentPathFixture(ushort nsIdx)
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("ComponentParent", nsIdx),
                BrowseName = new QualifiedName("ComponentParent", nsIdx)
            };

            var child = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId("ComponentChild", nsIdx),
                BrowseName = new QualifiedName("ComponentChild", nsIdx),
                SymbolicName = "ChildValue",
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            parent.AddChild(child);

            var handle = new NodeHandle
            {
                NodeId = child.NodeId,
                RootId = parent.NodeId,
                ComponentPath = "ChildValue",
                Validated = true,
                Node = child
            };

            return (parent, child, handle);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncNullFilterReturnsGoodAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                new NodeHandle(new NodeId("N", manager.NamespaceIndexes[0]), new BaseDataVariableState(null)),
                Attributes.Value,
                100,
                10,
                default).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.Null);
            Assert.That(result.Range, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncUnknownFilterTypeReturnsBadFilterNotAllowedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Int32 };
            var handle = new NodeHandle(variable.NodeId, variable);

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                new ExtensionObject(new EventFilter())).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterOnNonValueAttributeReturnsBadFilterNotAllowedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var handle = new NodeHandle(new NodeId("V", nsIdx), new BaseDataVariableState(null));
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = ObjectIds.Server,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Description,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterWithUnsupportedAggregateReturnsBadAggregateNotSupportedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var unsupportedAggregateId = new NodeId("UnsupportedAggregate", nsIdx);
            CreateAndSetupAggregateManager();
            var handle = new NodeHandle(new NodeId("V", nsIdx), new BaseDataVariableState(null));
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = unsupportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadAggregateNotSupported));
            Assert.That(result.FilterToUse, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncValidAggregateFilterSetsServerAggregateFilterAsFilterToUseAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            CreateAndSetupAggregateManager(supportedAggregateId);
            var handle = new NodeHandle(new NodeId("V", nsIdx), new BaseDataVariableState(null));
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = false }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(result.Range, Is.Null);
            Assert.That(((ServerAggregateFilter)result.FilterToUse).AggregateType, Is.EqualTo(supportedAggregateId));
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterProcessingIntervalAdjustedToSamplingIntervalAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            CreateAndSetupAggregateManager(supportedAggregateId, minimumProcessingInterval: 50);
            var handle = new NodeHandle(new NodeId("V", nsIdx), new BaseDataVariableState(null));
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 50,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = false }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                200,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(((ServerAggregateFilter)result.FilterToUse).ProcessingInterval, Is.EqualTo(200));
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterProcessingIntervalAdjustedToMinimumProcessingIntervalAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            const double minimumProcessingInterval = 500;
            CreateAndSetupAggregateManager(supportedAggregateId, minimumProcessingInterval);
            var handle = new NodeHandle(new NodeId("V", nsIdx), new BaseDataVariableState(null));
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 50,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = false }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(((ServerAggregateFilter)result.FilterToUse).ProcessingInterval, Is.EqualTo(minimumProcessingInterval));
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncAggregateFilterWithUseServerCapabilitiesDefaultsUpdatesAggregateConfigurationAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var supportedAggregateId = new NodeId("SupportedAggregate", nsIdx);
            CreateAndSetupAggregateManager(supportedAggregateId);
            var handle = new NodeHandle(new NodeId("V", nsIdx), new BaseDataVariableState(null));
            var filter = new ExtensionObject(new AggregateFilter
            {
                AggregateType = supportedAggregateId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = true }
            });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<ServerAggregateFilter>());
            Assert.That(((ServerAggregateFilter)result.FilterToUse).AggregateConfiguration.UseServerCapabilitiesDefaults, Is.False);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterOnNonValueAttributeReturnsBadFilterNotAllowedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Int32 };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 1.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Description,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterOnNonVariableNodeReturnsBadFilterNotAllowedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var objNode = new BaseObjectState(null) { NodeId = new NodeId("Obj", nsIdx) };
            var handle = new NodeHandle(objNode.NodeId, objNode);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 1.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterDeadbandNoneOnNumericVariableReturnsBadFilterNotAllowedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Int32 };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.None });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterAbsoluteDeadbandOnNonNumericTypeReturnsBadFilterNotAllowedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.String };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 5.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            Assert.That(result.FilterToUse, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterAbsoluteDeadbandOnNumericTypeSetsFilterToUseAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Double };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 5.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)result.FilterToUse).DeadbandValue, Is.EqualTo(5.0));
            Assert.That(result.Range, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterPercentDeadbandWithoutEURangeReturnsBadMonitoredItemFilterUnsupportedAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("V", nsIdx), DataType = DataTypeIds.Double };
            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Percent, DeadbandValue = 10.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemFilterUnsupported));
            Assert.That(result.FilterToUse, Is.Null);
        }

        [Test]
        public async Task ValidateMonitoringFilterAsyncDataChangeFilterPercentDeadbandWithEURangeSetsFilterToUseAndRangeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            SetupNumericTypeTree();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", nsIdx),
                BrowseName = new QualifiedName("V", nsIdx),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar
            };

            var euRangeProperty = new PropertyState(variable)
            {
                NodeId = new NodeId("EURange", nsIdx),
                BrowseName = new QualifiedName(BrowseNames.EURange),
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                Value = new Variant(new ExtensionObject(new Range { Low = 0.0, High = 100.0 }))
            };
            variable.AddChild(euRangeProperty);

            var handle = new NodeHandle(variable.NodeId, variable);
            var filter = new ExtensionObject(new DataChangeFilter { DeadbandType = (uint)DeadbandType.Percent, DeadbandValue = 10.0 });

            AsyncCustomNodeManager.ValidateMonitoringFilterResult result = await manager.ValidateMonitoringFilterPublicAsync(
                manager.SystemContext,
                handle,
                Attributes.Value,
                100,
                10,
                filter).ConfigureAwait(false);

            Assert.That((uint)result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.FilterToUse, Is.InstanceOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)result.FilterToUse).DeadbandType, Is.EqualTo((uint)DeadbandType.Percent));
            Assert.That(result.Range, Is.Not.Null);
            Assert.That(result.Range.High, Is.EqualTo(100.0));
            Assert.That(result.Range.Low, Is.EqualTo(0.0));
        }

        [Test]
        public async Task AddPredefinedNodeAsyncWithNonReferenceBaseTypeStateAddsSubtypeToTypeTreeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var dataType = new DataTypeState
            {
                NodeId = new NodeId("MyDataType", nsIdx),
                BrowseName = new QualifiedName("MyDataType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            await manager.AddPredefinedNodePublicAsync(manager.SystemContext, dataType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(dataType.NodeId), Is.True);
            Assert.That(manager.PredefinedNodes.ContainsKey(dataType.NodeId), Is.True);
        }

        [Test]
        public async Task AddPredefinedNodeAsyncWithReferenceTypeStateAddsReferenceSubtypeToTypeTreeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId("MyRefType", nsIdx),
                BrowseName = new QualifiedName("MyRefType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            await manager.AddPredefinedNodePublicAsync(manager.SystemContext, refType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(refType.NodeId), Is.True);
            // FindReferenceType uses the browse name registered by AddReferenceSubtype
            Assert.That(
                m_mockServer.Object.TypeTree.FindReferenceType(refType.BrowseName),
                Is.EqualTo(refType.NodeId));
        }

        [Test]
        public async Task AddPredefinedNodeAsyncRecursivelyAddsUnknownSuperTypeFromPredefinedNodesAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var parentType = new DataTypeState
            {
                NodeId = new NodeId("ParentType", nsIdx),
                BrowseName = new QualifiedName("ParentType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            // Place the parent directly in PredefinedNodes without adding it to the TypeTree
            manager.PredefinedNodes.AddOrUpdate(parentType.NodeId, parentType, (_, __) => parentType);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(parentType.NodeId), Is.False);

            var childType = new DataTypeState
            {
                NodeId = new NodeId("ChildType", nsIdx),
                BrowseName = new QualifiedName("ChildType", nsIdx),
                SuperTypeId = parentType.NodeId
            };

            await manager.AddPredefinedNodePublicAsync(manager.SystemContext, childType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(parentType.NodeId), Is.True);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(childType.NodeId), Is.True);
            Assert.That(
                m_mockServer.Object.TypeTree.IsTypeOf(childType.NodeId, parentType.NodeId),
                Is.True);
        }

        [Test]
        public async Task AddPredefinedNodeAsyncSkipsSuperTypeRecursionWhenSuperTypeAlreadyKnownAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            // Pre-register the parent type in the TypeTree directly
            var parentNodeId = new NodeId("KnownParent", nsIdx);
            m_mockServer.Object.TypeTree.AddSubtype(parentNodeId, NodeId.Null);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(parentNodeId), Is.True);

            var childType = new DataTypeState
            {
                NodeId = new NodeId("ChildOfKnownParent", nsIdx),
                BrowseName = new QualifiedName("ChildOfKnownParent", nsIdx),
                SuperTypeId = parentNodeId
            };

            await manager.AddPredefinedNodePublicAsync(manager.SystemContext, childType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(childType.NodeId), Is.True);
            Assert.That(
                m_mockServer.Object.TypeTree.IsTypeOf(childType.NodeId, parentNodeId),
                Is.True);
            // Parent's supertype should remain as registered (Null), not altered by the child add
            Assert.That(
                m_mockServer.Object.TypeTree.FindSuperType(parentNodeId),
                Is.EqualTo(NodeId.Null));
        }

        [Test]
        public async Task AddPredefinedNodeAsyncWithNullSuperTypeIdSkipsRecursionAndAddsToTypeTreeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];

            var rootType = new DataTypeState
            {
                NodeId = new NodeId("RootType", nsIdx),
                BrowseName = new QualifiedName("RootType", nsIdx),
                SuperTypeId = NodeId.Null
            };

            await manager.AddPredefinedNodePublicAsync(manager.SystemContext, rootType).ConfigureAwait(false);

            Assert.That(m_mockServer.Object.TypeTree.IsKnown(rootType.NodeId), Is.True);
            Assert.That(
                m_mockServer.Object.TypeTree.FindSuperType(rootType.NodeId),
                Is.EqualTo(NodeId.Null));
        }

        [Test]
        public async Task AddPredefinedNodeAsyncWithNonBaseTypeStateNodeDoesNotAddToTypeTreeAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var objectNode = new BaseObjectState(null);
            objectNode.CreateAsPredefinedNode(context);
            objectNode.NodeId = new NodeId("PlainObject", nsIdx);
            objectNode.BrowseName = new QualifiedName("PlainObject", nsIdx);

            await manager.AddPredefinedNodePublicAsync(context, objectNode).ConfigureAwait(false);

            Assert.That(manager.PredefinedNodes.ContainsKey(objectNode.NodeId), Is.True);
            Assert.That(m_mockServer.Object.TypeTree.IsKnown(objectNode.NodeId), Is.False);
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not used for security purposes")]
        public async Task ChaosTest_ConcurrentReadWriteBrowseAndMonitoredItemOperationsDoNotThrowAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateManager();
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            const int nodeCount = 20;
            const int workerCount = 10;
            const int operationsPerWorker = 100;

            // Build the address space: a folder containing nodeCount read/write variable nodes
            var folder = new FolderState(null);
            folder.CreateAsPredefinedNode(context);
            folder.NodeId = new NodeId("ChaosFolder", nsIdx);
            folder.BrowseName = new QualifiedName("ChaosFolder", nsIdx);
            folder.DisplayName = new LocalizedText("ChaosFolder");
            await manager.AddNodeAsync(context, default, folder).ConfigureAwait(false);

            var variables = new BaseDataVariableState[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(context);
                variable.NodeId = new NodeId($"ChaosVar_{i}", nsIdx);
                variable.BrowseName = new QualifiedName($"ChaosVar_{i}", nsIdx);
                variable.Value = i * 10;
                variable.DataType = DataTypeIds.Int32;
                variable.ValueRank = ValueRanks.Scalar;
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                await manager.AddNodeAsync(context, folder.NodeId, variable).ConfigureAwait(false);
                variables[i] = variable;
            }

            // Single shared factory: MonitoredItemIdFactory uses Interlocked.Increment internally
            var idFactory = new MonitoredItemIdFactory();
            var activeItems = new ConcurrentDictionary<uint, IMonitoredItem>();
            var unexpectedExceptions = new ConcurrentBag<Exception>();

            async Task WorkerAsync(int workerId)
            {
                var rng = new Random(workerId * 31337);
                for (int op = 0; op < operationsPerWorker; op++)
                {
                    int nodeIndex = rng.Next(nodeCount);
                    BaseDataVariableState node = variables[nodeIndex];
                    try
                    {
                        switch (rng.Next(7))
                        {
                            case 0: // Read value attribute
                            {
                                var nodesToRead = new List<ReadValueId>
                                {
                                    new() { NodeId = node.NodeId, AttributeId = Attributes.Value }
                                };
                                var values = new List<DataValue> { null };
                                var errors = new List<ServiceResult> { null };
                                await manager.ReadAsync(
                                    new OperationContext(new RequestHeader(), null, RequestType.Read),
                                    0, nodesToRead, values, errors).ConfigureAwait(false);
                                break;
                            }
                            case 1: // Write value attribute
                            {
                                var nodesToWrite = new List<WriteValue>
                                {
                                    new() {
                                        NodeId = node.NodeId,
                                        AttributeId = Attributes.Value,
                                        Value = new DataValue(new Variant(rng.Next(1000)))
                                    }
                                };
                                var errors = new List<ServiceResult> { null };
                                await manager.WriteAsync(
                                    new OperationContext(new RequestHeader(), null, RequestType.Write),
                                    nodesToWrite, errors).ConfigureAwait(false);
                                break;
                            }
                            case 2: // Browse folder for children
                                object browseHandle = await manager.GetManagerHandleAsync(folder.NodeId).ConfigureAwait(false);
                                if (browseHandle != null)
                                {
                                    var cp = new ContinuationPoint
                                    {
                                        NodeToBrowse = browseHandle,
                                        Manager = manager,
                                        View = new ViewDescription(),
                                        BrowseDirection = BrowseDirection.Forward,
                                        IncludeSubtypes = true,
                                        ResultMask = BrowseResultMask.All
                                    };
                                    var browseRefs = new List<ReferenceDescription>();
                                    await manager.BrowseAsync(
                                        new OperationContext(new RequestHeader(), null, RequestType.Browse),
                                        cp, browseRefs).ConfigureAwait(false);
                                }
                                break;
                            case 3: // Create a monitored item on the selected node
                                var itemToCreate = new MonitoredItemCreateRequest
                                {
                                    ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                                    MonitoringMode = MonitoringMode.Reporting,
                                    RequestedParameters = new MonitoringParameters
                                    {
                                        ClientHandle = (uint)workerId,
                                        SamplingInterval = 100,
                                        QueueSize = 5
                                    }
                                };
                                var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
                                var createErrors = new List<ServiceResult> { null };
                                var filterErrors = new List<MonitoringFilterResult> { null };
                                var createdItems = new List<IMonitoredItem> { null };
                                await manager.CreateMonitoredItemsAsync(
                                    CreateMonitoredItemsContext(),
                                    1, 1000, TimestampsToReturn.Both,
                                    itemsToCreate, createErrors, filterErrors, createdItems,
                                    false, idFactory).ConfigureAwait(false);
                                if (ServiceResult.IsGood(createErrors[0]) && createdItems[0] != null)
                                {
                                    activeItems[createdItems[0].Id] = createdItems[0];
                                }
                                break;
                            case 4: // Modify a random active monitored item
                            {
                                IMonitoredItem[] snapshot = [.. activeItems.Values];
                                if (snapshot.Length > 0)
                                {
                                    IMonitoredItem item = snapshot[rng.Next(snapshot.Length)];
                                    var modifyRequests = new List<MonitoredItemModifyRequest>
                                    {
                                        new() {
                                            MonitoredItemId = item.Id,
                                            RequestedParameters = new MonitoringParameters
                                            {
                                                ClientHandle = item.ClientHandle,
                                                SamplingInterval = rng.Next(100, 500),
                                                QueueSize = 5
                                            }
                                        }
                                    };
                                    var modifyErrors = new List<ServiceResult> { null };
                                    var modifyFilterErrors = new List<MonitoringFilterResult> { null };
                                    await manager.ModifyMonitoredItemsAsync(
                                        new OperationContext(new RequestHeader(), null, RequestType.ModifyMonitoredItems, m_mockSession.Object),
                                        TimestampsToReturn.Both,
                                        [item],
                                        modifyRequests, modifyErrors, modifyFilterErrors).ConfigureAwait(false);
                                }
                                break;
                            }
                            case 5: // Delete a random active monitored item
                            {
                                IMonitoredItem[] snapshot = [.. activeItems.Values];
                                if (snapshot.Length > 0)
                                {
                                    IMonitoredItem item = snapshot[rng.Next(snapshot.Length)];
                                    if (activeItems.TryRemove(item.Id, out _))
                                    {
                                        var toDelete = new List<IMonitoredItem> { item };
                                        var processed = new List<bool> { false };
                                        var deleteErrors = new List<ServiceResult> { null };
                                        await manager.DeleteMonitoredItemsAsync(
                                            new OperationContext(new RequestHeader(), null, RequestType.DeleteMonitoredItems),
                                            toDelete, processed, deleteErrors).ConfigureAwait(false);
                                    }
                                }
                                break;
                            }
                            case 6: // Toggle monitoring mode on a random active monitored item
                            {
                                IMonitoredItem[] snapshot = [.. activeItems.Values];
                                if (snapshot.Length > 0)
                                {
                                    IMonitoredItem item = snapshot[rng.Next(snapshot.Length)];
                                    MonitoringMode mode = rng.Next(2) == 0
                                        ? MonitoringMode.Reporting
                                        : MonitoringMode.Sampling;
                                    var modeItems = new List<IMonitoredItem> { item };
                                    var processed = new List<bool> { false };
                                    var modeErrors = new List<ServiceResult> { null };
                                    await manager.SetMonitoringModeAsync(
                                        new OperationContext(new RequestHeader(), null, RequestType.SetMonitoringMode),
                                        mode, modeItems, processed, modeErrors).ConfigureAwait(false);
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        unexpectedExceptions.Add(ex);
                    }
                }
            }

            // Run all workers concurrently on thread pool threads
            Task[] tasks = [.. Enumerable.Range(0, workerCount).Select(i => Task.Run(() => WorkerAsync(i)))];
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Clean up any remaining active monitored items
            foreach (IMonitoredItem item in activeItems.Values)
            {
                var toDelete = new List<IMonitoredItem> { item };
                var processed = new List<bool> { false };
                var errors = new List<ServiceResult> { null };
                await manager.DeleteMonitoredItemsAsync(
                    new OperationContext(new RequestHeader(), null, RequestType.DeleteMonitoredItems),
                    toDelete, processed, errors).ConfigureAwait(false);
            }

            Assert.That(
                unexpectedExceptions,
                Is.Empty,
                "Unexpected exceptions during chaos operations: " +
                string.Join("; ", unexpectedExceptions.Select(e => $"{e.GetType().Name}: {e.Message}")));
        }

        private AggregateManager CreateAndSetupAggregateManager(double minimumProcessingInterval = 1000.0)
        {
            var mockDiagnosticsNodeManager = new Mock<IDiagnosticsNodeManager>();
            m_mockServer.Setup(s => s.DiagnosticsNodeManager).Returns(mockDiagnosticsNodeManager.Object);
            var aggregateManager = new AggregateManager(m_mockServer.Object)
            {
                MinimumProcessingInterval = minimumProcessingInterval
            };
            m_mockServer.Setup(s => s.AggregateManager).Returns(aggregateManager);
            return aggregateManager;
        }

        private AggregateManager CreateAndSetupAggregateManager(NodeId supportedAggregateId, double minimumProcessingInterval = 1000.0)
        {
            AggregateManager aggregateManager = CreateAndSetupAggregateManager(minimumProcessingInterval);
            aggregateManager.RegisterFactory(
                supportedAggregateId,
                "TestAggregate",
                (id, start, end, interval, stepped, cfg, telemetry) => null);
            return aggregateManager;
        }

        private void SetupNumericTypeTree()
        {
            var typeTree = new TypeTable(m_namespaceTable);
            typeTree.AddSubtype(DataTypeIds.Number, NodeId.Null);
            typeTree.AddSubtype(DataTypeIds.Integer, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.UInteger, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.Float, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.Double, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.Int16, DataTypeIds.Integer);
            typeTree.AddSubtype(DataTypeIds.Int32, DataTypeIds.Integer);
            typeTree.AddSubtype(DataTypeIds.Int64, DataTypeIds.Integer);
            typeTree.AddSubtype(DataTypeIds.UInt16, DataTypeIds.UInteger);
            typeTree.AddSubtype(DataTypeIds.UInt32, DataTypeIds.UInteger);
            typeTree.AddSubtype(DataTypeIds.UInt64, DataTypeIds.UInteger);
            m_mockServer.Setup(s => s.TypeTree).Returns(typeTree);
        }

        private OperationContext CreateMonitoredItemsContext()
        {
            return new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems, m_mockSession.Object);
        }

        private TestableAsyncCustomNodeManager CreateManager()
        {
            var manager = new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_useSamplingGroups,
                m_mockLogger.Object,
                m_testNamespaceUri);

            SetupMasterNodeManager(manager);

            return manager;
        }

        private void SetupMasterNodeManager(TestableAsyncCustomNodeManager manager)
        {
            m_mockMasterNodeManager
                .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, _) =>
                {
                    NodeState nodeState = manager.Find(nodeId);
                    if (nodeState == null)
                    {
                        return new ValueTask<(object handle, IAsyncNodeManager nodeManager)>((null, null));
                    }

                    var handle = new NodeHandle(nodeId, nodeState);
                    return new ValueTask<(object handle, IAsyncNodeManager nodeManager)>((handle, manager));
                });
        }
    }

    public class TestableAsyncCustomNodeManager : AsyncCustomNodeManager
    {
        public NodeStateCollection NodesToLoad { get; set; }

        public TestableAsyncCustomNodeManager(
           IServerInternal server,
           ApplicationConfiguration configuration,
           ILogger logger,
           params string[] namespaceUris)
           : base(server, configuration, logger, namespaceUris)
        {
        }

        public TestableAsyncCustomNodeManager(
           IServerInternal server,
           ApplicationConfiguration configuration,
           bool useSamplingGroups,
           ILogger logger,
           params string[] namespaceUris)
           : base(server, configuration, useSamplingGroups, logger, namespaceUris)
        {
        }

        public ILogger Logger => m_logger;

        public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

        public new NodeIdDictionary<NodeState> RootNotifiers => base.RootNotifiers;

        public new NodeIdDictionary<MonitoredNode2> MonitoredNodes => base.MonitoredNodes;

        public new ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => base.MonitoredItems;

        public ValueTask AddRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
        {
            return AddRootNotifierAsync(notifier, cancellationToken);
        }

        public ValueTask RemoveRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
        {
            return RemoveRootNotifierAsync(notifier, cancellationToken);
        }

        public void InvokeOnReportEvent(ISystemContext context, NodeState node, IFilterTarget filterTarget)
        {
            OnReportEvent(context, node, filterTarget);
        }

        public ValueTask AddReverseReferencesPublicAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            return AddReverseReferencesAsync(externalReferences, cancellationToken);
        }

        public void SetNamespacesPublic(params string[] namespaceUris)
        {
            SetNamespaces(namespaceUris);
        }

        public void SetNamespaceIndexesPublic(ushort[] namespaceIndexes)
        {
            SetNamespaceIndexes(namespaceIndexes);
        }

        public void SetNamespaceUrisPublic(IEnumerable<string> uris)
        {
            NamespaceUris = uris;
        }

        public bool IsNodeIdInNamespacePublic(NodeId nodeId)
        {
            return IsNodeIdInNamespace(nodeId);
        }

        public NodeHandle IsHandleInNamespacePublic(object managerHandle)
        {
            return IsHandleInNamespace(managerHandle);
        }

        public NodeState LookupNodeInComponentCachePublic(ISystemContext context, NodeHandle handle)
        {
            return LookupNodeInComponentCache(context, handle);
        }

        public void RemoveNodeFromComponentCachePublic(ISystemContext context, NodeHandle handle)
        {
            RemoveNodeFromComponentCache(context, handle);
        }

        public NodeState AddNodeToComponentCachePublic(ISystemContext context, NodeHandle handle, NodeState node)
        {
            return AddNodeToComponentCache(context, handle, node);
        }

        public ValueTask<ValidateMonitoringFilterResult> ValidateMonitoringFilterPublicAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            CancellationToken cancellationToken = default)
        {
            return ValidateMonitoringFilterAsync(
                context, handle, attributeId, samplingInterval, queueSize, filter, cancellationToken);
        }

        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (NodesToLoad != null)
            {
                return new ValueTask<NodeStateCollection>(NodesToLoad);
            }

            return base.LoadPredefinedNodesAsync(context, cancellationToken);
        }

        public ValueTask AddPredefinedNodePublicAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            return AddPredefinedNodeAsync(context, node, cancellationToken);
        }
    }

    internal sealed class TestEventMonitoredItem : IEventMonitoredItem
    {
        public INodeManager NodeManager { get; set; }

        public ISession Session { get; set; }

        public IUserIdentity EffectiveIdentity { get; set; }

        public uint Id { get; set; }

        public uint SubscriptionId { get; set; }

        public bool IsDurable { get; set; }

        public uint ClientHandle { get; set; }

        public ISubscription SubscriptionCallback { get; set; }

        public object ManagerHandle { get; set; }

        public int MonitoredItemType { get; set; }

        public bool IsReadyToPublish { get; set; }

        public bool IsReadyToTrigger { get; set; }

        public bool IsResendData { get; set; }

        public NodeId NodeId { get; set; }

        public bool ResendDataRequested { get; private set; }

        public MonitoringMode MonitoringMode { get; set; } = MonitoringMode.Reporting;

        public double SamplingInterval { get; set; }

        public bool MonitoringAllEvents { get; set; }

        public EventFilter EventFilter { get; set; } = new EventFilter();

        public void Dispose()
        {
        }

        public void SetupResendDataTrigger()
        {
            ResendDataRequested = true;
        }

        public ServiceResult GetCreateResult(out MonitoredItemCreateResult result)
        {
            result = new MonitoredItemCreateResult();
            return StatusCodes.Good;
        }

        public ServiceResult GetModifyResult(out MonitoredItemModifyResult result)
        {
            result = new MonitoredItemModifyResult();
            return StatusCodes.Good;
        }

        public IStoredMonitoredItem ToStorableMonitoredItem()
        {
            return new TestStoredMonitoredItem
            {
                NodeId = NodeId,
                AttributeId = Attributes.Value
            };
        }

        public MonitoringMode SetMonitoringMode(MonitoringMode monitoringMode)
        {
            MonitoringMode previous = MonitoringMode;
            MonitoringMode = monitoringMode;
            return previous;
        }

        public void QueueEvent(IFilterTarget instance)
        {
            QueuedEvents.Add(instance);
        }

        public void QueueEvent(IFilterTarget instance, bool bypassFilter)
        {
            QueueEvent(instance);
        }

        public bool Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish)
        {
            return false;
        }

        public ServiceResult ModifyAttributes(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            MonitoringFilter originalFilter,
            MonitoringFilter filterToUse,
            Range range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest)
        {
            return StatusCodes.Good;
        }

        public List<IFilterTarget> QueuedEvents { get; } = [];
    }

    internal sealed class TestStoredMonitoredItem : IStoredMonitoredItem
    {
        public bool IsRestored { get; set; }

        public bool AlwaysReportUpdates { get; set; }

        public uint AttributeId { get; set; }

        public uint ClientHandle { get; set; }

        public DiagnosticsMasks DiagnosticsMasks { get; set; }

        public bool DiscardOldest { get; set; }

        public QualifiedName Encoding { get; set; }

        public uint Id { get; set; }

        public string IndexRange { get; set; }

        public NumericRange ParsedIndexRange { get; set; }

        public bool IsDurable { get; set; }

        public ServiceResult LastError { get; set; }

        public DataValue LastValue { get; set; }

        public MonitoringMode MonitoringMode { get; set; }

        public NodeId NodeId { get; set; }

        public MonitoringFilter FilterToUse { get; set; }

        public MonitoringFilter OriginalFilter { get; set; }

        public uint QueueSize { get; set; }

        public double Range { get; set; }

        public double SamplingInterval { get; set; }

        public int SourceSamplingInterval { get; set; }

        public uint SubscriptionId { get; set; }

        public TimestampsToReturn TimestampsToReturn { get; set; }

        public int TypeMask { get; set; }
    }
}
