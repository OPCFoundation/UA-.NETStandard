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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the montioredItems for a NodeManager
    /// </summary>
    public class SamplingGroupMonitoredItemManager :
        IMonitoredItemManager,
        IMonitoredItemManagerLifecycle
    {
        /// <inheritdoc/>
        public SamplingGroupMonitoredItemManager(
            IAsyncNodeManager nodeManager,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            m_samplingGroupManager = new SamplingGroupManager(
                server,
                nodeManager,
                (uint)configuration.ServerConfiguration!.MaxNotificationQueueSize,
                (uint)configuration.ServerConfiguration.MaxDurableNotificationQueueSize,
                configuration.ServerConfiguration.AvailableSamplingRates.ToArray()!);

            m_nodeManager = nodeManager;
            m_server = server;
            MonitoredNodes = [];
            MonitoredItems = new ConcurrentDictionary<uint, IMonitoredItem>();
        }

        /// <summary>
        /// Creates a monitored-item manager with a supplied sampling-group manager.
        /// </summary>
        internal SamplingGroupMonitoredItemManager(
            IAsyncNodeManager nodeManager,
            IServerInternal server,
            SamplingGroupManager samplingGroupManager)
        {
            m_nodeManager = nodeManager;
            m_server = server;
            m_samplingGroupManager = samplingGroupManager;
            MonitoredNodes = [];
            MonitoredItems = new ConcurrentDictionary<uint, IMonitoredItem>();
        }

        /// <inheritdoc/>
        public NodeIdDictionary<MonitoredNode2> MonitoredNodes { get; }

        /// <inheritdoc/>
        public ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems { get; }

        /// <inheritdoc/>
        public ISampledDataChangeMonitoredItem CreateMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServerSystemContext context,
            NodeHandle handle,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest itemToCreate,
            Range euRange,
            MonitoringFilter filterToUse,
            double samplingInterval,
            uint revisedQueueSize,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache)
        {
            // set min sampling interval if 0
            if (samplingInterval.CompareTo(0.0) == 0)
            {
                samplingInterval = 1;
            }

            uint monitoredItemId = monitoredItemIdFactory.GetNextId();

            // create monitored item.
            ISampledDataChangeMonitoredItem monitoredItem =
                m_samplingGroupManager.CreateMonitoredItem(
                    context.OperationContext!,
                    subscriptionId,
                    publishingInterval,
                    timestampsToReturn,
                    monitoredItemId,
                    handle,
                    itemToCreate,
                    euRange,
                    samplingInterval,
                    createDurable);

            // save the monitored item.
            MonitoredItems.AddOrUpdate(
                monitoredItemId,
                monitoredItem,
                (key, oldValue) => monitoredItem);

            return monitoredItem;
        }

        /// <inheritdoc/>
        public void ApplyChanges()
        {
            // update all groups with any new items.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_samplingGroupManager?.Dispose();
            }
        }

        /// <inheritdoc/>
        public StatusCode DeleteMonitoredItem(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            NodeHandle handle)
        {
            // validate monitored item.
            if (!MonitoredItems.TryGetValue(
                monitoredItem.Id,
                out IMonitoredItem? existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            // remove item.
            m_samplingGroupManager.StopMonitoring(monitoredItem);

            // remove association with the group.
            MonitoredItems.TryRemove(monitoredItem.Id, out _);

            // delete successful.
            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public ServiceResult? ModifyMonitoredItem(
            ServerSystemContext context,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringFilter filterToUse,
            Range euRange,
            double samplingInterval,
            uint revisedQueueSize,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify)
        {
            // validate monitored item.
            if (!MonitoredItems.TryGetValue(
                monitoredItem.Id,
                out IMonitoredItem? existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            return m_samplingGroupManager.ModifyMonitoredItem(
                context.OperationContext!,
                timestampsToReturn,
                monitoredItem,
                itemToModify,
                euRange);
        }

        /// <inheritdoc/>
        public ValueTask<(ServiceResult, MonitoringMode?)> SetMonitoringModeAsync(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (!MonitoredItems.TryGetValue(
                monitoredItem.Id,
                out IMonitoredItem? existingMonitoredItem))
            {
                return new ValueTask<(ServiceResult, MonitoringMode?)>((StatusCodes.BadMonitoredItemIdInvalid, null));
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return new ValueTask<(ServiceResult, MonitoringMode?)>((StatusCodes.BadMonitoredItemIdInvalid, null));
            }

            // update monitoring mode.
            MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);

            // need to provide an immediate update after enabling.
            if (previousMode == MonitoringMode.Disabled &&
                monitoringMode != MonitoringMode.Disabled)
            {
                var initialValue = new DataValue(
                    Variant.Null,
                    StatusCodes.BadWaitingForInitialData,
                    DateTimeUtc.MinValue,
                    DateTime.UtcNow);

                // read the initial value.

                if (monitoredItem.ManagerHandle is Node node)
                {
                    ServiceResult error = node.Read(
                        context,
                        monitoredItem.AttributeId,
                        ref initialValue);

                    if (ServiceResult.IsBad(error))
                    {
                        initialValue = initialValue
                            .WithWrappedValue(default)
                            .WithStatus(error.StatusCode);
                    }
                }

                monitoredItem.QueueValue(initialValue, null);
            }

            return new ValueTask<(ServiceResult, MonitoringMode?)>((StatusCodes.Good, previousMode));
        }

        /// <inheritdoc/>
        public bool RestoreMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServerSystemContext context,
            NodeHandle handle,
            IStoredMonitoredItem storedMonitoredItem,
            IUserIdentity savedOwnerIdentity,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            out ISampledDataChangeMonitoredItem monitoredItem)
        {
            if (MonitoredItems.ContainsKey(storedMonitoredItem.Id))
            {
                monitoredItem = null!;
                return false;
            }

            // create monitored item.
            monitoredItem = m_samplingGroupManager.RestoreMonitoredItem(
                handle,
                storedMonitoredItem,
                savedOwnerIdentity);

            ((IMonitoredItemLifecycle)monitoredItem).Rebind(m_nodeManager, handle);

            // save monitored item.
            if (!MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem))
            {
                m_samplingGroupManager.StopMonitoring(monitoredItem);
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public (MonitoredNode2?, ServiceResult) SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            MonitoredNode2? monitoredNode;
            // handle unsubscribe.
            if (unsubscribe)
            {
                // check for existing monitored node.
                if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
                {
                    return (null, StatusCodes.BadNodeIdUnknown);
                }

                monitoredNode.Remove(monitoredItem);
                MonitoredItems.TryRemove(monitoredItem.Id, out _);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(source.NodeId);
                }

                return (monitoredNode, ServiceResult.Good);
            }

            // only objects or views can be subscribed to.
            if (source is not BaseObjectState instance ||
                (instance.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
            {
                if (source is not ViewState view ||
                    (view.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return (null, StatusCodes.BadNotSupported);
                }
            }

            // check for existing monitored node.
            if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
            {
                MonitoredNodes[source.NodeId]
                    = monitoredNode = new MonitoredNode2(m_nodeManager, m_server, source,
                        IsMultiConsumerNode(source.NodeId));
            }

            // remove existing monitored items with the same Id prior to insertion in order to avoid duplicates
            // this is necessary since the SubscribeToEvents method is called also from ModifyMonitoredItemsForEvents
            monitoredNode.EventMonitoredItems.TryRemove(monitoredItem.Id, out _);

            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            monitoredNode.Add(monitoredItem);
            MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

            return (monitoredNode, ServiceResult.Good);
        }

        /// <inheritdoc/>
        IReadOnlyList<IMonitoredItem> IMonitoredItemManagerLifecycle.GetMonitoredItemsSnapshot(
            IReadOnlyCollection<NodeId>? nodeIds)
        {
            if (nodeIds == null)
            {
                return [.. MonitoredItems.Values];
            }

            if (nodeIds.Count == 0)
            {
                return [];
            }

            var requestedNodeIds = new HashSet<NodeId>(nodeIds, NodeIdComparer.Default);
            return [.. MonitoredItems.Values.Where(item => requestedNodeIds.Contains(item.NodeId))];
        }

        /// <inheritdoc/>
        (ServiceResult Result, bool Changed) IMonitoredItemManagerLifecycle.DetachMonitoredItem(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache)
        {
            if (monitoredItem is not IMonitoredItemLifecycle lifecycle)
            {
                return (StatusCodes.BadNotSupported, false);
            }

            if (!MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existing))
            {
                return lifecycle.IsDetached
                    ? (ServiceResult.Good, false)
                    : (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            if (!ReferenceEquals(existing, monitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            bool isEvent = (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) != 0;
            if (isEvent)
            {
                if (!MonitoredNodes.TryGetValue(
                    monitoredItem.NodeId,
                    out MonitoredNode2? monitoredNode) ||
                    !monitoredNode.EventMonitoredItems.TryGetValue(
                        monitoredItem.Id,
                        out IEventMonitoredItem? eventItem) ||
                    !ReferenceEquals(eventItem, monitoredItem))
                {
                    return (StatusCodes.BadInvalidState, false);
                }

                monitoredNode.Remove(eventItem);
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(monitoredItem.NodeId);
                    monitoredNode.Dispose();
                }
            }
            else
            {
                m_samplingGroupManager.StopMonitoring(monitoredItem);
            }

            if (!((ICollection<KeyValuePair<uint, IMonitoredItem>>)MonitoredItems).Remove(
                new KeyValuePair<uint, IMonitoredItem>(monitoredItem.Id, monitoredItem)))
            {
                return (StatusCodes.BadInvalidState, false);
            }

            lifecycle.BeginDetach();
            m_samplingGroupManager.ApplyChanges();
            return (ServiceResult.Good, true);
        }

        /// <inheritdoc/>
        (ServiceResult Result, bool Changed) IMonitoredItemManagerLifecycle.AttachMonitoredItem(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache)
        {
            if (monitoredItem is not IMonitoredItemLifecycle lifecycle)
            {
                return (StatusCodes.BadNotSupported, false);
            }

            if (MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existing))
            {
                if (!ReferenceEquals(existing, monitoredItem))
                {
                    return (StatusCodes.BadMonitoredItemIdInvalid, false);
                }

                return lifecycle.IsDetached
                    ? (StatusCodes.BadInvalidState, false)
                    : (ServiceResult.Good, false);
            }

            if (!lifecycle.IsDetached)
            {
                return (StatusCodes.BadInvalidState, false);
            }

            if (!MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            bool isEvent = (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) != 0;
            MonitoredNode2? monitoredNode = null;
            bool monitoredNodeCreated = false;
            bool monitoredNodeLinked = false;
            bool monitoringStarted = false;
            bool attached = false;
            try
            {
                lifecycle.Rebind(m_nodeManager, handle);
                if (isEvent)
                {
                    if (!MonitoredNodes.TryGetValue(handle.NodeId, out monitoredNode))
                    {
                        MonitoredNodes[handle.NodeId] = monitoredNode = new MonitoredNode2(
                            m_nodeManager,
                            m_server,
                            handle.Node,
                            IsMultiConsumerNode(handle.NodeId));
                        monitoredNodeCreated = true;
                    }
                    else
                    {
                        ServiceResult rebindResult = monitoredNode.Rebind(m_nodeManager, handle.Node);
                        if (ServiceResult.IsBad(rebindResult))
                        {
                            return (rebindResult, false);
                        }
                    }

                    handle.MonitoredNode = monitoredNode;
                    if (monitoredNode.EventMonitoredItems.TryGetValue(
                        monitoredItem.Id,
                        out IEventMonitoredItem? linkedEventItem))
                    {
                        return ReferenceEquals(linkedEventItem, monitoredItem)
                            ? (StatusCodes.BadInvalidState, false)
                            : (StatusCodes.BadMonitoredItemIdInvalid, false);
                    }

                    monitoredNode.Add((IEventMonitoredItem)monitoredItem);
                    monitoredNodeLinked = true;
                }
                else
                {
                    m_samplingGroupManager.StartMonitoring(
                        context.OperationContext ?? new OperationContext(monitoredItem),
                        monitoredItem,
                        monitoredItem.EffectiveIdentity);
                    monitoringStarted = true;
                }

                m_samplingGroupManager.ApplyChanges();
                attached = true;
                return (ServiceResult.Good, true);
            }
            finally
            {
                if (!attached)
                {
                    if (monitoringStarted)
                    {
                        m_samplingGroupManager.StopMonitoring(monitoredItem);
                        m_samplingGroupManager.ApplyChanges();
                    }

                    if (monitoredNodeLinked)
                    {
                        monitoredNode!.Remove((IEventMonitoredItem)monitoredItem);
                    }

                    if (monitoredNodeCreated)
                    {
                        MonitoredNodes.Remove(handle.NodeId);
                        monitoredNode?.Dispose();
                    }

                    MonitoredItems.TryRemove(monitoredItem.Id, out _);
                    DetachedMonitoredItemOwnership.Detach(m_server, lifecycle);
                }
            }
        }

        private bool IsMultiConsumerNode(NodeId nodeId)
        {
            return m_nodeManager.IsMultipleEventConsumerNode(nodeId);
        }

        private readonly IAsyncNodeManager m_nodeManager;
        private readonly IServerInternal m_server;
        private readonly SamplingGroupManager m_samplingGroupManager;
    }
}
