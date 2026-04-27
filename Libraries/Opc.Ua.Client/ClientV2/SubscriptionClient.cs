#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Options;
    using Microsoft.Extensions.Logging;
    using Nito.AsyncEx;
    using Opc.Ua.Client.Sessions;
    using Opc.Ua.Client.Subscriptions;
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// <para>
    /// In many cases a client will only be interested in handful of items that
    /// it wants to subsribe to. However, if there are numerous of these
    /// subscriptions this will be inefficient on top of servers, which typically
    /// limit the number of subscriptions to a handful also.
    /// </para>
    /// <para>
    /// Virtual subscriptions ensure that batches of monitored items are
    /// efficiently partitioned across phyiscal subscriptions.
    /// </para>
    /// <para>
    /// The subscription client provides a seperate API on top of the core
    /// subscription api. It uses a bag packing algorithm to partition monitored
    /// items across subscriptions on top of the session.
    /// </para>
    /// </summary>
    internal sealed class SubscriptionClient : IAsyncDisposable
    {
        /// <summary>
        /// Create client
        /// </summary>
        /// <param name="session"></param>
        /// <param name="telemetry"></param>
        public SubscriptionClient(Sessions.ISession session, ITelemetryContext telemetry)
        {
            _session = session;
            _observability = telemetry;
            _logger = _observability.LoggerFactory.CreateLogger<SubscriptionClient>();
            _resyncTimer = TimeProvider.System.CreateTimer(
                _ => _syncEvent.Set(),
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _syncTask = ManageSubscriptionsAsync(_cts.Token);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    await _cts.CancelAsync().ConfigureAwait(false);
                    await _syncTask.ConfigureAwait(false);
                }
                finally
                {
                    _resyncTimer.Dispose();
                }
            }
        }

        /// <summary>
        /// Register a new subscriber for a subscription defined by the
        /// subscription template.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="queue"></param>
        /// <param name="ct"></param>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<IAsyncDisposable> RegisterAsync(
            IOptionsMonitor<SubscriptionClientOptions> options, INotificationQueue queue,
            CancellationToken ct = default)
        {
            _ = options.CurrentValue.Options
                ?? throw ServiceResultException.Create(StatusCodes.BadInvalidArgument,
                    "Subscription options are missing and must be specified.");
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                //
                // If queue is registered with a different subscription we either
                // update the subscription or dispose the old one and create a new one.
                //
                if (_registrations.TryGetValue(queue, out var existing))
                {
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Queue is already registered with a subscription.");
                }

                var registration = new Registration(this, queue, options);
                _registrations.Add(queue, registration);
                _syncEvent.Set();
                return registration;
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        /// <summary>
        /// Manage subscriptions
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async Task ManageSubscriptionsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _syncEvent.WaitAsync(ct).ConfigureAwait(false);
                    _resyncTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    await SyncAsync(_session.Connected, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Client}: Error in subscription management.",
                        this);
                }
            }
        }

        /// <summary>
        /// Subscription registration which tracks the registered options and
        /// provides the channel to communicate back with the api client.
        /// </summary>
        internal sealed record Registration : IAsyncDisposable
        {
            /// <summary>
            /// Monitored items on the subscriber
            /// </summary>
            public IOptionsMonitor<SubscriptionClientOptions> Options { get; }

            /// <summary>
            /// Queue to publish notifications to
            /// </summary>
            public INotificationQueue Queue { get; }

            /// <summary>
            /// Mark the registration as dirty
            /// </summary>
            internal bool Dirty { get; set; }

            /// <summary>
            /// Create subscription
            /// </summary>
            /// <param name="outer"></param>
            /// <param name="queue"></param>
            /// <param name="options"></param>
            public Registration(SubscriptionClient outer, INotificationQueue queue,
                IOptionsMonitor<SubscriptionClientOptions> options)
            {
                Queue = queue;
                Options = options;
                options.OnChange((_, __) =>
                {
                    Dirty = true;
                    outer._syncEvent.Set();
                });
                _outer = outer;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (_outer._disposed)
                {
                    //
                    // Possibly the client has shut down before the owners of
                    // the registration have disposed it. This is not an error.
                    // It might however be better to order the clients to get
                    // disposed before clients.
                    //
                    return;
                }

                // Remove registration
                await _outer._subscriptionLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    _outer._registrations.Remove(Queue);
                    _outer._syncEvent.Set();
                }
                finally
                {
                    _outer._subscriptionLock.Release();
                }
            }

            private readonly SubscriptionClient _outer;
        }

        /// <summary>
        /// Called by the management thread to synchronize the subscriptions with the
        /// current view of the registrations.
        /// </summary>
        /// <param name="connected"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async Task SyncAsync(bool connected, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var removals = 0;
            var additions = 0;
            var updates = 0;
            Dictionary<Subscriptions.SubscriptionOptions, VirtualSubscription> existing;
            lock (_subscriptions)
            {
                existing = _subscriptions.ToDictionary();
            }

            _logger.LogDebug(
                "{Client}: Perform synchronization of subscriptions (total: {Total})",
                this, _session.Subscriptions.Count);

            // Get the max item per subscription as well as max
            var delay = Timeout.InfiniteTimeSpan;

            //
            // Take the subscription lock here! - we hold it all the way until we
            // have updated all subscription states. The subscriptions will access
            // the client again to obtain the monitored items from the subscribers
            // and we do not want any subscribers to be touched or removed while
            // we process the current registrations. Since the call to get the items
            // is frequent, we do not want to generate a copy every time but let
            // the subscriptions access the items directly.
            //
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Get the current registrations
                var s2r = _registrations.Values
                    .GroupBy(k => k.Options.CurrentValue.Options!)
                    .ToDictionary(v => v.Key, v => v.ToList());

                // Close and remove subscriptions that have no more registrations
                await Task.WhenAll(existing.Keys
                    .Except(s2r.Keys)
                    .Select(k => existing[k])
                    .Select(async subscription =>
                    {
                        try
                        {
                            lock (_subscriptions)
                            {
                                _subscriptions.Remove(subscription.CurrentValue);
                            }

                            // Removes the item from the session and dispose
                            await subscription.DisposeAsync().ConfigureAwait(false);
                            Interlocked.Increment(ref removals);
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "{Client}: Failed to close " +
                                "subscription {Subscription} in session.",
                                this, subscription);
                        }
                    })).ConfigureAwait(false);

                // Add new subscription
                var delays = await Task.WhenAll(s2r.Keys
                    .Except(existing.Keys)
                    .Select(async add =>
                    {
                        try
                        {
                            //
                            // Create a new virtual subscription with the
                            // subscription configuration template that as
                            // of yet has no representation
                            //
                            var subscription = new VirtualSubscription(this,
                                add, [.. s2r[add]]);
                            lock (_subscriptions)
                            {
                                _subscriptions.Add(add, subscription);
                            }

                            // Sync the subscription which will get it to go live.
                            await subscription.SyncAsync(_session.OperationLimits,
                                ct).ConfigureAwait(false);
                            Interlocked.Increment(ref additions);

                            s2r[add].ForEach(r => r.Dirty = false);
                            return Timeout.InfiniteTimeSpan;
                        }
                        catch (OperationCanceledException)
                        {
                            return Timeout.InfiniteTimeSpan;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "{Client}: Failed to add " +
                                "subscription {Subscription} in session.",
                                this, add);
                            return TimeSpan.FromMinutes(1);
                        }
                    })).ConfigureAwait(false);

                delay = delays.DefaultIfEmpty(Timeout.InfiniteTimeSpan).Min();
                // Update any items where subscriber signalled the item was updated
                delays = await Task.WhenAll(s2r.Keys.Intersect(existing.Keys)
                    .Where(u => s2r[u].Any(b => b.Dirty || connected))
                    .Select(async update =>
                    {
                        try
                        {
                            var subscription = existing[update];
                            subscription.Registrations = [.. s2r[update]];
                            await subscription.SyncAsync(_session.OperationLimits,
                                ct).ConfigureAwait(false);
                            Interlocked.Increment(ref updates);
                            s2r[update].ForEach(r => r.Dirty = false);
                            return Timeout.InfiniteTimeSpan;
                        }
                        catch (OperationCanceledException)
                        {
                            return Timeout.InfiniteTimeSpan;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "{Client}: Failed to update " +
                                "subscription {Subscription} in session.",
                                this, update);
                            return TimeSpan.FromMinutes(1);
                        }
                    })).ConfigureAwait(false);

                var delay2 = delays.DefaultIfEmpty(Timeout.InfiniteTimeSpan).Min();
                RescheduleSynchronization(delay < delay2 ? delay : delay2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Client}: Error trying to sync subscriptions.",
                    this);
                var delay2 = TimeSpan.FromMinutes(1);
                RescheduleSynchronization(delay < delay2 ? delay : delay2);
            }
            finally
            {
                _subscriptionLock.Release();
            }

            if (updates + removals + additions == 0)
            {
                return;
            }
            _logger.LogInformation("{Client}: Removed {Removals}, added {Additions}," +
                " and updated {Updates} subscriptions (total: {Total}) took {Duration} ms.",
                this, removals, additions, updates, _session.Subscriptions.Count,
                sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Called under lock, schedule resynchronization of all subscriptions
        /// after the specified delay
        /// </summary>
        /// <param name="delay"></param>
        private void RescheduleSynchronization(TimeSpan delay)
        {
            Debug.Assert(_subscriptionLock.CurrentCount == 0, "Must be locked");

            if (delay == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            var nextSync = TimeProvider.System.GetUtcNow() + delay;
            if (nextSync <= _nextSync)
            {
                _nextSync = nextSync;
                _resyncTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Virtual subscription is a subscription that pools subscriptions across
        /// several partitions that are made up of actual subscriptions in the
        /// underlying session.
        /// </summary>
        internal sealed class VirtualSubscription : Opc.Ua.OptionsMonitor<Subscriptions.SubscriptionOptions>,
            ISubscriptionNotificationHandler, IAsyncDisposable
        {
            /// <summary>
            /// Registrations associated with this subscription (once applied)
            /// </summary>
            public List<Registration> Registrations { get; set; }

            /// <summary>
            /// Create a virtual subscription that contains one or more subscriptions
            /// partitioned by max supported monitored items.
            /// </summary>
            /// <param name="client"></param>
            /// <param name="option"></param>
            /// <param name="registrations"></param>
            public VirtualSubscription(SubscriptionClient client, Subscriptions.SubscriptionOptions option,
                List<Registration> registrations) : base(option)
            {
                _subscriptionClient = client;
                _logger = client._observability.LoggerFactory.CreateLogger<VirtualSubscription>();
                Registrations = registrations;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    List<ISubscription> subscriptions;
                    lock (_lock)
                    {
                        subscriptions = [.. _subscriptions];
                        _subscriptions.Clear();
                    }
                    foreach (var subscription in subscriptions)
                    {
                        await subscription.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _lock.Dispose();
                }
            }

            /// <inheritdoc/>
            public async ValueTask OnKeepAliveNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
            {
                foreach (var registration in Registrations)
                {
                    await registration.Queue.QueueAsync(new KeepAlive(sequenceNumber,
                        publishTime, publishStateMask)).ConfigureAwait(false);
                }
            }

            /// <inheritdoc/>
            public async ValueTask OnDataChangeNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime, ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                // Split per registration
                var split = new Dictionary<Registration, List<DataValueChange>>();
                var m2r = _m2r;
                foreach (var value in notification.Span)
                {
                    if (value.MonitoredItem != null &&
                        m2r.TryGetValue(value.MonitoredItem, out var registration))
                    {
                        if (!split.TryGetValue(registration, out var list))
                        {
                            list = [];
                            split.Add(registration, list);
                        }
                        list.Add(value);
                    }
                    else
                    {
                        _logger.LogDebug("No notifications added to the message.");
                    }
                }
                foreach (var (registration, changes) in split)
                {
                    await registration.Queue.QueueAsync(new DataChanges(sequenceNumber,
                        publishTime, changes, publishStateMask, stringTable)).ConfigureAwait(false);
                }
            }

            /// <inheritdoc/>
            public async ValueTask OnEventDataNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime, ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                var m2r = _m2r;
                foreach (var eventNotification in notification.ToArray())
                {
                    if (eventNotification.MonitoredItem != null &&
                        m2r.TryGetValue(eventNotification.MonitoredItem, out var registration))
                    {
                        await registration.Queue.QueueAsync(new Event(sequenceNumber, publishTime,
                            eventNotification, publishStateMask, stringTable)).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogDebug("No notifications added to the message.");
                    }
                }
            }

            /// <summary>
            /// Create or update the subscriptions inside using the currently configured
            /// subscription options and complying to the provided limits.
            /// </summary>
            /// <param name="limits"></param>
            /// <param name="ct"></param>
            /// <returns></returns>
            /// <exception cref="ServiceResultException"></exception>
            internal async ValueTask SyncAsync(Limits limits, CancellationToken ct)
            {
                var maxMonitoredItems = limits.MaxMonitoredItemsPerSubscription;
                if (maxMonitoredItems == 0)
                {
                    maxMonitoredItems = kMaxMonitoredItemPerSubscriptionDefault;
                }

                // Parition the monitored items across subscriptions
                await _lock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var partitions = BagPackedPartition.Create(Registrations, maxMonitoredItems);

                    if (_subscriptions.Count < partitions.Count)
                    {
                        // Grow
                        for (var idx = _subscriptions.Count; idx < partitions.Count; idx++)
                        {
                            var subscription = _subscriptionClient._session.Subscriptions.Add(this, this);
                            _subscriptions.Add(subscription);
                        }
                    }
                    else if (_subscriptions.Count > partitions.Count)
                    {
                        // Shrink
                        foreach (var subscription in _subscriptions.Skip(partitions.Count))
                        {
                            await subscription.DisposeAsync().ConfigureAwait(false);
                        }
                        _subscriptions.RemoveRange(partitions.Count, _subscriptions.Count -
                            partitions.Count);
                    }

                    for (var partitionIdx = 0; partitionIdx < partitions.Count; partitionIdx++)
                    {
                        var monitoredItems = _subscriptions[partitionIdx].MonitoredItems.Update(
                            partitions[partitionIdx].Items.ConvertAll(item =>
                                (item.Name, (IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions>)item.Options))
    );

                        // Create lookup to split the monitored item notifications on receive
                        _m2r = partitions[partitionIdx].Items.Zip(monitoredItems)
                            .ToDictionary(k => k.Second, v => v.First.Registration);
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }

            /// <summary>
            /// Helper to partition subscribers across subscriptions.
            /// </summary>
            internal sealed class BagPackedPartition
            {
                /// <summary>
                /// Monitored items that should be in the subscription partition
                /// </summary>
                public List<(
                    Registration Registration,
                    string Name,
                    IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions> Options
                    )> Items
                { get; } = [];

                /// <summary>
                /// Create
                /// </summary>
                /// <param name="registrations"></param>
                /// <param name="maxMonitoredItems"></param>
                /// <returns></returns>
                public static List<BagPackedPartition> Create(
                    IEnumerable<Registration> registrations, uint maxMonitoredItems)
                {
                    var partitions = new List<BagPackedPartition>();
                    foreach (var registeredItems in registrations
                        .Select(r => r.Options.CurrentValue.MonitoredItems
                            .Select(m => (r, m.Key, m.Value))
                            .ToArrayOf())
                        .OrderByDescending(tl => tl.Count))
                    {
                        var placed = false;
                        foreach (var partition in partitions)
                        {
                            if (partition.Items.Count +
                                registeredItems.Count <= maxMonitoredItems)
                            {
                                partition.Items.AddRange(registeredItems);
                                placed = true;
                                break;
                            }
                        }
                        if (!placed)
                        {
                            // Break items into batches of max here and add partition each
                            foreach (var batch in registeredItems.Batch((int)maxMonitoredItems))
                            {
                                var newPartition = new BagPackedPartition();
                                newPartition.Items.AddRange(batch);
                                partitions.Add(newPartition);
                            }
                        }
                    }
                    return partitions;
                }
            }

            private Dictionary<IMonitoredItem, Registration> _m2r = [];
            private readonly SubscriptionClient _subscriptionClient;
            private readonly List<ISubscription> _subscriptions = [];
            private readonly SemaphoreSlim _lock = new(1, 1);
            private readonly ILogger _logger;
        }

        private const int kMaxMonitoredItemPerSubscriptionDefault = 64 * 1024;
        private readonly Dictionary<INotificationQueue, Registration> _registrations = [];
        private readonly Dictionary<Subscriptions.SubscriptionOptions, VirtualSubscription> _subscriptions = [];
        private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
        private readonly CancellationTokenSource _cts = new();
        private readonly AsyncAutoResetEvent _syncEvent = new();
        private readonly ITimer _resyncTimer;
        private readonly Task _syncTask;
        private readonly Sessions.ISession _session;
        private readonly ITelemetryContext _observability;
        private readonly ILogger _logger;
        private DateTimeOffset _nextSync;
        private bool _disposed;
    }
}
#endif
