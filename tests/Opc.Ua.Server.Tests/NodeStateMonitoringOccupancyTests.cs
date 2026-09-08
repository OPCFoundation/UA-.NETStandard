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

#nullable enable

using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Checks callback occupancy while the real <see cref="MonitoredNode2"/> adds and removes items.
    /// </summary>
    /// <remarks>
    /// Server and node-manager dependencies are mocked; no server is started.
    /// Failure-only CSV rows report the two monitoring callbacks and the total occupied behavior callbacks.
    /// </remarks>
    [TestFixture]
    [Category("NodeStateOccupancy")]
    [Category("MonitoredNode")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class NodeStateMonitoringOccupancyTests
    {
        /// <summary>
        /// Clears the diagnostic rows before each test.
        /// </summary>
        [SetUp]
        public void ClearRows()
        {
            m_rows.Clear();
        }

        /// <summary>
        /// Outputs buffered CSV rows only when the test does not pass.
        /// </summary>
        [TearDown]
        public void PrintRowsOnFailure()
        {
            if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Passed)
            {
                TestContext.Out.WriteLine(
                    k_csvHeader +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, m_rows));
            }
        }

        /// <summary>
        /// Tracks the callback assignment lifecycle for a data-change monitored variable.
        /// </summary>
        [Test]
        public void DataChangeMonitoredItemCallbackLifecycle()
        {
            var nodeId = new NodeId("occupancyVar", 1);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("occupancyVar", 1),
                DataType = DataTypeIds.Int32
            };

            (Mock<IAsyncNodeManager> nodeManagerMock, Mock<IServerInternal> serverMock) = CreateMinimalMocks();
            Mock<IDataChangeMonitoredItem2> itemMock = CreateDataChangeItemMock(10u);

            m_rows.Add(BuildRow("Before", node));
            Assert.That(node.OnStateChangedAsync, Is.Null, "OnStateChangedAsync must be null before monitoring.");
            Assert.That(TotalBehaviorNonNull(node), Is.Zero);

            using var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);

            monitoredNode.Add(itemMock.Object);
            m_rows.Add(BuildRow("DataChangeAdded", node));
            Assert.That(TotalBehaviorNonNull(node), Is.EqualTo(1));
            Assert.That(node.OnStateChangedAsync, Is.Not.Null,
                "MonitoredNode2.Add(IDataChangeMonitoredItem2) must install OnStateChangedAsync.");
            Assert.That(node.OnReportEventAsync, Is.Null,
                "OnReportEventAsync must remain null for data-change-only monitoring.");

            monitoredNode.Remove(itemMock.Object);
            m_rows.Add(BuildRow("DataChangeRemoved", node));
            Assert.That(TotalBehaviorNonNull(node), Is.Zero);
            Assert.That(node.OnStateChangedAsync, Is.Null,
                "OnStateChangedAsync must be cleared after last data-change item removed.");
        }

        /// <summary>
        /// Tracks the callback assignment lifecycle for an event monitored item.
        /// </summary>
        [Test]
        public void EventMonitoredItemCallbackLifecycle()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("occupancyObj", 1),
                BrowseName = new QualifiedName("occupancyObj", 1)
            };

            (Mock<IAsyncNodeManager> nodeManagerMock, Mock<IServerInternal> serverMock) = CreateMinimalMocks();
            Mock<IEventMonitoredItem> eventItemMock = CreateEventItemMock(20u);

            m_rows.Add(BuildRow("EventBefore", node));
            Assert.That(node.OnReportEventAsync, Is.Null, "OnReportEventAsync must be null before event monitoring.");
            Assert.That(TotalBehaviorNonNull(node), Is.Zero);

            using var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);

            monitoredNode.Add(eventItemMock.Object);
            m_rows.Add(BuildRow("EventAdded", node));
            Assert.That(TotalBehaviorNonNull(node), Is.EqualTo(1));
            Assert.That(node.OnReportEventAsync, Is.Not.Null,
                "MonitoredNode2.Add(IEventMonitoredItem) must install OnReportEventAsync.");
            Assert.That(node.OnStateChangedAsync, Is.Null,
                "OnStateChangedAsync must remain null for event-only monitoring.");

            monitoredNode.Remove(eventItemMock.Object);
            m_rows.Add(BuildRow("EventRemoved", node));
            Assert.That(TotalBehaviorNonNull(node), Is.Zero);
            Assert.That(node.OnReportEventAsync, Is.Null,
                "OnReportEventAsync must be cleared after last event item removed.");
        }

        /// <summary>
        /// Tracks co-occurrence when both a data-change item and an event item are
        /// present simultaneously: both callbacks are installed.
        /// </summary>
        [Test]
        public void DataChangeAndEventCoOccurrenceBothCallbacksPresent()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("occupancyBoth", 1),
                BrowseName = new QualifiedName("occupancyBoth", 1)
            };

            (Mock<IAsyncNodeManager> nodeManagerMock, Mock<IServerInternal> serverMock) = CreateMinimalMocks();
            Mock<IDataChangeMonitoredItem2> dataItemMock = CreateDataChangeItemMock(30u);
            Mock<IEventMonitoredItem> eventItemMock = CreateEventItemMock(31u);

            using var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(dataItemMock.Object);
            monitoredNode.Add(eventItemMock.Object);

            m_rows.Add(BuildRow("BothCallbacks", node));

            Assert.That(node.OnStateChangedAsync, Is.Not.Null,
                "OnStateChangedAsync must be set when data-change item is added.");
            Assert.That(node.OnReportEventAsync, Is.Not.Null,
                "OnReportEventAsync must be set when event item is added.");
            Assert.That(TotalBehaviorNonNull(node), Is.EqualTo(2),
                "Exactly 2 behavior callbacks must be occupied for combined monitoring.");

            monitoredNode.Remove(dataItemMock.Object);
            Assert.That(node.OnStateChangedAsync, Is.Null);
            Assert.That(node.OnReportEventAsync, Is.Not.Null);
            Assert.That(TotalBehaviorNonNull(node), Is.EqualTo(1));
            monitoredNode.Remove(eventItemMock.Object);
            Assert.That(node.OnReportEventAsync, Is.Null);
            Assert.That(TotalBehaviorNonNull(node), Is.Zero);
            m_rows.Add(BuildRow("BothRemoved", node));
        }

        /// <summary>
        /// Verifies that Description and RolePermissions are not modified by
        /// <see cref="MonitoredNode2"/> machinery.
        /// </summary>
        [Test]
        public void DescriptionAndRolePermissionsNotModifiedByMonitoring()
        {
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("occupancyMeta", 1),
                BrowseName = new QualifiedName("occupancyMeta", 1),
                DataType = DataTypeIds.Int32,
                Description = new LocalizedText("Described variable"),
                RolePermissions = ArrayOf.Wrapped(
                    new RolePermissionType { RoleId = new NodeId(1u, 0), Permissions = 0xFF })
            };

            LocalizedText descriptionBefore = node.Description;

            (Mock<IAsyncNodeManager> nodeManagerMock, Mock<IServerInternal> serverMock) = CreateMinimalMocks();
            Mock<IDataChangeMonitoredItem2> itemMock = CreateDataChangeItemMock(40u);

            using var monitoredNode = new MonitoredNode2(nodeManagerMock.Object, serverMock.Object, node);
            monitoredNode.Add(itemMock.Object);

            m_rows.Add(BuildRow("DescriptionAndPermissionsUnchanged", node));

            Assert.That(node.Description, Is.EqualTo(descriptionBefore),
                "MonitoredNode2 must not modify Description.");
            Assert.That(node.RolePermissions.IsNull, Is.False);
            Assert.That(node.RolePermissions.Count, Is.EqualTo(1));
            Assert.That(node.RolePermissions[0].RoleId, Is.EqualTo(new NodeId(1u, 0)));
            Assert.That(node.RolePermissions[0].Permissions, Is.EqualTo(0xFFu),
                "MonitoredNode2 must not mutate the permission payload.");
        }

        private static string BuildRow(string phase, NodeState node)
        {
            return $"{phase}," +
                $"{node.OnStateChangedAsync is not null}," +
                $"{node.OnReportEventAsync is not null}," +
                $"{TotalBehaviorNonNull(node)}";
        }

        private static int TotalBehaviorNonNull(NodeState n)
        {
            return B(n.OnValidate) +
                B(n.OnStateChanged) +
                B(n.OnStateChangedAsync) +
                B(n.OnReferenceAdded) +
                B(n.OnReferenceRemoved) +
                B(n.OnReportEvent) +
                B(n.OnReportEventAsync) +
                B(n.OnConditionRefresh) +
                B(n.OnCreateBrowser) +
                B(n.OnPopulateBrowser);
        }

        private static int B(Delegate? d)
        {
            return d is null ? 0 : 1;
        }

        private static (Mock<IAsyncNodeManager> nodeManagerMock, Mock<IServerInternal> serverMock)
            CreateMinimalMocks()
        {
            return (new Mock<IAsyncNodeManager>(), new Mock<IServerInternal>());
        }

        private static Mock<IDataChangeMonitoredItem2> CreateDataChangeItemMock(uint id)
        {
            var mock = new Mock<IDataChangeMonitoredItem2>();
            mock.Setup(m => m.Id).Returns(id);
            return mock;
        }

        private static Mock<IEventMonitoredItem> CreateEventItemMock(uint id)
        {
            var mock = new Mock<IEventMonitoredItem>();
            mock.Setup(m => m.Id).Returns(id);
            return mock;
        }

        private const string k_csvHeader =
            "Phase," +
            "OnStateChangedAsync," +
            "OnReportEventAsync," +
            "TotalBehaviorNonNull";

        private readonly List<string> m_rows = [];
    }
}
