/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Server;

namespace MemoryBuffer
{
    /// <summary>
    /// Provides a basic monitored item implementation which does not support queuing.
    /// </summary>
    public class MemoryBufferMonitoredItem : MonitoredItem
    {
		/// <summary>
		/// Initializes the object with its node type.
		/// </summary>
        public MemoryBufferMonitoredItem(
            IServerInternal     server,
            INodeManager        nodeManager,
            object              mangerHandle,
            uint                offset,
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
        :
            base(
                server,
                nodeManager,
                mangerHandle,
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
                minimumSamplingInterval)
		{
            m_offset = offset;
        }

        /// <summary>
        /// Modifies the monitored item parameters,
        /// </summary>
        public ServiceResult Modify(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            double samplingInterval)
        {
            return base.ModifyAttributes(diagnosticsMasks,
                timestampsToReturn,
                clientHandle,
                null,
                null,
                null,
                samplingInterval,
                0,
                false);
        }

        /// <summary>
        /// The offset in the memory buffer.
        /// </summary>
        public uint Offset
        {
            get
            {
                return m_offset;
            }
        }
        
        private uint m_offset;

        /*
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public MemoryBufferMonitoredItem(
            MemoryBufferState source,
            uint id,
            uint offset,
            uint attributeId,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            double samplingInterval)
        {
            m_source = source;
            m_id = id;
            m_offset = offset;
            m_attributeId = attributeId;
            m_timestampsToReturn = timestampsToReturn;
            m_diagnosticsMasks = diagnosticsMasks;
            m_monitoringMode = monitoringMode;
            m_clientHandle = clientHandle;
            m_samplingInterval = samplingInterval;
            m_nextSamplingTime = DateTime.UtcNow.Ticks;
            m_notificationsAvailable = false;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The monitoring mode.
        /// </summary>
        public MonitoringMode MonitoringMode
        {
            get
            {
                return m_monitoringMode;
            }
        }
        
        /// <summary>
        /// The offset in the memory buffer.
        /// </summary>
        public uint Offset
        {
            get
            {
                return m_offset;
            }
        }

        /// <summary>
        /// The attribute id.
        /// </summary>
        public uint AttributeId
        {
            get
            {
                return m_attributeId;
            }
        }

        /// <summary>
        /// The sampling interval.
        /// </summary>
        public double SamplingInterval
        {
            get
            {
                lock (m_lock)
                {
                    return m_samplingInterval;
                }
            }
        }

        /// <summary>
        /// Modifies the monitored item parameters,
        /// </summary>
        public ServiceResult Modify(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            double samplingInterval)
        {
            lock (m_lock)
            {
                m_diagnosticsMasks = diagnosticsMasks;
                m_timestampsToReturn = timestampsToReturn;
                m_clientHandle = clientHandle;
                m_samplingInterval = samplingInterval;

                return ServiceResult.Good;
            }
        }
        #endregion

        #region IMonitoredItem Members
        /// <summary>
        /// The node manager for the monitored item.
        /// </summary>
        public INodeManager NodeManager
        {
            get { return m_source.NodeManager; }
        }

        /// <summary>
        /// The session for the monitored item.
        /// </summary>
        public Session Session
        {
            get
            {
                ISubscription subscription = m_subscription;

                if (subscription != null)
                {
                    return subscription.Session;
                }

                return null;
            }
        }

        /// <summary>
        /// The identifier for the subscription that the monitored item belongs to.
        /// </summary>
        public uint SubscriptionId
        {
            get
            {
                ISubscription subscription = m_subscription;

                if (subscription != null)
                {
                    return subscription.Id;
                }

                return 0;
            }
        }

        /// <summary>
        /// The unique identifier for the monitored item.
        /// </summary>
        public uint Id
        {
            get { return m_id; }
        }

        /// <summary>
        /// The callback to use to notify the subscription when values are ready to publish.
        /// </summary>
        public ISubscription SubscriptionCallback
        {
            get
            {
                return m_subscription;
            }

            set
            {
                lock (m_lock)
                {
                    m_subscription = value;

                    if (m_notificationsAvailable)
                    {
                        NotifySubscription();
                    }
                }
            }
        }

        /// <summary>
        /// The handle assigned to the monitored item by the node manager.
        /// </summary>
        public object ManagerHandle
        {
            get { return m_source; }
        }

        /// <summary>
        /// The type of monitor item.
        /// </summary>
        public int MonitoredItemType
        {
            get { return MonitoredItemTypeMask.DataChange; }
        }

        /// <summary>
        /// Returns true if the item is ready to publish.
        /// </summary>
        public bool IsReadyToPublish
        {
            get
            {
                lock (m_lock)
                {
                    // check if not ready to publish.
                    if (!m_notificationsAvailable)
                    {
                        return false;
                    }

                    // check if monitoring was turned off.
                    if (m_monitoringMode != MonitoringMode.Reporting)
                    {
                        return false;
                    }

                    // re-queue if too little time has passed since the last publish.
                    long now = DateTime.UtcNow.Ticks;

                    if (m_nextSamplingTime > now)
                    {
                        return false;
                    }

                    return true;
                }
            }
        }

        /// <summary>
        /// Returns the results for the create request.
        /// </summary>
        public ServiceResult GetCreateResult(out MonitoredItemCreateResult result)
        {
            lock (m_lock)
            {
                result = new MonitoredItemCreateResult();

                result.MonitoredItemId = m_id;
                result.StatusCode = StatusCodes.Good;
                result.RevisedSamplingInterval = m_samplingInterval;
                result.RevisedQueueSize = 0;
                result.FilterResult = null;

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Returns the results for the modify request.
        /// </summary>
        public ServiceResult GetModifyResult(out MonitoredItemModifyResult result)
        {
            lock (m_lock)
            {
                result = new MonitoredItemModifyResult();

                result.StatusCode = StatusCodes.Good;
                result.RevisedSamplingInterval = m_samplingInterval;
                result.RevisedQueueSize = 0;
                result.FilterResult = null;

                return ServiceResult.Good;
            }
        }
        #endregion

        #region IDataChangeMonitoredItem Members
        /// <summary>
        /// Queues a new data change.
        /// </summary>
        public void QueueValue(DataValue value, ServiceResult error)
        {
            lock (m_lock)
            {
                // ensure the error is not null.
                if (error == null)
                {
                    error = ServiceResult.Good;
                }

                // ensure the value is not null.
                if (value == null)
                {
                    value = new DataValue(error.StatusCode);
                }

                // it is up to the called to ensure only changed values are queued.
                m_lastValue = value;
                m_lastError = error;

                // ensure value is consistent with error state.
                if (ServiceResult.IsBad(m_lastError))
                {
                    m_lastValue.Value = null;
                    m_lastValue.StatusCode = error.StatusCode;
                }

                // notify the subscription if not already notified during a previous data change.
                if (!m_notificationsAvailable)
                {
                    m_notificationsAvailable = true;
                    NotifySubscription();
                }
            }
        }

        /// <summary>
        /// Notifies the subscription if the monitoring mode is reporting.
        /// </summary>
        private void NotifySubscription()
        {
            if (m_subscription != null)
            {
                if (IsReadyToPublish)
                {
                    m_subscription.ItemReadyToPublish(this);
                }
                else
                {
                    m_subscription.ItemNotificationsAvailable(this);
                }
            }
        }

        /// <summary>
        /// Changes the monitoring mode.
        /// </summary>
        public MonitoringMode SetMonitoringMode(MonitoringMode monitoringMode)
        {
            lock (m_lock)
            {
                MonitoringMode previousMode = m_monitoringMode;

                if (previousMode == monitoringMode)
                {
                    return previousMode;
                }

                if (previousMode == MonitoringMode.Disabled)
                {
                    m_nextSamplingTime = DateTime.UtcNow.Ticks;
                }

                m_monitoringMode = monitoringMode;

                // notify the subscription if changed to reporting and data is ready to publish.
                if (m_monitoringMode == MonitoringMode.Reporting)
                {
                    if (m_notificationsAvailable)
                    {
                        NotifySubscription();
                    }
                }

                return previousMode;
            }
        }

        /// <summary>
        /// No filters supported.
        /// </summary>
        public DataChangeFilter DataChangeFilter
        {
            get { return null; }
        }

        /// <summary>
        /// Called by the subscription to publish any notification.
        /// </summary>
        public bool Publish(OperationContext context, Queue<MonitoredItemNotification> notifications, Queue<DiagnosticInfo> diagnostics)
        {
            lock (m_lock)
            {
                // check if not ready to publish.
                if (!IsReadyToPublish)
                {
                    return false;
                }

                long now = DateTime.UtcNow.Ticks;

                if (m_samplingInterval > 0)
                {
                    while (m_nextSamplingTime <= now)
                    {
                        m_nextSamplingTime += (long)(m_samplingInterval * TimeSpan.TicksPerMillisecond);
                    }
                }

                m_notificationsAvailable = false;

                // copy data value.
                MonitoredItemNotification item = new MonitoredItemNotification();

                item.ClientHandle = m_clientHandle;
                item.Value = m_lastValue;

                // apply timestamp filter.
                if (m_timestampsToReturn != TimestampsToReturn.Server && m_timestampsToReturn != TimestampsToReturn.Both)
                {
                    item.Value.ServerTimestamp = DateTime.MinValue;
                }

                if (m_timestampsToReturn != TimestampsToReturn.Source && m_timestampsToReturn != TimestampsToReturn.Both)
                {
                    item.Value.SourceTimestamp = DateTime.MinValue;
                }

                notifications.Enqueue(item);

                // update diagnostic info.
                DiagnosticInfo diagnosticInfo = null;

                if (m_lastError != null)
                {
                    if ((m_diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_source.Server, context, m_lastError);
                    }
                }

                diagnostics.Enqueue(diagnosticInfo);

                return true;
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private MemoryBufferState m_source;
        private ISubscription m_subscription;
        private uint m_id;
        private uint m_offset;
        private uint m_attributeId;
        private DataValue m_lastValue;
        private ServiceResult m_lastError;
        private TimestampsToReturn m_timestampsToReturn;
        private DiagnosticsMasks m_diagnosticsMasks;
        private uint m_clientHandle;
        private double m_samplingInterval;
        private MonitoringMode m_monitoringMode;
        private long m_nextSamplingTime;
        private bool m_notificationsAvailable;
        #endregion
         * */
    }
}
