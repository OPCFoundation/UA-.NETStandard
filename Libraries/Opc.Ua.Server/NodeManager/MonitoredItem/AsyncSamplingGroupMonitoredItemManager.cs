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
    /// An async-aware implementation of <see cref="IAsyncMonitoredItemManager"/> that uses
    /// <see cref="AsyncSamplingGroupManager"/> to manage <see cref="AsyncSamplingGroup"/>
    /// instances, each of which samples values via <see cref="IReadAsyncNodeManager"/>.
    /// </summary>
    /// <remarks>
    /// This is the async counterpart of <see cref="SamplingGroupMonitoredItemManager"/>.
    /// </remarks>
    public class AsyncSamplingGroupMonitoredItemManager : IAsyncMonitoredItemManager
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="AsyncSamplingGroupMonitoredItemManager"/> class.
        /// </summary>
        /// <param name="asyncNodeManager">The async node manager that owns the nodes.</param>
        /// <param name="server">The server instance.</param>
        /// <param name="configuration">The application configuration.</param>
        public AsyncSamplingGroupMonitoredItemManager(
            IAsyncNodeManager asyncNodeManager,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            m_samplingGroupManager = new AsyncSamplingGroupManager(
                server,
                asyncNodeManager,
                (uint)configuration.ServerConfiguration!.MaxNotificationQueueSize,
                (uint)configuration.ServerConfiguration.MaxDurableNotificationQueueSize,
                configuration.ServerConfiguration.AvailableSamplingRates.ToArray()!);

            m_asyncNodeManager = asyncNodeManager;
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
            m_samplingGroupManager.ApplyChanges();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overridable version of the Dispose.
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
            if (!MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            m_samplingGroupManager.StopMonitoring(monitoredItem);
            MonitoredItems.TryRemove(monitoredItem.Id, out _);

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
            if (!MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existingMonitoredItem))
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
        public (ServiceResult, MonitoringMode?) SetMonitoringMode(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle)
        {
            if (!MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);

            if (previousMode == MonitoringMode.Disabled &&
                monitoringMode != MonitoringMode.Disabled)
            {
                var initialValue = new DataValue
                {
                    ServerTimestamp = DateTime.UtcNow,
                    StatusCode = StatusCodes.BadWaitingForInitialData
                };

                if (monitoredItem.ManagerHandle is Node node)
                {
                    ServiceResult error = node.Read(
                        context,
                        monitoredItem.AttributeId,
                        initialValue);

                    if (ServiceResult.IsBad(error))
                    {
                        initialValue.WrappedValue = default;
                        initialValue.StatusCode = error.StatusCode;
                    }
                }

                monitoredItem.QueueValue(initialValue, null!);
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
            monitoredItem = m_samplingGroupManager.RestoreMonitoredItem(
                handle,
                storedMonitoredItem,
                savedOwnerIdentity);

            MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

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

            if (unsubscribe)
            {
                if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
                {
                    return (null, StatusCodes.BadNodeIdUnknown);
                }

                monitoredNode.Remove(monitoredItem);
                MonitoredItems.TryRemove(monitoredItem.Id, out _);

                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(source.NodeId);
                }

                return (monitoredNode, ServiceResult.Good);
            }

            if (source is not BaseObjectState instance ||
                (instance.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
            {
                if (source is not ViewState view ||
                    (view.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return (null, StatusCodes.BadNotSupported);
                }
            }

            if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
            {
                MonitoredNodes[source.NodeId]
                    = monitoredNode = new MonitoredNode2(
                        (INodeManager3)m_asyncNodeManager.SyncNodeManager,
                        m_server,
                        source);
            }

            monitoredNode.EventMonitoredItems.TryRemove(monitoredItem.Id, out _);
            monitoredNode.Add(monitoredItem);
            MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

            return (monitoredNode, ServiceResult.Good);
        }

        private readonly IAsyncNodeManager m_asyncNodeManager;
        private readonly IServerInternal m_server;
        private readonly AsyncSamplingGroupManager m_samplingGroupManager;
    }
}
