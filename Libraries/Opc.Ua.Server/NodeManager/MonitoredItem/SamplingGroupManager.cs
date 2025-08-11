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
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages the sampling groups for a node manager.
    /// </summary>
    public class SamplingGroupManager : IDisposable
    {
        /// <summary>
        /// Creates a new instance of a sampling group.
        /// </summary>
        public SamplingGroupManager(
            IServerInternal server,
            INodeManager nodeManager,
            uint maxQueueSize,
            uint maxDurableQueueSize,
            IEnumerable<SamplingRateGroup> samplingRates
        )
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_nodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            m_samplingGroups = [];
            m_sampledItems = [];
            m_maxQueueSize = maxQueueSize;
            m_maxDurableQueueSize = maxDurableQueueSize;

            if (samplingRates != null)
            {
                m_samplingRates = [.. samplingRates];

                if (m_samplingRates.Count == 0)
                {
                    m_samplingRates = [.. s_defaultSamplingRates];
                }
            }

            m_samplingRates ??= [.. s_defaultSamplingRates];
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
                List<SamplingGroup> samplingGroups = null;
                List<ISampledDataChangeMonitoredItem> monitoredItems = null;

                lock (m_lock)
                {
                    samplingGroups = [.. m_samplingGroups];
                    m_samplingGroups.Clear();

                    monitoredItems = [.. m_sampledItems.Keys];
                    m_sampledItems.Clear();
                }

                foreach (SamplingGroup samplingGroup in samplingGroups)
                {
                    Utils.SilentDispose(samplingGroup);
                }

                foreach (ISampledDataChangeMonitoredItem monitoredItem in monitoredItems)
                {
                    Utils.SilentDispose(monitoredItem);
                }
            }
        }

        /// <summary>
        /// Stops all sampling groups and clears all items.
        /// </summary>
        public virtual void Shutdown()
        {
            lock (m_lock)
            {
                // stop sampling groups.
                foreach (SamplingGroup samplingGroup in m_samplingGroups)
                {
                    samplingGroup.Shutdown();
                    Utils.SilentDispose(samplingGroup);
                }

                m_samplingGroups.Clear();
                m_sampledItems.Clear();
            }
        }

        /// <summary>
        /// Creates a new monitored item and calls StartMonitoring().
        /// </summary>
        public virtual ISampledDataChangeMonitoredItem CreateMonitoredItem(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            uint monitoredItemId,
            object managerHandle,
            MonitoredItemCreateRequest itemToCreate,
            Range range,
            double minimumSamplingInterval,
            bool createDurable
        )
        {
            // use publishing interval as sampling interval.
            double samplingInterval = itemToCreate.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = publishingInterval;
            }

            // limit the sampling interval.
            if (minimumSamplingInterval > 0 && samplingInterval < minimumSamplingInterval)
            {
                samplingInterval = minimumSamplingInterval;
            }

            // calculate queue size.
            uint revisedQueueSize = SubscriptionManager.CalculateRevisedQueueSize(
                createDurable,
                itemToCreate.RequestedParameters.QueueSize,
                m_maxQueueSize,
                m_maxDurableQueueSize
            );

            // get filter.
            MonitoringFilter filter = null;

            if (!ExtensionObject.IsNull(itemToCreate.RequestedParameters.Filter))
            {
                filter = itemToCreate.RequestedParameters.Filter.Body as MonitoringFilter;
            }

            // update limits for event filters.
            if (filter is EventFilter)
            {
                if (revisedQueueSize == 0)
                {
                    revisedQueueSize = int.MaxValue;
                }

                samplingInterval = 0;
            }

            // check if the queue size was not specified.
            if (revisedQueueSize == 0)
            {
                revisedQueueSize = 1;
            }

            // create monitored item.
            ISampledDataChangeMonitoredItem monitoredItem = CreateMonitoredItem(
                m_server,
                m_nodeManager,
                managerHandle,
                subscriptionId,
                monitoredItemId,
                context.Session,
                itemToCreate.ItemToMonitor,
                context.DiagnosticsMask,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                filter,
                filter,
                range,
                samplingInterval,
                revisedQueueSize,
                itemToCreate.RequestedParameters.DiscardOldest,
                samplingInterval,
                createDurable
            );

            // start sampling.
            StartMonitoring(context, monitoredItem);

            // return item.
            return monitoredItem;
        }

        /// <summary>
        /// Creates a new monitored item.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="nodeManager">The node manager.</param>
        /// <param name="managerHandle">The manager handle.</param>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="id">The id.</param>
        /// <param name="session">The session.</param>
        /// <param name="itemToMonitor">The item to monitor.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        /// <param name="timestampsToReturn">The timestamps to return.</param>
        /// <param name="monitoringMode">The monitoring mode.</param>
        /// <param name="clientHandle">The client handle.</param>
        /// <param name="originalFilter">The original filter.</param>
        /// <param name="filterToUse">The filter to use.</param>
        /// <param name="range">The range.</param>
        /// <param name="samplingInterval">The sampling interval.</param>
        /// <param name="queueSize">Size of the queue.</param>
        /// <param name="discardOldest">if set to <c>true</c> [discard oldest].</param>
        /// <param name="minimumSamplingInterval">The minimum sampling interval.</param>
        /// <param name="createDurable">True if a durable monitored item should be created.</param>
        /// <returns>The monitored item.</returns>
        protected virtual ISampledDataChangeMonitoredItem CreateMonitoredItem(
            IServerInternal server,
            INodeManager nodeManager,
            object managerHandle,
            uint subscriptionId,
            uint id,
            ISession session,
            ReadValueId itemToMonitor,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            MonitoringFilter originalFilter,
            MonitoringFilter filterToUse,
            Range range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            double minimumSamplingInterval,
            bool createDurable
        )
        {
            return new MonitoredItem(
                server,
                nodeManager,
                managerHandle,
                subscriptionId,
                id,
                itemToMonitor,
                diagnosticsMasks,
                timestampsToReturn,
                monitoringMode,
                clientHandle,
                originalFilter,
                filterToUse,
                range,
                samplingInterval,
                queueSize,
                discardOldest,
                minimumSamplingInterval,
                createDurable
            );
        }

        /// <summary>
        /// Restores a monitored item after a server restart and calls StartMonitoring().
        /// </summary>
        public virtual ISampledDataChangeMonitoredItem RestoreMonitoredItem(
            object managerHandle,
            IStoredMonitoredItem storedMonitoredItem,
            IUserIdentity savedOwnerIdentity
        )
        {
            // create monitored item.
            ISampledDataChangeMonitoredItem monitoredItem = new MonitoredItem(
                m_server,
                m_nodeManager,
                managerHandle,
                storedMonitoredItem
            );

            // start sampling.
            StartMonitoring(new OperationContext(monitoredItem), monitoredItem, savedOwnerIdentity);

            // return item.
            return monitoredItem;
        }

        /// <summary>
        /// Modifies a monitored item and calls ModifyMonitoring().
        /// </summary>
        public virtual ServiceResult ModifyMonitoredItem(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify,
            Range range
        )
        {
            // use existing interval as sampling interval.
            double samplingInterval = itemToModify.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = monitoredItem.SamplingInterval;
            }

            // limit the sampling interval.
            double minimumSamplingInterval = monitoredItem.MinimumSamplingInterval;

            if (minimumSamplingInterval > 0 && samplingInterval < minimumSamplingInterval)
            {
                samplingInterval = minimumSamplingInterval;
            }

            // calculate queue size.
            uint revisedQueueSize = SubscriptionManager.CalculateRevisedQueueSize(
                monitoredItem.IsDurable,
                itemToModify.RequestedParameters.QueueSize,
                m_maxQueueSize,
                m_maxDurableQueueSize
            );

            if (revisedQueueSize == 0)
            {
                revisedQueueSize = monitoredItem.QueueSize;
            }

            // get filter.
            MonitoringFilter filter = null;

            if (!ExtensionObject.IsNull(itemToModify.RequestedParameters.Filter))
            {
                filter = (MonitoringFilter)itemToModify.RequestedParameters.Filter.Body;
            }

            // update limits for event filters.
            if (filter is EventFilter)
            {
                samplingInterval = 0;
            }

            // modify the item attributes.
            ServiceResult error = monitoredItem.ModifyAttributes(
                context.DiagnosticsMask,
                timestampsToReturn,
                itemToModify.RequestedParameters.ClientHandle,
                filter,
                filter,
                range,
                samplingInterval,
                revisedQueueSize,
                itemToModify.RequestedParameters.DiscardOldest
            );

            // state of item did not change if an error returned here.
            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // update sampling.
            ModifyMonitoring(context, monitoredItem);

            // everything is ok.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Starts monitoring the item.
        /// </summary>
        /// <remarks>
        /// It will use the external source for monitoring if the source accepts the item.
        /// The changes will not take affect until the ApplyChanges() method is called.
        /// </remarks>
        public virtual void StartMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            IUserIdentity savedOwnerIdentity = null
        )
        {
            lock (m_lock)
            {
                // do nothing for disabled or exception based items.
                if (
                    monitoredItem.MonitoringMode == MonitoringMode.Disabled
                    || monitoredItem.MinimumSamplingInterval == 0
                )
                {
                    m_sampledItems.Add(monitoredItem, null);
                    return;
                }

                // find a suitable sampling group.
                foreach (SamplingGroup samplingGroup in m_samplingGroups)
                {
                    if (samplingGroup.StartMonitoring(context, monitoredItem, savedOwnerIdentity))
                    {
                        m_sampledItems.Add(monitoredItem, samplingGroup);
                        return;
                    }
                }

                // create a new sampling group.
                var samplingGroup2 = new SamplingGroup(
                    m_server,
                    m_nodeManager,
                    m_samplingRates,
                    context,
                    monitoredItem.SamplingInterval,
                    savedOwnerIdentity
                );

                samplingGroup2.StartMonitoring(context, monitoredItem, savedOwnerIdentity);

                m_samplingGroups.Add(samplingGroup2);
                m_sampledItems.Add(monitoredItem, samplingGroup2);
            }
        }

        /// <summary>
        /// Changes monitoring attributes the item.
        /// </summary>
        /// <remarks>
        /// It will call the external source to change the monitoring if an external source was provided originally.
        /// The changes will not take affect until the ApplyChanges() method is called.
        /// </remarks>
        public virtual void ModifyMonitoring(OperationContext context, ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                // find existing sampling group.
                SamplingGroup samplingGroup = null;

                if (m_sampledItems.TryGetValue(monitoredItem, out samplingGroup))
                {
                    if (samplingGroup != null && samplingGroup.ModifyMonitoring(context, monitoredItem))
                    {
                        return;
                    }

                    m_sampledItems.Remove(monitoredItem);
                }

                // assign to a new sampling group.
                StartMonitoring(context, monitoredItem);
                return;
            }
        }

        /// <summary>
        /// Stops monitoring the item.
        /// </summary>
        /// <remarks>
        /// It will call the external source to stop the monitoring if an external source was provided originally.
        /// The changes will not take affect until the ApplyChanges() method is called.
        /// </remarks>
        public virtual void StopMonitoring(ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                // check for sampling group.
                SamplingGroup samplingGroup = null;

                if (m_sampledItems.TryGetValue(monitoredItem, out samplingGroup))
                {
                    samplingGroup?.StopMonitoring(monitoredItem);

                    m_sampledItems.Remove(monitoredItem);
                    return;
                }
            }
        }

        /// <summary>
        /// Applies any pending changes caused by adding,changing or removing monitored items.
        /// </summary>
        public virtual void ApplyChanges()
        {
            lock (m_lock)
            {
                var unusedGroups = new List<SamplingGroup>();

                // apply changes to groups.
                foreach (SamplingGroup samplingGroup in m_samplingGroups)
                {
                    if (samplingGroup.ApplyChanges())
                    {
                        unusedGroups.Add(samplingGroup);
                    }
                }

                // remove unused groups.
                foreach (SamplingGroup samplingGroup in unusedGroups)
                {
                    m_samplingGroups.Remove(samplingGroup);
                    Utils.SilentDispose(samplingGroup);
                }
            }
        }

        private readonly Lock m_lock = new();
        private readonly IServerInternal m_server;
        private readonly INodeManager m_nodeManager;
        private readonly List<SamplingGroup> m_samplingGroups;
        private readonly Dictionary<ISampledDataChangeMonitoredItem, SamplingGroup> m_sampledItems;
        private readonly List<SamplingRateGroup> m_samplingRates;
        private readonly uint m_maxQueueSize;
        private readonly uint m_maxDurableQueueSize;

        /// <summary>
        /// The default sampling rates.
        /// </summary>
        private static readonly SamplingRateGroup[] s_defaultSamplingRates =
        [
            new SamplingRateGroup(100, 100, 4),
            new SamplingRateGroup(500, 250, 2),
            new SamplingRateGroup(1000, 1000, 4),
            new SamplingRateGroup(5000, 2500, 2),
            new SamplingRateGroup(10000, 10000, 4),
            new SamplingRateGroup(60000, 30000, 10),
            new SamplingRateGroup(300000, 60000, 15),
            new SamplingRateGroup(900000, 300000, 9),
            new SamplingRateGroup(3600000, 900000, 0),
        ];
    }
}
