// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

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
                LifecycleResult publicationResult1 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(
                    CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => manager = value)), timeout.Token).WaitAsync(
                    timeout.Token).ConfigureAwait(false);
                registration = RequireRegistration(publicationResult1.Registrations[0]);
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
