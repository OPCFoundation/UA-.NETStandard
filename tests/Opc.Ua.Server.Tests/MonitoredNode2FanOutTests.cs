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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the data change fan-out of <see cref="MonitoredNode2"/>: the Value attribute
    /// fast path, the general path for other attributes, the order in which the consumer
    /// drains changes and items that are added or removed while a change is delivered.
    /// </summary>
    [TestFixture]
    [Category("MonitoredNode")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MonitoredNode2FanOutTests
    {
        /// <summary>
        /// A value change reads the Value attribute once at enqueue time for all items
        /// that monitor it, and every item receives that value.
        /// </summary>
        [Test]
        public async Task ValueChangeReadsValueOnceForAllItemsAsync()
        {
            BaseDataVariableState node = CreateNode();
            int reads = 0;
            node.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant value) =>
            {
                Interlocked.Increment(ref reads);
                return ServiceResult.Good;
            };
            var monitoredNode = new MonitoredNode2(CreateNodeManager().Object, CreateServer().Object, node);
            Mock<IDataChangeMonitoredItem2>[] items = [.. Enumerable.Range(1, 3)
                .Select(i => CreateItem((uint)i, Attributes.Value))];
            foreach (Mock<IDataChangeMonitoredItem2> item in items)
            {
                monitoredNode.Add(item.Object);
            }

            node.Value = 42;
            await monitoredNode.OnMonitoredNodeChangedAsync(
                s_context, node, NodeStateChangeMasks.Value).ConfigureAwait(false);
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            Assert.That(reads, Is.EqualTo(1));
            foreach (Mock<IDataChangeMonitoredItem2> item in items)
            {
                Assert.That(QueuedValues(item).Select(v => v.WrappedValue.AsBoxedObject()), Is.EqualTo(new object[] { 42 }));
            }
        }

        /// <summary>
        /// Items that monitor the Value attribute and items that monitor another attribute on
        /// the same node each receive the attribute they monitor, and only for the changes that
        /// concern it.
        /// </summary>
        [TestCase(NodeStateChangeMasks.Value, true, false)]
        [TestCase(NodeStateChangeMasks.NonValue, false, true)]
        [TestCase(NodeStateChangeMasks.Value | NodeStateChangeMasks.NonValue, true, true)]
        public async Task MixedAttributesReceiveOnlyTheirAttributeAsync(
            NodeStateChangeMasks changes,
            bool expectValue,
            bool expectDisplayName)
        {
            BaseDataVariableState node = CreateNode();
            node.Value = 7;
            node.DisplayName = new LocalizedText("name");
            var monitoredNode = new MonitoredNode2(CreateNodeManager().Object, CreateServer().Object, node);
            Mock<IDataChangeMonitoredItem2> valueItem = CreateItem(1u, Attributes.Value);
            Mock<IDataChangeMonitoredItem2> displayNameItem = CreateItem(2u, Attributes.DisplayName);
            Mock<IDataChangeMonitoredItem2> secondDisplayNameItem = CreateItem(3u, Attributes.DisplayName);
            monitoredNode.Add(valueItem.Object);
            monitoredNode.Add(displayNameItem.Object);
            monitoredNode.Add(secondDisplayNameItem.Object);

            await monitoredNode.OnMonitoredNodeChangedAsync(s_context, node, changes).ConfigureAwait(false);
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            List<DataValue> values = QueuedValues(valueItem);
            Assert.That(values, Has.Count.EqualTo(expectValue ? 1 : 0));
            if (expectValue)
            {
                Assert.That(values[0].WrappedValue.AsBoxedObject(), Is.EqualTo(7));
            }
            foreach (Mock<IDataChangeMonitoredItem2> item in new[] { displayNameItem, secondDisplayNameItem })
            {
                List<DataValue> names = QueuedValues(item);
                Assert.That(names, Has.Count.EqualTo(expectDisplayName ? 1 : 0));
                if (expectDisplayName)
                {
                    Assert.That(names[0].WrappedValue.TryGetValue(out LocalizedText name), Is.True);
                    Assert.That(name.Text, Is.EqualTo("name"));
                }
            }
        }

        /// <summary>
        /// An index range applies to each item's own copy of the shared snapshot value, while
        /// an item without index range receives the whole value.
        /// </summary>
        [Test]
        public async Task IndexRangeItemDoesNotChangeTheValueOfOtherItemsAsync()
        {
            BaseDataVariableState node = CreateNode();
            node.DataType = DataTypeIds.Int32;
            node.ValueRank = ValueRanks.OneDimension;
            node.Value = s_whole;
            var monitoredNode = new MonitoredNode2(CreateNodeManager().Object, CreateServer().Object, node);
            Mock<IDataChangeMonitoredItem2> rangeItem = CreateItem(1u, Attributes.Value);
            rangeItem.Setup(m => m.IndexRange).Returns(new NumericRange(1, 2));
            Mock<IDataChangeMonitoredItem2> wholeItem = CreateItem(2u, Attributes.Value);
            monitoredNode.Add(rangeItem.Object);
            monitoredNode.Add(wholeItem.Object);

            await monitoredNode.OnMonitoredNodeChangedAsync(
                s_context, node, NodeStateChangeMasks.Value).ConfigureAwait(false);
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            Assert.That(QueuedValues(rangeItem).Single().WrappedValue.TryGetValue(out ArrayOf<int> slice), Is.True);
            Assert.That(slice, Is.EqualTo(s_slice));
            Assert.That(QueuedValues(wholeItem).Single().WrappedValue.TryGetValue(out ArrayOf<int> whole), Is.True);
            Assert.That(whole, Is.EqualTo(s_whole));
        }

        /// <summary>
        /// Changes written while the consumer is blocked queue up behind the bounded channel and
        /// are delivered in the order they were written once it drains them.
        /// </summary>
        [Test]
        public async Task ChangesQueuedBehindBlockedConsumerAreDeliveredInOrderAsync()
        {
            const int changeCount = 50;
            BaseDataVariableState node = CreateNode();
            using var gate = new SemaphoreSlim(0, 1);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> nodeManager = CreateGatedNodeManager(gate, started);
            var monitoredNode = new MonitoredNode2(nodeManager.Object, CreateServer().Object, node);
            Mock<IDataChangeMonitoredItem2> item = CreateItem(1u, Attributes.Value);
            monitoredNode.Add(item.Object);

            Task writer = Task.Run(async () =>
            {
                for (int i = 1; i <= changeCount; i++)
                {
                    node.Value = i;
                    await monitoredNode.OnMonitoredNodeChangedAsync(
                        s_context, node, NodeStateChangeMasks.Value).ConfigureAwait(false);
                }
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            // The bounded channel holds the writer back while the consumer is blocked.
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(writer.IsCompleted, Is.False);

            gate.Release();
            await writer.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                QueuedValues(item).Select(v => v.WrappedValue.AsBoxedObject()),
                Is.EqualTo(Enumerable.Range(1, changeCount).Cast<object>()));
        }

        /// <summary>
        /// An item removed while a change is delivered to the node's other items gets no
        /// later change, and an item added meanwhile gets the changes written after it was added.
        /// </summary>
        [Test]
        public async Task ItemsAddedOrRemovedDuringDeliveryAsync()
        {
            BaseDataVariableState node = CreateNode();
            using var gate = new SemaphoreSlim(0, 1);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> nodeManager = CreateGatedNodeManager(gate, started);
            var monitoredNode = new MonitoredNode2(nodeManager.Object, CreateServer().Object, node);
            Mock<IDataChangeMonitoredItem2> first = CreateItem(1u, Attributes.Value);
            Mock<IDataChangeMonitoredItem2> removed = CreateItem(2u, Attributes.Value);
            Mock<IDataChangeMonitoredItem2> added = CreateItem(3u, Attributes.Value);
            monitoredNode.Add(first.Object);
            monitoredNode.Add(removed.Object);

            node.Value = 1;
            await monitoredNode.OnMonitoredNodeChangedAsync(
                s_context, node, NodeStateChangeMasks.Value).ConfigureAwait(false);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            // The consumer is blocked inside the delivery of the first change.
            monitoredNode.Remove(removed.Object);
            monitoredNode.Add(added.Object);
            node.Value = 2;
            await monitoredNode.OnMonitoredNodeChangedAsync(
                s_context, node, NodeStateChangeMasks.Value).ConfigureAwait(false);

            gate.Release();
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                QueuedValues(first).Select(v => v.WrappedValue.AsBoxedObject()),
                Is.EqualTo(new object[] { 1, 2 }));
            // The removed item may have been reached before its removal, never after it.
            Assert.That(
                QueuedValues(removed).Select(v => v.WrappedValue.AsBoxedObject()),
                Is.SubsetOf(new object[] { 1 }));
            Assert.That(
                QueuedValues(added).Select(v => v.WrappedValue.AsBoxedObject()),
                Is.EqualTo(new object[] { 2 }));
        }

        /// <summary>
        /// The consumer stops after Dispose even when it waits on an empty channel.
        /// </summary>
        [Test]
        public async Task DisposeStopsIdleConsumerAsync()
        {
            BaseDataVariableState node = CreateNode();
            var monitoredNode = new MonitoredNode2(CreateNodeManager().Object, CreateServer().Object, node);
            Mock<IDataChangeMonitoredItem2> item = CreateItem(1u, Attributes.Value);
            monitoredNode.Add(item.Object);
            await monitoredNode.OnMonitoredNodeChangedAsync(
                s_context, node, NodeStateChangeMasks.Value).ConfigureAwait(false);
            await monitoredNode.WaitForPendingNotificationsAsync(CancellationToken.None).ConfigureAwait(false);

            monitoredNode.Dispose();

            await monitoredNode.DrainAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(QueuedValues(item), Has.Count.EqualTo(1));
        }

        private static BaseDataVariableState CreateNode()
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId("fanOut", 1),
                BrowseName = new QualifiedName("fanOut", 1),
                DataType = DataTypeIds.Int32,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = 0
            };
        }

        private static Mock<IAsyncNodeManager> CreateNodeManager()
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            return nodeManager;
        }

        /// <summary>
        /// The first permission check blocks on the gate, later checks of the same item are cached.
        /// </summary>
        private static Mock<IAsyncNodeManager> CreateGatedNodeManager(
            SemaphoreSlim gate,
            TaskCompletionSource<bool> started)
        {
            int calls = 0;
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager
                .Setup(m => m.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                .Returns<OperationContext, NodeId, PermissionType, CancellationToken>(
                    (_, _, _, ct) =>
                    {
                        if (Interlocked.Increment(ref calls) != 1)
                        {
                            return new ValueTask<ServiceResult>(ServiceResult.Good);
                        }
                        started.TrySetResult(true);
                        return new ValueTask<ServiceResult>(
                            gate.WaitAsync(ct).ContinueWith(
                                _ => ServiceResult.Good,
                                CancellationToken.None,
                                TaskContinuationOptions.OnlyOnRanToCompletion,
                                TaskScheduler.Default));
                    });
            return nodeManager;
        }

        private static Mock<IServerInternal> CreateServer()
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Auditing).Returns(false);
            return server;
        }

        private static Mock<IDataChangeMonitoredItem2> CreateItem(uint id, uint attributeId)
        {
            var session = new Mock<ISession>();
            var identity = new Mock<IUserIdentity>();
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);

            var item = new Mock<IDataChangeMonitoredItem2>();
            item.Setup(m => m.Id).Returns(id);
            item.Setup(m => m.AttributeId).Returns(attributeId);
            item.Setup(m => m.IndexRange).Returns(NumericRange.Null);
            item.Setup(m => m.DataEncoding).Returns(QualifiedName.Null);
            item.Setup(m => m.Session).Returns(session.Object);
            item.Setup(m => m.EffectiveIdentity).Returns(identity.Object);
            return item;
        }

        private static List<DataValue> QueuedValues(Mock<IDataChangeMonitoredItem2> item)
        {
            return [.. item.Invocations
                .Where(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue))
                .Select(i => (DataValue)i.Arguments[0]!)];
        }

        private static readonly ISystemContext s_context = new Mock<ISystemContext>().Object;
        private static readonly ArrayOf<int> s_whole = new(new[] { 0, 1, 2, 3, 4 });
        private static readonly ArrayOf<int> s_slice = new(new[] { 1, 2 });
    }
}
