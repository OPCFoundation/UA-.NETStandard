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
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("AsyncCustomNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class AsyncCustomNodeManagerTests
    {
        private Mock<IServerInternal> _mockServer;
        private ApplicationConfiguration _configuration;
        private Mock<ILogger> _mockLogger;
        private Mock<IMasterNodeManager> _mockMasterNodeManager;
        private ServerSystemContext _serverSystemContext;
        private NamespaceTable _namespaceTable;
        private string _testNamespaceUri = "http://test.org/UA/Data/";

        [SetUp]
        public void SetUp()
        {
            _mockServer = new Mock<IServerInternal>();
            _mockLogger = new Mock<ILogger>();
            _mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            _namespaceTable = new NamespaceTable();
            _namespaceTable.Append(_testNamespaceUri);

            _mockServer.Setup(s => s.NamespaceUris).Returns(_namespaceTable);
            _mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            _mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(_namespaceTable));
            _mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            _mockServer.Setup(s => s.NodeManager).Returns(_mockMasterNodeManager.Object);
            _mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfigurationNodeManager.Object);

            // Mock Telemetry
            var mockTelemetry = new Mock<ITelemetryContext>();
            _mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            _mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(new MonitoredItemQueueFactory(mockTelemetry.Object));

            // Setup DefaultSystemContext 
            _serverSystemContext = new ServerSystemContext(_mockServer.Object);
            _mockServer.Setup(s => s.DefaultSystemContext).Returns(_serverSystemContext);

            _configuration = new ApplicationConfiguration();
            _configuration.ServerConfiguration = new ServerConfiguration();
            _configuration.ServerConfiguration.MaxNotificationQueueSize = 100;
            _configuration.ServerConfiguration.MaxDurableNotificationQueueSize = 200;
        }

        [Test]
        public void Constructor_SetsPropertiesCorrectly()
        {
            using var manager = CreateManager();

            Assert.That(manager.MaxQueueSize, Is.EqualTo(100));
            Assert.That(manager.MaxDurableQueueSize, Is.EqualTo(200));
            Assert.That(manager.NamespaceIndexes, Has.Count.EqualTo(1));
            Assert.That(manager.NamespaceUris, Contains.Item(_testNamespaceUri));
            Assert.That(manager.Logger, Is.EqualTo(_mockLogger.Object));
            Assert.That(manager.SyncNodeManager, Is.Not.Null);
            Assert.That(manager.SystemContext.NodeIdFactory, Is.SameAs(manager));
        }

        [Test]
        public void NodeIDFactoryGeneratesNodesInTheRightNamespaceWithoutDuplicates()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            var generatedNodeIds = new HashSet<NodeId>(NodeIdComparer.Default);

            for (int i = 0; i < 100; i++)
            {
                var node = new BaseObjectState(null);
                var nodeId = manager.New(context, node);

                Assert.That(nodeId, Is.Not.Null);
                Assert.That(nodeId, Is.Not.EqualTo(NodeId.Null));
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(manager.NamespaceIndexes[0]));
                Assert.That(generatedNodeIds.Add(nodeId), Is.True, "Duplicate NodeId generated");
            }
        }

        [Test]
        public async Task FindPredefinedNode_ReturnsNodeOnlyWhenTypeMatches()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("FindNode", nsIdx);
            baseObject.BrowseName = new QualifiedName("FindNode", nsIdx);

            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            var matching = manager.FindPredefinedNode<BaseObjectState>(baseObject.NodeId);
            Assert.That(matching, Is.SameAs(baseObject));

            var nonMatching = manager.FindPredefinedNode<BaseDataVariableState>(baseObject.NodeId);
            Assert.That(nonMatching, Is.Null);

            var nullResult = manager.FindPredefinedNode<BaseObjectState>(NodeId.Null);
            Assert.That(nullResult, Is.Null);
        }

        [Test]
        public async Task CreateNodeAsync_AddsNodeToPredefinedNodes()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            baseObject.ReferenceTypeId = ReferenceTypeIds.Organizes;

            // Act
            var resultNodeId = await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            // Assert
            Assert.That(resultNodeId, Is.EqualTo(baseObject.NodeId));
            var storedNode = manager.Find(baseObject.NodeId);
            Assert.That(storedNode, Is.Not.Null);
            Assert.That(storedNode, Is.SameAs(baseObject));
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.True);
            Assert.That(baseObject.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
        }

        [Test]
        public async Task DeleteNodeAsync_RemovesNodeFromPredefinedNodes()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);

            await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);
            Assert.That(manager.Find(baseObject.NodeId), Is.Not.Null);

            // Act
            var result = await manager.DeleteNodeAsync(context, baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(manager.Find(baseObject.NodeId), Is.Null);
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.False);
            var secondResult = await manager.DeleteNodeAsync(context, baseObject.NodeId).ConfigureAwait(false);
            Assert.That(secondResult, Is.False);
        }

        [Test]
        public async Task CreateAddressSpaceAsync_LoadsNodesFromOverride()
        {
            using var manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var folder = new FolderState(null);
            folder.CreateAsPredefinedNode(manager.SystemContext);
            folder.NodeId = new NodeId("Folder", nsIdx);
            folder.BrowseName = new QualifiedName("Folder", nsIdx);
            folder.DisplayName = new LocalizedText("Folder");

            manager.NodesToLoad = new NodeStateCollection { folder };
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();

            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            Assert.That(manager.PredefinedNodes.ContainsKey(folder.NodeId), Is.True);
        }

        [Test]
        public async Task DeleteAddressSpaceAsync_DisposesAllNodes()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
        public async Task GetManagerHandleAsync_ReturnsHandleForExistingNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null);
            baseObject.CreateAsPredefinedNode(context);
            baseObject.NodeId = new NodeId("MyObject", nsIdx);
            baseObject.BrowseName = new QualifiedName("MyObject", nsIdx);
            baseObject.WriteMask = AttributeWriteMask.None;

            NodeId nodeID = await manager.AddNodeAsync(context, default, baseObject).ConfigureAwait(false);

            // Act
            var handle = await manager.GetManagerHandleAsync(baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(nodeID, Is.Not.EqualTo(NodeId.Null));
            Assert.That(handle, Is.InstanceOf<NodeHandle>());
            var nodeHandle = handle as NodeHandle;
            Assert.That(nodeHandle.NodeId, Is.EqualTo(baseObject.NodeId));
            Assert.That(nodeHandle.Node, Is.SameAs(baseObject));
            Assert.That(nodeHandle.Validated, Is.True);
            var invalidHandle = await manager.GetManagerHandleAsync(ObjectIds.Server).ConfigureAwait(false);
            Assert.That(invalidHandle, Is.Null);
        }

        [Test]
        public async Task GetNodeMetadataAsync_ReturnsMetadataForNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

            var handle = await manager.GetManagerHandleAsync(variable.NodeId).ConfigureAwait(false);

            var metadata = await manager.GetNodeMetadataAsync(
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
        public async Task ReadAsync_ReadsValueFromNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
        public async Task TranslateBrowsePathAsync_ResolvesTargets()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

            var handle = await manager.GetManagerHandleAsync(parent.NodeId).ConfigureAwait(false);
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
        public async Task BrowseAsync_ReturnsChildReferences()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

            var handle = await manager.GetManagerHandleAsync(parent.NodeId).ConfigureAwait(false);
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

            var result = await manager.BrowseAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Browse),
                continuationPoint,
                references).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].BrowseName, Is.EqualTo(child.BrowseName));
            Assert.That(references[0].NodeId, Is.EqualTo(new ExpandedNodeId(child.NodeId)));
        }

        [Test]
        public async Task WriteAsync_WritesValueToNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
        public async Task WriteAsync_PublishesValueToMonitoredItemQueue()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
                new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems),
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

            var hadMore = monitoredItem.Publish(
                new OperationContext(new RequestHeader(), null, RequestType.Publish),
                notifications,
                diagnostics,
                10,
                _mockLogger.Object);

            Assert.That(hadMore, Is.False);
            Assert.That(notifications.Count, Is.EqualTo(2));
            var notification = notifications.Dequeue();
            var notificationAfterWrite = notifications.Dequeue();
            Assert.That(notification.Value.Value, Is.EqualTo(0));
            Assert.That(notificationAfterWrite.Value.Value, Is.EqualTo(123));
            Assert.That(diagnostics.Count, Is.EqualTo(2));
            Assert.That(monitoredItem.IsReadyToPublish, Is.False);
        }

        [Test]
        public async Task WriteEngineeringUnitsAsync_PublishesSemanticsChangedValueToMonitoredItemQueue()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

            var propertyNodeID = euProperty.NodeId;

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
                new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems),
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

            var hadMore = monitoredItem.Publish(
                new OperationContext(new RequestHeader(), null, RequestType.Publish),
                notifications,
                diagnostics,
                10,
                _mockLogger.Object);

            Assert.That(hadMore, Is.False);
            Assert.That(notifications.Count, Is.EqualTo(2));
            var notification = notifications.Dequeue();
            var notificationAfterWrite = notifications.Dequeue();
            Assert.That(notification.Value.Value, Is.EqualTo(0));
            Assert.That(notification.Value.StatusCode.SemanticsChanged, Is.True);
            Assert.That(diagnostics.Count, Is.EqualTo(2));
            Assert.That(monitoredItem.IsReadyToPublish, Is.False);
        }

        [Test]
        public async Task AddReferencesAsync_AddsExternalReferences()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
            var matchingRefs = refs.FindAll(r => r.TargetId == targetId && r.ReferenceTypeId == ReferenceTypeIds.HasComponent);
            Assert.That(matchingRefs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DeleteReferenceAsync_RemovesBidirectionalReferences()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

            var handle = await manager.GetManagerHandleAsync(source.NodeId).ConfigureAwait(false);

            var result = await manager.DeleteReferenceAsync(
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
        public async Task CreateMonitoredItemsAsync_CreatesItem()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
                new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems),
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
            Assert.That(manager.MonitoredNodes.ContainsKey(variable.NodeId), Is.True);
            Assert.That(manager.MonitoredNodes[variable.NodeId].DataChangeMonitoredItems.ContainsKey(monitoredItems[0].Id), Is.True);
            Assert.That(monitoredItems[0].MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(monitoredItems[0].SamplingInterval, Is.EqualTo(100).Within(0.1));
        }

        [Test]
        public async Task ModifyMonitoredItemsAsync_ModifiesItem()
        {
            // Setup manager and node
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

            await manager.CreateMonitoredItemsAsync(new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems),
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
            var item = monitoredItems[0];

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
                 new OperationContext(new RequestHeader(), null, RequestType.ModifyMonitoredItems),
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
        public async Task SetMonitoringModeAsync_ChangesMode()
        {
            // Setup manager and node
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

            await manager.CreateMonitoredItemsAsync(new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            var item = monitoredItems[0];
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
        public async Task DeleteMonitoredItemsAsync_DeletesItem()
        {
            // Setup manager and node
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
                new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems),
                1,
                1000,
                TimestampsToReturn.Both,
                itemsToCreate,
                errors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            var monitoredItem = monitoredItems[0];
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
        public async Task CallAsync_InvokesRegisteredMethod()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
                Value = Array.Empty<Argument>()
            };
            method.OutputArguments = new PropertyState<Argument[]>(method)
            {
                Value = new[]
                {
                    new Argument
                    {
                        Name = "Result",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                }
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
                InputArguments = new VariantCollection()
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
                new CallMethodRequest
                {
                    ObjectId = parent.NodeId,
                    MethodId = method.NodeId,
                    InputArguments = new VariantCollection()
                }
            };
            var syncResults = new List<CallMethodResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.Call(operationContext, syncRequests, syncResults, syncErrors);

            Assert.That(ServiceResult.IsGood(syncErrors[0]), Is.True);
        }

        [Test]
        public async Task HistoryReadAsync_ReturnsUnsupportedForNodesWithoutHistory()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
                new HistoryReadValueId { NodeId = variable.NodeId }
            };
            var results = new List<HistoryReadResult> { null };
            var errors = new List<ServiceResult> { null };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.HistoryRead);

            await manager.HistoryReadAsync(opContext, details, TimestampsToReturn.Source, false, nodesToRead, results, errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncNodesToRead = new List<HistoryReadValueId>
            {
                new HistoryReadValueId { NodeId = variable.NodeId }
            };
            var syncResults = new List<HistoryReadResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.HistoryRead(opContext, details, TimestampsToReturn.Source, false, syncNodesToRead, syncResults, syncErrors);

            Assert.That(syncErrors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task HistoryUpdateAsync_ReturnsUnsupportedForNodesWithoutHistory()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
                UpdateValues = new DataValueCollection()
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
                    UpdateValues = new DataValueCollection()
                }
            };
            var syncResults = new List<HistoryUpdateResult> { null };
            var syncErrors = new List<ServiceResult> { null };

            syncManager.HistoryUpdate(opContext, typeof(UpdateDataDetails), syncNodesToUpdate, syncResults, syncErrors);

            Assert.That(syncErrors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task ConditionRefreshAsync_ReturnsGoodWhenMonitoringServer()
        {
            using var manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = ObjectIds.Server,
                Id = 1,
                ManagerHandle = new NodeHandle(ObjectIds.Server, null)
            };
            var items = new List<IEventMonitoredItem> { monitoredItem };
            var context = new OperationContext(new RequestHeader(), null, RequestType.Unknown);

            var result = await manager.ConditionRefreshAsync(context, items).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncResult = syncManager.ConditionRefresh(context, items);
            Assert.That(ServiceResult.IsGood(syncResult), Is.True);
        }

        [Test]
        public async Task SubscribeToEventsAsync_ReturnsBadNodeIdInvalidForUnknownSource()
        {
            using var manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem { NodeId = ObjectIds.Server };
            var context = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription);

            var result = await manager.SubscribeToEventsAsync(context, new object(), 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncResult = syncManager.SubscribeToEvents(context, new object(), 1, monitoredItem, false);
            Assert.That(syncResult.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task SubscribeToAllEventsAsync_ReturnsGoodWhenNoRootNotifiers()
        {
            using var manager = CreateManager();
            var monitoredItem = new TestEventMonitoredItem { NodeId = ObjectIds.Server };
            var context = new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription);

            var result = await manager.SubscribeToAllEventsAsync(context, 1, monitoredItem, false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncResult = syncManager.SubscribeToAllEvents(context, 1, monitoredItem, false);
            Assert.That(ServiceResult.IsGood(syncResult), Is.True);
        }

        [Test]
        public async Task RestoreMonitoredItemsAsync_RestoresStoredItems()
        {
            using var manager = CreateManager();
            _mockServer.Setup(s => s.IsRunning).Returns(false);

            var context = manager.SystemContext;
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
            var identity = new Mock<IUserIdentity>().Object;

            await manager.RestoreMonitoredItemsAsync(itemsToRestore, restoredItems, identity).ConfigureAwait(false);

            Assert.That(storedItem.IsRestored, Is.True);
            Assert.That(restoredItems[0], Is.Not.Null);
        }

        [Test]
        public async Task TransferMonitoredItemsAsync_MarksItemsProcessedAndTriggersResend()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
        public async Task SessionClosingAsync_CompletesWithoutError()
        {
            using var manager = CreateManager();
            var context = new OperationContext(new RequestHeader(), null, RequestType.CloseSession);

            await manager.SessionClosingAsync(context, new NodeId(10), true).ConfigureAwait(false);

            var syncManager = (INodeManager2)manager.SyncNodeManager;
            syncManager.SessionClosing(context, new NodeId(11), false);
        }

        [Test]
        public async Task IsNodeInViewAsync_ReturnsFalseForUnknownHandle()
        {
            using var manager = CreateManager();
            var context = new OperationContext(new RequestHeader(), null, RequestType.Browse);

            bool result = await manager.IsNodeInViewAsync(context, ObjectIds.Server, new object()).ConfigureAwait(false);
            Assert.That(result, Is.False);

            var syncManager = (INodeManager2)manager.SyncNodeManager;
            bool syncResult = syncManager.IsNodeInView(context, ObjectIds.Server, new object());
            Assert.That(syncResult, Is.False);
        }

        [Test]
        public async Task GetPermissionMetadataAsync_ReturnsAccessAndRoleInformation()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);
            node.CreateAsPredefinedNode(context);
            node.NodeId = new NodeId("PermissionNode", nsIdx);
            node.BrowseName = new QualifiedName("PermissionNode", nsIdx);
            node.AccessRestrictions = AccessRestrictionType.SigningRequired;
            node.RolePermissions = new RolePermissionTypeCollection
            {
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)PermissionType.Browse
                }
            };
            node.UserRolePermissions = new RolePermissionTypeCollection
            {
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)PermissionType.Read
                }
            };

            await manager.AddNodeAsync(context, default, node).ConfigureAwait(false);

            var handle = await manager.GetManagerHandleAsync(node.NodeId).ConfigureAwait(false);
            var cache = new Dictionary<NodeId, List<object>>
            {
                [node.NodeId] = new List<object>()
            };
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read);

            var metadata = await manager.GetPermissionMetadataAsync(opContext, handle, BrowseResultMask.All, cache, true).ConfigureAwait(false);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.AccessRestrictions, Is.EqualTo(AccessRestrictionType.SigningRequired));
            Assert.That(metadata.RolePermissions, Is.Not.Null);
            Assert.That(metadata.UserRolePermissions, Is.Not.Null);

            var syncManager = (INodeManager3)manager.SyncNodeManager;
            var syncMetadata = syncManager.GetPermissionMetadata(opContext, handle, BrowseResultMask.All, cache, true);
            Assert.That(syncMetadata, Is.Not.Null);
        }

        [Test]
        public async Task ValidateRolePermissionsAsync_ReturnsGoodWhenPermissionNotRequired()
        {
            using var manager = CreateManager();
            var result = await manager.ValidateRolePermissionsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read),
                NodeId.Null,
                PermissionType.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task ValidateEventRolePermissionsAsync_ReturnsGoodWhenEventInformationMissing()
        {
            using var manager = CreateManager();
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

            var result = await manager.ValidateEventRolePermissionsAsync(monitoredItem, eventState).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task AddRootNotifierAsyncAddsNodeToRootNotifiers()
        {
            using var manager = CreateManager();
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
        public async Task AddRootNotifierAsyncSetsOnReportEventCallback()
        {
            using var manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            Assert.That(notifier.OnReportEvent, Is.Not.Null);
        }

        [Test]
        public async Task AddRootNotifierAsyncAddsHasNotifierReferenceToServer()
        {
            using var manager = CreateManager();
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
        public async Task AddRootNotifierAsyncServerNodeSkipsCallbackAndReference()
        {
            // The Server object itself must not get the HasNotifier→Server reference
            // to prevent infinite recursion in event reporting.
            using var manager = CreateManager();
            var serverNode = new BaseObjectState(null);
            serverNode.NodeId = ObjectIds.Server;
            serverNode.BrowseName = new QualifiedName("Server");

            await manager.AddRootNotifierPublicAsync(serverNode).ConfigureAwait(false);

            Assert.That(manager.RootNotifiers.ContainsKey(ObjectIds.Server), Is.True);
            Assert.That(serverNode.OnReportEvent, Is.Null);
            Assert.That(
                serverNode.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.False);
        }

        [Test]
        public async Task AddRootNotifierAsyncIsIdempotent()
        {
            using var manager = CreateManager();
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
        public async Task RemoveRootNotifierAsyncRemovesFromRootNotifiers()
        {
            using var manager = CreateManager();
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
        public async Task RemoveRootNotifierAsyncClearsOnReportEventCallback()
        {
            using var manager = CreateManager();
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
        public async Task RemoveRootNotifierAsyncRemovesHasNotifierReference()
        {
            using var manager = CreateManager();
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
        public async Task RemoveRootNotifierAsyncIsNoopForUnknownNotifier()
        {
            using var manager = CreateManager();
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
            _mockServer
                .Setup(s => s.ReportEvent(It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>()))
                .Callback<ISystemContext, IFilterTarget>((_, e) => capturedEvent = e);

            using var manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var node = new BaseObjectState(null);
            node.NodeId = new NodeId("EventSource", nsIdx);

            var mockEvent = new Mock<IFilterTarget>().Object;

            manager.InvokeOnReportEvent(manager.SystemContext, node, mockEvent);

            _mockServer.Verify(
                s => s.ReportEvent(It.IsAny<ISystemContext>(), mockEvent),
                Times.Once);
            Assert.That(capturedEvent, Is.SameAs(mockEvent));
        }

        [Test]
        public async Task OnReportEventIsInvokedWhenNodeReportsEvent()
        {
            // Verifies that the callback wired by AddRootNotifierAsync routes events
            // through to IServerInternal.ReportEvent.
            IFilterTarget capturedEvent = null;
            _mockServer
                .Setup(s => s.ReportEvent(It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>()))
                .Callback<ISystemContext, IFilterTarget>((_, e) => capturedEvent = e);

            using var manager = CreateManager();
            ushort nsIdx = manager.NamespaceIndexes[0];
            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(manager.SystemContext);
            notifier.NodeId = new NodeId("Notifier", nsIdx);
            notifier.BrowseName = new QualifiedName("Notifier", nsIdx);

            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            var mockEvent = new Mock<IFilterTarget>().Object;
            notifier.ReportEvent(manager.SystemContext, mockEvent);

            Assert.That(capturedEvent, Is.SameAs(mockEvent));
        }

        [Test]
        public async Task SubscribeToEventsAsyncSucceedsForValidEventNotifierNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("EventSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("EventSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            var handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = eventSource.NodeId,
                Id = 42,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            var result = await manager.SubscribeToEventsAsync(
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
        public async Task SubscribeToEventsAsyncUnsubscribeRemovesEventMonitoredItem()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("EventSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("EventSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            var handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
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

            var unsubResult = await manager.SubscribeToEventsAsync(opContext, handle, 1, monitoredItem, true).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(unsubResult), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.False);
            Assert.That(
                manager.MonitoredNodes.ContainsKey(eventSource.NodeId),
                Is.False,
                "MonitoredNode entry should be cleaned up when no items remain");
        }

        [Test]
        public async Task SubscribeToEventsAsyncReturnsBadNotSupportedForNodeWithoutEventNotifier()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var nonEventSource = new BaseObjectState(null);
            nonEventSource.CreateAsPredefinedNode(context);
            nonEventSource.NodeId = new NodeId("NonEvent", nsIdx);
            nonEventSource.BrowseName = new QualifiedName("NonEvent", nsIdx);
            nonEventSource.EventNotifier = EventNotifiers.None; // no subscribe flag
            await manager.AddNodeAsync(context, default, nonEventSource).ConfigureAwait(false);

            var handle = await manager.GetManagerHandleAsync(nonEventSource.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem { NodeId = nonEventSource.NodeId, Id = 77 };

            var result = await manager.SubscribeToEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription),
                handle,
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task SubscribeToAllEventsAsyncSubscribesToRootNotifiers()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(context);
            notifier.NodeId = new NodeId("AreaNotifier", nsIdx);
            notifier.BrowseName = new QualifiedName("AreaNotifier", nsIdx);
            notifier.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, notifier).ConfigureAwait(false);
            await manager.AddRootNotifierPublicAsync(notifier).ConfigureAwait(false);

            var handle = await manager.GetManagerHandleAsync(notifier.NodeId).ConfigureAwait(false);
            var monitoredItem = new TestEventMonitoredItem
            {
                NodeId = notifier.NodeId,
                Id = 55,
                ManagerHandle = handle,
                MonitoringMode = MonitoringMode.Reporting
            };

            var result = await manager.SubscribeToAllEventsAsync(
                new OperationContext(new RequestHeader(), null, RequestType.CreateSubscription),
                1,
                monitoredItem,
                false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(manager.MonitoredItems.ContainsKey(monitoredItem.Id), Is.True);
            Assert.That(manager.MonitoredNodes.ContainsKey(notifier.NodeId), Is.True);
        }

        [Test]
        public async Task ConditionRefreshAsyncReturnsGoodForManagedMonitoredItem()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];

            var eventSource = new BaseObjectState(null);
            eventSource.CreateAsPredefinedNode(context);
            eventSource.NodeId = new NodeId("ConditionSource", nsIdx);
            eventSource.BrowseName = new QualifiedName("ConditionSource", nsIdx);
            eventSource.EventNotifier = EventNotifiers.SubscribeToEvents;
            await manager.AddNodeAsync(context, default, eventSource).ConfigureAwait(false);

            var handle = await manager.GetManagerHandleAsync(eventSource.NodeId).ConfigureAwait(false);
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

            var result = await manager.ConditionRefreshAsync(refreshContext, items).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task ConditionRefreshAsyncSkipsItemsNotManagedByThisNodeManager()
        {
            using var manager = CreateManager();

            var externalItem = new TestEventMonitoredItem
            {
                NodeId = ObjectIds.RootFolder, // not in this manager's namespace
                Id = 999,
                ManagerHandle = new NodeHandle(ObjectIds.RootFolder, null)
            };

            var result = await manager.ConditionRefreshAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Unknown),
                new List<IEventMonitoredItem> { externalItem }).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(externalItem.QueuedEvents, Is.Empty);
        }

        [Test]
        public async Task AddReverseReferencesAsyncCreatesRootNotifierForInverseHasNotifierToExternalNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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
        public async Task AddReverseReferencesAsyncDoesNotCreateRootNotifierForForwardHasNotifier()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
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

        private TestableAsyncCustomNodeManager CreateManager()
        {
            var manager = new TestableAsyncCustomNodeManager(
                _mockServer.Object,
                _configuration,
                _mockLogger.Object,
                _testNamespaceUri);

            SetupMasterNodeManager(manager);

            return manager;
        }

        private void SetupMasterNodeManager(TestableAsyncCustomNodeManager manager)
        {
            _mockMasterNodeManager
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

        public ILogger Logger => m_logger;

        public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

        public new NodeIdDictionary<NodeState> RootNotifiers => base.RootNotifiers;

        public new NodeIdDictionary<MonitoredNode2> MonitoredNodes => base.MonitoredNodes;

        public new ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => base.MonitoredItems;

        public ValueTask AddRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
            => AddRootNotifierAsync(notifier, cancellationToken);

        public ValueTask RemoveRootNotifierPublicAsync(NodeState notifier, CancellationToken cancellationToken = default)
            => RemoveRootNotifierAsync(notifier, cancellationToken);

        public void InvokeOnReportEvent(ISystemContext context, NodeState node, IFilterTarget filterTarget)
            => OnReportEvent(context, node, filterTarget);

        public ValueTask AddReverseReferencesPublicAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
            => AddReverseReferencesAsync(externalReferences, cancellationToken);

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
    }

    internal class TestEventMonitoredItem : IEventMonitoredItem
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
            var previous = MonitoringMode;
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

        public List<IFilterTarget> QueuedEvents { get; } = new();
    }

    internal class TestStoredMonitoredItem : IStoredMonitoredItem
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
