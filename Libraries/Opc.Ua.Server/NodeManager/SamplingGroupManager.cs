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
using System.Text;
using System.Threading;
using System.Security.Principal;

namespace Opc.Ua.Server
{    
    /// <summary>
    /// An object that manages the sampling groups for a node manager.
    /// </summary>
    public class SamplingGroupManager : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of a sampling group.
        /// </summary>
        public SamplingGroupManager(
            IServerInternal                server,
            INodeManager                   nodeManager,
            uint                           maxQueueSize,
            IEnumerable<SamplingRateGroup> samplingRates)
        {
            if (server == null)      throw new ArgumentNullException(nameof(server));
            if (nodeManager == null) throw new ArgumentNullException(nameof(nodeManager));

            m_server          = server;
            m_nodeManager     = nodeManager;
            m_samplingGroups  = new List<SamplingGroup>();
            m_sampledItems    = new Dictionary<ISampledDataChangeMonitoredItem,SamplingGroup>();
            m_maxQueueSize    = maxQueueSize;

            if (samplingRates != null)
            {
                m_samplingRates = new List<SamplingRateGroup>(samplingRates);
            
                if (m_samplingRates.Count == 0)
                {
                    m_samplingRates = new List<SamplingRateGroup>(s_DefaultSamplingRates);
                }
            }

            if (m_samplingRates == null)
            {
                m_samplingRates = new List<SamplingRateGroup>(s_DefaultSamplingRates);
            }
        }
        #endregion
                 
        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
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
                    samplingGroups = new List<SamplingGroup>(m_samplingGroups);
                    m_samplingGroups.Clear();

