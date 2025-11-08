/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Sample
{
    /// <summary>
    /// Provides a basic monitored item implementation which does not support queuing.
    /// </summary>
    public class DataChangeMonitoredItem : IDataChangeMonitoredItem2
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public DataChangeMonitoredItem(
            MonitoredNode source,
            uint id,
            uint attributeId,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            double samplingInterval,
            bool alwaysReportUpdates)
        {
            m_source = source;
            Id = id;
            AttributeId = attributeId;
            m_indexRange = indexRange;
            DataEncoding = dataEncoding;
            m_timestampsToReturn = timestampsToReturn;
            m_diagnosticsMasks = diagnosticsMasks;
            MonitoringMode = monitoringMode;
            ClientHandle = clientHandle;
            m_samplingInterval = samplingInterval;
            m_nextSampleTime = DateTime.UtcNow.Ticks;
            m_readyToPublish = false;
            m_readyToTrigger = false;
            m_resendData = false;
            AlwaysReportUpdates = alwaysReportUpdates;
            NodeId = source.Node.NodeId;
        }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public DataChangeMonitoredItem(
            IMonitoredItemQueueFactory monitoredItemQueueFactory,
            MonitoredNode source,
            uint id,
            uint attributeId,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            DataChangeFilter filter,
            Range range,
            bool alwaysReportUpdates)
        {
            m_source = source;
            m_monitoredItemQueueFactory = monitoredItemQueueFactory;
            Id = id;
            AttributeId = attributeId;
            m_indexRange = indexRange;
            DataEncoding = dataEncoding;
            m_timestampsToReturn = timestampsToReturn;
            m_diagnosticsMasks = diagnosticsMasks;
            MonitoringMode = monitoringMode;
            ClientHandle = clientHandle;
            m_samplingInterval = samplingInterval;
            m_nextSampleTime = DateTime.UtcNow.Ticks;
            m_readyToPublish = false;
            m_readyToTrigger = false;
            m_resendData = false;
            m_queue = null;
            m_queueSize = queueSize;
            DataChangeFilter = filter;
            m_range = 0;
            AlwaysReportUpdates = alwaysReportUpdates;
            NodeId = source.Node.NodeId;

            if (range != null)
            {
                m_range = range.High - range.Low;
            }

            if (queueSize > 1)
            {
                m_queue = new DataChangeQueueHandler(
                    id,
                    false,
                    m_monitoredItemQueueFactory,
                    source.Server.Telemetry);
                m_queue.SetQueueSize(queueSize, discardOldest, diagnosticsMasks);
                m_queue.SetSamplingInterval(samplingInterval);
            }
        }

        /// <summary>
        /// Constructs a new instance from a template.
        /// </summary>
        public DataChangeMonitoredItem(
            ISubscriptionStore subscriptionStore,
            IMonitoredItemQueueFactory monitoredItemQueueFactory,
            MonitoredNode source,
            IStoredMonitoredItem storedMonitoredItem)
        {
            m_source = source;
            m_monitoredItemQueueFactory = monitoredItemQueueFactory;
            Id = storedMonitoredItem.Id;
            AttributeId = storedMonitoredItem.AttributeId;
            m_indexRange = storedMonitoredItem.ParsedIndexRange;
            DataEncoding = storedMonitoredItem.Encoding;
            m_timestampsToReturn = storedMonitoredItem.TimestampsToReturn;
            m_diagnosticsMasks = storedMonitoredItem.DiagnosticsMasks;
            MonitoringMode = storedMonitoredItem.MonitoringMode;
            ClientHandle = storedMonitoredItem.ClientHandle;
            m_samplingInterval = storedMonitoredItem.SamplingInterval;
            m_nextSampleTime = DateTime.UtcNow.Ticks;
            m_readyToPublish = false;
            m_readyToTrigger = false;
            m_resendData = false;
            m_queue = null;
            m_queueSize = storedMonitoredItem.QueueSize;
            DataChangeFilter = storedMonitoredItem.FilterToUse as DataChangeFilter;
            m_range = storedMonitoredItem.Range;
            AlwaysReportUpdates = storedMonitoredItem.AlwaysReportUpdates;
            m_lastValue = storedMonitoredItem.LastValue;
            m_lastError = storedMonitoredItem.LastError;
            NodeId = storedMonitoredItem.NodeId;

            if (storedMonitoredItem.QueueSize > 1)
            {
                IDataChangeMonitoredItemQueue queue = subscriptionStore
                    .RestoreDataChangeMonitoredItemQueue(
                        storedMonitoredItem.Id);

                if (queue != null)
                {
                    m_queue = new DataChangeQueueHandler(
                        queue,
                        storedMonitoredItem.DiscardOldest,
                        storedMonitoredItem.SamplingInterval,
                        source.Server.Telemetry);
                }
                else
                {
                    m_queue = new DataChangeQueueHandler(
                        storedMonitoredItem.Id,
                        false,
                        m_monitoredItemQueueFactory,
                        source.Server.Telemetry);
                    m_queue.SetQueueSize(
                        storedMonitoredItem.QueueSize,
                        storedMonitoredItem.DiscardOldest,
                        storedMonitoredItem.DiagnosticsMasks);
                    m_queue.SetSamplingInterval(storedMonitoredItem.SamplingInterval);
                }
            }
        }

        /// <summary>
        /// Gets the id for the attribute being monitored.
        /// </summary>
        public uint AttributeId { get; }

        /// <summary>
        /// Gets the index range used to selected a subset of the value.
        /// </summary>
        public NumericRange IndexRange => m_indexRange;

        /// <summary>
        /// Gets the data encoding to use when returning the value.
        /// </summary>
        public QualifiedName DataEncoding { get; }

        /// <summary>
        /// Whether the monitored item should report a value without checking if it was changed.
        /// </summary>
        public bool AlwaysReportUpdates { get; set; }

        /// <summary>
        /// The number of milliseconds until the next sample.
        /// </summary>
        public int TimeToNextSample
        {
            get
            {
                lock (m_lock)
                {
                    if (MonitoringMode == MonitoringMode.Disabled)
                    {
                        return int.MaxValue;
                    }

                    DateTime now = DateTime.UtcNow;

                    if (m_nextSampleTime <= now.Ticks)
                    {
                        return 0;
                    }

                    return (int)((m_nextSampleTime - now.Ticks) / TimeSpan.TicksPerMillisecond);
                }
            }
        }

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        public MonitoringMode MonitoringMode { get; private set; }

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
            return Modify(
                diagnosticsMasks,
                timestampsToReturn,
                clientHandle,
                samplingInterval,
                0,
                false,
                null,
                null);
        }

        /// <summary>
        /// Modifies the monitored item parameters,
        /// </summary>
        public ServiceResult Modify(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            DataChangeFilter filter,
            Range range)
        {
            lock (m_lock)
            {
                m_diagnosticsMasks = diagnosticsMasks;
                m_timestampsToReturn = timestampsToReturn;
                ClientHandle = clientHandle;
                m_queueSize = queueSize;

                // subtract the previous sampling interval.
                long oldSamplingInterval = (long)(m_samplingInterval *
                    TimeSpan.TicksPerMillisecond);

                if (oldSamplingInterval < m_nextSampleTime)
                {
                    m_nextSampleTime -= oldSamplingInterval;
                }

                m_samplingInterval = samplingInterval;

                // calculate the next sampling interval.
                long newSamplingInterval = (long)(m_samplingInterval *
                    TimeSpan.TicksPerMillisecond);

                if (m_samplingInterval > 0)
                {
                    m_nextSampleTime += newSamplingInterval;
                }
                else
                {
                    m_nextSampleTime = 0;
                }

                // update the filter and the range.
                DataChangeFilter = filter;
                m_range = 0;

                if (range != null)
                {
                    m_range = range.High - range.Low;
                }

                // update the queue size.
                if (queueSize > 1)
                {
                    m_queue ??= new DataChangeQueueHandler(
                        Id,
                        false,
                        m_monitoredItemQueueFactory,
                        m_source.Server.Telemetry);
                    m_queue.SetQueueSize(
                        queueSize,
                        discardOldest,
                        diagnosticsMasks);
                    m_queue.SetSamplingInterval(samplingInterval);
                }
                else
                {
                    m_queue = null;
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Called when the attribute being monitored changed. Reads and queues the value.
        /// </summary>
        public void ValueChanged(ISystemContext context)
        {
            var value = new DataValue();

            ServiceResult error = m_source.Node
                .ReadAttribute(context, AttributeId, NumericRange.Empty, null, value);

            if (ServiceResult.IsBad(error))
            {
                value = new DataValue(error.StatusCode);
            }

            value.ServerTimestamp = DateTime.UtcNow;

            QueueValue(value, error, false);
        }

        /// <summary>
        /// The node manager for the monitored item.
        /// </summary>
        public INodeManager NodeManager => m_source.NodeManager;

        /// <summary>
        /// The session for the monitored item.
        /// </summary>
        public ISession Session
        {
            get
            {
                ISubscription subscription = SubscriptionCallback;

                if (subscription != null)
                {
                    return subscription.Session;
                }

                return null;
            }
        }

        /// <summary>
        /// The monitored items owner identity.
        /// </summary>
        public IUserIdentity EffectiveIdentity
        {
            get
            {
                ISubscription subscription = SubscriptionCallback;
                return subscription?.EffectiveIdentity;
            }
        }

        /// <summary>
        /// The identifier for the subscription that the monitored item belongs to.
        /// </summary>
        public uint SubscriptionId
        {
            get
            {
                ISubscription subscription = SubscriptionCallback;

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
        public uint Id { get; }

        /// <summary>
        /// The client handle.
        /// </summary>
        public uint ClientHandle { get; private set; }

        /// <summary>
        /// The callback to use to notify the subscription when values are ready to publish.
        /// </summary>
        public ISubscription SubscriptionCallback { get; set; }

        /// <summary>
        /// The handle assigned to the monitored item by the node manager.
        /// </summary>
        public object ManagerHandle => m_source;

        /// <summary>
        /// The type of monitor item.
        /// </summary>
        public int MonitoredItemType => MonitoredItemTypeMask.DataChange;

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
                    if (!m_readyToPublish)
                    {
                        return false;
                    }

                    // check if monitoring was turned off.
                    if (MonitoringMode != MonitoringMode.Reporting)
                    {
                        return false;
                    }

                    // re-queue if too little time has passed since the last publish.
                    long now = DateTime.UtcNow.Ticks;

                    return m_nextSampleTime <= now;
                }
            }
        }

        /// <summary>
        /// Gets or Sets a value indicating whether the item is ready to trigger in case it has some linked items.
        /// </summary>
        public bool IsReadyToTrigger
        {
            get
            {
                lock (m_lock)
                {
                    // only allow to trigger if sampling or reporting.
                    if (MonitoringMode == MonitoringMode.Disabled)
                    {
                        return false;
                    }

                    return m_readyToTrigger;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_readyToTrigger = value;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsResendData
        {
            get
            {
                lock (m_lock)
                {
                    return m_resendData;
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
                result = new MonitoredItemCreateResult
                {
                    MonitoredItemId = Id,
                    StatusCode = StatusCodes.Good,
                    RevisedSamplingInterval = m_samplingInterval,
                    RevisedQueueSize = 0,
                    FilterResult = null
                };

                if (m_queue != null)
                {
                    result.RevisedQueueSize = m_queueSize;
                }

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
                result = new MonitoredItemModifyResult
                {
                    StatusCode = StatusCodes.Good,
                    RevisedSamplingInterval = m_samplingInterval,
                    RevisedQueueSize = 0,
                    FilterResult = null
                };

                if (m_queue != null)
                {
                    result.RevisedQueueSize = m_queueSize;
                }

                return ServiceResult.Good;
            }
        }

        /// <inheritdoc/>
        public void SetupResendDataTrigger()
        {
            lock (m_lock)
            {
                if (MonitoringMode == MonitoringMode.Reporting)
                {
                    m_resendData = true;
                }
            }
        }

        /// <inheritdoc/>
        public IStoredMonitoredItem ToStorableMonitoredItem()
        {
            return new StoredMonitoredItem
            {
                SamplingInterval = m_samplingInterval,
                SubscriptionId = SubscriptionCallback.Id,
                QueueSize = m_queueSize,
                AlwaysReportUpdates = AlwaysReportUpdates,
                AttributeId = AttributeId,
                ClientHandle = ClientHandle,
                DiagnosticsMasks = m_diagnosticsMasks,
                IsDurable = false,
                Encoding = DataEncoding,
                FilterToUse = DataChangeFilter,
                Id = Id,
                LastError = m_lastError,
                LastValue = m_lastValue,
                MonitoringMode = MonitoringMode,
                NodeId = m_source.Node.NodeId,
                OriginalFilter = DataChangeFilter,
                Range = m_range,
                TimestampsToReturn = m_timestampsToReturn,
                ParsedIndexRange = m_indexRange
            };
        }

        /// <inheritdoc/>
        public void QueueValue(DataValue value, ServiceResult error)
        {
            QueueValue(value, error, false);
        }

        /// <inheritdoc/>
        public void QueueValue(DataValue value, ServiceResult error, bool ignoreFilters)
        {
            lock (m_lock)
            {
                // check if value has changed.
                if (!AlwaysReportUpdates &&
                    !ignoreFilters &&
                    !MonitoredItem.ValueChanged(
                        value,
                        error,
                        m_lastValue,
                        m_lastError,
                        DataChangeFilter,
                        m_range))
                {
                    return;
                }

                // make a shallow copy of the value.
                if (value != null)
                {
                    value = new DataValue
                    {
                        WrappedValue = value.WrappedValue,
                        StatusCode = value.StatusCode,
                        SourceTimestamp = value.SourceTimestamp,
                        SourcePicoseconds = value.SourcePicoseconds,
                        ServerTimestamp = value.ServerTimestamp,
                        ServerPicoseconds = value.ServerPicoseconds
                    };

                    // ensure the data value matches the error status code.
                    if (error != null && error.StatusCode.Code != 0)
                    {
                        value.StatusCode = error.StatusCode;
                    }
                }

                m_lastValue = value;
                m_lastError = error;

                // queue value.
                m_queue?.QueueValue(value, error);

                // flag the item as ready to publish.
                m_readyToPublish = true;
                m_readyToTrigger = true;
            }
        }

        /// <summary>
        /// Sets a flag indicating that the semantics for the monitored node have changed.
        /// </summary>
        /// <remarks>
        /// The StatusCode for next value reported by the monitored item will have the SemanticsChanged bit set.
        /// </remarks>
        public void SetSemanticsChanged()
        {
            lock (m_lock)
            {
                m_semanticsChanged = true;
            }
        }

        /// <summary>
        /// Sets a flag indicating that the structure of the monitored node has changed.
        /// </summary>
        /// <remarks>
        /// The StatusCode for next value reported by the monitored item will have the StructureChanged bit set.
        /// </remarks>
        public void SetStructureChanged()
        {
            lock (m_lock)
            {
                m_structureChanged = true;
            }
        }

        /// <summary>
        /// Changes the monitoring mode.
        /// </summary>
        public MonitoringMode SetMonitoringMode(MonitoringMode monitoringMode)
        {
            lock (m_lock)
            {
                MonitoringMode previousMode = MonitoringMode;

                if (previousMode == monitoringMode)
                {
                    return previousMode;
                }

                if (previousMode == MonitoringMode.Disabled)
                {
                    m_nextSampleTime = DateTime.UtcNow.Ticks;
                    m_lastError = null;
                    m_lastValue = null;
                }

                MonitoringMode = monitoringMode;

                if (monitoringMode == MonitoringMode.Disabled)
                {
                    m_readyToPublish = false;
                    m_readyToTrigger = false;
                }

                return previousMode;
            }
        }

        /// <summary>
        /// No filters supported.
        /// </summary>
        public DataChangeFilter DataChangeFilter { get; private set; }

        public bool IsDurable => false;

        public NodeId NodeId { get; }

        /// <summary>
        /// Increments the sample time to the next interval.
        /// </summary>
        private void IncrementSampleTime()
        {
            // update next sample time.
            long now = DateTime.UtcNow.Ticks;
            long samplingInterval = (long)(m_samplingInterval * TimeSpan.TicksPerMillisecond);

            if (m_nextSampleTime > 0)
            {
                long delta = now - m_nextSampleTime;

                if (samplingInterval > 0 && delta >= 0)
                {
                    m_nextSampleTime += ((delta / samplingInterval) + 1) * samplingInterval;
                }
            }
            // set sampling time based on current time.
            else
            {
                m_nextSampleTime = now + samplingInterval;
            }
        }

        /// <summary>
        /// Called by the subscription to publish any notification.
        /// </summary>
        public bool Publish(
            OperationContext context,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics,
            uint maxNotificationsPerPublish,
            ILogger logger)
        {
            lock (m_lock)
            {
                // check if not ready to publish.
                if (!IsReadyToPublish)
                {
                    if (!m_resendData)
                    {
                        return false;
                    }
                }
                else
                {
                    // update sample time.
                    IncrementSampleTime();
                }

                // check if queuing is enabled.
                if (m_queue != null && (!m_resendData || m_queue.ItemsInQueue != 0))
                {
                    uint notificationCount = 0;
                    while (
                        notificationCount < maxNotificationsPerPublish &&
                        m_queue.PublishSingleValue(out DataValue value, out ServiceResult error))
                    {
                        Publish(context, value, error, notifications, diagnostics, logger);
                        notificationCount++;

                        if (m_resendData)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    Publish(context, m_lastValue, m_lastError, notifications, diagnostics, logger);
                }

                bool moreValuesToPublish = m_queue?.ItemsInQueue > 0;

                // update flags
                m_readyToPublish = moreValuesToPublish;
                m_readyToTrigger = moreValuesToPublish;
                m_resendData = false;

                return moreValuesToPublish;
            }
        }

        /// <summary>
        /// Publishes a value.
        /// </summary>
        private void Publish(
            OperationContext context,
            DataValue value,
            ServiceResult error,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics,
            ILogger logger)
        {
            // set semantics changed bit.
            if (m_semanticsChanged)
            {
                if (value != null)
                {
                    value.StatusCode = value.StatusCode.SetSemanticsChanged(true);
                }

                if (error != null)
                {
                    error = new ServiceResult(
                        error.StatusCode.SetSemanticsChanged(true),
                        error.NamespaceUri,
                        error.LocalizedText,
                        error.AdditionalInfo,
                        error.InnerResult);
                }

                m_semanticsChanged = false;
            }

            // set structure changed bit.
            if (m_structureChanged)
            {
                if (value != null)
                {
                    value.StatusCode = value.StatusCode.SetStructureChanged(true);
                }

                if (error != null)
                {
                    _ = new ServiceResult(
                        error.StatusCode.SetStructureChanged(true),
                        error.NamespaceUri,
                        error.LocalizedText,
                        error.AdditionalInfo,
                        error.InnerResult);
                }

                m_structureChanged = false;
            }

            // copy data value.
            var item = new MonitoredItemNotification { ClientHandle = ClientHandle, Value = value };

            // apply timestamp filter.
            if (m_timestampsToReturn is not TimestampsToReturn.Server and not TimestampsToReturn.Both)
            {
                item.Value.ServerTimestamp = DateTime.MinValue;
            }

            if (m_timestampsToReturn is not TimestampsToReturn.Source and not TimestampsToReturn.Both)
            {
                item.Value.SourceTimestamp = DateTime.MinValue;
            }

            notifications.Enqueue(item);

            // update diagnostic info.
            DiagnosticInfo diagnosticInfo = null;

            if (m_lastError != null && (m_diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                    m_source.Server,
                    context,
                    m_lastError,
                    logger);
            }

            diagnostics.Enqueue(diagnosticInfo);
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
            //only durable queues need to be disposed
        }

        private readonly Lock m_lock = new();
        private readonly IMonitoredItemQueueFactory m_monitoredItemQueueFactory;
        private readonly MonitoredNode m_source;
        private DataValue m_lastValue;
        private ServiceResult m_lastError;
        private NumericRange m_indexRange;
        private TimestampsToReturn m_timestampsToReturn;
        private DiagnosticsMasks m_diagnosticsMasks;
        private double m_samplingInterval;
        private DataChangeQueueHandler m_queue;
        private uint m_queueSize;
        private double m_range;
        private long m_nextSampleTime;
        private bool m_readyToPublish;
        private bool m_readyToTrigger;
        private bool m_semanticsChanged;
        private bool m_structureChanged;
        private bool m_resendData;
    }
}
