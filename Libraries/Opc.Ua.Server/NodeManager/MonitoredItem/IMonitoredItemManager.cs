using System;
using System.Collections.Concurrent;

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

}
