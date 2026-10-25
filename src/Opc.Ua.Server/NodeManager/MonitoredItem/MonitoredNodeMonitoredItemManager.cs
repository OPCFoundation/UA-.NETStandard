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
        IMonitoredItemManagerLifecycle,
        ICustomMonitoredItemManager
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

        /// <summary>
        /// Queues a semantic force-resample for the exact node and sampled item owned by this manager.
        /// </summary>
        internal bool TryQueueSemanticResample(NodeState source, ISampledDataChangeMonitoredItem item)
        {
            if (source == null ||
                item == null ||
                item is not IDataChangeMonitoredItem2 dataChangeItem ||
                item.ManagerHandle is not NodeHandle handle ||
                !ReferenceEquals(handle.Node, source) ||
                !MonitoredNodes.TryGetValue(source.NodeId, out MonitoredNode2? monitoredNode))
            {
                return false;
            }

            return monitoredNode.TryQueueSemanticResample(source, dataChangeItem);
        }

        /// <summary>
        /// Waits for all node-consumer work pending when this method is called.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A task that completes when the current consumer queues have drained.</returns>
        internal async ValueTask WaitForPendingSemanticResamplesAsync(CancellationToken cancellationToken)
        {
            foreach (MonitoredNode2 monitoredNode in MonitoredNodes.Values)
            {
                await monitoredNode.WaitForPendingNotificationsAsync(cancellationToken).ConfigureAwait(false);
            }
        }

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

            NodeState cachedNode = addNodeToComponentCache(context, handle, handle.Node);

            if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out MonitoredNode2? monitoredNode))
            {
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
        ISampledDataChangeMonitoredItem ICustomMonitoredItemManager.CreateCustomMonitoredItem(
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
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache,
            MonitoredItemFactory factory,
            bool initialValueQueued)
        {
            // monitored nodes report changes; there is no immediate sample to suppress.
            _ = initialValueQueued;
            MonitoredNode2? monitoredNode = null;
            ISampledDataChangeMonitoredItem? monitoredItem = null;
            bool monitoredNodeCreated = false;
            bool monitoredNodeLinked = false;
            bool componentCacheAdded = false;

            uint monitoredItemId;
            do
            {
                monitoredItemId = monitoredItemIdFactory.GetNextId();
            } while (!MonitoredItems.TryAdd(monitoredItemId, null!));

            try
            {
                NodeState cachedNode = addNodeToComponentCache(context, handle, handle.Node);
                componentCacheAdded = true;
                if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out monitoredNode))
                {
                    MonitoredNodes[handle.Node.NodeId] = monitoredNode =
                        new MonitoredNode2(
                            m_nodeManager,
                            m_server,
                            cachedNode,
                            IsMultiConsumerNode(cachedNode.NodeId));
                    monitoredNodeCreated = true;
                }

                handle.Node = monitoredNode.Node;
                handle.MonitoredNode = monitoredNode;

                var factoryContext = new MonitoredItemFactoryContext(
                    server,
                    nodeManager,
                    context,
                    handle,
                    subscriptionId,
                    monitoredItemId,
                    publishingInterval,
                    diagnosticsMasks,
                    timestampsToReturn,
                    itemToCreate,
                    euRange,
                    filterToUse,
                    samplingInterval,
                    revisedQueueSize,
                    createDurable);
                monitoredItem = factory(factoryContext);
                CustomMonitoredItemValidation.Validate(factoryContext, monitoredItem);

                monitoredNode.Add(monitoredItem);
                monitoredNodeLinked = true;
                MonitoredItems[monitoredItemId] = monitoredItem;
                return monitoredItem;
            }
            catch
            {
                if (monitoredNodeLinked)
                {
                    monitoredNode!.Remove(monitoredItem!);
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
                MonitoredItems.TryRemove(monitoredItemId, out _);
                monitoredItem?.Dispose();
                throw;
            }
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
            m_eventLinkCounts.Clear();

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
            (ServiceResult result, _) = RemoveRegistration(
                context, monitoredItem, handle, null, sourceRelease: false);
            return result.StatusCode;
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

            // must send the latest value after enabling a disabled item. For an item whose
            // node was deleted SetMonitoringMode already queued Bad_NodeIdUnknown, and the
            // stale node must not override it with a Good value.
            if (previousMode == MonitoringMode.Disabled &&
                monitoringMode != MonitoringMode.Disabled &&
                monitoredItem is not IDetachableMonitoredItem { IsDeleted: true })
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

            NodeState cachedNode = addNodeToComponentCache(context, handle, handle.Node);

            // check if the node is already being monitored.
            if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out MonitoredNode2? monitoredNode))
            {
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

            ((IDetachableMonitoredItem)datachangeItem).Rebind(m_nodeManager, handle);

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
            if (MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? registeredItem) &&
                !ReferenceEquals(registeredItem, monitoredItem))
            {
                return (null, StatusCodes.BadMonitoredItemIdInvalid);
            }
            // handle unsubscribe.
            if (unsubscribe)
            {
                // check for existing monitored node.
                if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode!))
                {
                    return (null, StatusCodes.BadNodeIdUnknown);
                }

                if (monitoredNode.EventMonitoredItems.TryGetValue(
                    monitoredItem.Id, out IEventMonitoredItem? registeredEvent) &&
                    !ReferenceEquals(registeredEvent, monitoredItem))
                {
                    return (monitoredNode, StatusCodes.BadMonitoredItemIdInvalid);
                }
                RemoveEventLink(monitoredNode, monitoredItem);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(source.NodeId);
                    monitoredNode.Dispose();
                }
                if (!IsEventMonitoredItemLinked(monitoredItem.Id))
                {
                    MonitoredItems.TryRemove(monitoredItem.Id, out _);
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
            if (monitoredNode.EventMonitoredItems.TryGetValue(
                monitoredItem.Id, out IEventMonitoredItem? existingEvent) &&
                !ReferenceEquals(existingEvent, monitoredItem))
            {
                return (monitoredNode, StatusCodes.BadMonitoredItemIdInvalid);
            }
            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            AddEventLink(monitoredNode, monitoredItem);

            // A Server subscription shares this item across the manager's root notifiers.
            if (!MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem) &&
                (!MonitoredItems.TryGetValue(monitoredItem.Id, out registeredItem) ||
                    !ReferenceEquals(registeredItem, monitoredItem)))
            {
                RemoveEventLink(monitoredNode, monitoredItem);
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(source.NodeId);
                    monitoredNode.Dispose();
                }
                return (null, StatusCodes.BadMonitoredItemIdInvalid);
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
            if (monitoredItem is not IDetachableMonitoredItem)
            {
                return (StatusCodes.BadNotSupported, false);
            }
            return RemoveRegistration(
                context,
                monitoredItem,
                monitoredItem.ManagerHandle as NodeHandle,
                removeNodeFromComponentCache,
                sourceRelease: true);
        }

        /// <summary>
        /// Removes exact native ownership for both client deletion and generation source release.
        /// </summary>
        private (ServiceResult Result, bool Changed) RemoveRegistration(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            NodeHandle? handle,
            Action<ISystemContext, NodeHandle>? removeNodeFromComponentCache,
            bool sourceRelease)
        {
            if (!MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existing))
            {
                return sourceRelease && monitoredItem is IDetachableMonitoredItem { IsDetached: true }
                    ? (ServiceResult.Good, false)
                    : (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            if (!ReferenceEquals(existing, monitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            if (handle is null)
            {
                return (sourceRelease ? StatusCodes.BadInvalidState : StatusCodes.BadMonitoredItemIdInvalid, false);
            }
            NodeId nodeId = sourceRelease ? monitoredItem.NodeId : handle.NodeId;
            if (!MonitoredNodes.TryGetValue(nodeId, out MonitoredNode2? monitoredNode))
            {
                return (sourceRelease ? StatusCodes.BadInvalidState : StatusCodes.BadMonitoredItemIdInvalid, false);
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

                RemoveEventLink(monitoredNode, eventItem);
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
                removeNodeFromComponentCache?.Invoke(context, handle);
            }

            bool keepAllEventsOwner = !sourceRelease &&
                monitoredItem is IEventMonitoredItem { MonitoringAllEvents: true } &&
                IsEventMonitoredItemLinked(monitoredItem.Id);
            if (!keepAllEventsOwner &&
                !((ICollection<KeyValuePair<uint, IMonitoredItem>>)MonitoredItems).Remove(
                    new KeyValuePair<uint, IMonitoredItem>(monitoredItem.Id, monitoredItem)))
            {
                return (StatusCodes.BadInvalidState, false);
            }

            if (!monitoredNode.HasMonitoredItems)
            {
                MonitoredNodes.Remove(nodeId);
                monitoredNode.Dispose();
            }

            if (sourceRelease)
            {
                ((IDetachableMonitoredItem)monitoredItem).BeginDetach();
            }
            else if (!keepAllEventsOwner)
            {
                monitoredItem.Dispose();
            }
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
            if (monitoredItem is not IDetachableMonitoredItem lifecycle)
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

                    AddEventLink(monitoredNode, (IEventMonitoredItem)monitoredItem);
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
                            RemoveEventLink(monitoredNode!, (IEventMonitoredItem)monitoredItem);
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

        private bool IsEventMonitoredItemLinked(uint monitoredItemId)
        {
            return m_eventLinkCounts.ContainsKey(monitoredItemId);
        }

        private void AddEventLink(MonitoredNode2 monitoredNode, IEventMonitoredItem eventItem)
        {
            bool linked = monitoredNode.EventMonitoredItems.ContainsKey(eventItem.Id);
            monitoredNode.Add(eventItem);
            if (!linked && monitoredNode.EventMonitoredItems.ContainsKey(eventItem.Id))
            {
                m_eventLinkCounts.AddOrUpdate(eventItem.Id, 1, static (_, count) => count + 1);
            }
        }

        private void RemoveEventLink(MonitoredNode2 monitoredNode, IEventMonitoredItem eventItem)
        {
            bool linked = monitoredNode.EventMonitoredItems.ContainsKey(eventItem.Id);
            monitoredNode.Remove(eventItem);
            if (!linked || monitoredNode.EventMonitoredItems.ContainsKey(eventItem.Id))
            {
                return;
            }
            while (m_eventLinkCounts.TryGetValue(eventItem.Id, out int count))
            {
                if (count <= 1
                    ? ((ICollection<KeyValuePair<uint, int>>)m_eventLinkCounts).Remove(
                        new KeyValuePair<uint, int>(eventItem.Id, count))
                    : m_eventLinkCounts.TryUpdate(eventItem.Id, count - 1, count))
                {
                    return;
                }
            }
        }

        private bool IsMultiConsumerNode(NodeId nodeId)
        {
            return m_nodeManager.IsMultipleEventConsumerNode(nodeId);
        }

        private readonly IAsyncNodeManager m_nodeManager;
        private readonly IServerInternal m_server;
        private readonly ConcurrentDictionary<uint, int> m_eventLinkCounts = new();
    }
}
