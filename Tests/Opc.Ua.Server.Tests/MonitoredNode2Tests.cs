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
            var firstValidationSignal = new System.Threading.ManualResetEventSlim(false);
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
            firstValidationSignal.Wait();

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
            var firstValidationSignal = new System.Threading.ManualResetEventSlim(false);
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
            firstValidationSignal.Wait();

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
