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

namespace Opc.Ua.Server
{
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
                Utils.SilentDispose(m_samplingGroupManager);
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
                out IMonitoredItem existingMonitoredItem))
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
            // validate monitored item.
            if (!MonitoredItems.TryGetValue(
                monitoredItem.Id,
                out IMonitoredItem existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
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
        public (ServiceResult, MonitoringMode?) SetMonitoringMode(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle)
        {
            if (!MonitoredItems.TryGetValue(
                monitoredItem.Id,
                out IMonitoredItem existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            // update monitoring mode.
            MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);

            // need to provide an immediate update after enabling.
            if (previousMode == MonitoringMode.Disabled &&
                monitoringMode != MonitoringMode.Disabled)
            {
                var initialValue = new DataValue
                {
                    ServerTimestamp = DateTime.UtcNow,
                    StatusCode = StatusCodes.BadWaitingForInitialData
                };

                // read the initial value.

                if (monitoredItem.ManagerHandle is Node node)
                {
                    ServiceResult error = node.Read(
                        context,
                        monitoredItem.AttributeId,
                        initialValue);

                    if (ServiceResult.IsBad(error))
                    {
                        initialValue.Value = null;
                        initialValue.StatusCode = error.StatusCode;
                    }
                }

                monitoredItem.QueueValue(initialValue, null);
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
            // create monitored item.
            monitoredItem = m_samplingGroupManager.RestoreMonitoredItem(
                handle,
                storedMonitoredItem,
                savedOwnerIdentity);

            // save monitored item.
            MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

            return true;
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
                    = monitoredNode = new MonitoredNode2(m_nodeManager, source);
            }

            // remove existing monitored items with the same Id prior to insertion in order to avoid duplicates
            // this is necessary since the SubscribeToEvents method is called also from ModifyMonitoredItemsForEvents
            monitoredNode.EventMonitoredItems?.RemoveAll(e => e.Id == monitoredItem.Id);

            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            monitoredNode.Add(monitoredItem);
            MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

            return (monitoredNode, ServiceResult.Good);
        }

        private readonly CustomNodeManager2 m_nodeManager;
        private readonly SamplingGroupManager m_samplingGroupManager;
    }
}
