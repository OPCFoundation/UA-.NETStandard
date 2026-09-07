/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A handle that describes how to access a node/attribute via an i/o manager.
    /// </summary>
    public class MonitoredItem :
        IEventMonitoredItem,
        ISampledDataChangeMonitoredItem,
        ITriggeredMonitoredItem,
        IDetachableMonitoredItem,
        IMonitoredItemTransferState
    {
        /// <summary>
        /// Initializes the object with its node type.
        /// </summary>
        [Obsolete("Use the overload that accepts IAsyncNodeManager.")]
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
            MonitoringFilter? originalFilter,
            MonitoringFilter? filterToUse,
            Range? range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            double sourceSamplingInterval,
            bool createDurable = false)
            : this(
                server,
                nodeManager.ToAsyncNodeManager(),
                managerHandle,
                subscriptionId,
                id,
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
                sourceSamplingInterval,
                createDurable)
        {
        }

        /// <summary>
        /// Initializes the object with its node type.
        /// </summary>
        public MonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            uint subscriptionId,
            uint id,
            ReadValueId itemToMonitor,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            MonitoringFilter? originalFilter,
            MonitoringFilter? filterToUse,
            Range? range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            double sourceSamplingInterval,
            bool createDurable = false)
            : this(
                server,
                nodeManager,
                managerHandle,
                subscriptionId,
                id,
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
                sourceSamplingInterval,
                createDurable,
                null)
        {
        }

        /// <summary>
        /// Initializes the object with its node type and an explicit
        /// <see cref="TimeProvider"/> so the monotonic sampling clock can be
        /// mocked in tests.
        /// </summary>
        public MonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            uint subscriptionId,
            uint id,
            ReadValueId itemToMonitor,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            MonitoringFilter? originalFilter,
            MonitoringFilter? filterToUse,
            Range? range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            double sourceSamplingInterval,
            bool createDurable,
            TimeProvider? timeProvider)
        {
            if (itemToMonitor == null)
            {
                throw new ArgumentNullException(nameof(itemToMonitor));
            }

            m_logger = server.Telemetry.CreateLogger<MonitoredItem>();
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;

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
            m_cachedDataChangeFilter = filterToUse as DataChangeFilter;
            m_range = 0;
            m_samplingInterval = samplingInterval;
            QueueSize = queueSize;
            m_discardOldest = discardOldest;
            m_sourceSamplingInterval = (int)sourceSamplingInterval;
            m_calculator = null;
            m_nextSamplingTime = m_timeProvider.GetTimestampMilliseconds();
            AlwaysReportUpdates = false;
            m_monitoredItemQueueFactory = m_server.MonitoredItemQueueFactory;
            m_subscriptionStore = m_server.SubscriptionStore;
            IsDurable = createDurable;

            if (!m_monitoredItemQueueFactory.SupportsDurableQueues && IsDurable)
            {
                m_logger.DurableSubscriptionWasCreateButNoMonitoredItemQueueFactory(id, subscriptionId);
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
                    aggregateFilter!.AggregateType,
                    (DateTime)aggregateFilter.StartTime,
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
                m_filterToUse!,
                MonitoringMode);

            InitializeQueue();
        }

        /// <summary>
        /// Restore a MonitoredItem afer a restart.
        /// </summary>
        public MonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            IStoredMonitoredItem storedMonitoredItem)
            : this(server, nodeManager, managerHandle, storedMonitoredItem, null)
        {
        }

        /// <summary>
        /// Restore a MonitoredItem afer a restart with an explicit
        /// <see cref="TimeProvider"/>.
        /// </summary>
        public MonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            IStoredMonitoredItem storedMonitoredItem,
            TimeProvider? timeProvider)
        {
            if (storedMonitoredItem == null)
            {
                throw new ArgumentNullException(nameof(storedMonitoredItem));
            }
            m_logger = server.Telemetry.CreateLogger<MonitoredItem>();
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;

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
            m_cachedDataChangeFilter = storedMonitoredItem.FilterToUse as DataChangeFilter;
            m_range = storedMonitoredItem.Range;
            m_samplingInterval = storedMonitoredItem.SamplingInterval;
            QueueSize = storedMonitoredItem.QueueSize;
            m_discardOldest = storedMonitoredItem.DiscardOldest;
            m_sourceSamplingInterval = storedMonitoredItem.SourceSamplingInterval;
            m_calculator = null;
            m_nextSamplingTime = m_timeProvider.GetTimestampMilliseconds();
            m_monitoredItemQueueFactory = m_server.MonitoredItemQueueFactory;
            m_subscriptionStore = m_server.SubscriptionStore;
            m_restoredDataChangeQueue = storedMonitoredItem.RestoredDataChangeQueue;
            m_restoredEventQueue = storedMonitoredItem.RestoredEventQueue;
            IsDurable = storedMonitoredItem.IsDurable;
            AlwaysReportUpdates = storedMonitoredItem.AlwaysReportUpdates;
            m_lastError = storedMonitoredItem.LastError;
            m_lastValue = storedMonitoredItem.LastValue;
            MonitoredItemType = storedMonitoredItem.TypeMask;

            // without this the first transition out of filter scope after a restart is
            // dropped, because the item would not know the client had been told about the
            // condition.
            ArrayOf<string> filteredRetainConditionIds =
                storedMonitoredItem.FilteredRetainConditionIds;

            if (!filteredRetainConditionIds.IsEmpty)
            {
                m_filteredRetainConditionIds = [.. filteredRetainConditionIds];
            }

            // create aggregate calculator.
            if (storedMonitoredItem.FilterToUse is ServerAggregateFilter aggregateFilter)
            {
                m_calculator = m_server.AggregateManager.CreateCalculator(
                    aggregateFilter.AggregateType,
                    (DateTime)aggregateFilter.StartTime,
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
                m_filterToUse!,
                MonitoringMode);

            RestoreQueue();

            m_isDeleted = storedMonitoredItem.IsDeleted;
            m_isDetached = storedMonitoredItem.IsDetached;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_server = null!;
            NodeManager = null!;
            ManagerHandle = null!;
            SubscriptionId = 0;
            Id = 0;
            NodeId = default;
            AttributeId = 0;
            m_indexRange = null;
            m_parsedIndexRange = default;
            DataEncoding = default;
            ClientHandle = 0;
            MonitoringMode = MonitoringMode.Disabled;
            m_samplingInterval = 0;
            QueueSize = 0;
            m_discardOldest = true;
            Filter = null!;
            m_lastValue = default;
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
        public IAsyncNodeManager NodeManager { get; private set; }

        /// <inheritdoc/>
        bool IDetachableMonitoredItem.IsDetached
        {
            get
            {
                lock (m_lock)
                {
                    return m_isDetached;
                }
            }
        }

        /// <inheritdoc/>
        bool IDetachableMonitoredItem.IsDeleted
        {
            get
            {
                lock (m_lock)
                {
                    return m_isDeleted;
                }
            }
        }

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
                    return false;
                }

                // check if it has been triggered.
                if (MonitoringMode != MonitoringMode.Disabled && m_triggered)
                {
                    return true;
                }

                // check if monitoring was turned off.
                if (MonitoringMode != MonitoringMode.Reporting)
                {
                    return false;
                }

                if (m_sourceSamplingInterval == 0)
                {
                    // re-queue if too little time has passed since the last publish, in case it doesn't ResendData
                    long now = m_timeProvider.GetTimestampMilliseconds();

                    if (m_nextSamplingTime > now)
                    {
                        return false;
                    }
                }
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
                // only allow to trigger if sampling or reporting.
                if (MonitoringMode == MonitoringMode.Disabled)
                {
                    return false;
                }

                return Volatile.Read(ref m_readyToTrigger);
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
        public bool IsResendData => Volatile.Read(ref m_resendData);

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

        void IMonitoredItemTransferState.RestoreResendDataTrigger(bool resendData)
        {
            lock (m_lock)
            {
                m_resendData = resendData;
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
                    m_logger.SetTriggeredId(Id, SubscriptionId);
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

        /// <inheritdoc/>
        bool IDetachableMonitoredItem.TryBeginAttach()
        {
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return false;
                }

                m_isAttaching = true;
                return true;
            }
        }

        /// <inheritdoc/>
        bool IDetachableMonitoredItem.EndAttach()
        {
            lock (m_lock)
            {
                m_isAttaching = false;
                if (!m_isDisposed)
                {
                    return true;
                }
            }

            // The item was deleted and disposed while it was being handed to the replacement, so
            // the teardown that Dispose deferred runs now and the caller has to undo the attach.
            DisposeQueueHandlers();
            return false;
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.MarkNodeDeleted()
        {
            lock (m_lock)
            {
                m_isDeleted = true;
                QueueNodeIdUnknown();
            }
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.BeginDetach()
        {
            lock (m_lock)
            {
                m_isDetached = true;
            }
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.Detach(IServerInternal server)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            IAsyncNodeManager owner = GetDetachedOwner(server);

            lock (m_lock)
            {
                NodeManager = owner;
                ManagerHandle = DetachedHandle;
                m_isDetached = true;
            }
        }

        /// <summary>
        /// Returns the long lived NodeManager that a detached MonitoredItem is parked on. The
        /// CoreNodeManager is used because it outlives every NodeManager that can be retired.
        /// </summary>
        /// <param name="server">The server that owns the NodeManagers.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The server has no CoreNodeManager.</exception>
        internal static IAsyncNodeManager GetDetachedOwner(IServerInternal server)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            return server.NodeManager?.CoreNodeManager ??
                server.CoreNodeManager ??
                throw new InvalidOperationException(
                    "The server does not have a long lived CoreNodeManager for detached monitored items.");
        }

        /// <summary>
        /// Gets the handle a detached MonitoredItem is parked on.
        /// </summary>
        internal static object DetachedHandle => s_detachedHandle;

        /// <inheritdoc/>
        void IDetachableMonitoredItem.QueueNodeIdUnknown()
        {
            lock (m_lock)
            {
                if (!m_isDeleted)
                {
                    m_isDeleted = true;
                }

                QueueNodeIdUnknown();
            }
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.Rebind(IAsyncNodeManager nodeManager, object managerHandle)
        {
            lock (m_lock)
            {
                NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
                ManagerHandle = managerHandle;
                m_isDetached = false;
                m_isDeleted = false;
                }
        }

        /// <summary>
        /// The filter used by the monitored item.
        /// </summary>
        public MonitoringFilter? Filter { get; private set; }

        /// <summary>
        /// The event filter used by the monitored item.
        /// </summary>
        public EventFilter? EventFilter => Filter as EventFilter;

        /// <summary>
        /// The data change filter used by the monitored item.
        /// </summary>
        public DataChangeFilter? DataChangeFilter => Filter as DataChangeFilter;

        /// <summary>
        /// The session that owns the monitored item.
        /// </summary>
        public ISession Session
        {
            get
            {
                lock (m_lock)
                {
                    return m_subscription?.Session!;
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
                    return m_subscription?.EffectiveIdentity!;
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
                    result.StatusCode = m_samplingError!.Code;
                }

                return m_samplingError!;
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
                    result.StatusCode = m_samplingError!.Code;
                }

                return m_samplingError!;
            }
        }

        /// <summary>
        /// Modifies the attributes for monitored item.
        /// </summary>
        public ServiceResult? ModifyAttributes(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            MonitoringFilter? originalFilter,
            MonitoringFilter? filterToUse,
            Range? range,
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

                MonitoringFilter? previousFilterToUse = m_filterToUse;

                Filter = originalFilter;
                m_filterToUse = filterToUse;
                m_cachedDataChangeFilter = filterToUse as DataChangeFilter;

                DiscardFilteredRetainStateOnWhereClauseChange(previousFilterToUse, filterToUse);

                if (range != null)
                {
                    m_range = range.High - range.Low;
                }

                SetSamplingInterval(samplingInterval);
                QueueSize = queueSize;

                // check if aggregate filter has been updated.
                if (filterToUse is ServerAggregateFilter aggregateFilter)
                {
                    ServerAggregateFilter existingFilter = aggregateFilter;

                    bool match = true;

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
                            (DateTime)aggregateFilter.StartTime,
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
                    m_filterToUse!,
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

                m_logger.MONITORINGMODEMonitoredItemIdPreviousNew(
                    Id,
                    MonitoringMode,
                    monitoringMode,
                    SubscriptionId);

                if (previousMode == MonitoringMode.Disabled)
                {
                    m_nextSamplingTime = m_timeProvider.GetTimestampMilliseconds();
                    m_lastError = null;
                    m_lastValue = default;
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
                    m_filterToUse!,
                    MonitoringMode);

                InitializeQueue();

                return previousMode;
            }
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        public virtual void QueueValue(in DataValue value, ServiceResult? error)
        {
            QueueValue(in value, error, false);
        }

        /// <summary>
        /// Updates the queue with a data value or an error.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void QueueValue(in DataValue value, ServiceResult? error, bool ignoreFilters)
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

                DataValue current = value;

                // make a shallow copy of the value.
                if (!current.IsNull)
                {
                    m_logger.RECEIVEDVALUEMonitoredItemIdValueValue(
                        Id,
                        current.WrappedValue,
                        SubscriptionId);

                    current = current.Copy();

                    // ensure the data value matches the error status code.
                    if (error != null && error.StatusCode.Code != 0)
                    {
                        current = current.WithStatus(error.StatusCode);
                    }
                }

                // create empty value if none provided.
                if (ServiceResult.IsBad(error) && current.IsNull)
                {
                    DateTime utcNow = m_timeProvider.GetUtcNow().UtcDateTime;
                    current = new DataValue(
                        Variant.Null,
                        error!.StatusCode,
                        utcNow,
                        utcNow);
                }

                // this should never happen.
                if (current.IsNull)
                {
                    return;
                }

                // apply aggregate filter.
                if (m_calculator != null)
                {
                    if (!m_calculator.QueueRawValue(current) &&
                        m_logger.IsEnabled(LogLevel.Trace))
                    {
                        m_logger.ValueReceivedOutOfOrderSourceTimestampServerHandle(
                            current.SourceTimestamp
                                .ToLocalTime()
                                .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            Id,
                            SubscriptionId);
                    }

                    while (m_calculator.TryGetProcessedValue(false, out DataValue processedValue))
                    {
                        AddValueToQueue(processedValue, null!);
                    }

                    return;
                }

                // apply filter to incoming item.
                if (!ignoreFilters && !AlwaysReportUpdates && !ApplyFilter(current, error!))
                {
                    ServerUtils.ReportFilteredValue(NodeId, Id, current);
                    return;
                }

                ServerUtils.ReportQueuedValue(NodeId, Id, current);

                // add the value to the queue.
                AddValueToQueue(current, error!);
            }
        }

        /// <summary>
        /// Adds a value to the queue.
        /// </summary>
        private void AddValueToQueue(DataValue value, ServiceResult error)
        {
            bool overflow = false;
            if (QueueSize > 1)
            {
                overflow = m_dataChangeQueueHandler!.QueueValue(value, error);
            }

            if (!m_lastValue.IsNull)
            {
                m_readyToTrigger = true;
            }

            // save last value received.
            m_lastValue = value;
            m_lastError = error;
            m_readyToPublish = true;

            m_logger.QUEUEVALUEMonitoredItemIdValueValueCODECode(
                Id,
                m_lastValue.WrappedValue,
                m_lastValue.StatusCode.Code,
                overflow,
                SubscriptionId);
        }

        /// <summary>
        /// Whether the item is monitoring all events produced by the server.
        /// </summary>
        public bool MonitoringAllEvents => NodeId == ObjectIds.Server;

        /// <summary>
        /// Fetches the event fields from the event.
        /// </summary>
        private EventFieldList GetEventFields(
            IFilterContext context,
            EventFilter filter,
            IFilterTarget instance)
        {
            // fetch the event fields.
            var eventFieldValues = new List<Variant>();
            foreach (SimpleAttributeOperand clause in filter.SelectClauses)
            {
                // get the value of the attribute (apply localization).
                Variant value = instance.GetAttributeValue(
                    context,
                    clause.TypeDefinitionId,
                    clause.BrowsePath,
                    clause.AttributeId,
                    clause.ParsedIndexRange);

                // add the value to the list of event fields.
                if (!value.IsNull)
                {
                    // translate any localized text.
                    if (value.TryGetValue(out LocalizedText text))
                    {
                        value = m_server.ResourceManager.Translate(Session?.PreferredLocales!, text);
                    }

                    // add value.
                    eventFieldValues.Add(value);
                }
                // add a dummy entry for missing values.
                else
                {
                    eventFieldValues.Add(Variant.Null);
                }
            }
            var result = (EventFieldList)EventFieldListActivator.Instance.CreateInstance();
            result.ClientHandle = ClientHandle;
            result.Handle = instance;
            result.EventFields = eventFieldValues;
            return result;
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
                    Session?.PreferredLocales!,
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
                m_eventQueueHandler!.QueueEvent(fields);
                m_readyToPublish = true;
                m_readyToTrigger = true;
            }
        }

        /// <summary>
        /// Determines whether an event can be sent with SupportsFilteredRetain in consideration.
        /// </summary>
        /// <remarks>
        /// Filtered retain (OPC UA Part 9, B.1.4) lets a condition report one final event as it
        /// leaves the scope of this client's where clause, so the client sees Retain go false
        /// even though the condition itself is unchanged on the server. The item therefore
        /// remembers which conditions currently pass its where clause; a condition that drops
        /// out of that set is delivered once more and then forgotten.
        /// <para>
        /// The condition behind <paramref name="instance"/> is resolved from either an
        /// <see cref="InstanceStateSnapshot"/> - the shape <c>ReportEvent</c> produces - or a
        /// <see cref="ConditionState"/> handed over directly, which is what
        /// <c>ConditionState.ConditionRefresh</c> puts into the refresh event list. Any other
        /// <see cref="IFilterTarget"/> cannot be mapped onto a condition, so a server that
        /// queues its own filter targets falls back to plain where clause evaluation and
        /// filtered retain has no effect for those events.
        /// </para>
        /// </remarks>
        protected bool CanSendFilteredAlarm(
            IFilterContext context,
            EventFilter filter,
            IFilterTarget instance)
        {
            bool passedFilter = filter.WhereClause.Evaluate(context, instance);

            ConditionState? alarmCondition = GetFilteredRetainCondition(instance);

            if (alarmCondition == null || filter.SelectClauses.IsNull)
            {
                return passedFilter;
            }

            HashSet<string> conditionIds = GetFilteredRetainConditionIds();
            string key = GetFilteredRetainKey(alarmCondition);

            // the key is present exactly while the condition passed the where clause the
            // last time it was evaluated, so removing it here both consumes the entry for
            // the transition out of scope and clears the way for re-priming it below.
            bool wasInFilterScope = conditionIds.Remove(key);

            if (passedFilter)
            {
                // Archie - December 17 2024
                // Whether the condition should only be tracked while Retain is true is an
                // open question with the Part 9 editor; it is tracked in
                // https://github.com/OPCFoundation/UA-.NETStandard/issues/4370. Until that
                // is settled a condition is tracked whenever it passes the where clause,
                // which means it always produces one trailing event on the way out - even
                // if Retain was already false when it passed.
                conditionIds.Add(key);
                return true;
            }

            // out of scope now: send the trailing event if it was in scope before.
            return wasInFilterScope;
        }

        /// <summary>
        /// Returns the condition that <paramref name="instance"/> reports on when that
        /// condition opted into filtered retain, otherwise <c>null</c>.
        /// </summary>
        private static ConditionState? GetFilteredRetainCondition(IFilterTarget instance)
        {
            ConditionState? condition = instance switch
            {
                InstanceStateSnapshot snapshot => snapshot.Handle as ConditionState,
                ConditionState state => state,
                _ => null
            };

            return condition?.SupportsFilteredRetain?.Value == true ? condition : null;
        }

        /// <summary>
        /// Builds the key a condition is tracked under.
        /// </summary>
        /// <remarks>
        /// A branch shares its parent's NodeId - branches are told apart by BranchId - so the
        /// NodeId alone would make a condition and all of its branches contend for a single
        /// entry, and one branch leaving filter scope would consume the entry another branch
        /// relies on.
        /// </remarks>
        private static string GetFilteredRetainKey(ConditionState condition)
        {
            return Utils.Format(
                "{0}|{1}",
                condition.NodeId,
                condition.BranchId?.Value ?? default);
        }

        private HashSet<string> GetFilteredRetainConditionIds()
        {
            return m_filteredRetainConditionIds ??= [];
        }

        /// <summary>
        /// Drops the filtered retain bookkeeping when the where clause it was derived from
        /// is replaced.
        /// </summary>
        /// <remarks>
        /// Every entry means "this condition passed the previous where clause". Keeping
        /// those across a ModifyMonitoredItems that changes the where clause would produce
        /// a trailing event against a filter that never saw the condition pass. The select
        /// clauses do not take part in the decision, since they only shape the fields of an
        /// event that is being sent anyway.
        /// <para>
        /// A monitoring mode change deliberately keeps the state. A disabled item does not
        /// evaluate events at all, so the entries still describe what the client was last
        /// told, and the trailing event is owed to it once reporting resumes.
        /// </para>
        /// </remarks>
        private void DiscardFilteredRetainStateOnWhereClauseChange(
            MonitoringFilter? previousFilter,
            MonitoringFilter? newFilter)
        {
            if (m_filteredRetainConditionIds == null ||
                m_filteredRetainConditionIds.Count == 0)
            {
                return;
            }

            ContentFilter? previousWhereClause = (previousFilter as EventFilter)?.WhereClause;
            ContentFilter? newWhereClause = (newFilter as EventFilter)?.WhereClause;

            if (!Utils.IsEqual(previousWhereClause, newWhereClause))
            {
                m_filteredRetainConditionIds.Clear();
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
            long now = m_timeProvider.GetTimestampMilliseconds();
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
                    m_logger.MONITOREDITEMPublishQueueSizeQueueSize(
                        notifications.Count,
                        SubscriptionId,
                        Id);

                    EventFieldList? overflowEvent = null;

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

                        // fetch the event fields. The overflow path is reached only
                        // when m_eventQueueHandler is active, which guarantees an
                        // EventFilter has been configured for this monitored item.
                        overflowEvent = GetEventFields(
                            new FilterContext(
                                m_server.NamespaceUris,
                                m_server.TypeTree,
                                Session?.PreferredLocales!,
                                m_server.Telemetry),
                            (EventFilter)m_filterToUse!,
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

                    m_logger.MONITOREDITEMPublishQueueSizeQueueSize(
                        notifications.Count,
                        SubscriptionId,
                        Id);
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
                    if (m_calculator != null &&
                        m_calculator.HasEndTimePassed(DateTime.UtcNow))
                    {
                        while (m_calculator.TryGetProcessedValue(false, out DataValue processedValue))
                        {
                            AddValueToQueue(processedValue, null!);
                        }

                        if (m_calculator.TryGetProcessedValue(true, out DataValue partialValue))
                        {
                            AddValueToQueue(partialValue, null!);
                        }
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
                    m_logger.DequeueValue(
                        m_lastValue.WrappedValue,
                        m_lastValue.StatusCode.Code,
                        m_lastValue.StatusCode.Overflow,
                        Id);
                    Publish(context, notifications, diagnostics, m_lastValue, m_lastError!);
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
        /// Publishes a single data change notification.
        /// </summary>
        /// <param name="context">The context of the Publish request.</param>
        /// <param name="notifications">The queue the notification is added to.</param>
        /// <param name="diagnostics">The queue the diagnostic info is added to.</param>
        /// <param name="value">The value to publish.</param>
        /// <param name="error">The error that belongs to the value.</param>
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
                value = value.WithStatus(value.StatusCode.SetSemanticsChanged(true));

                if (error != null)
                {
                    error = new ServiceResult(
                        error.NamespaceUri,
                        error.StatusCode.SetSemanticsChanged(true),
                        error.LocalizedText,
                        error.AdditionalInfo,
                        error.InnerResult);
                }

                m_semanticsChanged = false;
            }

            // set structure changed bit.
            if (m_structureChanged)
            {
                value = value.WithStatus(value.StatusCode.SetStructureChanged(true));

                if (error != null)
                {
                    error = new ServiceResult(
                        error.NamespaceUri,
                        error.StatusCode.SetStructureChanged(true),
                        error.LocalizedText,
                        error.AdditionalInfo,
                        error.InnerResult);
                }

                m_structureChanged = false;
            }

            // copy data value.
            var item = (MonitoredItemNotification)MonitoredItemNotificationActivator.Instance.CreateInstance();
            item.ClientHandle = ClientHandle;
            item.Value = value!;

            // apply timestamp filter.
            if (m_timestampsToReturn is not TimestampsToReturn.Server and not TimestampsToReturn.Both)
            {
                item.Value = item.Value!.WithServerTimestamp(DateTimeUtc.MinValue);
            }

            if (m_timestampsToReturn is not TimestampsToReturn.Source and not TimestampsToReturn.Both)
            {
                item.Value = item.Value!.WithSourceTimestamp(DateTimeUtc.MinValue);
            }

            ServerUtils.ReportPublishValue(NodeId, Id, item.Value!);
            notifications.Enqueue(item);

            // update diagnostic info.
            DiagnosticInfo? diagnosticInfo = null;

            if ((DiagnosticsMasks & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_server, context, error!, m_logger);
            }

            diagnostics.Enqueue(diagnosticInfo!);

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
                    return m_subscription!;
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

                    long now = m_timeProvider.GetTimestampMilliseconds();

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
            lock (m_lock)
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
                    IsDeleted = m_isDeleted,
                    IsDetached = m_isDetached,
                    Encoding = DataEncoding,
                    FilterToUse = m_filterToUse!,
                    Id = Id,
                    IndexRange = m_indexRange!,
                    LastError = m_lastError!,
                    LastValue = m_lastValue,
                    MonitoringMode = MonitoringMode,
                    NodeId = NodeId,
                    OriginalFilter = Filter!,
                    Range = m_range,
                    TimestampsToReturn = m_timestampsToReturn,
                    TypeMask = MonitoredItemType,
                    ParsedIndexRange = m_parsedIndexRange,
                    FilteredRetainConditionIds = m_filteredRetainConditionIds?.Count > 0
                        ? [.. m_filteredRetainConditionIds]
                        : ArrayOf<string>.Null
                };
            }
        }

        private void QueueNodeIdUnknown()
        {
            if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
            {
                return;
            }

            DataValue value = CreateNodeIdUnknownValue();
            var error = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            // With queueing disabled the last value is what the Client is served, so there is
            // nothing to protect and the notification simply becomes that value.
            if (QueueSize > 1)
            {
                m_dataChangeQueueHandler?.QueueRequiredValue(value, error);
            }

            m_lastValue = value;
            m_lastError = error;
            m_readyToPublish = true;
            m_readyToTrigger = true;
        }

        private DataValue CreateNodeIdUnknownValue()
        {
            DateTime utcNow = m_timeProvider.GetUtcNow().UtcDateTime;
            return new DataValue(
                Variant.Null,
                StatusCodes.BadNodeIdUnknown,
                utcNow,
                utcNow);
        }

        private static bool IsBadNodeIdUnknown(in DataValue value, ServiceResult? error)
        {
            if (error?.StatusCode.Code == StatusCodes.BadNodeIdUnknown.Code)
            {
                return true;
            }

            return !value.IsNull && value.StatusCode.Code == StatusCodes.BadNodeIdUnknown.Code;
        }

        /// <summary>
        /// Applies the filter to value to determine if the new value should be kept.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="value"/> is the default/null DataValue.</exception>
        protected virtual bool ApplyFilter(in DataValue value, ServiceResult error)
        {
            if (value.IsNull)
            {
                throw new ArgumentException("value cannot be null/default", nameof(value));
            }

            return ValueChanged(
                value,
                error,
                m_lastValue,
                m_lastError!,
                m_cachedDataChangeFilter!,
                m_range);
        }

        /// <summary>
        /// Applies the filter to value to determine if the new value should be kept.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static bool ValueChanged(
            in DataValue value,
            ServiceResult error,
            in DataValue lastValue,
            ServiceResult lastError,
            DataChangeFilter filter,
            double range)
        {
            if (value.IsNull)
            {
                throw new ArgumentException("value cannot be null/default", nameof(value));
            }

            // select default data change filters.
            const double deadband = 0.0;
            DeadbandType deadbandType = DeadbandType.None;
            DataChangeTrigger trigger = DataChangeTrigger.StatusValue;

            // apply filter.
            if (filter != null)
            {
                trigger = filter.Trigger;
                deadbandType = (DeadbandType)(int)filter.DeadbandType;
                _ = filter.DeadbandValue;

                // when deadband is used and the trigger is StatusValueTimestamp, then it should behave as if trigger is StatusValue.
                if ((deadbandType != DeadbandType.None) &&
                    (trigger == DataChangeTrigger.StatusValueTimestamp))
                {
                    trigger = DataChangeTrigger.StatusValue;
                }
            }
            else
            {
                filter = new DataChangeFilter
                {
                    DeadbandType = (uint)deadbandType,
                    DeadbandValue = deadband
                };
            }

            // get the current status.
            StatusCode status = StatusCodes.Good;

            if (error != null)
            {
                status = error.StatusCode;
            }
            else if (!lastValue.IsNull)
            {
                status = value.StatusCode;
            }

            // get the last status.
            StatusCode lastStatus = StatusCodes.Good;

            if (lastError != null)
            {
                lastStatus = lastError.StatusCode;
            }
            else if (!lastValue.IsNull)
            {
                lastStatus = lastValue.StatusCode;
            }

            // value changed if any status change occurrs.
            if (!status.Equals(lastStatus, StatusCodeComparison.AllBits))
            {
                return true;
            }

            // value changed if only one is null.
            if (lastValue.IsNull)
            {
                return true;
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
            if (!filter.AreEqual(lastValue.WrappedValue, value.WrappedValue, range))
            {
                return true;
            }

            // must be equal.
            return false;
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
                            m_dataChangeQueueHandler?.Dispose();
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

                        if (queueLastValue && !m_lastValue.IsNull)
                        {
                            m_dataChangeQueueHandler.QueueValue(m_lastValue, m_lastError!);
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
                    m_eventQueueHandler?.Dispose();
                    m_eventQueueHandler = null;
                    m_dataChangeQueueHandler?.Dispose();
                    m_dataChangeQueueHandler = null;
                    break;
                default:
                    throw ServiceResultException.Unexpected(
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
                        IDataChangeMonitoredItemQueue? restoredQueue = m_restoredDataChangeQueue;
                        if (restoredQueue == null)
                        {
                            try
                            {
                                restoredQueue = m_subscriptionStore.RestoreDataChangeMonitoredItemQueue(
                                    Id);
                            }
                            catch (Exception ex)
                            {
                                m_logger.FailedToRestoreQueueForMonitoredItem(ex, Id, SubscriptionId);
                            }
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
                        IEventMonitoredItemQueue? restoredQueue = m_restoredEventQueue;
                        if (restoredQueue == null)
                        {
                            try
                            {
                                restoredQueue = m_subscriptionStore.RestoreEventMonitoredItemQueue(Id);
                            }
                            catch (Exception ex)
                            {
                                m_logger.FailedToRestoreQueueForMonitoredItem2(ex, Id, SubscriptionId);
                            }
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
                    throw ServiceResultException.Unexpected(
                        $"Unexpected MonitoringMode {MonitoringMode}");
            }
        }

        /// <summary>
        /// Update the overflow count.
        /// </summary>
        private void QueueOverflowHandler()
        {
            (m_subscription as ISubscriptionPublishPipeline)?.QueueOverflowHandler();
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
            if (!disposing)
            {
                return;
            }

            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }

                m_isDisposed = true;

                // A NodeManager reload may be handing this item to its replacement right now.
                // Tearing the queues down underneath it would leave the replacement sampling a
                // disposed item, so the teardown waits for the attach to report back.
                if (m_isAttaching)
                {
                    return;
                }
            }

            DisposeQueueHandlers();
        }

        /// <summary>
        /// Releases the queues once no attach is in flight.
        /// </summary>
        private void DisposeQueueHandlers()
        {
            DataChangeQueueHandler? dataChangeQueueHandler;
            EventQueueHandler? eventQueueHandler;
            lock (m_lock)
            {
                dataChangeQueueHandler = m_dataChangeQueueHandler;
                eventQueueHandler = m_eventQueueHandler;
            }

            dataChangeQueueHandler?.Dispose();
            eventQueueHandler?.Dispose();
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private bool m_isDisposed;
        private bool m_isAttaching;
        private readonly TimeProvider m_timeProvider;
        private IServerInternal m_server;
        private string? m_indexRange;
        private NumericRange m_parsedIndexRange;
        private TimestampsToReturn m_timestampsToReturn;
        private MonitoringFilter? m_filterToUse;
        private DataChangeFilter? m_cachedDataChangeFilter;
        private double m_range;
        private double m_samplingInterval;
        private bool m_discardOldest;
        private int m_sourceSamplingInterval;
        private DataValue m_lastValue;
        private ServiceResult? m_lastError;
        private long m_nextSamplingTime;
        private readonly IMonitoredItemQueueFactory m_monitoredItemQueueFactory;
        private DataChangeQueueHandler? m_dataChangeQueueHandler;
        private EventQueueHandler? m_eventQueueHandler;
        private readonly ISubscriptionStore m_subscriptionStore;
        private readonly IDataChangeMonitoredItemQueue? m_restoredDataChangeQueue;
        private readonly IEventMonitoredItemQueue? m_restoredEventQueue;
        private bool m_readyToPublish;
        private bool m_readyToTrigger;
        private bool m_semanticsChanged;
        private bool m_structureChanged;
        private ISubscription? m_subscription;
        private ServiceResult? m_samplingError;
        private IAggregateCalculator? m_calculator;
        private bool m_triggered;
        private bool m_resendData;
        private HashSet<string>? m_filteredRetainConditionIds;
        private bool m_isDetached;

        /// <summary>
        /// The handle a detached MonitoredItem is parked on. It is a shared sentinel, because a
        /// detached item has no real Node behind it until it is attached again.
        /// </summary>
        private static readonly object s_detachedHandle = new();
        private bool m_isDeleted;
    }

    /// <summary>
    /// Source-generated log messages for MonitoredItem.
    /// </summary>
    internal static partial class MonitoredItemLog
    {
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 0, Level = LogLevel.Error,
            Message = "Durable subscription was created but no MonitoredItemQueueFactory that supports durable " +
                "queues was registered, monitored item with id {Id} could not be created, " +
                "SubscriptionId={SubscriptionId}")]
        public static partial void DurableSubscriptionWasCreateButNoMonitoredItemQueueFactory(
            this ILogger logger,
            uint id,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 1, Level = LogLevel.Trace,
            Message = "SetTriggered[{Id}], SubscriptionId={SubscriptionId}")]
        public static partial void SetTriggeredId(this ILogger logger, uint id, uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 2, Level = LogLevel.Trace,
            Message = "MONITORING MODE[{MonitoredItemId}] {Previous} -> {New}, SubscriptionId={SubscriptionId}")]
        public static partial void MONITORINGMODEMonitoredItemIdPreviousNew(
            this ILogger logger,
            uint monitoredItemId,
            MonitoringMode previous,
            MonitoringMode @new,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 3, Level = LogLevel.Trace,
            Message = "RECEIVED VALUE[{MonitoredItemId}] Value={Value}, SubscriptionId={SubscriptionId}")]
        public static partial void RECEIVEDVALUEMonitoredItemIdValueValue(
            this ILogger logger,
            uint monitoredItemId,
            Variant value,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 4, Level = LogLevel.Trace,
            Message = "Value received out of order: {SourceTimestamp}, ServerHandle={MonitoredItemId}, " +
                "SubscriptionId={SubscriptionId}")]
        public static partial void ValueReceivedOutOfOrderSourceTimestampServerHandle(
            this ILogger logger,
            string? sourceTimestamp,
            uint monitoredItemId,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 5, Level = LogLevel.Trace,
            Message = "QUEUE VALUE[{MonitoredItemId}]: Value={Value} CODE={Code}<{Code:X8}> OVERFLOW={Overflow}, " +
                "SubscriptionId={SubscriptionId}")]
        public static partial void QUEUEVALUEMonitoredItemIdValueValueCODECode(
            this ILogger logger,
            uint monitoredItemId,
            Variant value,
            uint code,
            bool overflow,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 6, Level = LogLevel.Trace,
            Message = "MONITORED ITEM: Publish(QueueSize={QueueSize}), " +
                "SubscriptionId={SubscriptionId}, MonitoredItemId={MonitoredItemId}")]
        public static partial void MONITOREDITEMPublishQueueSizeQueueSize(
            this ILogger logger,
            int queueSize,
            uint subscriptionId,
            uint monitoredItemId);

        [LoggerMessage(
            EventId = ServerCompatibilityEventIds.MonitoredItemReady,
            EventName = "MonitoredItemReady",
            Level = LogLevel.Trace,
            Message = "IsReadyToPublish[{Id}] {State}")]
        public static partial void CompatibilityMonitoredItemReady(
            this ILogger logger,
            uint id,
            string state);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 7, Level = LogLevel.Error,
            Message = "Failed to restore queue for monitored item with id {MonitoredItemId}," +
                " SubscriptionId={SubscriptionId}")]
        public static partial void FailedToRestoreQueueForMonitoredItem(
            this ILogger logger,
            Exception ex,
            uint monitoredItemId,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 8, Level = LogLevel.Error,
            Message = "Failed to restore queue for monitored item with id {Id}," +
                " SubscriptionId={SubscriptionId}")]
        public static partial void FailedToRestoreQueueForMonitoredItem2(
            this ILogger logger,
            Exception ex,
            uint id,
            uint subscriptionId);
    }

}
