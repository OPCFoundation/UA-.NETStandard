/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A handle that describes how to access a node/attribute via an i/o manager.
    /// </summary>
    public class MonitoredItem :
        IEventMonitoredItem,
        ISampledDataChangeMonitoredItem,
        ITriggeredMonitoredItem
    {
        /// <summary>
        /// Initializes the object with its node type.
        /// </summary>
        public MonitoredItem(
            IServerInternal server,
            INodeManager nodeManager,
            object managerHandle,
            uint subscriptionId,
            uint id,
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
            double sourceSamplingInterval,
            bool createDurable = false)
        {
            if (itemToMonitor == null)
            {
                throw new ArgumentNullException(nameof(itemToMonitor));
            }

            m_logger = server.Telemetry.CreateLogger<MonitoredItem>();

            Initialize();

            m_server = server;
            NodeManager = nodeManager;
            ManagerHandle = managerHandle;
            SubscriptionId = subscriptionId;
            Id = id;
            NodeId = itemToMonitor.NodeId;
            AttributeId = itemToMonitor.AttributeId;
            m_indexRange = itemToMonitor.IndexRange;
            m_parsedIndexRange = itemToMonitor.ParsedIndexRange;
            DataEncoding = itemToMonitor.DataEncoding;
            DiagnosticsMasks = diagnosticsMasks;
            m_timestampsToReturn = timestampsToReturn;
            MonitoringMode = monitoringMode;
            ClientHandle = clientHandle;
            Filter = originalFilter;
            m_filterToUse = filterToUse;
            m_range = 0;
            m_samplingInterval = samplingInterval;
            QueueSize = queueSize;
            m_discardOldest = discardOldest;
            m_sourceSamplingInterval = (int)sourceSamplingInterval;
            m_calculator = null;
            m_nextSamplingTime = HiResClock.TickCount64;
            AlwaysReportUpdates = false;
            m_monitoredItemQueueFactory = m_server.MonitoredItemQueueFactory;
            m_subscriptionStore = m_server.SubscriptionStore;
            IsDurable = createDurable;

            if (!m_monitoredItemQueueFactory.SupportsDurableQueues && IsDurable)
            {
                m_logger.LogError(
                    "Durable subscription was create but no MonitoredItemQueueFactory that supports durable queues was registered, monitored item with id {id} could not be created",
                    id);
                throw new ServiceResultException(StatusCodes.BadInternalError);
            }

            MonitoredItemType = MonitoredItemTypeMask.DataChange;

            if (originalFilter is EventFilter)
            {
                MonitoredItemType = MonitoredItemTypeMask.Events;

                if (itemToMonitor.NodeId == Objects.Server)
                {
                    MonitoredItemType |= MonitoredItemTypeMask.AllEvents;
                }
            }

            // create aggregate calculator.
            var aggregateFilter = filterToUse as ServerAggregateFilter;

            if (filterToUse is ServerAggregateFilter)
            {
                m_calculator = m_server.AggregateManager.CreateCalculator(
                    aggregateFilter.AggregateType,
                    aggregateFilter.StartTime,
                    DateTime.MaxValue,
                    aggregateFilter.ProcessingInterval,
                    aggregateFilter.Stepped,
                    aggregateFilter.AggregateConfiguration);
            }

            if (range != null)
            {
                m_range = range.High - range.Low;
            }

            // report change to item state.
            ServerUtils.ReportCreateMonitoredItem(
                NodeId,
                Id,
                m_samplingInterval,
                QueueSize,
                m_discardOldest,
                m_filterToUse,
                MonitoringMode);

            InitializeQueue();
        }

        /// <summary>
        /// Restore a MonitoredItem afer a restart.
        /// </summary>
        public MonitoredItem(
            IServerInternal server,
            INodeManager nodeManager,
            object managerHandle,
            IStoredMonitoredItem storedMonitoredItem)
        {
            if (storedMonitoredItem == null)
            {
                throw new ArgumentNullException(nameof(storedMonitoredItem));
            }
            m_logger = server.Telemetry.CreateLogger<MonitoredItem>();

            Initialize();

            m_server = server;
            NodeManager = nodeManager;
            ManagerHandle = managerHandle;
            SubscriptionId = storedMonitoredItem.SubscriptionId;
            Id = storedMonitoredItem.Id;
            NodeId = storedMonitoredItem.NodeId;
            AttributeId = storedMonitoredItem.AttributeId;
            m_indexRange = storedMonitoredItem.IndexRange;
            m_parsedIndexRange = storedMonitoredItem.ParsedIndexRange;
            DataEncoding = storedMonitoredItem.Encoding;
            DiagnosticsMasks = storedMonitoredItem.DiagnosticsMasks;
            m_timestampsToReturn = storedMonitoredItem.TimestampsToReturn;
            MonitoringMode = storedMonitoredItem.MonitoringMode;
            ClientHandle = storedMonitoredItem.ClientHandle;
            Filter = storedMonitoredItem.OriginalFilter;
            m_filterToUse = storedMonitoredItem.FilterToUse;
            m_range = storedMonitoredItem.Range;
            m_samplingInterval = storedMonitoredItem.SamplingInterval;
            QueueSize = storedMonitoredItem.QueueSize;
            m_discardOldest = storedMonitoredItem.DiscardOldest;
            m_sourceSamplingInterval = storedMonitoredItem.SourceSamplingInterval;
            m_calculator = null;
            m_nextSamplingTime = HiResClock.TickCount64;
            m_monitoredItemQueueFactory = m_server.MonitoredItemQueueFactory;
            m_subscriptionStore = m_server.SubscriptionStore;
            IsDurable = storedMonitoredItem.IsDurable;
            AlwaysReportUpdates = storedMonitoredItem.AlwaysReportUpdates;
            m_lastError = storedMonitoredItem.LastError;
            m_lastValue = storedMonitoredItem.LastValue;
            MonitoredItemType = storedMonitoredItem.TypeMask;

            // create aggregate calculator.
            if (storedMonitoredItem.FilterToUse is ServerAggregateFilter aggregateFilter)
            {
                m_calculator = m_server.AggregateManager.CreateCalculator(
                    aggregateFilter.AggregateType,
                    aggregateFilter.StartTime,
                    DateTime.MaxValue,
                    aggregateFilter.ProcessingInterval,
                    aggregateFilter.Stepped,
                    aggregateFilter.AggregateConfiguration);
            }

            // report change to item state.
            ServerUtils.ReportCreateMonitoredItem(
                NodeId,
                Id,
                m_samplingInterval,
                QueueSize,
                m_discardOldest,
                m_filterToUse,
                MonitoringMode);

            RestoreQueue();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_server = null;
            NodeManager = null;
            ManagerHandle = null;
            SubscriptionId = 0;
            Id = 0;
            NodeId = null;
            AttributeId = 0;
            m_indexRange = null;
            m_parsedIndexRange = NumericRange.Empty;
            DataEncoding = null;
            ClientHandle = 0;
            MonitoringMode = MonitoringMode.Disabled;
            m_samplingInterval = 0;
            QueueSize = 0;
            m_discardOldest = true;
            Filter = null;
            m_lastValue = null;
            m_lastError = null;
            m_readyToPublish = false;
            m_readyToTrigger = false;
            m_sourceSamplingInterval = 0;
            m_samplingError = ServiceResult.Good;
            m_resendData = false;
        }

        /// <summary>
        /// The node manager that created the item.
        /// </summary>
        public INodeManager NodeManager { get; private set; }

        /// <summary>
        /// The handle assigned by the node manager when it created the item.
        /// </summary>
        public object ManagerHandle { get; private set; }

        /// <summary>
        /// The identifier for the subscription that owns the monitored item.
        /// </summary>
        public uint SubscriptionId { get; private set; }

        /// <summary>
        /// A bit mask that indicates what the monitored item is.
        /// </summary>
        /// <remarks>
        /// Predefined bits are defined by the MonitoredItemTypeMasks class.
        /// NodeManagers may use the remaining bits.
        /// </remarks>
        public int MonitoredItemType { get; }

        /// <summary>
        /// Returns true if the item is ready to publish.
        /// </summary>
        public bool IsReadyToPublish
        {
            get
            {
                // check if aggregate interval has passed.
                if (m_calculator != null && m_calculator.HasEndTimePassed(DateTime.UtcNow))
                {
                    return true;
                }

                // check if not ready to publish in case it doesn't ResendData
                if (!m_readyToPublish)
                {
                    ServerUtils.EventLog.MonitoredItemReady(Id, "FALSE");
                    m_logger.LogTrace("IsReadyToPublish[{MonitoredItemId}] FALSE", Id);
                    return false;
                }

                // check if it has been triggered.
                if (MonitoringMode != MonitoringMode.Disabled && m_triggered)
                {
                    ServerUtils.EventLog.MonitoredItemReady(Id, "TRIGGERED");
                    m_logger.LogTrace("IsReadyToPublish[{MonitoredItemId}] TRIGGERED", Id);
                    return true;
                }

                // check if monitoring was turned off.
                if (MonitoringMode != MonitoringMode.Reporting)
                {
                    ServerUtils.EventLog.MonitoredItemReady(Id, "FALSE");
                    m_logger.LogTrace("IsReadyToPublish[{MonitoredItemId}] FALSE", Id);
                    return false;
                }

                if (m_sourceSamplingInterval == 0)
                {
                    // re-queue if too little time has passed since the last publish, in case it doesn't ResendData
                    long now = HiResClock.TickCount64;

                    if (m_nextSamplingTime > now)
                    {
                        ServerUtils.EventLog.MonitoredItemReady(
                            Id,
                            Utils.Format("FALSE {0}ms", m_nextSamplingTime - now));
                        m_logger.LogTrace("IsReadyToPublish[{MonitoredItemId}] FALSE {Delay}ms", Id, m_nextSamplingTime - now);
                        return false;
                    }
                }
                ServerUtils.EventLog.MonitoredItemReady(Id, "NORMAL");
                m_logger.LogTrace("IsReadyToPublish[{MonitoredItemId}] NORMAL", Id);
                return true;
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

        /// <inheritdoc/>
        public void SetupResendDataTrigger()
        {
            lock (m_lock)
            {
                if (MonitoringMode == MonitoringMode.Reporting &&
                    (MonitoredItemType & MonitoredItemTypeMask.DataChange) != 0)
                {
                    m_resendData = true;
                }
            }
        }

        /// <summary>
        /// Sets a flag indicating that the item has been triggered and should publish.
        /// </summary>
        public bool SetTriggered()
        {
            lock (m_lock)
            {
                if (m_readyToPublish)
                {
                    m_logger.LogTrace(Utils.TraceMasks.OperationDetail, "SetTriggered[{Id}]", Id);
                    m_triggered = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Sets a flag indicating that the semantics for the monitored node have changed.
        /// </summary>
        /// <remarks>
        /// The StatusCode for next value reported by the monitored item will have the SemanticsChanged bit set.
        /// </remarks>
        public void SetSemanticsChanged()
        {
            m_semanticsChanged = true;
        }

        /// <summary>
        /// Sets a flag indicating that the structure of the monitored node has changed.
        /// </summary>
        /// <remarks>
        /// The StatusCode for next value reported by the monitored item will have the StructureChanged bit set.
        /// </remarks>
        public void SetStructureChanged()
        {
            m_structureChanged = true;
        }

        /// <summary>
        /// The filter used by the monitored item.
        /// </summary>
        public MonitoringFilter Filter { get; private set; }

        /// <summary>
        /// The event filter used by the monitored item.
        /// </summary>
        public EventFilter EventFilter => Filter as EventFilter;

        /// <summary>
        /// The data change filter used by the monitored item.
        /// </summary>
        public DataChangeFilter DataChangeFilter => Filter as DataChangeFilter;

        /// <summary>
        /// The session that owns the monitored item.
        /// </summary>
        public ISession Session
        {
            get
            {
                lock (m_lock)
                {
                    return m_subscription?.Session;
                }
            }
        }

        /// <summary>
        /// The monitored items owner identity.
        /// </summary>
        public IUserIdentity EffectiveIdentity
        {
            get
            {
                lock (m_lock)
                {
                    return m_subscription?.EffectiveIdentity;
                }
            }
        }

        /// <summary>
        /// The identifier for the item that is unique within the server.
        /// </summary>
        public uint Id { get; private set; }

        /// <summary>
        /// The identifier for the client handle assigned to the monitored item.
        /// </summary>
        public uint ClientHandle { get; private set; }

        /// <summary>
        /// The node id being monitored.
        /// </summary>
        public NodeId NodeId { get; private set; }

        /// <summary>
        /// The attribute being monitored.
        /// </summary>
        public uint AttributeId { get; private set; }

        /// <summary>
        /// The current monitoring mode for the item
        /// </summary>
        public MonitoringMode MonitoringMode { get; private set; }

        /// <summary>
        /// The sampling interval for the item.
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
        /// The minimum sampling interval for the item.
        /// </summary>
        public double MinimumSamplingInterval => m_sourceSamplingInterval;

        /// <summary>
        /// The queue size for the item.
        /// </summary>
        public uint QueueSize { get; private set; }

        /// <summary>
        /// Gets number of elements actually contained in value queue.
        /// </summary>
        public int ItemsInQueue
        {
            get
            {
                lock (m_lock)
                {
                    if (m_eventQueueHandler != null)
                    {
                        return m_eventQueueHandler.ItemsInQueue;
                    }

                    if (m_dataChangeQueueHandler != null)
                    {
                        return m_dataChangeQueueHandler.ItemsInQueue;
                    }

                    return 0;
                }
            }
        }

        /// <summary>
        /// The diagnostics masks to use when collecting notifications for the item.
        /// </summary>
        public DiagnosticsMasks DiagnosticsMasks { get; private set; }

        /// <summary>
        /// The index range requested by the monitored item.
        /// </summary>
        public NumericRange IndexRange => m_parsedIndexRange;

        /// <summary>
        /// The data encoding requested by the monitored item.
        /// </summary>
        public QualifiedName DataEncoding { get; private set; }

        /// <summary>
        /// Whether the monitored item should report a value without checking if it was changed.
        /// </summary>
        public bool AlwaysReportUpdates { get; set; }

        /// <summary>
        /// Returns a description of the item being monitored.
        /// </summary>
        public ReadValueId GetReadValueId()
        {
            lock (m_lock)
            {
                return new ReadValueId
                {
                    NodeId = NodeId,
                    AttributeId = AttributeId,
                    IndexRange = m_indexRange,
                    ParsedIndexRange = m_parsedIndexRange,
                    DataEncoding = DataEncoding,
                    Handle = ManagerHandle
                };
            }
        }

        /// <summary>
        /// Sets an error that occured in the sampling group.
        /// </summary>
        /// <remarks>
        /// The sampling group or node manager that owns the item may call this to indicate that
        /// a fatal error occurred which means the item will no longer receive any data updates.
        /// This error state can be cleared by calling this method and passing in ServiceResult.Good.
        /// </remarks>
        public void SetSamplingError(ServiceResult error)
        {
            lock (m_lock)
            {
                if (error == null)
                {
                    m_samplingError = ServiceResult.Good;
                }

                m_samplingError = error;
            }
        }

        /// <summary>
        /// Returns the result after creating the monitor item.
        /// </summary>
        public ServiceResult GetCreateResult(out MonitoredItemCreateResult result)
        {
            lock (m_lock)
            {
                result = new MonitoredItemCreateResult
                {
                    MonitoredItemId = Id,
                    RevisedSamplingInterval = m_samplingInterval,
                    RevisedQueueSize = QueueSize,
                    StatusCode = StatusCodes.Good
                };

                if (ServiceResult.IsBad(m_samplingError))
                {
                    result.StatusCode = m_samplingError.Code;
                }

                return m_samplingError;
            }
        }

        /// <summary>
        /// Returns the result after modifying the monitor item.
        /// </summary>
        public ServiceResult GetModifyResult(out MonitoredItemModifyResult result)
        {
            lock (m_lock)
            {
                result = new MonitoredItemModifyResult
                {
                    RevisedSamplingInterval = m_samplingInterval,
                    RevisedQueueSize = QueueSize,
                    StatusCode = StatusCodes.Good
                };

                if (ServiceResult.IsBad(m_samplingError))
                {
                    result.StatusCode = m_samplingError.Code;
                }

                return m_samplingError;
            }
        }

        /// <summary>
        /// Modifies the attributes for monitored item.
        /// </summary>
        public ServiceResult ModifyAttributes(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            MonitoringFilter originalFilter,
            MonitoringFilter filterToUse,
            Range range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest)
        {
            lock (m_lock)
            {
                DiagnosticsMasks = diagnosticsMasks;
                m_timestampsToReturn = timestampsToReturn;
                ClientHandle = clientHandle;
                m_discardOldest = discardOldest;

                Filter = originalFilter;
                m_filterToUse = filterToUse;

                if (range != null)
                {
                    m_range = range.High - range.Low;
                }

                SetSamplingInterval(samplingInterval);
                QueueSize = queueSize;

                // check if aggregate filter has been updated.
                var aggregateFilter = filterToUse as ServerAggregateFilter;

                if (filterToUse is ServerAggregateFilter)
                {
                    var existingFilter = filterToUse as ServerAggregateFilter;

                    bool match = existingFilter != null;

                    if (match && existingFilter.AggregateType != aggregateFilter.AggregateType)
                    {
                        match = false;
                    }

                    if (match &&
                        existingFilter.ProcessingInterval != aggregateFilter.ProcessingInterval)
                    {
                        match = false;
                    }

                    if (match && existingFilter.StartTime != aggregateFilter.StartTime)
                    {
                        match = false;
                    }

                    if (match &&
                        !existingFilter.AggregateConfiguration
                            .IsEqual(aggregateFilter.AggregateConfiguration))
                    {
                        match = false;
                    }

                    if (!match)
                    {
                        m_calculator = m_server.AggregateManager.CreateCalculator(
                            aggregateFilter.AggregateType,
                            aggregateFilter.StartTime,
                            DateTime.MaxValue,
                            aggregateFilter.ProcessingInterval,
                            aggregateFilter.Stepped,
                            aggregateFilter.AggregateConfiguration);
                    }
                }

                // report change to item state.
                ServerUtils.ReportModifyMonitoredItem(
                    NodeId,
                    Id,
                    m_samplingInterval,
                    QueueSize,
                    m_discardOldest,
                    m_filterToUse,
                    MonitoringMode);

                InitializeQueue();

                return null;
            }
        }

        /// <summary>
        /// Updates the sampling interval for an item.
        /// </summary>
        public void SetSamplingInterval(double samplingInterval)
        {
            lock (m_lock)
            {
                if (samplingInterval == -1)
                {
                    return;
                }

                // subtract the previous sampling interval.
                long oldSamplingInterval = (long)m_samplingInterval;

                if (oldSamplingInterval < m_nextSamplingTime)
                {
                    m_nextSamplingTime -= oldSamplingInterval;
                }

                m_samplingInterval = samplingInterval;

                // calculate the next sampling interval.
                long newSamplingInterval = (long)m_samplingInterval;

                if (m_samplingInterval > 0)
                {
                    m_nextSamplingTime += newSamplingInterval;
                }
                else
                {
                    m_nextSamplingTime = 0;
                }
            }
        }

        /// <summary>
        /// Changes the monitoring mode for the item.
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

                m_logger.LogTrace(
                    "MONITORING MODE[{MonitoredItemId}] {Previous} -> {New}",
                    Id,
                    MonitoringMode,
                    monitoringMode);

                if (previousMode == MonitoringMode.Disabled)
                {
                    m_nextSamplingTime = HiResClock.TickCount64;
                    m_lastError = null;
                    m_lastValue = null;
                }

                MonitoringMode = monitoringMode;

                if (monitoringMode == MonitoringMode.Disabled)
                {
                    m_readyToPublish = false;
                    m_readyToTrigger = false;
                    m_triggered = false;
                }

                // report change to item state.
                ServerUtils.ReportModifyMonitoredItem(
                    NodeId,
                    Id,
                    m_samplingInterval,
                    QueueSize,
                    m_discardOldest,
                    m_filterToUse,
                    MonitoringMode);

                InitializeQueue();

                return previousMode;
            }
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        public virtual void QueueValue(DataValue value, ServiceResult error)
        {
            QueueValue(value, error, false);
        }

        /// <summary>
        /// Updates the queue with a data value or an error.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void QueueValue(DataValue value, ServiceResult error, bool ignoreFilters)
        {
            lock (m_lock)
            {
                // this method should only be called for variables.
                if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                // check monitoring mode.
                if (MonitoringMode == MonitoringMode.Disabled)
                {
                    return;
                }

                // make a shallow copy of the value.
                if (value != null)
                {
                    m_logger.LogTrace(
                        Utils.TraceMasks.OperationDetail,
                        "RECEIVED VALUE[{MonitoredItemId}] Value={Value}",
                        Id,
                        value.WrappedValue);

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

                // create empty value if none provided.
                if (ServiceResult.IsBad(error) && value == null)
                {
                    value = new DataValue
                    {
                        StatusCode = error.StatusCode,
                        SourceTimestamp = DateTime.UtcNow,
                        ServerTimestamp = DateTime.UtcNow
                    };
                }

                // this should never happen.
                if (value == null)
                {
                    return;
                }

                // apply aggregate filter.
                if (m_calculator != null)
                {
                    if (!m_calculator.QueueRawValue(value))
                    {
                        m_logger.LogTrace(
                            "Value received out of order: {SourceTimestamp}, ServerHandle={MonitoredItemId}",
                            value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            Id);
                    }

                    DataValue processedValue = m_calculator.GetProcessedValue(false);

                    while (processedValue != null)
                    {
                        AddValueToQueue(processedValue, null);
                        processedValue = m_calculator.GetProcessedValue(false);
                    }

                    return;
                }

                // apply filter to incoming item.
                if (!AlwaysReportUpdates && !ignoreFilters && !ApplyFilter(value, error))
                {
                    ServerUtils.ReportFilteredValue(NodeId, Id, value);
                    return;
                }

                ServerUtils.ReportQueuedValue(NodeId, Id, value);

                // add the value to the queue.
                AddValueToQueue(value, error);
            }
        }

        /// <summary>
        /// Adds a value to the queue.
        /// </summary>
        private void AddValueToQueue(DataValue value, ServiceResult error)
        {
            if (QueueSize > 1)
            {
                m_dataChangeQueueHandler.QueueValue(value, error);
            }

            if (m_lastValue != null)
            {
                m_readyToTrigger = true;
            }

            // save last value received.
            m_lastValue = value;
            m_lastError = error;
            m_readyToPublish = true;

            m_logger.LogTrace(
                Utils.TraceMasks.OperationDetail,
                "QUEUE VALUE[{MonitoredItemId}]: Value={Value} CODE={Code}<{Code:X8}> OVERFLOW={Overflow}",
                Id,
                m_lastValue.WrappedValue,
                m_lastValue.StatusCode.Code,
                m_lastValue.StatusCode.Code,
                m_lastValue.StatusCode.Overflow);
        }

        /// <summary>
        /// Whether the item is monitoring all events produced by the server.
        /// </summary>
        public bool MonitoringAllEvents => NodeId == ObjectIds.Server;

        /// <summary>
        /// Fetches the event fields from the event.
        /// </summary>
        private EventFieldList GetEventFields(
            FilterContext context,
            EventFilter filter,
            IFilterTarget instance)
        {
            // fetch the event fields.
            var fields = new EventFieldList { ClientHandle = ClientHandle, Handle = instance };

            foreach (SimpleAttributeOperand clause in filter.SelectClauses)
            {
                // get the value of the attribute (apply localization).
                object value = instance.GetAttributeValue(
                    context,
                    clause.TypeDefinitionId,
                    clause.BrowsePath,
                    clause.AttributeId,
                    clause.ParsedIndexRange);

                // add the value to the list of event fields.
                if (value != null)
                {
                    // translate any localized text.
                    if (value is LocalizedText text)
                    {
                        value = m_server.ResourceManager.Translate(Session?.PreferredLocales, text);
                    }

                    // add value.
                    fields.EventFields.Add(new Variant(value));
                }
                // add a dummy entry for missing values.
                else
                {
                    fields.EventFields.Add(Variant.Null);
                }
            }

            return fields;
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        public virtual void QueueEvent(IFilterTarget instance)
        {
            QueueEvent(instance, false);
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void QueueEvent(IFilterTarget instance, bool bypassFilter)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            lock (m_lock)
            {
                // this method should only be called for objects or views.
                if ((MonitoredItemType & MonitoredItemTypeMask.Events) == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                // can't do anything if queuing is disabled.
                if (m_eventQueueHandler == null)
                {
                    return;
                }

                //Check for duplicates and discard
                if (m_eventQueueHandler.IsEventContainedInQueue(instance))
                {
                    return;
                }

                // check for space in the queue.
                if (m_eventQueueHandler.SetQueueOverflowIfFull())
                {
                    return;
                }

                // construct the context to use for the event filter.
                var context = new FilterContext(
                    m_server.NamespaceUris,
                    m_server.TypeTree,
                    Session?.PreferredLocales,
                    m_server.Telemetry);

                // event filter must be specified.
                if (m_filterToUse is not EventFilter filter)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                // apply filter.
                if (!bypassFilter && !CanSendFilteredAlarm(context, filter, instance))
                {
                    return;
                }

                // fetch the event fields.
                EventFieldList fields = GetEventFields(context, filter, instance);
                QueueEvent(fields);
            }
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        public virtual void QueueEvent(EventFieldList fields)
        {
            lock (m_lock)
            {
                m_eventQueueHandler.QueueEvent(fields);
                m_readyToPublish = true;
                m_readyToTrigger = true;
            }
        }

        /// <summary>
        /// Determines whether an event can be sent with SupportsFilteredRetain in consideration.
        /// </summary>
        protected bool CanSendFilteredAlarm(
            FilterContext context,
            EventFilter filter,
            IFilterTarget instance)
        {
            bool passedFilter = filter.WhereClause.Evaluate(context, instance);

            ConditionState alarmCondition = null;
            NodeId conditionId = null;
            if (instance is InstanceStateSnapshot instanceStateSnapshot)
            {
                alarmCondition = instanceStateSnapshot.Handle as ConditionState;

                if (alarmCondition != null &&
                    alarmCondition.SupportsFilteredRetain != null &&
                    alarmCondition.SupportsFilteredRetain.Value &&
                    filter.SelectClauses != null)
                {
                    conditionId = alarmCondition.NodeId;
                }
            }

            bool canSend = passedFilter;

            // ConditionId is valid only if FilteredRetain is set for the alarm condition
            if (conditionId != null && alarmCondition != null)
            {
                HashSet<string> conditionIds = GetFilteredRetainConditionIds();

                string key = conditionId.ToString();

                bool saved = conditionIds.Contains(key);

                if (saved)
                {
                    conditionIds.Remove(key);
                }

                if (passedFilter)
                {
                    // Archie - December 17 2024
                    // Requires discussion with Part 9 Editor
                    // if (alarmCondition.Retain.Value)
                    {
                        conditionIds.Add(key);
                    }
                }
                else if (saved)
                {
                    canSend = true;
                }
            }

            return canSend;
        }

        private HashSet<string> GetFilteredRetainConditionIds()
        {
            return m_filteredRetainConditionIds ??= [];
        }

        /// <summary>
        /// Used to check whether the item is ready to sample.
        /// </summary>
        public bool SamplingIntervalExpired()
        {
            lock (m_lock)
            {
                return TimeToNextSample <= 0;
            }
        }

        /// <summary>
        /// Increments the sample time to the next interval.
        /// </summary>
        private void IncrementSampleTime()
        {
            // update next sample time.
            long now = HiResClock.TickCount64;
            long samplingInterval = (long)m_samplingInterval;

            if (m_nextSamplingTime > 0)
            {
                long delta = now - m_nextSamplingTime;

                if (samplingInterval > 0 && delta >= 0)
                {
                    m_nextSamplingTime += ((delta / samplingInterval) + 1) * samplingInterval;
                }
            }
            // set sampling time based on current time.
            else
            {
                m_nextSamplingTime = now + samplingInterval;
            }
        }

        /// <summary>
        /// Publishes all available event notifications.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public virtual bool Publish(
            OperationContext context,
            Queue<EventFieldList> notifications,
            uint maxNotificationsPerPublish)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (notifications == null)
            {
                throw new ArgumentNullException(nameof(notifications));
            }

            lock (m_lock)
            {
                // check if the item reports events.
                if ((MonitoredItemType & MonitoredItemTypeMask.Events) == 0)
                {
                    return false;
                }

                // only publish if reporting.
                if (!IsReadyToPublish)
                {
                    return false;
                }

                // go to the next sampling interval.
                IncrementSampleTime();

                bool moreValuesToPublish = false;
                // publish events.
                if (m_eventQueueHandler != null)
                {
                    m_logger.LogTrace(
                        Utils.TraceMasks.OperationDetail,
                        "MONITORED ITEM: Publish(QueueSize={QueueSize})",
                        notifications.Count);

                    EventFieldList overflowEvent = null;

                    if (m_eventQueueHandler.Overflow)
                    {
                        // construct event.
                        var e = new EventQueueOverflowEventState(null);

                        var message = new TranslationInfo(
                            "EventQueueOverflowEventState",
                            "en-US",
                            "Events lost due to queue overflow.");

                        ISystemContext systemContext = new ServerSystemContext(m_server, context);

                        e.Initialize(
                            systemContext,
                            null,
                            EventSeverity.Low,
                            new LocalizedText(message));

                        e.SetChildValue(
                            systemContext,
                            BrowseNames.SourceNode,
                            ObjectIds.Server,
                            false);
                        e.SetChildValue(systemContext, BrowseNames.SourceName, "Internal", false);

                        // fetch the event fields.
                        overflowEvent = GetEventFields(
                            new FilterContext(
                                m_server.NamespaceUris,
                                m_server.TypeTree,
                                Session?.PreferredLocales,
                                m_server.Telemetry),
                            m_filterToUse as EventFilter,
                            e);
                    }

                    // place overflow event at the beginning of the queue.
                    if (overflowEvent != null && m_discardOldest)
                    {
                        notifications.Enqueue(overflowEvent);
                        maxNotificationsPerPublish--;
                    }
                    uint notificationCount = m_eventQueueHandler.Publish(
                        context,
                        notifications,
                        maxNotificationsPerPublish);

                    moreValuesToPublish = m_eventQueueHandler?.ItemsInQueue > 0;

                    // place overflow event at the end of the queue if queue is empty.
                    if (overflowEvent != null && !m_discardOldest)
                    {
                        if (notificationCount < maxNotificationsPerPublish)
                        {
                            notifications.Enqueue(overflowEvent);
                        }
                        else
                        {
                            moreValuesToPublish = true;
                        }
                    }

                    m_logger.LogTrace(
                        Utils.TraceMasks.OperationDetail,
                        "MONITORED ITEM: Publish(QueueSize={QueueSize})",
                        notifications.Count);
                }

                // reset state variables.
                m_readyToPublish = moreValuesToPublish;
                m_readyToTrigger = moreValuesToPublish;
                m_triggered = false;

                return moreValuesToPublish;
            }
        }

        /// <summary>
        /// Publishes all available data change notifications.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public virtual bool Publish(
            OperationContext context,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics,
            uint maxNotificationsPerPublish,
            ILogger logger)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (notifications == null)
            {
                throw new ArgumentNullException(nameof(notifications));
            }

            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            lock (m_lock)
            {
                // check if the item reports data changes.
                if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
                {
                    return false;
                }

                if (!IsReadyToPublish)
                {
                    if (!m_resendData)
                    {
                        return false;
                    }
                }
                else
                {
                    // pull any unprocessed data.
                    if (m_calculator != null && m_calculator.HasEndTimePassed(DateTime.UtcNow))
                    {
                        DataValue processedValue = m_calculator.GetProcessedValue(false);

                        while (processedValue != null)
                        {
                            AddValueToQueue(processedValue, null);
                        }

                        processedValue = m_calculator.GetProcessedValue(true);
                        AddValueToQueue(processedValue, null);
                    }

                    IncrementSampleTime();
                }
                // check if queueing enabled.
                if (m_dataChangeQueueHandler != null &&
                    (!m_resendData || m_dataChangeQueueHandler.ItemsInQueue != 0))
                {
                    uint notificationCount = 0;
                    while (
                        notificationCount < maxNotificationsPerPublish &&
                        m_dataChangeQueueHandler.PublishSingleValue(
                            out DataValue value,
                            out ServiceResult error))
                    {
                        Publish(context, notifications, diagnostics, value, error);

                        notificationCount++;

                        if (m_resendData)
                        {
                            break;
                        }
                    }
                }
                // publish last value if no queuing or no items are queued
                else
                {
                    m_logger.LogTrace(
                        "DEQUEUE VALUE: Value={Value} CODE={Code}<{Code:X8}> OVERFLOW={Overflow}",
                        m_lastValue.WrappedValue,
                        m_lastValue.StatusCode.Code,
                        m_lastValue.StatusCode.Code,
                        m_lastValue.StatusCode.Overflow);
                    Publish(context, notifications, diagnostics, m_lastValue, m_lastError);
                }

                bool moreValuesToPublish = m_dataChangeQueueHandler?.ItemsInQueue > 0;

                // reset state variables.
                m_readyToPublish = moreValuesToPublish;
                m_readyToTrigger = moreValuesToPublish;
                m_resendData = false;
                m_triggered = false;

                return moreValuesToPublish;
            }
        }

        /// <summary>
        /// Publishes a single data change notifications.
        /// </summary>
        protected virtual bool Publish(
            OperationContext context,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics,
            DataValue value,
            ServiceResult error)
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
                        error.SymbolicId,
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
                    error = new ServiceResult(
                        error.StatusCode.SetStructureChanged(true),
                        error.SymbolicId,
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

            ServerUtils.ReportPublishValue(NodeId, Id, item.Value);
            notifications.Enqueue(item);

            // update diagnostic info.
            DiagnosticInfo diagnosticInfo = null;

            if ((DiagnosticsMasks & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_server, context, error, m_logger);
            }

            diagnostics.Enqueue(diagnosticInfo);

            return false;
        }

        /// <summary>
        /// The object to call when item is ready to publish.
        /// </summary>
        public ISubscription SubscriptionCallback
        {
            get
            {
                lock (m_lock)
                {
                    return m_subscription;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_subscription = value;
                }
            }
        }

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

                    // node manager responsible for ensuring correct sampling.
                    if (m_sourceSamplingInterval > 0)
                    {
                        return 0;
                    }

                    long now = HiResClock.TickCount64;

                    if (m_nextSamplingTime <= now)
                    {
                        return 0;
                    }

                    return (int)(m_nextSamplingTime - now);
                }
            }
        }

        /// <inheritdoc/>
        public bool IsDurable { get; }

        /// <inheritdoc/>
        public IStoredMonitoredItem ToStorableMonitoredItem()
        {
            return new StoredMonitoredItem
            {
                SamplingInterval = m_samplingInterval,
                SourceSamplingInterval = m_sourceSamplingInterval,
                SubscriptionId = SubscriptionId,
                QueueSize = QueueSize,
                AlwaysReportUpdates = AlwaysReportUpdates,
                AttributeId = AttributeId,
                ClientHandle = ClientHandle,
                DiagnosticsMasks = DiagnosticsMasks,
                DiscardOldest = m_discardOldest,
                IsDurable = IsDurable,
                Encoding = DataEncoding,
                FilterToUse = m_filterToUse,
                Id = Id,
                IndexRange = m_indexRange,
                LastError = m_lastError,
                LastValue = m_lastValue,
                MonitoringMode = MonitoringMode,
                NodeId = NodeId,
                OriginalFilter = Filter,
                Range = m_range,
                TimestampsToReturn = m_timestampsToReturn,
                TypeMask = MonitoredItemType,
                ParsedIndexRange = m_parsedIndexRange
            };
        }

        /// <summary>
        /// Applies the filter to value to determine if the new value should be kept.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        protected virtual bool ApplyFilter(DataValue value, ServiceResult error)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return ValueChanged(
                value,
                error,
                m_lastValue,
                m_lastError,
                m_filterToUse as DataChangeFilter,
                m_range);
        }

        /// <summary>
        /// Applies the filter to value to determine if the new value should be kept.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        public static bool ValueChanged(
            DataValue value,
            ServiceResult error,
            DataValue lastValue,
            ServiceResult lastError,
            DataChangeFilter filter,
            double range)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            // select default data change filters.
            double deadband = 0.0;
            DeadbandType deadbandType = DeadbandType.None;
            DataChangeTrigger trigger = DataChangeTrigger.StatusValue;

            // apply filter.
            if (filter != null)
            {
                trigger = filter.Trigger;
                deadbandType = (DeadbandType)(int)filter.DeadbandType;
                deadband = filter.DeadbandValue;

                // when deadband is used and the trigger is StatusValueTimestamp, then it should behave as if trigger is StatusValue.
                if ((deadbandType != DeadbandType.None) &&
                    (trigger == DataChangeTrigger.StatusValueTimestamp))
                {
                    trigger = DataChangeTrigger.StatusValue;
                }
            }

            // get the current status.
            uint status = StatusCodes.Good;

            if (error != null)
            {
                status = error.StatusCode.Code;
            }
            else if (lastValue != null)
            {
                status = value.StatusCode.Code;
            }

            // get the last status.
            uint lastStatus = StatusCodes.Good;

            if (lastError != null)
            {
                lastStatus = lastError.StatusCode.Code;
            }
            else if (lastValue != null)
            {
                lastStatus = lastValue.StatusCode.Code;
            }

            // value changed if any status change occurrs.
            if (status != lastStatus)
            {
                return true;
            }

            // value changed if only one is null.
            if (value == null || lastValue == null)
            {
                return lastValue != null || value != null;
            }

            // check if timestamp has changed.
            if (trigger == DataChangeTrigger.StatusValueTimestamp &&
                lastValue.SourceTimestamp != value.SourceTimestamp)
            {
                return true;
            }

            // check if value changes are ignored.
            if (trigger == DataChangeTrigger.Status)
            {
                return false;
            }

            // check if reference to same object.
            if (!Equals(lastValue.Value, value.Value, deadbandType, deadband, range))
            {
                return true;
            }

            // must be equal.
            return false;
        }

        /// <summary>
        /// Checks if the two values are equal.
        /// </summary>
        protected static bool Equals(
            object value1,
            object value2,
            DeadbandType deadbandType,
            double deadband,
            double range)
        {
            // check if reference to same object.
            if (ReferenceEquals(value1, value2))
            {
                return true;
            }

            // check for invalid values.
            if (value1 == null || value2 == null)
            {
                return value1 == value2;
            }

            // check for type change.
            if (value1.GetType() != value2.GetType())
            {
                return false;
            }

            // special case NaN is always not equal
            if (value1.Equals(float.NaN) ||
                value1.Equals(double.NaN) ||
                value2.Equals(float.NaN) ||
                value2.Equals(double.NaN))
            {
                return false;
            }

            // check if values are equal.
            if (value1.Equals(value2))
            {
                return true;
            }

            if (value1 is not Array array1 || value2 is not Array array2)
            {
                if (value1 is XmlElement xmlElement1 && value2 is XmlElement xmlElement2)
                {
                    return xmlElement1.OuterXml
                        .Equals(xmlElement2.OuterXml, StringComparison.Ordinal);
                }

                // nothing more to do if no deadband.
                if (deadbandType == DeadbandType.None)
                {
                    return false;
                }

                // check deadband.
                return !ExceedsDeadband(value1, value2, deadbandType, deadband, range);
            }

            // compare lengths.
            if (array1.Length != array2.Length)
            {
                return false;
            }

            // compare each element.
            bool isVariant = array1.GetType().GetElementType() == typeof(Variant);

            for (int ii = 0; ii < array1.Length; ii++)
            {
                object element1 = array1.GetValue(ii);
                object element2 = array2.GetValue(ii);

                if (isVariant)
                {
                    element1 = ((Variant)element1).Value;
                    element2 = ((Variant)element2).Value;
                }

                if (!Equals(element1, element2, deadbandType, deadband, range))
                {
                    return false;
                }
            }

            // must be equal.
            return true;
        }

        /// <summary>
        /// Returns true if the deadband was exceeded.
        /// </summary>
        protected static bool ExceedsDeadband(
            object value1,
            object value2,
            DeadbandType deadbandType,
            double deadband,
            double range)
        {
            // cannot convert doubles safely to decimals.
            if (value1 is double x)
            {
                return ExceedsDeadband((double)x, (double)value2, deadbandType, deadband, range);
            }

            try
            {
                decimal decimal1 = Convert.ToDecimal(value1, CultureInfo.InvariantCulture);
                decimal decimal2 = Convert.ToDecimal(value2, CultureInfo.InvariantCulture);
                decimal baseline = 1;

                if (deadbandType == DeadbandType.Percent)
                {
                    baseline = ((decimal)range) / 100;
                }

                if (baseline > 0 && Math.Abs((decimal1 - decimal2) / baseline) <= (decimal)deadband)
                {
                    return false;
                }
            }
            catch
            {
                // treat all conversion errors as evidence that the deadband was exceeded.
            }

            return true;
        }

        /// <summary>
        /// Returns true if the deadband was exceeded.
        /// </summary>
        private static bool ExceedsDeadband(
            double value1,
            double value2,
            DeadbandType deadbandType,
            double deadband,
            double range)
        {
            double baseline = 1;

            if (deadbandType == DeadbandType.Percent)
            {
                baseline = range / 100;
            }

            return baseline <= 0 || Math.Abs((value1 - value2) / baseline) > deadband;
        }

        /// <summary>
        /// Clears and re-initializes the queue if the monitoring parameters changed.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void InitializeQueue()
        {
            switch (MonitoringMode)
            {
                case MonitoringMode.Reporting:
                case MonitoringMode.Sampling:
                    // check if queuing is disabled.
                    if (QueueSize == 0)
                    {
                        if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                        {
                            QueueSize = 1;
                        }

                        if ((MonitoredItemType & MonitoredItemTypeMask.Events) != 0)
                        {
                            QueueSize = 1000;
                        }
                    }

                    // create data queue.
                    if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                    {
                        if (QueueSize <= 1)
                        {
                            Utils.SilentDispose(m_dataChangeQueueHandler);
                            m_dataChangeQueueHandler = null;
                            break; // queueing is disabled
                        }

                        bool queueLastValue = false;

                        if (m_dataChangeQueueHandler == null)
                        {
                            m_dataChangeQueueHandler = new DataChangeQueueHandler(
                                Id,
                                IsDurable,
                                m_monitoredItemQueueFactory,
                                m_server.Telemetry,
                                QueueOverflowHandler);
                            queueLastValue = true;
                        }

                        m_dataChangeQueueHandler.SetQueueSize(
                            QueueSize,
                            m_discardOldest,
                            DiagnosticsMasks);
                        m_dataChangeQueueHandler.SetSamplingInterval(m_samplingInterval);

                        if (queueLastValue && m_lastValue != null)
                        {
                            m_dataChangeQueueHandler.QueueValue(m_lastValue, m_lastError);
                        }
                    }
                    else // create event queue.
                    {
                        m_eventQueueHandler ??= new EventQueueHandler(
                            IsDurable,
                            m_monitoredItemQueueFactory,
                            Id,
                            m_server.Telemetry);
                        m_eventQueueHandler.SetQueueSize(QueueSize, m_discardOldest);
                    }
                    break;
                case MonitoringMode.Disabled:
                    Utils.SilentDispose(m_eventQueueHandler);
                    m_eventQueueHandler = null;
                    Utils.SilentDispose(m_dataChangeQueueHandler);
                    m_dataChangeQueueHandler = null;
                    break;
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        $"Unexpected MonitoringMode {MonitoringMode}");
            }
        }

        /// <summary>
        /// Restore a persitent queue after a restart
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void RestoreQueue()
        {
            switch (MonitoringMode)
            {
                case MonitoringMode.Reporting:
                case MonitoringMode.Sampling:
                    // check if queuing is disabled.
                    if (QueueSize == 0)
                    {
                        if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                        {
                            QueueSize = 1;
                        }

                        if ((MonitoredItemType & MonitoredItemTypeMask.Events) != 0)
                        {
                            QueueSize = 1000;
                        }
                    }

                    // create data queue.
                    if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                    {
                        if (QueueSize <= 1)
                        {
                            break; // queueing is disabled
                        }
                        IDataChangeMonitoredItemQueue restoredQueue = null;
                        try
                        {
                            restoredQueue = m_subscriptionStore.RestoreDataChangeMonitoredItemQueue(
                                Id);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(
                                ex,
                                "Failed to restore queue for monitored item with id {MonitoredItemId}",
                                Id);
                        }

                        if (restoredQueue != null)
                        {
                            // initialize with existing queue
                            m_dataChangeQueueHandler = new DataChangeQueueHandler(
                                restoredQueue,
                                m_discardOldest,
                                m_samplingInterval,
                                m_server.Telemetry,
                                QueueOverflowHandler);
                        }
                        else
                        {
                            // create new queue
                            m_dataChangeQueueHandler = new DataChangeQueueHandler(
                                Id,
                                IsDurable,
                                m_monitoredItemQueueFactory,
                                m_server.Telemetry,
                                QueueOverflowHandler);

                            m_dataChangeQueueHandler.SetQueueSize(
                                QueueSize,
                                m_discardOldest,
                                DiagnosticsMasks);
                            m_dataChangeQueueHandler.SetSamplingInterval(m_samplingInterval);
                        }
                    }
                    else // create event queue.
                    {
                        IEventMonitoredItemQueue restoredQueue = null;
                        try
                        {
                            restoredQueue = m_subscriptionStore.RestoreEventMonitoredItemQueue(Id);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(
                                ex,
                                "Failed to restore queue for monitored item with id {Id}",
                                Id);
                        }
                        if (restoredQueue != null)
                        {
                            // initialize with existing queue
                            m_eventQueueHandler = new EventQueueHandler(
                                restoredQueue,
                                m_discardOldest,
                                m_server.Telemetry);
                        }
                        else
                        {
                            // create new queue
                            m_eventQueueHandler = new EventQueueHandler(
                                IsDurable,
                                m_monitoredItemQueueFactory,
                                Id,
                                m_server.Telemetry);
                            m_eventQueueHandler.SetQueueSize(QueueSize, m_discardOldest);
                        }
                    }
                    break;
                case MonitoringMode.Disabled:
                    break;
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        $"Unexpected MonitoringMode {MonitoringMode}");
            }
        }

        /// <summary>
        /// Update the overflow count.
        /// </summary>
        private void QueueOverflowHandler()
        {
            m_subscription?.QueueOverflowHandler();
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
                Utils.SilentDispose(m_dataChangeQueueHandler);
                Utils.SilentDispose(m_eventQueueHandler);
            }
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private IServerInternal m_server;
        private string m_indexRange;
        private NumericRange m_parsedIndexRange;
        private TimestampsToReturn m_timestampsToReturn;
        private MonitoringFilter m_filterToUse;
        private double m_range;
        private double m_samplingInterval;
        private bool m_discardOldest;
        private int m_sourceSamplingInterval;
        private DataValue m_lastValue;
        private ServiceResult m_lastError;
        private long m_nextSamplingTime;
        private readonly IMonitoredItemQueueFactory m_monitoredItemQueueFactory;
        private DataChangeQueueHandler m_dataChangeQueueHandler;
        private EventQueueHandler m_eventQueueHandler;
        private readonly ISubscriptionStore m_subscriptionStore;
        private bool m_readyToPublish;
        private bool m_readyToTrigger;
        private bool m_semanticsChanged;
        private bool m_structureChanged;
        private ISubscription m_subscription;
        private ServiceResult m_samplingError;
        private IAggregateCalculator m_calculator;
        private bool m_triggered;
        private bool m_resendData;
        private HashSet<string> m_filteredRetainConditionIds;
    }
}
