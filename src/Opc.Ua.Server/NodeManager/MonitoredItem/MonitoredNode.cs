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
    public class MonitoredNode2 : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Bounds retained enqueue-time values, including large structures and subscriber-specific copies,
        /// by applying asynchronous producer backpressure before thousands of snapshots accumulate.
        /// </summary>
        private const int k_defaultChannelCapacity = 2;

        /// <summary>
        /// While no notification waits in the channel, the consumer queues an event on its
        /// own for up to a thousandth of a second before it queues the remaining items in
        /// parallel.
        /// </summary>
        private const long k_parallelEventDeliveryBudgetDivisor = 1000;

        /// <summary>
        /// The fewest items queued in one range when an event is queued in parallel, so a
        /// range is worth waking a pool thread for.
        /// </summary>
        private const int k_minItemsPerEventDeliveryRange = 32;

        /// <summary>
        /// The most events the consumer queues in parallel at once when it is behind: the
        /// event it took and the events waiting behind it for the same items.
        /// </summary>
        private const int k_maxEventsPerBatch = 4;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredNode2"/> class.
        /// </summary>
        /// <param name="nodeManager">The node manager.</param>
        /// <param name="server">The server.</param>
        /// <param name="node">The node.</param>
        /// <param name="enableMultipleEventConsumers">
        /// When <c>true</c>, delivers each event concurrently to independent
        /// monitored items while preserving event order. The Server node
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
        /// When <c>true</c>, delivers each event concurrently to independent
        /// monitored items while preserving event order.
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
            m_parallelEventDeliveryBudget = m_timeProvider.TimestampFrequency / k_parallelEventDeliveryBudgetDivisor;
            m_useMultipleConsumers = enableMultipleEventConsumers || node.NodeId == ObjectIds.Server;
            m_channel = Channel.CreateBounded<INodeNotification>(new BoundedChannelOptions(k_defaultChannelCapacity)
            {
                // One loop dequeues notifications in order. Parallel delivery fans out within
                // an event and completes before the next event can reach the same monitored item.
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            m_consumerCts = new CancellationTokenSource();
            m_consumerTask = Task.Run(() => ProcessChannelAsync(m_consumerCts.Token));
        }

        /// <summary>
        /// Gets or sets the NodeManager which the MonitoredNode belongs to.
        /// </summary>
        public IAsyncNodeManager NodeManager
        {
            get => m_nodeManager;
            set
            {
                m_nodeManager = value ?? throw new ArgumentNullException(nameof(value));
                Generation = NodeManagerGenerationRegistry.GetOrCreate(value).Reference;
            }
        }

        /// <summary>
        /// Gets the non-owning identity of the generation currently bound to this monitored node.
        /// </summary>
        internal NodeManagerGenerationReference Generation { get; private set; } = null!;

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
        /// <exception cref="ObjectDisposedException">The monitored node has been disposed.</exception>
        public void Add(IDataChangeMonitoredItem2 datachangeItem)
        {
            lock (m_rebindLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(MonitoredNode2));
                }
                bool wasEmpty = !HasMonitoredItems;
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
                    Interlocked.Increment(ref m_permissionGeneration);
                    m_permissionCache.TryRemove(datachangeItem.Id, out _);
                }

                if (DataChangeMonitoredItems.IsEmpty)
                {
                    Node.OnStateChangedAsync = null;

                    // Unsubscribe from namespace default permission changes when the last item is removed.
                    if (EventMonitoredItems.IsEmpty)
                    {
                        m_server.ConfigurationNodeManager?.DefaultPermissionsChanged -= OnDefaultPermissionsChanged;
                    }
                }
            }
        }

        /// <summary>
        /// Admits a subscriber-specific force-resample onto the monitored node's consumer.
        /// </summary>
        /// <param name="source">The exact source node whose semantics changed.</param>
        /// <param name="item">The monitored item that must be resampled.</param>
        /// <returns><c>true</c> when the current lifecycle owns and queued the request.</returns>
        public bool TryQueueSemanticResample(NodeState source, IDataChangeMonitoredItem2 item)
        {
            if (source == null || item == null)
            {
                return false;
            }

            lock (m_rebindLock)
            {
                if (m_disposed ||
                    m_draining ||
                    !ReferenceEquals(source, Node) ||
                    !DataChangeMonitoredItems.TryGetValue(item.Id, out IDataChangeMonitoredItem2? current) ||
                    !ReferenceEquals(current, item))
                {
                    return false;
                }

                item.SetSemanticsChanged();
                m_pendingSemanticResamples.Enqueue(new SemanticResample(
                    GetSubscriberContextTemplate(),
                    source,
                    item));
                // The request is retained independently of the bounded notification channel.
                // If the channel is full, its consumer drains the pending request after older
                // notifications, so synchronous admission cannot silently lose a resample.
                _ = m_channel.Writer.TryWrite(new SemanticResampleSignal(GetSubscriberContextTemplate()));
                return true;
            }
        }

        /// <summary>
        /// Adds the specified event monitored item.
        /// </summary>
        /// <param name="eventItem">The monitored item.</param>
        /// <exception cref="ObjectDisposedException">The monitored node has been disposed.</exception>
        public void Add(IEventMonitoredItem eventItem)
        {
            lock (m_rebindLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(MonitoredNode2));
                }
                bool wasEmpty = !HasMonitoredItems;
                EventMonitoredItems.TryAdd(eventItem.Id, eventItem);

                Node.OnReportEventAsync = OnReportEventAsync;
                if (wasEmpty && m_server.ConfigurationNodeManager != null)
                {
                    m_server.ConfigurationNodeManager.DefaultPermissionsChanged += OnDefaultPermissionsChanged;
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
                Interlocked.Increment(ref m_permissionGeneration);
                DropEventPermissionCacheEntries(eventItem.Id);

                if (EventMonitoredItems.IsEmpty)
                {
                    Node.OnReportEventAsync = null;
                    if (DataChangeMonitoredItems.IsEmpty)
                    {
                        m_server.ConfigurationNodeManager?.DefaultPermissionsChanged -= OnDefaultPermissionsChanged;
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
                InvalidatePermissionCaches();

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
            MasterNodeManager.SourceEmissionAdmission? emission = null;
            try
            {
                if (!MasterNodeManager.TryCaptureSourceEmission(
                    m_server,
                    NodeManager,
                    out emission,
                    Generation,
                    isGlobalServerEventSink: Node.NodeId == ObjectIds.Server,
                    context: context))
                {
                    return;
                }
                IFilterTarget eventTarget = e;
                m_server.EventManager?.AdmitEvent(context, e);
                // Build snapshot so the original event state is preserved when the consumer processes it.
                if (e is NodeState eventState)
                {
                    eventTarget = (IFilterTarget)eventState.Clone();
                    m_server.EventManager?.TransferEventIdentity(e, eventTarget);
                }

                var notification = new EventSnapshot
                {
                    Context = context,
                    EventTargetSnapshot = eventTarget,
                    MonitoredItems = [.. EventMonitoredItems.Values],
                    SourceEmission = emission
                };
                await m_channel.Writer.WriteAsync(notification, cancellationToken).ConfigureAwait(false);
                emission = null;
            }
            catch (ChannelClosedException)
            {
                // The channel was completed during shutdown/disposal.
            }
            finally
            {
                emission?.Dispose();
            }
        }

        /// <summary>
        /// Synchronous wrapper over <see cref="OnReportEventAsync"/>. Completes inline when the
        /// channel has capacity; blocks only when the bounded channel is full (only synchronous
        /// callers pay that cost).
        /// </summary>
        /// <remarks>
        /// Retained for callers written against the synchronous
        /// <see cref="NodeState.OnReportEvent"/> contract. Nothing in the stack
        /// wires this any more - notifiers are attached through
        /// <see cref="NodeState.OnReportEventAsync"/> - and the blocking wait a
        /// full channel forces here can starve the thread pool, so new code
        /// must use <see cref="OnReportEventAsync"/>.
        /// </remarks>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="e">The event.</param>
        [Obsolete("Use OnReportEventAsync")]
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
                if (Generation?.TryGetGeneration(out NodeManagerGeneration? generation) == true &&
                    generation?.Gate.State == NodeManagerGenerationState.Live)
                {
                    foreach (IDataChangeMonitoredItem2 monitoredItem in
                        DataChangeMonitoredItems.Values)
                    {
                        if (IsInCurrentGeneration(monitoredItem) &&
                            monitoredItem is IMonitoredItemGenerationBinding binding)
                        {
                            binding.Invalidate(terminal: false);
                        }
                    }
                    foreach (IEventMonitoredItem monitoredItem in EventMonitoredItems.Values)
                    {
                        if (IsInCurrentGeneration(monitoredItem) &&
                            monitoredItem is IMonitoredItemGenerationBinding binding)
                        {
                            binding.Invalidate(terminal: false);
                        }
                    }
                }
                return;
            }

            if (DataChangeMonitoredItems == null || DataChangeMonitoredItems.IsEmpty)
            {
                return;
            }
            MasterNodeManager.SourceEmissionAdmission? emission = null;
            try
            {
                if (!MasterNodeManager.TryCaptureItemEmission(m_server, NodeManager, Generation, out emission))
                {
                    return;
                }
                // A collection expression races the dictionary's Count against CopyTo during concurrent registration.
                KeyValuePair<uint, IDataChangeMonitoredItem2>[] monitoredItems = DataChangeMonitoredItems.ToArray();
                // Collect the distinct attribute IDs being monitored so we can pre-read them.
                // The raw value is read once without any index range or data encoding; each
                // monitored item applies its own range/encoding in ProcessDataChangeSnapshotAsync.
                var attributeIds = new HashSet<uint>();
                foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in monitoredItems)
                {
                    IDataChangeMonitoredItem2 item = kvp.Value;
                    if (item is MonitoredItem { UsesExternalValueSource: true })
                    {
                        continue;
                    }
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
                        DateTime.MinValue,
                        m_timeProvider.GetUtcNow().UtcDateTime);

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

                Dictionary<uint, (DataValue Value, ISession? Session, IUserIdentity? Identity)>?
                    subscriberValueSnapshots = null;
                if (attributeSnapshots.TryGetValue(Attributes.Value, out DataValue reportedValue) &&
                    reportedValue.StatusCode == StatusCodes.BadUserAccessDenied &&
                    node is BaseVariableState variable &&
                    (variable.OnReadUserAccessLevel is not null ||
                        (NodeManager is AsyncCustomNodeManager custom &&
                            custom.HasValueReadAuthorization(variable))))
                {
                    ServerSystemContext serverContext = GetSubscriberContextTemplate();
                    foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in monitoredItems)
                    {
                        if (kvp.Value.AttributeId != Attributes.Value ||
                            kvp.Value is MonitoredItem { UsesExternalValueSource: true })
                        {
                            continue;
                        }
                        ServerSystemContext subscriberContext = GetSubscriberContext(serverContext, kvp.Value);
                        ServiceResult authorization = await MonitoredReadAuthorization.ValidateAsync(
                            NodeManager, node, subscriberContext, Attributes.Value, cancellationToken)
                            .ConfigureAwait(false);
                        if (ServiceResult.IsBad(authorization))
                        {
                            (subscriberValueSnapshots ??= [])[kvp.Key] = (
                                DataValue.FromStatusCode(authorization.StatusCode),
                                subscriberContext.OperationContext?.Session, subscriberContext.UserIdentity);
                            continue;
                        }
                        (_, DataValue subscriberValue) = await node.ReadAttributeAsync(
                            subscriberContext,
                            Attributes.Value,
                            default,
                            QualifiedName.Null,
                            new DataValue(
                                default,
                                StatusCodes.Good,
                                DateTime.MinValue,
                                m_timeProvider.GetUtcNow().UtcDateTime),
                            cancellationToken).ConfigureAwait(false);
                        (subscriberValueSnapshots ??= [])[kvp.Key] = (
                            subscriberValue, subscriberContext.OperationContext?.Session, subscriberContext.UserIdentity);
                    }
                }

                var notification = new DataChangeSnapshot
                {
                    Context = context,
                    NodeId = node.NodeId,
                    Changes = changes,
                    MonitoredItems = monitoredItems,
                    AttributeSnapshots = attributeSnapshots,
                    SubscriberValueSnapshots = subscriberValueSnapshots,
                    SourceEmission = emission
                };
                await m_channel.Writer.WriteAsync(notification, cancellationToken).ConfigureAwait(false);
                emission = null;
            }
            catch (ChannelClosedException)
            {
                // The channel was completed during shutdown/disposal.
            }
            finally
            {
                emission?.Dispose();
            }
        }

        /// <summary>
        /// Waits for notifications already admitted to the report-by-exception consumer without stopping it.
        /// </summary>
        /// <remarks>
        /// Callers must quiesce producers first. This is a sampling fence, not evidence that any client has
        /// received a value. Cancellation releases the fence's completion even if its channel entry remains
        /// behind an uncooperative callback.
        /// </remarks>
        internal async ValueTask WaitForPendingNotificationsAsync(CancellationToken cancellationToken)
        {
            var fence = new NotificationFence(m_server.DefaultSystemContext);
            try
            {
                try
                {
                    await m_channel.Writer.WriteAsync(fence, cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    await m_consumerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                await Task.WhenAny(fence.Completion.Task, m_consumerTask)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                fence.Completion.TrySetCanceled(cancellationToken);
            }
        }

        /// <summary>
        /// Synchronous wrapper over <see cref="OnMonitoredNodeChangedAsync"/>. Completes inline for a
        /// synchronously-readable node with channel capacity; blocks only when a read is genuinely
        /// asynchronous or the bounded channel is full (only synchronous callers pay that cost).
        /// </summary>
        /// <remarks>
        /// Retained for callers written against the synchronous
        /// <see cref="NodeState.OnStateChanged"/> contract. Nothing in the
        /// stack wires this any more, and an asynchronous read handler turns it
        /// into a blocking wait that can starve the thread pool, so new code
        /// must use <see cref="OnMonitoredNodeChangedAsync"/>.
        /// </remarks>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="changes">The mask indicating what changes have occurred.</param>
        [Obsolete("Use OnMonitoredNodeChangedAsync")]
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
                            EventSnapshot? next = eventSnapshot;
                            while (next is not null)
                            {
                                using var emission = next.SourceEmission;
                                next = await ProcessEventSnapshotAsync(next, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            using var emission = notification.SourceEmission;
                            if (notification is DataChangeSnapshot dataChangeSnapshot)
                            {
                                await ProcessDataChangeSnapshotAsync(dataChangeSnapshot, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            else if (notification is SemanticResample semanticResample)
                            {
                                await ProcessSemanticResampleAsync(semanticResample, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            else if (notification is SemanticResampleSignal)
                            {
                            }
                            else if (notification is NotificationFence fence)
                            {
                                await ProcessPendingSemanticResamplesAsync(cancellationToken).ConfigureAwait(false);
                                fence.Completion.TrySetResult(true);
                            }
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

                    if (!m_channel.Reader.TryPeek(out _))
                    {
                        try
                        {
                            await ProcessPendingSemanticResamplesAsync(cancellationToken).ConfigureAwait(false);
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
        /// Processes an <see cref="EventSnapshot"/> from the channel, together with the
        /// events that wait behind it for the same items when the consumer is behind.
        /// </summary>
        /// <returns>
        /// An event taken from the channel that goes to other items than the batch and is
        /// processed next, or <c>null</c>.
        /// </returns>
        private async Task<EventSnapshot?> ProcessEventSnapshotAsync(
            EventSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            int count = CollectEventDeliveries(
                snapshot.EventTargetSnapshot,
                0,
                snapshot.MonitoredItems,
                snapshot.SourceEmission);
            int collected = count;
            int events = 0;
            EventSnapshot? different = null;
            List<MasterNodeManager.SourceEmissionAdmission?>? batchedEmissions = null;
            try
            {
                if (count == 0)
                {
                    return null;
                }

                m_eventBatch[events++] = CreateEventDelivery(snapshot, 0);
                if (!m_useMultipleConsumers)
                {
                    for (int ii = 0; ii < count; ii++)
                    {
                        await StartEventDelivery(m_eventBatch[0], ii, cancellationToken).ConfigureAwait(false);
                    }
                    return null;
                }

                // while no notification waits behind the event, the consumer keeps up on its
                // own and does not wake pool threads: it queues the event itself and splits
                // only what is left of an event that takes longer than the budget. When
                // notifications wait, the consumer is behind: it takes the events waiting for
                // the same items, too, and splits them at once, so that the pool threads are
                // woken once for a few events. Each item is in one range, which queues the
                // events of the batch to it in order, so the items are queued on several
                // threads while every item queues its events in order: the next batch is
                // taken from the channel only after all ranges completed.
                List<Task>? pending = null;
                int next = 0;
                if (m_parallelEventDeliveryBudget >= 0 && m_channel.Reader.Count == 0)
                {
                    long start = m_timeProvider.GetTimestamp();
                    while (next < count)
                    {
                        StartEventDeliveries(m_eventBatch[0], next, next + 1, ref pending, cancellationToken);
                        next++;
                        if (cancellationToken.IsCancellationRequested ||
                            m_timeProvider.GetTimestamp() - start > m_parallelEventDeliveryBudget)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    while (events < m_eventBatch.Length &&
                        m_channel.Reader.TryPeek(out INodeNotification? waiting) &&
                        waiting is EventSnapshot &&
                        m_channel.Reader.TryRead(out waiting))
                    {
                        var waitingEvent = (EventSnapshot)waiting;
                        int offset = events * count;
                        int waitingCount = CollectEventDeliveries(
                            waitingEvent.EventTargetSnapshot,
                            offset,
                            waitingEvent.MonitoredItems,
                            waitingEvent.SourceEmission);
                        collected = offset + waitingCount;
                        if (!HasSameItems(offset, waitingCount, count))
                        {
                            different = waitingEvent;
                            break;
                        }
                        if (!ReferenceEquals(
                            snapshot.SourceEmission?.Generation,
                            waitingEvent.SourceEmission?.Generation))
                        {
                            different = waitingEvent;
                            break;
                        }
                        (batchedEmissions ??= []).Add(waitingEvent.SourceEmission);
                        m_eventBatch[events++] = CreateEventDelivery(waitingEvent, offset);
                    }
                }

                try
                {
                    if (next < count)
                    {
                        await DeliverEventsInRangesAsync(events, next, count, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (pending != null)
                    {
                        await Task.WhenAll(pending).ConfigureAwait(false);
                    }
                }
                return different;
            }
            finally
            {
                if (batchedEmissions is not null)
                {
                    foreach (MasterNodeManager.SourceEmissionAdmission? emission in batchedEmissions)
                    {
                        emission?.Dispose();
                    }
                }
                // no reference to an item or event outlives the batch in the buffers.
                Array.Clear(m_eventDeliveries, 0, collected);
                Array.Clear(m_eventBatch, 0, events);
            }
        }

        /// <summary>
        /// Creates the delivery of an event whose items start at <paramref name="offset"/>
        /// in <see cref="m_eventDeliveries"/>.
        /// </summary>
        private EventDelivery CreateEventDelivery(EventSnapshot snapshot, int offset)
        {
            // Extract EventTypeId + SourceNodeId once so the per-item
            // permission cache can be keyed by them. The asynchronous
            // permission lookup is the dominant cost; caching the verdict
            // per (item, eventType, sourceNode) avoids two role-validation
            // calls per delivered event.
            IFilterTarget target = snapshot.EventTargetSnapshot;
            (NodeId eventTypeId, NodeId sourceNodeId) = ExtractEventIdentity(target);
            return new EventDelivery(
                target,
                eventTypeId,
                sourceNodeId,
                snapshot.SourceEmission?.Generation,
                offset);
        }

        /// <summary>
        /// Whether the <paramref name="count"/> deliveries at <paramref name="offset"/> go to
        /// the same items in the same order as the first <paramref name="expected"/> ones.
        /// </summary>
        private bool HasSameItems(int offset, int count, int expected)
        {
            if (count != expected)
            {
                return false;
            }

            for (int ii = 0; ii < count; ii++)
            {
                if (!ReferenceEquals(m_eventDeliveries[offset + ii].Item, m_eventDeliveries[ii].Item))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Puts the items an event is delivered to into <see cref="m_eventDeliveries"/> from
        /// <paramref name="offset"/> on, each with the evaluation of the event it shares with
        /// the items with an equal filter.
        /// </summary>
        /// <remarks>
        /// Runs on the consumer, so the groups are looked up by one thread; the deliveries
        /// that run in parallel use the evaluations, which synchronize themselves. The buffer
        /// is reused for every batch, which the consumer delivers one after the other.
        /// </remarks>
        /// <returns>The number of deliveries.</returns>
        private int CollectEventDeliveries(
            IFilterTarget target,
            int offset,
            IEventMonitoredItem[] monitoredItems,
            MasterNodeManager.SourceEmissionAdmission? sourceEmission)
        {
            bool isAuditEvent =
                target is AuditEventState || (target is InstanceStateSnapshot sn && sn.Handle is AuditEventState);

            // the items with an equal filter share one evaluation of the event: the where
            // clause and the select clauses are evaluated once per group, not per item.
            var evaluations = new SharedEventEvaluations();
            int count = 0;
            foreach (IEventMonitoredItem item in monitoredItems)
            {
                if (Node.NodeId != ObjectIds.Server &&
                    sourceEmission is
                    { Generation.Gate.State: not NodeManagerGenerationState.Live } &&
                    !IsInCurrentGeneration(item) &&
                    item is not IMonitoredItemGenerationBinding { IsSourceUnavailable: true })
                {
                    continue;
                }
                if (isAuditEvent &&
                    (!m_server.Auditing ||
                    (item.Session?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                        item.Session?.EndpointDescription?.TransportProfileUri != Profiles.HttpsBinaryTransport)))
                {
                    continue;
                }

                if (offset + count == m_eventDeliveries.Length)
                {
                    Array.Resize(ref m_eventDeliveries, Math.Max(16, (offset + count) * 2));
                }

                MonitoredItem.SharedEventEvaluation? evaluation = evaluations.Get(
                    item,
                    out MonitoredItem.SharedEventFilterKey? key);
                m_eventDeliveries[offset + count++] = new EventItemDelivery(item, key, evaluation);
            }
            return count;
        }

        /// <summary>
        /// Delivers the first <paramref name="events"/> events of <see cref="m_eventBatch"/>
        /// to their items in [<paramref name="start"/>, <paramref name="end"/>) in up to
        /// <see cref="MaxEventDeliveryRanges"/> ranges at once, one of them on the calling
        /// thread.
        /// </summary>
        private Task DeliverEventsInRangesAsync(
            int events,
            int start,
            int end,
            CancellationToken cancellationToken)
        {
            // every range gets at least the minimum number of items.
            int ranges = Math.Min(
                MaxEventDeliveryRanges,
                (end - start) / m_minItemsPerEventDeliveryRange);
            if (ranges <= 1)
            {
                return DeliverEventsAsync(events, start, end, cancellationToken);
            }
            return DeliverEventsInRangesCoreAsync(events, start, end, ranges, cancellationToken);
        }

        private async Task DeliverEventsInRangesCoreAsync(
            int events,
            int start,
            int end,
            int ranges,
            CancellationToken cancellationToken)
        {
            int size = end - start;
            var others = new Task[ranges - 1];
            for (int ii = 1; ii < ranges; ii++)
            {
                int rangeStart = start + (int)((long)size * ii / ranges);
                int rangeEnd = start + (int)((long)size * (ii + 1) / ranges);
                others[ii - 1] = Task.Run(
                    () => DeliverEventsAsync(events, rangeStart, rangeEnd, cancellationToken),
                    cancellationToken);
            }

            try
            {
                await DeliverEventsAsync(
                    events,
                    start,
                    start + (size / ranges),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // the buffers are reused by the next batch only after every range is done.
                await Task.WhenAll(others).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The most ranges the items of one event are queued in at once.
        /// </summary>
        internal static int MaxEventDeliveryRanges { get; } = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

        /// <summary>
        /// The time, in <see cref="TimeProvider.GetTimestamp"/> ticks, the consumer queues
        /// an event on its own, while no notification waits behind it, before it queues the
        /// remaining items in parallel. Tests set a negative budget to split every event as
        /// if notifications were waiting.
        /// </summary>
        internal long ParallelEventDeliveryBudget
        {
            get => m_parallelEventDeliveryBudget;
            set => m_parallelEventDeliveryBudget = value;
        }

        /// <summary>
        /// The fewest items queued in one range when an event is queued in parallel.
        /// </summary>
        internal int MinItemsPerEventDeliveryRange
        {
            get => m_minItemsPerEventDeliveryRange;
            set => m_minItemsPerEventDeliveryRange = Math.Max(1, value);
        }

        /// <summary>
        /// Delivers the first <paramref name="events"/> events of <see cref="m_eventBatch"/>,
        /// one after the other, to their items in [<paramref name="start"/>,
        /// <paramref name="end"/>).
        /// </summary>
        private async Task DeliverEventsAsync(
            int events,
            int start,
            int end,
            CancellationToken cancellationToken)
        {
            for (int ii = 0; ii < events; ii++)
            {
                List<Task>? pending = null;
                StartEventDeliveries(m_eventBatch[ii], start, end, ref pending, cancellationToken);
                if (pending != null)
                {
                    // an item gets the next event only after it got this one.
                    await Task.WhenAll(pending).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Starts the delivery of an event to each of its items in [<paramref name="start"/>,
        /// <paramref name="end"/>), one after the other.
        /// </summary>
        /// <remarks>
        /// A delivery that waits for an asynchronous permission check is added to
        /// <paramref name="pending"/>, and the next item is not held up by it. A canceled
        /// delivery is added, too, so that the caller sees the cancellation only once it
        /// joined every delivery it started.
        /// </remarks>
        private void StartEventDeliveries(
            EventDelivery delivery,
            int start,
            int end,
            ref List<Task>? pending,
            CancellationToken cancellationToken)
        {
            for (int ii = start; ii < end; ii++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // the caller joins the deliveries started so far before it sees the
                    // cancellation, so no delivery outlives the batch.
                    (pending ??= []).Add(Task.FromCanceled(cancellationToken));
                    return;
                }

                Task task = StartEventDelivery(delivery, ii, cancellationToken);
                if (task.Status != TaskStatus.RanToCompletion)
                {
                    (pending ??= []).Add(task);
                }
            }
        }

        /// <summary>
        /// Starts the delivery of an event to its item at <paramref name="index"/>.
        /// </summary>
        private Task StartEventDelivery(EventDelivery delivery, int index, CancellationToken cancellationToken)
        {
            EventItemDelivery item = m_eventDeliveries[delivery.Offset + index];
            return ProcessEventForItemAsync(
                item.Item,
                delivery.Target,
                delivery.EventTypeId,
                delivery.SourceNodeId,
                delivery.SourceGeneration,
                item.Key,
                item.Evaluation,
                cancellationToken);
        }

        /// <summary>
        /// An event of a batch, whose items start at <see cref="Offset"/> in
        /// <see cref="m_eventDeliveries"/>.
        /// </summary>
        private readonly struct EventDelivery
        {
            public EventDelivery(
                IFilterTarget target,
                NodeId eventTypeId,
                NodeId sourceNodeId,
                NodeManagerGeneration? sourceGeneration,
                int offset)
            {
                Target = target;
                EventTypeId = eventTypeId;
                SourceNodeId = sourceNodeId;
                SourceGeneration = sourceGeneration;
                Offset = offset;
            }

            public IFilterTarget Target { get; }

            public NodeId EventTypeId { get; }

            public NodeId SourceNodeId { get; }

            public NodeManagerGeneration? SourceGeneration { get; }

            public int Offset { get; }
        }

        /// <summary>
        /// An item an event is delivered to, with the group evaluation it uses.
        /// </summary>
        private readonly struct EventItemDelivery
        {
            public EventItemDelivery(
                IEventMonitoredItem item,
                MonitoredItem.SharedEventFilterKey? key,
                MonitoredItem.SharedEventEvaluation? evaluation)
            {
                Item = item;
                Key = key;
                Evaluation = evaluation;
            }

            public IEventMonitoredItem Item { get; }

            public MonitoredItem.SharedEventFilterKey? Key { get; }

            public MonitoredItem.SharedEventEvaluation? Evaluation { get; }
        }

        /// <summary>
        /// The evaluations of one event, one per group of items with an equal filter.
        /// </summary>
        /// <remarks>
        /// Only the consumer looks up the groups, before the event is delivered to any item.
        /// </remarks>
        private sealed class SharedEventEvaluations
        {
            /// <summary>
            /// Returns the evaluation of the group <paramref name="item"/> belongs to, or
            /// <c>null</c> when the item evaluates the event itself.
            /// </summary>
            public MonitoredItem.SharedEventEvaluation? Get(
                IEventMonitoredItem item,
                out MonitoredItem.SharedEventFilterKey? key)
            {
                key = (item as MonitoredItem)?.GetSharedEventFilterKey();
                if (key == null)
                {
                    return null;
                }

                m_groups ??= [];
                if (!m_groups.TryGetValue(key, out MonitoredItem.SharedEventEvaluation? evaluation))
                {
                    evaluation = new MonitoredItem.SharedEventEvaluation();
                    m_groups.Add(key, evaluation);
                }
                return evaluation;
            }

            private Dictionary<MonitoredItem.SharedEventFilterKey, MonitoredItem.SharedEventEvaluation>? m_groups;
        }

        /// <summary>
        /// Checks audit visibility and event permissions before queuing an event for one monitored item.
        /// </summary>
        private async Task ProcessEventForItemAsync(
            IEventMonitoredItem monitoredItem,
            IFilterTarget target,
            NodeId eventTypeId,
            NodeId sourceNodeId,
            NodeManagerGeneration? sourceGeneration,
            MonitoredItem.SharedEventFilterKey? key,
            MonitoredItem.SharedEventEvaluation? evaluation,
            CancellationToken cancellationToken)
        {
            if (target is AuditEventState || (target is InstanceStateSnapshot sn && sn.Handle is AuditEventState))
            {
                EndpointDescription? endpoint = MonitoredReadAuthorization.GetEndpoint(monitoredItem.Session);
                if (!m_server.Auditing || !MonitoredReadAuthorization.IsConfidential(endpoint))
                {
                    return;
                }
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                (ServiceResult validationResult, ISession? authorizationSession,
                    NotificationAuthorization? authorization) = await GetOrAddEventPermissionAsync(
                    monitoredItem, target, eventTypeId, sourceNodeId, cancellationToken).ConfigureAwait(false);
                // An Uncertain verdict is not a denial, so it must not drop the event.
                if (!ServiceResult.IsBad(validationResult))
                {
                    if (monitoredItem is MonitoredItem concreteItem)
                    {
                        concreteItem.QueueEventIfCurrent(
                            authorizationSession,
                            authorization,
                            target,
                            key,
                            evaluation,
                            admittedGeneration: sourceGeneration);
                    }
                    else
                    {
                        monitoredItem.QueueEvent(target);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (
                error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                m_logger?.EventReceiverFailed(error, monitoredItem.Id);
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
        /// on miss. A verdict is bound to the subscriber's current session,
        /// effective identity, identity generation, and node permission version.
        /// Falls back to an uncached lookup when either NodeId is null.
        /// </summary>
        private async ValueTask<(ServiceResult Result, ISession? Session, NotificationAuthorization? Authorization)>
            GetOrAddEventPermissionAsync(
            IEventMonitoredItem monitoredItem,
            IFilterTarget filterTarget,
            NodeId eventTypeId,
            NodeId sourceNodeId,
            CancellationToken cancellationToken)
        {
            bool cacheable = !eventTypeId.IsNull && !sourceNodeId.IsNull;
            var key = new EventPermissionCacheKey(monitoredItem.Id, eventTypeId, sourceNodeId);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool currentIdentity = MonitoredReadAuthorization.TryRefreshIdentity(m_server, monitoredItem.Session);
                MonitoredItemPermissionContext authorization = CapturePermissionContext(monitoredItem);
                if (!currentIdentity)
                {
                    return (new ServiceResult(StatusCodes.BadUserAccessDenied), authorization.Session, null);
                }
                if (authorization.Session?.IsIdentityStale == true)
                {
                    continue;
                }
                NotificationAuthorization? notificationAuthorization = NotificationAuthorization.Capture(
                    authorization.Session);
                ServiceResult result;
                using OperationContext context = MonitoredReadAuthorization.CreateContext(monitoredItem);
                ServiceResult restrictions = await MonitoredReadAuthorization.ValidateEventRestrictionsAsync(
                    m_server, Node, context, cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(restrictions))
                {
                    return (restrictions, authorization.Session, notificationAuthorization);
                }
                if (cacheable &&
                    m_eventPermissionCache.TryGetValue(
                        key, out (MonitoredItemPermissionContext Context, ServiceResult Result) cached) &&
                    cached.Context.Matches(authorization))
                {
                    result = cached.Result;
                }
                else
                {
                    result = await NodeManager.ValidateEventRolePermissionsAsync(
                        monitoredItem, filterTarget, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (cacheable)
                    {
                        m_eventPermissionCache[key] = (authorization, result);
                    }
                }
                if (authorization.Matches(CapturePermissionContext(monitoredItem)))
                {
                    return (result, authorization.Session, notificationAuthorization);
                }
            }
        }

        /// <summary>
        /// Captures the authorization inputs that may change while a monitored-item permission lookup is suspended.
        /// </summary>
        private MonitoredItemPermissionContext CapturePermissionContext(IMonitoredItem monitoredItem)
        {
            ISession? session = monitoredItem.Session;
            return new MonitoredItemPermissionContext(
                Volatile.Read(ref m_permissionGeneration),
                NodeState.RolePermissionsVersion,
                session,
                session?.EffectiveIdentity ?? monitoredItem.EffectiveIdentity,
                session?.CaptureIdentityRefreshSnapshot().Generation ?? 0);
        }

        /// <summary>
        /// Processes a <see cref="DataChangeSnapshot"/> from the channel.
        /// </summary>
        private async Task ProcessDataChangeSnapshotAsync(
            DataChangeSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (DataChangeMonitoredItems == null)
            {
                return;
            }

            // If RolePermissions or UserRolePermissions have changed, invalidate the permission cache.
            if ((snapshot.Changes & NodeStateChangeMasks.RolePermissions) != 0)
            {
                InvalidatePermissionCaches();
            }

            foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in snapshot.MonitoredItems)
            {
                IDataChangeMonitoredItem2 monitoredItem = kvp.Value;
                if (snapshot.SourceEmission is
                    { Generation.Gate.State: not NodeManagerGenerationState.Live } &&
                    !IsInCurrentGeneration(monitoredItem) &&
                    monitoredItem is not IMonitoredItemGenerationBinding { IsSourceUnavailable: true })
                {
                    continue;
                }
                if (monitoredItem is MonitoredItem { UsesExternalValueSource: true })
                {
                    continue;
                }
                try
                {
                    await ProcessDataChangeForItemAsync(snapshot, monitoredItem, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error) when (
                    error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                {
                    m_logger?.MonitoredNode2ConsumerEncounteredAnErrorProcessing(error);
                }
            }
        }

        /// <summary>
        /// Authorizes and delivers one subscriber's snapshot independently of other monitored items.
        /// </summary>
        private async Task ProcessDataChangeForItemAsync(
            DataChangeSnapshot snapshot,
            IDataChangeMonitoredItem2 monitoredItem,
            CancellationToken cancellationToken)
        {
            bool valueChanged = monitoredItem.AttributeId == Attributes.Value &&
                (snapshot.Changes & NodeStateChangeMasks.Value) != 0;
            bool attributeChanged = monitoredItem.AttributeId != Attributes.Value &&
                (snapshot.Changes & NodeStateChangeMasks.NonValue) != 0;
            if ((!valueChanged && !attributeChanged) ||
                !snapshot.AttributeSnapshots.TryGetValue(monitoredItem.AttributeId, out DataValue snapshotValue))
            {
                return;
            }

            (ServiceResult validationResult, ServerSystemContext contextToUse) = await GetDataChangePermissionAsync(
                snapshot, monitoredItem, MonitoredReadAuthorization.PermissionFor(monitoredItem.AttributeId),
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(validationResult))
            {
                QueueError(monitoredItem, validationResult, contextToUse, snapshot.SourceEmission);
                return;
            }

            if (valueChanged)
            {
                if (Node is BaseVariableState variable)
                {
                    if (variable.OnReadUserAccessLevel != null)
                    {
                        byte userAccessLevel = variable.UserAccessLevel;
                        ServiceResult userAccess = variable.OnReadUserAccessLevel(
                            contextToUse, variable, ref userAccessLevel);
                        if (ServiceResult.IsBad(userAccess) || (userAccessLevel & AccessLevels.CurrentRead) == 0)
                        {
                            QueueError(monitoredItem, ServiceResult.IsBad(userAccess)
                                ? userAccess : new ServiceResult(StatusCodes.BadUserAccessDenied),
                                contextToUse, snapshot.SourceEmission);
                            return;
                        }
                    }

                    if (snapshotValue.StatusCode == StatusCodes.BadUserAccessDenied &&
                        (variable.OnReadUserAccessLevel != null ||
                            (NodeManager is AsyncCustomNodeManager custom && custom.HasValueReadAuthorization(variable))))
                    {
                        if (snapshot.SubscriberValueSnapshots == null ||
                            !snapshot.SubscriberValueSnapshots.TryGetValue(
                                monitoredItem.Id,
                                out (DataValue Value, ISession? Session, IUserIdentity? Identity) captured) ||
                            !ReferenceEquals(captured.Session, monitoredItem.Session) ||
                            !ReferenceEquals(captured.Identity,
                                monitoredItem.Session?.EffectiveIdentity ?? monitoredItem.EffectiveIdentity))
                        {
                            // A later subscriber cannot inherit the reporter's value or a later reread.
                            return;
                        }
                        snapshotValue = captured.Value;
                    }
                }
                DataValue valueToQueue = ApplyRangeAndEncoding(contextToUse, monitoredItem, snapshotValue);
                QueueValueIfCurrent(
                    monitoredItem, contextToUse, valueToQueue, valueToQueue.StatusCode, snapshot.SourceEmission);
                return;
            }

            if (IsUserDependentAttribute(monitoredItem.AttributeId))
            {
                ServiceResult readResult;
                (readResult, snapshotValue) = await Node.ReadAttributeAsync(
                    contextToUse,
                    monitoredItem.AttributeId,
                    default,
                    QualifiedName.Null,
                    new DataValue(
                        default, StatusCodes.Good, DateTime.MinValue, m_timeProvider.GetUtcNow().UtcDateTime),
                    cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(readResult))
                {
                    QueueError(monitoredItem, readResult, contextToUse, snapshot.SourceEmission);
                    return;
                }
            }
            QueueValueIfCurrent(
                monitoredItem, contextToUse, snapshotValue, ServiceResult.Good, snapshot.SourceEmission);
        }

        /// <summary>
        /// Re-reads one semantic-change item using its current subscriber identity and policy.
        /// </summary>
        private async Task ProcessSemanticResampleAsync(
            SemanticResample request,
            CancellationToken cancellationToken)
        {
            IDataChangeMonitoredItem2 item = request.Item;
            if (!IsSemanticResampleCurrent(request))
            {
                return;
            }

            var snapshot = new DataChangeSnapshot
            {
                Context = request.Context,
                NodeId = request.Source.NodeId,
                Changes = NodeStateChangeMasks.Value
            };

            while (IsSemanticResampleCurrent(request))
            {
                cancellationToken.ThrowIfCancellationRequested();
                MonitoredItemPermissionContext authorization = CapturePermissionContext(item);
                (ServiceResult permission, ServerSystemContext contextToUse) =
                    await GetDataChangePermissionAsync(
                        snapshot,
                        item,
                        MonitoredReadAuthorization.PermissionFor(item.AttributeId),
                        cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(permission))
                {
                    if (authorization.Matches(CapturePermissionContext(item)) &&
                        IsSemanticResampleCurrent(request))
                    {
                        QueueError(item, permission, contextToUse);
                        return;
                    }
                    continue;
                }

                ServiceResult readResult;
                DataValue value;
                (readResult, value) = await request.Source.ReadAttributeAsync(
                    contextToUse,
                    item.AttributeId,
                    default,
                    QualifiedName.Null,
                    new DataValue(
                        default,
                        StatusCodes.Good,
                        DateTime.MinValue,
                        m_timeProvider.GetUtcNow().UtcDateTime),
                    cancellationToken).ConfigureAwait(false);

                if (!authorization.Matches(CapturePermissionContext(item)) ||
                    !IsSemanticResampleCurrent(request))
                {
                    continue;
                }

                if (ServiceResult.IsBad(readResult))
                {
                    QueueError(item, readResult, contextToUse);
                    return;
                }

                DataValue queuedValue = ApplyRangeAndEncoding(contextToUse, item, value);
                QueueValueIfCurrent(item, contextToUse, queuedValue, queuedValue.StatusCode, ignoreFilters: true);
                return;
            }
        }

        private async Task ProcessPendingSemanticResamplesAsync(CancellationToken cancellationToken)
        {
            while (m_pendingSemanticResamples.TryDequeue(out SemanticResample? request))
            {
                await ProcessSemanticResampleAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private bool IsSemanticResampleCurrent(SemanticResample request)
        {
            lock (m_rebindLock)
            {
                return !m_disposed &&
                    ReferenceEquals(request.Source, Node) &&
                    IsInCurrentGeneration(request.Item) &&
                    DataChangeMonitoredItems.TryGetValue(request.Item.Id, out IDataChangeMonitoredItem2? current) &&
                    ReferenceEquals(current, request.Item);
            }
        }

        /// <summary>
        /// Queues a bad status value so the Publish response reports the error
        /// instead of the item going silent.
        /// </summary>
        private void QueueError(
            IDataChangeMonitoredItem2 monitoredItem,
            ServiceResult error,
            ServerSystemContext? context = null,
            MasterNodeManager.SourceEmissionAdmission? sourceEmission = null)
        {
            if (!IsInCurrentGeneration(monitoredItem))
            {
                return;
            }
            DataValue value = DataValue.FromStatusCode(
                error.StatusCode, m_timeProvider.GetUtcNow().UtcDateTime);
            if (context != null)
            {
                QueueValueIfCurrent(monitoredItem, context, value, error, sourceEmission);
            }
            else
            {
                monitoredItem.QueueValue(value, error);
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the exact-source read check of the attribute involves no
        /// registered Value read authorizer, whose policy must run on every delivery, and no
        /// read callback for the node's restrictions or permissions, whose answer may change
        /// without moving any version the cached verdict is keyed on.
        /// </summary>
        private bool CanCacheReadAuthorization(uint attributeId)
        {
            NodeState node = Node;
            if (node.OnReadAccessRestrictions != null ||
                node.OnReadRolePermissions != null ||
                node.OnReadUserRolePermissions != null)
            {
                return false;
            }
            return NodeManager switch
            {
                AsyncCustomNodeManager custom => attributeId != Attributes.Value ||
                    Node is not BaseVariableState variable ||
                    !custom.HasValueReadAuthorization(variable),
                AsyncNodeManagerAdapter { SyncNodeManager: CustomNodeManager2 } => true,
                _ => false
            };
        }

        private static bool IsUserDependentAttribute(uint attributeId)
        {
            return attributeId is Attributes.UserAccessLevel or
                Attributes.UserWriteMask or
                Attributes.UserExecutable or
                Attributes.UserRolePermissions;
        }

        private async ValueTask<(ServiceResult Result, ServerSystemContext Context)> GetDataChangePermissionAsync(
            DataChangeSnapshot snapshot,
            IDataChangeMonitoredItem2 monitoredItem,
            PermissionType permission,
            CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                _ = MonitoredReadAuthorization.TryRefreshIdentity(m_server, monitoredItem.Session);
                MonitoredItemPermissionContext authorization = CapturePermissionContext(monitoredItem);
                long generation = authorization.Generation +
                    authorization.RolePermissionsVersion +
                    authorization.IdentityGeneration;
                EndpointDescription? endpoint = MonitoredReadAuthorization.GetEndpoint(monitoredItem.Session);
                MessageSecurityMode? securityMode = endpoint?.SecurityMode;
                string? transport = endpoint?.TransportProfileUri;
                AccessRestrictionType? restrictions = Node.AccessRestrictions;
                // the subscriber's own context, also when the reporter's context is not a
                // ServerSystemContext: every access check of the item uses its identity.
                ServerSystemContext cachedContext = GetSubscriberContext(
                    GetSubscriberContextTemplate(), monitoredItem);
                OperationContext operationContext = cachedContext.OperationContext!;
                ServiceResult result;
                if (authorization.Session?.IsIdentityStale == true)
                {
                    return (new ServiceResult(StatusCodes.BadUserAccessDenied), cachedContext);
                }
                ReadAuthorizationVerdict? readVerdict = null;
                if (m_permissionCache.TryGetValue(
                    monitoredItem.Id,
                    out (long Generation, ISession? Session,
                        ServerSystemContext? Context, ServiceResult Result, ReadAuthorizationVerdict? Read) cached) &&
                    cached.Generation == generation &&
                    ReferenceEquals(cached.Session, operationContext.Session) &&
                    ReferenceEquals(cached.Context, cachedContext))
                {
                    result = cached.Result;
                    readVerdict = cached.Read;
                }
                else
                {
                    result = await NodeManager.ValidateRolePermissionsAsync(
                        operationContext, snapshot.NodeId, permission, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    m_permissionCache[monitoredItem.Id] = (
                        generation, operationContext.Session, cachedContext, result, null);
                }
                if (!ServiceResult.IsBad(result))
                {
                    // The exact-source recheck runs per delivery and item. Without a registered Value
                    // read authorizer (whose policy is never cached) its verdict depends only on the
                    // inputs keyed here and in the role-permission entry, so a good verdict is reused
                    // until one of them moves. Namespace defaults behind read callbacks or access
                    // levels are not keyed by any version, so a verdict is only kept while the
                    // defaults are static.
                    long defaultsGeneration = NamespaceDefaultPermissionReader.Generation;
                    bool cacheable = CanCacheReadAuthorization(monitoredItem.AttributeId);
                    if (cacheable &&
                        readVerdict != null &&
                        readVerdict.Matches(
                            m_server,
                            Node.NodeId.NamespaceIndex,
                            defaultsGeneration,
                            endpoint,
                            securityMode,
                            transport,
                            restrictions))
                    {
                        result = readVerdict.Result;
                    }
                    else
                    {
                        ServiceResult roleResult = result;
                        NamespaceMetadataState? defaults = null;
                        cacheable = cacheable && NamespaceDefaultPermissionReader.TryGetStaticDefaults(
                            m_server, Node.NodeId.NamespaceIndex, out defaults);
                        result = await MonitoredReadAuthorization.ValidateAsync(
                            NodeManager, Node, cachedContext, monitoredItem.AttributeId, ct).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        if (cacheable &&
                            ServiceResult.IsGood(result) &&
                            defaultsGeneration == NamespaceDefaultPermissionReader.Generation &&
                            NamespaceDefaultPermissionReader.AreDefaultsStatic(defaults))
                        {
                            m_permissionCache[monitoredItem.Id] = (
                                generation,
                                operationContext.Session,
                                cachedContext,
                                roleResult,
                                new ReadAuthorizationVerdict(
                                    defaultsGeneration,
                                    defaults,
                                    endpoint,
                                    securityMode,
                                    transport,
                                    restrictions,
                                    result));
                        }
                    }
                }
                EndpointDescription? currentEndpoint = MonitoredReadAuthorization.GetEndpoint(monitoredItem.Session);
                if (authorization.Matches(CapturePermissionContext(monitoredItem)) &&
                    ReferenceEquals(endpoint, currentEndpoint) &&
                    securityMode == currentEndpoint?.SecurityMode &&
                    transport == currentEndpoint?.TransportProfileUri &&
                    restrictions == Node.AccessRestrictions)
                {
                    return (result, cachedContext);
                }
            }
        }

        /// <summary>
        /// Reads the value of an attribute and reports it to the MonitoredItem.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token passed to the asynchronous operation.</param>
        /// <param name="context">The system context.</param>
        /// <param name="node">The node state.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public async ValueTask QueueValueAsync(
            ISystemContext context,
            NodeState node,
            IDataChangeMonitoredItem2 monitoredItem,
            CancellationToken cancellationToken = default)
        {
            if (monitoredItem is MonitoredItem { UsesExternalValueSource: true })
            {
                return;
            }
            bool admitted = MasterNodeManager.TryCaptureItemEmission(
                m_server, NodeManager, Generation, out var emission);
            using var emissionLease = emission;
            if (!admitted)
            {
                return;
            }
            if (!ReferenceEquals(node, Node))
            {
                QueueError(monitoredItem, new ServiceResult(StatusCodes.BadNodeIdUnknown));
                return;
            }
            var snapshot = new DataChangeSnapshot { Context = context, NodeId = node.NodeId };
            PermissionType requested = MonitoredReadAuthorization.PermissionFor(monitoredItem.AttributeId);
            (ServiceResult authorization, ServerSystemContext subscriberContext) = await GetDataChangePermissionAsync(
                snapshot, monitoredItem, requested, cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(authorization))
            {
                QueueError(monitoredItem, authorization, subscriberContext, emission);
                return;
            }
            var value = new DataValue(
                Variant.Null,
                StatusCodes.Good,
                DateTime.MinValue,
                m_timeProvider.GetUtcNow().UtcDateTime);
            (ServiceResult error, value) = await node.ReadAttributeAsync(
                subscriberContext,
                monitoredItem.AttributeId,
                monitoredItem.IndexRange,
                monitoredItem.DataEncoding,
                value,
                cancellationToken).ConfigureAwait(false);

            if (ServiceResult.IsBad(error))
            {
                value = default;
            }

            (authorization, ServerSystemContext currentContext) = await GetDataChangePermissionAsync(
                snapshot, monitoredItem, requested, cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(authorization) ||
                !ReferenceEquals(subscriberContext.UserIdentity, currentContext.UserIdentity))
            {
                QueueError(monitoredItem, ServiceResult.IsBad(authorization)
                    ? authorization : new ServiceResult(StatusCodes.BadUserAccessDenied), subscriberContext, emission);
                return;
            }
            QueueValueIfCurrent(monitoredItem, subscriberContext, value, error, emission);
        }

        /// <summary>
        /// Preserves read authorization provenance through the final monitored-item enqueue.
        /// </summary>
        private static void QueueValueIfCurrent(
            IDataChangeMonitoredItem2 item,
            ServerSystemContext context,
            in DataValue value,
            ServiceResult? error,
            MasterNodeManager.SourceEmissionAdmission? sourceEmission = null,
            bool ignoreFilters = false)
        {
            if (item is MonitoredItem concreteItem && context.OperationContext is OperationContext operation)
            {
                concreteItem.QueueValueIfCurrent(
                    operation, value, error, ignoreFilters, sourceEmission: sourceEmission);
            }
            else if (ignoreFilters)
            {
                item.QueueValue(value, error, ignoreFilters: true);
            }
            else
            {
                item.QueueValue(value, error);
            }
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
        /// Gets the subscriber's system context for the monitored item: the context shared by all
        /// items of its session, copied once per node manager. Nothing is allocated per item or
        /// notification; a changed identity, epoch or endpoint replaces the shared context, which
        /// also retires the item's cached permission verdicts (they are keyed by the context).
        /// </summary>
        /// <param name="template">The node manager's system context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private static ServerSystemContext GetSubscriberContext(
            ServerSystemContext template,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            // CA2000: the shared context is owned by the session's entry and never disposed by callers.
#pragma warning disable CA2000
            return MonitoredReadAuthorization.GetSubscriberContext(monitoredItem).GetSystemContext(template);
#pragma warning restore CA2000
        }

        /// <summary>
        /// Invalidates the permission and context caches for all monitored items belonging
        /// to the specified session. Call this when the session's user identity changes so
        /// that role permissions are re-evaluated on the next data change notification.
        /// </summary>
        /// <param name="sessionId">The NodeId of the session whose identity has changed.</param>
        public void InvalidatePermissionCacheForSession(NodeId sessionId)
        {
            bool invalidated = false;
            foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in DataChangeMonitoredItems)
            {
                IDataChangeMonitoredItem2 monitoredItem = kvp.Value;

                if (monitoredItem?.Session?.Id.Equals(sessionId) == true)
                {
                    if (!invalidated)
                    {
                        Interlocked.Increment(ref m_permissionGeneration);
                        invalidated = true;
                    }
                    uint id = monitoredItem.Id;
                    m_permissionCache.TryRemove(id, out _);
                }
            }

            foreach (KeyValuePair<uint, IEventMonitoredItem> kvp in EventMonitoredItems)
            {
                IEventMonitoredItem monitoredItem = kvp.Value;
                if (monitoredItem?.Session?.Id.Equals(sessionId) == true)
                {
                    if (!invalidated)
                    {
                        Interlocked.Increment(ref m_permissionGeneration);
                        invalidated = true;
                    }
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
            InvalidatePermissionCaches();
        }

        private bool IsInCurrentGeneration(IMonitoredItem monitoredItem)
        {
            if (monitoredItem is not MonitoredItem { Generation: { } itemGeneration })
            {
                return true;
            }

            return itemGeneration.SlotId == Generation.SlotId &&
                itemGeneration.Number == Generation.Number;
        }

        private void InvalidatePermissionCaches()
        {
            Interlocked.Increment(ref m_permissionGeneration);
            m_permissionCache.Clear();
            m_eventPermissionCache.Clear();
        }

        /// <summary>
        /// Drops every <see cref="EventPermissionCacheKey"/> entry that
        /// belongs to the specified monitored item id.
        /// </summary>
        private void DropEventPermissionCacheEntries(uint monitoredItemId)
        {
            foreach (KeyValuePair<EventPermissionCacheKey, (MonitoredItemPermissionContext Context, ServiceResult Result)>
                entry
                in m_eventPermissionCache)
            {
                if (entry.Key.MonitoredItemId == monitoredItemId)
                {
                    m_eventPermissionCache.TryRemove(entry.Key, out _);
                }
            }
        }


        private readonly ConcurrentDictionary<
            uint, (long Generation, ISession? Session, ServerSystemContext? Context, ServiceResult Result,
                ReadAuthorizationVerdict? Read)>
            m_permissionCache = new();

        private readonly ConcurrentDictionary<
            EventPermissionCacheKey, (MonitoredItemPermissionContext Context, ServiceResult Result)>
            m_eventPermissionCache = new();

        private long m_permissionGeneration;


        /// <summary>
        /// Uses the owner or server template so reporter-specific authority cannot enter a subscriber's context.
        /// </summary>
        private ServerSystemContext GetSubscriberContextTemplate()
        {
            return (NodeManager as AsyncCustomNodeManager)?.SystemContext ??
                m_server.DefaultSystemContext ??
                LazyInitializer.EnsureInitialized(
                    ref m_subscriberContextTemplate,
                    () => new ServerSystemContext(m_server))!;
        }

        private ServerSystemContext? m_subscriberContextTemplate;
        private readonly IServerInternal m_server;
        private IAsyncNodeManager m_nodeManager = null!;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger? m_logger;
        private readonly Channel<INodeNotification> m_channel;
        private readonly CancellationTokenSource m_consumerCts;
        private readonly Task m_consumerTask;
        private readonly bool m_useMultipleConsumers;
        private EventItemDelivery[] m_eventDeliveries = [];
        private readonly EventDelivery[] m_eventBatch = new EventDelivery[k_maxEventsPerBatch];
        private long m_parallelEventDeliveryBudget;
        private int m_minItemsPerEventDeliveryRange = k_minItemsPerEventDeliveryRange;
        private readonly Lock m_rebindLock = new();
        private readonly ConcurrentQueue<SemanticResample> m_pendingSemanticResamples = new();
        private bool m_disposed;
        private bool m_draining;

        /// <summary>
        /// Completes the notification writer and asynchronously waits for all queued
        /// notifications to be delivered. No further notifications can be enqueued.
        /// </summary>
        /// <param name="cancellationToken">Cancels waiting for queued notifications to drain.</param>
        /// <returns>A task that completes when the notification consumer has stopped.</returns>
        public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
        {
            lock (m_rebindLock)
            {
                m_draining = true;
                m_channel.Writer.TryComplete();
            }
            await m_consumerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Drains queued notifications before releasing the consumer resources.
        /// Use <see cref="Dispose()"/> instead to cancel delivery immediately.
        /// </summary>
        /// <returns>A task that completes after queued notifications and resource cleanup finish.</returns>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await DrainAsync().ConfigureAwait(false);
            }
            finally
            {
                Dispose();
                GC.SuppressFinalize(this);
            }
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
            lock (m_rebindLock)
            {
                if (m_disposed)
                {
                    return;
                }
                if (disposing &&
                    Generation.TryGetGeneration(out NodeManagerGeneration? generation) &&
                    generation!.TryDeferDisposal(this))
                {
                    m_draining = true;
                    return;
                }
                m_disposed = true;
                m_draining = true;
                if (disposing)
                {
                    m_server.ConfigurationNodeManager?.DefaultPermissionsChanged -= OnDefaultPermissionsChanged;
                    if (Node.OnStateChangedAsync == OnMonitoredNodeChangedAsync)
                    {
                        Node.OnStateChangedAsync = null;
                    }
                    if (Node.OnReportEventAsync == OnReportEventAsync)
                    {
                        Node.OnReportEventAsync = null;
                    }
                    InvalidatePermissionCaches();

                    DataChangeMonitoredItems.Clear();
                    EventMonitoredItems.Clear();
                }
            }

            if (disposing)
            {
                // Synchronous disposal must not block on an asynchronous delivery callback.
                m_channel.Writer.TryComplete();

                if (m_consumerTask != null)
                {
                    m_consumerCts.Cancel();
                    _ = m_consumerTask.ContinueWith(
                        static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                        m_consumerCts,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                else
                {
                    m_consumerCts?.Dispose();
                }

                while (m_channel.Reader.TryRead(out INodeNotification? pending))
                {
                    pending.SourceEmission?.Dispose();
                }
            }
        }

        /// <summary>
        /// Binds a monitored-item verdict to the effective authorization context that produced it.
        /// </summary>
        /// <param name="Generation">The monitored node's local invalidation generation.</param>
        /// <param name="RolePermissionsVersion">The version of node and user role-permission attributes.</param>
        /// <param name="Session">The subscription's current owning session.</param>
        /// <param name="Identity">The current effective identity, not merely the principal's display name.</param>
        /// <param name="IdentityGeneration">The session's activation and live-role refresh generation.</param>
        private readonly record struct MonitoredItemPermissionContext(
            long Generation,
            long RolePermissionsVersion,
            ISession? Session,
            IUserIdentity? Identity,
            long IdentityGeneration)
        {
            /// <summary>
            /// Compares identity references because principal equality does not imply equal effective role grants.
            /// </summary>
            public bool Matches(in MonitoredItemPermissionContext other)
            {
                return Generation == other.Generation &&
                    RolePermissionsVersion == other.RolePermissionsVersion &&
                    ReferenceEquals(Session, other.Session) &&
                    ReferenceEquals(Identity, other.Identity) &&
                    IdentityGeneration == other.IdentityGeneration;
            }
        }

        /// <summary>
        /// A good exact-source read verdict and the channel, restriction and namespace-default
        /// inputs it was computed with. The namespace metadata node (or its absence) is kept so
        /// metadata linked later, a read callback attached to one of its defaults later, or a
        /// revoked read access on one (none of which moves a generation) retires the verdict.
        /// </summary>
        private sealed class ReadAuthorizationVerdict(
            long defaultsGeneration,
            NamespaceMetadataState? defaults,
            EndpointDescription? endpoint,
            MessageSecurityMode? securityMode,
            string? transport,
            AccessRestrictionType? restrictions,
            ServiceResult result)
        {
            public ServiceResult Result { get; } = result;

            public bool Matches(
                IServerInternal server,
                ushort namespaceIndex,
                long currentDefaultsGeneration,
                EndpointDescription? currentEndpoint,
                MessageSecurityMode? currentSecurityMode,
                string? currentTransport,
                AccessRestrictionType? currentRestrictions)
            {
                // The metadata node (or its absence) is resolved again on every reuse: metadata linked
                // later through a reference alone moves no generation, and the registry only notices
                // it on lookup. A cached lookup, hit or miss, does not enumerate the namespaces.
                return defaultsGeneration == currentDefaultsGeneration &&
                    ReferenceEquals(endpoint, currentEndpoint) &&
                    securityMode == currentSecurityMode &&
                    transport == currentTransport &&
                    restrictions == currentRestrictions &&
                    NamespaceDefaultPermissionReader.TryGetStaticDefaults(
                        server, namespaceIndex, out NamespaceMetadataState? current) &&
                    ReferenceEquals(current, defaults);
            }
        }

        private sealed class NotificationFence : INodeNotification
        {
            public NotificationFence(ISystemContext context)
            {
                Context = context;
            }

            public ISystemContext Context { get; }

            /// <summary>
            /// Gets the source emission, which a drain fence never holds.
            /// </summary>
            public MasterNodeManager.SourceEmissionAdmission? SourceEmission => null;

            public TaskCompletionSource<bool> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Source-generated log messages for MonitoredNode.
    /// </summary>
    internal static partial class MonitoredNodeLog
    {
        /// <summary>
        /// Logs a notification processing error in a monitored-node consumer.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 0, Level = LogLevel.Warning,
            Message = "MonitoredNode2 consumer encountered an error processing a notification.")]
        public static partial void MonitoredNode2ConsumerEncounteredAnErrorProcessing(
            this ILogger logger,
            Exception ex);

        /// <summary>
        /// Logs unexpected termination of a monitored-node consumer.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 1, Level = LogLevel.Error,
            Message = "MonitoredNode2 consumer terminated unexpectedly.")]
        public static partial void MonitoredNode2ConsumerTerminatedUnexpectedly(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs a drain timeout that forces cancellation of additional monitored-node consumers.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 2, Level = LogLevel.Warning,
            Message = "MonitoredNode2 additional consumers did not drain within 5 s; cancelling forcibly.")]
        public static partial void MonitoredNode2AdditionalConsumersDidNotDrainWithin(this ILogger logger);

        /// <summary>
        /// Logs failures of additional monitored-node consumers during shutdown.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 3, Level = LogLevel.Warning,
            Message = "MonitoredNode2 additional consumers faulted during shutdown.")]
        public static partial void MonitoredNode2AdditionalConsumersFaultedDuring(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs a drain timeout that forces cancellation of the monitored-node consumer.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 4, Level = LogLevel.Warning,
            Message = "MonitoredNode2 consumer did not drain within 5 s; cancelling forcibly.")]
        public static partial void MonitoredNode2ConsumerDidNotDrainWithin5(this ILogger logger);

        /// <summary>
        /// Logs a monitored-node consumer failure during shutdown.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        [LoggerMessage(EventId = ServerEventIds.MonitoredNode + 5, Level = LogLevel.Warning,
            Message = "MonitoredNode2 consumer faulted during shutdown.")]
        public static partial void MonitoredNode2ConsumerFaultedDuringShutdown(this ILogger logger, Exception ex);
    }
}
