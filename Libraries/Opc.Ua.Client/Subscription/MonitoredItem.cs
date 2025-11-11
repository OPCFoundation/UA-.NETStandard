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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A monitored item.
    /// </summary>
    public class MonitoredItem : ISnapshotRestore<MonitoredItemState>, ICloneable
    {
        private static readonly TimeSpan s_time_epsilon = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        public MonitoredItem(
            ITelemetryContext telemetry,
            MonitoredItemOptions? options = null)
            : this(Utils.IncrementIdentifier(ref s_globalClientHandle), telemetry, options)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class.
        /// </summary>
        /// <param name="clientHandle">The client handle. The caller must ensure it
        /// uniquely identifies the monitored item.</param>
        /// <param name="telemetry"></param>
        /// <param name="options"></param>
        public MonitoredItem(
            uint clientHandle,
            ITelemetryContext telemetry,
            MonitoredItemOptions? options = null)
        {
            State = options ?? new MonitoredItemOptions();
            Status = new MonitoredItemStatus();
            m_logger = telemetry.CreateLogger<MonitoredItem>();
            ClientHandle = clientHandle;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredItem"/> class from a template.
        /// </summary>
        public MonitoredItem(
            MonitoredItem template,
            bool copyEventHandlers = false,
            bool copyClientHandle = false)
        {
            Status = new MonitoredItemStatus();
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            m_logger = template.m_logger;

            State = template.State;
            ClientHandle = 0;
            AttributesModified = true;
            m_logger ??= LoggerUtils.Null.Logger;

            string displayName = template.DisplayName;
            if (displayName != null)
            {
                int index = displayName.LastIndexOf(' ');
                if (index != -1)
                {
                    displayName = displayName[..index];
                }
            }

            Handle = template.Handle;
            DisplayName = Utils.Format("{0} {1}", displayName, ClientHandle);
            // copy state (except client handle logic handled below)
            State = template.State with { DisplayName = DisplayName };
            if (copyEventHandlers)
            {
                m_Notification = template.m_Notification;
            }
            if (copyClientHandle)
            {
                ClientHandle = template.ClientHandle;
            }
            else
            {
                ClientHandle = Utils.IncrementIdentifier(ref s_globalClientHandle);
            }
            // ensure state consistency with node class transitions
            NodeClass = State.NodeClass;
        }

        /// <summary>
        /// Public parameterless ctor for serialization/deserialization scenarios.
        /// </summary>
        [Obsolete("Use constructor with ITelemetryContext argument")]
        public MonitoredItem()
            : this(null!, null)
        {
        }

        /// <inheritdoc/>
        public virtual void Restore(MonitoredItemState state)
        {
            State = state;
            ClientHandle = state.ClientId;
            ServerId = state.ServerId;
        }

        /// <inheritdoc/>
        public virtual void Snapshot(out MonitoredItemState state)
        {
            state = new MonitoredItemState(State)
            {
                ServerId = Status.Id,
                ClientId = ClientHandle
            };
        }

        /// <summary>
        /// Monitored item state/options.
        /// </summary>
        public MonitoredItemOptions State { get; internal set; }

        /// <summary>
        /// A display name for the monitored item
        /// </summary>
        public string DisplayName
        {
            get => State.DisplayName ?? "MonitoredItem";
            set => State = State with { DisplayName = value };
        }

        /// <summary>
        /// The start node id
        /// </summary>
        public NodeId StartNodeId
        {
            get => State.StartNodeId;
            set => State = State with { StartNodeId = value ?? NodeId.Null };
        }

        /// <summary>
        /// The relative path
        /// </summary>
        public string? RelativePath
        {
            get => State.RelativePath;
            set
            {
                if (value != State.RelativePath)
                {
                    m_resolvedNodeId = NodeId.Null;
                }
                State = State with { RelativePath = value };
            }
        }

        /// <summary>
        /// The node class
        /// </summary>
        public NodeClass NodeClass
        {
            get => State.NodeClass;
            set
            {
                if (State.NodeClass == value)
                {
                    return;
                }
                if (((int)value & ((int)NodeClass.Object | (int)NodeClass.View)) != 0)
                {
                    State = State with
                    {
                        NodeClass = value,
                        AttributeId = Attributes.EventNotifier,
                        QueueSize = State.QueueSize <= 1 ? int.MaxValue : State.QueueSize,
                        Filter = State.Filter is not EventFilter ?
                            GetDefaultEventFilter() : State.Filter
                    };
                }
                else
                {
                    State = State with
                    {
                        NodeClass = value,
                        AttributeId = Attributes.Value,
                        QueueSize = State.QueueSize == int.MaxValue ? 1 : State.QueueSize,
                        Filter = State.Filter is EventFilter ?
                            null : State.Filter,
                    };
                }
                m_dataCache = null;
                m_eventCache = null;
            }
        }

        /// <summary>
        /// The attribute id
        /// </summary>
        public uint AttributeId
        {
            get => State.AttributeId;
            set => State = State with { AttributeId = value };
        }

        /// <summary>
        /// The index range
        /// </summary>
        public string? IndexRange
        {
            get => State.IndexRange;
            set => State = State with { IndexRange = value };
        }

        /// <summary>
        /// The data encoding
        /// </summary>
        public QualifiedName Encoding
        {
            get => State.Encoding;
            set => State = State with { Encoding = value ?? QualifiedName.Null };
        }

        /// <summary>
        /// The monitoring mode
        /// </summary>
        public MonitoringMode MonitoringMode
        {
            get => State.MonitoringMode;
            set => State = State with { MonitoringMode = value };
        }

        /// <summary>
        /// The sampling interval
        /// </summary>
        public int SamplingInterval
        {
            get => State.SamplingInterval;
            set
            {
                if (State.SamplingInterval != value)
                {
                    AttributesModified = true;
                }
                State = State with { SamplingInterval = value };
            }
        }

        /// <summary>
        /// The monitoring filter
        /// </summary>
        public MonitoringFilter? Filter
        {
            get => State.Filter;
            set
            {
                if (!Equals(State.Filter, value))
                {
                    ValidateFilter(NodeClass, value);
                    AttributesModified = true;
                }
                State = State with { Filter = value };
            }
        }

        /// <summary>
        /// The queue size
        /// </summary>
        public uint QueueSize
        {
            get => State.QueueSize;
            set
            {
                if (State.QueueSize != value)
                {
                    AttributesModified = true;
                }
                State = State with { QueueSize = value };
            }
        }

        /// <summary>
        /// Discard oldest when full
        /// </summary>
        public bool DiscardOldest
        {
            get => State.DiscardOldest;
            set
            {
                if (State.DiscardOldest != value)
                {
                    AttributesModified = true;
                }
                State = State with { DiscardOldest = value };
            }
        }

        /// <summary>
        /// Server-assigned id for the MonitoredItem.
        /// </summary>
        public uint ServerId
        {
            get => Status.Id;
            set => Status.Id = value;
        }

        /// <summary>
        /// The subscription that owns the monitored item.
        /// </summary>
        public Subscription? Subscription
        {
            get => m_subscription;
            internal set
            {
                if (m_subscription == null && value?.Telemetry != null)
                {
                    m_logger = value.Telemetry.CreateLogger<MonitoredItem>();
                }
                m_subscription = value;
            }
        }

        /// <summary>
        /// A local handle assigned to the monitored item.
        /// </summary>
        public object? Handle { get; set; }

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
                if (string.IsNullOrEmpty(State.RelativePath))
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
        public bool AttributesModified { get; private set; } = true;

        /// <summary>
        /// The status associated with the monitored item.
        /// </summary>
        public MonitoredItemStatus Status { get; }

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
        public IEncodeable? LastValue
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
        public NotificationMessage? LastMessage
        {
            get
            {
                lock (m_cache)
                {
                    if (m_dataCache != null)
                    {
                        return ((MonitoredItemNotification?)m_lastNotification)?.Message;
                    }

                    if (m_eventCache != null)
                    {
                        return ((EventFieldList?)m_lastNotification)?.Message;
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
                                m_logger.LogWarning(
                                    "Received ServerTimestamp {ServerTimestamp} is in the future for MonitoredItemId {MonitoredItemId}",
                                    datachange.Value.ServerTimestamp.ToLocalTime(),
                                    ClientHandle);
                            }

                            // validate SourceTimestamp of the notification.
                            if (datachange.Value.SourceTimestamp > now)
                            {
                                m_logger.LogWarning(
                                    "Received SourceTimestamp {SourceTimestamp} is in the future for MonitoredItemId {MonitoredItemId}",
                                    datachange.Value.SourceTimestamp.ToLocalTime(),
                                    ClientHandle);
                            }
                        }

                        if (datachange.Value.StatusCode.Overflow)
                        {
                            m_logger.LogWarning(
                                "Overflow bit set for data change with ServerTimestamp {ServerTimestamp} " +
                                "and value {Value} for MonitoredItemId {MonitoredItemId}",
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
        protected internal void SetResolvePathResult(
            BrowsePathResult result,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            ServiceResult? error = null;

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
                if (result.Targets.Count > 0 && Subscription?.Session != null)
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
        protected internal void SetCreateResult(
            MonitoredItemCreateRequest request,
            MonitoredItemCreateResult result,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            ServiceResult? error = null;

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
        protected internal void SetModifyResult(
            MonitoredItemModifyRequest request,
            MonitoredItemModifyResult result,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            ServiceResult? error = null;

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
        protected internal void SetTransferResult(uint clientHandle)
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
        protected internal void SetDeleteResult(
            StatusCode result,
            int index,
            DiagnosticInfoCollection? diagnosticInfos,
            ResponseHeader? responseHeader)
        {
            ServiceResult? error = null;

            if (StatusCode.IsBad(result))
            {
                error = ClientBase.GetResult(result, index, diagnosticInfos, responseHeader);
            }

            Status.SetDeleteResult(error);
        }

        /// <summary>
        /// Returns the field name the specified SelectClause in the EventFilter.
        /// </summary>
        public string? GetFieldName(int index)
        {
            if (State.Filter is not EventFilter filter)
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
        public object? GetFieldValue(
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
        public object? GetFieldValue(
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
        public object? GetFieldValue(
            EventFieldList eventFields,
            NodeId eventTypeId,
            IList<QualifiedName> browsePath,
            uint attributeId)
        {
            if (eventFields == null)
            {
                return null;
            }

            if (State.Filter is not EventFilter filter)
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
        public async ValueTask<INode?> GetEventTypeAsync(
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
        public static ServiceResult? GetServiceResult(IEncodeable notification)
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
        /// The service result for a field in an notification
        /// (the field must contain a Status object).
        /// </summary>
        public static ServiceResult? GetServiceResult(IEncodeable notification, int index)
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
        /// </summary>
        private void EnsureCacheIsInitialized()
        {
            if (m_dataCache == null && m_eventCache == null)
            {
                if (((int)State.NodeClass & ((int)NodeClass.Object | (int)NodeClass.View)) != 0)
                {
                    m_eventCache = new MonitoredItemEventCache(100);
                }
                else
                {
                    m_dataCache = new MonitoredItemDataCache(Subscription?.Telemetry, 1);
                }
            }
        }

        /// <summary>
        /// Throws an exception if the flter cannot be used with the node class.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateFilter(NodeClass nodeClass, MonitoringFilter? filter)
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
                        State = State with { NodeClass = NodeClass.Variable };
                    }

                    break;
                case NodeClass.Object:
                case NodeClass.View:
                    if (!typeof(EventFilter).IsInstanceOfType(filter))
                    {
                        State = State with { NodeClass = NodeClass.Object };
                    }

                    break;
                case NodeClass.Method:
                case NodeClass.ObjectType:
                case NodeClass.ReferenceType:
                case NodeClass.DataType:
                case NodeClass.Unspecified:
                    throw ServiceResultException.Create(
                        StatusCodes.BadFilterNotAllowed,
                        "Filters may not be specified for nodes of class '{0}'.",
                        nodeClass);
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass: {nodeClass}.");
            }
        }

        /// <summary>
        /// Sets the default event filter.
        /// </summary>
        private static EventFilter GetDefaultEventFilter()
        {
            var filter = new EventFilter();

            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.EventId);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.EventType);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.SourceNode);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.SourceName);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Time);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.ReceiveTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.LocalTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Message);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Severity);

            return filter;
        }

        private static uint s_globalClientHandle;
        private NodeId m_resolvedNodeId = NodeId.Null;
        private Subscription? m_subscription;
        private ILogger m_logger = LoggerUtils.Null.Logger;
        private readonly Lock m_cache = new();
        private MonitoredItemDataCache? m_dataCache;
        private MonitoredItemEventCache? m_eventCache;
        private IEncodeable? m_lastNotification;
        private event MonitoredItemNotificationEventHandler? m_Notification;
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
        /// <summary>
        /// Constructs a cache for a monitored item.
        /// </summary>
        public MonitoredItemDataCache(ITelemetryContext? telemetry, int queueSize = 1)
        {
            QueueSize = queueSize;
            m_logger = telemetry.CreateLogger<MonitoredItemDataCache>();
            if (queueSize > 1)
            {
                m_values = new ConcurrentQueue<DataValue>();
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
        public DataValue? LastValue { get; private set; }

        /// <summary>
        /// Returns all values in the queue.
        /// </summary>
        public IList<DataValue> Publish()
        {
            List<DataValue> values;
            if (m_values != null)
            {
                values = new List<DataValue>(m_values.Count);
                for (int ii = 0; ii < values.Count; ii++)
                {
                    if (!m_values.TryDequeue(out DataValue? dequeued))
                    {
                        break;
                    }
                    values.Add(dequeued);
                }
            }
            else
            {
                values = [LastValue];
            }
            return values;
        }

        /// <summary>
        /// Saves a notification in the cache.
        /// </summary>
        public void OnNotification(MonitoredItemNotification notification)
        {
            LastValue = notification.Value;
            CoreClientUtils.EventLog.Notification(
                (int)notification.ClientHandle,
                LastValue.WrappedValue);

            m_logger.LogDebug(
                "Notification: ClientHandle={ClientHandle}, Value={Value}, SourceTime={SourceTime}",
                notification.ClientHandle,
                notification.Value.WrappedValue,
                notification.Value.SourceTimestamp);

            if (m_values != null)
            {
                m_values.Enqueue(notification.Value);
                while (m_values.Count > QueueSize)
                {
                    if (!m_values.TryDequeue(out DataValue? dropped))
                    {
                        break;
                    }
                    m_logger.LogInformation(
                        "Dropped value: ClientHandle={ClientHandle}, Value={Value}, SourceTime={SourceTime}",
                        notification.ClientHandle,
                        dropped.WrappedValue,
                        dropped.SourceTimestamp);
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
                m_values ??= new ConcurrentQueue<DataValue>();
            }

            QueueSize = queueSize;

            if (m_values == null)
            {
                return;
            }
            while (m_values.Count > QueueSize)
            {
                if (!m_values.TryDequeue(out DataValue? dropped))
                {
                    break;
                }
                m_logger.LogDebug(
                    "Setting queue size dropped value: Value={Value}, SourceTime={SourceTime}",
                    dropped.WrappedValue,
                    dropped.SourceTimestamp);
            }
        }

        private ConcurrentQueue<DataValue>? m_values;
        private readonly ILogger m_logger;
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
        public EventFieldList? LastEvent { get; private set; }

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
