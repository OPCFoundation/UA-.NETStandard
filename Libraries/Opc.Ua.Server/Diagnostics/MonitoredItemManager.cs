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
        ServiceResult ModifyMonitoredItem(
           OperationContext context,
           TimestampsToReturn timestampsToReturn,
           ISampledDataChangeMonitoredItem monitoredItem,
           MonitoredItemModifyRequest itemToModify,
           Range range);

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
        ServiceResult SetMonitoringMode(
            ServerSystemContext context,
            IMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle);

        /// <summary>
        /// Restore a monitored item
        /// </summary>
        bool RestoreMonitoredItem(
            ServerSystemContext context,
            NodeHandle handle,
            IStoredMonitoredItem storedMonitoredItem,
            out IMonitoredItem monitoredItem);


        // TODO: Condition refresh???

        // TODO: Subscribe to Events
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
        }
        /// <inheritdoc/>
        public NodeIdDictionary<MonitoredNode2> MonitoredNodes => throw new InvalidOperationException("Monitored items are managed using sampling groups");
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
            m_samplingGroupManager.Dispose();
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

        private readonly ConcurrentDictionary<uint, IMonitoredItem> m_monitoredItems;
        private SamplingGroupManager m_samplingGroupManager;
    }

}
