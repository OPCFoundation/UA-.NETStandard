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
using System.Globalization;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{    
    /// <summary>
    /// An object which periodically reads the items and updates the cache.
    /// </summary>
    public class SamplingGroup : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of a sampling group.
        /// </summary>
        public SamplingGroup(
            IServerInternal         server,
            INodeManager            nodeManager,
            List<SamplingRateGroup> samplingRates,
            OperationContext        context,
            double                  samplingInterval)
        {
            if (server == null)        throw new ArgumentNullException(nameof(server));
            if (nodeManager == null)   throw new ArgumentNullException(nameof(nodeManager));
            if (samplingRates == null) throw new ArgumentNullException(nameof(samplingRates));

            m_server           = server;
            m_nodeManager      = nodeManager;
            m_samplingRates    = samplingRates;
            m_session          = context.Session;
            m_diagnosticsMask  = (DiagnosticsMasks)context.DiagnosticsMask & DiagnosticsMasks.OperationAll;
            m_samplingInterval = AdjustSamplingInterval(samplingInterval);

            m_itemsToAdd    = new List<ISampledDataChangeMonitoredItem>();
            m_itemsToRemove = new List<ISampledDataChangeMonitoredItem>();
            m_items         = new Dictionary<uint, ISampledDataChangeMonitoredItem>();

            // create a event to signal shutdown.
            m_shutdownEvent = new ManualResetEvent(true);
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
                lock (m_lock)
                {
                    m_shutdownEvent.Set();
                    m_samplingRates.Clear();
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts the sampling thread which periodically reads the items in the group.
        /// </summary>
        public void Startup()
        {
            lock (m_lock)
            {
                m_shutdownEvent.Reset();

                Task.Run(() =>
                {
                    SampleMonitoredItems(m_samplingInterval);
                });
            }
        }

        /// <summary>
        /// Stops the sampling thread.
        /// </summary>
        public void Shutdown()
        {
            lock (m_lock)
            {
                m_shutdownEvent.Set();
                m_items.Clear();
            }
        }

        /// <summary>
        /// Checks if the monitored item can be handled by the group.
        /// </summary>
        /// <returns>
        /// True if the item was added to the group.
        /// </returns>
        /// <remarks>
        /// The ApplyChanges() method must be called to actually start sampling the item. 
        /// </remarks>
        public bool StartMonitoring(OperationContext context, ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                if (MeetsGroupCriteria(context, monitoredItem))
                {
                    m_itemsToAdd.Add(monitoredItem);
                    monitoredItem.SetSamplingInterval(m_samplingInterval);
                    return true;
                }

                return false;
            }
        }
        
        /// <summary>
        /// Checks if the monitored item can still be handled by the group.
        /// </summary>
        /// <returns>
        /// False if the item has be marked for removal from the group.
        /// </returns>
        /// <remarks>
        /// The ApplyChanges() method must be called to actually stop sampling the item. 
        /// </remarks>
        public bool ModifyMonitoring(OperationContext context, ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                if (m_items.ContainsKey(monitoredItem.Id))
                {
                    if (MeetsGroupCriteria(context, monitoredItem))
                    {
                        monitoredItem.SetSamplingInterval(m_samplingInterval);
                        return true;
                    }
                        
                    m_itemsToRemove.Add(monitoredItem);
                }

                return false;
            }
        }

        /// <summary>
        /// Stops monitoring the item.
        /// </summary>
        /// <returns>
        /// Returns true if the items was marked for removal from the group.
        /// </returns>
        public bool StopMonitoring(ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                if (m_items.ContainsKey(monitoredItem.Id))
                {
                    m_itemsToRemove.Add(monitoredItem);
                    return true;
                }
                    
                return false;
            }
        }

        /// <summary>
        /// Updates the group by apply any pending changes.
        /// </summary>
        /// <returns>
        /// Returns true if the group has no more items and can be dropped.
        /// </returns>
        public bool ApplyChanges()
        {
            lock (m_lock)
            {
                // add items.
                List<ISampledDataChangeMonitoredItem> itemsToSample = new List<ISampledDataChangeMonitoredItem>();

                for (int ii = 0; ii < m_itemsToAdd.Count; ii++)
                {
                    ISampledDataChangeMonitoredItem monitoredItem = m_itemsToAdd[ii];

                    if (!m_items.ContainsKey(monitoredItem.Id))
                    {
                        m_items.Add(monitoredItem.Id, monitoredItem);
                        
                        if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                        {
                            itemsToSample.Add(monitoredItem);
                        }
                    }
                }

                m_itemsToAdd.Clear();

                // collect first sample.
                if (itemsToSample.Count > 0)
                {
                    Task.Run(() =>
                    {
                        DoSample(itemsToSample);
                    });
                }
                                
                // remove items.
                for (int ii = 0; ii < m_itemsToRemove.Count; ii++)
                {
                    m_items.Remove(m_itemsToRemove[ii].Id);
                }

                m_itemsToRemove.Clear();

                // start the group if it is not running.
                if (m_items.Count > 0)
                {
                    Startup();
                }
                    
                // stop the group if it is running.
                else if (m_items.Count == 0)
                {
                    Shutdown();
                }
                
                // can be shutdown if no items left.
                return m_items.Count == 0;
            }   
        }
        #endregion
            
        #region Private Methods
        /// <summary>
        /// Checks if the item meets the group's criteria.
        /// </summary>
        private bool MeetsGroupCriteria(OperationContext context, ISampledDataChangeMonitoredItem monitoredItem)
        {
            // can only sample variables.
            if ((monitoredItem.MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
            {
                return false;
            }

            // can't sample disabled items.
            if (monitoredItem.MonitoringMode == MonitoringMode.Disabled)
            {
                return false;
            }

            // check sampling interval.
            if (AdjustSamplingInterval(monitoredItem.SamplingInterval) != m_samplingInterval)
            {
                return false;
            }         
            
            // compare session.
            if (context.SessionId != m_session.Id)
            {
                return false;
            }

            // check the diagnostics marks.
            if (m_diagnosticsMask != (context.DiagnosticsMask & DiagnosticsMasks.OperationAll))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ensures the requested sampling interval lines up with one of the supported sampling rates.
        /// </summary>
        private double AdjustSamplingInterval(double samplingInterval)
        {            
            foreach (SamplingRateGroup samplingRate in m_samplingRates)
            {
                // groups are ordered by start rate.
                if (samplingInterval <= samplingRate.Start)
                {
                    return samplingRate.Start;
                }
                
                // check if within range specfied by the group.
                double maxSamplingRate = samplingRate.Start;
                
                if (samplingRate.Increment > 0)
                {
                    maxSamplingRate += samplingRate.Increment*samplingRate.Count;
                }
                
                if (samplingInterval > maxSamplingRate)
                {
                    continue;
                }
                
                // find sampling rate within rate group.
                if (samplingInterval == maxSamplingRate)
                {
                    return maxSamplingRate;
                }

                for (double ii = samplingRate.Start; ii <= maxSamplingRate; ii += samplingRate.Increment)
                {
                    if (ii >= samplingInterval)
                    {
                        return ii;
                    }
                }
            }

            return samplingInterval;
        }

        /// <summary>
        /// Periodically checks if the sessions have timed out.
        /// </summary>
        private void SampleMonitoredItems(object data)
        {
            try
            {
                //Utils.Trace("Server: {0} Thread Started.", Thread.CurrentThread.Name);

                int sleepCycle = Convert.ToInt32(data, CultureInfo.InvariantCulture);
                int timeToWait = sleepCycle;

                while (m_server.IsRunning)
                {
                    DateTime start = DateTime.UtcNow;

                    // wait till next sample.
                    if (m_shutdownEvent.WaitOne(timeToWait))
                    {
                        break;
                    }

                    // get current list of items to sample.
                    List<ISampledDataChangeMonitoredItem> items = new List<ISampledDataChangeMonitoredItem>();

                    lock (m_lock)
                    {
                        uint disabledItemCount = 0;
                        Dictionary<uint,ISampledDataChangeMonitoredItem>.Enumerator enumerator = m_items.GetEnumerator();

                        while (enumerator.MoveNext())
                        {
                            ISampledDataChangeMonitoredItem monitoredItem = enumerator.Current.Value;
                            
                            if (monitoredItem.MonitoringMode == MonitoringMode.Disabled)
                            {
                                disabledItemCount++;
                                continue;
                            }

                            // check whether the item should be sampled.
                            //if (!monitoredItem.SamplingIntervalExpired())
                            //{
                            //    continue;
                            //}

                            items.Add(monitoredItem);   
                        }
                    }

                    // sample the values.
                    DoSample(items);

                    int delay = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                    timeToWait = sleepCycle;

                    if (delay > sleepCycle)
                    {
                        timeToWait = 2*sleepCycle - delay;

                        if (timeToWait < 0)
                        {
                            Utils.Trace("WARNING: SamplingGroup cannot sample fast enough. TimeToSample={0}ms, SamplingInterval={1}ms", delay, sleepCycle);
                            timeToWait = sleepCycle;
                        }
                    }
                }
                
                //Utils.Trace("Server: {0} Thread Exited Normally.", Thread.CurrentThread.Name);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Server: SampleMonitoredItems Thread Exited Unexpectedly.");
            }
        }

        /// <summary>
        /// Samples the values of the items.
        /// </summary>
        private void DoSample(object state)
        {  
            try
            {
                List<ISampledDataChangeMonitoredItem> items = state as List<ISampledDataChangeMonitoredItem>;

                // read values for all enabled items.
                if (items != null && items.Count > 0)
                {
                    ReadValueIdCollection itemsToRead = new ReadValueIdCollection(items.Count);
                    DataValueCollection values = new DataValueCollection(items.Count);
                    List<ServiceResult> errors = new List<ServiceResult>(items.Count);

                    // allocate space for results.
                    for (int ii = 0; ii < items.Count; ii++)
                    {
                        ReadValueId readValueId = items[ii].GetReadValueId();
                        readValueId.Processed = false;
                        itemsToRead.Add(readValueId);

                        values.Add(null);
                        errors.Add(null);
                    }

                    OperationContext context = new OperationContext(m_session, m_diagnosticsMask);

                    // read values.
                    m_nodeManager.Read(
                        context,
                        0,
                        itemsToRead,
                        values,
                        errors);

                    // update monitored items.
                    for (int ii = 0; ii < items.Count; ii++)
                    {
                        if (values[ii] == null)
                        {
                            values[ii] = new DataValue(StatusCodes.BadInternalError, DateTime.UtcNow);
                        }

                        items[ii].QueueValue(values[ii], errors[ii]);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Server: Unexpected error sampling values.");
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private IServerInternal m_server;
        private INodeManager m_nodeManager;
        private Session m_session;
        private DiagnosticsMasks m_diagnosticsMask;
        private double m_samplingInterval;
        private List<ISampledDataChangeMonitoredItem> m_itemsToAdd;
        private List<ISampledDataChangeMonitoredItem> m_itemsToRemove;
        private Dictionary<uint, ISampledDataChangeMonitoredItem> m_items;
        private ManualResetEvent m_shutdownEvent;
        private List<SamplingRateGroup> m_samplingRates;
        #endregion
    }
}
