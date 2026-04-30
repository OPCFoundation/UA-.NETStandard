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
                long lastNotificationTimestamp = _lastNotificationTimestamp;
                if (lastNotificationTimestamp == 0)
                {
                    return false;
                }
                TimeSpan timeSinceLastNotification = TimeProvider.System
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
        protected Subscription(ISubscriptionContext context, ISubscriptionNotificationHandler handler,
            IMessageAckQueue completion, IOptionsMonitor<SubscriptionOptions> options,
            ITelemetryContext telemetry)
            : base(context.SubscriptionServiceSet, completion, telemetry)
        {
            m_handler = handler;
            m_context = context;
            m_monitoredItems = new MonitoredItemManager(this, telemetry);
            m_publishTimer = TimeProvider.System.CreateTimer(OnKeepAlive,
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            OnOptionsChanged(options.CurrentValue);
            m_changeTracking = options.OnChange((o, _) => OnOptionsChanged(o));
            m_stateManagement = StateManagerAsync(m_cts.Token);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{m_context}:{Id}";
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

        /// <inheritdoc/>
        public async ValueTask RecreateAsync(CancellationToken ct)
        {
            await m_stateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _lastSequenceNumberProcessed = 0;
                _lastNotificationTimestamp = 0;

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
                _availableInRetransmissionQueue = availableSequenceNumbers;

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
            if (disposing)
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
        protected override ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
            DateTime publishTime, DataChangeNotification notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            return m_handler.OnDataChangeNotificationAsync(this, sequenceNumber,
                publishTime, m_monitoredItems.CreateNotification(notification),
                publishStateMask, stringTable);
        }

        /// <inheritdoc/>
        protected override ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
            DateTime publishTime, EventNotificationList notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            return m_handler.OnEventDataNotificationAsync(this, sequenceNumber,
                publishTime, m_monitoredItems.CreateNotification(notification),
                publishStateMask, stringTable);
        }

        /// <inheritdoc/>
        protected override ValueTask OnStatusChangeNotificationAsync(uint sequenceNumber,
            DateTime publishTime, StatusChangeNotification notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            // TODO - trigger recovery of subscription, etc.
            return default;
        }

        /// <summary>
        /// Called when the subscription state changed
        /// </summary>
        /// <param name="state"></param>
        protected virtual void OnSubscriptionStateChanged(SubscriptionState state)
        {
            _logger.LogInformation("{Subscription}: {State}.", this, state);
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
                        _logger.LogError(ex, "Failed to apply subscription changes.");
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
                _logger.LogInformation(e, "Deleting subscription on server failed.");
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

                _logger.LogInformation(
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
                _logger.LogInformation(
                    "{Subscription}: Created - Publishing is {New}.",
                    this, publishingEnabled ? "Enabled" : "Disabled");
                CurrentPublishingEnabled = publishingEnabled;
            }

            if (CurrentKeepAliveCount != revisedKeepAliveCount)
            {
                _logger.LogInformation(
                    "{Subscription}: Changed KeepAliveCount to {New}.",
                    this, revisedKeepAliveCount);

                CurrentKeepAliveCount = revisedKeepAliveCount;
            }

            if (CurrentPublishingInterval != revisedPublishingInterval)
            {
                _logger.LogInformation(
                    "{Subscription}: Changed PublishingInterval to {New}.",
                    this, revisedPublishingInterval);
                CurrentPublishingInterval = revisedPublishingInterval;
            }

            if (CurrentMaxNotificationsPerPublish != maxNotificationsPerPublish)
            {
                _logger.LogInformation(
                    "{Subscription}: Change MaxNotificationsPerPublish to {New}.",
                    this, maxNotificationsPerPublish);
                CurrentMaxNotificationsPerPublish = maxNotificationsPerPublish;
            }

            if (CurrentLifetimeCount != revisedLifetimeCount)
            {
                _logger.LogInformation(
                    "{Subscription}: Changed LifetimeCount to {New}.",
                    this, revisedLifetimeCount);
                CurrentLifetimeCount = revisedLifetimeCount;
            }

            if (CurrentPriority != priority)
            {
                _logger.LogInformation(
                    "{Subscription}: Changed Priority to {New}.",
                    this, priority);
                CurrentPriority = priority;
            }

            if (created)
            {
                Id = subscriptionId;
                StartKeepAliveTimer();
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
            _lastSequenceNumberProcessed = 0;
            _lastNotificationTimestamp = 0;

            Id = 0;
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
            _lastNotificationTimestamp = TimeProvider.System.GetTimestamp();
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
                _logger.LogInformation("{Subscription}: Adjusted KeepAliveCount " +
                    "from {Old} to {New}.", this, keepAliveCount, kDefaultKeepAlive);
                keepAliveCount = kDefaultKeepAlive;
            }

            // ensure the lifetime is sensible given the sampling interval.
            if (options.PublishingInterval > TimeSpan.Zero)
            {
                if (options.MinLifetimeInterval > TimeSpan.Zero &&
                    options.MinLifetimeInterval < m_context.SessionTimeout)
                {
                    _logger.LogWarning(
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
                    _logger.LogInformation(
                        "{Subscription}: Adjusted LifetimeCount to value={New}.",
                        this, lifetimeCount);
                }

                if (options.PublishingInterval.Multiply(lifetimeCount) < m_context.SessionTimeout)
                {
                    _logger.LogWarning(
                        "{Subscription}: Lifetime {LifeTime}ms configured is less " +
                        "than session timeout {Timeout}ms.", this,
                        options.PublishingInterval.Multiply(lifetimeCount), m_context.SessionTimeout);
                }
            }
            else if (lifetimeCount == 0)
            {
                // don't know what the sampling interval will be - use something large
                // enough to ensure the user does not experience unexpected drop outs.
                _logger.LogInformation(
                    "{Subscription}: Adjusted LifetimeCount from {Old} to {New}. ",
                    this, lifetimeCount, kDefaultLifeTime);
                lifetimeCount = kDefaultLifeTime;
            }

            // validate spec: lifetimecount shall be at least 3*keepAliveCount
            uint minLifeTimeCount = 3 * keepAliveCount;
            if (lifetimeCount < minLifeTimeCount)
            {
                _logger.LogInformation(
                    "{Subscription}: Adjusted LifetimeCount from {Old} to {New}.",
                    this, lifetimeCount, minLifeTimeCount);
                lifetimeCount = minLifeTimeCount;
            }
        }

        private static readonly TimeSpan s_minKeepAliveTimerInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan s_keepAliveTimerMargin = TimeSpan.FromSeconds(1);
        private TimeSpan m_keepAliveInterval;
        private int m_publishLateCount;
        private readonly Nito.AsyncEx.AsyncAutoResetEvent m_stateControl = new();
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
