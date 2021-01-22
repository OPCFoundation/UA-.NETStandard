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
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using static Opc.Ua.Utils;

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
            object mangerHandle,
            uint subscriptionId,
            uint id,
            Session session,
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
            double sourceSamplingInterval)
        {
            if (itemToMonitor == null) throw new ArgumentNullException(nameof(itemToMonitor));

            Initialize();

            m_server = server;
            m_nodeManager = nodeManager;
            m_managerHandle = mangerHandle;
            m_subscriptionId = subscriptionId;
            m_id = id;
            m_session = session;
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
            m_alwaysReportUpdates = false;

            m_typeMask = MonitoredItemTypeMask.DataChange;

            if (originalFilter is EventFilter)
            {
                m_typeMask = MonitoredItemTypeMask.Events;

                if (itemToMonitor.NodeId == Objects.Server)
                {
                    m_typeMask |= MonitoredItemTypeMask.AllEvents;
                }
            }

            // create aggregate calculator.
            ServerAggregateFilter aggregateFilter = filterToUse as ServerAggregateFilter;

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
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_server = null;
            m_nodeManager = null;
            m_managerHandle = null;
            m_subscriptionId = 0;
            m_id = 0;
            m_session = null;
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
            m_events = null;
            m_overflow = false;
            m_readyToPublish = false;
            m_readyToTrigger = false;
            m_sourceSamplingInterval = 0;
            m_samplingError = ServiceResult.Good;
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
        public int MonitoredItemType => m_typeMask;

        /// <summary>
        /// Returns true if the item is ready to publish.
        /// </summary>
        public bool IsReadyToPublish
        {
            get
            {
                // check if aggregate interval has passed.
                if (m_calculator != null)
                {
                    if (m_calculator.HasEndTimePassed(DateTime.UtcNow))
                    {
                        return true;
                    }
                }

                // check if not ready to publish.
                if (!m_readyToPublish)
                {
                    Utils.Trace((int)TraceMasks.OperationDetail, "IsReadyToPublish[{0}] FALSE", m_id);
                    return false;
                }

                // check if it has been triggered.
                if (m_monitoringMode != MonitoringMode.Disabled && m_triggered)
                {
                    Utils.Trace((int)TraceMasks.OperationDetail, "IsReadyToPublish[{0}] TRIGGERED", m_id);
                    return true;
                }

                // check if monitoring was turned off.
                if (m_monitoringMode != MonitoringMode.Reporting)
                {
                    Utils.Trace((int)TraceMasks.OperationDetail, "IsReadyToPublish[{0}] FALSE", m_id);
                    return false;
                }

                if (m_sourceSamplingInterval == 0)
                {
                    // re-queue if too little time has passed since the last publish.
                    long now = HiResClock.TickCount64;

                    if (m_nextSamplingTime > now)
                    {
                        Utils.Trace((int)TraceMasks.OperationDetail, "IsReadyToPublish[{0}] FALSE {1}", m_id, new TimeSpan(m_nextSamplingTime - now).TotalSeconds);
                        return false;
                    }
                }

                Utils.Trace((int)TraceMasks.OperationDetail, "IsReadyToPublish[{0}] NORMAL", m_id);
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

        /// <summary>
        /// Sets a flag indicating that the item has been triggered and should publish.
        /// </summary>
        public bool SetTriggered()
        {
            lock (m_lock)
            {
                if (m_readyToPublish)
                {
                    Utils.Trace("SetTriggered[{0}]", m_id);
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
        public Session Session
        {
            get
            {
                lock (m_lock)
                {
                    return m_session;
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
                    if (m_events != null)
                    {
                        return m_events.Count;
                    }

                    if (m_queue != null)
                    {
                        return m_queue.ItemsInQueue;
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
        public bool AlwaysReportUpdates
        {
            get { return m_alwaysReportUpdates; }
            set { m_alwaysReportUpdates = value; }
        }

        /// <summary>
        /// Returns a description of the item being monitored. 
        /// </summary>
        public ReadValueId GetReadValueId()
        {
            lock (m_lock)
            {
                ReadValueId valueId = new ReadValueId();

                valueId.NodeId = m_nodeId;
                valueId.AttributeId = m_attributeId;
                valueId.IndexRange = m_indexRange;
                valueId.ParsedIndexRange = m_parsedIndexRange;
                valueId.DataEncoding = m_encoding;
                valueId.Handle = m_managerHandle;

                return valueId;
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
                result = new MonitoredItemCreateResult();

                result.MonitoredItemId = m_id;
                result.RevisedSamplingInterval = m_samplingInterval;
                result.RevisedQueueSize = m_queueSize;
                result.StatusCode = StatusCodes.Good;

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
                result = new MonitoredItemModifyResult();

                result.RevisedSamplingInterval = m_samplingInterval;
                result.RevisedQueueSize = m_queueSize;
                result.StatusCode = StatusCodes.Good;

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
                ServerAggregateFilter aggregateFilter = filterToUse as ServerAggregateFilter;

                if (filterToUse is ServerAggregateFilter)
                {
                    ServerAggregateFilter existingFilter = filterToUse as ServerAggregateFilter;

                    bool match = existingFilter != null;

                    if (match) if (existingFilter.AggregateType != aggregateFilter.AggregateType) match = false;
                    if (match) if (existingFilter.ProcessingInterval != aggregateFilter.ProcessingInterval) match = false;
                    if (match) if (existingFilter.StartTime != aggregateFilter.StartTime) match = false;
                    if (match) if (!existingFilter.AggregateConfiguration.IsEqual(aggregateFilter.AggregateConfiguration)) match = false;

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
        void ISampledDataChangeMonitoredItem.SetMonitoringMode(MonitoringMode monitoringMode)
        {
            SetMonitoringMode(monitoringMode);
        }

        /// <summary>
        /// Changes the monitoring mode for the item.
        /// </summary>
        void IEventMonitoredItem.SetMonitoringMode(MonitoringMode monitoringMode)
        {
            SetMonitoringMode(monitoringMode);
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

                Utils.Trace("MONITORING MODE[{0}] {1} -> {2}", m_id, m_monitoringMode, monitoringMode);

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
        public virtual void QueueValue(DataValue value, ServiceResult error, bool bypassFilter)
        {
            lock (m_lock)
            {
                // this method should only be called for variables. 
                if ((m_typeMask & MonitoredItemTypeMask.DataChange) == 0)
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
                    Utils.Trace("RECEIVED VALUE[{0}] Value={1}", this.m_id, value.WrappedValue);

                    DataValue copy = new DataValue();

                    copy.WrappedValue = value.WrappedValue;
                    copy.StatusCode = value.StatusCode;
                    copy.SourceTimestamp = value.SourceTimestamp;
                    copy.SourcePicoseconds = value.SourcePicoseconds;
                    copy.ServerTimestamp = value.ServerTimestamp;
                    copy.ServerPicoseconds = value.ServerPicoseconds;

                    value = copy;

                    // ensure the data value matches the error status code.
                    if (error != null && error.StatusCode.Code != 0)
                    {
                        value.StatusCode = error.StatusCode;
                    }
                }

                // create empty value if none provided.
                if (ServiceResult.IsBad(error))
                {
                    if (value == null)
                    {
                        value = new DataValue();
                        value.StatusCode = error.StatusCode;
                        value.SourceTimestamp = DateTime.UtcNow;
                        value.ServerTimestamp = DateTime.UtcNow;
                    }
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
                        Utils.Trace("Value received out of order: {1}, ServerHandle={0}", m_id, value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff"));
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
                if (!m_alwaysReportUpdates && !bypassFilter)
                {
                    if (!ApplyFilter(value, error))
                    {
                        ServerUtils.ReportFilteredValue(m_nodeId, m_id, value);
                        return;
                    }
                }

                ServerUtils.ReportQueuedValue(m_nodeId, m_id, value);

                // add the value to the queue.
                AddValueToQueue(value, error);
            }
        }

        /// <summary>
        /// Sets the overflow bit.
        /// </summary>
        private ServiceResult SetOverflowBit(
            object value,
            ServiceResult error)
        {
            DataValue dataValue = value as DataValue;

            if (dataValue != null)
            {
                dataValue.StatusCode = dataValue.StatusCode.SetOverflow(true);
            }

            if (error != null)
            {
                error = new ServiceResult(
                    error.StatusCode.SetOverflow(true),
                    error.SymbolicId,
                    error.NamespaceUri,
                    error.LocalizedText,
                    error.AdditionalInfo,
                    error.InnerResult);
            }

            return error;
        }

        /// <summary>
        /// Adds a value to the queue.
        /// </summary>
        private void AddValueToQueue(DataValue value, ServiceResult error)
        {
            if (m_queueSize > 1)
            {
                m_queue.QueueValue(value, error);
            }

            if (m_lastValue != null)
            {
                m_readyToTrigger = true;
            }

            // save last value received.
            m_lastValue = value;
            m_lastError = error;
            m_readyToPublish = true;

            Utils.Trace("QUEUE VALUE[{0}]: Value={1} CODE={2}<{2:X8}> OVERFLOW={3}", m_id, m_lastValue.WrappedValue, m_lastValue.StatusCode.Code, m_lastValue.StatusCode.Overflow);
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
            EventFieldList fields = new EventFieldList();

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
                    LocalizedText text = value as LocalizedText;

                    if (text != null)
                    {
                        value = m_server.ResourceManager.Translate(m_session.PreferredLocales, text);
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
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            lock (m_lock)
            {
                // this method should only be called for objects or views. 
                if ((m_typeMask & MonitoredItemTypeMask.Events) == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                // can't do anything if queuing is disabled.
                if (m_events == null)
                {
                    return;
                }

                // check for duplicate instances being reported via multiple paths.
                for (int ii = 0; ii < m_events.Count; ii++)
                {
                    EventFieldList processedEvent = m_events[ii] as EventFieldList;

                    if (processedEvent != null)
                    {
                        if (Object.ReferenceEquals(instance, processedEvent.Handle))
                        {
                            return;
                        }
                    }
                }

                // check for space in the queue.
                if (m_events.Count >= m_queueSize)
                {
                    if (!m_discardOldest)
                    {
                        m_overflow = true;
                        return;
                    }
                }

                // construct the context to use for the event filter.
                FilterContext context = new FilterContext(m_server.NamespaceUris, m_server.TypeTree, m_session.PreferredLocales);

                // event filter must be specified.
                EventFilter filter = m_filterToUse as EventFilter;

                if (filter == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                // apply filter.
                if (!bypassFilter)
                {
                    if (!filter.WhereClause.Evaluate(context, instance))
                    {
                        return;
                    }
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
                // make space in the queue.
                if (m_events.Count >= m_queueSize)
                {
                    m_overflow = true;

                    if (m_discardOldest)
                    {
                        m_events.RemoveAt(0);
                    }
                }

                // queue the event.
                m_events.Add(fields);
                m_readyToPublish = true;
                m_readyToTrigger = true;
            }
        }

        /// <summary>
        /// Whether the item has notifications that are ready to publish.
        /// </summary>
        [Obsolete("Not used - Use IsReadyToPublish")]
        public virtual bool ReadyToPublish
        {
            get
            {
                lock (m_lock)
                {
                    // only publish if reporting.
                    if (m_monitoringMode != MonitoringMode.Reporting)
                    {
                        return false;
                    }

                    return m_readyToPublish;
                }
            }
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
        public virtual bool Publish(OperationContext context, Queue<EventFieldList> notifications)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (notifications == null) throw new ArgumentNullException(nameof(notifications));

            lock (m_lock)
            {
                // check if the item reports events.
                if ((m_typeMask & MonitoredItemTypeMask.Events) == 0)
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

                // publish events.
                if (m_events != null)
                {
                    Utils.Trace("MONITORED ITEM: Publish(QueueSize={0})", notifications.Count);

                    EventFieldList overflowEvent = null;

                    if (m_overflow)
                    {
                        // construct event.
                        EventQueueOverflowEventState e = new EventQueueOverflowEventState(null);

                        TranslationInfo message = new TranslationInfo(
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
                            new FilterContext(m_server.NamespaceUris, m_server.TypeTree, m_session.PreferredLocales),
                            m_filterToUse as EventFilter,
                            e);
                    }

                    // place event at the beginning of the queue.
                    if (overflowEvent != null && m_discardOldest)
                    {
                        notifications.Enqueue(overflowEvent);
                    }

                    for (int ii = 0; ii < m_events.Count; ii++)
                    {
                        EventFieldList fields = (EventFieldList)m_events[ii];

                        // apply any diagnostic masks.
                        for (int jj = 0; jj < fields.EventFields.Count; jj++)
                        {
                            object value = fields.EventFields[jj].Value;

                            StatusResult result = value as StatusResult;

                            if (result != null)
                            {
                                result.ApplyDiagnosticMasks(context.DiagnosticsMask, context.StringTable);
                            }
                        }

                        notifications.Enqueue((EventFieldList)m_events[ii]);
                    }

                    m_events.Clear();

                    // place event at the end of the queue.
                    if (overflowEvent != null && !m_discardOldest)
                    {
                        notifications.Enqueue(overflowEvent);
                    }

                    Utils.Trace("MONITORED ITEM: Publish(QueueSize={0})", notifications.Count);
                }

                // reset state variables.
                m_overflow = false;
                m_readyToPublish = false;
                m_readyToTrigger = false;
                m_triggered = false;

                return false;
            }
        }

        /// <summary>
        /// Publishes all available data change notifications.
        /// </summary>
        public virtual bool Publish(
            OperationContext context,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (notifications == null) throw new ArgumentNullException(nameof(notifications));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

            lock (m_lock)
            {
                // check if the item reports data changes.
                if ((m_typeMask & MonitoredItemTypeMask.DataChange) == 0)
                {
                    return false;
                }

                // only publish if reporting.
                if (!IsReadyToPublish)
                {
                    return false;
                }

                // pull any unprocessed data.
                if (m_calculator != null)
                {
                    if (m_calculator.HasEndTimePassed(DateTime.UtcNow))
                    {
                        DataValue processedValue = m_calculator.GetProcessedValue(false);

                        while (processedValue != null)
                        {
                            AddValueToQueue(processedValue, null);
                        }

                        processedValue = m_calculator.GetProcessedValue(true);
                        AddValueToQueue(processedValue, null);
                    }
                }

                // go to the next sampling interval.
                IncrementSampleTime();

                // check if queueing enabled.
                if (m_queue != null)
                {
                    DataValue value = null;
                    ServiceResult error = null;

                    while (m_queue.Publish(out value, out error))
                    {
                        Publish(context, notifications, diagnostics, value, error);
                    }
                }

                // publish last value if no queuing.
                else
                {
                    Utils.Trace("DEQUEUE VALUE: Value={0} CODE={1}<{1:X8}> OVERFLOW={2}", m_lastValue.WrappedValue, m_lastValue.StatusCode.Code, m_lastValue.StatusCode.Overflow);
                    Publish(context, notifications, diagnostics, m_lastValue, m_lastError);
                }

                // reset state variables.
                m_overflow = false;
                m_readyToPublish = false;
                m_readyToTrigger = false;
                m_triggered = false;

                return false;
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
            MonitoredItemNotification item = new MonitoredItemNotification();

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
                        return Int32.MaxValue;
                    }

                    // node manager responsible for ensuring correct sampling.
                    if (m_sourceSamplingInterval > 0)
                    {
                        return 0;
                    }

                    var now = HiResClock.TickCount64;

                    if (m_nextSamplingTime <= now)
                    {
                        return 0;
                    }

                    return (int)(m_nextSamplingTime - now);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Applies the filter to value to determine if the new value should be kept.
        /// </summary>
        protected virtual bool ApplyFilter(DataValue value, ServiceResult error)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            bool changed = ValueChanged(
                value,
                error,
                m_lastValue,
                m_lastError,
                m_filterToUse as DataChangeFilter,
                m_range);

            return changed;
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
            if (value == null) throw new ArgumentNullException(nameof(value));

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
            if (trigger == DataChangeTrigger.StatusValueTimestamp)
            {
                if (lastValue.SourceTimestamp != value.SourceTimestamp)
                {
                    return true;
                }
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
            Array array1 = value1 as Array;
            Array array2 = value2 as Array;

            if (array1 == null || array2 == null)
            {

                XmlElement xmlElement1 = value1 as XmlElement;
                XmlElement xmlElement2 = value2 as XmlElement;

                if (xmlElement1 != null && xmlElement2 != null)
                {
                    return xmlElement1.OuterXml.Equals(xmlElement2.OuterXml);
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
            if (value1 is double)
            {
                return ExceedsDeadband((double)value1, (double)value2, deadbandType, deadband, range);
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

                if (baseline > 0)
                {
                    if (Math.Abs((decimal1 - decimal2) / baseline) <= (decimal)deadband)
                    {
                        return false;
                    }
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

            if (baseline > 0)
            {
                if (Math.Abs((value1 - value2) / baseline) <= deadband)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Clears and re-initializes the queue if the monitoring parameters changed.
        /// </summary>
        protected void InitializeQueue()
        {
            switch (m_monitoringMode)
            {
                default:
                case MonitoringMode.Disabled:
                {
                    m_queue = null;
                    m_events = null;
                    break;
                }

                case MonitoringMode.Reporting:
                case MonitoringMode.Sampling:
                {
                    // check if queuing is disabled.
                    if (m_queueSize == 0)
                    {
                        if (m_typeMask == MonitoredItemTypeMask.DataChange)
                        {
                            m_queueSize = 1;
                        }

                        if ((m_typeMask & MonitoredItemTypeMask.Events) != 0)
                        {
                            m_queueSize = 1000;
                        }
                    }

                    // create data queue.
                    if (m_typeMask == MonitoredItemTypeMask.DataChange)
                    {
                        if (m_queueSize <= 1)
                        {
                            m_queue = null;
                            break; // queueing is disabled
                        }

                        bool queueLastValue = false;

                        if (m_queue == null)
                        {
                            m_queue = new MonitoredItemQueue(m_id, QueueOverflowHandler);
                            queueLastValue = true;
                        }

                        m_queue.SetQueueSize(m_queueSize, m_discardOldest, m_diagnosticsMasks);
                        m_queue.SetSamplingInterval(m_samplingInterval);

                        if (queueLastValue && m_lastValue != null)
                        {
                            m_queue.QueueValue(m_lastValue, m_lastError);
                        }
                    }
                    else // create event queue.
                    {
                        if (m_events == null)
                        {
                            m_events = new List<EventFieldList>();
                        }

                        // check if existing queue entries must be discarded;
                        if (m_events.Count > m_queueSize)
                        {
                            int queueSize = (int)m_queueSize;

                            if (m_discardOldest)
                            {
                                m_events.RemoveRange(0, m_events.Count - queueSize);
                            }
                            else
                            {
                                m_events.RemoveRange(queueSize, m_events.Count - queueSize);
                            }
                        }
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Update the overflow count.
        /// </summary>
        private void QueueOverflowHandler()
        {
            m_subscription?.QueueOverflowHandler();
        }
        #endregion

        #region Private Members
        private object m_lock = new object();
        private IServerInternal m_server;
        private INodeManager m_nodeManager;
        private object m_managerHandle;
        private uint m_subscriptionId;
        private uint m_id;
        private int m_typeMask;
        private Session m_session;
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
        private bool m_alwaysReportUpdates;

        private DataValue m_lastValue;
        private ServiceResult m_lastError;
        private long m_nextSamplingTime;
        private List<EventFieldList> m_events;
        private MonitoredItemQueue m_queue;
        private bool m_overflow;
        private bool m_readyToPublish;
        private bool m_readyToTrigger;
        private bool m_semanticsChanged;
        private bool m_structureChanged;
        private ISubscription m_subscription;
        private ServiceResult m_samplingError;
        private IAggregateCalculator m_calculator;
        private bool m_triggered;
        #endregion
    }
}
