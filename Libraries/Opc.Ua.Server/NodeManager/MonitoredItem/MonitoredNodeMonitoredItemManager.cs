/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the montioredItems for a NodeManager
    /// </summary>
    public class MonitoredNodeMonitoredItemManager : IMonitoredItemManager
    {
        /// <inheritdoc/>
        public MonitoredNodeMonitoredItemManager(CustomNodeManager2 nodeManager)
        {
            m_nodeManager = nodeManager;
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
            INodeManager nodeManager,
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
            uint monitoredItemId,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache)
        {
            // check if the node is already being monitored.

            if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out MonitoredNode2 monitoredNode))
            {
                NodeState cachedNode = addNodeToComponentCache(context, handle, handle.Node);
                MonitoredNodes[handle.Node.NodeId]
                    = monitoredNode = new MonitoredNode2(m_nodeManager, cachedNode);
            }

            handle.Node = monitoredNode.Node;
            handle.MonitoredNode = monitoredNode;

            // create the item.
            ISampledDataChangeMonitoredItem datachangeItem = new MonitoredItem(
                server,
                m_nodeManager,
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

            // save the monitored item.
            monitoredNode.Add(datachangeItem);
            MonitoredItems.AddOrUpdate(
                monitoredItemId,
                datachangeItem,
                (key, oldValue) => datachangeItem);

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
        }

        /// <inheritdoc/>
        public StatusCode DeleteMonitoredItem(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            NodeHandle handle)
        {
            // check if the node is already being monitored.
            if (MonitoredNodes.TryGetValue(handle.NodeId, out MonitoredNode2 monitoredNode))
            {
                monitoredNode.Remove(monitoredItem);
                MonitoredItems.TryRemove(monitoredItem.Id, out _);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(handle.NodeId);
                }
            }
            else
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public (ServiceResult, MonitoringMode?) SetMonitoringMode(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle)
        {
            // update monitoring mode.
            MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);

            // must send the latest value after enabling a disabled item.
            if (monitoringMode == MonitoringMode.Reporting &&
                previousMode == MonitoringMode.Disabled)
            {
                handle.MonitoredNode.QueueValue(context, handle.Node, monitoredItem);
            }

            return (StatusCodes.Good, previousMode);
        }

        /// <inheritdoc/>
        public bool RestoreMonitoredItem(
            IServerInternal server,
            INodeManager nodeManager,
            ServerSystemContext context,
            NodeHandle handle,
            IStoredMonitoredItem storedMonitoredItem,
            IUserIdentity savedOwnerIdentity,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            out ISampledDataChangeMonitoredItem monitoredItem)
        {
            // check if the node is already being monitored.
            if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out MonitoredNode2 monitoredNode))
            {
                NodeState cachedNode = addNodeToComponentCache(context, handle, handle.Node);
                MonitoredNodes[handle.Node.NodeId]
                    = monitoredNode = new MonitoredNode2(m_nodeManager, cachedNode);
            }

            handle.Node = monitoredNode.Node;
            handle.MonitoredNode = monitoredNode;

            // create the item.
            ISampledDataChangeMonitoredItem datachangeItem = new MonitoredItem(
                server,
                nodeManager,
                handle,
                storedMonitoredItem);

            // update monitored item list.
            monitoredItem = datachangeItem;

            // save the monitored item.
            monitoredNode.Add(datachangeItem);

            return true;
        }

        /// <inheritdoc/>
        public ServiceResult ModifyMonitoredItem(
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
        public (MonitoredNode2, ServiceResult) SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            MonitoredNode2 monitoredNode = null;
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
            if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
            {
                MonitoredNodes[source.NodeId]
                    = monitoredNode = new MonitoredNode2(m_nodeManager, source);
            }

            // remove existing monitored items with the same Id prior to insertion in order to avoid duplicates
            // this is necessary since the SubscribeToEvents method is called also from ModifyMonitoredItemsForEvents
            monitoredNode.EventMonitoredItems?.RemoveAll(e => e.Id == monitoredItem.Id);

            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            monitoredNode.Add(monitoredItem);
            _ = MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

            return (monitoredNode, ServiceResult.Good);
        }

        private readonly CustomNodeManager2 m_nodeManager;
    }
}
