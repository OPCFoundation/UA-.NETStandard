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
using System.Threading;
using System.Threading.Tasks;
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

            _namespaceTable = new NamespaceTable();
            _namespaceTable.Append(_testNamespaceUri);

            _mockServer.Setup(s => s.NamespaceUris).Returns(_namespaceTable);
            _mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            _mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(_namespaceTable));
            _mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            _mockServer.Setup(s => s.NodeManager).Returns(_mockMasterNodeManager.Object);

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
        }

        [Test]
        public async Task CreateNodeAsync_AddsNodeToPredefinedNodes()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null)
            {
                NodeId = new NodeId("MyObject", nsIdx),
                BrowseName = new QualifiedName("MyObject", nsIdx)
            };

            // Act
            var resultNodeId = await manager.CreateNodeAsync(context,
                NodeId.Null,
                ReferenceTypeIds.Organizes,
                new QualifiedName("MyObject", nsIdx),
                baseObject).ConfigureAwait(false);

            // Assert
            Assert.That(resultNodeId, Is.EqualTo(baseObject.NodeId));
            Assert.That(manager.Find(baseObject.NodeId), Is.Not.Null);
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.True);
        }

        [Test]
        public async Task DeleteNodeAsync_RemovesNodeFromPredefinedNodes()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null)
            {
                NodeId = new NodeId("MyObject", nsIdx),
                BrowseName = new QualifiedName("MyObject", nsIdx)
            };

            await manager.CreateNodeAsync(
                context,
                NodeId.Null,
                ReferenceTypeIds.Organizes,
                new QualifiedName("MyObject", nsIdx),
                baseObject).ConfigureAwait(false);
            Assert.That(manager.Find(baseObject.NodeId), Is.Not.Null);

            // Act
            var result = await manager.DeleteNodeAsync(context, baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(manager.Find(baseObject.NodeId), Is.Null);
            Assert.That(manager.PredefinedNodes.ContainsKey(baseObject.NodeId), Is.False);
        }

        [Test]
        public async Task GetManagerHandleAsync_ReturnsHandleForExistingNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null)
            {
                NodeId = new NodeId("MyObject", nsIdx),
                BrowseName = new QualifiedName("MyObject", nsIdx),
                WriteMask = AttributeWriteMask.None
            };

            NodeId nodeID = await manager.CreateNodeAsync(context,
                NodeId.Null,
                ReferenceTypeIds.Organizes,
                new QualifiedName("MyObject", nsIdx),
                baseObject).ConfigureAwait(false);

            // Act
            var handle = await manager.GetManagerHandleAsync(baseObject.NodeId).ConfigureAwait(false);

            // Assert
            Assert.That(nodeID, Is.Not.EqualTo(NodeId.Null));
            Assert.That(handle, Is.InstanceOf<NodeHandle>());
            var nodeHandle = handle as NodeHandle;
            Assert.That(nodeHandle.NodeId, Is.EqualTo(baseObject.NodeId));
            Assert.That(nodeHandle.Node, Is.SameAs(baseObject));
            Assert.That(nodeHandle.Validated, Is.True);
        }

        [Test]
        public async Task ReadAsync_ReadsValueFromNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("MyVar", nsIdx),
                BrowseName = new QualifiedName("MyVar", nsIdx),
                Value = 42,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            await manager.CreateNodeAsync(context, NodeId.Null, ReferenceTypeIds.Organizes, new QualifiedName("MyVar", nsIdx), variable).ConfigureAwait(false);

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
        }

        [Test]
        public async Task WriteAsync_WritesValueToNode()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("MyVar", nsIdx),
                BrowseName = new QualifiedName("MyVar", nsIdx),
                Value = 0,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };

            await manager.CreateNodeAsync(context, NodeId.Null, ReferenceTypeIds.Organizes, new QualifiedName("MyVar", nsIdx), variable).ConfigureAwait(false);

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
        }

        [Test]
        public async Task WriteAsync_PublishesValueToMonitoredItemQueue()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("MyVarQueue", nsIdx),
                BrowseName = new QualifiedName("MyVarQueue", nsIdx),
                Value = 0,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };

            await manager.CreateNodeAsync(
                context,
                NodeId.Null,
                ReferenceTypeIds.Organizes,
                new QualifiedName("MyVarQueue", nsIdx),
                variable).ConfigureAwait(false);

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

            // Ensure the monitored item is ready before publishing.
            var publishReady = SpinWait.SpinUntil(() => monitoredItem.IsReadyToPublish, TimeSpan.FromMilliseconds(500));
            Assert.That(publishReady, Is.True);

            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            var hadMore = monitoredItem.Publish(
                new OperationContext(new RequestHeader(), null, RequestType.Publish),
                notifications,
                diagnostics,
                10,
                _mockLogger.Object);

            Assert.That(hadMore, Is.False);
            Assert.That(notifications.Count, Is.EqualTo(1));
            var notification = notifications.Dequeue();
            Assert.That(notification.Value.Value, Is.EqualTo(123));
        }

        [Test]
        public async Task AddReferencesAsync_AddsExternalReferences()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var baseObject = new BaseObjectState(null)
            {
                NodeId = new NodeId("MyObject", nsIdx),
                BrowseName = new QualifiedName("MyObject", nsIdx)
            };
            await manager.CreateNodeAsync(
                context,
                NodeId.Null,
                ReferenceTypeIds.Organizes,
                new QualifiedName("MyObject", nsIdx),
                baseObject).ConfigureAwait(false);

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
        }

        [Test]
        public async Task CreateMonitoredItemsAsync_CreatesItem()
        {
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("MyVar", nsIdx),
                BrowseName = new QualifiedName("MyVar", nsIdx),
                Value = 10,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead
            };
            await manager.CreateNodeAsync(context, NodeId.Null, ReferenceTypeIds.Organizes, new QualifiedName("MyVar", nsIdx), variable).ConfigureAwait(false);

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
        }

        [Test]
        public async Task ModifyMonitoredItemsAsync_ModifiesItem()
        {
            // Setup manager and node
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("MyVar", nsIdx),
                BrowseName = new QualifiedName("MyVar", nsIdx),
                Value = 10,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead
            };
            await manager.CreateNodeAsync(context, NodeId.Null, ReferenceTypeIds.Organizes, new QualifiedName("MyVar", nsIdx), variable).ConfigureAwait(false);

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
        }

        [Test]
        public async Task SetMonitoringModeAsync_ChangesMode()
        {
            // Setup manager and node
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("MyVar", nsIdx),
                BrowseName = new QualifiedName("MyVar", nsIdx),
                Value = 10,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead
            };
            await manager.CreateNodeAsync(context, NodeId.Null, ReferenceTypeIds.Organizes, new QualifiedName("MyVar", nsIdx), variable).ConfigureAwait(false);

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
        }

        [Test]
        public async Task DeleteMonitoredItemsAsync_DeletesItem()
        {
            // Setup manager and node
            using var manager = CreateManager();
            var context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("MyVar", nsIdx),
                BrowseName = new QualifiedName("MyVar", nsIdx),
                Value = 10,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead
            };
            await manager.CreateNodeAsync(context, NodeId.Null, ReferenceTypeIds.Organizes, new QualifiedName("MyVar", nsIdx), variable).ConfigureAwait(false);

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

            Assert.That(monitoredItems[0], Is.Not.Null);

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

        public new NodeIdDictionary<MonitoredNode2> MonitoredNodes => base.MonitoredNodes;

        public new ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => base.MonitoredItems;
    }
}
