using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the montioredItems for a NodeManager
    /// </summary>
    public interface IMonitoredItemManager : IDisposable
    {
        /// <summary>
        /// The table of monitored items.
        /// </summary>
        ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems { get; }
        /// <summary>
        /// Gets the table of nodes being monitored.
        /// If sampling groups are used only contains the MonitoredNodes being monitored for events
        /// </summary>
        NodeIdDictionary<MonitoredNode2> MonitoredNodes { get; }
        /// <summary>
        /// Apply pending changes to the monitored items.
        /// </summary>
        void ApplyChanges();
        /// <summary>
        /// Create a MonitoredItem and save it in the store
        /// </summary>
        IMonitoredItem CreateMonitoredItem(IServerInternal server,
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
                                          Func<ISystemContext, NodeHandle, NodeState, NodeState> AddNodeToComponentCache);
        /// <summary>
        /// Modify a monitored item
        /// </summary>
        ServiceResult ModifyMonitoredItem(ServerSystemContext context,
                                                 DiagnosticsMasks diagnosticsMasks,
                                                 TimestampsToReturn timestampsToReturn,
                                                 MonitoringFilter filterToUse,
                                                 Range euRange,
                                                 double samplingInterval,
                                                 uint revisedQueueSize,
                                                 ISampledDataChangeMonitoredItem monitoredItem,
                                                 MonitoredItemModifyRequest itemToModify);

        /// <summary>
        /// Delete a MonitoredItem and remove it from the store
        /// </summary>
        StatusCode DeleteMonitoredItem(
            ServerSystemContext context,
            IMonitoredItem monitoredItem,
            NodeHandle handle);

        /// <summary>
        /// Set the monitoring mode for a monitored item
        /// </summary>
        (ServiceResult, MonitoringMode?) SetMonitoringMode(
            ServerSystemContext context,
            IMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle);

        /// <summary>
        /// Restore a monitored item
        /// </summary>
        bool RestoreMonitoredItem(
            IServerInternal server,
            INodeManager nodeManager,
            ServerSystemContext context,
            NodeHandle handle,
            IStoredMonitoredItem storedMonitoredItem,
            IUserIdentity savedOwnerIdentity,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> AddNodeToComponentCache,
            out IMonitoredItem monitoredItem);

        /// <summary>
        /// Subscribes to events.
        /// </summary>
        (MonitoredNode2, ServiceResult) SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe);
    }
    /// <summary>
    /// Manages the montioredItems for a NodeManager
    /// </summary>
    public class MonitoredNodeMonitoredItemManager : IMonitoredItemManager
    {
        /// <inheritdoc/>
        public MonitoredNodeMonitoredItemManager(
            CustomNodeManager2 nodeManager)
        {
            m_nodeManager = nodeManager;
            m_monitoredNodes = new NodeIdDictionary<MonitoredNode2>();
            m_monitoredItems = new ConcurrentDictionary<uint, IMonitoredItem>();
        }
        /// <inheritdoc/>
        public NodeIdDictionary<MonitoredNode2> MonitoredNodes => m_monitoredNodes;
        /// <inheritdoc/>
        public ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => m_monitoredItems;

        /// <inheritdoc/>
        public IMonitoredItem CreateMonitoredItem(
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
            Func<ISystemContext, NodeHandle, NodeState, NodeState> AddNodeToComponentCache)
        {
            // check if the node is already being monitored.
            MonitoredNode2 monitoredNode = null;

            if (!m_monitoredNodes.TryGetValue(handle.Node.NodeId, out monitoredNode))
            {
                NodeState cachedNode = AddNodeToComponentCache(context, handle, handle.Node);
                m_monitoredNodes[handle.Node.NodeId] = monitoredNode = new MonitoredNode2(m_nodeManager, cachedNode);
            }

            handle.Node = monitoredNode.Node;
            handle.MonitoredNode = monitoredNode;

            // create the item.
            MonitoredItem datachangeItem = new MonitoredItem(
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
            m_monitoredItems.AddOrUpdate(monitoredItemId, datachangeItem, (key, oldValue) => datachangeItem);


            return datachangeItem;
        }
        /// <inheritdoc/>
        public void ApplyChanges()
        {
            //only needed for sampling groups
            return;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            //only needed for sampling groups
            return;
        }

        /// <inheritdoc/>
        public StatusCode DeleteMonitoredItem(ServerSystemContext context, IMonitoredItem monitoredItem, NodeHandle handle)
        {
            // check for valid monitored item.
            MonitoredItem datachangeItem = monitoredItem as MonitoredItem;

            // check if the node is already being monitored.
            MonitoredNode2 monitoredNode = null;

            if (m_monitoredNodes.TryGetValue(handle.NodeId, out monitoredNode))
            {
                monitoredNode.Remove(datachangeItem);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    m_monitoredNodes.Remove(handle.NodeId);
                }
            }
            else
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public (ServiceResult, MonitoringMode?) SetMonitoringMode(ServerSystemContext context, IMonitoredItem monitoredItem, MonitoringMode monitoringMode, NodeHandle handle)
        {
            // check for valid monitored item.
            MonitoredItem datachangeItem = monitoredItem as MonitoredItem;

            // update monitoring mode.
            MonitoringMode previousMode = datachangeItem.SetMonitoringMode(monitoringMode);

            // must send the latest value after enabling a disabled item.
            if (monitoringMode == MonitoringMode.Reporting && previousMode == MonitoringMode.Disabled)
            {
                handle.MonitoredNode.QueueValue(context, handle.Node, datachangeItem);
            }

            return (StatusCodes.Good, previousMode);
        }

        /// <inheritdoc/>
        public bool RestoreMonitoredItem(IServerInternal server,
                                         INodeManager nodeManager,
                                         ServerSystemContext context,
                                         NodeHandle handle,
                                         IStoredMonitoredItem storedMonitoredItem,
                                         IUserIdentity savedOwnerIdentity,
                                         Func<ISystemContext, NodeHandle, NodeState, NodeState> AddNodeToComponentCache,
                                         out IMonitoredItem monitoredItem)
        {
            // check if the node is already being monitored.
            MonitoredNode2 monitoredNode = null;

            if (!m_monitoredNodes.TryGetValue(handle.Node.NodeId, out monitoredNode))
            {
                NodeState cachedNode = AddNodeToComponentCache(context, handle, handle.Node);
                m_monitoredNodes[handle.Node.NodeId] = monitoredNode = new MonitoredNode2(m_nodeManager, cachedNode);
            }

            handle.Node = monitoredNode.Node;
            handle.MonitoredNode = monitoredNode;

            // create the item.
            var datachangeItem = new MonitoredItem(
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
        public ServiceResult ModifyMonitoredItem(ServerSystemContext context,
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
        public (MonitoredNode2, ServiceResult) SubscribeToEvents(ServerSystemContext context, NodeState source, IEventMonitoredItem monitoredItem, bool unsubscribe)
        {
            MonitoredNode2 monitoredNode = null;
            // handle unsubscribe.
            if (unsubscribe)
            {
                // check for existing monitored node.
                if (!m_monitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
                {
                    return (null, StatusCodes.BadNodeIdUnknown);
                }

                monitoredNode.Remove(monitoredItem);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    m_monitoredNodes.Remove(source.NodeId);
                }

                return (monitoredNode, ServiceResult.Good);
            }

            // only objects or views can be subscribed to.
            if (!(source is BaseObjectState instance) || (instance.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
            {
                if (!(source is ViewState view) || (view.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return (null, StatusCodes.BadNotSupported);
                }
            }

            // check for existing monitored node.
            if (!m_monitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
            {
                m_monitoredNodes[source.NodeId] = monitoredNode = new MonitoredNode2(m_nodeManager, source);
            }

            if (monitoredNode.EventMonitoredItems != null)
            {
                // remove existing monitored items with the same Id prior to insertion in order to avoid duplicates
                // this is necessary since the SubscribeToEvents method is called also from ModifyMonitoredItemsForEvents
                monitoredNode.EventMonitoredItems.RemoveAll(e => e.Id == monitoredItem.Id);
            }

            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            monitoredNode.Add(monitoredItem);

            return (monitoredNode, ServiceResult.Good);
        }

        private readonly CustomNodeManager2 m_nodeManager;
        private readonly NodeIdDictionary<MonitoredNode2> m_monitoredNodes;
        private readonly ConcurrentDictionary<uint, IMonitoredItem> m_monitoredItems;
    }

    /// <summary>
    /// Manages the montioredItems for a NodeManager
    /// </summary>
    public class SamplingGroupMonitoredItemManager : IMonitoredItemManager
    {
        /// <inheritdoc/>
        public SamplingGroupMonitoredItemManager(
            CustomNodeManager2 nodeManager,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {

            m_samplingGroupManager = new SamplingGroupManager(
                server,
                nodeManager,
                (uint)configuration.ServerConfiguration.MaxNotificationQueueSize,
                (uint)configuration.ServerConfiguration.MaxDurableNotificationQueueSize,
                configuration.ServerConfiguration.AvailableSamplingRates);

            m_nodeManager = nodeManager;
            m_monitoredNodes = new NodeIdDictionary<MonitoredNode2>();
            m_monitoredItems = new ConcurrentDictionary<uint, IMonitoredItem>();
        }
        /// <inheritdoc/>
        public NodeIdDictionary<MonitoredNode2> MonitoredNodes => m_monitoredNodes;
        /// <inheritdoc/>
        public ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => m_monitoredItems;

        /// <inheritdoc/>
        public IMonitoredItem CreateMonitoredItem(
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
            Func<ISystemContext, NodeHandle, NodeState, NodeState> AddNodeToComponentCache)
        {
            // create monitored item.
            MonitoredItem monitoredItem = m_samplingGroupManager.CreateMonitoredItem(
                context.OperationContext,
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
            m_monitoredItems.AddOrUpdate(monitoredItemId, monitoredItem, (key, oldValue) => monitoredItem);

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
            Utils.SilentDispose(m_samplingGroupManager);
        }

        /// <inheritdoc/>
        public StatusCode DeleteMonitoredItem(ServerSystemContext context, IMonitoredItem monitoredItem, NodeHandle handle)
        {
            // validate monitored item.
            IMonitoredItem existingMonitoredItem = null;

            if (!m_monitoredItems.TryGetValue(monitoredItem.Id, out existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            if (!Object.ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            // remove item.
            m_samplingGroupManager.StopMonitoring((MonitoredItem)monitoredItem);

            // remove association with the group.
            m_monitoredItems.TryRemove(monitoredItem.Id, out _);

            // delete successful.
            return StatusCodes.Good;
        }
        /// <inheritdoc/>
        public ServiceResult ModifyMonitoredItem(ServerSystemContext context,
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
            IMonitoredItem existingMonitoredItem = null;

            if (!m_monitoredItems.TryGetValue(monitoredItem.Id, out existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            if (!Object.ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            return m_samplingGroupManager.ModifyMonitoredItem(
                        context.OperationContext,
                        timestampsToReturn,
                        monitoredItem,
                        itemToModify,
                        euRange);
        }

        /// <inheritdoc/>
        public (ServiceResult, MonitoringMode?) SetMonitoringMode(ServerSystemContext context, IMonitoredItem monitoredItem, MonitoringMode monitoringMode, NodeHandle handle)
        {
            // check for valid monitored item.
            MonitoredItem datachangeItem = monitoredItem as MonitoredItem;
            IMonitoredItem existingMonitoredItem;

            if (!m_monitoredItems.TryGetValue(monitoredItem.Id, out existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            if (!Object.ReferenceEquals(datachangeItem, existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            // update monitoring mode.
            MonitoringMode previousMode = datachangeItem.SetMonitoringMode(monitoringMode);

            // need to provide an immediate update after enabling.
            if (previousMode == MonitoringMode.Disabled && monitoringMode != MonitoringMode.Disabled)
            {
                DataValue initialValue = new DataValue();

                initialValue.ServerTimestamp = DateTime.UtcNow;
                initialValue.StatusCode = StatusCodes.BadWaitingForInitialData;

                // read the initial value.
                Node node = monitoredItem.ManagerHandle as Node;

                if (node != null)
                {
                    ServiceResult error = node.Read(context, datachangeItem.AttributeId, initialValue);

                    if (ServiceResult.IsBad(error))
                    {
                        initialValue.Value = null;
                        initialValue.StatusCode = error.StatusCode;
                    }
                }

                datachangeItem.QueueValue(initialValue, null);
            }

            return (StatusCodes.Good, previousMode);
        }

        /// <inheritdoc/>
        public bool RestoreMonitoredItem(IServerInternal server,
                                         INodeManager nodeManager,
                                         ServerSystemContext context,
                                         NodeHandle handle,
                                         IStoredMonitoredItem storedMonitoredItem,
                                         IUserIdentity savedOwnerIdentity,
                                         Func<ISystemContext, NodeHandle, NodeState, NodeState> AddNodeToComponentCache,
                                         out IMonitoredItem monitoredItem)
        {
            // create monitored item.
            monitoredItem = m_samplingGroupManager.RestoreMonitoredItem(
                handle,
                storedMonitoredItem,
                savedOwnerIdentity
                );

            // save monitored item.
            m_monitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

            return true;
        }

        /// <inheritdoc/>
        public (MonitoredNode2, ServiceResult) SubscribeToEvents(ServerSystemContext context, NodeState source, IEventMonitoredItem monitoredItem, bool unsubscribe)
        {
            MonitoredNode2 monitoredNode = null;
            // handle unsubscribe.
            if (unsubscribe)
            {
                // check for existing monitored node.
                if (!m_monitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
                {
                    return (null, StatusCodes.BadNodeIdUnknown);
                }

                monitoredNode.Remove(monitoredItem);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    m_monitoredNodes.Remove(source.NodeId);
                }

                return (monitoredNode, ServiceResult.Good);
            }

            // only objects or views can be subscribed to.
            if (!(source is BaseObjectState instance) || (instance.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
            {
                if (!(source is ViewState view) || (view.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return (null, StatusCodes.BadNotSupported);
                }
            }

            // check for existing monitored node.
            if (!m_monitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
            {
                m_monitoredNodes[source.NodeId] = monitoredNode = new MonitoredNode2(m_nodeManager, source);
            }

            if (monitoredNode.EventMonitoredItems != null)
            {
                // remove existing monitored items with the same Id prior to insertion in order to avoid duplicates
                // this is necessary since the SubscribeToEvents method is called also from ModifyMonitoredItemsForEvents
                monitoredNode.EventMonitoredItems.RemoveAll(e => e.Id == monitoredItem.Id);
            }

            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            monitoredNode.Add(monitoredItem);

            return (monitoredNode, ServiceResult.Good);
        }

        private readonly CustomNodeManager2 m_nodeManager;
        private readonly NodeIdDictionary<MonitoredNode2> m_monitoredNodes;
        private readonly ConcurrentDictionary<uint, IMonitoredItem> m_monitoredItems;
        private SamplingGroupManager m_samplingGroupManager;
    }

}
