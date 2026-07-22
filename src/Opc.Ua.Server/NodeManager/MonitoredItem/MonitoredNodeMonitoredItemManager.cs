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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the montioredItems for a NodeManager
    /// </summary>
    public class MonitoredNodeMonitoredItemManager :
        IMonitoredItemManager,
        IMonitoredItemManagerLifecycle
    {
        /// <inheritdoc/>
        public MonitoredNodeMonitoredItemManager(IAsyncNodeManager nodeManager, IServerInternal server)
        {
            m_nodeManager = nodeManager;
            m_server = server;
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
            // check if the node is already being monitored.

            if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out MonitoredNode2? monitoredNode))
            {
                NodeState cachedNode = addNodeToComponentCache(context, handle, handle.Node);
                MonitoredNodes[handle.Node.NodeId]
                    = monitoredNode = new MonitoredNode2(m_nodeManager, m_server, cachedNode,
                        IsMultiConsumerNode(cachedNode.NodeId));
            }

            handle.Node = monitoredNode!.Node;
            handle.MonitoredNode = monitoredNode;

            // Allocate the monitored item id
            uint monitoredItemId;
            do
            {
                monitoredItemId = monitoredItemIdFactory.GetNextId();
            } while (!MonitoredItems.TryAdd(monitoredItemId, null!));

            // create the item.
            var datachangeItem = new MonitoredItem(
                server,
                nodeManager,
                handle,
                subscriptionId,
                monitoredItemId,
                itemToCreate.ItemToMonitor,
                diagnosticsMasks,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                filterToUse,
                filterToUse,
                euRange,
                samplingInterval,
                revisedQueueSize,
                itemToCreate.RequestedParameters.DiscardOldest,
                0,
                createDurable);

            // now save the monitored item.
            monitoredNode.Add((IDataChangeMonitoredItem2)datachangeItem);
            Debug.Assert(MonitoredItems[monitoredItemId] == null);
            MonitoredItems[monitoredItemId] = datachangeItem;

            return datachangeItem;
        }

        /// <inheritdoc/>
        public void ApplyChanges()
        {
            //only needed for sampling groups
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
            var nodes = MonitoredNodes.Values.ToList();
            MonitoredNodes.Clear();

            foreach (MonitoredNode2 node in nodes)
            {
                node.Dispose();
            }

            var items = MonitoredItems.Values.ToList();
            MonitoredItems.Clear();

            foreach (IMonitoredItem item in items)
            {
                item.Dispose();
            }
        }

        /// <inheritdoc/>
        public StatusCode DeleteMonitoredItem(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            NodeHandle handle)
        {
            // check if the node is already being monitored.
            if (MonitoredNodes.TryGetValue(handle.NodeId, out MonitoredNode2? monitoredNode))
            {
                monitoredNode.Remove(monitoredItem);
                MonitoredItems.TryRemove(monitoredItem.Id, out _);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(handle.NodeId);
                    monitoredNode.Dispose();
                }

                monitoredItem.Dispose();
            }
            else
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public async ValueTask<(ServiceResult, MonitoringMode?)> SetMonitoringModeAsync(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle,
            CancellationToken cancellationToken = default)
        {
            // update monitoring mode.
            MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);

            // must send the latest value after enabling a disabled item.
            if (monitoringMode == MonitoringMode.Reporting &&
                previousMode == MonitoringMode.Disabled)
            {
                await handle.MonitoredNode.QueueValueAsync(context, handle.Node, monitoredItem, cancellationToken).ConfigureAwait(false);
            }

            return (StatusCodes.Good, previousMode);
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

            // check if the node is already being monitored.
            if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out MonitoredNode2? monitoredNode))
            {
                NodeState cachedNode = addNodeToComponentCache(context, handle, handle.Node);
                MonitoredNodes[handle.Node.NodeId]
                    = monitoredNode = new MonitoredNode2(m_nodeManager, m_server, cachedNode,
                        IsMultiConsumerNode(cachedNode.NodeId));
            }

            handle.Node = monitoredNode!.Node;
            handle.MonitoredNode = monitoredNode;

            // create the item.
            var datachangeItem = new MonitoredItem(
                server,
                nodeManager,
                handle,
                storedMonitoredItem);

            ((IMonitoredItemLifecycle)datachangeItem).Rebind(m_nodeManager, handle);

            // update monitored item list.
            monitoredItem = datachangeItem;

            // save the monitored item.
            monitoredNode.Add((IDataChangeMonitoredItem2)datachangeItem);

            if (MonitoredItems.TryAdd(datachangeItem.Id, datachangeItem))
            {
                return true;
            }

            monitoredNode.Remove((IDataChangeMonitoredItem2)datachangeItem);
            if (!monitoredNode.HasMonitoredItems)
            {
                MonitoredNodes.Remove(handle.NodeId);
                monitoredNode.Dispose();
            }

            return false;
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
            // modify the monitored item parameters.
            return monitoredItem.ModifyAttributes(
                diagnosticsMasks,
                timestampsToReturn,
                itemToModify.RequestedParameters.ClientHandle,
                filterToUse,
                filterToUse,
                euRange,
                samplingInterval,
                revisedQueueSize,
                itemToModify.RequestedParameters.DiscardOldest);
        }

        /// <inheritdoc/>
        public (MonitoredNode2?, ServiceResult) SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            MonitoredNode2 monitoredNode;
            // handle unsubscribe.
            if (unsubscribe)
            {
                // check for existing monitored node.
                if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode!))
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
            if ((source is not BaseObjectState instance) ||
                (instance.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
            {
                if ((source is not ViewState view) ||
                    (view.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return (null, StatusCodes.BadNotSupported);
                }
            }

            // check for existing monitored node.
            if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode!))
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
            if (!MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem))
            {
                return (monitoredNode, StatusCodes.BadUnexpectedError);
            }

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

            if (monitoredItem.ManagerHandle is not NodeHandle handle ||
                !MonitoredNodes.TryGetValue(monitoredItem.NodeId, out MonitoredNode2? monitoredNode))
            {
                return (StatusCodes.BadInvalidState, false);
            }

            bool isEvent = (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) != 0;
            if (isEvent)
            {
                if (!monitoredNode.EventMonitoredItems.TryGetValue(
                    monitoredItem.Id,
                    out IEventMonitoredItem? eventItem) ||
                    !ReferenceEquals(eventItem, monitoredItem))
                {
                    return (StatusCodes.BadInvalidState, false);
                }

                monitoredNode.Remove(eventItem);
            }
            else
            {
                if (!monitoredNode.DataChangeMonitoredItems.TryGetValue(
                    monitoredItem.Id,
                    out IDataChangeMonitoredItem2? dataChangeItem) ||
                    !ReferenceEquals(dataChangeItem, monitoredItem))
                {
                    return (StatusCodes.BadInvalidState, false);
                }

                monitoredNode.Remove(dataChangeItem);
                removeNodeFromComponentCache(context, handle);
            }

            if (!((ICollection<KeyValuePair<uint, IMonitoredItem>>)MonitoredItems).Remove(
                new KeyValuePair<uint, IMonitoredItem>(monitoredItem.Id, monitoredItem)))
            {
                return (StatusCodes.BadInvalidState, false);
            }

            if (!monitoredNode.HasMonitoredItems)
            {
                MonitoredNodes.Remove(monitoredItem.NodeId);
                monitoredNode.Dispose();
            }

            lifecycle.BeginDetach();
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
            bool componentCacheAdded = false;
            bool monitoredNodeCreated = false;
            bool monitoredNodeLinked = false;
            bool attached = false;
            try
            {
                if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out monitoredNode))
                {
                    NodeState node = isEvent
                        ? handle.Node
                        : addNodeToComponentCache(context, handle, handle.Node);
                    componentCacheAdded = !isEvent;
                    MonitoredNodes[handle.Node.NodeId] = monitoredNode = new MonitoredNode2(
                        m_nodeManager,
                        m_server,
                        node,
                        IsMultiConsumerNode(node.NodeId));
                    monitoredNodeCreated = true;
                }
                else
                {
                    if (!isEvent)
                    {
                        NodeState freshNode = handle.Node;
                        handle.Node = addNodeToComponentCache(context, handle, freshNode);
                        componentCacheAdded = true;
                        if (!ReferenceEquals(handle.Node, freshNode))
                        {
                            return (StatusCodes.BadInvalidState, false);
                        }
                    }

                    ServiceResult rebindResult = monitoredNode.Rebind(m_nodeManager, handle.Node);
                    if (ServiceResult.IsBad(rebindResult))
                    {
                        return (rebindResult, false);
                    }
                }

                handle.Node = monitoredNode.Node;
                handle.MonitoredNode = monitoredNode;
                if (isEvent)
                {
                    if (monitoredNode.EventMonitoredItems.TryGetValue(
                        monitoredItem.Id,
                        out IEventMonitoredItem? linkedEventItem))
                    {
                        return ReferenceEquals(linkedEventItem, monitoredItem)
                            ? (StatusCodes.BadInvalidState, false)
                            : (StatusCodes.BadMonitoredItemIdInvalid, false);
                    }

                    monitoredNode.Add((IEventMonitoredItem)monitoredItem);
                }
                else
                {
                    if (monitoredNode.DataChangeMonitoredItems.TryGetValue(
                        monitoredItem.Id,
                        out IDataChangeMonitoredItem2? linkedDataChangeItem))
                    {
                        return ReferenceEquals(linkedDataChangeItem, monitoredItem)
                            ? (StatusCodes.BadInvalidState, false)
                            : (StatusCodes.BadMonitoredItemIdInvalid, false);
                    }

                    monitoredNode.Add(monitoredItem);
                }
                monitoredNodeLinked = true;

                lifecycle.Rebind(m_nodeManager, handle);
                attached = true;
                return (ServiceResult.Good, true);
            }
            finally
            {
                if (!attached)
                {
                    if (monitoredNodeLinked)
                    {
                        if (isEvent)
                        {
                            monitoredNode!.Remove((IEventMonitoredItem)monitoredItem);
                        }
                        else
                        {
                            monitoredNode!.Remove(monitoredItem);
                        }
                    }

                    if (monitoredNodeCreated)
                    {
                        MonitoredNodes.Remove(handle.NodeId);
                        monitoredNode?.Dispose();
                    }

                    if (componentCacheAdded)
                    {
                        removeNodeFromComponentCache(context, handle);
                    }

                    MonitoredItems.TryRemove(monitoredItem.Id, out _);
                }
            }
        }

        private bool IsMultiConsumerNode(NodeId nodeId)
        {
            return m_nodeManager.IsMultipleEventConsumerNode(nodeId);
        }

        private readonly IAsyncNodeManager m_nodeManager;
        private readonly IServerInternal m_server;
    }
}
