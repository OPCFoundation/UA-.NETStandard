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
using System.Runtime.Serialization;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A monitored item.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [KnownType(typeof(DataChangeFilter))]
    [KnownType(typeof(EventFilter))]
    [KnownType(typeof(AggregateFilter))]
    public class MonitoredItem
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        public MonitoredItem()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        /// <param name="clientHandle">The client handle. The caller must ensure it uniquely identifies the monitored item.</param>
        public MonitoredItem(uint clientHandle)
        {
            Initialize();
            m_clientHandle = clientHandle;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        /// <param name="template">The template used to specify the monitoring parameters.</param>
        public MonitoredItem(MonitoredItem template) : this(template, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        /// <param name="template">The template used to specify the monitoring parameters.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        public MonitoredItem(MonitoredItem template, bool copyEventHandlers) : this(template, copyEventHandlers, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        /// <param name="template">The template used to specify the monitoring parameters.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        /// <param name="copyClientHandle">if set to <c>true</c> the clientHandle is of the template copied.</param>
        public MonitoredItem(MonitoredItem template, bool copyEventHandlers, bool copyClientHandle)
        {
            Initialize();

            if (template != null)
            {
                string displayName = template.DisplayName;

                if (displayName != null)
                {
                    // remove any existing numeric suffix.
                    int index = displayName.LastIndexOf(' ');

                    if (index != -1)
                    {
                        try
                        {
                            displayName = displayName.Substring(0, index);
                        }
                        catch
                        {
                            // not a numeric suffix.
                        }
                    }
                }

                m_handle = template.m_handle;
                m_displayName = Utils.Format("{0} {1}", displayName, m_clientHandle);
                m_startNodeId = template.m_startNodeId;
                m_relativePath = template.m_relativePath;
                m_attributeId = template.m_attributeId;
                m_indexRange = template.m_indexRange;
                m_encoding = template.m_encoding;
                m_monitoringMode = template.m_monitoringMode;
                m_samplingInterval = template.m_samplingInterval;
                m_filter = (MonitoringFilter)Utils.Clone(template.m_filter);
                m_queueSize = template.m_queueSize;
                m_discardOldest = template.m_discardOldest;
                m_attributesModified = true;

                if (copyEventHandlers)
                {
                    m_Notification = template.m_Notification;
                }

                if (copyClientHandle)
                {
                    m_clientHandle = template.m_clientHandle;
                }

                // this ensures the state is consistent with the node class.
                NodeClass = template.m_nodeClass;
            }
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            // object initializers are not called during deserialization.
            m_cache = new object();

            Initialize();
        }

        /// <summary>
        /// Sets the private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_startNodeId = null;
            m_relativePath = null;
            m_clientHandle = 0;
            m_attributeId = Attributes.Value;
            m_indexRange = null;
            m_encoding = null;
            m_monitoringMode = MonitoringMode.Reporting;
            m_samplingInterval = -1;
            m_filter = null;
            m_queueSize = 0;
            m_discardOldest = true;
            m_attributesModified = true;
            m_status = new MonitoredItemStatus();

            // this ensures the state is consistent with the node class.
            NodeClass = NodeClass.Variable;

            // assign a unique handle.
            m_clientHandle = Utils.IncrementIdentifier(ref s_globalClientHandle);
        }
        #endregion

        #region Persistent Properties
        /// <summary>
        /// A display name for the monitored item.
        /// </summary>
        [DataMember(Order = 1)]
        public string DisplayName
        {
            get { return m_displayName; }
            set { m_displayName = value; }
        }


        /// <summary>
        /// The start node for the browse path that identifies the node to monitor.
        /// </summary>
        [DataMember(Order = 2)]
        public NodeId StartNodeId
        {
            get { return m_startNodeId; }
            set { m_startNodeId = value; }
        }

        /// <summary>
        /// The relative path from the browse path to the node to monitor.
        /// </summary>
        /// <remarks>
        /// A null or empty string specifies that the start node id should be monitored.
        /// </remarks>
        [DataMember(Order = 3)]
        public string RelativePath
        {
            get { return m_relativePath; }

            set
            {
                // clear resolved path if relative path has changed.
                if (m_relativePath != value)
                {
                    m_resolvedNodeId = null;
                }

                m_relativePath = value;
            }
        }

        /// <summary>
        /// The node class of the node being monitored (affects the type of filter available).
        /// </summary>
        [DataMember(Order = 4)]
        public NodeClass NodeClass
        {
            get { return m_nodeClass; }

            set
            {
                if (m_nodeClass != value)
                {
                    if ((value & (NodeClass.Object | NodeClass.View)) != 0)
                    {
                        // ensure a valid event filter.
                        if (!(m_filter is EventFilter))
                        {
                            UseDefaultEventFilter();
                        }

                        // set the queue size to the default for events.
                        if (QueueSize <= 1)
                        {
                            QueueSize = Int32.MaxValue;
                        }

                        m_eventCache = new MonitoredItemEventCache(100);
                        m_attributeId = Attributes.EventNotifier;
                    }
                    else
                    {
                        // clear the filter if it is only valid for events.
                        if (m_filter is EventFilter)
                        {
                            m_filter = null;
                        }

                        // set the queue size to the default for data changes.
                        if (QueueSize == Int32.MaxValue)
                        {
                            QueueSize = 1;
                        }

                        m_dataCache = new MonitoredItemDataCache(1);
                    }
                }

                m_nodeClass = value;
            }
        }

        /// <summary>
        /// The attribute to monitor.
        /// </summary>
        [DataMember(Order = 5)]
        public uint AttributeId
        {
            get { return m_attributeId; }
            set { m_attributeId = value; }
        }

        /// <summary>
        /// The range of array indexes to monitor.
        /// </summary>
        [DataMember(Order = 6)]
        public string IndexRange
        {
            get { return m_indexRange; }
            set { m_indexRange = value; }
        }

        /// <summary>
        /// The encoding to use when returning notifications.
        /// </summary>
        [DataMember(Order = 7)]
        public QualifiedName Encoding
        {
            get { return m_encoding; }
            set { m_encoding = value; }
        }

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        [DataMember(Order = 8)]
        public MonitoringMode MonitoringMode
        {
            get { return m_monitoringMode; }
            set { m_monitoringMode = value; }
        }

        /// <summary>
        /// The sampling interval.
        /// </summary>
        [DataMember(Order = 9)]
        public int SamplingInterval
        {
            get { return m_samplingInterval; }

            set
            {
                if (m_samplingInterval != value)
                {
                    m_attributesModified = true;
                }

                m_samplingInterval = value;
            }
        }

        /// <summary>
        /// The filter to use to select values to return.
        /// </summary>
        [DataMember(Order = 10)]
        public MonitoringFilter Filter
        {
            get { return m_filter; }

            set
            {
                // validate filter against node class.
                ValidateFilter(m_nodeClass, value);

                m_attributesModified = true;
                m_filter = value;
            }
        }

        /// <summary>
        /// The length of the queue used to buffer values.
        /// </summary>
        [DataMember(Order = 11)]
        public uint QueueSize
        {
            get { return m_queueSize; }

            set
            {
                if (m_queueSize != value)
                {
                    m_attributesModified = true;
                }

                m_queueSize = value;
            }
        }

        /// <summary>
        /// Whether to discard the oldest entries in the queue when it is full.
        /// </summary>
        [DataMember(Order = 12)]
        public bool DiscardOldest
        {
            get { return m_discardOldest; }

            set
            {
                if (m_discardOldest != value)
                {
                    m_attributesModified = true;
                }

                m_discardOldest = value;
            }
        }

        /// <summary>
        /// Server-assigned id for the MonitoredItem.
        /// </summary>
        [DataMember(Order = 13)]
        public uint ServerId
        {
            get { return m_status.Id; }
            set { m_status.Id = value; }
        }
        #endregion

        #region Dynamic Properties
        /// <summary>
        /// The subscription that owns the monitored item.
        /// </summary>
        public Subscription Subscription
        {
            get { return m_subscription; }
            internal set { m_subscription = value; }
        }

        /// <summary>
        /// A local handle assigned to the monitored item.
        /// </summary>
        public object Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// Whether the item has been created on the server.
        /// </summary>
        public bool Created => m_status.Created;

        /// <summary>
        /// The identifier assigned by the client.
        /// </summary>
        public uint ClientHandle => m_clientHandle;

        /// <summary>
        /// The node id to monitor after applying any relative path.
        /// </summary>
        public NodeId ResolvedNodeId
        {
            get
            {
                // just return the start id if relative path is empty.
                if (String.IsNullOrEmpty(m_relativePath))
                {
                    return m_startNodeId;
                }

                return m_resolvedNodeId;
            }

            internal set { m_resolvedNodeId = value; }
        }

        /// <summary>
        /// Whether the monitoring attributes have been modified since the item was created.
        /// </summary>
        public bool AttributesModified => m_attributesModified;

        /// <summary>
        /// The status associated with the monitored item.
        /// </summary>
        public MonitoredItemStatus Status => m_status;
        #endregion

        #region Cache Related Functions
        /// <summary>
        /// Returns the queue size used by the cache.
        /// </summary>
        public int CacheQueueSize
        {
            get
            {
                lock (m_cache)
                {
                    if (m_dataCache != null)
                    {
                        return m_dataCache.QueueSize;
                    }

                    if (m_eventCache != null)
                    {
                        return m_eventCache.QueueSize;
                    }

                    return 0;
                }
            }

            set
            {
                lock (m_cache)
                {
                    if (m_dataCache != null)
                    {
                        m_dataCache.SetQueueSize(value);
                    }

                    if (m_eventCache != null)
                    {
                        m_eventCache.SetQueueSize(value);
                    }
                }
            }
        }

        /// <summary>
        /// The last value or event received from the server.
        /// </summary>
        public IEncodeable LastValue
        {
            get
            {
                lock (m_cache)
                {
                    return m_lastNotification;
                }
            }
        }

        /// <summary>
        /// Read all values in the cache queue.
        /// </summary>
        public IList<DataValue> DequeueValues()
        {
            lock (m_cache)
            {
                if (m_dataCache != null)
                {
                    return m_dataCache.Publish();
                }

                return new List<DataValue>();
            }
        }

        /// <summary>
        /// Read all events in the cache queue.
        /// </summary>
        public IList<EventFieldList> DequeueEvents()
        {
            lock (m_cache)
            {
                if (m_eventCache != null)
                {
                    return m_eventCache.Publish();
                }

                return new List<EventFieldList>();
            }
        }

        /// <summary>
        /// The last message containing a notification for the item.
        /// </summary>
        public NotificationMessage LastMessage
        {
            get
            {
                lock (m_cache)
                {
                    if (m_dataCache != null)
                    {
                        return ((MonitoredItemNotification)m_lastNotification).Message;
                    }

                    if (m_eventCache != null)
                    {
                        return ((EventFieldList)m_lastNotification).Message;
                    }

                    return null;
                }
            }
        }

        /// <summary>
        /// Raised when a new notification arrives.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event MonitoredItemNotificationEventHandler Notification
        {
            add
            {
                lock (m_cache)
                {
                    m_Notification += value;
                }
            }

            remove
            {
                lock (m_cache)
                {
                    m_Notification -= value;
                }
            }
        }

        /// <summary>
        /// Saves a data change or event in the cache.
        /// </summary>
        public void SaveValueInCache(IEncodeable newValue)
        {
            lock (m_cache)
            {
                // only validate timestamp on first sample
                bool validateTimestamp = m_lastNotification == null;

                m_lastNotification = newValue;

                if (m_dataCache != null)
                {
                    MonitoredItemNotification datachange = newValue as MonitoredItemNotification;

                    if (datachange != null)
                    {
                        if (datachange.Value != null)
                        {
                            if (validateTimestamp)
                            {
                                var now = DateTime.UtcNow;

                                // validate the ServerTimestamp of the notification.
                                if (datachange.Value.ServerTimestamp > now)
                                {
                                    Utils.LogWarning("Received ServerTimestamp {0} is in the future for MonitoredItemId {1}",
                                        datachange.Value.ServerTimestamp.ToLocalTime(), ClientHandle);
                                }

                                // validate SourceTimestamp of the notification.
                                if (datachange.Value.SourceTimestamp > now)
                                {
                                    Utils.LogWarning("Received SourceTimestamp {0} is in the future for MonitoredItemId {1}",
                                        datachange.Value.SourceTimestamp.ToLocalTime(), ClientHandle);
                                }
                            }

                            if (datachange.Value.StatusCode.Overflow)
                            {
                                Utils.LogWarning("Overflow bit set for data change with ServerTimestamp {0} and value {1} for MonitoredItemId {2}",
                                    datachange.Value.ServerTimestamp.ToLocalTime(), datachange.Value.Value, ClientHandle);
                            }
                        }

                        m_dataCache.OnNotification(datachange);
                    }
                }

                if (m_eventCache != null)
                {
                    EventFieldList eventchange = newValue as EventFieldList;

                    if (m_eventCache != null)
                    {
                        m_eventCache.OnNotification(eventchange);
                    }
                }

                if (m_Notification != null)
                {
                    m_Notification(this, new MonitoredItemNotificationEventArgs(newValue));
                }
            }
        }
        #endregion

        #region ICloneable Members
        /// <summary>
        /// Creates a deep copy of the object.
        /// </summary>
        public new object MemberwiseClone()
        {
            return new MonitoredItem(this);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Sets the error status for the monitored item.
        /// </summary>
        public void SetError(ServiceResult error)
        {
            m_status.SetError(error);
        }

        /// <summary>
        /// Updates the object with the results of a translate browse path request.
        /// </summary>
        public void SetResolvePathResult(
            BrowsePathResult result,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            ServiceResult error = null;

            if (StatusCode.IsBad(result.StatusCode))
            {
                error = ClientBase.GetResult(result.StatusCode, index, diagnosticInfos, responseHeader);
            }
            else
            {
                ResolvedNodeId = NodeId.Null;

                // update the node id.
                if (result.Targets.Count > 0)
                {
                    ResolvedNodeId = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, m_subscription.Session.NamespaceUris);
                }
            }

            m_status.SetResolvePathResult(result, error);
        }

        /// <summary>
        /// Updates the object with the results of a create monitored item request.
        /// </summary>
        public void SetCreateResult(
            MonitoredItemCreateRequest request,
            MonitoredItemCreateResult result,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            ServiceResult error = null;

            if (StatusCode.IsBad(result.StatusCode))
            {
                error = ClientBase.GetResult(result.StatusCode, index, diagnosticInfos, responseHeader);
            }

            m_status.SetCreateResult(request, result, error);
            m_attributesModified = false;
        }

        /// <summary>
        /// Updates the object with the results of a modify monitored item request.
        /// </summary>
        public void SetModifyResult(
            MonitoredItemModifyRequest request,
            MonitoredItemModifyResult result,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            ServiceResult error = null;

            if (StatusCode.IsBad(result.StatusCode))
            {
                error = ClientBase.GetResult(result.StatusCode, index, diagnosticInfos, responseHeader);
            }

            m_status.SetModifyResult(request, result, error);
            m_attributesModified = false;
        }

        /// <summary>
        /// Updates the object with the results of a transfer subscription request.
        /// </summary>
        public void SetTransferResult(uint clientHandle)
        {
            // ensure the global counter is not duplicating future handle ids
            Utils.LowerLimitIdentifier(ref s_globalClientHandle, clientHandle);
            m_clientHandle = clientHandle;  
            m_status.SetTransferResult(this);
            m_attributesModified = false;
        }

        /// <summary>
        /// Updates the object with the results of a delete monitored item request.
        /// </summary>
        public void SetDeleteResult(
            StatusCode result,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            ServiceResult error = null;

            if (StatusCode.IsBad(result))
            {
                error = ClientBase.GetResult(result, index, diagnosticInfos, responseHeader);
            }

            m_status.SetDeleteResult(error);
        }

        /// <summary>
        /// Returns the field name the specified SelectClause in the EventFilter.
        /// </summary>
        public string GetFieldName(int index)
        {
            EventFilter filter = m_filter as EventFilter;

            if (filter == null)
            {
                return null;
            }

            if (index < 0 || index >= filter.SelectClauses.Count)
            {
                return null;
            }

            return Utils.Format("{0}", SimpleAttributeOperand.Format(filter.SelectClauses[index].BrowsePath));
        }

        /// <summary>
        /// Returns value of the field name containing the event type.
        /// </summary>
        public object GetFieldValue(
            EventFieldList eventFields,
            NodeId eventTypeId,
            string browsePath,
            uint attributeId)
        {
            QualifiedNameCollection browseNames = SimpleAttributeOperand.Parse(browsePath);
            return GetFieldValue(eventFields, eventTypeId, browseNames, attributeId);
        }

        /// <summary>
        /// Returns value of the field name containing the event type.
        /// </summary>
        public object GetFieldValue(
            EventFieldList eventFields,
            NodeId eventTypeId,
            QualifiedName browseName)
        {
            QualifiedNameCollection browsePath = new QualifiedNameCollection();
            browsePath.Add(browseName);
            return GetFieldValue(eventFields, eventTypeId, browsePath, Attributes.Value);
        }

        /// <summary>
        /// Returns value of the field name containing the event type.
        /// </summary>
        public object GetFieldValue(
            EventFieldList eventFields,
            NodeId eventTypeId,
            IList<QualifiedName> browsePath,
            uint attributeId)
        {
            if (eventFields == null)
            {
                return null;
            }

            EventFilter filter = m_filter as EventFilter;

            if (filter == null)
            {
                return null;
            }

            for (int ii = 0; ii < filter.SelectClauses.Count; ii++)
            {
                if (ii >= eventFields.EventFields.Count)
                {
                    return null;
                }

                // check for match.
                SimpleAttributeOperand clause = filter.SelectClauses[ii];

                // attribute id
                if (clause.AttributeId != attributeId)
                {
                    continue;
                }

                // match null browse path.
                if (browsePath == null || browsePath.Count == 0)
                {
                    if (clause.BrowsePath != null && clause.BrowsePath.Count > 0)
                    {
                        continue;
                    }

                    // ignore event type id when matching null browse paths.
                    return eventFields.EventFields[ii].Value;
                }

                // match browse path.

                // event type id.
                if (clause.TypeDefinitionId != eventTypeId)
                {
                    continue;
                }

                // match element count.
                if (clause.BrowsePath.Count != browsePath.Count)
                {
                    continue;
                }

                // check each element.
                bool match = true;

                for (int jj = 0; jj < clause.BrowsePath.Count; jj++)
                {
                    if (clause.BrowsePath[jj] != browsePath[jj])
                    {
                        match = false;
                        break;
                    }
                }

                // check of no match.
                if (!match)
                {
                    continue;
                }

                // return value.
                return eventFields.EventFields[ii].Value;
            }

            // no event type in event field list.
            return null;
        }

        /// <summary>
        /// Returns value of the field name containing the event type.
        /// </summary>
        public INode GetEventType(EventFieldList eventFields)
        {
            // get event type.
            NodeId eventTypeId = GetFieldValue(eventFields, ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.EventType) as NodeId;

            if (eventTypeId != null && m_subscription != null && m_subscription.Session != null)
            {
                return m_subscription.Session.NodeCache.Find(eventTypeId);
            }

            // no event type in event field list.
            return null;
        }

        /// <summary>
        /// Returns value of the field name containing the event type.
        /// </summary>
        public DateTime GetEventTime(EventFieldList eventFields)
        {
            // get event time.
            DateTime? eventTime = GetFieldValue(eventFields, ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.Time) as DateTime?;

            if (eventTime != null)
            {
                return eventTime.Value;
            }

            // no event time in event field list.
            return DateTime.MinValue;
        }

        /// <summary>
        /// The service result for a data change notification.
        /// </summary>
        public static ServiceResult GetServiceResult(IEncodeable notification)
        {
            MonitoredItemNotification datachange = notification as MonitoredItemNotification;

            if (datachange == null)
            {
                return null;
            }

            NotificationMessage message = datachange.Message;

            if (message == null)
            {
                return null;
            }

            return new ServiceResult(datachange.Value.StatusCode, datachange.DiagnosticInfo, message.StringTable);
        }

        /// <summary>
        /// The service result for a field in an notification (the field must contain a Status object).
        /// </summary>
        public static ServiceResult GetServiceResult(IEncodeable notification, int index)
        {
            EventFieldList eventFields = notification as EventFieldList;

            if (eventFields == null)
            {
                return null;
            }

            NotificationMessage message = eventFields.Message;

            if (message == null)
            {
                return null;
            }

            if (index < 0 || index >= eventFields.EventFields.Count)
            {
                return null;
            }

            StatusResult status = ExtensionObject.ToEncodeable(eventFields.EventFields[index].Value as ExtensionObject) as StatusResult;

            if (status == null)
            {
                return null;
            }

            return new ServiceResult(status.StatusCode, status.DiagnosticInfo, message.StringTable);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Throws an exception if the flter cannot be used with the node class.
        /// </summary>
        private void ValidateFilter(NodeClass nodeClass, MonitoringFilter filter)
        {
            if (filter == null)
            {
                return;
            }

            switch (nodeClass)
            {
                case NodeClass.Variable:
                case NodeClass.VariableType:
                {
                    if (!typeof(DataChangeFilter).IsInstanceOfType(filter))
                    {
                        m_nodeClass = NodeClass.Variable;
                    }

                    break;
                }

                case NodeClass.Object:
                case NodeClass.View:
                {
                    if (!typeof(EventFilter).IsInstanceOfType(filter))
                    {
                        m_nodeClass = NodeClass.Object;
                    }

                    break;
                }

                default:
                {
                    throw ServiceResultException.Create(StatusCodes.BadFilterNotAllowed, "Filters may not be specified for nodes of class '{0}'.", nodeClass);
                }
            }
        }

        /// <summary>
        /// Sets the default event filter.
        /// </summary>
        private void UseDefaultEventFilter()
        {
            EventFilter filter = filter = new EventFilter();

            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.EventId);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.EventType);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.SourceNode);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.SourceName);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.Time);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.ReceiveTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.LocalTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.Message);
            filter.AddSelectClause(ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.Severity);

            m_filter = filter;
        }
        #endregion

        #region Private Fields
        private Subscription m_subscription;
        private object m_handle;
        private string m_displayName;
        private NodeId m_startNodeId;
        private string m_relativePath;
        private NodeId m_resolvedNodeId;
        private NodeClass m_nodeClass;
        private uint m_attributeId;
        private string m_indexRange;
        private QualifiedName m_encoding;
        private MonitoringMode m_monitoringMode;
        private int m_samplingInterval;
        private MonitoringFilter m_filter;
        private uint m_queueSize;
        private bool m_discardOldest;
        private uint m_clientHandle;
        private MonitoredItemStatus m_status;
        private bool m_attributesModified;
        private static long s_globalClientHandle;

        private object m_cache = new object();
        private MonitoredItemDataCache m_dataCache;
        private MonitoredItemEventCache m_eventCache;
        private IEncodeable m_lastNotification;
        private event MonitoredItemNotificationEventHandler m_Notification;
        #endregion
    }

    #region MonitoredItemEventArgs Class
    /// <summary>
    /// The event arguments provided when a new notification message arrives.
    /// </summary>
    public class MonitoredItemNotificationEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal MonitoredItemNotificationEventArgs(IEncodeable notificationValue)
        {
            m_notificationValue = notificationValue;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The new notification.
        /// </summary>
        public IEncodeable NotificationValue => m_notificationValue;
        #endregion

        #region Private Fields
        private readonly IEncodeable m_notificationValue;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive monitored item value notifications.
    /// </summary>
    public delegate void MonitoredItemNotificationEventHandler(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);
    #endregion

    /// <summary>
    /// An item in the cache
    /// </summary>
    public class MonitoredItemDataCache
    {
        #region Constructors
        /// <summary>
        /// Constructs a cache for a monitored item.
        /// </summary>
        public MonitoredItemDataCache(int queueSize)
        {
            m_queueSize = queueSize;
            m_values = new Queue<DataValue>();
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The size of the queue to maintain.
        /// </summary>
        public int QueueSize => m_queueSize;

        /// <summary>
        /// The last value received from the server.
        /// </summary>
        public DataValue LastValue => m_lastValue;

        /// <summary>
        /// Returns all values in the queue.
        /// </summary>
        public IList<DataValue> Publish()
        {
            DataValue[] values = new DataValue[m_values.Count];

            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = m_values.Dequeue();
            }

            return values;
        }

        /// <summary>
        /// Saves a notification in the cache.
        /// </summary>
        public void OnNotification(MonitoredItemNotification notification)
        {
            m_values.Enqueue(notification.Value);
            m_lastValue = notification.Value;

            CoreClientUtils.EventLog.NotificationValue(notification.ClientHandle, m_lastValue.WrappedValue);

            while (m_values.Count > m_queueSize)
            {
                m_values.Dequeue();
            }
        }

        /// <summary>
        /// Changes the queue size.
        /// </summary>
        public void SetQueueSize(int queueSize)
        {
            if (queueSize == m_queueSize)
            {
                return;
            }

            if (queueSize < 1)
            {
                queueSize = 1;
            }

            m_queueSize = queueSize;

            while (m_values.Count > m_queueSize)
            {
                m_values.Dequeue();
            }
        }
        #endregion

        #region Private Fields
        private int m_queueSize;
        private DataValue m_lastValue;
        private readonly Queue<DataValue> m_values;
        #endregion
    }

    /// <summary>
    /// Saves the events received from the srever.
    /// </summary>
    public class MonitoredItemEventCache
    {
        #region Constructors
        /// <summary>
        /// Constructs a cache for a monitored item.
        /// </summary>
        public MonitoredItemEventCache(int queueSize)
        {
            m_queueSize = queueSize;
            m_events = new Queue<EventFieldList>();
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The size of the queue to maintain.
        /// </summary>
        public int QueueSize => m_queueSize;

        /// <summary>
        /// The last event received.
        /// </summary>
        public EventFieldList LastEvent => m_lastEvent;

        /// <summary>
        /// Returns all events in the queue.
        /// </summary>
        public IList<EventFieldList> Publish()
        {
            EventFieldList[] events = new EventFieldList[m_events.Count];

            for (int ii = 0; ii < events.Length; ii++)
            {
                events[ii] = m_events.Dequeue();
            }

            return events;
        }

        /// <summary>
        /// Saves a notification in the cache.
        /// </summary>
        public void OnNotification(EventFieldList notification)
        {
            m_events.Enqueue(notification);
            m_lastEvent = notification;

            while (m_events.Count > m_queueSize)
            {
                m_events.Dequeue();
            }
        }

        /// <summary>
        /// Changes the queue size.
        /// </summary>
        public void SetQueueSize(int queueSize)
        {
            if (queueSize == m_queueSize)
            {
                return;
            }

            if (queueSize < 1)
            {
                queueSize = 1;
            }

            m_queueSize = queueSize;

            while (m_events.Count > m_queueSize)
            {
                m_events.Dequeue();
            }
        }
        #endregion

        #region Private Fields
        private int m_queueSize;
        private EventFieldList m_lastEvent;
        private readonly Queue<EventFieldList> m_events;
        #endregion
    }
}
