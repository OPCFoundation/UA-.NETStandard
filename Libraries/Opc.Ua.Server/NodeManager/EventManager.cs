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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages all events raised within the server.
    /// </summary>
    public class EventManager : IDisposable
    {
        /// <summary>
        /// Creates a new instance of a sampling group.
        /// </summary>
        public EventManager(IServerInternal server, uint maxQueueSize, uint maxDurableQueueSize)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_monitoredItems = [];
            m_maxEventQueueSize = maxQueueSize;
            m_maxDurableEventQueueSize = maxDurableQueueSize;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
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
                List<IEventMonitoredItem>? monitoredItems;
                lock (m_lock)
                {
                    monitoredItems = [.. m_monitoredItems.Values];
                    m_monitoredItems.Clear();
                }

                foreach (IEventMonitoredItem monitoredItem in monitoredItems)
                {
                    monitoredItem?.Dispose();
                }
            }
        }

        /// <summary>
        /// Reports an event to the supplied monitored items WITHOUT
        /// validating <see cref="PermissionType.ReceiveEvents"/>. Use
        /// <see cref="ReportEventAsync"/> instead so per-item permission
        /// checks on the event's <c>EventTypeId</c> and <c>SourceNode</c>
        /// are honored (Part 3 §8.55).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="e"/> is <c>null</c>.</exception>
        [Obsolete("Use ReportEventAsync so PermissionType.ReceiveEvents is validated on EventTypeId and SourceNode for each monitored item.")]
        public static void ReportEvent(IFilterTarget e, IList<IEventMonitoredItem> monitoredItems)
        {
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }

            foreach (IEventMonitoredItem monitoredItem in monitoredItems)
            {
                monitoredItem.QueueEvent(e);
            }
        }

        /// <summary>
        /// Reports an event to the supplied monitored items, validating
        /// <see cref="PermissionType.ReceiveEvents"/> on both the
        /// <c>EventTypeId</c> and <c>SourceNode</c> of the event for each
        /// item before queueing (per Part 3 §8.55).
        /// </summary>
        /// <param name="e">The event payload.</param>
        /// <param name="nodeManager">
        /// The node manager that owns the source. Used to evaluate role
        /// permissions for the event's <c>EventTypeId</c> and
        /// <c>SourceNode</c>.
        /// </param>
        /// <param name="monitoredItems">The candidate receivers.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="e"/>, <paramref name="nodeManager"/>, or
        /// <paramref name="monitoredItems"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask ReportEventAsync(
            IFilterTarget e,
            IAsyncNodeManager nodeManager,
            IList<IEventMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }
            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            foreach (IEventMonitoredItem monitoredItem in monitoredItems)
            {
                if (monitoredItem == null)
                {
                    continue;
                }

                ServiceResult result = await nodeManager
                    .ValidateEventRolePermissionsAsync(monitoredItem, e, cancellationToken)
                    .ConfigureAwait(false);

                if (ServiceResult.IsBad(result))
                {
                    continue;
                }

                monitoredItem.QueueEvent(e);
            }
        }

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        public IEventMonitoredItem CreateMonitoredItem(
            OperationContext context,
            IAsyncNodeManager nodeManager,
            object handle,
            uint subscriptionId,
            MonitoredItemIdFactory monitoredItemIdFactory,
            TimestampsToReturn timestampsToReturn,
            double publishingInterval,
            MonitoredItemCreateRequest itemToCreate,
            EventFilter filter,
            bool createDurable)
        {
            lock (m_lock)
            {
                // calculate sampling interval.
                double samplingInterval = itemToCreate.RequestedParameters.SamplingInterval;

                if (samplingInterval < 0)
                {
                    samplingInterval = publishingInterval;
                }

                // limit the queue size.
                uint revisedQueueSize = CalculateRevisedQueueSize(
                    createDurable,
                    itemToCreate.RequestedParameters.QueueSize);

                // Allocate the monitored item id
                uint monitoredItemId;
                do
                {
                    monitoredItemId = monitoredItemIdFactory.GetNextId();
                } while (!m_monitoredItems.TryAdd(monitoredItemId, null!));

                // create the monitored item.
                IEventMonitoredItem monitoredItem = new MonitoredItem(
                    m_server,
                    nodeManager,
                    handle,
                    subscriptionId,
                    monitoredItemId,
                    itemToCreate.ItemToMonitor,
                    context.DiagnosticsMask,
                    timestampsToReturn,
                    itemToCreate.MonitoringMode,
                    itemToCreate.RequestedParameters.ClientHandle,
                    filter,
                    filter,
                    null,
                    samplingInterval,
                    revisedQueueSize,
                    itemToCreate.RequestedParameters.DiscardOldest,
                    MinimumSamplingIntervals.Continuous,
                    createDurable);

                // now save the monitored item.
                Debug.Assert(m_monitoredItems[monitoredItemId] == null);
                m_monitoredItems[monitoredItemId] = monitoredItem;
                return monitoredItem;
            }
        }

        /// <summary>
        /// Restore a MonitoredItem after a restart
        /// </summary>
        public IEventMonitoredItem RestoreMonitoredItem(
            IAsyncNodeManager nodeManager,
            object handle,
            IStoredMonitoredItem storedMonitoredItem)
        {
            lock (m_lock)
            {
                // limit the queue size.
                storedMonitoredItem.QueueSize = CalculateRevisedQueueSize(
                    storedMonitoredItem.IsDurable,
                    storedMonitoredItem.QueueSize);

                // create the monitored item.
                var monitoredItem = new MonitoredItem(
                    m_server,
                    nodeManager,
                    handle,
                    storedMonitoredItem);

                // save the monitored item.
                m_monitoredItems.Add(monitoredItem.Id, monitoredItem);

                return monitoredItem;
            }
        }

        /// <summary>
        /// calculates a revised queue size based on the application confiugration limits
        /// </summary>
        private uint CalculateRevisedQueueSize(bool isDurable, uint queueSize)
        {
            if (queueSize > m_maxEventQueueSize && !isDurable)
            {
                queueSize = m_maxEventQueueSize;
            }

            if (queueSize > m_maxDurableEventQueueSize && isDurable)
            {
                queueSize = m_maxDurableEventQueueSize;
            }

            return queueSize;
        }

        /// <summary>
        /// Modifies a monitored item.
        /// </summary>
        public void ModifyMonitoredItem(
            OperationContext context,
            IEventMonitoredItem monitoredItem,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequest itemToModify,
            EventFilter filter)
        {
            lock (m_lock)
            {
                // should never be called with items that it does not own.
                if (!m_monitoredItems.ContainsKey(monitoredItem.Id))
                {
                    return;
                }

                // limit the queue size.
                uint revisedQueueSize = CalculateRevisedQueueSize(
                    monitoredItem.IsDurable,
                    itemToModify.RequestedParameters.QueueSize);

                // modify the attributes.
                monitoredItem.ModifyAttributes(
                    context.DiagnosticsMask,
                    timestampsToReturn,
                    itemToModify.RequestedParameters.ClientHandle,
                    filter,
                    filter,
                    null!,
                    itemToModify.RequestedParameters.SamplingInterval,
                    revisedQueueSize,
                    itemToModify.RequestedParameters.DiscardOldest);
            }
        }

        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        public void DeleteMonitoredItem(uint monitoredItemId)
        {
            lock (m_lock)
            {
                m_monitoredItems.Remove(monitoredItemId);
            }
        }

        /// <summary>
        /// Returns the currently active monitored items.
        /// </summary>
        public IList<IEventMonitoredItem> GetMonitoredItems()
        {
            lock (m_lock)
            {
                return [.. m_monitoredItems.Values];
            }
        }

        private readonly Lock m_lock = new();
        private readonly IServerInternal m_server;
        private readonly Dictionary<uint, IEventMonitoredItem> m_monitoredItems;
        private readonly uint m_maxEventQueueSize;
        private readonly uint m_maxDurableEventQueueSize;
    }
}
