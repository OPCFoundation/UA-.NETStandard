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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// A managed subscription inside a subscription manager. Can be
    /// extended to provide custom subscription implementations on
    /// top to route the received data appropriately per application.
    /// The subscription itself is based on top of the message processor
    /// implementation that routes messages to subscribers, but adds
    /// state management of the subscription on the server using the
    /// subscription and monitored item service set provided as context.
    /// </summary>
    internal abstract class Subscription : MessageProcessor, IManagedSubscription,
        IMonitoredItemManagerContext
    {
        /// <inheritdoc/>
        public byte CurrentPriority { get; private set; }

        /// <summary>
        /// Server-assigned subscription id. <c>0</c> when the
        /// subscription has not been created on the server yet.
        /// </summary>
        public uint ServerId => Id;

        /// <inheritdoc/>
        public TimeSpan CurrentPublishingInterval { get; private set; }

        /// <inheritdoc/>
        public uint CurrentKeepAliveCount { get; private set; }

        /// <inheritdoc/>
        public uint CurrentLifetimeCount { get; private set; }

        /// <inheritdoc/>
        public bool CurrentPublishingEnabled { get; private set; }

        /// <inheritdoc/>
        public uint CurrentMaxNotificationsPerPublish { get; private set; }

        /// <inheritdoc/>
        public IMonitoredItemCollection MonitoredItems => m_monitoredItems;

        /// <inheritdoc/>
        public bool Created => Id != 0;

        /// <inheritdoc/>
        public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet
            => m_context.MonitoredItemServiceSet;

        /// <inheritdoc/>
        public IMethodServiceSetClientMethods MethodServiceSet
            => m_context.MethodServiceSet;

        /// <summary>
        /// Returns true if the subscription is not receiving publishes.
        /// </summary>
        internal bool PublishingStopped
        {
            get
            {
                long lastNotificationTimestamp = LastNotificationTimestamp;
                if (lastNotificationTimestamp == 0)
                {
                    return false;
                }
                TimeSpan timeSinceLastNotification = TimeProvider
                    .GetElapsedTime(lastNotificationTimestamp);
                return timeSinceLastNotification >
                    m_keepAliveInterval + s_keepAliveTimerMargin;
            }
        }

        /// <summary>
        /// Current subscription options
        /// </summary>
        protected internal SubscriptionOptions Options { get; protected set; } = new();

        /// <summary>
        /// Create subscription
        /// </summary>
        /// <param name="context"></param>
        /// <param name="handler"></param>
        /// <param name="completion"></param>
        /// <param name="options"></param>
        /// <param name="telemetry"></param>
        /// <param name="loadState">Optional snapshot of a previously
        /// active subscription. When supplied, the V2 state machine is
        /// constructed in "loaded" mode: <see cref="MessageProcessor.Id"/>
        /// is pre-set to the saved server-side subscription id, the
        /// supplied monitored items are pre-bound to their saved
        /// server/client handles, and the initial signal that would
        /// otherwise trigger <c>CreateSubscription</c> is suppressed.
        /// The owning <see cref="SubscriptionManager.RestoreAsync"/>
        /// flow then issues TransferSubscriptions and binds runtime
        /// state via <see cref="TryCompleteTransferAsync"/>.</param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// for elapsed-time and timer calculations. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        protected Subscription(
            ISubscriptionContext context,
            ISubscriptionNotificationHandler handler,
            IMessageAckQueue completion,
            IOptionsMonitor<SubscriptionOptions> options,
            ITelemetryContext telemetry,
            SubscriptionLoadState? loadState = null,
            TimeProvider? timeProvider = null)
            : base(context.SubscriptionServiceSet, completion, telemetry, timeProvider)
        {
            m_handler = handler;
            m_context = context;
            m_monitoredItems = new MonitoredItemManager(this, telemetry);
            m_publishTimer = TimeProvider.CreateTimer(OnKeepAlive,
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (loadState != null)
            {
                // Pre-install server-assigned identifiers + items
                // before hooking change tracking, so the V2 state
                // machine sees a coherent "loaded" snapshot when it
                // first wakes.
                Id = loadState.ServerId;
                m_createdEvent.Set();
                Options = options.CurrentValue;
                foreach (MonitoredItemLoadState item in loadState.MonitoredItems)
                {
                    m_monitoredItems.AddLoaded(item);
                }
                // Hook options change tracking but do NOT signal
                // m_stateControl — the manager's RestoreAsync drives
                // transfer + completion explicitly.
                m_changeTracking = options.OnChange((o, _) => OnOptionsChanged(o));
            }
            else
            {
                OnOptionsChanged(options.CurrentValue);
                m_changeTracking = options.OnChange((o, _) => OnOptionsChanged(o));
            }
            m_stateManagement = StateManagerAsync(m_cts.Token);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{m_context}:{Id}";
        }

        /// <summary>
        /// Capture an immutable snapshot of this subscription's
        /// configuration + identifiers + the per-item state.
        /// </summary>
        public SubscriptionStateSnapshot Snapshot()
        {
            var items = new List<MonitoredItemStateSnapshot>();
            foreach (IMonitoredItem item in m_monitoredItems.Items)
            {
                if (item is MonitoredItems.MonitoredItem concrete)
                {
                    items.Add(concrete.Snapshot());
                }
            }
            uint[] available = AvailableInRetransmissionQueue == null
                ? []
                : [.. AvailableInRetransmissionQueue];
            return SubscriptionStateSnapshot.AsOptions(
                Options,
                Id,
                available.ToArrayOf(),
                items.ToArrayOf());
        }

        /// <inheritdoc/>
        public async ValueTask ConditionRefreshAsync(CancellationToken ct)
        {
            if (!Created)
            {
                throw ServiceResultException.Create(StatusCodes.BadSubscriptionIdInvalid,
                    "Subscription has not been created.");
            }
            var methodsToCall = new CallMethodRequest[]
            {
                new()
                {
                    MethodId = MethodIds.ConditionType_ConditionRefresh,
                    InputArguments = [new Variant(Id)]
                }
            };
            await m_context.MethodServiceSet.CallAsync(null, methodsToCall,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Configure triggering relationships between monitored items
        /// in this subscription. Per OPC UA Part 4 §5.13.5, the service
        /// call reports per-link status; this implementation updates
        /// the triggered items' <see cref="IMonitoredItem.TriggeringItem"/>
        /// link only for results whose status is Good. Partial failures
        /// do not corrupt local state; callers inspect the returned
        /// <see cref="SetTriggeringResponse"/> for per-link results.
        /// </summary>
        /// <remarks>
        /// Kept on the concrete <see cref="Subscription"/> class (not on
        /// <see cref="ISubscription"/>) — the API surface is too
        /// low-level for the SDK contract; per-item link/unlink methods
        /// are tracked as a follow-up.
        /// </remarks>
        public async ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            uint triggeringItemClientHandle,
            IReadOnlyList<uint> linksToAdd,
            IReadOnlyList<uint> linksToRemove,
            CancellationToken ct = default)
        {
            if (linksToAdd == null)
            {
                throw new ArgumentNullException(nameof(linksToAdd));
            }
            if (linksToRemove == null)
            {
                throw new ArgumentNullException(nameof(linksToRemove));
            }
            if (!Created)
            {
                throw ServiceResultException.Create(StatusCodes.BadSubscriptionIdInvalid,
                    "Subscription has not been created.");
            }
            if (!m_monitoredItems.TryGetMonitoredItemByClientHandle(
                triggeringItemClientHandle, out IMonitoredItem? triggeringItem) ||
                triggeringItem is null)
            {
                throw new ArgumentException(
                    $"Triggering item with client handle {triggeringItemClientHandle} " +
                    "is not part of this subscription.",
                    nameof(triggeringItemClientHandle));
            }
            if (!triggeringItem.Created)
            {
                throw ServiceResultException.Create(StatusCodes.BadMonitoredItemIdInvalid,
                    "Triggering item has not been created on the server yet.");
            }

            // Resolve client handles → server monitored item ids while
            // keeping a parallel list of the client handles for the
            // post-call local update. Skipping items not in the
            // subscription would silently lose links — instead we throw
            // so the caller can fix the call site.
            uint[] addServerIds = new uint[linksToAdd.Count];
            for (int i = 0; i < linksToAdd.Count; i++)
            {
                addServerIds[i] = ResolveServerId(linksToAdd[i], nameof(linksToAdd));
            }
            uint[] removeServerIds = new uint[linksToRemove.Count];
            for (int i = 0; i < linksToRemove.Count; i++)
            {
                removeServerIds[i] = ResolveServerId(linksToRemove[i], nameof(linksToRemove));
            }

            SetTriggeringResponse response = await m_context.MonitoredItemServiceSet
                .SetTriggeringAsync(
                    null,
                    Id,
                    triggeringItem.ServerId,
                    addServerIds.ToArrayOf(),
                    removeServerIds.ToArrayOf(),
                    ct)
                .ConfigureAwait(false);

            // Update local state only for results with a Good status —
            // partial failure must not corrupt the in-process tracking.
            // The triggered items remember who triggers them; the
            // reverse list (triggered-by-this) is resolved on demand via
            // the manager when callers ask for "what does this item
            // trigger".
            ArrayOf<StatusCode> addResults = response.AddResults;
            for (int i = 0; i < linksToAdd.Count; i++)
            {
                StatusCode status = addResults.Count > i ? addResults[i] : StatusCodes.Bad;
                if (!StatusCode.IsGood(status))
                {
                    continue;
                }
                if (m_monitoredItems.TryGetMonitoredItemByClientHandle(
                    linksToAdd[i], out IMonitoredItem? triggered) &&
                    triggered is MonitoredItems.MonitoredItem triggeredInternal)
                {
                    triggeredInternal.TriggeringItemClientHandle =
                        triggeringItemClientHandle;
                }
            }
            ArrayOf<StatusCode> removeResults = response.RemoveResults;
            for (int i = 0; i < linksToRemove.Count; i++)
            {
                StatusCode status = removeResults.Count > i ? removeResults[i] : StatusCodes.Bad;
                if (!StatusCode.IsGood(status))
                {
                    continue;
                }
                if (m_monitoredItems.TryGetMonitoredItemByClientHandle(
                    linksToRemove[i], out IMonitoredItem? triggered) &&
                    triggered is MonitoredItems.MonitoredItem triggeredInternal &&
                    triggeredInternal.TriggeringItemClientHandle ==
                        triggeringItemClientHandle)
                {
                    triggeredInternal.TriggeringItemClientHandle = 0;
                }
            }

            return response;

            uint ResolveServerId(uint clientHandle, string paramName)
            {
                if (!m_monitoredItems.TryGetMonitoredItemByClientHandle(
                    clientHandle, out IMonitoredItem? item) || item == null)
                {
                    throw new ArgumentException(
                        $"Monitored item with client handle {clientHandle} " +
                        "is not part of this subscription.",
                        paramName);
                }
                if (!item.Created)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadMonitoredItemIdInvalid,
                        $"Monitored item {clientHandle} has not been created on the server yet.");
                }
                return item.ServerId;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<TimeSpan> SetAsDurableAsync(
            TimeSpan lifetime,
            CancellationToken ct = default)
        {
            if (!Created)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSubscriptionIdInvalid,
                    "Subscription has not been created on the server.");
            }
            // SetSubscriptionDurable uses whole-hour granularity (UInt32);
            // round up so requesting 90 minutes asks the server for 2 hours.
            uint lifetimeInHours = (uint)Math.Max(
                1,
                Math.Ceiling(lifetime.TotalHours));
            ArrayOf<CallMethodRequest> methodsToCall =
            [
                new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_SetSubscriptionDurable,
                    InputArguments =
                    [
                        new Variant(Id),
                        new Variant(lifetimeInHours)
                    ]
                }
            ];
            CallResponse response = await m_context.MethodServiceSet
                .CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
            ArrayOf<CallMethodResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;
            ClientBase.ValidateResponse(results, methodsToCall);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(ClientBase.GetResult(
                    results[0].StatusCode, 0, diagnosticInfos,
                    response.ResponseHeader));
            }
            ArrayOf<Variant> outputs = results[0].OutputArguments;
            if (outputs.Count == 0 ||
                !outputs[0].TryGetValue(out uint revised))
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "Server.SetSubscriptionDurable returned no revised lifetime.");
            }
            return TimeSpan.FromHours(revised);
        }

        /// <inheritdoc/>
        public async ValueTask RecreateAsync(CancellationToken ct)
        {
            await m_stateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                LastSequenceNumberProcessed = 0;
                LastNotificationTimestamp = 0;

                Id = 0;
                CurrentPublishingInterval = TimeSpan.Zero;
                CurrentKeepAliveCount = 0;
                CurrentPublishingEnabled = false;
                CurrentPriority = 0;

                // Recreate subscription
                await CreateAsync(Options, ct).ConfigureAwait(false);

                await m_monitoredItems.ApplyChangesAsync(true, true,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                m_stateLock.Release();
            }
        }

        /// <inheritdoc/>
        public void NotifySubscriptionManagerPaused(bool paused)
        {
            m_monitoredItems.NotifySubscriptionManagerPaused(paused);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> TryCompleteTransferAsync(
            IReadOnlyList<uint> availableSequenceNumbers, CancellationToken ct)
        {
            await m_stateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                StopKeepAliveTimer();
                bool success = await m_monitoredItems.TrySynchronizeHandlesAsync(
                    ct).ConfigureAwait(false);
                if (!success)
                {
                    return false;
                }

                // save available sequence numbers
                AvailableInRetransmissionQueue = availableSequenceNumbers;

                await m_monitoredItems.ApplyChangesAsync(true, false,
                    ct).ConfigureAwait(false);
                StartKeepAliveTimer();
                return true;
            }
            finally
            {
                m_stateLock.Release();
            }
        }

        /// <summary>
        /// Reset this loaded subscription back into the create-fresh
        /// pipeline. Called by
        /// <see cref="SubscriptionManager.RestoreAsync"/> when
        /// <c>TransferSubscriptions</c> rejects the saved server-side
        /// id (typically <c>BadSubscriptionIdInvalid</c>): the V2
        /// engine then drops the loaded identifiers and creates a fresh
        /// server-side subscription via the normal
        /// <see cref="StateManagerAsync"/> path.
        /// </summary>
        /// <remarks>
        /// Triggering links captured in the load state are intentionally
        /// not replayed on recreate — the saved server-side triggering
        /// relationships are tied to the (now-stale) server item ids.
        /// Callers that want triggering preserved across a fallback
        /// recreate must re-issue
        /// <see cref="SetTriggeringAsync"/> after the items finish
        /// re-creating.
        /// </remarks>
        internal async ValueTask ResetToRecreateAsync(CancellationToken ct)
        {
            await m_stateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Id = 0;
                m_createdEvent.Reset();
                CurrentPublishingInterval = TimeSpan.Zero;
                CurrentKeepAliveCount = 0;
                CurrentLifetimeCount = 0;
                CurrentPublishingEnabled = false;
                CurrentPriority = 0;
                CurrentMaxNotificationsPerPublish = 0;
                LastSequenceNumberProcessed = 0;
                LastNotificationTimestamp = 0;
                AvailableInRetransmissionQueue = [];

                // Reset every loaded item so it queues a fresh
                // CreateMonitoredItem on the next ApplyChanges pass.
                foreach (IMonitoredItem item in m_monitoredItems.Items)
                {
                    if (item is MonitoredItems.MonitoredItem mi)
                    {
                        mi.Reset();
                    }
                }
            }
            finally
            {
                m_stateLock.Release();
            }
            // Wake the state manager so the next pass observes
            // !Created and runs CreateAsync.
            m_stateControl.Set();
        }

        /// <summary>
        /// Wait until the subscription becomes <see cref="Created"/>
        /// (the server has assigned an Id and
        /// <see cref="OnSubscriptionUpdateComplete"/> has run).
        /// Used by <see cref="SubscriptionManager.RestoreTransferAsync"/>
        /// to await the recreate-fallback before returning to the
        /// caller of <see cref="SubscriptionManager.LoadAsync"/>.
        /// </summary>
        /// <param name="ct"></param>
        internal Task WaitForCreatedAsync(CancellationToken ct)
        {
            return m_createdEvent.WaitAsync(ct);
        }

        /// <inheritdoc/>
        public override ValueTask OnPublishReceivedAsync(NotificationMessage message,
            IReadOnlyList<uint>? availableSequenceNumbers,
            IReadOnlyList<string> stringTable)
        {
            // Reset the keep alive timer
            m_publishTimer.Change(m_keepAliveInterval, m_keepAliveInterval);

            // send notification that publishing received a keep alive
            // or has to republish.
            if (PublishingStopped)
            {
                OnPublishStateChanged(PublishState.Recovered);
            }
            return base.OnPublishReceivedAsync(message, availableSequenceNumbers,
                stringTable);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsync(bool disposing)
        {
            if (disposing && !Disposed)
            {
                try
                {
                    await m_cts.CancelAsync().ConfigureAwait(false);
                    await m_stateManagement.ConfigureAwait(false);

                    await m_monitoredItems.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_publishTimer.Dispose();
                    m_changeTracking?.Dispose();
                    m_cts.Dispose();
                    m_stateLock.Dispose();
                }
            }
            await base.DisposeAsync(disposing).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Update()
        {
            m_stateControl.Set();
        }

        /// <inheritdoc/>
        public MonitoredItems.MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options, IMonitoredItemContext context)
        {
            return CreateMonitoredItem(name, options, context, Observability);
        }

        /// <summary>
        /// Create monitored item
        /// </summary>
        /// <param name="name"></param>
        /// <param name="options"></param>
        /// <param name="context"></param>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        protected abstract MonitoredItems.MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options, IMonitoredItemContext context,
            ITelemetryContext telemetry);

        /// <summary>
        /// Called when the options changed
        /// </summary>
        /// <param name="options"></param>
        protected virtual void OnOptionsChanged(SubscriptionOptions options)
        {
            SubscriptionOptions currentOptions = Options;
            if (currentOptions == options)
            {
                return;
            }
            Options = options;
            m_stateControl.Set();
        }

        /// <inheritdoc/>
        protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
            DateTime publishTime, PublishState publishStateMask)
        {
            return m_handler.OnKeepAliveNotificationAsync(this, sequenceNumber,
                publishTime, publishStateMask);
        }

        /// <inheritdoc/>
        protected override async ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
            DateTime publishTime, DataChangeNotification notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            try
            {
                await m_handler.OnDataChangeNotificationAsync(this, sequenceNumber,
                    publishTime, m_monitoredItems.CreateNotification(notification),
                    publishStateMask, stringTable).ConfigureAwait(false);
            }
            finally
            {
                if (PoolNotifications)
                {
                    ReuseDataChangeNotification(notification);
                }
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
            DateTime publishTime, EventNotificationList notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            try
            {
                await m_handler.OnEventDataNotificationAsync(this, sequenceNumber,
                    publishTime, m_monitoredItems.CreateNotification(notification),
                    publishStateMask, stringTable).ConfigureAwait(false);
            }
            finally
            {
                if (PoolNotifications)
                {
                    ReuseEventNotificationList(notification);
                }
            }
        }

        /// <summary>
        /// Walk a dispatched <see cref="DataChangeNotification"/> after the
        /// handler returns and release pooled payload instances back to
        /// their activators. Items that do not implement
        /// <see cref="IPooledEncodeable"/> are skipped silently — this
        /// allows the walk to be safe before source-gen learns to emit
        /// the interface for tagged types, and serves as the universal
        /// dispatch path for any future pooled payload variant.
        /// </summary>
        private static void ReuseDataChangeNotification(DataChangeNotification notification)
        {
            ReadOnlySpan<MonitoredItemNotification> items =
                notification.MonitoredItems.Span;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is IPooledEncodeable item)
                {
                    item.Reuse();
                }
            }
            if (notification is IPooledEncodeable container)
            {
                container.Reuse();
            }
        }

        /// <summary>
        /// Event-side companion to <see cref="ReuseDataChangeNotification"/>.
        /// </summary>
        private static void ReuseEventNotificationList(EventNotificationList notification)
        {
            ReadOnlySpan<EventFieldList> events = notification.Events.Span;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i] is IPooledEncodeable evt)
                {
                    evt.Reuse();
                }
            }
            if (notification is IPooledEncodeable container)
            {
                container.Reuse();
            }
        }

        /// <inheritdoc/>
        protected override ValueTask OnStatusChangeNotificationAsync(uint sequenceNumber,
            DateTime publishTime, StatusChangeNotification notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            // The base MessageProcessor does not automatically raise
            // OnPublishStateChanged for status notifications, so do it
            // here. This is what surfaces the Transferred / Timeout
            // flags to user-visible publish-state handlers.
            OnPublishStateChanged(publishStateMask);

            // Recovery on unsolicited Good_SubscriptionTransferred.
            // Per OPC UA Part 4 §5.14.7 this notification is sent to
            // the *old* session when its subscription was transferred
            // away — receiving it on a freshly created subscription
            // here is a server quirk (e.g. Kepware leaking
            // pre-restart state). Opt-in policy lets the caller ask
            // for an in-place recreate instead of leaving the
            // subscription dark.
            if (notification.Status == StatusCodes.GoodSubscriptionTransferred
                && Options.RecoveryPolicy
                    .HasFlag(SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer)
                && Created
                && !Disposed)
            {
                if (Interlocked.CompareExchange(
                    ref m_recreateAfterTransferInProgress, 1, 0) == 0)
                {
                    _ = Task.Run(RecoverAfterUnsolicitedTransferAsync);
                }
            }
            return default;
        }

        /// <summary>
        /// Run an in-place recreate on the same session after an
        /// unsolicited <c>Good_SubscriptionTransferred</c>. Drops
        /// queued acknowledgements for the dead subscription id
        /// before invoking <see cref="ResetToRecreateAsync"/> so the
        /// state-manager loop sees a coherent reset and the ack
        /// queue cannot leak <c>BadSubscriptionIdInvalid</c>s on
        /// servers that re-use subscription identifiers. Idempotent:
        /// concurrent dispatches collapse through
        /// <see cref="m_recreateAfterTransferInProgress"/>.
        /// </summary>
        private async Task RecoverAfterUnsolicitedTransferAsync()
        {
            uint deadId = Id;
            try
            {
                Logger.LogWarning(
                    "{Subscription}: unsolicited Good_SubscriptionTransferred received — " +
                    "auto-recreating on the same session " +
                    "(RecoveryPolicy=RecreateOnUnsolicitedTransfer).",
                    this);

                if (deadId != 0)
                {
                    int dropped = AckQueue.DropPendingForSubscription(deadId);
                    if (dropped > 0)
                    {
                        Logger.LogInformation(
                            "{Subscription}: dropped {Count} stale acknowledgement(s) " +
                            "before recovery recreate.",
                            this,
                            dropped);
                    }
                }

                if (Disposed)
                {
                    return;
                }

                await ResetToRecreateAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                Logger.LogInformation(
                    "{Subscription}: recreate signalled after unsolicited " +
                    "Good_SubscriptionTransferred (old SubscriptionId={OldId}).",
                    this,
                    deadId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "{Subscription}: recovery after unsolicited " +
                    "Good_SubscriptionTransferred failed (old SubscriptionId={OldId}); " +
                    "the subscription stays dark until the next reconnect or " +
                    "manual recreate.",
                    this,
                    deadId);
            }
            finally
            {
                Interlocked.Exchange(ref m_recreateAfterTransferInProgress, 0);
            }
        }

        private int m_recreateAfterTransferInProgress;

        /// <summary>
        /// Called when the subscription state changed
        /// </summary>
        /// <param name="state"></param>
        protected virtual void OnSubscriptionStateChanged(SubscriptionState state)
        {
            Logger.LogInformation("{Subscription}: {State}.", this, state);
            FireStateChangedToHandler(state, default);
        }

        /// <inheritdoc/>
        protected override void OnPublishStateChanged(PublishState stateMask)
        {
            base.OnPublishStateChanged(stateMask);
            FireStateChangedToHandler(default, stateMask);
        }

        private void FireStateChangedToHandler(SubscriptionState state,
            PublishState publishStateMask)
        {
            try
            {
                // Fire-and-forget: handler is allowed to block, but we
                // intentionally don't await here because OnSubscriptionStateChanged
                // is invoked from inside StateManagerAsync (under m_stateLock)
                // and OnPublishStateChanged is invoked from the publish dispatch
                // path. Either await would risk a deadlock if the handler
                // re-enters the engine. Handlers that need backpressure
                // should buffer in OnSubscriptionStateChangedAsync and
                // process on a worker.
                _ = m_handler.OnSubscriptionStateChangedAsync(this, state,
                    publishStateMask).AsTask();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "{Subscription}: OnSubscriptionStateChangedAsync handler threw.",
                    this);
            }
        }

        /// <summary>
        /// Controls the state changes of the subscriptions and the contained monitored
        /// items.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task StateManagerAsync(CancellationToken ct)
        {
            OnSubscriptionStateChanged(SubscriptionState.Opened);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await m_stateControl.WaitAsync(ct).ConfigureAwait(false);
                    await m_stateLock.WaitAsync(ct).ConfigureAwait(false);
                    SubscriptionOptions options = Options;
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            if (options.Disabled)
                            {
                                await DeleteAsync(ct).ConfigureAwait(false);
                                break; // Wait for changes while disabled
                            }

                            if (!Created)
                            {
                                await CreateAsync(options, ct).ConfigureAwait(false);
                            }
                            else
                            {
                                await ModifyAsync(options, ct).ConfigureAwait(false);
                            }

                            bool modified = await m_monitoredItems.ApplyChangesAsync(
                                false, false, ct).ConfigureAwait(false);
                            if (modified)
                            {
                                OnSubscriptionStateChanged(SubscriptionState.Modified);
                            }
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to apply subscription changes.");
                    }
                    finally
                    {
                        m_stateLock.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }

            // Delete subscription on server on dispose
            await DeleteAsync(default).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a subscription on the server, but keeps the subscription in the session.
        /// </summary>
        /// <param name="ct"></param>
        ///
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask DeleteAsync(CancellationToken ct)
        {
            // nothing to do if not created.
            if (!Created)
            {
                return;
            }
            try
            {
                // delete the subscription.
                ArrayOf<uint> subscriptionIds = new uint[] { Id };
                DeleteSubscriptionsResponse response = await m_context.SubscriptionServiceSet.DeleteSubscriptionsAsync(null,
                    subscriptionIds, ct).ConfigureAwait(false);
                // validate response.
                ClientBase.ValidateResponse(response.Results, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(response.Results[0]))
                {
                    throw new ServiceResultException(ClientBase.GetResult(
                        response.Results[0], 0, response.DiagnosticInfos,
                        response.ResponseHeader));
                }
            }
            // suppress exception if silent flag is set.
            catch (Exception e)
            {
                Logger.LogInformation(e, "Deleting subscription on server failed.");
            }
            OnSubscriptionDeleteCompleted();
        }

        /// <summary>
        /// Creates a subscription on the server and adds all monitored items.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="ct"></param>
        internal async ValueTask CreateAsync(SubscriptionOptions options, CancellationToken ct)
        {
            // create the subscription.
            AdjustCounts(options, out uint revisedMaxKeepAliveCount, out uint revisedLifetimeCount);

            CreateSubscriptionResponse response = await m_context.SubscriptionServiceSet.CreateSubscriptionAsync(null,
                options.PublishingInterval.TotalMilliseconds, revisedLifetimeCount,
                revisedMaxKeepAliveCount, options.MaxNotificationsPerPublish,
                options.PublishingEnabled, options.Priority, ct).ConfigureAwait(false);

            OnSubscriptionUpdateComplete(true, response.SubscriptionId,
                TimeSpan.FromMilliseconds(response.RevisedPublishingInterval),
                response.RevisedMaxKeepAliveCount, response.RevisedLifetimeCount,
                options.Priority, options.MaxNotificationsPerPublish,
                options.PublishingEnabled);
        }

        /// <summary>
        /// Modifies a subscription on the server.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="ct"></param>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask ModifyAsync(SubscriptionOptions options, CancellationToken ct)
        {
            // modify the subscription.
            AdjustCounts(options, out uint revisedMaxKeepAliveCount, out uint revisedLifetimeCount);

            if (revisedMaxKeepAliveCount != CurrentKeepAliveCount ||
                revisedLifetimeCount != CurrentLifetimeCount ||
                options.Priority != CurrentPriority ||
                options.MaxNotificationsPerPublish != CurrentMaxNotificationsPerPublish ||
                options.PublishingInterval != CurrentPublishingInterval)
            {
                ModifySubscriptionResponse response = await m_context.SubscriptionServiceSet.ModifySubscriptionAsync(null, Id,
                    options.PublishingInterval.TotalMilliseconds, revisedLifetimeCount,
                    revisedMaxKeepAliveCount, options.MaxNotificationsPerPublish, options.Priority,
                    ct).ConfigureAwait(false);

                if (options.PublishingEnabled != CurrentPublishingEnabled)
                {
                    await SetPublishingModeAsync(options, ct).ConfigureAwait(false);
                }

                OnSubscriptionUpdateComplete(false, 0,
                    TimeSpan.FromMilliseconds(response.RevisedPublishingInterval),
                    response.RevisedMaxKeepAliveCount, response.RevisedLifetimeCount,
                    options.Priority, options.MaxNotificationsPerPublish,
                    options.PublishingEnabled);
            }
            else if (options.PublishingEnabled != CurrentPublishingEnabled)
            {
                await SetPublishingModeAsync(options, ct).ConfigureAwait(false);

                // update current state.
                CurrentPublishingEnabled = options.PublishingEnabled;
                OnSubscriptionStateChanged(SubscriptionState.Modified);
            }

            async Task SetPublishingModeAsync(SubscriptionOptions options, CancellationToken ct)
            {
                // modify the subscription.
                ArrayOf<uint> subscriptionIds = new uint[] { Id };
                SetPublishingModeResponse response = await m_context.SubscriptionServiceSet.SetPublishingModeAsync(
                    null, options.PublishingEnabled, subscriptionIds, ct).ConfigureAwait(false);

                // validate response.
                ClientBase.ValidateResponse(response.Results, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(response.Results[0]))
                {
                    throw new ServiceResultException(
                        ClientBase.GetResult(response.Results[0], 0,
                        response.DiagnosticInfos, response.ResponseHeader));
                }

                Logger.LogInformation(
                    "{Subscription}: Modified - Publishing is now {New}.",
                    this, options.PublishingEnabled ? "Enabled" : "Disabled");
            }
        }

        /// <summary>
        /// Update the subscription with the given revised settings.
        /// </summary>
        /// <param name="created"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="revisedPublishingInterval"></param>
        /// <param name="revisedKeepAliveCount"></param>
        /// <param name="revisedLifetimeCount"></param>
        /// <param name="priority"></param>
        /// <param name="maxNotificationsPerPublish"></param>
        /// <param name="publishingEnabled"></param>
        internal void OnSubscriptionUpdateComplete(bool created,
            uint subscriptionId, TimeSpan revisedPublishingInterval,
            uint revisedKeepAliveCount, uint revisedLifetimeCount,
            byte priority, uint maxNotificationsPerPublish,
            bool publishingEnabled)
        {
            if (CurrentPublishingEnabled != publishingEnabled)
            {
                Logger.LogInformation(
                    "{Subscription}: Created - Publishing is {New}.",
                    this, publishingEnabled ? "Enabled" : "Disabled");
                CurrentPublishingEnabled = publishingEnabled;
            }

            if (CurrentKeepAliveCount != revisedKeepAliveCount)
            {
                Logger.LogInformation(
                    "{Subscription}: Changed KeepAliveCount to {New}.",
                    this, revisedKeepAliveCount);

                CurrentKeepAliveCount = revisedKeepAliveCount;
            }

            if (CurrentPublishingInterval != revisedPublishingInterval)
            {
                Logger.LogInformation(
                    "{Subscription}: Changed PublishingInterval to {New}.",
                    this, revisedPublishingInterval);
                CurrentPublishingInterval = revisedPublishingInterval;
            }

            if (CurrentMaxNotificationsPerPublish != maxNotificationsPerPublish)
            {
                Logger.LogInformation(
                    "{Subscription}: Change MaxNotificationsPerPublish to {New}.",
                    this, maxNotificationsPerPublish);
                CurrentMaxNotificationsPerPublish = maxNotificationsPerPublish;
            }

            if (CurrentLifetimeCount != revisedLifetimeCount)
            {
                Logger.LogInformation(
                    "{Subscription}: Changed LifetimeCount to {New}.",
                    this, revisedLifetimeCount);
                CurrentLifetimeCount = revisedLifetimeCount;
            }

            if (CurrentPriority != priority)
            {
                Logger.LogInformation(
                    "{Subscription}: Changed Priority to {New}.",
                    this, priority);
                CurrentPriority = priority;
            }

            if (created)
            {
                Id = subscriptionId;
                StartKeepAliveTimer();
                NotifyManagerOfCreation();
                m_createdEvent.Set();
            }

            // Notify all monitored items of the changes
            SubscriptionState state = created ?
                SubscriptionState.Created : SubscriptionState.Modified;
            m_monitoredItems.OnSubscriptionStateChange(state, CurrentPublishingInterval);
            OnSubscriptionStateChanged(state);
        }

        /// <summary>
        /// Delete the subscription.
        /// Ignore errors, always reset all parameter.
        /// </summary>
        internal void OnSubscriptionDeleteCompleted()
        {
            LastSequenceNumberProcessed = 0;
            LastNotificationTimestamp = 0;

            Id = 0;
            m_createdEvent.Reset();
            CurrentPublishingInterval = TimeSpan.Zero;
            CurrentKeepAliveCount = 0;
            CurrentPublishingEnabled = false;
            CurrentPriority = 0;

            m_deletedItems.Clear();

            // Notify all monitored items of the changes
            m_monitoredItems.OnSubscriptionStateChange(SubscriptionState.Deleted,
                CurrentPublishingInterval);
            OnSubscriptionStateChanged(SubscriptionState.Deleted);
        }

        /// <summary>
        /// Stop the keep alive timer for the subscription.
        /// </summary>
        private void StopKeepAliveTimer()
        {
            m_publishTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Starts a timer to ensure publish requests are sent frequently
        /// enough to detect network interruptions.
        /// </summary>
        private void StartKeepAliveTimer()
        {
            SubscriptionOptions options = Options;
            LastNotificationTimestamp = TimeProvider.GetTimestamp();
            m_keepAliveInterval = CurrentPublishingInterval.Multiply(CurrentKeepAliveCount + 1);
            if (m_keepAliveInterval < s_minKeepAliveTimerInterval)
            {
                m_keepAliveInterval = options.PublishingInterval.Multiply(options.KeepAliveCount + 1);
            }
            if (m_keepAliveInterval > Timeout.InfiniteTimeSpan)
            {
                m_keepAliveInterval = Timeout.InfiniteTimeSpan;
            }
            if (m_keepAliveInterval < s_minKeepAliveTimerInterval)
            {
                m_keepAliveInterval = s_minKeepAliveTimerInterval;
            }
            m_publishTimer.Change(m_keepAliveInterval, m_keepAliveInterval);
        }

        /// <summary>
        /// Checks if a notification has arrived in time.
        /// </summary>
        /// <param name="state"></param>
        private void OnKeepAlive(object? state)
        {
            // check if a publish has arrived.
            if (PublishingStopped)
            {
                Interlocked.Increment(ref m_publishLateCount);
                OnPublishStateChanged(PublishState.Stopped);
            }
        }

        /// <summary>
        /// Ensures sensible values for the counts.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="keepAliveCount"></param>
        /// <param name="lifetimeCount"></param>
        private void AdjustCounts(SubscriptionOptions options, out uint keepAliveCount,
            out uint lifetimeCount)
        {
            const uint kDefaultKeepAlive = 10;
            const uint kDefaultLifeTime = 1000;

            keepAliveCount = options.KeepAliveCount;
            lifetimeCount = options.LifetimeCount;
            // keep alive count must be at least 1, 10 is a good default.
            if (keepAliveCount == 0)
            {
                Logger.LogInformation("{Subscription}: Adjusted KeepAliveCount " +
                    "from {Old} to {New}.", this, keepAliveCount, kDefaultKeepAlive);
                keepAliveCount = kDefaultKeepAlive;
            }

            // ensure the lifetime is sensible given the sampling interval.
            if (options.PublishingInterval > TimeSpan.Zero)
            {
                if (options.MinLifetimeInterval > TimeSpan.Zero &&
                    options.MinLifetimeInterval < m_context.SessionTimeout)
                {
                    Logger.LogWarning(
                        "{Subscription}: A smaller minimum LifetimeInterval " +
                        "{Counter}ms than session timeout {Timeout}ms configured.",
                        this, options.MinLifetimeInterval, m_context.SessionTimeout);
                }

                uint minLifetimeInterval = (uint)options.MinLifetimeInterval.TotalMilliseconds;
                uint publishingInterval = (uint)options.PublishingInterval.TotalMilliseconds;
                uint minLifetimeCount = minLifetimeInterval / publishingInterval;
                if (lifetimeCount < minLifetimeCount)
                {
                    lifetimeCount = minLifetimeCount;

                    if (minLifetimeInterval % publishingInterval != 0)
                    {
                        lifetimeCount++;
                    }
                    Logger.LogInformation(
                        "{Subscription}: Adjusted LifetimeCount to value={New}.",
                        this, lifetimeCount);
                }

                if (options.PublishingInterval.Multiply(lifetimeCount) < m_context.SessionTimeout)
                {
                    Logger.LogWarning(
                        "{Subscription}: Lifetime {LifeTime}ms configured is less " +
                        "than session timeout {Timeout}ms.", this,
                        options.PublishingInterval.Multiply(lifetimeCount), m_context.SessionTimeout);
                }
            }
            else if (lifetimeCount == 0)
            {
                // don't know what the sampling interval will be - use something large
                // enough to ensure the user does not experience unexpected drop outs.
                Logger.LogInformation(
                    "{Subscription}: Adjusted LifetimeCount from {Old} to {New}. ",
                    this, lifetimeCount, kDefaultLifeTime);
                lifetimeCount = kDefaultLifeTime;
            }

            // validate spec: lifetimecount shall be at least 3*keepAliveCount
            uint minLifeTimeCount = 3 * keepAliveCount;
            if (lifetimeCount < minLifeTimeCount)
            {
                Logger.LogInformation(
                    "{Subscription}: Adjusted LifetimeCount from {Old} to {New}.",
                    this, lifetimeCount, minLifeTimeCount);
                lifetimeCount = minLifeTimeCount;
            }
        }

        private static readonly TimeSpan s_minKeepAliveTimerInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan s_keepAliveTimerMargin = TimeSpan.FromSeconds(1);
        private TimeSpan m_keepAliveInterval;
        private int m_publishLateCount;
        private readonly AsyncAutoResetEvent m_stateControl = new();
        private readonly AsyncManualResetEvent m_createdEvent = new();
        private readonly CancellationTokenSource m_cts = new();
        private readonly Task m_stateManagement;
        private readonly SemaphoreSlim m_stateLock = new(1, 1);
        private readonly List<uint> m_deletedItems = [];
        private readonly ITimer m_publishTimer;
        private readonly IDisposable? m_changeTracking;
        private readonly ISubscriptionNotificationHandler m_handler;
        private readonly ISubscriptionContext m_context;
        private readonly MonitoredItemManager m_monitoredItems;
    }
}
