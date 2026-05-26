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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the MonitoredNode2 class, particularly the event-driven
    /// RolePermissions validation caching behaviour.
    /// </summary>
    [TestFixture]
    [Category("MonitoredNode")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MonitoredNode2Tests
    {
        /// <summary>
        /// Verifies that ValidateRolePermissions is called only once when the same node
        /// value changes multiple times (permission result is cached).
        /// </summary>
        [Test]
        public void OnMonitoredNodeChanged_PermissionsCached_ValidateCalledOnce()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            // Act – fire value-change notification three times
            ISystemContext context = new Mock<ISystemContext>().Object;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Dispose waits for the consumer task to drain all queued notifications.
            monitoredNode.Dispose();

            // Assert – ValidateRolePermissions should have been called exactly once (cached after first call)
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    nodeId,
                    PermissionType.Read,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that the permission cache is invalidated when RolePermissions change,
        /// causing ValidateRolePermissions to be called again on the next value change.
        /// </summary>
        [Test]
        public void OnMonitoredNodeChanged_RolePermissionsChanged_CacheInvalidated()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act – first value change populates the cache
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Simulate a RolePermissions change (invalidates cache)
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.RolePermissions);

            // Second value change should trigger re-validation
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Dispose waits for the consumer task to drain all queued notifications.
            monitoredNode.Dispose();

            // Assert – ValidateRolePermissions should have been called twice (once before and once after cache invalidation)
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    nodeId,
                    PermissionType.Read,
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that when ValidateRolePermissions returns a bad result, the value is not
        /// queued and the result is cached (so validation is not repeated on every change).
        /// </summary>
        [Test]
        public void OnMonitoredNodeChanged_PermissionDenied_ValueNotQueuedAndResultCached()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act – fire value-change notification twice
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Dispose waits for the consumer task to drain all queued notifications.
            monitoredNode.Dispose();

            // Assert – QueueValue should never have been called (permission denied)
            monitoredItemMock.Verify(
                m => m.QueueValue(It.IsAny<DataValue>(), It.IsAny<ServiceResult>()),
                Times.Never);

            // And validate was only called once (cached bad result)
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    nodeId,
                    PermissionType.Read,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that the RolePermissions change mask is set when the RolePermissions
        /// property of a NodeState is modified.
        /// </summary>
        [Test]
        public void NodeState_RolePermissionsPropertyChange_SetsRolePermissionsChangeMask()
        {
            // Arrange & Act
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32,
                RolePermissions = []
            };

            // Assert – both NonValue and RolePermissions bits must be set
            Assert.That(node.ChangeMasks & NodeStateChangeMasks.NonValue, Is.Not.EqualTo(NodeStateChangeMasks.None));
            Assert.That(node.ChangeMasks & NodeStateChangeMasks.RolePermissions, Is.Not.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Verifies that the RolePermissions change mask is set when the UserRolePermissions
        /// property of a NodeState is modified.
        /// </summary>
        [Test]
        public void NodeState_UserRolePermissionsPropertyChange_SetsRolePermissionsChangeMask()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32,
                UserRolePermissions = [] // act
            };

            // Assert – both NonValue and RolePermissions bits must be set
            Assert.That(node.ChangeMasks & NodeStateChangeMasks.NonValue, Is.Not.EqualTo(NodeStateChangeMasks.None));
            Assert.That(node.ChangeMasks & NodeStateChangeMasks.RolePermissions, Is.Not.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Verifies that after setting RolePermissions and calling ClearChangeMasks,
        /// the OnStateChanged callback is called with NodeStateChangeMasks.RolePermissions set.
        /// </summary>
        [Test]
        public void NodeState_ClearChangeMasks_FiresCallbackWithRolePermissionsMask()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            NodeStateChangeMasks capturedMask = NodeStateChangeMasks.None;
            node.OnStateChanged = (_, _, masks) => capturedMask = masks;

            ISystemContext contextMock = new Mock<ISystemContext>().Object;

            // Act – change RolePermissions and then clear masks
            node.RolePermissions = [];
            node.ClearChangeMasks(contextMock, false);

            // Assert – callback should have received the RolePermissions mask
            Assert.That(capturedMask & NodeStateChangeMasks.RolePermissions, Is.Not.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Verifies that the permission cache is invalidated when the
        /// <see cref="IConfigurationNodeManager.DefaultPermissionsChanged"/> event fires,
        /// causing <c>ValidateRolePermissions</c> to be called again on the next value change.
        /// </summary>
        [Test]
        public void OnMonitoredNodeChanged_DefaultPermissionsChanged_CacheInvalidated()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            using var firstValidationSignal = new System.Threading.ManualResetEventSlim(false);
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    firstValidationSignal.Set();
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            // Set up a ConfigurationNodeManager mock that exposes the DefaultPermissionsChanged event
            var configNodeManagerMock = new Mock<IConfigurationNodeManager>();
            EventHandler capturedHandler = null;
            configNodeManagerMock
                .SetupAdd(m => m.DefaultPermissionsChanged += It.IsAny<EventHandler>())
                .Callback<EventHandler>(h => capturedHandler = h);

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);
            serverMock.Setup(s => s.ConfigurationNodeManager).Returns(configNodeManagerMock.Object);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);
            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // First value change populates the cache
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Wait until the consumer has processed the first notification and populated the cache.
            firstValidationSignal.Wait(TimeSpan.FromSeconds(30));

            // Simulate namespace DefaultPermissionsChanged event firing
            Assert.That(capturedHandler, Is.Not.Null, "DefaultPermissionsChanged handler should have been subscribed");
            capturedHandler.Invoke(configNodeManagerMock.Object, EventArgs.Empty);

            // Second value change should trigger re-validation since cache was cleared
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Dispose waits for the consumer task to drain all queued notifications.
            monitoredNode.Dispose();

            // Assert – ValidateRolePermissions should have been called twice (once before and once after cache invalidation)
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(It.IsAny<OperationContext>(), nodeId, PermissionType.Read, It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that all value changes are delivered to the monitored item in FIFO order
        /// when multiple changes are queued before the first one is processed.
        /// The semaphore holds up the first validation so that subsequent changes pile up
        /// in the channel; once released every change must be delivered – none dropped.
        /// </summary>
        [Test]
        public void AllValueChanges_NoneDropped_WhenNodeChangesWhileValidationIsBlocked()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = 0
            };

            // Gate that blocks the first (and only) permission validation call.
            using var validationGate = new SemaphoreSlim(0, 1);
            using var validationStarted = new ManualResetEventSlim(false);

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns<OperationContext, NodeId, PermissionType, CancellationToken>(
                    (_, _, _, ct) =>
                    {
                        validationStarted.Set();
                        return new ValueTask<ServiceResult>(
                            validationGate.WaitAsync(ct).ContinueWith(
                                _ => ServiceResult.Good,
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnRanToCompletion,
                                TaskScheduler.Default));
                    });

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act – first change: consumer will block on validation
            node.Value = 10;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Wait until the consumer has started validation so it is definitely blocked
            validationStarted.Wait(TimeSpan.FromSeconds(30));

            // While blocked, enqueue two more changes; each call snapshots the value immediately
            node.Value = 20;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            node.Value = 30;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Release validation – all three notifications should now be processed in order
            validationGate.Release();

            // Dispose drains the channel before returning
            monitoredNode.Dispose();

            // Extract the DataValue arguments from the recorded QueueValue invocations
            var deliveredValues = monitoredItemMock.Invocations
                .Where(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue))
                .Select(i => ((DataValue)i.Arguments[0]).WrappedValue.AsBoxedObject())
                .ToList();

            // Assert – every change was delivered, none dropped
            Assert.That(deliveredValues, Has.Count.EqualTo(3));
            Assert.That(deliveredValues, Is.EqualTo(new object[] { 10, 20, 30 }));

            // Validation should have been called only once (result cached for changes 2 and 3)
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    nodeId,
                    PermissionType.Read,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that each value-change snapshot captures the node value at the time
        /// <see cref="MonitoredNode2.OnMonitoredNodeChanged"/> is called (enqueue-time),
        /// not at the time the consumer processes the snapshot (dequeue-time).
        /// </summary>
        [Test]
        public void ValueChangeSnapshot_CapturesNodeValueAtEnqueueTime_NotAtProcessingTime()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = 0
            };

            using var validationGate = new SemaphoreSlim(0, 1);
            using var validationStarted = new ManualResetEventSlim(false);

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns<OperationContext, NodeId, PermissionType, CancellationToken>(
                    (_, _, _, ct) =>
                    {
                        validationStarted.Set();
                        return new ValueTask<ServiceResult>(
                            validationGate.WaitAsync(ct).ContinueWith(
                                _ => ServiceResult.Good,
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnRanToCompletion,
                                TaskScheduler.Default));
                    });

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Enqueue snapshot with value=100
            node.Value = 100;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            validationStarted.Wait(TimeSpan.FromSeconds(30));

            // Enqueue second snapshot while consumer is blocked; value at enqueue time is 200
            node.Value = 200;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Mutate the node value to something else BEFORE releasing the consumer;
            // the already-captured snapshots must not be affected
            node.Value = 999;

            validationGate.Release();
            monitoredNode.Dispose();

            // Assert – snapshots reflect the values at enqueue time (100, 200), not the final value (999)
            var deliveredValues = monitoredItemMock.Invocations
                .Where(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue))
                .Select(i => ((DataValue)i.Arguments[0]).WrappedValue.AsBoxedObject())
                .ToList();
            Assert.That(deliveredValues, Has.Count.EqualTo(2));
            Assert.That(deliveredValues, Is.EqualTo(new object[] { 100, 200 }));
        }

        /// <summary>
        /// Verifies that when multiple monitored items are registered on the same node,
        /// all of them receive every value-change notification.
        /// </summary>
        [Test]
        public void MultipleMonitoredItems_AllReceiveEveryValueChange()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = 0
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> item1Mock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);
            Mock<IDataChangeMonitoredItem2> item2Mock = CreateDataChangeMonitoredItemMock(2u, Attributes.Value);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(item1Mock.Object);
            monitoredNode.Add(item2Mock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act – fire three value changes
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            monitoredNode.Dispose();

            // Assert – each item received all three changes
            int item1Count = item1Mock.Invocations
                .Count(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue));
            int item2Count = item2Mock.Invocations
                .Count(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue));
            Assert.That(item1Count, Is.EqualTo(3), "item1 should have received 3 notifications");
            Assert.That(item2Count, Is.EqualTo(3), "item2 should have received 3 notifications");
        }

        /// <summary>
        /// Verifies that non-value attribute changes (e.g. DisplayName) are delivered to the
        /// monitored item without invoking permission validation, because role-permission checks
        /// only apply to the Value attribute.
        /// </summary>
        [Test]
        public void NonValueAttributeChange_DeliveredWithoutPermissionValidation()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32,
                DisplayName = new LocalizedText("initial")
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock =
                CreateDataChangeMonitoredItemMock(1u, Attributes.DisplayName);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act – fire a non-value attribute change three times
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.NonValue);
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.NonValue);
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.NonValue);

            monitoredNode.Dispose();

            // Assert – value was queued for each change
            // (QueueValue is recorded on the base IDataChangeMonitoredItem interface by Moq;
            //  use Invocations to count it reliably across both interface levels)
            int queueCount = monitoredItemMock.Invocations
                .Count(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue));
            Assert.That(queueCount, Is.EqualTo(3));

            // No permission validation should have been performed for non-value attributes
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that value changes queued before a RolePermissions-change notification is
        /// processed are not affected by the cache invalidation – only changes processed AFTER
        /// the invalidation require a fresh validation call.
        /// </summary>
        [Test]
        public void AllValueChanges_NoneDropped_WhenRolePermissionsChangeIsInterleavedAndValidationIsBlocked()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = 0
            };

            // Block the first validation so we can interleave a RolePermissions change
            using var firstValidationGate = new SemaphoreSlim(0, 1);
            using var firstValidationStarted = new ManualResetEventSlim(false);
            int validationCallCount = 0;

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns<OperationContext, NodeId, PermissionType, CancellationToken>(
                    (_, _, _, ct) =>
                    {
                        int callIndex = Interlocked.Increment(ref validationCallCount);
                        if (callIndex == 1)
                        {
                            firstValidationStarted.Set();
                            return new ValueTask<ServiceResult>(
                                firstValidationGate.WaitAsync(ct).ContinueWith(
                                    _ => ServiceResult.Good,
                                    CancellationToken.None,
                                    TaskContinuationOptions.OnlyOnRanToCompletion,
                                    TaskScheduler.Default));
                        }

                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock = CreateDataChangeMonitoredItemMock(1u, Attributes.Value);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // First value change – consumer blocks on validation
            node.Value = 10;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);
            firstValidationStarted.Wait(TimeSpan.FromSeconds(30));

            // While consumer is blocked, enqueue a RolePermissions change followed by another value change
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.RolePermissions);
            node.Value = 20;
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Release validation
            firstValidationGate.Release();

            monitoredNode.Dispose();

            // Assert – both value changes must have been delivered
            var deliveredValues = monitoredItemMock.Invocations
                .Where(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue))
                .Select(i => ((DataValue)i.Arguments[0]).WrappedValue.AsBoxedObject())
                .ToList();
            Assert.That(deliveredValues, Has.Count.EqualTo(2));
            Assert.That(deliveredValues, Is.EqualTo(new object[] { 10, 20 }));

            // Validation must have been called twice: once before and once after cache invalidation
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    nodeId,
                    PermissionType.Read,
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        // =====================================================================
        // Event handling tests
        // =====================================================================

        /// <summary>
        /// Verifies that a single event fired on a node is delivered to the registered
        /// event monitored item via the channel consumer.
        /// </summary>
        [Test]
        public void OnReportEvent_SingleEvent_DeliveredToMonitoredItem()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IEventMonitoredItem> eventItemMock = CreateEventMonitoredItemMock(1u);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(eventItemMock.Object);

            var eventState = new BaseEventState(null);
            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act
            monitoredNode.OnReportEvent(context, node, eventState);
            monitoredNode.Dispose();

            // Assert – QueueEvent called exactly once
            int queueCount = eventItemMock.Invocations
                .Count(i => i.Method.Name == nameof(IEventMonitoredItem.QueueEvent));
            Assert.That(queueCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that all events fired while validation is blocked are preserved and
        /// delivered in order — none are dropped by the bounded channel.
        /// </summary>
        [Test]
        public void OnReportEvent_AllEvents_NoneDropped_WhenValidationIsBlocked()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            using var validationGate = new SemaphoreSlim(0, 1);
            using var validationStarted = new ManualResetEventSlim(false);
            int validationCallCount = 0;

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    validationStarted.Set();
                    int callIndex = Interlocked.Increment(ref validationCallCount);
                    if (callIndex == 1)
                    {
                        return new ValueTask<ServiceResult>(
                                    validationGate.WaitAsync().ContinueWith(
                                        _ => ServiceResult.Good,
                                        CancellationToken.None,
                                        TaskContinuationOptions.OnlyOnRanToCompletion,
                                        TaskScheduler.Default));
                    }

                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IEventMonitoredItem> eventItemMock = CreateEventMonitoredItemMock(1u);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(eventItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act – fire first event; consumer blocks on validation
            monitoredNode.OnReportEvent(context, node, new BaseEventState(null));
            validationStarted.Wait(TimeSpan.FromSeconds(30));

            // While blocked, enqueue two more events
            monitoredNode.OnReportEvent(context, node, new BaseEventState(null));
            monitoredNode.OnReportEvent(context, node, new BaseEventState(null));

            // Release and drain
            validationGate.Release();
            monitoredNode.Dispose();

            // Assert – all three events delivered
            int queueCount = eventItemMock.Invocations
                .Count(i => i.Method.Name == nameof(IEventMonitoredItem.QueueEvent));
            Assert.That(queueCount, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies that the event snapshot is taken at the time <see cref="OnReportEvent"/> is
        /// called. Mutating the original event instance after enqueueing must not alter what the
        /// consumer delivers to the monitored item.
        /// </summary>
        [Test]
        public void OnReportEvent_EventSnapshot_CapturesStateAtEnqueueTime_NotAtProcessingTime()
        {
            // Arrange
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            using var validationGate = new SemaphoreSlim(0, 1);
            using var validationStarted = new ManualResetEventSlim(false);
            int validationCallCount = 0;

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    validationStarted.Set();
                    int callIndex = Interlocked.Increment(ref validationCallCount);
                    if (callIndex == 1)
                    {
                        return new ValueTask<ServiceResult>(
                                    validationGate.WaitAsync().ContinueWith(
                                        _ => ServiceResult.Good,
                                        CancellationToken.None,
                                        TaskContinuationOptions.OnlyOnRanToCompletion,
                                        TaskScheduler.Default));
                    }

                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            // Capture the IFilterTarget that reaches QueueEvent so we can inspect it.
            IFilterTarget deliveredTarget = null;

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IEventMonitoredItem> eventItemMock = CreateEventMonitoredItemMock(1u);
            eventItemMock
                .Setup(m => m.QueueEvent(It.IsAny<IFilterTarget>()))
                .Callback<IFilterTarget>(t => deliveredTarget = t);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(eventItemMock.Object);

            // Create a concrete instance state and enqueue it
            var originalEvent = new BaseObjectState(null)
            {
                NodeId = new NodeId("event1", 1),
                BrowseName = new QualifiedName("event1", 1)
            };

            ISystemContext context = new Mock<ISystemContext>().Object;
            monitoredNode.OnReportEvent(context, node, originalEvent);
            validationStarted.Wait(TimeSpan.FromSeconds(30));

            // Mutate the original after enqueue – the snapshot must not reflect this change
            originalEvent.BrowseName = new QualifiedName("mutated", 1);

            validationGate.Release();
            monitoredNode.Dispose();

            // Assert – the delivered target is an InstanceStateSnapshot (proving a deep copy was
            // taken at enqueue time). Its Handle points back to the original event instance;
            // the snapshot itself is independent of any subsequent mutation to that instance.
            Assert.That(deliveredTarget, Is.Not.Null);
            Assert.That(deliveredTarget, Is.InstanceOf<InstanceStateSnapshot>());
            var snapshot = (InstanceStateSnapshot)deliveredTarget;
            // The snapshot's Handle is the original (now mutated) event, confirming that the
            // snapshot was captured FROM that instance but is structurally independent.
            Assert.That(snapshot.Handle, Is.SameAs(originalEvent));
            // The original browse name was changed to "mutated" AFTER the snapshot was taken;
            // the original node is now mutated, which confirms our enqueue happened before mutation.
            Assert.That(originalEvent.BrowseName, Is.EqualTo(new QualifiedName("mutated", 1)));
        }

        /// <summary>
        /// Verifies that when <see cref="IAsyncNodeManager.ValidateEventRolePermissionsAsync"/>
        /// returns a bad result the event is silently dropped and never passed to
        /// <see cref="IEventMonitoredItem.QueueEvent"/>.
        /// </summary>
        [Test]
        public void OnReportEvent_PermissionDenied_EventNotQueued()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IEventMonitoredItem> eventItemMock = CreateEventMonitoredItemMock(1u);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(eventItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act
            monitoredNode.OnReportEvent(context, node, new BaseEventState(null));
            monitoredNode.Dispose();

            // Assert – QueueEvent must never have been called
            int queueCount = eventItemMock.Invocations
                .Count(i => i.Method.Name == nameof(IEventMonitoredItem.QueueEvent));
            Assert.That(queueCount, Is.Zero);
        }

        /// <summary>
        /// Verifies that audit events are suppressed when <see cref="IServerInternal.Auditing"/>
        /// is false – no call to <see cref="IEventMonitoredItem.QueueEvent"/> should be made.
        /// </summary>
        [Test]
        public void OnReportEvent_AuditEvent_DroppedWhenAuditingDisabled()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            // Auditing is DISABLED on the server
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IEventMonitoredItem> eventItemMock = CreateEventMonitoredItemMock(1u);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(eventItemMock.Object);

            // Fire an audit event – should be filtered out because Auditing == false
            var auditEvent = new AuditEventState(null);
            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act
            monitoredNode.OnReportEvent(context, node, auditEvent);
            monitoredNode.Dispose();

            // Assert – nothing queued
            int queueCount = eventItemMock.Invocations
                .Count(i => i.Method.Name == nameof(IEventMonitoredItem.QueueEvent));
            Assert.That(queueCount, Is.Zero);
        }

        /// <summary>
        /// Verifies that multiple event monitored items registered on the same node all
        /// receive every event notification.
        /// </summary>
        [Test]
        public void OnReportEvent_MultipleEventMonitoredItems_AllReceiveEveryEvent()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IEventMonitoredItem> item1Mock = CreateEventMonitoredItemMock(1u);
            Mock<IEventMonitoredItem> item2Mock = CreateEventMonitoredItemMock(2u);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(item1Mock.Object);
            monitoredNode.Add(item2Mock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Act – fire three events
            monitoredNode.OnReportEvent(context, node, new BaseEventState(null));
            monitoredNode.OnReportEvent(context, node, new BaseEventState(null));
            monitoredNode.OnReportEvent(context, node, new BaseEventState(null));

            monitoredNode.Dispose();

            // Assert – both items received all three events
            int item1Count = item1Mock.Invocations
                .Count(i => i.Method.Name == nameof(IEventMonitoredItem.QueueEvent));
            int item2Count = item2Mock.Invocations
                .Count(i => i.Method.Name == nameof(IEventMonitoredItem.QueueEvent));
            Assert.That(item1Count, Is.EqualTo(3), "item1 should have received 3 events");
            Assert.That(item2Count, Is.EqualTo(3), "item2 should have received 3 events");
        }

        /// <summary>
        /// Verifies that when the event context carries a session ID that does not match the
        /// monitored item's session, the event is not delivered to that item.
        /// </summary>
        [Test]
        public void OnReportEvent_SessionContextMismatch_EventNotDeliveredToOtherSession()
        {
            // Arrange
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("testNode", 1),
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            // Monitored item belongs to session "A"
            var sessionAId = new NodeId("sessionA", 1);
            Mock<IEventMonitoredItem> eventItemMock = CreateEventMonitoredItemMockWithSession(1u, sessionAId);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(eventItemMock.Object);

            // Context identifies session "B" – should not reach item for session "A"
            var sessionBId = new NodeId("sessionB", 1);
            var sessionContextMock = new Mock<ISessionSystemContext>();
            sessionContextMock.Setup(c => c.SessionId).Returns(sessionBId);

            // Act
            monitoredNode.OnReportEvent(sessionContextMock.Object, node, new BaseEventState(null));
            monitoredNode.Dispose();

            // Assert – event must not have been queued for the item in a different session
            int queueCount = eventItemMock.Invocations
                .Count(i => i.Method.Name == nameof(IEventMonitoredItem.QueueEvent));
            Assert.That(queueCount, Is.Zero);
        }

        private static Mock<IEventMonitoredItem> CreateEventMonitoredItemMock(uint id)
        {
            var sessionMock = new Mock<ISession>();
            var identityMock = new Mock<IUserIdentity>();
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(identityMock.Object);

            var eventItemMock = new Mock<IEventMonitoredItem>();
            eventItemMock.Setup(m => m.Id).Returns(id);
            eventItemMock.Setup(m => m.Session).Returns(sessionMock.Object);

            return eventItemMock;
        }

        private static Mock<IEventMonitoredItem> CreateEventMonitoredItemMockWithSession(
            uint id,
            NodeId sessionId)
        {
            var sessionMock = new Mock<ISession>();
            var identityMock = new Mock<IUserIdentity>();
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(identityMock.Object);
            sessionMock.Setup(s => s.Id).Returns(sessionId);

            var eventItemMock = new Mock<IEventMonitoredItem>();
            eventItemMock.Setup(m => m.Id).Returns(id);
            eventItemMock.Setup(m => m.Session).Returns(sessionMock.Object);

            return eventItemMock;
        }

        private static Mock<IDataChangeMonitoredItem2> CreateDataChangeMonitoredItemMock(
            uint id,
            uint attributeId)
        {
            var sessionMock = new Mock<ISession>();
            var identityMock = new Mock<IUserIdentity>();
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(identityMock.Object);

            var monitoredItemMock = new Mock<IDataChangeMonitoredItem2>();
            monitoredItemMock.Setup(m => m.Id).Returns(id);
            monitoredItemMock.Setup(m => m.AttributeId).Returns(attributeId);
            monitoredItemMock.Setup(m => m.IndexRange).Returns(NumericRange.Null);
            monitoredItemMock.Setup(m => m.DataEncoding).Returns(QualifiedName.Null);
            monitoredItemMock.Setup(m => m.Session).Returns(sessionMock.Object);
            monitoredItemMock.Setup(m => m.EffectiveIdentity).Returns(identityMock.Object);

            return monitoredItemMock;
        }

        /// <summary>
        /// Verifies that <see cref="MonitoredNode2.InvalidatePermissionCacheForSession"/> clears
        /// the cached permission result for all monitored items belonging to the specified session,
        /// causing <c>ValidateRolePermissions</c> to be called again on the next value change.
        /// </summary>
        [Test]
        public void InvalidatePermissionCacheForSessionClearsPermissionCacheForMatchingSession()
        {
            // Arrange
            var sessionId = new NodeId("session1", 1);
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            using var firstValidationSignal = new System.Threading.ManualResetEventSlim(false);
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    firstValidationSignal.Set();
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            Mock<IDataChangeMonitoredItem2> monitoredItemMock =
                CreateDataChangeMonitoredItemMockWithSession(1u, Attributes.Value, sessionId);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Populate the permission cache with the first value change
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Wait until the consumer has processed the first notification and populated the cache.
            firstValidationSignal.Wait(TimeSpan.FromSeconds(30));

            // Act – invalidate permission cache for the session (simulates identity change)
            monitoredNode.InvalidatePermissionCacheForSession(sessionId);

            // Next value change should trigger re-validation
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Dispose waits for the consumer task to drain all queued notifications.
            monitoredNode.Dispose();

            // Assert – called twice: once before and once after cache invalidation
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(It.IsAny<OperationContext>(), nodeId, PermissionType.Read, It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that <see cref="MonitoredNode2.InvalidatePermissionCacheForSession"/> does NOT
        /// clear the permission cache for monitored items belonging to a different session.
        /// </summary>
        [Test]
        public void InvalidatePermissionCacheForSessionDoesNotClearCacheForOtherSession()
        {
            // Arrange
            var sessionId = new NodeId("session1", 1);
            var otherSessionId = new NodeId("session2", 1);
            var nodeId = new NodeId("testNode", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("testNode", 1),
                DataType = DataTypeIds.Int32
            };

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Auditing).Returns(false);

            // Monitored item belongs to sessionId, not otherSessionId
            Mock<IDataChangeMonitoredItem2> monitoredItemMock =
                CreateDataChangeMonitoredItemMockWithSession(1u, Attributes.Value, sessionId);

            var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(monitoredItemMock.Object);

            ISystemContext context = new Mock<ISystemContext>().Object;

            // Populate the cache
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Act – invalidate for a DIFFERENT session
            monitoredNode.InvalidatePermissionCacheForSession(otherSessionId);

            // Next value change should still use the cached result
            monitoredNode.OnMonitoredNodeChanged(context, node, NodeStateChangeMasks.Value);

            // Dispose waits for the consumer task to drain all queued notifications.
            monitoredNode.Dispose();

            // Assert – ValidateRolePermissions still called only once (cache was not cleared)
            nodeManagerMock.Verify(
                m => m.ValidateRolePermissionsAsync(It.IsAny<OperationContext>(), nodeId, PermissionType.Read, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static Mock<IDataChangeMonitoredItem2> CreateDataChangeMonitoredItemMockWithSession(
            uint id,
            uint attributeId,
            NodeId sessionId)
        {
            var sessionMock = new Mock<ISession>();
            var identityMock = new Mock<IUserIdentity>();
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(identityMock.Object);
            sessionMock.Setup(s => s.Id).Returns(sessionId);

            var monitoredItemMock = new Mock<IDataChangeMonitoredItem2>();
            monitoredItemMock.Setup(m => m.Id).Returns(id);
            monitoredItemMock.Setup(m => m.AttributeId).Returns(attributeId);
            monitoredItemMock.Setup(m => m.IndexRange).Returns(NumericRange.Null);
            monitoredItemMock.Setup(m => m.DataEncoding).Returns(QualifiedName.Null);
            monitoredItemMock.Setup(m => m.Session).Returns(sessionMock.Object);
            monitoredItemMock.Setup(m => m.EffectiveIdentity).Returns(identityMock.Object);

            return monitoredItemMock;
        }
    }
}
