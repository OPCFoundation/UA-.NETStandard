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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores the current set of MonitoredItems for a Node.
    /// </summary>
    /// <remarks>
    /// An instance of this object is created the first time a MonitoredItem is
    /// created for any attribute of a Node. The object is deleted when the last
    /// MonitoredItem is deleted.
    /// </remarks>
    public class MonitoredNode2 : IDisposable
    {
        private const int k_defaultChannelCapacity = 4096;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredNode2"/> class.
        /// </summary>
        /// <param name="nodeManager">The node manager.</param>
        /// <param name="server">The server.</param>
        /// <param name="node">The node.</param>
        /// <param name="enableMultipleEventConsumers">
        /// When <c>true</c>, enables dynamic scaling of consumer tasks based on
        /// the number of event monitored items. The Server node
        /// (<see cref="ObjectIds.Server"/>) always opts in automatically.
        /// </param>
        public MonitoredNode2(
            IAsyncNodeManager nodeManager,
            IServerInternal server,
            NodeState node,
            bool enableMultipleEventConsumers = false)
            : this(nodeManager, server, node, enableMultipleEventConsumers, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredNode2"/> class
        /// with an explicit <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="nodeManager">The node manager.</param>
        /// <param name="server">The server.</param>
        /// <param name="node">The node.</param>
        /// <param name="enableMultipleEventConsumers">
        /// When <c>true</c>, enables dynamic scaling of consumer tasks.
        /// </param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used for the operation-context
        /// cache lifetime and for wall-clock <c>SourceTimestamp</c> /
        /// <c>ServerTimestamp</c> values produced inside this node. When
        /// <c>null</c>, the time provider exposed by the server (via
        /// <see cref="ITimeProviderProvider"/>) is used, falling back to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public MonitoredNode2(
            IAsyncNodeManager nodeManager,
            IServerInternal server,
            NodeState node,
            bool enableMultipleEventConsumers,
            TimeProvider? timeProvider)
        {
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            Node = node ?? throw new ArgumentNullException(nameof(node));
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_logger = server.Telemetry?.CreateLogger<MonitoredNode2>();
            m_useMultipleConsumers = enableMultipleEventConsumers || node.NodeId == ObjectIds.Server;
            m_channel = Channel.CreateBounded<INodeNotification>(new BoundedChannelOptions(k_defaultChannelCapacity)
            {
                SingleReader = !m_useMultipleConsumers,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            m_consumerCts = new CancellationTokenSource();
            m_consumerTask = Task.Run(() => ProcessChannelAsync(m_consumerCts.Token));

            if (m_useMultipleConsumers)
            {
                m_additionalConsumers = [];
            }
        }

        /// <summary>
        /// Gets or sets the NodeManager which the MonitoredNode belongs to.
        /// </summary>
        public IAsyncNodeManager NodeManager { get; set; }

        /// <summary>
        /// Gets or sets the Node being monitored.
        /// </summary>
        public NodeState Node { get; set; }

        /// <summary>
        /// Gets the current list of data change MonitoredItems.
        /// </summary>
        public ConcurrentDictionary<uint, IDataChangeMonitoredItem2> DataChangeMonitoredItems { get; } = new();

        /// <summary>
        /// Gets the current list of event MonitoredItems.
        /// </summary>
        public ConcurrentDictionary<uint, IEventMonitoredItem> EventMonitoredItems { get; } = new();

        /// <summary>
        /// Gets a value indicating whether this instance has monitored items.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance has monitored items; otherwise, <c>false</c>.
        /// </value>
        public bool HasMonitoredItems
        {
            get
            {
                if (DataChangeMonitoredItems != null && !DataChangeMonitoredItems.IsEmpty)
                {
                    return true;
                }

                if (EventMonitoredItems != null && !EventMonitoredItems.IsEmpty)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Adds the specified data change monitored item.
        /// </summary>
        /// <param name="datachangeItem">The monitored item.</param>
        public void Add(IDataChangeMonitoredItem2 datachangeItem)
        {
            lock (m_rebindLock)
            {
                bool wasEmpty = DataChangeMonitoredItems.IsEmpty;
                DataChangeMonitoredItems.TryAdd(datachangeItem.Id, datachangeItem);

                Node.OnStateChangedAsync = OnMonitoredNodeChangedAsync;

                // Subscribe to namespace default permission changes when the first item is added.
                if (wasEmpty && m_server.ConfigurationNodeManager != null)
                {
                    m_server.ConfigurationNodeManager.DefaultPermissionsChanged += OnDefaultPermissionsChanged;
                }
            }
        }

        /// <summary>
        /// Removes the specified data change monitored item.
        /// </summary>
        /// <param name="datachangeItem">The monitored item.</param>
        public void Remove(IDataChangeMonitoredItem2 datachangeItem)
        {
            lock (m_rebindLock)
            {
                if (DataChangeMonitoredItems.TryRemove(datachangeItem.Id, out _))
                {
                    // Remove the cached context for the monitored item
                    m_contextCache.TryRemove(datachangeItem.Id, out _);
                    m_permissionCache.TryRemove(datachangeItem.Id, out _);
                }

                if (DataChangeMonitoredItems.IsEmpty)
                {
                    Node.OnStateChangedAsync = null;

                    // Unsubscribe from namespace default permission changes when the last item is removed.
                    m_server.ConfigurationNodeManager?.DefaultPermissionsChanged -= OnDefaultPermissionsChanged;
                }
            }
        }

        /// <summary>
        /// Adds the specified event monitored item.
        /// </summary>
        /// <param name="eventItem">The monitored item.</param>
        public void Add(IEventMonitoredItem eventItem)
        {
            lock (m_rebindLock)
            {
                EventMonitoredItems.TryAdd(eventItem.Id, eventItem);

                Node.OnReportEventAsync = OnReportEventAsync;

                // Scale up: add a consumer task for each new event MI beyond the first.
                if (m_useMultipleConsumers && m_additionalConsumers != null)
                {
                    lock (m_additionalConsumersLock)
                    {
                        // The primary consumer task always runs; add additional ones
                        // so total consumers = EventMonitoredItems.Count.
                        int totalDesired = EventMonitoredItems.Count;
                        if (totalDesired > m_additionalConsumers.Count + 1)
                        {
                            AddConsumer();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Removes the specified event monitored item.
        /// </summary>
        /// <param name="eventItem">The monitored item.</param>
        public void Remove(IEventMonitoredItem eventItem)
        {
            lock (m_rebindLock)
            {
                EventMonitoredItems.TryRemove(eventItem.Id, out _);
                DropEventPermissionCacheEntries(eventItem.Id);

                if (EventMonitoredItems.IsEmpty)
                {
                    Node.OnReportEventAsync = null;
                }

                // Scale down: remove a consumer task when MIs decrease (keep at least 1 total = primary).
                if (m_useMultipleConsumers && m_additionalConsumers != null)
                {
                    lock (m_additionalConsumersLock)
                    {
                        // Total consumers = 1 (primary) + m_additionalConsumers.Count
                        int totalDesired = Math.Max(1, EventMonitoredItems.Count);
                        while (m_additionalConsumers.Count + 1 > totalDesired)
                        {
                            RemoveLastConsumer();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Rebinds the monitored node to a replacement NodeState and node manager.
        /// </summary>
        internal ServiceResult Rebind(IAsyncNodeManager nodeManager, NodeState node)
        {
            if (nodeManager == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (node == null || node.NodeId != Node.NodeId)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            lock (m_rebindLock)
            {
                if (m_disposed)
                {
                    return StatusCodes.BadInvalidState;
                }

                if (!ReferenceEquals(Node, node))
                {
                    if (Node.OnStateChangedAsync == OnMonitoredNodeChangedAsync)
                    {
                        Node.OnStateChangedAsync = null;
                    }

                    if (Node.OnReportEventAsync == OnReportEventAsync)
                    {
                        Node.OnReportEventAsync = null;
                    }
                }

                NodeManager = nodeManager;
                Node = node;

                if (!DataChangeMonitoredItems.IsEmpty)
                {
                    Node.OnStateChangedAsync = OnMonitoredNodeChangedAsync;
                }

                if (!EventMonitoredItems.IsEmpty)
                {
                    Node.OnReportEventAsync = OnReportEventAsync;
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Called asynchronously when a Node produces an event. Clones the event state and enqueues
        /// it for the consumer, awaiting the (bounded) channel so back-pressure is preserved without
        /// blocking a thread.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="e">The event.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask OnReportEventAsync(
            ISystemContext context,
            NodeState node,
            IFilterTarget e,
            CancellationToken cancellationToken = default)
        {
            if (m_disposed)
            {
                return;
            }

            IFilterTarget eventTarget = e;
            // Build snapshot so the original event state is preserved when the consumer processes it.
            if (e is NodeState eventState)
            {
                eventTarget = (IFilterTarget)eventState.Clone();
            }

            var notification = new EventSnapshot
            {
                Context = context,
                EventTargetSnapshot = eventTarget
            };

            try
            {
                await m_channel.Writer.WriteAsync(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                // The channel was completed during shutdown/disposal.
            }
        }

        /// <summary>
        /// Synchronous wrapper over <see cref="OnReportEventAsync"/>. Completes inline when the
        /// channel has capacity; blocks only when the bounded channel is full (only synchronous
        /// callers pay that cost).
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="e">The event.</param>
        public void OnReportEvent(ISystemContext context, NodeState node, IFilterTarget e)
        {
            ValueTask report = OnReportEventAsync(context, node, e, default);
            if (!report.IsCompletedSuccessfully)
            {
                report.AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Called asynchronously when the state of a Node changes. Reads each monitored attribute at
        /// enqueue time - through the async entry point, so a genuinely asynchronous read handler is
        /// honored - and enqueues the snapshot, awaiting the bounded channel for back-pressure. No
        /// thread is blocked.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="changes">The mask indicating what changes have occurred.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask OnMonitoredNodeChangedAsync(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes,
            CancellationToken cancellationToken = default)
        {
            if (m_disposed)
            {
                return;
            }

            if ((changes & NodeStateChangeMasks.Deleted) != 0)
            {
                foreach (IDataChangeMonitoredItem2 monitoredItem in
                    DataChangeMonitoredItems.Values)
                {
                    if (monitoredItem is IMonitoredItemLifecycle lifecycle)
                    {
                        lifecycle.MarkNodeDeleted();
                    }
                }
                foreach (IEventMonitoredItem monitoredItem in EventMonitoredItems.Values)
                {
                    if (monitoredItem is IMonitoredItemLifecycle lifecycle)
                    {
                        lifecycle.MarkNodeDeleted();
                    }
                }
                return;
            }

            if (DataChangeMonitoredItems == null || DataChangeMonitoredItems.IsEmpty)
            {
                return;
            }

            // Collect the distinct attribute IDs being monitored so we can pre-read them.
            // The raw value is read once without any index range or data encoding; each
            // monitored item applies its own range/encoding in ProcessDataChangeSnapshotAsync.
            var attributeIds = new HashSet<uint>();
            foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in DataChangeMonitoredItems)
            {
                IDataChangeMonitoredItem2 item = kvp.Value;
                bool isValueAttribute = item.AttributeId == Attributes.Value;
                if (isValueAttribute && (changes & NodeStateChangeMasks.Value) == 0)
                {
                    continue;
                }
                if (!isValueAttribute && (changes & NodeStateChangeMasks.NonValue) == 0)
                {
                    continue;
                }
                attributeIds.Add(item.AttributeId);
            }

            var attributeSnapshots = new Dictionary<uint, DataValue>(attributeIds.Count);

            foreach (uint attributeId in attributeIds)
            {
                var dataValue = new DataValue(
                    default,
                    StatusCodes.Good,
                    m_timeProvider.GetUtcNow().UtcDateTime,
                    DateTime.MinValue);

                // Read at enqueue time via the async entry point: ReadAttributeAsync honors an
                // asynchronous value read handler (OnReadValueAsync) when one is registered and
                // otherwise completes synchronously. The value is therefore materialised for this
                // change before it is enqueued (strict enqueue-time snapshot) without blocking.
                (_, attributeSnapshots[attributeId]) = await node.ReadAttributeAsync(
                    context,
                    attributeId,
                    default,
                    QualifiedName.Null,
                    dataValue,
                    cancellationToken).ConfigureAwait(false);
            }

            var notification = new DataChangeSnapshot
            {
                Context = context,
                NodeId = node.NodeId,
                Changes = changes,
                AttributeSnapshots = attributeSnapshots
            };

            try
            {
                await m_channel.Writer.WriteAsync(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                // The channel was completed during shutdown/disposal.
            }
        }

        /// <summary>
        /// Synchronous wrapper over <see cref="OnMonitoredNodeChangedAsync"/>. Completes inline for a
        /// synchronously-readable node with channel capacity; blocks only when a read is genuinely
        /// asynchronous or the bounded channel is full (only synchronous callers pay that cost).
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="changes">The mask indicating what changes have occurred.</param>
        public void OnMonitoredNodeChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            ValueTask change = OnMonitoredNodeChangedAsync(context, node, changes, default);
            if (!change.IsCompletedSuccessfully)
            {
                change.AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Consumer loop that processes notifications from the channel.
        /// </summary>
        private async Task ProcessChannelAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (INodeNotification notification in m_channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        if (notification is EventSnapshot eventSnapshot)
                        {
                            await ProcessEventSnapshotAsync(eventSnapshot, cancellationToken).ConfigureAwait(false);
                        }
                        else if (notification is DataChangeSnapshot dataChangeSnapshot)
                        {
                            await ProcessDataChangeSnapshotAsync(dataChangeSnapshot, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        m_logger?.MonitoredNode2ConsumerEncounteredAnErrorProcessing(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                m_logger?.MonitoredNode2ConsumerTerminatedUnexpectedly(ex);
            }
        }

        /// <summary>
        /// Processes an <see cref="EventSnapshot"/> from the channel.
        /// </summary>
        private async Task ProcessEventSnapshotAsync(EventSnapshot snapshot, CancellationToken cancellationToken)
        {
            // Extract EventTypeId + SourceNodeId once so the per-item
            // permission cache can be keyed by them. The asynchronous
            // permission lookup is the dominant cost; caching the verdict
            // per (item, eventType, sourceNode) avoids two role-validation
            // calls per delivered event.
            (NodeId eventTypeId, NodeId sourceNodeId) = ExtractEventIdentity(snapshot.EventTargetSnapshot);

            foreach (KeyValuePair<uint, IEventMonitoredItem> kvp in EventMonitoredItems)
            {
                IEventMonitoredItem monitoredItem = kvp.Value;
                IFilterTarget e = snapshot.EventTargetSnapshot;

                if (e is AuditEventState || (e is InstanceStateSnapshot sn && sn.Handle is AuditEventState))
                {
                    if (!m_server.Auditing)
                    {
                        continue;
                    }
                    if (monitoredItem?.Session?.EndpointDescription?.SecurityMode !=
                            MessageSecurityMode.SignAndEncrypt &&
                        monitoredItem?.Session?.EndpointDescription?.TransportProfileUri !=
                            Profiles.HttpsBinaryTransport)
                    {
                        continue;
                    }
                }

                ServiceResult validationResult = await GetOrAddEventPermissionAsync(
                    monitoredItem!,
                    e,
                    eventTypeId,
                    sourceNodeId,
                    cancellationToken).ConfigureAwait(false);

                if (ServiceResult.IsBad(validationResult))
                {
                    continue;
                }

                monitoredItem?.QueueEvent(e);
            }
        }

        /// <summary>
        /// Extracts the <c>EventTypeId</c> and <c>SourceNode</c> from an
        /// event payload. Returns default <see cref="NodeId"/> values when
        /// the payload is not a <see cref="BaseEventState"/> (or wrapped
        /// in an <see cref="InstanceStateSnapshot"/> over one), in which
        /// case the result is not cached.
        /// </summary>
        private static (NodeId EventTypeId, NodeId SourceNodeId) ExtractEventIdentity(IFilterTarget filterTarget)
        {
            var baseEventState = filterTarget as BaseEventState;
            if (baseEventState == null && filterTarget is InstanceStateSnapshot snapshot)
            {
                baseEventState = snapshot.Handle as BaseEventState;
            }

            if (baseEventState == null)
            {
                return (default, default);
            }

            NodeId eventTypeId = baseEventState.EventType?.Value ?? default;
            NodeId sourceNodeId = baseEventState.SourceNode?.Value ?? default;
            return (eventTypeId, sourceNodeId);
        }

        /// <summary>
        /// Returns the cached permission verdict for the
        /// (item, eventType, sourceNode) tuple, computing and caching it
        /// on miss. Falls back to an uncached lookup when either NodeId
        /// is null so events that don't carry the standard identity
        /// fields still go through the per-event check.
        /// </summary>
        private async ValueTask<ServiceResult> GetOrAddEventPermissionAsync(
            IEventMonitoredItem monitoredItem,
            IFilterTarget filterTarget,
            NodeId eventTypeId,
            NodeId sourceNodeId,
            CancellationToken cancellationToken)
        {
            if (eventTypeId.IsNull || sourceNodeId.IsNull)
            {
                // Not a cacheable identity — defer to the existing path.
                return await NodeManager.ValidateEventRolePermissionsAsync(
                    monitoredItem,
                    filterTarget,
                    cancellationToken).ConfigureAwait(false);
            }

            var key = new EventPermissionCacheKey(monitoredItem.Id, eventTypeId, sourceNodeId);
            if (m_eventPermissionCache.TryGetValue(key, out ServiceResult? cached) && cached != null)
            {
                return cached;
            }

            ServiceResult result = await NodeManager.ValidateEventRolePermissionsAsync(
                monitoredItem,
                filterTarget,
                cancellationToken).ConfigureAwait(false);

            m_eventPermissionCache[key] = result;
            return result;
        }

        /// <summary>
        /// Processes a <see cref="DataChangeSnapshot"/> from the channel.
        /// </summary>
        private async Task ProcessDataChangeSnapshotAsync(DataChangeSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (DataChangeMonitoredItems == null)
            {
                return;
            }

            // If RolePermissions or UserRolePermissions have changed, invalidate the permission cache.
            if ((snapshot.Changes & NodeStateChangeMasks.RolePermissions) != 0)
            {
                m_permissionCache.Clear();
                m_eventPermissionCache.Clear();
            }

            foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in DataChangeMonitoredItems)
            {
                IDataChangeMonitoredItem2 monitoredItem = kvp.Value;
                OperationContext operationContext;
                ISystemContext contextToUse;

                if (snapshot.Context is ServerSystemContext serverContext)
                {
                    ServerSystemContext serverSystemContextToUse = GetOrCreateContext(serverContext, monitoredItem);
                    operationContext = serverSystemContextToUse.OperationContext!;
                    contextToUse = serverSystemContextToUse;
                }
                else
                {
                    operationContext = new OperationContext(monitoredItem);
                    contextToUse = snapshot.Context;
                }

                if (monitoredItem.AttributeId == Attributes.Value &&
                    (snapshot.Changes & NodeStateChangeMasks.Value) != 0)
                {
                    if (!m_permissionCache.TryGetValue(monitoredItem.Id, out ServiceResult? validationResult))
                    {
                        validationResult = await NodeManager.ValidateRolePermissionsAsync(
                            operationContext,
                            snapshot.NodeId,
                            PermissionType.Read,
                            cancellationToken).ConfigureAwait(false);
                        m_permissionCache[monitoredItem.Id] = validationResult;
                    }

                    if (ServiceResult.IsBad(validationResult))
                    {
                        continue;
                    }

                    if (snapshot.AttributeSnapshots.TryGetValue(monitoredItem.AttributeId, out DataValue snapshotValue))
                    {
                        DataValue valueToQueue = ApplyRangeAndEncoding(contextToUse, monitoredItem, snapshotValue);
                        monitoredItem.QueueValue(valueToQueue, valueToQueue.StatusCode);
                    }

                    continue;
                }

                if (monitoredItem.AttributeId != Attributes.Value &&
                    (snapshot.Changes & NodeStateChangeMasks.NonValue) != 0)
                {
                    if (snapshot.AttributeSnapshots.TryGetValue(monitoredItem.AttributeId, out DataValue snapshotValue))
                    {
                        monitoredItem.QueueValue(snapshotValue, ServiceResult.Good);
                    }
                }
            }
        }

        /// <summary>
        /// Reads the value of an attribute and reports it to the MonitoredItem.
        /// </summary>
        public async ValueTask QueueValueAsync(
            ISystemContext context,
            NodeState node,
            IDataChangeMonitoredItem2 monitoredItem,
            CancellationToken cancellationToken = default)
        {
            var value = new DataValue(
                Variant.Null,
                StatusCodes.Good,
                DateTime.MinValue,
                m_timeProvider.GetUtcNow().UtcDateTime);
            (ServiceResult error, value) = await node.ReadAttributeAsync(
                context,
                monitoredItem.AttributeId,
                monitoredItem.IndexRange,
                monitoredItem.DataEncoding,
                value,
                cancellationToken).ConfigureAwait(false);

            if (ServiceResult.IsBad(error))
            {
                value = default;
            }

            monitoredItem.QueueValue(value, error);
        }

        /// <summary>
        /// Applies the monitored item's <see cref="IDataChangeMonitoredItem2.IndexRange"/> and
        /// <see cref="IDataChangeMonitoredItem2.DataEncoding"/> to the raw snapshot value.
        /// The snapshot <see cref="DataValue"/> is shared across items, so the <see cref="Variant"/>
        /// is cloned before transformation to avoid mutating the shared copy.
        /// </summary>
        private static DataValue ApplyRangeAndEncoding(
            ISystemContext context,
            IDataChangeMonitoredItem2 monitoredItem,
            in DataValue snapshotValue)
        {
            // Clone the Variant so we do not mutate the shared snapshot value.
            Variant value = snapshotValue.WrappedValue.Copy();

            ServiceResult applyResult = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                context,
                monitoredItem.IndexRange,
                monitoredItem.DataEncoding,
                ref value);

            if (ServiceResult.IsBad(applyResult))
            {
                return new DataValue(
                    Variant.Null,
                    applyResult.StatusCode,
                    snapshotValue.SourceTimestamp,
                    snapshotValue.ServerTimestamp);
            }

            return new DataValue(value, snapshotValue.StatusCode, snapshotValue.SourceTimestamp, snapshotValue.ServerTimestamp);
        }

        /// <summary>
        /// Gets or creates a cached context for the monitored item.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <returns>The cached or newly created context.</returns>
        private ServerSystemContext GetOrCreateContext(
            ServerSystemContext context,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            uint monitoredItemId = monitoredItem.Id;
            long currentTimestamp = m_timeProvider.GetTimestamp();
            OperationContext operationContext;

            // Check if the context already exists in the cache
            if (m_contextCache.TryGetValue(
                    monitoredItemId,
                    out (ServerSystemContext Context, long CreatedAtTimestamp) cachedEntry))
            {
                // Refresh context if the owning session changed (e.g. after subscription transfer)
                // or if the cache entry has expired.
                // Note: identity-based invalidation is handled proactively by
                // InvalidatePermissionCacheForSession when ActivateSession changes the identity.
                if (cachedEntry.Context.OperationContext!.Session != monitoredItem.Session ||
                    m_timeProvider.GetElapsedTime(cachedEntry.CreatedAtTimestamp) > m_cacheLifetime)
                {
                    operationContext = new OperationContext(monitoredItem);

                    ServerSystemContext updatedContext = context.Copy(
                        operationContext);
                    m_contextCache[monitoredItemId] = (updatedContext, currentTimestamp);

                    // Invalidate the permission cache since the session context has changed.
                    m_permissionCache.TryRemove(monitoredItemId, out _);

                    return updatedContext;
                }

                return cachedEntry.Context;
            }

            // Create a new context and add it to the cache
            operationContext = new OperationContext(monitoredItem);
            ServerSystemContext newContext = context.Copy(operationContext);
            m_contextCache.TryAdd(monitoredItemId, (newContext, currentTimestamp));

            return newContext;
        }

        /// <summary>
        /// Invalidates the permission and context caches for all monitored items belonging
        /// to the specified session. Call this when the session's user identity changes so
        /// that role permissions are re-evaluated on the next data change notification.
        /// </summary>
        /// <param name="sessionId">The NodeId of the session whose identity has changed.</param>
        public void InvalidatePermissionCacheForSession(NodeId sessionId)
        {
            foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in DataChangeMonitoredItems)
            {
                IDataChangeMonitoredItem2 monitoredItem = kvp.Value;

                if (monitoredItem?.Session?.Id.Equals(sessionId) == true)
                {
                    uint id = monitoredItem.Id;
                    m_permissionCache.TryRemove(id, out _);
                    m_contextCache.TryRemove(id, out _);
                }
            }

            foreach (KeyValuePair<uint, IEventMonitoredItem> kvp in EventMonitoredItems)
            {
                IEventMonitoredItem monitoredItem = kvp.Value;
                if (monitoredItem?.Session?.Id.Equals(sessionId) == true)
                {
                    DropEventPermissionCacheEntries(monitoredItem.Id);
                }
            }
        }

        /// <summary>
        /// Called when the namespace default permissions (<c>DefaultRolePermissions</c> or
        /// <c>DefaultUserRolePermissions</c>) change. Invalidates the entire permission cache
        /// so that all entries are re-validated on the next value change.
        /// </summary>
        private void OnDefaultPermissionsChanged(object? sender, EventArgs e)
        {
            m_permissionCache.Clear();
            m_eventPermissionCache.Clear();
        }

        /// <summary>
        /// Drops every <see cref="EventPermissionCacheKey"/> entry that
        /// belongs to the specified monitored item id.
        /// </summary>
        private void DropEventPermissionCacheEntries(uint monitoredItemId)
        {
            foreach (KeyValuePair<EventPermissionCacheKey, ServiceResult> entry in m_eventPermissionCache)
            {
                if (entry.Key.MonitoredItemId == monitoredItemId)
                {
                    m_eventPermissionCache.TryRemove(entry.Key, out _);
                }
            }
        }

        /// <summary>
        /// Adds a new consumer task to the pool for the regular channel.
        /// Must be called while holding the <see cref="m_additionalConsumersLock"/> lock.
        /// </summary>
        private void AddConsumer()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(m_consumerCts.Token);
            var task = Task.Run(() => ProcessChannelAsync(cts.Token));
            m_additionalConsumers!.Add(new ConsumerEntry(task, cts));
        }

        /// <summary>
        /// Removes the last additional consumer task from the pool.
        /// Must be called while holding the <see cref="m_additionalConsumersLock"/> lock.
        /// </summary>
        private void RemoveLastConsumer()
        {
            if (m_additionalConsumers!.Count == 0)
            {
                return;
            }

            int lastIndex = m_additionalConsumers.Count - 1;
            ConsumerEntry entry = m_additionalConsumers[lastIndex];
            m_additionalConsumers.RemoveAt(lastIndex);
            entry.Cts.Cancel();
            entry.Cts.Dispose();
        }

        /// <summary>
        /// Represents a single consumer task and its associated cancellation token.
        /// </summary>
        private readonly struct ConsumerEntry
        {
            public ConsumerEntry(Task task, CancellationTokenSource cts)
            {
                Task = task;
                Cts = cts;
            }

            public Task Task { get; }
            public CancellationTokenSource Cts { get; }
        }

        private readonly ConcurrentDictionary<uint, (ServerSystemContext Context, long CreatedAtTimestamp)> m_contextCache =
            new();

        private readonly ConcurrentDictionary<uint, ServiceResult> m_permissionCache =
            new();

        private readonly ConcurrentDictionary<EventPermissionCacheKey, ServiceResult> m_eventPermissionCache =
            new();

        private readonly TimeSpan m_cacheLifetime = TimeSpan.FromMinutes(5);

        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger? m_logger;
        private readonly Channel<INodeNotification> m_channel;
        private readonly CancellationTokenSource m_consumerCts;
        private readonly Task m_consumerTask;
        private readonly bool m_useMultipleConsumers;
        private readonly List<ConsumerEntry>? m_additionalConsumers;
        private readonly Lock m_additionalConsumersLock = new();
        private readonly Lock m_rebindLock = new();
        private bool m_disposed;

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
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;

            if (disposing)
            {
                // Complete the writer; consumers drain remaining items and exit normally.
                m_channel.Writer.TryComplete();

                // Wait for additional consumer tasks to finish.
                if (m_additionalConsumers != null)
                {
                    ConsumerEntry[] entries;
                    lock (m_additionalConsumersLock)
                    {
                        entries = [.. m_additionalConsumers];
                        m_additionalConsumers.Clear();
                    }

                    Task[] tasks = Array.ConvertAll(entries, e => e.Task);

                    try
                    {
                        // Bound the wait — do not block indefinitely if consumers are stuck.
                        bool completed = Task.WaitAll(tasks, TimeSpan.FromSeconds(5));

                        if (!completed)
                        {
                            m_logger?.MonitoredNode2AdditionalConsumersDidNotDrainWithin();

                            foreach (ConsumerEntry entry in entries)
                            {
                                entry.Cts.Cancel();
                            }

                            try
                            {
                                Task.WaitAll(tasks);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger?.MonitoredNode2AdditionalConsumersFaultedDuring(ex);
                    }
                    finally
                    {
                        foreach (ConsumerEntry entry in entries)
                        {
                            entry.Cts.Dispose();
                        }
                    }
                }

                if (m_consumerTask != null)
                {
                    try
                    {
                        // Bound the wait — do not block indefinitely if the consumer is stuck.
                        bool completed = m_consumerTask
                            .Wait(TimeSpan.FromSeconds(5));

                        if (!completed)
                        {
                            m_logger?.MonitoredNode2ConsumerDidNotDrainWithin5();
                            m_consumerCts.Cancel();

                            try
                            {
                                m_consumerTask.GetAwaiter().GetResult();
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger?.MonitoredNode2ConsumerFaultedDuringShutdown(ex);
                    }
                }

                // Cancel and dispose only after the consumer has finished.
                m_consumerCts?.Cancel();
                m_consumerCts?.Dispose();
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for MonitoredNode.
    /// </summary>
    internal static partial class MonitoredNodeLog
    {
        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 0, Level = LogLevel.Warning,
            Message = "MonitoredNode2 consumer encountered an error processing a notification.")]
        public static partial void MonitoredNode2ConsumerEncounteredAnErrorProcessing(
            this ILogger logger,
            Exception ex);

        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 1, Level = LogLevel.Error,
            Message = "MonitoredNode2 consumer terminated unexpectedly.")]
        public static partial void MonitoredNode2ConsumerTerminatedUnexpectedly(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 2, Level = LogLevel.Warning,
            Message = "MonitoredNode2 additional consumers did not drain within 5 s; cancelling forcibly.")]
        public static partial void MonitoredNode2AdditionalConsumersDidNotDrainWithin(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 3, Level = LogLevel.Warning,
            Message = "MonitoredNode2 additional consumers faulted during shutdown.")]
        public static partial void MonitoredNode2AdditionalConsumersFaultedDuring(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 4, Level = LogLevel.Warning,
            Message = "MonitoredNode2 consumer did not drain within 5 s; cancelling forcibly.")]
        public static partial void MonitoredNode2ConsumerDidNotDrainWithin5(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 5, Level = LogLevel.Warning,
            Message = "MonitoredNode2 consumer faulted during shutdown.")]
        public static partial void MonitoredNode2ConsumerFaultedDuringShutdown(this ILogger logger, Exception ex);
    }

}
