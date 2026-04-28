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

namespace Opc.Ua.Client.Subscriptions
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Opc.Ua.Client.Services;
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

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
        public IMonitoredItemCollection MonitoredItems => _monitoredItems;

        /// <inheritdoc/>
        public bool Created => Id != 0;

        /// <inheritdoc/>
        public IMonitoredItemServiceSet MonitoredItemServiceSet
            => _context.MonitoredItemServiceSet;

        /// <inheritdoc/>
        public IMethodServiceSet MethodServiceSet
            => _context.MethodServiceSet;

        /// <summary>
        /// Returns true if the subscription is not receiving publishes.
        /// </summary>
        internal bool PublishingStopped
        {
            get
            {
                var lastNotificationTimestamp = _lastNotificationTimestamp;
                if (lastNotificationTimestamp == 0)
                {
                    return false;
                }
                var timeSinceLastNotification = TimeProvider.System
                    .GetElapsedTime(lastNotificationTimestamp);
                return timeSinceLastNotification >
                    _keepAliveInterval + kKeepAliveTimerMargin;
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
            ITelemetryContext telemetry) : base(context.SubscriptionServiceSet, completion, telemetry)
        {
            _handler = handler;
            _context = context;
            _monitoredItems = new MonitoredItemManager(this, telemetry);
            _publishTimer = TimeProvider.System.CreateTimer(OnKeepAlive,
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            OnOptionsChanged(options.CurrentValue);
            _changeTracking = options.OnChange((o, _) => OnOptionsChanged(o));
            _stateManagement = StateManagerAsync(_cts.Token);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{_context}:{Id}";
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
                new CallMethodRequest()
                {
                    MethodId = MethodIds.ConditionType_ConditionRefresh,
                    InputArguments = [new Variant(Id)]
                }
            };
            await _context.MethodServiceSet.CallAsync(null, methodsToCall,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RecreateAsync(CancellationToken ct)
        {
            await _stateLock.WaitAsync(ct).ConfigureAwait(false);
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

                await _monitoredItems.ApplyChangesAsync(true, true,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <inheritdoc/>
        public void NotifySubscriptionManagerPaused(bool paused)
        {
            _monitoredItems.NotifySubscriptionManagerPaused(paused);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> TryCompleteTransferAsync(
            IReadOnlyList<uint> availableSequenceNumbers, CancellationToken ct)
        {
            await _stateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                StopKeepAliveTimer();
                var success = await _monitoredItems.TrySynchronizeHandlesAsync(
                    ct).ConfigureAwait(false);
                if (!success)
                {
                    return false;
                }

                // save available sequence numbers
                _availableInRetransmissionQueue = availableSequenceNumbers;

                await _monitoredItems.ApplyChangesAsync(true, false,
                    ct).ConfigureAwait(false);
                StartKeepAliveTimer();
                return true;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <inheritdoc/>
        public override ValueTask OnPublishReceivedAsync(NotificationMessage message,
            IReadOnlyList<uint>? availableSequenceNumbers,
            IReadOnlyList<string> stringTable)
        {
            // Reset the keep alive timer
            _publishTimer.Change(_keepAliveInterval, _keepAliveInterval);

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
                    await _cts.CancelAsync().ConfigureAwait(false);
                    await _stateManagement.ConfigureAwait(false);

                    await _monitoredItems.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _publishTimer.Dispose();
                    _changeTracking?.Dispose();
                    _cts.Dispose();
                    _stateLock.Dispose();
                }
            }
            await base.DisposeAsync(disposing).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Update()
        {
            _stateControl.Set();
        }

        /// <inheritdoc/>
        public MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItemOptions> options, IMonitoredItemContext context)
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
        protected abstract MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItemOptions> options, IMonitoredItemContext context,
            ITelemetryContext telemetry);

        /// <summary>
        /// Called when the options changed
        /// </summary>
        /// <param name="options"></param>
        protected virtual void OnOptionsChanged(SubscriptionOptions options)
        {
            var currentOptions = Options;
            if (currentOptions == options)
            {
                return;
            }
            Options = options;
            _stateControl.Set();
        }

        /// <inheritdoc/>
        protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
            DateTime publishTime, PublishState publishStateMask)
        {
            return _handler.OnKeepAliveNotificationAsync(this, sequenceNumber,
                publishTime, publishStateMask);
        }

        /// <inheritdoc/>
        protected override ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
            DateTime publishTime, DataChangeNotification notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            return _handler.OnDataChangeNotificationAsync(this, sequenceNumber,
                publishTime, _monitoredItems.CreateNotification(notification),
                publishStateMask, stringTable);
        }

        /// <inheritdoc/>
        protected override ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
            DateTime publishTime, EventNotificationList notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable)
        {
            return _handler.OnEventDataNotificationAsync(this, sequenceNumber,
                publishTime, _monitoredItems.CreateNotification(notification),
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
                    await _stateControl.WaitAsync(ct).ConfigureAwait(false);
                    await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                    var options = Options;
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

                            var modified = await _monitoredItems.ApplyChangesAsync(
                                false, false, ct).ConfigureAwait(false);
                            if (modified)
                            {
                                OnSubscriptionStateChanged(SubscriptionState.Modified);
                            }
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to apply subscription changes.");
                    }
                    finally
                    {
                        _stateLock.Release();
                    }
                }
            }
            catch (OperationCanceledException) { }

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
                var response = await _context.SubscriptionServiceSet.DeleteSubscriptionsAsync(null,
                    subscriptionIds, ct).ConfigureAwait(false);
                // validate response.
                Ua.ClientBase.ValidateResponse(response.Results, subscriptionIds);
                Ua.ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(response.Results[0]))
                {
                    throw new ServiceResultException(Ua.ClientBase.GetResult(
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
            AdjustCounts(options, out var revisedMaxKeepAliveCount, out var revisedLifetimeCount);

            var response = await _context.SubscriptionServiceSet.CreateSubscriptionAsync(null,
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
            AdjustCounts(options, out var revisedMaxKeepAliveCount, out var revisedLifetimeCount);

            if (revisedMaxKeepAliveCount != CurrentKeepAliveCount ||
                revisedLifetimeCount != CurrentLifetimeCount ||
                options.Priority != CurrentPriority ||
                options.MaxNotificationsPerPublish != CurrentMaxNotificationsPerPublish ||
                options.PublishingInterval != CurrentPublishingInterval)
            {
                var response = await _context.SubscriptionServiceSet.ModifySubscriptionAsync(null, Id,
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
                var response = await _context.SubscriptionServiceSet.SetPublishingModeAsync(
                    null, options.PublishingEnabled, subscriptionIds, ct).ConfigureAwait(false);

                // validate response.
                Ua.ClientBase.ValidateResponse(response.Results, subscriptionIds);
                Ua.ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(response.Results[0]))
                {
                    throw new ServiceResultException(
                        Ua.ClientBase.GetResult(response.Results[0], 0,
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
            var state = created ?
                SubscriptionState.Created : SubscriptionState.Modified;
            _monitoredItems.OnSubscriptionStateChange(state, CurrentPublishingInterval);
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

            _deletedItems.Clear();

            // Notify all monitored items of the changes
            _monitoredItems.OnSubscriptionStateChange(SubscriptionState.Deleted,
                CurrentPublishingInterval);
            OnSubscriptionStateChanged(SubscriptionState.Deleted);
        }

        /// <summary>
        /// Stop the keep alive timer for the subscription.
        /// </summary>
        private void StopKeepAliveTimer()
        {
            _publishTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Starts a timer to ensure publish requests are sent frequently
        /// enough to detect network interruptions.
        /// </summary>
        private void StartKeepAliveTimer()
        {
            var options = Options;
            _lastNotificationTimestamp = TimeProvider.System.GetTimestamp();
            _keepAliveInterval = CurrentPublishingInterval.Multiply(CurrentKeepAliveCount + 1);
            if (_keepAliveInterval < kMinKeepAliveTimerInterval)
            {
                _keepAliveInterval = options.PublishingInterval.Multiply(options.KeepAliveCount + 1);
            }
            if (_keepAliveInterval > Timeout.InfiniteTimeSpan)
            {
                _keepAliveInterval = Timeout.InfiniteTimeSpan;
            }
            if (_keepAliveInterval < kMinKeepAliveTimerInterval)
            {
                _keepAliveInterval = kMinKeepAliveTimerInterval;
            }
            _publishTimer.Change(_keepAliveInterval, _keepAliveInterval);
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
                Interlocked.Increment(ref _publishLateCount);
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
                    options.MinLifetimeInterval < _context.SessionTimeout)
                {
                    _logger.LogWarning(
                        "{Subscription}: A smaller minimum LifetimeInterval " +
                        "{Counter}ms than session timeout {Timeout}ms configured.",
                        this, options.MinLifetimeInterval, _context.SessionTimeout);
                }

                var minLifetimeInterval = (uint)options.MinLifetimeInterval.TotalMilliseconds;
                var publishingInterval = (uint)options.PublishingInterval.TotalMilliseconds;
                var minLifetimeCount = minLifetimeInterval / publishingInterval;
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

                if (options.PublishingInterval.Multiply(lifetimeCount) < _context.SessionTimeout)
                {
                    _logger.LogWarning(
                        "{Subscription}: Lifetime {LifeTime}ms configured is less " +
                        "than session timeout {Timeout}ms.", this,
                        options.PublishingInterval.Multiply(lifetimeCount), _context.SessionTimeout);
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
            var minLifeTimeCount = 3 * keepAliveCount;
            if (lifetimeCount < minLifeTimeCount)
            {
                _logger.LogInformation(
                    "{Subscription}: Adjusted LifetimeCount from {Old} to {New}.",
                    this, lifetimeCount, minLifeTimeCount);
                lifetimeCount = minLifeTimeCount;
            }
        }

        private static readonly TimeSpan kMinKeepAliveTimerInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan kKeepAliveTimerMargin = TimeSpan.FromSeconds(1);
        private TimeSpan _keepAliveInterval;
        private int _publishLateCount;
        private readonly Nito.AsyncEx.AsyncAutoResetEvent _stateControl = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _stateManagement;
        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private readonly List<uint> _deletedItems = [];
        private readonly ITimer _publishTimer;
        private readonly IDisposable? _changeTracking;
        private readonly ISubscriptionNotificationHandler _handler;
        private readonly ISubscriptionContext _context;
        private readonly MonitoredItemManager _monitoredItems;
    }
}