                    monitoredItems = new List<ISampledDataChangeMonitoredItem>(m_sampledItems.Keys);
                    m_sampledItems.Clear();
                }
                
                foreach (SamplingGroup samplingGroup in samplingGroups)
                {
                    Utils.SilentDispose(samplingGroup);
                }

                foreach (MonitoredItem monitoredItem in monitoredItems)
                {
                    Utils.SilentDispose(monitoredItem);
                }
            }
        }
        #endregion
           
        #region Public Methods
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
                }

                m_samplingGroups.Clear();
                m_sampledItems.Clear();
            }
        }

        /// <summary>
        /// Creates a new monitored item and calls StartMonitoring().
        /// </summary>
        public virtual MonitoredItem CreateMonitoredItem(
            OperationContext           context,
            uint                       subscriptionId,
            double                     publishingInterval,
            TimestampsToReturn         timestampsToReturn,
            uint                       monitoredItemId,
            object                     managerHandle,
            MonitoredItemCreateRequest itemToCreate,
            Range                      range,
            double                     minimumSamplingInterval)
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
            uint queueSize = itemToCreate.RequestedParameters.QueueSize;
            
            if (queueSize > m_maxQueueSize)
            {
                queueSize = m_maxQueueSize;
            }
            
            // get filter.
            MonitoringFilter filter = null;

            if (!ExtensionObject.IsNull(itemToCreate.RequestedParameters.Filter))
            {
                filter = itemToCreate.RequestedParameters.Filter.Body as MonitoringFilter;               
            }
            
            // update limits for event filters.
            if (filter is EventFilter)
            {
                if (queueSize == 0)
                {
                    queueSize = Int32.MaxValue;
                }

                samplingInterval = 0;
            }

            // check if the queue size was not specified.
            if (queueSize == 0)
            {
                queueSize = 1;
            }
            
            // create monitored item.
            MonitoredItem monitoredItem = CreateMonitoredItem(
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
                queueSize,
                itemToCreate.RequestedParameters.DiscardOldest,
                samplingInterval);
            
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
        /// <returns>The monitored item.</returns>
        protected virtual MonitoredItem CreateMonitoredItem(
            IServerInternal     server,
            INodeManager        nodeManager,
            object              managerHandle,
            uint                subscriptionId,
            uint                id,
            Session             session,
            ReadValueId         itemToMonitor,
            DiagnosticsMasks    diagnosticsMasks,
            TimestampsToReturn  timestampsToReturn,
            MonitoringMode      monitoringMode,
            uint                clientHandle,
            MonitoringFilter    originalFilter,
            MonitoringFilter    filterToUse,
            Range               range,
            double              samplingInterval,
            uint                queueSize,
            bool                discardOldest,
            double              minimumSamplingInterval)
        {
            return new MonitoredItem(
                server,
                nodeManager,
                managerHandle,
                subscriptionId,
                id,
                session,
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
                minimumSamplingInterval);
        }
        
        /// <summary>
        /// Modifies a monitored item and calls ModifyMonitoring().
        /// </summary>
        public virtual ServiceResult ModifyMonitoredItem(
            OperationContext           context,
            TimestampsToReturn         timestampsToReturn,
            ISampledDataChangeMonitoredItem   monitoredItem,
            MonitoredItemModifyRequest itemToModify,
            Range                      range)
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
            uint queueSize = itemToModify.RequestedParameters.QueueSize;

            if (queueSize == 0)
            {
                queueSize = monitoredItem.QueueSize;
            }
        
            if (queueSize > m_maxQueueSize)
            {
                queueSize = m_maxQueueSize;
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
                queueSize,
                itemToModify.RequestedParameters.DiscardOldest);

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
        public virtual void StartMonitoring(OperationContext context, ISampledDataChangeMonitoredItem monitoredItem)
        {             
            lock (m_lock)
            {
                // do nothing for disabled or exception based items.
                if (monitoredItem.MonitoringMode == MonitoringMode.Disabled || monitoredItem.MinimumSamplingInterval == 0)
                {
                    m_sampledItems.Add(monitoredItem, null);
                    return;
                }
                                
                // find a suitable sampling group.
                foreach (SamplingGroup samplingGroup in m_samplingGroups)
                {
                    if (samplingGroup.StartMonitoring(context, monitoredItem))
                    {
                        m_sampledItems.Add(monitoredItem, samplingGroup);
                        return;
                    }
                }
                
                // create a new sampling group.
                SamplingGroup samplingGroup2 = new SamplingGroup(
                    m_server,
                    m_nodeManager,
                    m_samplingRates,
                    context,
                    monitoredItem.SamplingInterval);

                samplingGroup2.StartMonitoring(context, monitoredItem);

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
                    if (samplingGroup != null)
                    {
                        if (samplingGroup.ModifyMonitoring(context, monitoredItem))
                        {
                            return;
                        }
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
                    if (samplingGroup != null)
                    {
                        samplingGroup.StopMonitoring(monitoredItem);
                    }

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
                List<SamplingGroup> unusedGroups = new List<SamplingGroup>();

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
                    samplingGroup.Shutdown();
                    m_samplingGroups.Remove(samplingGroup);
                }
            }   
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private IServerInternal m_server;
        private INodeManager m_nodeManager;
        private List<SamplingGroup> m_samplingGroups;
        private Dictionary<ISampledDataChangeMonitoredItem,SamplingGroup> m_sampledItems;
        private List<SamplingRateGroup> m_samplingRates;
        private uint m_maxQueueSize;

        /// <summary>
        /// The default sampling rates.
        /// </summary>
        private static readonly SamplingRateGroup[] s_DefaultSamplingRates = new SamplingRateGroup[]
        {
            new SamplingRateGroup(100, 100, 4),
            new SamplingRateGroup(500, 250, 2),
            new SamplingRateGroup(1000, 1000, 4),
            new SamplingRateGroup(5000, 2500, 2),
            new SamplingRateGroup(10000, 10000, 4),
            new SamplingRateGroup(60000, 30000, 10),
            new SamplingRateGroup(300000, 60000, 15),
            new SamplingRateGroup(900000, 300000, 9),
            new SamplingRateGroup(3600000, 900000, 0)
        };

        #endregion
    }
}
