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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A monitored item.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [KnownType(typeof(DataChangeFilter))]
    [KnownType(typeof(EventFilter))]
    [KnownType(typeof(AggregateFilter))]
    public class MonitoredItem : ICloneable
    {
        private static readonly TimeSpan s_time_epsilon = TimeSpan.FromMilliseconds(500);

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
            ClientHandle = clientHandle;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        /// <param name="template">The template used to specify the monitoring parameters.</param>
        public MonitoredItem(MonitoredItem template)
            : this(template, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        /// <param name="template">The template used to specify the monitoring parameters.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        public MonitoredItem(MonitoredItem template, bool copyEventHandlers)
            : this(template, copyEventHandlers, false)
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
                            displayName = displayName[..index];
                        }
                        catch
                        {
                            // not a numeric suffix.
                        }
                    }
                }

                Handle = template.Handle;
                DisplayName = Utils.Format("{0} {1}", displayName, ClientHandle);
                StartNodeId = template.StartNodeId;
                m_relativePath = template.m_relativePath;
                AttributeId = template.AttributeId;
                IndexRange = template.IndexRange;
                Encoding = template.Encoding;
                MonitoringMode = template.MonitoringMode;
                m_samplingInterval = template.m_samplingInterval;
                m_filter = Utils.Clone(template.m_filter);
                m_queueSize = template.m_queueSize;
                m_discardOldest = template.m_discardOldest;
                AttributesModified = true;

                if (copyEventHandlers)
                {
                    m_Notification = template.m_Notification;
                }

                if (copyClientHandle)
                {
                    ClientHandle = template.ClientHandle;
                }

                // this ensures the state is consistent with the node class.
                NodeClass = template.m_nodeClass;
            }
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        protected void Initialize(StreamingContext context)
        {
            // object initializers are not called during deserialization.
            m_cache = new Lock();

            Initialize();
        }

        /// <summary>
        /// Sets the private members to default values.
        /// </summary>
        private void Initialize()
        {
            StartNodeId = null;
            m_relativePath = null;
            ClientHandle = 0;
            AttributeId = Attributes.Value;
            IndexRange = null;
            Encoding = null;
            MonitoringMode = MonitoringMode.Reporting;
            m_samplingInterval = -1;
            m_filter = null;
            m_queueSize = 0;
            m_discardOldest = true;
            AttributesModified = true;
            Status = new MonitoredItemStatus();

            // this ensures the state is consistent with the node class.
            NodeClass = NodeClass.Variable;

            // assign a unique handle.
            ClientHandle = Utils.IncrementIdentifier(ref s_globalClientHandle);
        }

        /// <summary>
        /// A display name for the monitored item.
        /// </summary>
        [DataMember(Order = 1)]
        public string DisplayName { get; set; }

        /// <summary>
        /// The start node for the browse path that identifies the node to monitor.
        /// </summary>
        [DataMember(Order = 2)]
        public NodeId StartNodeId { get; set; }

        /// <summary>
        /// The relative path from the browse path to the node to monitor.
        /// </summary>
        /// <remarks>
        /// A null or empty string specifies that the start node id should be monitored.
        /// </remarks>
        [DataMember(Order = 3)]
        public string RelativePath
        {
            get => m_relativePath;
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
            get => m_nodeClass;
            set
            {
                if (m_nodeClass != value)
                {
                    if (((int)value & ((int)NodeClass.Object | (int)NodeClass.View)) != 0)
                    {
                        // ensure a valid event filter.
                        if (m_filter is not EventFilter)
                        {
                            UseDefaultEventFilter();
                        }

                        // set the queue size to the default for events.
                        if (QueueSize <= 1)
                        {
                            QueueSize = int.MaxValue;
                        }

                        AttributeId = Attributes.EventNotifier;
                    }
                    else
                    {
                        // clear the filter if it is only valid for events.
                        if (m_filter is EventFilter)
                        {
                            m_filter = null;
                        }

                        // set the queue size to the default for data changes.
                        if (QueueSize == int.MaxValue)
                        {
                            QueueSize = 1;
                        }

                        AttributeId = Attributes.Value;
                    }

                    m_dataCache = null;
                    m_eventCache = null;
                }

                m_nodeClass = value;
            }
        }

        /// <summary>
        /// The attribute to monitor.
        /// </summary>
        [DataMember(Order = 5)]
        public uint AttributeId { get; set; }

        /// <summary>
        /// The range of array indexes to monitor.
        /// </summary>
        [DataMember(Order = 6)]
        public string IndexRange { get; set; }

        /// <summary>
        /// The encoding to use when returning notifications.
        /// </summary>
        [DataMember(Order = 7)]
        public QualifiedName Encoding { get; set; }

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        [DataMember(Order = 8)]
        public MonitoringMode MonitoringMode { get; set; }

        /// <summary>
        /// The sampling interval.
        /// </summary>
        [DataMember(Order = 9)]
        public int SamplingInterval
        {
            get => m_samplingInterval;
            set
            {
                if (m_samplingInterval != value)
                {
                    AttributesModified = true;
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
            get => m_filter;
            set
            {
                // validate filter against node class.
                ValidateFilter(m_nodeClass, value);

                AttributesModified = true;
                m_filter = value;
            }
        }

        /// <summary>
        /// The length of the queue used to buffer values.
        /// </summary>
        [DataMember(Order = 11)]
        public uint QueueSize
        {
            get => m_queueSize;
            set
            {
                if (m_queueSize != value)
                {
                    AttributesModified = true;
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
            get => m_discardOldest;
            set
            {
                if (m_discardOldest != value)
                {
                    AttributesModified = true;
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
            get => Status.Id;
            set => Status.Id = value;
        }

        /// <summary>
        /// The subscription that owns the monitored item.
        /// </summary>
        public Subscription Subscription { get; internal set; }

        /// <summary>
        /// A local handle assigned to the monitored item.
        /// </summary>
        public object Handle { get; set; }

        /// <summary>
        /// Whether the item has been created on the server.
        /// </summary>
        public bool Created => Status.Created;

        /// <summary>
        /// The identifier assigned by the client.
        /// </summary>
        public uint ClientHandle { get; private set; }

        /// <summary>
        /// The node id to monitor after applying any relative path.
        /// </summary>
        public NodeId ResolvedNodeId
        {
            get
            {
                // just return the start id if relative path is empty.
                if (string.IsNullOrEmpty(m_relativePath))
                {
                    return StartNodeId;
                }

                return m_resolvedNodeId;
            }
            internal set => m_resolvedNodeId = value;
        }

        /// <summary>
        /// Whether the monitoring attributes have been modified since the item was created.
        /// </summary>
        public bool AttributesModified { get; private set; }

        /// <summary>
        /// The status associated with the monitored item.
        /// </summary>
        public MonitoredItemStatus Status { get; private set; }

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
                    EnsureCacheIsInitialized();

                    m_dataCache?.SetQueueSize(value);

                    m_eventCache?.SetQueueSize(value);
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

                return [];
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

                return [];
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
        /// Reset the notification event handler.
        /// </summary>
        public void DetachNotificationEventHandlers()
        {
            lock (m_cache)
            {
                m_Notification = null;
            }
        }

        /// <summary>
        /// Saves a data change or event in the cache.
        /// </summary>
        public void SaveValueInCache(IEncodeable newValue)
        {
            lock (m_cache)
            {
                EnsureCacheIsInitialized();

                // only validate timestamp on first sample
                bool validateTimestamp = m_lastNotification == null;

                m_lastNotification = newValue;

                if (m_dataCache != null && newValue is MonitoredItemNotification datachange)
                {
                    if (datachange.Value != null)
                    {
                        if (validateTimestamp)
                        {
                            DateTime now = DateTime.UtcNow.Add(s_time_epsilon);

                            // validate the ServerTimestamp of the notification.
                            if (datachange.Value.ServerTimestamp > now)
                            {
                                Utils.LogWarning(
                                    "Received ServerTimestamp {0} is in the future for MonitoredItemId {1}",
                                    datachange.Value.ServerTimestamp.ToLocalTime(),
                                    ClientHandle);
                            }

                            // validate SourceTimestamp of the notification.
                            if (datachange.Value.SourceTimestamp > now)
                            {
                                Utils.LogWarning(
                                    "Received SourceTimestamp {0} is in the future for MonitoredItemId {1}",
                                    datachange.Value.SourceTimestamp.ToLocalTime(),
                                    ClientHandle);
                            }
                        }

                        if (datachange.Value.StatusCode.Overflow)
                        {
                            Utils.LogWarning(
                                "Overflow bit set for data change with ServerTimestamp {0} and value {1} for MonitoredItemId {2}",
                                datachange.Value.ServerTimestamp.ToLocalTime(),
                                datachange.Value.Value,
                                ClientHandle);
                        }
                    }

                    m_dataCache.OnNotification(datachange);
                }

                if (m_eventCache != null && newValue is EventFieldList eventchange)
                {
                    m_eventCache?.OnNotification(eventchange);
                }

                m_Notification?.Invoke(this, new MonitoredItemNotificationEventArgs(newValue));
            }
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the object.
        /// </summary>
        public new object MemberwiseClone()
        {
            return new MonitoredItem(this);
        }

        /// <summary>
        /// Clones a monitored item or the subclass with an option to copy event handlers.
        /// </summary>
        /// <returns>A cloned instance of the monitored item or a subclass.</returns>
        public virtual MonitoredItem CloneMonitoredItem(
            bool copyEventHandlers,
            bool copyClientHandle)
        {
            return new MonitoredItem(this, copyEventHandlers, copyClientHandle);
        }

        /// <summary>
        /// Sets the error status for the monitored item.
        /// </summary>
        public void SetError(ServiceResult error)
        {
            Status.SetError(error);
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
                error = ClientBase.GetResult(
                    result.StatusCode,
                    index,
                    diagnosticInfos,
                    responseHeader);
            }
            else
            {
                ResolvedNodeId = NodeId.Null;

                // update the node id.
                if (result.Targets.Count > 0)
                {
                    ResolvedNodeId = ExpandedNodeId.ToNodeId(
                        result.Targets[0].TargetId,
                        Subscription.Session.NamespaceUris);
                }
            }

            Status.SetResolvePathResult(error);
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
                error = ClientBase.GetResult(
                    result.StatusCode,
                    index,
                    diagnosticInfos,
                    responseHeader);
            }

            Status.SetCreateResult(request, result, error);
            AttributesModified = false;
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
                error = ClientBase.GetResult(
                    result.StatusCode,
                    index,
                    diagnosticInfos,
                    responseHeader);
            }

            Status.SetModifyResult(request, result, error);
            AttributesModified = false;
        }

        /// <summary>
        /// Updates the object with the results of a transfer subscription request.
        /// </summary>
        public void SetTransferResult(uint clientHandle)
        {
            // ensure the global counter is not duplicating future handle ids
            Utils.SetIdentifierToAtLeast(ref s_globalClientHandle, clientHandle);
            ClientHandle = clientHandle;
            Status.SetTransferResult(this);
            AttributesModified = false;
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

            Status.SetDeleteResult(error);
        }

        /// <summary>
        /// Returns the field name the specified SelectClause in the EventFilter.
        /// </summary>
        public string GetFieldName(int index)
        {
            if (m_filter is not EventFilter filter)
            {
                return null;
            }

            if (index < 0 || index >= filter.SelectClauses.Count)
            {
                return null;
            }

            return Utils.Format(
                "{0}",
                SimpleAttributeOperand.Format(filter.SelectClauses[index].BrowsePath));
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
            var browsePath = new QualifiedNameCollection { browseName };
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

            if (m_filter is not EventFilter filter)
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
        public async ValueTask<INode> GetEventTypeAsync(
            EventFieldList eventFields,
            CancellationToken ct = default)
        {
            // get event type.
            var eventTypeId = GetFieldValue(
                eventFields,
                ObjectTypes.BaseEventType,
                BrowseNames.EventType) as NodeId;

            if (eventTypeId != null &&
                Subscription != null &&
                Subscription.Session != null)
            {
                return await Subscription.Session.NodeCache.FindAsync(
                    eventTypeId,
                    ct).ConfigureAwait(false);
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
            var eventTime = GetFieldValue(
                eventFields,
                ObjectTypes.BaseEventType,
                BrowseNames.Time) as DateTime?;

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
            if (notification is not MonitoredItemNotification datachange)
            {
                return null;
            }

            NotificationMessage message = datachange.Message;

            if (message == null)
            {
                return null;
            }

            return new ServiceResult(
                datachange.Value.StatusCode,
                datachange.DiagnosticInfo,
                message.StringTable);
        }

        /// <summary>
        /// The service result for a field in an notification (the field must contain a Status object).
        /// </summary>
        public static ServiceResult GetServiceResult(IEncodeable notification, int index)
        {
            if (notification is not EventFieldList eventFields)
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

            if (ExtensionObject.ToEncodeable(
                    eventFields.EventFields[index].Value as ExtensionObject)
                is not StatusResult status)
            {
                return null;
            }

            return new ServiceResult(status.StatusCode, status.DiagnosticInfo, message.StringTable);
        }

        /// <summary>
        /// To save memory the cache is by default not initialized
        /// until <see cref="SaveValueInCache(IEncodeable)"/> is called.
        ///
        /// </summary>
        private void EnsureCacheIsInitialized()
        {
            if (m_dataCache == null && m_eventCache == null)
            {
                if (((int)m_nodeClass & ((int)NodeClass.Object | (int)NodeClass.View)) != 0)
                {
                    m_eventCache = new MonitoredItemEventCache(100);
                }
                else
                {
                    m_dataCache = new MonitoredItemDataCache(1);
                }
            }
        }

        /// <summary>
        /// Throws an exception if the flter cannot be used with the node class.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
                    if (!typeof(DataChangeFilter).IsInstanceOfType(filter))
                    {
                        m_nodeClass = NodeClass.Variable;
                    }

                    break;
                case NodeClass.Object:
                case NodeClass.View:
                    if (!typeof(EventFilter).IsInstanceOfType(filter))
                    {
                        m_nodeClass = NodeClass.Object;
                    }

                    break;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadFilterNotAllowed,
                        "Filters may not be specified for nodes of class '{0}'.",
                        nodeClass);
            }
        }

        /// <summary>
        /// Sets the default event filter.
        /// </summary>
        private void UseDefaultEventFilter()
        {
            EventFilter filter = _ = new EventFilter();

            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.EventId);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.EventType);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.SourceNode);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.SourceName);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Time);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.ReceiveTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.LocalTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Message);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Severity);

            m_filter = filter;
        }

        private string m_relativePath;
        private NodeId m_resolvedNodeId;
        private NodeClass m_nodeClass;
        private int m_samplingInterval;
        private MonitoringFilter m_filter;
        private uint m_queueSize;
        private bool m_discardOldest;
        private static uint s_globalClientHandle;

        private Lock m_cache = new();
        private MonitoredItemDataCache m_dataCache;
        private MonitoredItemEventCache m_eventCache;
        private IEncodeable m_lastNotification;
        private event MonitoredItemNotificationEventHandler m_Notification;
    }

    /// <summary>
    /// The event arguments provided when a new notification message arrives.
    /// </summary>
    public class MonitoredItemNotificationEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal MonitoredItemNotificationEventArgs(IEncodeable notificationValue)
        {
            NotificationValue = notificationValue;
        }

        /// <summary>
        /// The new notification.
        /// </summary>
        public IEncodeable NotificationValue { get; }
    }

    /// <summary>
    /// The delegate used to receive monitored item value notifications.
    /// </summary>
    public delegate void MonitoredItemNotificationEventHandler(
        MonitoredItem monitoredItem,
        MonitoredItemNotificationEventArgs e);

    /// <summary>
    /// A client cache which can hold the last monitored items in a queue.
    /// By default (1) only the last value is cached.
    /// </summary>
    public class MonitoredItemDataCache
    {
        private const int kDefaultMaxCapacity = 100;

        /// <summary>
        /// Constructs a cache for a monitored item.
        /// </summary>
        public MonitoredItemDataCache(int queueSize = 1)
        {
            QueueSize = queueSize;
            if (queueSize > 1)
            {
                m_values = new Queue<DataValue>(Math.Min(queueSize + 1, kDefaultMaxCapacity));
            }
            else
            {
                QueueSize = 1;
            }
        }

        /// <summary>
        /// The size of the queue to maintain.
        /// </summary>
        public int QueueSize { get; private set; }

        /// <summary>
        /// The last value received from the server.
        /// </summary>
        public DataValue LastValue { get; private set; }

        /// <summary>
        /// Returns all values in the queue.
        /// </summary>
        public IList<DataValue> Publish()
        {
            DataValue[] values;
            if (m_values != null)
            {
                values = new DataValue[m_values.Count];
                for (int ii = 0; ii < values.Length; ii++)
                {
                    values[ii] = m_values.Dequeue();
                }
            }
            else
            {
                values = new DataValue[1];
                values[0] = LastValue;
            }
            return values;
        }

        /// <summary>
        /// Saves a notification in the cache.
        /// </summary>
        public void OnNotification(MonitoredItemNotification notification)
        {
            LastValue = notification.Value;
            CoreClientUtils.EventLog
                .NotificationValue(notification.ClientHandle, LastValue.WrappedValue);

            if (m_values != null)
            {
                m_values.Enqueue(notification.Value);
                while (m_values.Count > QueueSize)
                {
                    m_values.Dequeue();
                }
            }
        }

        /// <summary>
        /// Changes the queue size.
        /// </summary>
        public void SetQueueSize(int queueSize)
        {
            if (queueSize == QueueSize)
            {
                return;
            }

            if (queueSize <= 1)
            {
                queueSize = 1;
                m_values = null;
            }
            else
            {
                m_values ??= new Queue<DataValue>(Math.Min(queueSize + 1, kDefaultMaxCapacity));
            }

            QueueSize = queueSize;

            while (m_values.Count > QueueSize)
            {
                m_values.Dequeue();
            }
        }

        private Queue<DataValue> m_values;
    }

    /// <summary>
    /// Saves the events received from the server.
    /// </summary>
    public class MonitoredItemEventCache
    {
        /// <summary>
        /// Constructs a cache for a monitored item.
        /// </summary>
        public MonitoredItemEventCache(int queueSize)
        {
            QueueSize = queueSize;
            m_events = new Queue<EventFieldList>();
        }

        /// <summary>
        /// The size of the queue to maintain.
        /// </summary>
        public int QueueSize { get; private set; }

        /// <summary>
        /// The last event received.
        /// </summary>
        public EventFieldList LastEvent { get; private set; }

        /// <summary>
        /// Returns all events in the queue.
        /// </summary>
        public IList<EventFieldList> Publish()
        {
            var events = new EventFieldList[m_events.Count];

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
            LastEvent = notification;

            while (m_events.Count > QueueSize)
            {
                m_events.Dequeue();
            }
        }

        /// <summary>
        /// Changes the queue size.
        /// </summary>
        public void SetQueueSize(int queueSize)
        {
            if (queueSize == QueueSize)
            {
                return;
            }

            if (queueSize < 1)
            {
                queueSize = 1;
            }

            QueueSize = queueSize;

            while (m_events.Count > QueueSize)
            {
                m_events.Dequeue();
            }
        }

        private readonly Queue<EventFieldList> m_events;
    }
}
