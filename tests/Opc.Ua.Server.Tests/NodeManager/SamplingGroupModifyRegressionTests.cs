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

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that modifying sampled monitored items keeps every item in exactly one sampling group.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroup")]
    public sealed class SamplingGroupModifyRegressionTests
    {
        /// <summary>
        /// Verifies that an item pending addition leaves the group when a modification moves it elsewhere.
        /// </summary>
        [Test]
        public void ModifyMonitoringRemovesPendingAdditionThatNoLongerMatches()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                using var group = new SamplingGroup(
                    server.Object, nodeManager.Object, Rates(), context, 500);
                using MonitoredItem item = CreateItem(server.Object, nodeManager.Object, 1, 500);

                Assert.That(group.StartMonitoring(context, item), Is.True);
                item.SetSamplingInterval(1000);

                Assert.That(group.ModifyMonitoring(context, item), Is.False);
                Assert.That(group.ApplyChanges(), Is.True, "the moved item must not be sampled by the old group");
            }
        }

        /// <summary>
        /// Verifies that an item pending addition stays in the group when the modification still matches it.
        /// </summary>
        [Test]
        public void ModifyMonitoringKeepsPendingAdditionThatStillMatches()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                using var group = new SamplingGroup(
                    server.Object, nodeManager.Object, Rates(), context, 500);
                using MonitoredItem item = CreateItem(server.Object, nodeManager.Object, 1, 500);

                Assert.That(group.StartMonitoring(context, item), Is.True);
                item.SetSamplingInterval(450);

                Assert.That(group.ModifyMonitoring(context, item), Is.True);
                Assert.That(item.SamplingInterval, Is.EqualTo(500));
                Assert.That(group.ApplyChanges(), Is.False);
                group.StopMonitoring(item);
                Assert.That(group.ApplyChanges(), Is.True);
            }
        }

        /// <summary>
        /// Verifies that two modifications of one item before the changes are applied leave no orphaned
        /// sampling group behind once the item is deleted.
        /// </summary>
        [Test]
        public void RepeatedModifyBeforeApplyLeavesNoOrphanedGroup()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                using var manager = new SamplingGroupManager(
                    server.Object, nodeManager.Object, 10, 10, Rates());
                using MonitoredItem item = CreateItem(server.Object, nodeManager.Object, 1, 100);
                manager.StartMonitoring(context, item);
                manager.ApplyChanges();

                item.SetSamplingInterval(500);
                manager.ModifyMonitoring(context, item);
                item.SetSamplingInterval(1000);
                manager.ModifyMonitoring(context, item);
                manager.ApplyChanges();

                Assert.That(GroupCount(manager), Is.EqualTo(1), "the item must be sampled by one group only");

                manager.StopMonitoring(item);
                manager.ApplyChanges();

                Assert.That(GroupCount(manager), Is.Zero, "no group may keep sampling the deleted item");
            }
        }

        /// <summary>
        /// Verifies that modifying an item away from its group and back again before the changes are applied
        /// keeps it in its original group.
        /// </summary>
        [Test]
        public void ModifyAwayAndBackBeforeApplyKeepsItemSampled()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                using var manager = new SamplingGroupManager(
                    server.Object, nodeManager.Object, 10, 10, Rates());
                using MonitoredItem item = CreateItem(server.Object, nodeManager.Object, 1, 100);
                manager.StartMonitoring(context, item);
                manager.ApplyChanges();

                item.SetSamplingInterval(500);
                manager.ModifyMonitoring(context, item);
                item.SetSamplingInterval(100);
                manager.ModifyMonitoring(context, item);
                manager.ApplyChanges();

                Assert.That(GroupCount(manager), Is.EqualTo(1), "the item must still be sampled");
                Assert.That(item.SamplingInterval, Is.EqualTo(100));

                manager.StopMonitoring(item);
                manager.ApplyChanges();

                Assert.That(GroupCount(manager), Is.Zero);
            }
        }

        /// <summary>
        /// Verifies that a modify requesting queue size 0 revises a data item to the default queue size 1
        /// instead of keeping the previous queue size (Part 4 7.21).
        /// </summary>
        [Test]
        public void ModifyWithQueueSizeZeroRevisesToOne()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                using var manager = new SamplingGroupManager(
                    server.Object, nodeManager.Object, 100, 100, Rates());
                using MonitoredItem item = CreateItem(server.Object, nodeManager.Object, 1, 100, queueSize: 10);
                manager.StartMonitoring(context, item);
                manager.ApplyChanges();
                Assert.That(item.QueueSize, Is.EqualTo(10));

                ServiceResult result = manager.ModifyMonitoredItem(
                    context,
                    TimestampsToReturn.Both,
                    item,
                    new MonitoredItemModifyRequest
                    {
                        MonitoredItemId = item.Id,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 100,
                            QueueSize = 0,
                            DiscardOldest = true
                        }
                    },
                    null!,
                    null!,
                    revisedSamplingInterval: 100)!;

                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(item.QueueSize, Is.EqualTo(1));

                manager.StopMonitoring(item);
                manager.ApplyChanges();
            }
        }

        private static List<SamplingRateGroup> Rates()
        {
            return [new SamplingRateGroup(100, 100, 9)];
        }

        private static int GroupCount(SamplingGroupManager manager)
        {
            FieldInfo field = typeof(SamplingGroupManager).GetField(
                "m_samplingGroups", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return ((ICollection)field!.GetValue(manager)!).Count;
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            return new OperationContext(
                new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
        }

        private static MonitoredItem CreateItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            uint id,
            double samplingInterval,
            uint queueSize = 1)
        {
            return new MonitoredItem(
                server,
                nodeManager,
                new NodeHandle(),
                subscriptionId: 1,
                id,
                new ReadValueId { NodeId = new NodeId(id, 1), AttributeId = Attributes.Value },
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                clientHandle: id,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval,
                queueSize,
                discardOldest: true,
                sourceSamplingInterval: 1);
        }
    }
}
