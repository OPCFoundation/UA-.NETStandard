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
    /// Adapts a synchronous <see cref="IMonitoredItemManager"/> to the
    /// <see cref="IAsyncMonitoredItemManager"/> interface.
    /// </summary>
    /// <remarks>
    /// All calls are forwarded to the wrapped synchronous implementation.
    /// If the wrapped implementation already implements
    /// <see cref="IAsyncMonitoredItemManager"/> directly, the adapter simply
    /// forwards calls through it without boxing overhead.
    /// </remarks>
    public class AsyncMonitoredItemManagerAdapter : IAsyncMonitoredItemManager
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="AsyncMonitoredItemManagerAdapter"/> class.
        /// </summary>
        /// <param name="syncManager">
        /// The synchronous monitored item manager to adapt.
        /// </param>
        public AsyncMonitoredItemManagerAdapter(IMonitoredItemManager syncManager)
        {
            SyncManager = syncManager ?? throw new ArgumentNullException(nameof(syncManager));
        }

        /// <summary>
        /// Gets the wrapped synchronous monitored item manager.
        /// </summary>
        public IMonitoredItemManager SyncManager { get; }

        /// <inheritdoc/>
        public NodeIdDictionary<MonitoredNode2> MonitoredNodes => SyncManager.MonitoredNodes;

        /// <inheritdoc/>
        public ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems => SyncManager.MonitoredItems;

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
            return SyncManager.CreateMonitoredItem(
                server,
                nodeManager,
                context,
                handle,
                subscriptionId,
                publishingInterval,
                diagnosticsMasks,
                timestampsToReturn,
                itemToCreate,
                euRange,
                filterToUse,
                samplingInterval,
                revisedQueueSize,
                createDurable,
                monitoredItemIdFactory,
                addNodeToComponentCache);
        }

        /// <inheritdoc/>
        public void ApplyChanges()
        {
            SyncManager.ApplyChanges();
        }

        /// <inheritdoc/>
        public StatusCode DeleteMonitoredItem(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            NodeHandle handle)
        {
            return SyncManager.DeleteMonitoredItem(context, monitoredItem, handle);
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
            return SyncManager.ModifyMonitoredItem(
                context,
                diagnosticsMasks,
                timestampsToReturn,
                filterToUse,
                euRange,
                samplingInterval,
                revisedQueueSize,
                monitoredItem,
                itemToModify);
        }

        /// <inheritdoc/>
        public (ServiceResult, MonitoringMode?) SetMonitoringMode(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle)
        {
            return SyncManager.SetMonitoringMode(context, monitoredItem, monitoringMode, handle);
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
            return SyncManager.RestoreMonitoredItem(
                server,
                nodeManager,
                context,
                handle,
                storedMonitoredItem,
                savedOwnerIdentity,
                addNodeToComponentCache,
                out monitoredItem);
        }

        /// <inheritdoc/>
        public (MonitoredNode2?, ServiceResult) SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            return SyncManager.SubscribeToEvents(context, source, monitoredItem, unsubscribe);
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
                SyncManager.Dispose();
            }
        }
    }
}
