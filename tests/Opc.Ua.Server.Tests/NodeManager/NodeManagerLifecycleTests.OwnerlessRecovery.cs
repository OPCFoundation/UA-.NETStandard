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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Actual manager publication and late-node insertion recover restored ownerless items without a host probe.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LifecyclePublicationRecoversOwnerlessRestoredItemAsync(bool addNodeLater)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            const uint ItemId = 4000001;
            IServerInternal server = m_server.CurrentInstance;
            ushort namespaceIndex = server.NamespaceUris.GetIndexOrAppend(kModelNamespaceUri);
            var valueId = new NodeId(addNodeLater ? 8010u : kValueNodeId, namespaceIndex);
            IDataChangeMonitoredItemQueue queue = server.MonitoredItemQueueFactory
                .CreateDataChangeQueue(false, ItemId);
            queue.ResetQueue(10, false);
            queue.Enqueue(new DataValue(Variant.From(7), StatusCodes.Good), ServiceResult.Good);
            var stored = new StoredMonitoredItem
            {
                Id = ItemId,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = valueId,
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000000,
                TimestampsToReturn = TimestampsToReturn.Both,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(Variant.From(7), StatusCodes.Good),
                IsDurable = true,
                IsDetached = true,
                RestoredDataChangeQueue = queue
            };
            using var item = new MonitoredItem(server, stored);
            var owner = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = owner;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(owner);
            long originalRevision = binding.SourceRevision;
            TrackingLifecycleNodeManager? manager = null;
            NodeManagerRegistration? registration = null;
            try
            {
                Assert.That(item.NodeManager, Is.Null);
                Assert.That(binding.IsSourceUnavailable, Is.True);
                Assert.That(item.ItemsInQueue, Is.EqualTo(1));
                registration = await m_server.NodeManagerLifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(kGeneration2Value, value => manager = value),
                    null,
                    timeout.Token).AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(manager);

                if (addNodeLater)
                {
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(binding.SourceRevision, Is.EqualTo(originalRevision));
                    await manager.AddContinuationChildrenAsync("Recovered", timeout.Token).ConfigureAwait(false);
                }

                Assert.That(item.NodeManager, Is.SameAs(manager));
                Assert.That(binding.IsSourceUnavailable, Is.False);
                Assert.That(binding.SourceRevision, Is.EqualTo(originalRevision + 1));
                Assert.That(item.SubscriptionCallback, Is.SameAs(owner));
                Assert.That(item.ToStorableMonitoredItem().IsDurable, Is.True);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
                Assert.That(generation.MonitoredItems.TryGetValue(item, out ISubscription? registered), Is.True);
                Assert.That(registered, Is.SameAs(owner));
                Assert.That(generation.Slot.UnresolvedItems.ContainsKey(item), Is.False);

                using var context = new OperationContext(item);
                var notifications = new Queue<MonitoredItemNotification>();
                item.Publish(
                    context, notifications, new Queue<DiagnosticInfo>(), 10,
                    server.Telemetry.CreateLogger<NodeManagerLifecycleTests>());
                Assert.That(notifications, Is.Not.Empty);
                Assert.That(notifications.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(notifications.Peek().Value.WrappedValue.TryGetValue(out int restoredValue), Is.True);
                Assert.That(restoredValue, Is.EqualTo(7));

                DataValue live = await ReadValueAsync(valueId).ConfigureAwait(false);
                Assert.That(live.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(live.WrappedValue.TryGetValue(out int liveValue), Is.True);
                Assert.That(liveValue, Is.EqualTo(addNodeLater ? 42 : kGeneration2Value));
            }
            finally
            {
                try
                {
                    if (item.NodeManager is INodeManagerMonitoredItemSource source)
                    {
                        ServiceResult released = await source.ReleaseSourceAsync(
                            item, binding.SourceRevision, CancellationToken.None).ConfigureAwait(false);
                        Assert.That(ServiceResult.IsGood(released), Is.True);
                    }
                }
                finally
                {
                    binding.UnbindSubscription(owner);
                    if (registration is not null)
                    {
                        await RemoveAndRetireAsync(
                            m_server.NodeManagerLifecycle, registration, timeout.Token)
                            .WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
