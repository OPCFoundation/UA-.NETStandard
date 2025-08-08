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
using System.Xml;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A handle that describes how to access a node/attribute via an i/o manager.
    /// </summary>
    public class MonitoredItem : IEventMonitoredItem, ISampledDataChangeMonitoredItem, ITriggeredMonitoredItem
    {
        #region Constructors
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

            Initialize();

            m_server = server;
            m_nodeManager = nodeManager;
            m_managerHandle = managerHandle;
            m_subscriptionId = subscriptionId;
            m_id = id;
            m_nodeId = itemToMonitor.NodeId;
            m_attributeId = itemToMonitor.AttributeId;
            m_indexRange = itemToMonitor.IndexRange;
            m_parsedIndexRange = itemToMonitor.ParsedIndexRange;
            m_encoding = itemToMonitor.DataEncoding;
            m_diagnosticsMasks = diagnosticsMasks;
            m_timestampsToReturn = timestampsToReturn;
            m_monitoringMode = monitoringMode;
            m_clientHandle = clientHandle;
            m_originalFilter = originalFilter;
            m_filterToUse = filterToUse;
            m_range = 0;
            m_samplingInterval = samplingInterval;
            m_queueSize = queueSize;
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
                Utils.LogError("Durable subscription was create but no MonitoredItemQueueFactory that supports durable queues was registered, monitored item with id {id} could not be created", id);
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
                m_nodeId,
                m_id,
                m_samplingInterval,
                m_queueSize,
                m_discardOldest,
                m_filterToUse,
                m_monitoringMode);

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

            Initialize();

            m_server = server;
            m_nodeManager = nodeManager;
            m_managerHandle = managerHandle;
            m_subscriptionId = storedMonitoredItem.SubscriptionId;
            m_id = storedMonitoredItem.Id;
            m_nodeId = storedMonitoredItem.NodeId;
            m_attributeId = storedMonitoredItem.AttributeId;
            m_indexRange = storedMonitoredItem.IndexRange;
            m_parsedIndexRange = storedMonitoredItem.ParsedIndexRange;
            m_encoding = storedMonitoredItem.Encoding;
            m_diagnosticsMasks = storedMonitoredItem.DiagnosticsMasks;
            m_timestampsToReturn = storedMonitoredItem.TimestampsToReturn;
            m_monitoringMode = storedMonitoredItem.MonitoringMode;
            m_clientHandle = storedMonitoredItem.ClientHandle;
            m_originalFilter = storedMonitoredItem.OriginalFilter;
            m_filterToUse = storedMonitoredItem.FilterToUse;
            m_range = storedMonitoredItem.Range;
            m_samplingInterval = storedMonitoredItem.SamplingInterval;
            m_queueSize = storedMonitoredItem.QueueSize;
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
                m_nodeId,
                m_id,
                m_samplingInterval,
                m_queueSize,
                m_discardOldest,
                m_filterToUse,
                m_monitoringMode);

            RestoreQueue();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_server = null;
            m_nodeManager = null;
            m_managerHandle = null;
            m_subscriptionId = 0;
            m_id = 0;
            m_nodeId = null;
            m_attributeId = 0;
            m_indexRange = null;
            m_parsedIndexRange = NumericRange.Empty;
            m_encoding = null;
            m_clientHandle = 0;
            m_monitoringMode = MonitoringMode.Disabled;
            m_samplingInterval = 0;
            m_queueSize = 0;
            m_discardOldest = true;
            m_originalFilter = null;
            m_lastValue = null;
            m_lastError = null;
            m_readyToPublish = false;
            m_readyToTrigger = false;
            m_sourceSamplingInterval = 0;
            m_samplingError = ServiceResult.Good;
            m_resendData = false;
        }
        #endregion

        #region IMonitoredItem Members
        /// <summary>
        /// The node manager that created the item.
        /// </summary>
        public INodeManager NodeManager => m_nodeManager;

        /// <summary>
        /// The handle assigned by the node manager when it created the item.
        /// </summary>
        public object ManagerHandle => m_managerHandle;

        /// <summary>
        /// The identifier for the subscription that owns the monitored item.
        /// </summary>
        public uint SubscriptionId => m_subscriptionId;

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
                    ServerUtils.EventLog.MonitoredItemReady(m_id, "FALSE");
                    return false;
                }

                // check if it has been triggered.
                if (m_monitoringMode != MonitoringMode.Disabled && m_triggered)
                {
                    ServerUtils.EventLog.MonitoredItemReady(m_id, "TRIGGERED");
                    return true;
                }

                // check if monitoring was turned off.
                if (m_monitoringMode != MonitoringMode.Reporting)
                {
                    ServerUtils.EventLog.MonitoredItemReady(m_id, "FALSE");
                    return false;
                }

                if (m_sourceSamplingInterval == 0)
                {
                    // re-queue if too little time has passed since the last publish, in case it doesn't ResendData
                    long now = HiResClock.TickCount64;

                    if (m_nextSamplingTime > now)
                    {
                        ServerUtils.EventLog.MonitoredItemReady(m_id, Utils.Format("FALSE {0}ms", m_nextSamplingTime - now));
                        return false;
                    }
                }
                ServerUtils.EventLog.MonitoredItemReady(m_id, "NORMAL");
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
                    if (m_monitoringMode == MonitoringMode.Disabled)
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
                if (m_monitoringMode == MonitoringMode.Reporting &&
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
                    Utils.LogTrace(Utils.TraceMasks.OperationDetail, "SetTriggered[{0}]", m_id);
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
        public MonitoringFilter Filter => m_originalFilter;

        /// <summary>
        /// The event filter used by the monitored item.
        /// </summary>
        public EventFilter EventFilter => m_originalFilter as EventFilter;

        /// <summary>
        /// The data change filter used by the monitored item.
        /// </summary>
        public DataChangeFilter DataChangeFilter => m_originalFilter as DataChangeFilter;

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
        public uint Id => m_id;

        /// <summary>
        /// The identifier for the client handle assigned to the monitored item.
        /// </summary>
        public uint ClientHandle => m_clientHandle;

        /// <summary>
        /// The node id being monitored.
        /// </summary>
        public NodeId NodeId => m_nodeId;

        /// <summary>
        /// The attribute being monitored.
        /// </summary>
        public uint AttributeId => m_attributeId;

        /// <summary>
        /// The current monitoring mode for the item
        /// </summary>
        public MonitoringMode MonitoringMode => m_monitoringMode;

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
        public uint QueueSize => m_queueSize;

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
        public DiagnosticsMasks DiagnosticsMasks => m_diagnosticsMasks;

        /// <summary>
        /// The index range requested by the monitored item.
        /// </summary>
        public NumericRange IndexRange => m_parsedIndexRange;

        /// <summary>
        /// The data encoding requested by the monitored item.
        /// </summary>
        public QualifiedName DataEncoding => m_encoding;

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
                return new ReadValueId {
                    NodeId = m_nodeId,
                    AttributeId = m_attributeId,
                    IndexRange = m_indexRange,
                    ParsedIndexRange = m_parsedIndexRange,
                    DataEncoding = m_encoding,
                    Handle = m_managerHandle
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
                result = new MonitoredItemCreateResult {
                    MonitoredItemId = m_id,
                    RevisedSamplingInterval = m_samplingInterval,
                    RevisedQueueSize = m_queueSize,
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
                result = new MonitoredItemModifyResult {
                    RevisedSamplingInterval = m_samplingInterval,
                    RevisedQueueSize = m_queueSize,
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
                m_diagnosticsMasks = diagnosticsMasks;
                m_timestampsToReturn = timestampsToReturn;
                m_clientHandle = clientHandle;
                m_discardOldest = discardOldest;

                m_originalFilter = originalFilter;
                m_filterToUse = filterToUse;

                if (range != null)
                {
                    m_range = range.High - range.Low;
                }

                SetSamplingInterval(samplingInterval);
                m_queueSize = queueSize;

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

                    if (match && existingFilter.ProcessingInterval != aggregateFilter.ProcessingInterval)
                    {
                        match = false;
                    }

                    if (match && existingFilter.StartTime != aggregateFilter.StartTime)
                    {
                        match = false;
                    }

                    if (match && !existingFilter.AggregateConfiguration.IsEqual(aggregateFilter.AggregateConfiguration))
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
                    m_nodeId,
                    m_id,
                    m_samplingInterval,
                    m_queueSize,
                    m_discardOldest,
                    m_filterToUse,
                    m_monitoringMode);

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
                MonitoringMode previousMode = m_monitoringMode;

                if (previousMode == monitoringMode)
                {
                    return previousMode;
                }

                Utils.LogTrace("MONITORING MODE[{0}] {1} -> {2}", m_id, m_monitoringMode, monitoringMode);

                if (previousMode == MonitoringMode.Disabled)
                {
                    m_nextSamplingTime = HiResClock.TickCount64;
                    m_lastError = null;
                    m_lastValue = null;
                }

                m_monitoringMode = monitoringMode;

                if (monitoringMode == MonitoringMode.Disabled)
                {
                    m_readyToPublish = false;
                    m_readyToTrigger = false;
                    m_triggered = false;
                }

                // report change to item state.
                ServerUtils.ReportModifyMonitoredItem(
                    m_nodeId,
                    m_id,
                    m_samplingInterval,
                    m_queueSize,
                    m_discardOldest,
                    m_filterToUse,
                    m_monitoringMode);

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
                if (m_monitoringMode == MonitoringMode.Disabled)
                {
                    return;
                }

                // make a shallow copy of the value.
                if (value != null)
                {
                    Utils.LogTrace(Utils.TraceMasks.OperationDetail, "RECEIVED VALUE[{0}] Value={1}", this.m_id, value.WrappedValue);

                    value = new DataValue {
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
                    value = new DataValue();
                    value.StatusCode = error.StatusCode;
                    value.SourceTimestamp = DateTime.UtcNow;
                    value.ServerTimestamp = DateTime.UtcNow;
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
                        Utils.LogTrace("Value received out of order: {1}, ServerHandle={0}",
                            m_id, value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
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
                    ServerUtils.ReportFilteredValue(m_nodeId, m_id, value);
                    return;
                }

                ServerUtils.ReportQueuedValue(m_nodeId, m_id, value);

                // add the value to the queue.
                AddValueToQueue(value, error);
            }
        }

        /// <summary>
        /// Adds a value to the queue.
        /// </summary>
        private void AddValueToQueue(DataValue value, ServiceResult error)
        {
            if (m_queueSize > 1)
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
            ServerUtils.EventLog.QueueValue(m_id, m_lastValue.WrappedValue, m_lastValue.StatusCode);
        }

        /// <summary>
        /// Whether the item is monitoring all events produced by the server.
        /// </summary>
        public bool MonitoringAllEvents => this.m_nodeId == ObjectIds.Server;

        /// <summary>
        /// Fetches the event fields from the event.
        /// </summary>
        private EventFieldList GetEventFields(FilterContext context, EventFilter filter, IFilterTarget instance)
        {
            // fetch the event fields.
            var fields = new EventFieldList();

            fields.ClientHandle = m_clientHandle;
            fields.Handle = instance;

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
                    var text = value as LocalizedText;

                    if (text != null)
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
                var context = new FilterContext(m_server.NamespaceUris, m_server.TypeTree, Session?.PreferredLocales);

                // event filter must be specified.
                if (!(m_filterToUse is EventFilter filter))
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
        /// <returns></returns>
        protected bool CanSendFilteredAlarm(FilterContext context, EventFilter filter, IFilterTarget instance)
        {
            bool passedFilter = filter.WhereClause.Evaluate(context, instance);

            ConditionState alarmCondition = null;
            NodeId conditionId = null;
            var instanceStateSnapshot = instance as InstanceStateSnapshot;
            if (instanceStateSnapshot != null)
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
            return m_filteredRetainConditionIds ??= new HashSet<string>();
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
        public virtual bool Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish)
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
                    Utils.LogTrace(Utils.TraceMasks.OperationDetail, "MONITORED ITEM: Publish(QueueSize={0})", notifications.Count);

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

                        e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                        e.SetChildValue(systemContext, BrowseNames.SourceName, "Internal", false);

                        // fetch the event fields.
                        overflowEvent = GetEventFields(
                            new FilterContext(m_server.NamespaceUris, m_server.TypeTree, Session?.PreferredLocales),
                            m_filterToUse as EventFilter,
                            e);
                    }

                    // place overflow event at the beginning of the queue.
                    if (overflowEvent != null && m_discardOldest)
                    {
                        notifications.Enqueue(overflowEvent);
                        maxNotificationsPerPublish--;
                    }
                    uint notificationCount = m_eventQueueHandler.Publish(context, notifications, maxNotificationsPerPublish);

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

                    Utils.LogTrace(Utils.TraceMasks.OperationDetail, "MONITORED ITEM: Publish(QueueSize={0})", notifications.Count);
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
        public virtual bool Publish(
            OperationContext context,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics,
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
                if (m_dataChangeQueueHandler != null && (!m_resendData || m_dataChangeQueueHandler.ItemsInQueue != 0))
                {
                    DataValue value = null;
                    ServiceResult error = null;

                    uint notificationCount = 0;
                    while (notificationCount < maxNotificationsPerPublish && m_dataChangeQueueHandler.PublishSingleValue(out value, out error))
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
                    ServerUtils.EventLog.DequeueValue(m_lastValue.WrappedValue, m_lastValue.StatusCode);
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
            var item = new MonitoredItemNotification();

            item.ClientHandle = m_clientHandle;
            item.Value = value;

            // apply timestamp filter.
            if (m_timestampsToReturn != TimestampsToReturn.Server && m_timestampsToReturn != TimestampsToReturn.Both)
            {
                item.Value.ServerTimestamp = DateTime.MinValue;
            }

            if (m_timestampsToReturn != TimestampsToReturn.Source && m_timestampsToReturn != TimestampsToReturn.Both)
            {
                item.Value.SourceTimestamp = DateTime.MinValue;
            }

            ServerUtils.ReportPublishValue(m_nodeId, m_id, item.Value);
            notifications.Enqueue(item);

            // update diagnostic info.
            DiagnosticInfo diagnosticInfo = null;

            if ((m_diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_server, context, error);
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
                    if (m_monitoringMode == MonitoringMode.Disabled)
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
            return new StoredMonitoredItem {
                SamplingInterval = m_samplingInterval,
                SourceSamplingInterval = m_sourceSamplingInterval,
                SubscriptionId = m_subscriptionId,
                QueueSize = m_queueSize,
                AlwaysReportUpdates = AlwaysReportUpdates,
                AttributeId = m_attributeId,
                ClientHandle = m_clientHandle,
                DiagnosticsMasks = m_diagnosticsMasks,
                DiscardOldest = m_discardOldest,
                IsDurable = IsDurable,
                Encoding = m_encoding,
                FilterToUse = m_filterToUse,
                Id = m_id,
                IndexRange = m_indexRange,
                LastError = m_lastError,
                LastValue = m_lastValue,
                MonitoringMode = m_monitoringMode,
                NodeId = m_nodeId,
                OriginalFilter = m_originalFilter,
                Range = m_range,
                TimestampsToReturn = m_timestampsToReturn,
                TypeMask = MonitoredItemType,
                ParsedIndexRange = m_parsedIndexRange
            };
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Applies the filter to value to determine if the new value should be kept.
        /// </summary>
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
                if ((deadbandType != DeadbandType.None) && (trigger == DataChangeTrigger.StatusValueTimestamp))
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
            if (trigger == DataChangeTrigger.StatusValueTimestamp && lastValue.SourceTimestamp != value.SourceTimestamp)
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
        protected static bool Equals(object value1, object value2, DeadbandType deadbandType, double deadband, double range)
        {
            // check if reference to same object.
            if (Object.ReferenceEquals(value1, value2))
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

            // check for arrays.
            var array1 = value1 as Array;
            var array2 = value2 as Array;

            if (array1 == null || array2 == null)
            {
                var xmlElement1 = value1 as XmlElement;
                var xmlElement2 = value2 as XmlElement;

                if (xmlElement1 != null && xmlElement2 != null)
                {
                    return xmlElement1.OuterXml.Equals(xmlElement2.OuterXml, StringComparison.Ordinal);
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
        protected static bool ExceedsDeadband(object value1, object value2, DeadbandType deadbandType, double deadband, double range)
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
        private static bool ExceedsDeadband(double value1, double value2, DeadbandType deadbandType, double deadband, double range)
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
        protected void InitializeQueue()
        {
            switch (m_monitoringMode)
            {
                case MonitoringMode.Disabled:
                default:
                    Utils.SilentDispose(m_eventQueueHandler);
                    m_eventQueueHandler = null;
                    Utils.SilentDispose(m_dataChangeQueueHandler);
                    m_dataChangeQueueHandler = null;
                    break;

                case MonitoringMode.Reporting:
                case MonitoringMode.Sampling:
                    // check if queuing is disabled.
                    if (m_queueSize == 0)
                    {
                        if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                        {
                            m_queueSize = 1;
                        }

                        if ((MonitoredItemType & MonitoredItemTypeMask.Events) != 0)
                        {
                            m_queueSize = 1000;
                        }
                    }

                    // create data queue.
                    if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                    {
                        if (m_queueSize <= 1)
                        {
                            Utils.SilentDispose(m_dataChangeQueueHandler);
                            m_dataChangeQueueHandler = null;
                            break; // queueing is disabled
                        }

                        bool queueLastValue = false;

                        if (m_dataChangeQueueHandler == null)
                        {
                            m_dataChangeQueueHandler = new DataChangeQueueHandler(Id, IsDurable, m_monitoredItemQueueFactory, QueueOverflowHandler);
                            queueLastValue = true;
                        }

                        m_dataChangeQueueHandler.SetQueueSize(m_queueSize, m_discardOldest, m_diagnosticsMasks);
                        m_dataChangeQueueHandler.SetSamplingInterval(m_samplingInterval);

                        if (queueLastValue && m_lastValue != null)
                        {
                            m_dataChangeQueueHandler.QueueValue(m_lastValue, m_lastError);
                        }
                    }
                    else // create event queue.
                    {
                        (m_eventQueueHandler ??= new EventQueueHandler(IsDurable, m_monitoredItemQueueFactory, Id)).SetQueueSize(m_queueSize, m_discardOldest);
                    }

                    break;
            }
        }

        /// <summary>
        /// Restore a persitent queue after a restart
        /// </summary>
        protected void RestoreQueue()
        {
            switch (m_monitoringMode)
            {
                case MonitoringMode.Disabled:
                default:
                    break;

                case MonitoringMode.Reporting:
                case MonitoringMode.Sampling:
                    // check if queuing is disabled.
                    if (m_queueSize == 0)
                    {
                        if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                        {
                            m_queueSize = 1;
                        }

                        if ((MonitoredItemType & MonitoredItemTypeMask.Events) != 0)
                        {
                            m_queueSize = 1000;
                        }
                    }

                    // create data queue.
                    if (MonitoredItemType == MonitoredItemTypeMask.DataChange)
                    {
                        if (m_queueSize <= 1)
                        {
                            break; // queueing is disabled
                        }
                        IDataChangeMonitoredItemQueue restoredQueue = null;
                        try
                        {
                            restoredQueue = m_subscriptionStore.RestoreDataChangeMonitoredItemQueue(m_id);
                        }
                        catch (Exception ex)
                        {
                            Utils.LogError(ex, "Failed to restore queue for monitored item with id {0}", Id);
                        }

                        if (restoredQueue != null)
                        {
                            // initialize with existing queue
                            m_dataChangeQueueHandler = new DataChangeQueueHandler(restoredQueue, m_discardOldest, m_samplingInterval, QueueOverflowHandler);
                        }
                        else
                        {
                            // create new queue
                            m_dataChangeQueueHandler = new DataChangeQueueHandler(Id, IsDurable, m_monitoredItemQueueFactory, QueueOverflowHandler);

                            m_dataChangeQueueHandler.SetQueueSize(m_queueSize, m_discardOldest, m_diagnosticsMasks);
                            m_dataChangeQueueHandler.SetSamplingInterval(m_samplingInterval);
                        }
                    }
                    else // create event queue.
                    {
                        IEventMonitoredItemQueue restoredQueue = null;
                        try
                        {
                            restoredQueue = m_subscriptionStore.RestoreEventMonitoredItemQueue(m_id);
                        }
                        catch (Exception ex)
                        {
                            Utils.LogError(ex, "Failed to restore queue for monitored item with id {0}", Id);
                        }
                        if (restoredQueue != null)
                        {
                            // initialize with existing queue
                            m_eventQueueHandler = new EventQueueHandler(restoredQueue, m_discardOldest);
                        }
                        else
                        {
                            // create new queue
                            m_eventQueueHandler = new EventQueueHandler(IsDurable, m_monitoredItemQueueFactory, Id);
                            m_eventQueueHandler.SetQueueSize(m_queueSize, m_discardOldest);
                        }
                    }

                    break;
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

        #endregion

        #region Private Members
        private readonly object m_lock = new object();
        private IServerInternal m_server;
        private INodeManager m_nodeManager;
        private object m_managerHandle;
        private uint m_subscriptionId;
        private uint m_id;
        private NodeId m_nodeId;
        private uint m_attributeId;
        private string m_indexRange;
        private NumericRange m_parsedIndexRange;
        private QualifiedName m_encoding;
        private DiagnosticsMasks m_diagnosticsMasks;
        private TimestampsToReturn m_timestampsToReturn;
        private uint m_clientHandle;
        private MonitoringMode m_monitoringMode;
        private MonitoringFilter m_originalFilter;
        private MonitoringFilter m_filterToUse;
        private double m_range;
        private double m_samplingInterval;
        private uint m_queueSize;
        private bool m_discardOldest;
        private int m_sourceSamplingInterval;
        private DataValue m_lastValue;
        private ServiceResult m_lastError;
        private long m_nextSamplingTime;
        private readonly IMonitoredItemQueueFactory m_monitoredItemQueueFactory;
        private IDataChangeQueueHandler m_dataChangeQueueHandler;
        private IEventQueueHandler m_eventQueueHandler;
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
        #endregion
    }
}
