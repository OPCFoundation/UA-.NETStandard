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
    public partial class MonitoredItem :
        IEventMonitoredItem,
        ISampledDataChangeMonitoredItem,
        IInitialValueMonitoredItem,
        ITriggeredMonitoredItem,
        IDetachableMonitoredItem,
        IMonitoredItemGenerationBinding,
        IMonitoredItemTransferState,
        IReadySignalingMonitoredItem,
        IMonitoredItemValuePublication
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
        /// <param name="server">The server used to initialize the instance.</param>
        /// <param name="nodeManager">The node manager used to initialize the instance.</param>
        /// <param name="managerHandle">The manager handle used to initialize the instance.</param>
        /// <param name="subscriptionId">The subscription ID used to initialize the instance.</param>
        /// <param name="id">The ID used to initialize <c>MonitoredItem</c>.</param>
        /// <param name="itemToMonitor">The item to monitor used to initialize the instance.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks used to initialize the instance.</param>
        /// <param name="timestampsToReturn">The timestamps to return used to initialize the instance.</param>
        /// <param name="monitoringMode">The monitoring mode used to initialize the instance.</param>
        /// <param name="clientHandle">The client handle used to initialize the instance.</param>
        /// <param name="originalFilter">The original filter used to initialize the instance.</param>
        /// <param name="filterToUse">The filter to use used to initialize the instance.</param>
        /// <param name="range">The range used to initialize the instance.</param>
        /// <param name="samplingInterval">The sampling interval used to initialize the instance.</param>
        /// <param name="queueSize">The queue size used to initialize the instance.</param>
        /// <param name="discardOldest">The discard-oldest flag used to initialize <c>MonitoredItem</c>.</param>
        /// <param name="sourceSamplingInterval">The source sampling interval used to initialize the instance.</param>
        /// <param name="createDurable">The create-durable flag used to initialize <c>MonitoredItem</c>.</param>
        /// <param name="timeProvider">The time provider used to initialize the instance.</param>
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
            SourceMetadata = CaptureSourceMetadata(server, managerHandle);
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

            if (filterToUse is ServerAggregateFilter aggregateFilter)
            {
                m_filterToUse = new AggregationFilterHandler(
                    m_server.AggregateManager,
                    aggregateFilter,
                    QueueProcessedValue,
                    primeInitialValue: true);
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
                FilterToUse!,
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
            : this(
                server, storedMonitoredItem, timeProvider,
                nodeManager ?? throw new ArgumentNullException(nameof(nodeManager)), managerHandle)
        {
        }

        /// <summary>
        /// Restores an unresolved item without creating a synthetic manager binding.
        /// </summary>
        /// <param name="server">The server owning the persisted queues.</param>
        /// <param name="storedMonitoredItem">The persisted item state.</param>
        /// <param name="timeProvider">The optional time provider.</param>
        internal MonitoredItem(
            IServerInternal server,
            IStoredMonitoredItem storedMonitoredItem,
            TimeProvider? timeProvider = null)
            : this(server, storedMonitoredItem, timeProvider, null, null)
        {
        }

        /// <summary>
        /// Restores persisted queues with an optional real source binding.
        /// </summary>
        private MonitoredItem(
            IServerInternal server,
            IStoredMonitoredItem storedMonitoredItem,
            TimeProvider? timeProvider,
            IAsyncNodeManager? nodeManager,
            object? managerHandle)
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
            if (nodeManager is not null)
            {
                NodeManager = nodeManager;
            }
            else
            {
                m_slot = new NodeManagerSlot();
            }
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

            if (storedMonitoredItem.FilterToUse is ServerAggregateFilter aggregateFilter)
            {
                m_filterToUse = new AggregationFilterHandler(
                    m_server.AggregateManager,
                    aggregateFilter,
                    QueueProcessedValue,
                    primeInitialValue: false);
            }

            // report change to item state.
            ServerUtils.ReportCreateMonitoredItem(
                NodeId,
                Id,
                m_samplingInterval,
                QueueSize,
                m_discardOldest,
                FilterToUse!,
                MonitoringMode);

            RestoreQueue();

            // notifications that were queued before the restart are still owed to the
            // client, so the item is ready without waiting for the next change. Only a
            // reporting item delivers them and triggers its linked items with them: the
            // queue of a sampling item keeps its samples until it is triggered itself, so
            // it holds nothing new since the restart.
            if (ItemsInQueue > 0)
            {
                m_readyToPublish = true;
                m_readyToTrigger = MonitoringMode == MonitoringMode.Reporting;
            }

            lock (m_lock)
            {
                m_isTerminal = (storedMonitoredItem as IStoredMonitoredItemInvalidation)?.IsTerminal ?? false;
                SetDetachState(storedMonitoredItem.IsDetached, storedMonitoredItem.IsDeleted);
            }
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_server = null!;
            m_nodeManager = null;
            Generation = null;
            ManagerHandle = null;
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
        /// The currently bound node manager, or null while the item is unresolved.
        /// </summary>
        public IAsyncNodeManager? NodeManager
        {
            get => m_nodeManager;
            private set
            {
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                    value ?? throw new ArgumentNullException(nameof(value)));
                if (Generation?.TryGetGeneration(out NodeManagerGeneration? previous) == true)
                {
                    previous!.MonitoredItems.TryRemove(this, out _);
                }
                if (m_slot is { } previousSlot && !ReferenceEquals(previousSlot, generation.Slot))
                {
                    previousSlot.UnresolvedItems.TryRemove(this, out _);
                }
                m_slot = generation.Slot;
                m_nodeManager = value;
                Generation = generation.Reference;
                if (m_subscriptionOwned)
                {
                    generation.MonitoredItems[this] = m_subscription!;
                }
            }
        }

        /// <summary>
        /// Gets the non-owning identity of the generation currently bound to this monitored item.
        /// </summary>
        internal NodeManagerGenerationReference? Generation { get; private set; }

        /// <summary>
        /// Gets the metadata snapshot of the source node captured when the item was created or last rebound, which
        /// lets a detached item be modified after its source node left the address space.
        /// </summary>
        internal NodeState? SourceMetadata { get; private set; }

        /// <summary>
        /// Sets the lost-node flags and updates slot ownership while the item lock is held.
        /// </summary>
        private void SetDetachState(bool isDetached, bool isDeleted)
        {
            m_isDetached = isDetached;
            m_isDeleted = isDeleted;

            UpdateUnresolvedOwnership();
        }

        /// <summary>
        /// Updates the stable slot's unresolved set while the item lock is held.
        /// </summary>
        private void UpdateUnresolvedOwnership()
        {
            if (m_subscriptionOwned && !m_isDisposed && !m_isTerminal && (m_isDetached || m_isDeleted))
            {
                m_slot.UnresolvedItems[this] = m_subscription!;
            }
            else
            {
                m_slot.UnresolvedItems.TryRemove(this, out _);
            }
        }

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
        /// The handle assigned by the bound node manager, or null while the item is unresolved.
        /// </summary>
        public object? ManagerHandle { get; private set; }

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
        public int MonitoredItemType { get; private init; }

        /// <summary>
        /// Returns true if the item is ready to publish.
        /// </summary>
        public bool IsReadyToPublish
        {
            get
            {
                if (MonitoringMode == MonitoringMode.Disabled ||
                    (MonitoringMode != MonitoringMode.Reporting && !m_triggered))
                {
                    return false;
                }

                // check if aggregate interval has passed.
                if (AggregateFilter?.HasEndTimePassed(DateTime.UtcNow) == true)
                {
                    return true;
                }

                // check if not ready to publish in case it doesn't ResendData
                if (!m_readyToPublish)
                {
                    return false;
                }

                // check if it has been triggered.
                if (m_triggered)
                {
                    return true;
                }

                // events are exception based and are not sampled (Part 4, 5.13.1.2), so a
                // requested sampling interval must not hold back their delivery.
                if (m_sourceSamplingInterval == 0 &&
                    (MonitoredItemType & MonitoredItemTypeMask.Events) == 0)
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
                    SignalReady();
                }
            }
        }

        void IMonitoredItemTransferState.RestoreResendDataTrigger(bool resendData)
        {
            lock (m_lock)
            {
                m_resendData = resendData;
                if (resendData)
                {
                    SignalReady();
                }
            }
        }

        /// <inheritdoc/>
        bool IReadySignalingMonitoredItem.HasPendingNotifications =>
            Volatile.Read(ref m_readyToPublish) ||
            Volatile.Read(ref m_readyToTrigger) ||
            Volatile.Read(ref m_resendData) ||
            // an aggregate becomes ready when its processing interval ends, without a new value.
            AggregateFilter != null;

        /// <inheritdoc/>
        bool IReadySignalingMonitoredItem.TryAcquireReadySignal()
        {
            return Interlocked.CompareExchange(ref m_readySignal, 1, 0) == 0;
        }

        /// <inheritdoc/>
        void IReadySignalingMonitoredItem.ReleaseReadySignal()
        {
            Interlocked.Exchange(ref m_readySignal, 0);
        }

        /// <summary>
        /// Tells the owning subscription that the item has something to report, unless it
        /// was told already and has not looked at the item since. Called after the ready
        /// flags were set, while holding <c>m_lock</c>.
        /// </summary>
        private void SignalReady()
        {
            // the compare-exchange is a full fence: the flags set before it are visible to a
            // subscription that releases the signal and then checks the flags again.
            if (m_subscription is IMonitoredItemReadySink sink &&
                Interlocked.CompareExchange(ref m_readySignal, 1, 0) == 0)
            {
                sink.OnMonitoredItemReady(this);
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
        bool IMonitoredItemGenerationBinding.IsDisposed
        {
            get
            {
                lock (m_lock)
                {
                    return m_isDisposed;
                }
            }
        }

        /// <inheritdoc/>
        void IMonitoredItemGenerationBinding.Invalidate(bool terminal)
        {
            Invalidate(terminal);
        }

        /// <inheritdoc/>
        bool IMonitoredItemGenerationBinding.IsTerminal
        {
            get
            {
                lock (m_lock)
                {
                    return m_isTerminal;
                }
            }
        }

        /// <summary>
        /// Records the lost-node state without repeating its initial status notification.
        /// </summary>
        /// <param name="terminal">Whether future adoption is forbidden.</param>
        private void Invalidate(bool terminal)
        {
            lock (m_lock)
            {
                m_isTerminal |= terminal;
                if (m_isDeleted)
                {
                    SetDetachState(m_isDetached || terminal, isDeleted: true);
                    return;
                }
                m_valuePublication?.Fail(StatusCodes.BadNodeIdUnknown);
                SetDetachState(m_isDetached || terminal, isDeleted: true);
                QueueNodeIdUnknown();
            }
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.BeginDetach()
        {
            lock (m_lock)
            {
                m_valuePublication?.Fail(StatusCodes.BadInvalidState);
                SetDetachState(isDetached: true, m_isDeleted);
            }
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.Detach(IServerInternal server)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            lock (m_lock)
            {
                if (Generation?.TryGetGeneration(out NodeManagerGeneration? previous) == true)
                {
                    previous!.MonitoredItems.TryRemove(this, out _);
                }
                m_nodeManager = null;
                Generation = null;
                ManagerHandle = null;
                SetDetachState(isDetached: true, m_isDeleted);
            }
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.QueueNodeIdUnknown()
        {
            lock (m_lock)
            {
                SetDetachState(m_isDetached, isDeleted: true);

                QueueNodeIdUnknown();
            }
        }

        /// <inheritdoc/>
        void IDetachableMonitoredItem.Rebind(IAsyncNodeManager nodeManager, object managerHandle)
        {
            NodeState? metadata = CaptureSourceMetadata(m_server, managerHandle);
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    throw new ServiceResultException(StatusCodes.BadMonitoredItemIdInvalid);
                }
                if (m_isTerminal)
                {
                    throw new InvalidOperationException("A terminal monitored item cannot be rebound.");
                }
                NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
                ManagerHandle = managerHandle;
                SourceMetadata = metadata;
                SetDetachState(isDetached: false, isDeleted: false);
                m_finalValuePublished = false;
            }
        }

        /// <summary>
        /// Gets the original client filter before server-side revision.
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
        /// Gets or initializes whether values are supplied by an external source instead of stack sampling.
        /// </summary>
        internal bool UsesExternalValueSource
        {
            get => (MonitoredItemType & MonitoredItemTypeMask.ExternalValueSource) != 0;
            init => MonitoredItemType = value
                ? MonitoredItemType | MonitoredItemTypeMask.ExternalValueSource
                : MonitoredItemType & ~MonitoredItemTypeMask.ExternalValueSource;
        }

        /// <summary>
        /// Returns a description of the item being monitored.
        /// </summary>
        public ReadValueId GetReadValueId()
        {
            lock (m_lock)
            {
                var readValueId = new ReadValueId
                {
                    NodeId = NodeId,
                    AttributeId = AttributeId,
                    IndexRange = m_indexRange,
                    ParsedIndexRange = m_parsedIndexRange,
                    DataEncoding = DataEncoding
                };
                if (ManagerHandle is { } handle)
                {
                    readValueId.Handle = handle;
                }
                return readValueId;
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
                m_samplingError = error ?? ServiceResult.Good;
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
                MonitoringFilter? previousFilterToUse = FilterToUse;
                uint previousClientHandle = ClientHandle;
                AggregationFilterHandler? aggregateFilter = AggregateFilter;
                if (aggregateFilter == null && filterToUse is ServerAggregateFilter)
                {
                    aggregateFilter = new AggregationFilterHandler(
                        m_server.AggregateManager,
                        QueueProcessedValue,
                        previousFilterToUse);
                }
                AggregationFilterHandler.Modification? aggregateChange = aggregateFilter?.PrepareChange(filterToUse);

                // the queued events are brought in line with the new select clauses and client
                // handle when they are published; registering that cannot fail.
                RebuildQueuedEventFields(
                    previousFilterToUse,
                    filterToUse,
                    previousClientHandle,
                    clientHandle);

                DiagnosticsMasks = diagnosticsMasks;
                m_timestampsToReturn = timestampsToReturn;
                ClientHandle = clientHandle;
                m_discardOldest = discardOldest;

                Filter = originalFilter;

                DiscardFilteredRetainStateOnWhereClauseChange(previousFilterToUse, filterToUse);

                if (range != null)
                {
                    m_range = range.High - range.Low;
                }

                SetSamplingInterval(samplingInterval);
                QueueSize = queueSize;

                aggregateFilter?.CommitChange(filterToUse, aggregateChange);
                m_filterToUse = (IMonitoringFilter?)aggregateFilter ?? filterToUse;
                if (aggregateFilter != null)
                {
                    m_valuePublication?.Fail(StatusCodes.BadNotSupported);
                }

                // report change to item state.
                ServerUtils.ReportModifyMonitoredItem(
                    NodeId,
                    Id,
                    m_samplingInterval,
                    QueueSize,
                    m_discardOldest,
                    FilterToUse!,
                    MonitoringMode);

                InitializeQueue();

                return null;
            }
        }

        /// <summary>
        /// Revises unresolved attributes against server policy without racing source adoption or disposal.
        /// </summary>
        internal (ServiceResult? Error, MonitoringFilterResult? FilterResult) ModifyUnresolvedAttributes(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequest request,
            ServerConfiguration configuration)
        {
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return (StatusCodes.BadMonitoredItemIdInvalid, null);
                }
                if (!m_isDetached)
                {
                    return (StatusCodes.BadInvalidState, null);
                }
                MonitoringParameters parameters = request.RequestedParameters;
                NodeState? metadata = SourceMetadata;
                if (metadata is null && !parameters.Filter.IsNull)
                {
                    return (StatusCodes.BadMonitoredItemFilterUnsupported, null);
                }
                double defaultInterval = SubscriptionCallback is ISubscription subscription
                    ? subscription.PublishingInterval : SamplingInterval;
                double samplingInterval = SubscriptionManager.CalculateRevisedSamplingInterval(
                    parameters.SamplingInterval, defaultInterval, metadata, AttributeId,
                    configuration.MinSupportedSamplingInterval);
                uint queueSize = SubscriptionManager.CalculateRevisedQueueSize(
                    IsDurable, parameters.QueueSize, (uint)configuration.MaxNotificationQueueSize,
                    (uint)configuration.MaxDurableNotificationQueueSize);
                AsyncCustomNodeManager.ValidateMonitoringFilterResult validated =
                    MonitoredItemFilterValidation.Validate(
                        m_server, new ServerSystemContext(m_server, context), metadata, AttributeId, parameters.Filter);
                if (StatusCode.IsBad(validated.StatusCode))
                {
                    return (validated.StatusCode, validated.FilterResult);
                }
                if (validated.FilterToUse is ServerAggregateFilter aggregate)
                {
                    MonitoredItemFilterValidation.ReviseDefaultAggregate(
                        m_server, samplingInterval, queueSize, aggregate);
                    validated.FilterResult = MonitoredItemFilterValidation.CreateAggregateResult(aggregate);
                }
                ServiceResult? error = ModifyAttributes(
                    context.DiagnosticsMask, timestampsToReturn, parameters.ClientHandle,
                    validated.FilterToUse, validated.FilterToUse, validated.Range, samplingInterval,
                    queueSize, parameters.DiscardOldest);
                return (error, validated.FilterResult);
            }
        }

        /// <summary>
        /// Brings events that were queued before a modification in line with the new
        /// select clauses and client handle.
        /// </summary>
        /// <remarks>
        /// Event fields are resolved when the event is queued, but the client decodes every
        /// EventFieldList it receives after the modification with the new select clauses
        /// (Part 4, 7.25.3). Queued events are therefore resolved again from their filter
        /// target when they are published. An event without one (restored from a durable or
        /// redundant queue) keeps
        /// the values of the clauses selected before and reports the new ones as null; only
        /// an event none of whose fields is still selected is dropped, which is reported to
        /// the client as an event queue overflow.
        /// </remarks>
        private void RebuildQueuedEventFields(
            MonitoringFilter? previousFilter,
            MonitoringFilter? newFilter,
            uint previousClientHandle,
            uint clientHandle)
        {
            if (m_eventQueueHandler == null ||
                m_eventQueueHandler.ItemsInQueue == 0 ||
                newFilter is not EventFilter filter)
            {
                return;
            }

            bool selectClausesChanged = !AreSelectClausesEqual(
                (previousFilter as EventFilter)?.SelectClauses ?? default,
                filter.SelectClauses);

            if (!selectClausesChanged)
            {
                if (previousClientHandle != clientHandle)
                {
                    m_eventQueueHandler.TransformQueuedEvents(fields =>
                    {
                        fields.ClientHandle = clientHandle;
                        return fields;
                    });
                }

                return;
            }

            var context = new FilterContext(
                m_server.NamespaceUris,
                m_server.TypeTree,
                Session?.PreferredLocales!,
                m_server.Telemetry);

            // where each new select clause was in the previous layout, for the events
            // without a filter target, e.g. restored from a durable queue.
            int[]? previousIndexes = null;

            m_eventQueueHandler.TransformQueuedEvents(fields =>
            {
                if (fields.Handle is not IFilterTarget target)
                {
                    previousIndexes ??= MapSelectClauses(
                        (previousFilter as EventFilter)?.SelectClauses ?? default,
                        filter.SelectClauses);
                    return ProjectEventFields(fields, previousIndexes, clientHandle);
                }

                // a trailing filtered retain event keeps its FilteredRetainTarget handle,
                // so the rebuilt fields report the same Retain override.
                EventFieldList rebuilt = GetEventFields(context, filter, target);
                rebuilt.ClientHandle = clientHandle;
                return rebuilt;
            });
        }

        /// <summary>
        /// Returns for every current select clause the index of the identical previous
        /// clause, or -1 when the clause is new.
        /// </summary>
        private static int[] MapSelectClauses(
            ArrayOf<SimpleAttributeOperand> previous,
            ArrayOf<SimpleAttributeOperand> current)
        {
            int[] previousIndexes = new int[current.Count];
            for (int ii = 0; ii < current.Count; ii++)
            {
                previousIndexes[ii] = -1;
                if (current[ii] == null)
                {
                    continue;
                }

                for (int jj = 0; jj < previous.Count; jj++)
                {
                    if (Utils.IsEqual(previous[jj], current[ii]))
                    {
                        previousIndexes[ii] = jj;
                        break;
                    }
                }
            }

            return previousIndexes;
        }

        /// <summary>
        /// Moves the fields of a queued event that cannot be resolved again onto the new
        /// select clauses: a field selected before keeps its value, a new one is null. An
        /// event none of whose fields is selected any more is dropped.
        /// </summary>
        private static EventFieldList? ProjectEventFields(
            EventFieldList fields,
            int[] previousIndexes,
            uint clientHandle)
        {
            var eventFields = new List<Variant>(previousIndexes.Length);
            bool anyMapped = false;
            foreach (int previousIndex in previousIndexes)
            {
                if (previousIndex >= 0 && previousIndex < fields.EventFields.Count)
                {
                    eventFields.Add(fields.EventFields[previousIndex]);
                    anyMapped = true;
                }
                else
                {
                    eventFields.Add(Variant.Null);
                }
            }

            if (!anyMapped)
            {
                return null;
            }

            return new EventFieldList
            {
                ClientHandle = clientHandle,
                EventFields = eventFields
            };
        }

        private static bool AreSelectClausesEqual(
            ArrayOf<SimpleAttributeOperand> previous,
            ArrayOf<SimpleAttributeOperand> current)
        {
            if (previous.Count != current.Count)
            {
                return false;
            }

            for (int ii = 0; ii < previous.Count; ii++)
            {
                if (!Utils.IsEqual(previous[ii], current[ii]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Prepares an aggregate calculator before the node manager commits a monitored-item modification.
        /// </summary>
        internal AggregationFilterHandler.Modification? PrepareAggregateModification(ServerAggregateFilter filter)
        {
            lock (m_lock)
            {
                AggregationFilterHandler aggregateFilter = AggregateFilter ??
                    new AggregationFilterHandler(m_server.AggregateManager, QueueProcessedValue, FilterToUse);
                AggregationFilterHandler.Modification? preparation = aggregateFilter.PrepareModification(filter);
                m_filterToUse = aggregateFilter;
                return preparation;
            }
        }

        /// <summary>
        /// Cancels an uncommitted aggregate preparation without disturbing a newer modification.
        /// </summary>
        internal void CancelPreparedAggregateModification(AggregationFilterHandler.Modification preparation)
        {
            lock (m_lock)
            {
                AggregateFilter?.CancelModification(preparation);
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
                    m_readyToPublish = false;
                }

                MonitoringMode = monitoringMode;

                // a pending ResendData/transfer resend only applies to a reporting item;
                // a sampling or disabled item must not report (Part 4, 5.13.1.3).
                if (monitoringMode != MonitoringMode.Reporting)
                {
                    m_valuePublication?.Complete(published: false);
                    m_resendData = false;
                }

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
                    FilterToUse!,
                    MonitoringMode);

                InitializeQueue();

                // the node was deleted while the item was disabled, or before it was
                // disabled: the item reports Bad_NodeIdUnknown again once it samples.
                if (previousMode == MonitoringMode.Disabled && m_isDeleted)
                {
                    QueueNodeIdUnknown();
                }

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
            QueueValueCore(in value, error, ignoreFilters, initialValue: false);
        }

        /// <summary>
        /// Removes notifications that were authorized under a previous session identity.
        /// </summary>
        internal void ClearQueuedNotifications()
        {
            lock (m_lock)
            {
                m_dataChangeQueueHandler?.Clear();
                m_eventQueueHandler?.Clear();
                AggregateFilter?.ClearNotifications();
                m_valuePublication?.Complete(published: false);
                m_valuePublication = null;
                m_finalValuePending = false;
                m_filteredRetainConditionIds?.Clear();
                m_lastValue = default;
                m_lastError = null;
                m_readyToPublish = false;
                m_readyToTrigger = false;
                m_resendData = false;
                m_triggered = false;
                Interlocked.Exchange(ref m_readySignal, 0);
            }
        }

        /// <summary>
        /// Discards only notifications predating the current identity, preserving newly admitted events.
        /// </summary>
        internal void EnsureNotificationAuthorization()
        {
            lock (m_lock)
            {
                ISession? session = Session;
                if (session == null)
                {
                    return;
                }
                NotificationAuthorization? authorization = NotificationAuthorization.Capture(session);
                bool sameSession = ReferenceEquals(m_notificationSession, session);
                bool sameAuthorization = ReferenceEquals(m_notificationAuthorization, authorization) ||
                    (m_notificationAuthorization != null &&
                        authorization != null &&
                        m_notificationAuthorization.IsEquivalentTo(authorization, sameSession));
                if (m_notificationSession != null && !sameAuthorization)
                {
                    ClearQueuedNotifications();
                }
                m_notificationSession = session;
                m_notificationAuthorization = authorization;
            }
        }

        /// <summary>
        /// Admits a value only if the identity used to authorize its read still owns this item.
        /// </summary>
        internal void QueueValueIfCurrent(
            OperationContext context,
            in DataValue value,
            ServiceResult? error,
            bool ignoreFilters = false,
            bool initialValue = false,
            MasterNodeManager.SourceEmissionAdmission? sourceEmission = null)
        {
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }
                ISession? session = Session ?? context.Session;
                if (!ReferenceEquals(context.Session, session) ||
                    !NotificationAuthorization.IsCurrent(session, context.NotificationAuthorization))
                {
                    return;
                }
                if (m_subscription == null)
                {
                    if (m_notificationSession != null &&
                        !NotificationAuthorization.IsCurrent(m_notificationSession, m_notificationAuthorization))
                    {
                        ClearQueuedNotifications();
                    }
                    m_notificationSession = session;
                    m_notificationAuthorization = context.NotificationAuthorization;
                }
                if (initialValue)
                {
                    QueueValueCore(
                        value, error, ignoreFilters, initialValue: true, sourceEmission);
                }
                else
                {
                    QueueValueCore(
                        value, error, ignoreFilters, initialValue: false, sourceEmission);
                }
                if (!NotificationAuthorization.IsCurrent(session, context.NotificationAuthorization))
                {
                    ClearQueuedNotifications();
                }
            }
        }

        /// <summary>
        /// Admits an event only in the epoch that authorized its delivery.
        /// </summary>
        internal void QueueEventIfCurrent(
            ISession? session,
            NotificationAuthorization? authorization,
            IFilterTarget target,
            SharedEventFilterKey? key,
            SharedEventEvaluation? evaluation,
            NodeManagerGeneration? admittedGeneration = null,
            bool? bypassFilter = null)
        {
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }
                if (!ReferenceEquals(session, Session) ||
                    !NotificationAuthorization.IsCurrent(session, authorization))
                {
                    return;
                }
                if (evaluation != null)
                {
                    QueueSharedEvent(target, key!, evaluation, admittedGeneration);
                }
                else
                {
                    QueueAdmittedEvent(target, admittedGeneration, bypassFilter);
                }
                if (!NotificationAuthorization.IsCurrent(session, authorization))
                {
                    ClearQueuedNotifications();
                }
            }
        }

        void IInitialValueMonitoredItem.QueueInitialValue(
            in DataValue value,
            ServiceResult? error,
            bool ignoreFilters)
        {
            QueueValueCore(in value, error, ignoreFilters, initialValue: true);
        }

        ServiceResult IInitialValueMonitoredItem.CompleteInitialValue()
        {
            lock (m_lock)
            {
                return AggregateFilter?.CompleteInitialValue(QueueBufferedValue) ?? ServiceResult.Good;
            }
        }

        private void QueueValueCore(
            in DataValue value,
            ServiceResult? error,
            bool ignoreFilters,
            bool initialValue,
            MasterNodeManager.SourceEmissionAdmission? sourceEmission = null)
        {
            bool capturedSourceEmission = sourceEmission.HasValue;
            MasterNodeManager.SourceEmissionAdmission? emission = null;
            bool admitted = capturedSourceEmission;
            if (!admitted)
            {
                admitted = MasterNodeManager.TryCaptureItemEmission(
                    m_server,
                    NodeManager,
                    Generation,
                    out emission);
            }
            using var emissionLease = emission;
            if (!admitted)
            {
                return;
            }
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }
                if ((sourceEmission ?? emission) is { Generation: var generation } &&
                    !IsBoundToGeneration(generation) &&
                    !(capturedSourceEmission && m_isDetached && ReferenceEquals(m_slot, generation.Slot)))
                {
                    return;
                }
                EnsureNotificationAuthorization();

                // this method should only be called for variables.
                if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                // check monitoring mode.
                if (MonitoringMode == MonitoringMode.Disabled ||
                    (m_finalValuePublished && (m_isDisposed || m_isDeleted || m_isDetached)) ||
                    (!capturedSourceEmission &&
                        !CanQueueSourceEmission() &&
                        !IsBadNodeIdUnknown(value, error)))
                {
                    return;
                }

                // A terminal observation is pinned until its real Publish response is built.
                // Older asynchronous reads and report-by-exception work cannot replace it.
                if (m_valuePublication != null)
                {
                    return;
                }

                if (!initialValue &&
                    AggregateFilter?.TryBufferLiveValue(value, error, ignoreFilters) == true)
                {
                    return;
                }

                QueueValueLocked(
                    value,
                    error,
                    ignoreFilters,
                    initialValue: initialValue);
                if (m_isDeleted &&
                    m_server.NodeManager is MasterNodeManager master &&
                    !master.IsSourceEmissionAllowed(NodeManager))
                {
                    QueueNodeIdUnknown();
                }
            }
        }

        private void QueueBufferedValue(in DataValue value, ServiceResult? error, bool ignoreFilters)
        {
            QueueValueLocked(value, error, ignoreFilters, initialValue: false);
        }

        private void QueueProcessedValue(in DataValue value)
        {
            AddValueToQueue(value, null!);
        }

        private void QueueValueLocked(
            in DataValue value,
            ServiceResult? error,
            bool ignoreFilters,
            bool initialValue)
        {
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

            if (initialValue && ServiceResult.IsBad(error))
            {
                ServerUtils.ReportQueuedValue(NodeId, Id, current);
                AddRequiredValueToQueue(
                    current,
                    error!,
                    replaceExisting: false);
                return;
            }

            // apply aggregate filter.
            if (!ServiceResult.IsBad(error) &&
                AggregateFilter is { } aggregateFilter &&
                aggregateFilter.TryQueueValue(current, initialValue, out bool accepted))
            {
                if (!accepted &&
                    m_logger.IsEnabled(LogLevel.Trace))
                {
                    m_logger.ValueReceivedOutOfOrderSourceTimestampServerHandle(
                        current.SourceTimestamp
                            .ToLocalTime()
                            .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        Id,
                        SubscriptionId);
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

        private void AddRequiredValueToQueue(
            DataValue value,
            ServiceResult error,
            bool replaceExisting)
        {
            if (QueueSize > 1)
            {
                m_dataChangeQueueHandler?.QueueRequiredValue(
                    value,
                    error,
                    replaceExisting);
            }
            m_lastValue = value;
            m_lastError = error;
            m_readyToPublish = true;
            m_readyToTrigger = true;
            SignalReady();
        }

        /// <summary>
        /// Adds a value to the queue.
        /// </summary>
        private void AddValueToQueue(in DataValue value, ServiceResult error)
        {
            bool overflow = false;
            if (QueueSize > 1)
            {
                overflow = m_dataChangeQueueHandler!.QueueValue(value, error);
            }

            // every notification triggers the linked items, including the first one after
            // leaving DISABLED. A notification queued before the first triggering link was
            // created must not trigger (Part 4, 5.13.1.6); SetTriggering discards the flag
            // when it creates that link.
            m_readyToTrigger = true;

            // save last value received.
            m_lastValue = value;
            m_lastError = error;
            m_readyToPublish = true;
            SignalReady();

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
        /// Returns which select clauses of <paramref name="filter"/> are rejected. They are
        /// determined once per filter, because a rejected clause stays unvalidated and
        /// validating it again for every event would repeat the failing (and possibly
        /// throwing) validation for every event the item receives. Clauses restored without
        /// validation are validated here.
        /// </summary>
        private bool[] GetRejectedSelectClauses(IFilterContext context, EventFilter filter)
        {
            RejectedSelectClauses? cached = m_rejectedSelectClauses;
            if (cached != null && ReferenceEquals(cached.Filter, filter))
            {
                return cached.Rejected;
            }

            ArrayOf<SimpleAttributeOperand> clauses = filter.SelectClauses;
            bool[] rejected = new bool[clauses.Count];
            for (int ii = 0; ii < rejected.Length; ii++)
            {
                SimpleAttributeOperand clause = clauses[ii];
                rejected[ii] = clause == null ||
                    (!clause.Validated && ServiceResult.IsBad(clause.Validate(context, 0)));
            }

            m_rejectedSelectClauses = new RejectedSelectClauses(filter, rejected);
            return rejected;
        }

        /// <summary>
        /// The rejected select clauses of one event filter.
        /// </summary>
        private sealed class RejectedSelectClauses
        {
            public RejectedSelectClauses(EventFilter filter, bool[] rejected)
            {
                Filter = filter;
                Rejected = rejected;
            }

            public EventFilter Filter { get; }

            public bool[] Rejected { get; }
        }

        /// <summary>
        /// Fetches the event fields from the event.
        /// </summary>
        private EventFieldList GetEventFields(
            FilterContext context,
            EventFilter filter,
            IFilterTarget instance)
        {
            return CreateEventFieldList(instance, EvaluateEventFields(context, filter, instance));
        }

        /// <summary>
        /// Creates the event field list of this item for event field values.
        /// </summary>
        private EventFieldList CreateEventFieldList(IFilterTarget instance, Variant[] eventFieldValues)
        {
            var result = (EventFieldList)EventFieldListActivator.Instance.CreateInstance();
            result.ClientHandle = ClientHandle;
            result.Handle = instance;
            result.EventFields = eventFieldValues;
            m_server.EventManager?.RegisterEventFields(result, instance);
            return result;
        }

        /// <summary>
        /// Reads the values of the select clauses from the event.
        /// </summary>
        private Variant[] EvaluateEventFields(
            FilterContext context,
            EventFilter filter,
            IFilterTarget instance)
        {
            // fetch the event fields.
            bool[] rejected = GetRejectedSelectClauses(context, filter);
            var eventFieldValues = new Variant[rejected.Length];
            for (int ii = 0; ii < rejected.Length; ii++)
            {
                // a select clause rejected in the EventFilterResult returns a null field
                // (Part 4 §7.22.3).
                if (rejected[ii])
                {
                    continue;
                }

                SimpleAttributeOperand clause = filter.SelectClauses[ii];

                // get the value of the attribute (apply localization).
                Variant value = instance.GetAttributeValue(
                    context,
                    clause.TypeDefinitionId,
                    clause.BrowsePath,
                    clause.AttributeId,
                    clause.ParsedIndexRange);

                // a missing value stays a null field.
                if (!value.IsNull)
                {
                    // translate any localized text (scalar or array).
                    if (value.TypeInfo.BuiltInType == BuiltInType.LocalizedText)
                    {
                        value = m_server.ResourceManager.TranslateValue(context.PreferredLocales, value);
                    }

                    eventFieldValues[ii] = value;
                }
            }
            return eventFieldValues;
        }

        /// <summary>
        /// Returns the context to evaluate the event filter with. The context only carries
        /// the server tables and the preferred locales of the session, so it is created again
        /// only when the session replaces its locales (or the item moves to another session).
        /// </summary>
        private FilterContext GetEventFilterContext(out string[]? preferredLocales)
        {
            preferredLocales = Session?.PreferredLocales;
            EventFilterContext? cached = m_eventFilterContext;
            if (cached == null || !ReferenceEquals(cached.PreferredLocales, preferredLocales))
            {
                cached = new EventFilterContext(
                    preferredLocales,
                    new FilterContext(
                        m_server.NamespaceUris,
                        m_server.TypeTree,
                        preferredLocales!,
                        m_server.Telemetry));
                m_eventFilterContext = cached;
            }
            return cached.Context;
        }

        /// <summary>
        /// The event filter context and the locales it was created for.
        /// </summary>
        private sealed class EventFilterContext
        {
            public EventFilterContext(string[]? preferredLocales, FilterContext context)
            {
                PreferredLocales = preferredLocales;
                Context = context;
            }

            public string[]? PreferredLocales { get; }

            public FilterContext Context { get; }
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
        /// <param name="instance">The filter target.</param>
        /// <param name="bypassFilter">The bypass-filter flag supplied by the caller.</param>
        public virtual void QueueEvent(IFilterTarget instance, bool bypassFilter)
        {
            QueueEventCore(instance, bypassFilter, TakeAdmittedSourceGeneration());
        }

        /// <summary>
        /// Queues an event through the overridable entry point while carrying the source admission the caller
        /// already holds, so derived item types still observe every event.
        /// </summary>
        private void QueueAdmittedEvent(
            IFilterTarget instance,
            NodeManagerGeneration? admittedGeneration,
            bool? bypassFilter = null)
        {
            NodeManagerGeneration? previous = m_admittedSourceGeneration;
            m_admittedSourceGeneration = admittedGeneration;
            try
            {
                if (bypassFilter is { } bypass)
                {
                    QueueEvent(instance, bypass);
                }
                else
                {
                    QueueEvent(instance);
                }
            }
            finally
            {
                m_admittedSourceGeneration = previous;
            }
        }

        /// <summary>
        /// Takes this item's locked delivery admission without lending it to another item or thread.
        /// </summary>
        private NodeManagerGeneration? TakeAdmittedSourceGeneration()
        {
            if (!m_lock.IsHeldByCurrentThread)
            {
                return null;
            }
            NodeManagerGeneration? admitted = m_admittedSourceGeneration;
            m_admittedSourceGeneration = null;
            return admitted;
        }

        private void QueueEventCore(
            IFilterTarget instance,
            bool bypassFilter,
            NodeManagerGeneration? admittedGeneration)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }
            bool capturedSourceEmission = admittedGeneration is not null;
            MasterNodeManager.SourceEmissionAdmission? emission = null;
            bool admitted = capturedSourceEmission || MasterNodeManager.TryCaptureSourceEmission(
                    m_server,
                    NodeManager,
                    out emission,
                    Generation,
                    isGlobalServerEventSink: MonitoringAllEvents);
            using var emissionLease = emission;
            if (!admitted)
            {
                return;
            }
            m_server.EventManager?.AdmitEvent(m_server.DefaultSystemContext, instance);

            // the evaluation shared by the items with an equal filter, when the event
            // consumer offers one for this event.
            SharedEventEvaluation? sharedEvaluation = TakeSharedEvaluation(
                instance,
                out SharedEventFilterKey? sharedKey);

            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }
                EnsureNotificationAuthorization();
                if (!CanUseEventSource(admittedGeneration) ||
                    (!capturedSourceEmission && !CanQueueSourceEmission()))
                {
                    return;
                }

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

                FilterContext context = GetEventFilterContext(out string[]? preferredLocales);

                // event filter must be specified.
                if (FilterToUse is not EventFilter filter)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                // the shared evaluation was made for the filter and locales the event was
                // grouped by; an item modified since then evaluates the event itself.
                if (sharedKey == null || !sharedKey.Matches(filter, preferredLocales))
                {
                    sharedEvaluation = null;
                }

                // apply filter first: an event the where clause would have rejected was
                // never going to be queued, so it must not count as a queue overflow.
                bool overrideRetain = false;
                if (!bypassFilter)
                {
                    bool passedFilter = sharedEvaluation != null
                        ? sharedEvaluation.PassesWhereClause(context, filter, instance)
                        : filter.WhereClause.Evaluate(context, instance);
                    if (!ApplyFilteredRetain(filter, instance, passedFilter, out overrideRetain))
                    {
                        return;
                    }
                }

                // check for space in the queue only for events that passed the filter.
                if (m_eventQueueHandler.SetQueueOverflowIfFull())
                {
                    return;
                }

                // fetch the event fields. The trailing filtered retain event reads them
                // through a wrapper that reports Retain = false to this client only. The
                // wrapper stays the handle of the queued event, so a later change of the
                // select clauses rebuilds its fields with the same Retain override. Its
                // fields are this item's own, the other fields are shared by the group.
                EventFieldList fields;
                if (overrideRetain)
                {
                    fields = GetEventFields(context, filter, new FilteredRetainTarget(instance));
                }
                else if (sharedEvaluation != null)
                {
                    fields = CreateEventFieldList(
                        instance,
                        sharedEvaluation.GetEventFields(this, context, filter, instance));
                }
                else
                {
                    fields = GetEventFields(context, filter, instance);
                }
                NodeManagerGeneration? previous = m_admittedSourceGeneration;
                m_admittedSourceGeneration = admittedGeneration ?? emission?.Generation;
                try
                {
                    QueueEvent(fields);
                }
                finally
                {
                    m_admittedSourceGeneration = previous;
                }
            }
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        /// <param name="fields">The event field list.</param>
        public virtual void QueueEvent(EventFieldList fields)
        {
            QueueEventFields(fields, TakeAdmittedSourceGeneration());
        }

        private void QueueEventFields(
            EventFieldList fields,
            NodeManagerGeneration? admittedGeneration)
        {
            bool capturedSourceEmission = admittedGeneration is not null;
            MasterNodeManager.SourceEmissionAdmission? emission = null;
            bool admitted = capturedSourceEmission || MasterNodeManager.TryCaptureSourceEmission(
                    m_server,
                    NodeManager,
                    out emission,
                    Generation,
                    isGlobalServerEventSink: MonitoringAllEvents);
            using var emissionLease = emission;
            if (!admitted)
            {
                return;
            }
            m_server.EventManager?.AdmitEventFields(fields);
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }
                EnsureNotificationAuthorization();
                if (!CanUseEventSource(admittedGeneration) ||
                    (!capturedSourceEmission && !CanQueueSourceEmission()))
                {
                    return;
                }
                m_eventQueueHandler!.QueueEvent(fields);
                m_readyToPublish = true;
                m_readyToTrigger = true;
                SignalReady();
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
        /// <para>
        /// <paramref name="overrideRetain"/> is <c>true</c> only for the trailing event a
        /// condition produces as it leaves this client's where clause. Part 9, 5.5.2 requires
        /// that event to carry a client specific <c>Retain = false</c> whatever the server
        /// retains, so the caller has to substitute that value into the delivered fields
        /// without touching the shared filter target.
        /// </para>
        /// </remarks>
        protected bool CanSendFilteredAlarm(
            IFilterContext context,
            EventFilter filter,
            IFilterTarget instance,
            out bool overrideRetain)
        {
            bool passedFilter = filter.WhereClause.Evaluate(context, instance);
            return ApplyFilteredRetain(filter, instance, passedFilter, out overrideRetain);
        }

        /// <summary>
        /// Applies filtered retain to the where clause result of an event: see
        /// <see cref="CanSendFilteredAlarm"/>.
        /// </summary>
        private bool ApplyFilteredRetain(
            EventFilter filter,
            IFilterTarget instance,
            bool passedFilter,
            out bool overrideRetain)
        {
            overrideRetain = false;

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

            // out of scope now: send the trailing event if it was in scope before, and
            // tell this client that it no longer retains the condition.
            overrideRetain = wasInFilterScope;
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
                EnsureNotificationAuthorization();

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

                    // with discardOldest TRUE the overflow event is placed at the beginning
                    // of the queue (Part 4 5.13.1.5).
                    if (m_discardOldest && m_eventQueueHandler.Overflow)
                    {
                        notifications.Enqueue(CreateEventQueueOverflowEventFields(context));
                        EventQueueOverflowReported();
                        maxNotificationsPerPublish--;
                    }

                    m_eventQueueHandler.Publish(
                        context,
                        notifications,
                        maxNotificationsPerPublish,
                        out bool overflowEventDue);

                    // with discardOldest FALSE it follows the events queued before the loss.
                    if (overflowEventDue)
                    {
                        notifications.Enqueue(CreateEventQueueOverflowEventFields(context));
                        EventQueueOverflowReported();
                    }

                    moreValuesToPublish =
                        m_eventQueueHandler.ItemsInQueue > 0 || m_eventQueueHandler.Overflow;
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
        /// Creates the fields of an EventQueueOverflowEventType event for this item.
        /// </summary>
        private EventFieldList CreateEventQueueOverflowEventFields(OperationContext context)
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
            return GetEventFields(
                new FilterContext(
                    m_server.NamespaceUris,
                    m_server.TypeTree,
                    Session?.PreferredLocales!,
                    m_server.Telemetry),
                (EventFilter)FilterToUse!,
                e);
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
                EnsureNotificationAuthorization();

                // check if the item reports data changes.
                if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
                {
                    return false;
                }

                // a disabled item reports nothing, not even a pending resend.
                if (MonitoringMode == MonitoringMode.Disabled)
                {
                    m_resendData = false;
                    return false;
                }

                if (maxNotificationsPerPublish == 0)
                {
                    return IsReadyToPublish || m_resendData;
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
                    AggregateFilter?.Publish(DateTime.UtcNow);

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
                            out ServiceResult error,
                            out bool required))
                    {
                        PublishObservedValue(context, notifications, diagnostics, value, error, required);

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
                    PublishObservedValue(
                        context, notifications, diagnostics, m_lastValue, m_lastError!, m_finalValuePending);
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

            // select the translation the session prefers for a localized text value
            // (Part 4 5.4); the queued value keeps all its translations.
            if (value.WrappedValue.TypeInfo.BuiltInType == BuiltInType.LocalizedText &&
                m_server.ResourceManager != null)
            {
                value = value.WithWrappedValue(
                    m_server.ResourceManager.TranslateValue(context.PreferredLocales, value.WrappedValue));
            }

            // apply timestamp filter.
            if (m_timestampsToReturn is not TimestampsToReturn.Server and not TimestampsToReturn.Both)
            {
                value = value.WithServerTimestamp(DateTimeUtc.MinValue);
            }

            if (m_timestampsToReturn is not TimestampsToReturn.Source and not TimestampsToReturn.Both)
            {
                value = value.WithSourceTimestamp(DateTimeUtc.MinValue);
            }

            // not pooled: a notification lives until its message was sent and acknowledged, so
            // a publishing cycle holds all notifications of the cycle at once. Recycling them
            // through the shared activator pool cost more CPU (pool contention, touching cold
            // instances again) than allocating them.
            var item = new MonitoredItemNotification
            {
                ClientHandle = ClientHandle,
                Value = value
            };

            ServerUtils.ReportPublishValue(NodeId, Id, value);
            notifications.Enqueue(item);
            if (m_publishingFinalValue)
            {
                m_valuePublication?.TrackNotification(item);
            }

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
                    EnsureNotificationAuthorization();
                }
            }
        }

        /// <inheritdoc/>
        void IMonitoredItemGenerationBinding.BindSubscription(ISubscription subscription)
        {
            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(MonitoredItem));
                }
                if (!ReferenceEquals(m_subscription, subscription))
                {
                    throw new InvalidOperationException("The monitored item belongs to another subscription.");
                }
                if (Generation?.TryGetGeneration(out NodeManagerGeneration? generation) != true)
                {
                    throw new InvalidOperationException("The monitored item's generation is unavailable.");
                }
                m_subscriptionOwned = true;
                generation!.MonitoredItems[this] = subscription;
                UpdateUnresolvedOwnership();
            }
        }

        /// <inheritdoc/>
        void IMonitoredItemGenerationBinding.UnbindSubscription(ISubscription subscription)
        {
            lock (m_lock)
            {
                if (!ReferenceEquals(m_subscription, subscription))
                {
                    return;
                }
                m_subscriptionOwned = false;
                m_slot.UnresolvedItems.TryRemove(this, out _);
                if (Generation?.TryGetGeneration(out NodeManagerGeneration? generation) == true)
                {
                    generation!.MonitoredItems.TryRemove(this, out _);
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

        private MonitoringFilter? FilterToUse => m_filterToUse?.Filter;

        private AggregationFilterHandler? AggregateFilter => m_filterToUse as AggregationFilterHandler;

        /// <inheritdoc/>
        public IStoredMonitoredItem ToStorableMonitoredItem()
        {
            lock (m_lock)
            {
                EnsureNotificationAuthorization();
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
                    IsTerminal = m_isTerminal,
                    Encoding = DataEncoding,
                    FilterToUse = FilterToUse!,
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

        private bool CanQueueSourceEmission()
        {
            return !m_isDetached &&
                (!m_isDeleted ||
                    (!UsesExternalValueSource && (MonitoredItemType & MonitoredItemTypeMask.DataChange) != 0));
        }

        /// <summary>
        /// Compares the bound generation without consulting the public slot identifier or taking its lock.
        /// </summary>
        private bool IsBoundToGeneration(NodeManagerGeneration generation)
        {
            return Generation is { } reference &&
                reference.TryGetGeneration(out NodeManagerGeneration? boundGeneration) &&
                ReferenceEquals(boundGeneration, generation);
        }

        /// <summary>
        /// Checks a borrowed event source while the item lock protects its current binding.
        /// </summary>
        private bool CanUseEventSource(NodeManagerGeneration? generation)
        {
            return generation is null ||
                MonitoringAllEvents ||
                IsBoundToGeneration(generation) ||
                (m_isDetached && ReferenceEquals(m_slot, generation.Slot));
        }

        /// <summary>
        /// Captures a detached copy of the source node metadata needed to revise and validate item modifications.
        /// </summary>
        /// <param name="server">The server providing the type tree and system context.</param>
        /// <param name="managerHandle">The node manager handle of the monitored source node.</param>
        /// <returns>The metadata snapshot, or null when the handle does not carry a node.</returns>
        private static NodeState? CaptureSourceMetadata(IServerInternal server, object managerHandle)
        {
            if (managerHandle is not NodeHandle { Node: { } source })
            {
                return null;
            }
            if (source is not BaseVariableState variable)
            {
                return new BaseObjectState(null) { NodeId = source.NodeId };
            }
            var metadata = new BaseDataVariableState(null)
            {
                NodeId = source.NodeId,
                DataType = server.TypeTree.IsTypeOf(variable.DataType, DataTypeIds.Number)
                    ? DataTypeIds.Number
                    : variable.DataType,
                MinimumSamplingInterval = variable.MinimumSamplingInterval
            };
            if (variable.FindChild(server.DefaultSystemContext, QualifiedName.From(BrowseNames.EURange)) is
                PropertyState property && property.Value.TryGetStructure<Range>(out Range? range))
            {
                metadata.AddChild(new PropertyState(metadata)
                {
                    BrowseName = QualifiedName.From(BrowseNames.EURange),
                    Value = new Variant(new ExtensionObject(new Range { Low = range.Low, High = range.High }))
                });
            }
            return metadata;
        }

        private void QueueNodeIdUnknown()
        {
            // a disabled item queues nothing (Part 4, 5.13.4.1); m_isDeleted is kept and
            // the notification is queued when the item leaves DISABLED.
            if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0 ||
                MonitoringMode == MonitoringMode.Disabled)
            {
                return;
            }

            DataValue value = CreateNodeIdUnknownValue();
            var error = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            AddRequiredValueToQueue(
                value,
                error,
                replaceExisting: true);
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
                (FilterToUse as DataChangeFilter)!,
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
                // runs for every sampled value of an unfiltered item; AreEqual only reads it.
                filter = s_defaultDataChangeFilter;
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
                        if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) != 0)
                        {
                            QueueSize = 1;
                        }

                        if ((MonitoredItemType & MonitoredItemTypeMask.Events) != 0)
                        {
                            QueueSize = 1000;
                        }
                    }

                    // create data queue.
                    if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) != 0)
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

                        // seed the new queue only with a value that is still waiting to be
                        // published. m_lastValue is kept after publishing for the change
                        // filter, so re-queueing it then would report it a second time.
                        if (queueLastValue && m_readyToPublish && !m_lastValue.IsNull)
                        {
                            if (m_finalValuePending)
                            {
                                m_dataChangeQueueHandler.QueueRequiredValue(m_lastValue, m_lastError!);
                            }
                            else
                            {
                                m_dataChangeQueueHandler.QueueValue(m_lastValue, m_lastError!);
                            }
                        }
                    }
                    else // create event queue.
                    {
                        m_eventQueueHandler ??= new EventQueueHandler(
                            IsDurable,
                            m_monitoredItemQueueFactory,
                            Id,
                            m_server.Telemetry,
                            QueueOverflowHandler,
                            null);
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
            // Only the restore of the subscription manager stages the queues, because it
            // completes or aborts them once every stored subscription was restored.
            m_queueRestoreStaged = RestoredQueueStaging.IsRequested;
            switch (MonitoringMode)
            {
                case MonitoringMode.Reporting:
                case MonitoringMode.Sampling:
                    // check if queuing is disabled.
                    if (QueueSize == 0)
                    {
                        if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) != 0)
                        {
                            QueueSize = 1;
                        }

                        if ((MonitoredItemType & MonitoredItemTypeMask.Events) != 0)
                        {
                            QueueSize = 1000;
                        }
                    }

                    // create data queue.
                    if ((MonitoredItemType & MonitoredItemTypeMask.DataChange) != 0)
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
                            // the queue keeps its stored contents until the restore completes.
                            if (m_queueRestoreStaged)
                            {
                                (restoredQueue as IRestorableMonitoredItemQueue)?.BeginRestore();
                            }

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
                            if (m_queueRestoreStaged)
                            {
                                m_dataChangeQueueHandler.RestorableQueue?.BeginRestore();
                            }

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
                            // the queue keeps its stored contents until the restore completes.
                            if (m_queueRestoreStaged)
                            {
                                (restoredQueue as IRestorableMonitoredItemQueue)?.BeginRestore();
                            }

                            // initialize with existing queue
                            m_eventQueueHandler = new EventQueueHandler(
                                restoredQueue,
                                m_discardOldest,
                                m_server.Telemetry,
                                QueueOverflowHandler,
                                null);

                            // the queue may have been persisted with a size that was
                            // revised since, e.g. a literal size of 1 before queueSize 1
                            // was mapped to the server minimum (Part 4 §7.21).
                            if (restoredQueue.QueueSize != QueueSize)
                            {
                                m_eventQueueHandler.SetQueueSize(QueueSize, m_discardOldest);
                            }
                        }
                        else
                        {
                            // create new queue
                            m_eventQueueHandler = new EventQueueHandler(
                                IsDurable,
                                m_monitoredItemQueueFactory,
                                Id,
                                m_server.Telemetry,
                                QueueOverflowHandler,
                                null);
                            if (m_queueRestoreStaged)
                            {
                                m_eventQueueHandler.RestorableQueue?.BeginRestore();
                            }
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

        /// <summary>
        /// Update the event queue overflow count once an EventQueueOverflowEventType
        /// event is reported to the client.
        /// </summary>
        private void EventQueueOverflowReported()
        {
            (m_subscription as ISubscriptionPublishPipeline)?.EventQueueOverflowHandler();
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
                m_subscriptionOwned = false;
                if (Generation?.TryGetGeneration(out NodeManagerGeneration? generation) == true)
                {
                    generation!.MonitoredItems.TryRemove(this, out _);
                }
                m_valuePublication?.Complete(published: false);
                SetDetachState(m_isDetached, m_isDeleted);
            }

            DisposeQueueHandlers();
        }

        /// <summary>
        /// Releases the queues after disposal has forbidden further source deliveries.
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

        /// <summary>
        /// Writes the queues the restore of the item staged to their storage. Called
        /// once every stored subscription was restored and the store was told so, so
        /// it does not throw: a queue that fails is logged, and the others are still
        /// committed.
        /// </summary>
        internal void CommitQueueRestore()
        {
            lock (m_lock)
            {
                if (!m_queueRestoreStaged)
                {
                    return;
                }
                m_queueRestoreStaged = false;
            }
            ForEachRestorableQueue(static queue => queue.CommitRestore());
        }

        /// <summary>
        /// Keeps the stored contents of the queues of the item when they are
        /// disposed, and drops what the restore staged. Used when an aborted restore
        /// releases the item: the stored contents are needed when the restore is
        /// retried.
        /// </summary>
        internal void AbortQueueRestore()
        {
            lock (m_lock)
            {
                m_queueRestoreStaged = false;
            }
            ForEachRestorableQueue(static queue => queue.AbortRestore());
        }

        /// <summary>
        /// Applies the action to each restorable queue of the item while holding the
        /// item lock, so that it is serialized with the changes of the queues. A queue
        /// whose action throws is logged and does not keep the action from the others.
        /// </summary>
        private void ForEachRestorableQueue(Action<IRestorableMonitoredItemQueue> action)
        {
            var queues = new List<IRestorableMonitoredItemQueue>(4);
            lock (m_lock)
            {
                AddRestorableQueue(queues, m_dataChangeQueueHandler?.RestorableQueue);
                AddRestorableQueue(queues, m_eventQueueHandler?.RestorableQueue);
                AddRestorableQueue(queues, m_restoredDataChangeQueue as IRestorableMonitoredItemQueue);
                AddRestorableQueue(queues, m_restoredEventQueue as IRestorableMonitoredItemQueue);
                foreach (IRestorableMonitoredItemQueue queue in queues)
                {
                    try
                    {
                        action(queue);
                    }
                    catch (Exception ex)
                    {
                        m_logger.FailedToCompleteQueueRestoreForMonitoredItem(ex, Id, SubscriptionId);
                    }
                }
            }
        }

        private static void AddRestorableQueue(
            List<IRestorableMonitoredItemQueue> queues,
            IRestorableMonitoredItemQueue? queue)
        {
            if (queue != null && !queues.Exists(existing => ReferenceEquals(existing, queue)))
            {
                queues.Add(queue);
            }
        }

        private readonly Lock m_lock = new();

        /// <summary>The owner of this item's notification epoch.</summary>
        private ISession? m_notificationSession;

        /// <summary>The epoch of this item's queued values, events, and aggregate state.</summary>
        private NotificationAuthorization? m_notificationAuthorization;
        private bool m_queueRestoreStaged;

        /// <summary>
        /// The filter applied when a monitored item has none: no deadband, StatusValue trigger.
        /// Shared and never modified.
        /// </summary>
        private static readonly DataChangeFilter s_defaultDataChangeFilter = new()
        {
            DeadbandType = (uint)DeadbandType.None,
            DeadbandValue = 0.0
        };
        private readonly ILogger m_logger;
        private bool m_isDisposed;

        /// <summary>Whether the subscription still owns this item's generation binding.</summary>
        private bool m_subscriptionOwned;

        /// <summary>The stable slot owning this item, including while its node is unavailable.</summary>
        private NodeManagerSlot m_slot = null!;

        /// <summary>Whether invalidation permanently forbids this item's adoption.</summary>
        private bool m_isTerminal;

        private readonly TimeProvider m_timeProvider;
        private IServerInternal m_server;
        private IAsyncNodeManager? m_nodeManager;
        private string? m_indexRange;
        private NumericRange m_parsedIndexRange;
        private TimestampsToReturn m_timestampsToReturn;
        private IMonitoringFilter? m_filterToUse;
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
        private int m_readySignal;
        private ServiceResult? m_samplingError;
        private bool m_triggered;
        private bool m_resendData;
        private HashSet<string>? m_filteredRetainConditionIds;
        private RejectedSelectClauses? m_rejectedSelectClauses;
        private EventFilterContext? m_eventFilterContext;
        private bool m_isDetached;
        private bool m_isDeleted;
    }

    /// <summary>
    /// Source-generated log messages for MonitoredItem.
    /// </summary>
    internal static partial class MonitoredItemLog
    {
        /// <summary>
        /// Logs rejection of a durable item when the registered queue factory lacks durable support.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 0, Level = LogLevel.Error,
            Message = "Durable subscription was created but no MonitoredItemQueueFactory that supports durable " +
                "queues was registered, monitored item with id {Id} could not be created, " +
                "SubscriptionId={SubscriptionId}")]
        public static partial void DurableSubscriptionWasCreateButNoMonitoredItemQueueFactory(
            this ILogger logger,
            uint id,
            uint subscriptionId);

        /// <summary>
        /// Traces a monitored item being marked for triggered publication.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 1, Level = LogLevel.Trace,
            Message = "SetTriggered[{Id}], SubscriptionId={SubscriptionId}")]
        public static partial void SetTriggeredId(this ILogger logger, uint id, uint subscriptionId);

        /// <summary>
        /// Traces a monitored item's monitoring-mode transition.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 2, Level = LogLevel.Trace,
            Message = "MONITORING MODE[{MonitoredItemId}] {Previous} -> {New}, SubscriptionId={SubscriptionId}")]
        public static partial void MONITORINGMODEMonitoredItemIdPreviousNew(
            this ILogger logger,
            uint monitoredItemId,
            MonitoringMode previous,
            MonitoringMode @new,
            uint subscriptionId);

        /// <summary>
        /// Traces a raw value received by a monitored item.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 3, Level = LogLevel.Trace,
            Message = "RECEIVED VALUE[{MonitoredItemId}] Value={Value}, SubscriptionId={SubscriptionId}")]
        public static partial void RECEIVEDVALUEMonitoredItemIdValueValue(
            this ILogger logger,
            uint monitoredItemId,
            Variant value,
            uint subscriptionId);

        /// <summary>
        /// Traces a sample rejected by aggregate processing because its timestamp is out of order.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 4, Level = LogLevel.Trace,
            Message = "Value received out of order: {SourceTimestamp}, ServerHandle={MonitoredItemId}, " +
                "SubscriptionId={SubscriptionId}")]
        public static partial void ValueReceivedOutOfOrderSourceTimestampServerHandle(
            this ILogger logger,
            string? sourceTimestamp,
            uint monitoredItemId,
            uint subscriptionId);

        /// <summary>
        /// Traces a queued value together with its status and overflow indication.
        /// </summary>
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

        /// <summary>
        /// Traces the queue size when a monitored item publishes notifications.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 6, Level = LogLevel.Trace,
            Message = "MONITORED ITEM: Publish(QueueSize={QueueSize}), " +
                "SubscriptionId={SubscriptionId}, MonitoredItemId={MonitoredItemId}")]
        public static partial void MONITOREDITEMPublishQueueSizeQueueSize(
            this ILogger logger,
            int queueSize,
            uint subscriptionId,
            uint monitoredItemId);

        /// <summary>
        /// Emits the compatibility trace for a monitored item's publication-readiness state.
        /// </summary>
        [LoggerMessage(
            EventId = ServerCompatibilityEventIds.MonitoredItemReady,
            EventName = "MonitoredItemReady",
            Level = LogLevel.Trace,
            Message = "IsReadyToPublish[{Id}] {State}")]
        public static partial void CompatibilityMonitoredItemReady(
            this ILogger logger,
            uint id,
            string state);

        /// <summary>
        /// Logs a failure to restore a monitored item's persisted data-change queue.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 7, Level = LogLevel.Error,
            Message = "Failed to restore queue for monitored item with id {MonitoredItemId}," +
                " SubscriptionId={SubscriptionId}")]
        public static partial void FailedToRestoreQueueForMonitoredItem(
            this ILogger logger,
            Exception ex,
            uint monitoredItemId,
            uint subscriptionId);

        /// <summary>
        /// Logs a failure to restore a monitored item's persisted event queue.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 8, Level = LogLevel.Error,
            Message = "Failed to restore queue for monitored item with id {Id}," +
                " SubscriptionId={SubscriptionId}")]
        public static partial void FailedToRestoreQueueForMonitoredItem2(
            this ILogger logger,
            Exception ex,
            uint id,
            uint subscriptionId);

        /// <summary>
        /// Logs a restored queue that failed to complete or abort its staged restore.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MonitoredItem + 9, Level = LogLevel.Error,
            Message = "Failed to complete the restore of the queue of monitored item with id {MonitoredItemId}," +
                " SubscriptionId={SubscriptionId}")]
        public static partial void FailedToCompleteQueueRestoreForMonitoredItem(
            this ILogger logger,
            Exception ex,
            uint monitoredItemId,
            uint subscriptionId);
    }
}
