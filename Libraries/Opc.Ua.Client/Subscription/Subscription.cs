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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A subscription.
    /// </summary>
    public class Subscription : ISnapshotRestore<SubscriptionState>, IDisposable, ICloneable
    {
        private const int kMinKeepAliveTimerInterval = 1000;
        private const int kKeepAliveTimerMargin = 1000;
        private const int kRepublishMessageTimeout = 2500;
        private const int kRepublishMessageExpiredTimeout = 10000;

        /// <summary>
        /// Create subscription
        /// </summary>
        [Obsolete("Use Subscription(TelemetryContext) instead")]
        public Subscription()
            : this(null!, null)
        {
        }

        /// <summary>
        /// Creates a empty object.
        /// </summary>
        public Subscription(ITelemetryContext telemetry, SubscriptionOptions? options = null)
        {
            Telemetry = telemetry ?? AmbientMessageContext.Telemetry;
            m_logger = Telemetry.CreateLogger<Subscription>();
            State = options ?? new SubscriptionOptions();
            DefaultItem = CreateMonitoredItem();
        }

        /// <summary>
        /// Initializes the subscription from a template.
        /// </summary>
        /// <param name="template">The template.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        public Subscription(Subscription template, bool copyEventHandlers = false)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            m_telemetry = template.m_telemetry;
            m_logger = template.m_logger;
            State = template.State;
            Handle = template.Handle;
            DefaultItem = CreateMonitoredItem(template.DefaultItem.State);
            m_lastSequenceNumberProcessed = template.m_lastSequenceNumberProcessed;

            if (copyEventHandlers)
            {
                m_StateChanged = template.m_StateChanged;
                m_PublishStatusChanged = template.m_PublishStatusChanged;
                FastDataChangeCallback = template.FastDataChangeCallback;
                FastEventCallback = template.FastEventCallback;
                FastKeepAliveCallback = template.FastKeepAliveCallback;
            }

            // copy the list of monitored items.
            var clonedMonitoredItems = new List<MonitoredItem>();
            foreach (MonitoredItem monitoredItem in template.MonitoredItems)
            {
                MonitoredItem clone = monitoredItem.CloneMonitoredItem(copyEventHandlers, true);
                clone.DisplayName = monitoredItem.DisplayName;
                clonedMonitoredItems.Add(clone);
            }
            if (clonedMonitoredItems.Count > 0)
            {
                AddItems(clonedMonitoredItems);
            }
        }

        /// <inheritdoc/>
        public virtual void Restore(SubscriptionState state)
        {
            CurrentPublishingInterval = state.CurrentPublishingInterval;
            CurrentKeepAliveCount = state.CurrentKeepAliveCount;
            CurrentLifetimeCount = state.CurrentLifetimeCount;

            var monitoredItems = new List<MonitoredItem>(state.MonitoredItems.Count);
            foreach (MonitoredItemState monitoredItemState in state.MonitoredItems)
            {
                MonitoredItem monitoredItem = CreateMonitoredItem(monitoredItemState);
                monitoredItem.Restore(monitoredItemState);
                monitoredItems.Add(monitoredItem);
            }

            AddItems(monitoredItems);
        }

        /// <inheritdoc/>
        public virtual void Snapshot(out SubscriptionState state)
        {
            lock (m_cache)
            {
                var monitoredItemStateCollection = new MonitoredItemStateCollection(
                    m_monitoredItems.Count);
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    monitoredItem.Snapshot(out MonitoredItemState monitoredItemState);
                    monitoredItemStateCollection.Add(monitoredItemState);
                }
                state = new SubscriptionState(State)
                {
                    MonitoredItems = monitoredItemStateCollection,
                    CurrentKeepAliveCount = CurrentKeepAliveCount,
                    CurrentLifetimeCount = CurrentLifetimeCount,
                    CurrentPublishingInterval = CurrentPublishingInterval
                };
            }
        }

        /// <summary>
        /// Resets the state of the publish timer and associated message worker.
        /// </summary>
        private void ResetPublishTimerAndWorkerState()
        {
            ResetPublishTimerAndWorkerStateAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Resets the state of the publish timer and associated message worker.
        /// </summary>
        private async Task ResetPublishTimerAndWorkerStateAsync()
        {
            Task? workerTask;
            CancellationTokenSource? workerCts;
            lock (m_cache)
            {
                // Called under the m_cache lock
                if (m_publishTimer == null &&
                    m_messageWorkerCts == null &&
                    m_messageWorkerTask == null &&
                    m_messageWorkerEvent == null)
                {
                    return;
                }

                // stop the publish timer.
                Utils.SilentDispose(m_publishTimer);
                m_publishTimer = null;

                if (m_messageWorkerTask == null)
                {
                    Utils.SilentDispose(m_messageWorkerCts);
                    m_messageWorkerCts = null;
                    return;
                }

                // stop the publish worker (outside of lock)
                workerTask = m_messageWorkerTask;
                workerCts = m_messageWorkerCts;
                m_messageWorkerTask = null;
                m_messageWorkerCts = null;
            }
            try
            {
                m_messageWorkerEvent.Set();
                workerCts?.Cancel();
                await workerTask.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "SubscriptionId {SubscriptionId} - Reset Publish Worker exception.", Id);
            }
            finally
            {
                Utils.SilentDispose(workerCts);
            }
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
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
            if (disposing)
            {
                ResetPublishTimerAndWorkerState();
                m_disposed = true;
            }
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new Subscription(this);
        }

        /// <summary>
        /// Clones a subscription or a subclass with an option to copy event handlers.
        /// </summary>
        /// <returns>A cloned instance of the subscription or its subclass.</returns>
        public virtual Subscription CloneSubscription(bool copyEventHandlers)
        {
            return new Subscription(this, copyEventHandlers);
        }

        /// <summary>
        /// Create a monitored item with the provided item state
        /// </summary>
        protected virtual MonitoredItem CreateMonitoredItem(MonitoredItemOptions? options = null)
        {
            return new MonitoredItem(Telemetry!, options);
        }

        /// <summary>
        /// Subscription state/options
        /// </summary>
        public SubscriptionOptions State { get; private set; }

        /// <summary>
        /// Raised to indicate that the state of the subscription has changed.
        /// </summary>
        public event SubscriptionStateChangedEventHandler StateChanged
        {
            add => m_StateChanged += value;
            remove => m_StateChanged -= value;
        }

        /// <summary>
        /// Raised to indicate the publishing state for the subscription has stopped or resumed (see PublishingStopped property).
        /// </summary>
        public event PublishStateChangedEventHandler PublishStatusChanged
        {
            add => m_PublishStatusChanged += value;
            remove => m_PublishStatusChanged -= value;
        }

        /// <summary>
        /// A display name for the subscription.
        /// </summary>
        public string DisplayName
        {
            get => State.DisplayName;
            set => State = State with { DisplayName = value };
        }

        /// <summary>
        /// The publishing interval.
        /// </summary>
        public int PublishingInterval
        {
            get => State.PublishingInterval;
            set => State = State with { PublishingInterval = value };
        }

        /// <summary>
        /// The keep alive count.
        /// </summary>
        public uint KeepAliveCount
        {
            get => State.KeepAliveCount;
            set => State = State with { KeepAliveCount = value };
        }

        /// <summary>
        /// The life time of the subscription in counts of
        /// publish interval.
        /// LifetimeCount shall be at least 3*KeepAliveCount.
        /// </summary>
        public uint LifetimeCount
        {
            get => State.LifetimeCount;
            set => State = State with { LifetimeCount = value };
        }

        /// <summary>
        /// The maximum number of notifications per publish request.
        /// </summary>
        public uint MaxNotificationsPerPublish
        {
            get => State.MaxNotificationsPerPublish;
            set => State = State with { MaxNotificationsPerPublish = value };
        }

        /// <summary>
        /// Whether publishing is enabled.
        /// </summary>
        public bool PublishingEnabled
        {
            get => State.PublishingEnabled;
            set => State = State with { PublishingEnabled = value };
        }

        /// <summary>
        /// The priority assigned to the subscription.
        /// </summary>
        public byte Priority
        {
            get => State.Priority;
            set => State = State with { Priority = value };
        }

        /// <summary>
        /// The timestamps to return with the notification messages.
        /// </summary>
        public TimestampsToReturn TimestampsToReturn
        {
            get => State.TimestampsToReturn;
            set => State = State with { TimestampsToReturn = value };
        }

        /// <summary>
        /// The maximum number of messages to keep in the internal cache.
        /// </summary>
        public int MaxMessageCount
        {
            get => State.MaxMessageCount;
            set
            {
                // lock needed to synchronize with message list processing
                lock (m_cache)
                {
                    State = State with { MaxMessageCount = value };
                }
            }
        }

        /// <summary>
        /// The default monitored item.
        /// </summary>
        public MonitoredItem DefaultItem { get; set; }

        /// <summary>
        /// The minimum lifetime for subscriptions in milliseconds.
        /// </summary>
        public uint MinLifetimeInterval
        {
            get => State.MinLifetimeInterval;
            set => State = State with { MinLifetimeInterval = value };
        }

        /// <summary>
        /// Gets or sets a value indicating whether the notifications are cached within the monitored items.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if monitored item cache is disabled; otherwise, <c>false</c>.
        /// </value>
        /// <remarks>
        /// Applications must process the Session.Notication event if this is set to true.
        /// This flag improves performance by eliminating the processing involved in updating the cache.
        /// </remarks>
        public bool DisableMonitoredItemCache
        {
            get => State.DisableMonitoredItemCache;
            set => State = State with { DisableMonitoredItemCache = value };
        }

        /// <summary>
        /// Gets or sets the behavior of waiting for sequential order in handling incoming messages.
        /// </summary>
        /// <value>
        /// <c>true</c> if incoming messages are handled sequentially; <c>false</c> otherwise.
        /// </value>
        /// <remarks>
        /// Setting <see cref="SequentialPublishing"/> to <c>true</c> means incoming messages are processed in
        /// a "single-threaded" manner and callbacks will not be invoked in parallel.
        /// </remarks>
        public bool SequentialPublishing
        {
            get => State.SequentialPublishing;
            set
            {
                // synchronize with message list processing
                lock (m_cache)
                {
                    State = State with { SequentialPublishing = value };
                }
            }
        }

        /// <summary>
        /// If the available sequence numbers of a subscription
        /// are republished or acknowledged after a transfer.
        /// </summary>
        /// <remarks>
        /// Default <c>false</c>, set to <c>true</c> if no data loss is important
        /// and available publish requests (sequence numbers) that were never acknowledged should be
        /// recovered with a republish. The setting is used after a subscription transfer.
        /// </remarks>
        public bool RepublishAfterTransfer
        {
            get => State.RepublishAfterTransfer;
            set => State = State with { RepublishAfterTransfer = value };
        }

        /// <summary>
        /// The unique identifier assigned by the server which can be used to transfer a session.
        /// </summary>
        public uint TransferId
        {
            get => State.TransferId;
            set => State = State with { TransferId = value };
        }

        /// <summary>
        /// Gets or sets the fast data change callback.
        /// </summary>
        /// <value>The fast data change callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastDataChangeNotificationEventHandler? FastDataChangeCallback { get; set; }

        /// <summary>
        /// Gets or sets the fast event callback.
        /// </summary>
        /// <value>The fast event callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastEventNotificationEventHandler? FastEventCallback { get; set; }

        /// <summary>
        /// Gets or sets the fast keep alive callback.
        /// </summary>
        /// <value>The keep alive change callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastKeepAliveNotificationEventHandler? FastKeepAliveCallback { get; set; }

        /// <summary>
        /// The items to monitor.
        /// </summary>
        public IEnumerable<MonitoredItem> MonitoredItems
        {
            get
            {
                lock (m_cache)
                {
                    return [.. m_monitoredItems.Values];
                }
            }
        }

        /// <summary>
        /// Returns true if the subscription has changes that need to be applied.
        /// </summary>
        public bool ChangesPending
        {
            get
            {
                lock (m_cache)
                {
                    if (m_deletedItems.Count > 0)
                    {
                        return true;
                    }

                    foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                    {
                        if (Created && !monitoredItem.Status.Created)
                        {
                            return true;
                        }

                        if (monitoredItem.AttributesModified)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }

        /// <summary>
        /// Returns the number of monitored items.
        /// </summary>
        public uint MonitoredItemCount
        {
            get
            {
                lock (m_cache)
                {
                    return (uint)m_monitoredItems.Count;
                }
            }
        }

        /// <summary>
        /// The session that owns the subscription item.
        /// </summary>
        public ISession? Session { get; protected internal set; }

        /// <summary>
        /// Enables owners to set the telemetry context
        /// </summary>
        protected internal ITelemetryContext? Telemetry
        {
            get => m_telemetry; // Accessible from monitored item
            internal set
            {
                m_telemetry = value;
                m_logger = value.CreateLogger<MonitoredItem>();
            }
        }

        /// <summary>
        /// A local handle assigned to the subscription
        /// </summary>
        public object? Handle { get; set; }

        /// <summary>
        /// The unique identifier assigned by the server.
        /// </summary>
        public uint Id { get; private set; }

        /// <summary>
        /// Whether the subscription has been created on the server.
        /// </summary>
        public bool Created => Id != 0;

        /// <summary>
        /// The current publishing interval.
        /// </summary>
        public double CurrentPublishingInterval { get; set; }

        /// <summary>
        /// The current keep alive count.
        /// </summary>
        public uint CurrentKeepAliveCount { get; set; }

        /// <summary>
        /// The current lifetime count.
        /// </summary>
        public uint CurrentLifetimeCount { get; set; }

        /// <summary>
        /// Whether publishing is currently enabled.
        /// </summary>
        public bool CurrentPublishingEnabled { get; private set; }

        /// <summary>
        /// The priority assigned to subscription when it was created.
        /// </summary>
        public byte CurrentPriority { get; private set; }

        /// <summary>
        /// The time that the last notification received was published.
        /// </summary>
        public DateTime PublishTime
        {
            get
            {
                lock (m_cache)
                {
                    if (m_messageCache.Count > 0)
                    {
                        return m_messageCache.Last!.Value.PublishTime;
                    }
                }

                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// The time that the last notification was received.
        /// </summary>
        public DateTime LastNotificationTime
        {
            get
            {
                long ticks = Interlocked.Read(ref m_lastNotificationTime);
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// The sequence number assigned to the last notification message.
        /// </summary>
        public uint SequenceNumber
        {
            get
            {
                lock (m_cache)
                {
                    if (m_messageCache.Count > 0)
                    {
                        return m_messageCache.Last!.Value.SequenceNumber;
                    }
                }

                return 0;
            }
        }

        /// <summary>
        /// The number of notifications contained in the last notification message.
        /// </summary>
        public uint NotificationCount
        {
            get
            {
                lock (m_cache)
                {
                    if (m_messageCache.Count > 0)
                    {
                        return (uint)m_messageCache.Last!.Value.NotificationData.Count;
                    }
                }

                return 0;
            }
        }

        /// <summary>
        /// The last notification received from the server.
        /// </summary>
        public NotificationMessage? LastNotification
        {
            get
            {
                lock (m_cache)
                {
                    if (m_messageCache.Count > 0)
                    {
                        return m_messageCache.Last!.Value;
                    }

                    return null;
                }
            }
        }

        /// <summary>
        /// The cached notifications.
        /// </summary>
        public IEnumerable<NotificationMessage> Notifications
        {
            get
            {
                lock (m_cache)
                {
                    // make a copy to ensure the state of the last cannot change during enumeration.
                    return [.. m_messageCache];
                }
            }
        }

        /// <summary>
        /// The sequence numbers that are available for republish requests.
        /// </summary>
        public IEnumerable<uint> AvailableSequenceNumbers
        {
            get
            {
                lock (m_cache)
                {
                    return m_availableSequenceNumbers != null
                        ? m_availableSequenceNumbers.ToArray()
                        : [];
                }
            }
        }

        /// <summary>
        /// Sends a notification that the state of the subscription has changed.
        /// </summary>
        public void ChangesCompleted()
        {
            try
            {
                m_StateChanged?.Invoke(this, new SubscriptionStateChangedEventArgs(m_changeMask));
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Subscription state change callback exception with change mask 0x{ChangeMask:X2}",
                    m_changeMask);
            }
            m_changeMask = SubscriptionChangeMask.None;
        }

        /// <summary>
        /// Returns true if the subscription is not receiving publishes.
        /// </summary>
        public bool PublishingStopped
        {
            get
            {
                int timeSinceLastNotification = HiResClock.TickCount - m_lastNotificationTickCount;
                return timeSinceLastNotification > m_keepAliveInterval + kKeepAliveTimerMargin;
            }
        }

        /// <summary>
        /// Creates a subscription on the server and adds all monitored items.
        /// </summary>
        public async Task CreateAsync(CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(false);

            // create the subscription.
            uint revisedMaxKeepAliveCount = KeepAliveCount;
            uint revisedLifetimeCount = LifetimeCount;

            AdjustCounts(Session.SessionTimeout, ref revisedMaxKeepAliveCount, ref revisedLifetimeCount);

            CreateSubscriptionResponse response = await Session
                .CreateSubscriptionAsync(
                    null,
                    PublishingInterval,
                    revisedLifetimeCount,
                    revisedMaxKeepAliveCount,
                    MaxNotificationsPerPublish,
                    false,
                    Priority,
                    ct)
                .ConfigureAwait(false);

            CreateSubscription(
                response.SubscriptionId,
                response.RevisedPublishingInterval,
                response.RevisedMaxKeepAliveCount,
                response.RevisedLifetimeCount);

            await CreateItemsAsync(ct).ConfigureAwait(false);

            // only enable publishing afer CreateSubscription is called
            // to avoid race conditions with subscription cleanup.
            if (PublishingEnabled)
            {
                await SetPublishingModeAsync(PublishingEnabled, ct).ConfigureAwait(false);
            }

            ChangesCompleted();
        }

        /// <summary>
        /// Deletes a subscription on the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task DeleteAsync(bool silent, CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            if (!silent)
            {
                VerifySessionAndSubscriptionState(true);
            }

            // nothing to do if not created.
            if (!Created || Session == null)
            {
                return;
            }

            await ResetPublishTimerAndWorkerStateAsync().ConfigureAwait(false);

            try
            {
                // delete the subscription.
                UInt32Collection subscriptionIds = new uint[] { Id };

                DeleteSubscriptionsResponse response = await Session
                    .DeleteSubscriptionsAsync(null, subscriptionIds, ct)
                    .ConfigureAwait(false);

                // validate response.
                ClientBase.ValidateResponse(response.Results, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(response.Results[0]))
                {
                    throw new ServiceResultException(
                        ClientBase.GetResult(
                            response.Results[0],
                            0,
                            response.DiagnosticInfos,
                            response.ResponseHeader));
                }
            }
            // suppress exception if silent flag is set.
            catch (Exception e)
            {
                if (!silent)
                {
                    if (e is ServiceResultException)
                    {
                        throw;
                    }
                    throw new ServiceResultException(e, StatusCodes.Bad);
                }
            }
            // always put object in disconnected state even if an error occurs.
            finally
            {
                DeleteSubscription();
            }

            ChangesCompleted();
        }

        /// <summary>
        /// Modifies a subscription on the server.
        /// </summary>
        public async Task ModifyAsync(CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            // modify the subscription.
            uint revisedKeepAliveCount = KeepAliveCount;
            uint revisedLifetimeCounter = LifetimeCount;

            AdjustCounts(Session.SessionTimeout, ref revisedKeepAliveCount, ref revisedLifetimeCounter);

            ModifySubscriptionResponse response = await Session
                .ModifySubscriptionAsync(
                    null,
                    Id,
                    PublishingInterval,
                    revisedLifetimeCounter,
                    revisedKeepAliveCount,
                    MaxNotificationsPerPublish,
                    Priority,
                    ct)
                .ConfigureAwait(false);

            // update current state.
            ModifySubscription(
                response.RevisedPublishingInterval,
                response.RevisedMaxKeepAliveCount,
                response.RevisedLifetimeCount);

            ChangesCompleted();
        }

        /// <summary>
        /// Changes the publishing enabled state for the subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task SetPublishingModeAsync(bool enabled, CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            // modify the subscription.
            UInt32Collection subscriptionIds = new uint[] { Id };

            SetPublishingModeResponse response = await Session
                .SetPublishingModeAsync(null, enabled, new uint[] { Id }, ct)
                .ConfigureAwait(false);

            // validate response.
            ClientBase.ValidateResponse(response.Results, subscriptionIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

            if (StatusCode.IsBad(response.Results[0]))
            {
                throw new ServiceResultException(
                    ClientBase.GetResult(
                        response.Results[0],
                        0,
                        response.DiagnosticInfos,
                        response.ResponseHeader));
            }

            // update current state.
            CurrentPublishingEnabled = PublishingEnabled = enabled;
            m_changeMask |= SubscriptionChangeMask.Modified;

            ChangesCompleted();
        }

        /// <summary>
        /// Republishes the specified notification message.
        /// </summary>
        public async Task<NotificationMessage> RepublishAsync(
            uint sequenceNumber,
            CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            RepublishResponse response = await Session
                .RepublishAsync(null, Id, sequenceNumber, ct)
                .ConfigureAwait(false);

            return response.NotificationMessage;
        }

        /// <summary>
        /// Applies any changes to the subscription items.
        /// </summary>
        public async Task ApplyChangesAsync(CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            await DeleteItemsAsync(ct).ConfigureAwait(false);
            await ModifyItemsAsync(ct).ConfigureAwait(false);
            await CreateItemsAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves all relative paths to nodes on the server.
        /// </summary>
        public async Task ResolveItemNodeIdsAsync(CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            // collect list of browse paths.
            var browsePaths = new BrowsePathCollection();
            var itemsToBrowse = new List<MonitoredItem>();

            PrepareResolveItemNodeIds(browsePaths, itemsToBrowse);

            // nothing to do.
            if (browsePaths.Count == 0)
            {
                return;
            }

            // translate browse paths.
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct)
                .ConfigureAwait(false);

            BrowsePathResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, browsePaths);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToBrowse[ii]
                    .SetResolvePathResult(
                        results[ii],
                        ii,
                        response.DiagnosticInfos,
                        response.ResponseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsModified;
        }

        /// <summary>
        /// Creates all items on the server that have not already been created.
        /// </summary>
        public async Task<IList<MonitoredItem>> CreateItemsAsync(CancellationToken ct = default)
        {
            MonitoredItemCreateRequestCollection requestItems;
            List<MonitoredItem> itemsToCreate;
            VerifySession();

            (requestItems, itemsToCreate) = await PrepareItemsToCreateAsync(ct)
                .ConfigureAwait(false);

            if (requestItems.Count == 0)
            {
                return itemsToCreate;
            }

            using Activity? activity = m_telemetry.StartActivity();
            try
            {
                // create monitored items.
                CreateMonitoredItemsResponse response = await Session
                    .CreateMonitoredItemsAsync(null, Id, TimestampsToReturn, requestItems, ct)
                    .ConfigureAwait(false);

                MonitoredItemCreateResultCollection results = response.Results;
                ClientBase.ValidateResponse(results, itemsToCreate);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, itemsToCreate);

                // update results.
                for (int ii = 0; ii < results.Count; ii++)
                {
                    itemsToCreate[ii]
                        .SetCreateResult(
                            requestItems[ii],
                            results[ii],
                            ii,
                            response.DiagnosticInfos,
                            response.ResponseHeader);
                }
            }
            catch
            {
                // Clear the Creating flag on all items if an exception occurs
                foreach (MonitoredItem monitoredItem in itemsToCreate)
                {
                    monitoredItem.Status.Creating = false;
                }
                throw;
            }

            m_changeMask |= SubscriptionChangeMask.ItemsCreated;
            ChangesCompleted();

            // Restore triggering relationships after items are created
            await RestoreTriggeringAsync(ct).ConfigureAwait(false);

            // return the list of items affected by the change.
            return itemsToCreate;
        }

        /// <summary>
        /// Modifies all items that have been changed.
        /// </summary>
        public async Task<IList<MonitoredItem>> ModifyItemsAsync(CancellationToken ct = default)
        {
            VerifySessionAndSubscriptionState(true);

            var requestItems = new MonitoredItemModifyRequestCollection();
            var itemsToModify = new List<MonitoredItem>();

            PrepareItemsToModify(requestItems, itemsToModify);

            if (requestItems.Count == 0)
            {
                return itemsToModify;
            }

            using Activity? activity = m_telemetry.StartActivity();
            // modify the subscription.
            ModifyMonitoredItemsResponse response = await Session
                .ModifyMonitoredItemsAsync(null, Id, TimestampsToReturn, requestItems, ct)
                .ConfigureAwait(false);

            MonitoredItemModifyResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, itemsToModify);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, itemsToModify);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToModify[ii]
                    .SetModifyResult(
                        requestItems[ii],
                        results[ii],
                        ii,
                        response.DiagnosticInfos,
                        response.ResponseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsModified;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToModify;
        }

        /// <summary>
        /// Deletes all items that have been marked for deletion.
        /// </summary>
        public async Task<IList<MonitoredItem>> DeleteItemsAsync(
            CancellationToken ct = default)
        {
            VerifySessionAndSubscriptionState(true);

            if (m_deletedItems.Count == 0)
            {
                return [];
            }

            using Activity? activity = m_telemetry.StartActivity();
            List<MonitoredItem> itemsToDelete = m_deletedItems;
            m_deletedItems = [];

            var monitoredItemIds = new UInt32Collection();

            foreach (MonitoredItem monitoredItem in itemsToDelete)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            DeleteMonitoredItemsResponse response = await Session
                .DeleteMonitoredItemsAsync(null, Id, monitoredItemIds, ct)
                .ConfigureAwait(false);

            StatusCodeCollection results = response.Results;
            ClientBase.ValidateResponse(results, monitoredItemIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, monitoredItemIds);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToDelete[ii].SetDeleteResult(
                    results[ii],
                    ii,
                    response.DiagnosticInfos,
                    response.ResponseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsDeleted;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToDelete;
        }

        /// <summary>
        /// Restores triggering relationships for monitored items that were
        /// configured with triggers before reconnection.
        /// </summary>
        private async Task RestoreTriggeringAsync(CancellationToken ct = default)
        {
            VerifySessionAndSubscriptionState(true);

            // Build triggering groups outside of lock to avoid await in lock
            Dictionary<uint, List<uint>> triggeringGroups;
            lock (m_cache)
            {
                // Group monitored items by their triggering item
                triggeringGroups = [];
                foreach (MonitoredItem item in m_monitoredItems.Values)
                {
                    if (item.TriggeredItems != null && item.TriggeredItems.Count > 0)
                    {
                        // This item triggers other items
                        var triggeredServerIds = new List<uint>();
                        foreach (uint triggeredClientHandle in item.TriggeredItems)
                        {
                            // Find the monitored item by client handle
                            if (m_monitoredItems.TryGetValue(triggeredClientHandle, out MonitoredItem? triggeredItem) &&
                                triggeredItem.Status.Created)
                            {
                                triggeredServerIds.Add(triggeredItem.Status.Id);
                            }
                        }

                        if (triggeredServerIds.Count > 0)
                        {
                            if (!triggeringGroups.TryGetValue(item.Status.Id, out List<uint>? list))
                            {
                                list = [];
                                triggeringGroups[item.Status.Id] = list;
                            }
                            list.AddRange(triggeredServerIds);
                        }
                    }
                }
            }

            // Call SetTriggering for each triggering item
            foreach (KeyValuePair<uint, List<uint>> kvp in triggeringGroups)
            {
                uint triggeringItemId = kvp.Key;
                var linksToAdd = new UInt32Collection(kvp.Value);

                try
                {
                    await Session.SetTriggeringAsync(
                        null,
                        Id,
                        triggeringItemId,
                        linksToAdd,
                        [],
                        ct).ConfigureAwait(false);

                    m_logger.LogInformation(
                        "Restored {Count} triggering links for MonitoredItem {TriggeringItemId} in Subscription {SubscriptionId}",
                        linksToAdd.Count,
                        triggeringItemId,
                        Id);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        ex,
                        "Failed to restore triggering links for MonitoredItem {TriggeringItemId} in Subscription {SubscriptionId}",
                        triggeringItemId,
                        Id);
                }
            }
        }

        /// <summary>
        /// Set monitoring mode of items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="monitoredItems"/>
        /// is <c>null</c>.</exception>
        public async Task<List<ServiceResult?>?> SetMonitoringModeAsync(
            MonitoringMode monitoringMode,
            IList<MonitoredItem> monitoredItems,
            CancellationToken ct = default)
        {
            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            if (monitoredItems.Count == 0)
            {
                return null;
            }

            // get list of items to update.
            var monitoredItemIds = new UInt32Collection();
            foreach (MonitoredItem monitoredItem in monitoredItems)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            SetMonitoringModeResponse response = await Session
                .SetMonitoringModeAsync(null, Id, monitoringMode, monitoredItemIds, ct)
                .ConfigureAwait(false);

            StatusCodeCollection results = response.Results;
            ClientBase.ValidateResponse(results, monitoredItemIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, monitoredItemIds);

            // update results.
            var errors = new List<ServiceResult?>();
            bool noErrors = UpdateMonitoringMode(
                monitoredItems,
                errors,
                results,
                response.DiagnosticInfos,
                response.ResponseHeader,
                monitoringMode);

            // raise state changed event.
            m_changeMask |= SubscriptionChangeMask.ItemsModified;
            ChangesCompleted();

            // return null list if no errors occurred.
            if (noErrors)
            {
                return null;
            }

            return errors;
        }

        /// <summary>
        /// Sets the triggering relationships for a monitored item in this subscription
        /// and tracks them for automatic restoration after reconnection.
        /// </summary>
        /// <param name="triggeringItem">The monitored item that will trigger other items.</param>
        /// <param name="linksToAdd">Monitored items to be reported when the triggering item changes.</param>
        /// <param name="linksToRemove">Monitored items to stop reporting when the triggering item changes.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The response from the server.</returns>
        /// <exception cref="ArgumentNullException">Thrown when triggeringItem is null.</exception>
        /// <exception cref="ServiceResultException">Thrown when the operation fails.</exception>
        public async Task<SetTriggeringResponse> SetTriggeringAsync(
            MonitoredItem triggeringItem,
            IList<MonitoredItem>? linksToAdd,
            IList<MonitoredItem>? linksToRemove,
            CancellationToken ct = default)
        {
            if (triggeringItem == null)
            {
                throw new ArgumentNullException(nameof(triggeringItem));
            }

            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            if (!triggeringItem.Status.Created)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Triggering item has not been created on the server.");
            }

            // Convert monitored items to server IDs
            var serverIdsToAdd = new UInt32Collection();
            var clientHandlesToAdd = new UInt32Collection();
            if (linksToAdd != null)
            {
                foreach (MonitoredItem item in linksToAdd)
                {
                    if (!item.Status.Created)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            $"Monitored item '{item.DisplayName}' has not been created on the server.");
                    }
                    serverIdsToAdd.Add(item.Status.Id);
                    clientHandlesToAdd.Add(item.ClientHandle);
                }
            }

            var serverIdsToRemove = new UInt32Collection();
            var clientHandlesToRemove = new UInt32Collection();
            if (linksToRemove != null)
            {
                foreach (MonitoredItem item in linksToRemove)
                {
                    if (!item.Status.Created)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            $"Monitored item '{item.DisplayName}' has not been created on the server.");
                    }
                    serverIdsToRemove.Add(item.Status.Id);
                    clientHandlesToRemove.Add(item.ClientHandle);
                }
            }

            // Call the Session SetTriggering method
            SetTriggeringResponse response = await Session.SetTriggeringAsync(
                null,
                Id,
                triggeringItem.Status.Id,
                serverIdsToAdd,
                serverIdsToRemove,
                ct).ConfigureAwait(false);

            // Update the triggering relationships for automatic restoration
            lock (m_cache)
            {
                // Initialize the triggered items collection if needed
                triggeringItem.TriggeredItems ??= [];

                // Add new links
                if (clientHandlesToAdd.Count > 0)
                {
                    foreach (uint clientHandle in clientHandlesToAdd)
                    {
                        if (!triggeringItem.TriggeredItems.Contains(clientHandle))
                        {
                            triggeringItem.TriggeredItems.Add(clientHandle);
                        }

                        // Update the triggered item to remember its triggering item
                        if (m_monitoredItems.TryGetValue(clientHandle, out MonitoredItem? triggeredItem))
                        {
                            triggeredItem.TriggeringItemId = triggeringItem.Status.Id;
                        }
                    }
                }

                // Remove links
                if (clientHandlesToRemove.Count > 0)
                {
                    foreach (uint clientHandle in clientHandlesToRemove)
                    {
                        triggeringItem.TriggeredItems.Remove(clientHandle);

                        // Clear the triggering item reference
                        if (m_monitoredItems.TryGetValue(clientHandle, out MonitoredItem? triggeredItem))
                        {
                            triggeredItem.TriggeringItemId = 0;
                        }
                    }
                }
            }

            return response;
        }

        /// <summary>
        /// Tells the server to refresh all conditions being monitored by the subscription.
        /// </summary>
        public async Task<bool> ConditionRefreshAsync(CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            var methodsToCall = new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = ObjectTypeIds.ConditionType,
                    MethodId = MethodIds.ConditionType_ConditionRefresh,
                    InputArguments = [new Variant(Id)]
                }
            };

            try
            {
                await Session.CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
                return true;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        /// <summary>
        /// Tells the server to refresh all conditions being monitored by the subscription for a specific
        /// monitoredItem for events.
        /// </summary>
        public async Task<bool> ConditionRefresh2Async(
            uint monitoredItemId,
            CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);

            var methodsToCall = new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = ObjectTypeIds.ConditionType,
                    MethodId = MethodIds.ConditionType_ConditionRefresh2,
                    InputArguments = [new Variant(Id), new Variant(monitoredItemId)]
                }
            };

            try
            {
                await Session.CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
                return true;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        /// <summary>
        /// Called after the subscription was transferred.
        /// </summary>
        /// <param name="session">The session to which the subscription is transferred.</param>
        /// <param name="id">Id of the transferred subscription.</param>
        /// <param name="availableSequenceNumbers">The available sequence numbers on the server.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<bool> TransferAsync(
            ISession session,
            uint id,
            UInt32Collection availableSequenceNumbers,
            CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            if (Created)
            {
                // handle the case when the client has the subscription template and reconnects
                if (id != Id)
                {
                    return false;
                }

                // remove the subscription from disconnected session
                if (Session?.RemoveTransferredSubscription(this) != true)
                {
                    m_logger.LogError(
                        "SubscriptionId {SubscriptionId}: Failed to remove transferred subscription from owner SessionId={SessionId}.",
                        Id,
                        Session?.SessionId);
                    return false;
                }

                // remove default subscription template which was copied in Session.Create()
                var subscriptionsToRemove = session.Subscriptions
                    .Where(s => !s.Created && s.TransferId == Id)
                    .ToList();
                await session.RemoveSubscriptionsAsync(subscriptionsToRemove, ct)
                    .ConfigureAwait(false);

                // add transferred subscription to session
                if (!session.AddSubscription(this))
                {
                    m_logger.LogError(
                        "SubscriptionId {SubscriptionId}: Failed to add transferred subscription to SessionId={SessionId}.",
                        Id,
                        session.SessionId);
                    return false;
                }
            }
            else
            {
                // handle the case when the client restarts and loads the saved subscriptions from storage
                bool success;
                UInt32Collection serverHandles;
                UInt32Collection clientHandles;
                (success, serverHandles, clientHandles) = await GetMonitoredItemsAsync(ct)
                    .ConfigureAwait(false);
                if (!success)
                {
                    m_logger.LogError(
                        "SubscriptionId {SubscriptionId}: The server failed to respond to GetMonitoredItems after transfer.",
                        Id);
                    return false;
                }

                int monitoredItemsCount = m_monitoredItems.Count;
                if (serverHandles.Count != monitoredItemsCount ||
                    clientHandles.Count != monitoredItemsCount)
                {
                    // invalid state
                    m_logger.LogError(
                        "SubscriptionId {SubscriptionId}: Number of Monitored Items on client and server do not match after transfer {Previous}!={New}",
                        Id,
                        serverHandles.Count,
                        monitoredItemsCount);
                    return false;
                }

                // sets state to 'Created'
                Id = id;
                TransferItems(serverHandles, clientHandles, out IList<MonitoredItem> itemsToModify);

                await ModifyItemsAsync(ct).ConfigureAwait(false);
            }

            // add available sequence numbers to incoming
            ProcessTransferredSequenceNumbers(availableSequenceNumbers);

            m_changeMask |= SubscriptionChangeMask.Transferred;
            ChangesCompleted();

            // Restore triggering relationships after subscription transfer
            await RestoreTriggeringAsync(ct).ConfigureAwait(false);

            StartKeepAliveTimer();

            TraceState("TRANSFERRED ASYNC");

            return true;
        }

        /// <summary>
        /// Adds the notification message to internal cache.
        /// </summary>
        public void SaveMessageInCache(
            IList<uint>? availableSequenceNumbers,
            NotificationMessage message)
        {
            PublishStateChangedEventHandler? callback = null;

            lock (m_cache)
            {
                if (availableSequenceNumbers != null)
                {
                    m_availableSequenceNumbers = availableSequenceNumbers;
                }

                if (message == null)
                {
                    return;
                }

                // check if a publish error was previously reported.
                if (PublishingStopped)
                {
                    callback = m_PublishStatusChanged;
                    TraceState("PUBLISHING RECOVERED");
                }

                DateTime now = DateTime.UtcNow;
                Interlocked.Exchange(ref m_lastNotificationTime, now.Ticks);

                int tickCount = HiResClock.TickCount;
                m_lastNotificationTickCount = tickCount;

                // create queue for the first time.
                m_incomingMessages ??= new LinkedList<IncomingMessage>();

                // find or create an entry for the incoming sequence number.
                IncomingMessage entry = FindOrCreateEntry(now, tickCount, message.SequenceNumber);

                // check for keep alive.
                if (message.NotificationData.Count > 0)
                {
                    entry.Message = message;
                    entry.Processed = false;
                }

                // fill in any gaps in the queue
                LinkedListNode<IncomingMessage>? node = m_incomingMessages.First;

                while (node != null)
                {
                    entry = node.Value;
                    LinkedListNode<IncomingMessage>? next = node.Next;

                    if (next != null && next.Value.SequenceNumber > entry.SequenceNumber + 1)
                    {
                        var placeholder = new IncomingMessage
                        {
                            SequenceNumber = entry.SequenceNumber + 1,
                            Timestamp = now,
                            TickCount = tickCount
                        };
                        node = m_incomingMessages.AddAfter(node, placeholder);
                        continue;
                    }

                    node = next;
                }

                // clean out processed values.
                node = m_incomingMessages.First;

                while (node != null)
                {
                    entry = node.Value;
                    LinkedListNode<IncomingMessage>? next = node.Next;

                    // can only pull off processed or expired or missing messages.
                    if (!entry.Processed &&
                        !(
                            entry.Republished &&
                            (
                                entry.RepublishStatus != StatusCodes.Good ||
                                (tickCount - entry.TickCount) > kRepublishMessageExpiredTimeout)))
                    {
                        break;
                    }

                    if (next != null)
                    {
                        //If the message being removed is supposed to be the next message, advance it to release anything waiting on it to be processed
                        if (entry.SequenceNumber == m_lastSequenceNumberProcessed + 1)
                        {
                            if (!entry.Processed)
                            {
                                m_logger.LogWarning(
                                    "SubscriptionId {SubscriptionId} skipping PublishResponse Sequence Number {SequenceNumber}",
                                    Id,
                                    entry.SequenceNumber);
                            }

                            m_lastSequenceNumberProcessed = entry.SequenceNumber;
                        }

                        m_incomingMessages.Remove(node);
                    }

                    node = next;
                }
            }

            // send notification that publishing received a keep alive or has to republish.
            PublishingStateChanged(callback, PublishStateChangedMask.Recovered);

            // process messages.
            m_messageWorkerEvent.Set();
        }

        /// <summary>
        /// Get the number of outstanding message workers
        /// </summary>
        public int OutstandingMessageWorkers => m_outstandingMessageWorkers;

        /// <summary>
        /// Adds an item to the subscription.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="monitoredItem"/> is <c>null</c>.</exception>
        public void AddItem(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            lock (m_cache)
            {
                if (!m_monitoredItems.TryAdd(monitoredItem.ClientHandle, monitoredItem))
                {
                    return;
                }
                monitoredItem.Subscription = this;
            }

            m_changeMask |= SubscriptionChangeMask.ItemsAdded;
            ChangesCompleted();
        }

        /// <summary>
        /// Adds items to the subscription.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="monitoredItems"/> is <c>null</c>.</exception>
        public void AddItems(IEnumerable<MonitoredItem> monitoredItems)
        {
            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            bool added = false;

            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in monitoredItems)
                {
                    if (m_monitoredItems.TryAdd(monitoredItem.ClientHandle, monitoredItem))
                    {
                        monitoredItem.Subscription = this;
                        added = true;
                    }
                }
            }

            if (added)
            {
                m_changeMask |= SubscriptionChangeMask.ItemsAdded;
                ChangesCompleted();
            }
        }

        /// <summary>
        /// Removes an item from the subscription.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="monitoredItem"/> is <c>null</c>.</exception>
        public void RemoveItem(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            lock (m_cache)
            {
                if (!m_monitoredItems.TryRemove(monitoredItem.ClientHandle, out _))
                {
                    return;
                }

                monitoredItem.Subscription = null;
            }

            if (monitoredItem.Status.Created)
            {
                m_deletedItems.Add(monitoredItem);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsRemoved;
            ChangesCompleted();
        }

        /// <summary>
        /// Removes items from the subscription.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="monitoredItems"/> is <c>null</c>.</exception>
        public void RemoveItems(IEnumerable<MonitoredItem> monitoredItems)
        {
            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            bool changed = false;

            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in monitoredItems)
                {
                    if (m_monitoredItems.TryRemove(monitoredItem.ClientHandle, out _))
                    {
                        monitoredItem.Subscription = null;

                        if (monitoredItem.Status.Created)
                        {
                            m_deletedItems.Add(monitoredItem);
                        }

                        changed = true;
                    }
                }
            }

            if (changed)
            {
                m_changeMask |= SubscriptionChangeMask.ItemsRemoved;
                ChangesCompleted();
            }
        }

        /// <summary>
        /// Returns the monitored item identified by the client handle.
        /// </summary>
        public MonitoredItem? FindItemByClientHandle(uint clientHandle)
        {
            lock (m_cache)
            {
                if (m_monitoredItems.TryGetValue(clientHandle, out MonitoredItem? monitoredItem))
                {
                    return monitoredItem;
                }

                return null;
            }
        }

        /// <summary>
        /// Call the ResendData method on the server for this subscription.
        /// </summary>
        public async Task<bool> ResendDataAsync(CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySessionAndSubscriptionState(true);
            try
            {
                await Session.CallAsync(ObjectIds.Server, MethodIds.Server_ResendData, ct, Id)
                    .ConfigureAwait(false);
                return true;
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogError(sre, "SubscriptionId {SubscriptionId}: Failed to call ResendData on server", Id);
            }
            return false;
        }

        /// <summary>
        /// Call the GetMonitoredItems method on the server.
        /// </summary>
        public async Task<(
            bool,
            UInt32Collection,
            UInt32Collection
            )> GetMonitoredItemsAsync(CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            VerifySession();
            var serverHandles = new UInt32Collection();
            var clientHandles = new UInt32Collection();
            try
            {
                VariantCollection outputArguments = await Session.CallAsync(
                    ObjectIds.Server,
                    MethodIds.Server_GetMonitoredItems,
                    ct,
                    TransferId).ConfigureAwait(false);
                if (outputArguments != null && outputArguments.Count == 2)
                {
                    serverHandles.AddRange((uint[])outputArguments[0]);
                    clientHandles.AddRange((uint[])outputArguments[1]);
                    return (true, serverHandles, clientHandles);
                }
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogError(
                    sre,
                    "SubscriptionId {SubscriptionId}: Failed to call GetMonitoredItems on server",
                    Id);
            }
            return (false, serverHandles, clientHandles);
        }

        /// <summary>
        /// Set the subscription to durable.
        /// </summary>
        public async Task<(bool, uint)> SetSubscriptionDurableAsync(
            uint lifetimeInHours,
            CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            uint revisedLifetimeInHours = lifetimeInHours;
            VerifySession();

            try
            {
                VariantCollection outputArguments = await Session
                    .CallAsync(
                        ObjectIds.Server,
                        MethodIds.Server_SetSubscriptionDurable,
                        ct,
                        Id,
                        lifetimeInHours)
                    .ConfigureAwait(false);

                if (outputArguments != null && outputArguments.Count == 1)
                {
                    revisedLifetimeInHours = (uint)outputArguments[0];
                    return (true, revisedLifetimeInHours);
                }
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogError(
                    sre,
                    "SubscriptionId {SubscriptionId}: Failed to call SetSubscriptionDurable on server",
                    Id);
            }

            return (false, revisedLifetimeInHours);
        }

        /// <summary>
        /// Updates the available sequence numbers and queues after transfer.
        /// </summary>
        /// <remarks>
        /// If <see cref="RepublishAfterTransfer"/> is set to <c>true</c>, sequence numbers
        /// are queued for republish, otherwise ack may be sent.
        /// </remarks>
        /// <param name="availableSequenceNumbers">The list of available sequence
        /// numbers on the server.</param>
        private void ProcessTransferredSequenceNumbers(UInt32Collection availableSequenceNumbers)
        {
            lock (m_cache)
            {
                // reset incoming state machine and clear cache
                m_lastSequenceNumberProcessed = 0;
                m_resyncLastSequenceNumberProcessed = true;

                // save available sequence numbers
                m_availableSequenceNumbers = (UInt32Collection)availableSequenceNumbers
                    .MemberwiseClone();

                if (availableSequenceNumbers.Count != 0 && RepublishAfterTransfer)
                {
                    // create queue for the first time.
                    m_incomingMessages ??= new LinkedList<IncomingMessage>();

                    // update last sequence number processed
                    // available seq numbers may not be in order
                    foreach (uint sequenceNumber in availableSequenceNumbers)
                    {
                        if (sequenceNumber >= m_lastSequenceNumberProcessed)
                        {
                            m_lastSequenceNumberProcessed = sequenceNumber + 1;
                        }
                    }

                    // only republish consecutive sequence numbers
                    // triggers the republish mechanism immediately,
                    // if event is in the past
                    DateTime now = DateTime.UtcNow.AddMilliseconds(-kRepublishMessageTimeout * 2);
                    int tickCount = HiResClock.TickCount - (kRepublishMessageTimeout * 2);
                    uint lastSequenceNumberToRepublish = m_lastSequenceNumberProcessed - 1;
                    int availableNumbers = availableSequenceNumbers.Count;
                    int republishMessages = 0;
                    for (int i = 0; i < availableNumbers; i++)
                    {
                        bool found = false;
                        foreach (uint sequenceNumber in availableSequenceNumbers)
                        {
                            if (lastSequenceNumberToRepublish == sequenceNumber)
                            {
                                FindOrCreateEntry(now, tickCount, sequenceNumber);
                                found = true;
                                break;
                            }
                        }

                        if (found)
                        {
                            // remove sequence number handled for republish
                            availableSequenceNumbers.Remove(lastSequenceNumberToRepublish);
                            lastSequenceNumberToRepublish--;
                            republishMessages++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    m_logger.LogInformation(
                        "SubscriptionId {SubscriptionId}: Republishing {Count} messages, next sequencenumber {SequenceNumber} after transfer.",
                        Id,
                        republishMessages,
                        m_lastSequenceNumberProcessed);

                    availableSequenceNumbers.Clear();
                }
            }
        }

        /// <summary>
        /// Starts a timer to ensure publish requests are sent frequently enough
        /// to detect network interruptions.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void StartKeepAliveTimer()
        {
            // stop the publish timer.
            lock (m_cache)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(Subscription),
                        "Starting keep alive timer on disposed subscription");
                }
                bool startPublishing = false;
                int oldKeepAliveInterval = m_keepAliveInterval;
                m_keepAliveInterval = CalculateKeepAliveInterval();

                // don`t create new KeepAliveTimer if interval did not change and timers are still running
                if (oldKeepAliveInterval != m_keepAliveInterval || m_publishTimer == null)
                {
                    Utils.SilentDispose(m_publishTimer);
                    m_publishTimer = null;
                    Interlocked.Exchange(ref m_lastNotificationTime, DateTime.UtcNow.Ticks);
                    m_lastNotificationTickCount = HiResClock.TickCount;
                    m_publishTimer = new Timer(
                        OnKeepAlive,
                        m_keepAliveInterval,
                        m_keepAliveInterval,
                        m_keepAliveInterval);
                    startPublishing = true;
                }

                if (m_messageWorkerTask == null || m_messageWorkerTask.IsCompleted)
                {
                    Utils.SilentDispose(m_messageWorkerCts);
                    m_messageWorkerCts = new CancellationTokenSource();
                    CancellationToken ct = m_messageWorkerCts.Token;
                    m_messageWorkerTask = Task
                        .Factory.StartNew(
                            () => PublishResponseMessageWorkerAsync(ct),
                            ct,
                            TaskCreationOptions.LongRunning,
                            TaskScheduler.Default)
                        .Unwrap();
                    startPublishing = true;
                }

                if (!startPublishing)
                {
                    return;
                }
            }

            // start publishing. Fill the queue.
            Session?.StartPublishing(BeginPublishTimeout(), false);
        }

        /// <summary>
        /// Checks if a notification has arrived. Sends a publish if it has not.
        /// </summary>
        private void OnKeepAlive(object? state)
        {
            if (!PublishingStopped)
            {
                return;
            }

            HandleOnKeepAliveStopped();
        }

        /// <summary>
        /// Handles callback if publishing stopped. Sends a publish.
        /// </summary>
        private void HandleOnKeepAliveStopped()
        {
            // check if a publish has arrived.
            PublishStateChangedEventHandler? callback = m_PublishStatusChanged;
            ISession? session = Session;

            Interlocked.Increment(ref m_publishLateCount);

            if (session != null &&
                session.Connected &&
                !session.Reconnecting)
            {
                TraceState("PUBLISHING STOPPED");

                PublishingStateChanged(callback,
                    PublishStateChangedMask.Stopped);

                // try to send a publish to recover stopped publishing.
                session.BeginPublish(BeginPublishTimeout());
            }
            else
            {
                PublishingStateChanged(callback,
                    PublishStateChangedMask.Stopped |
                    PublishStateChangedMask.SessionNotConnected);
            }
        }

        /// <summary>
        /// Publish response worker task for the subscriptions.
        /// </summary>
        private async Task PublishResponseMessageWorkerAsync(CancellationToken ct)
        {
            m_logger.LogTrace(
                "SubscriptionId {SubscriptionId} - Publish Task {TaskId:X8} Started.",
                Id,
                Task.CurrentId);

            try
            {
                while (!ct.IsCancellationRequested && !m_disposed)
                {
                    await m_messageWorkerEvent.WaitAsync(ct).ConfigureAwait(false);
                    await OnMessageReceivedAsync(ct).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
                // intentionally fall through
            }
            catch (OperationCanceledException)
            {
                // intentionally fall through
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "SubscriptionId {SubscriptionId} - Publish Worker Task {TaskId:X8} Exited Unexpectedly.",
                    Id,
                    Task.CurrentId);
                return;
            }

            m_logger.LogTrace(
                "SubscriptionId {SubscriptionId} - Publish Task {TaskId:X8} Exited Normally.",
                Id,
                Task.CurrentId);
        }

        /// <summary>
        /// Dumps the current state of the session queue.
        /// </summary>
        internal void TraceState(string context)
        {
            CoreClientUtils.EventLog.SubscriptionState(
                context,
                Id,
                new DateTime(m_lastNotificationTime),
                Session?.GoodPublishRequestCount ?? 0,
                CurrentPublishingInterval,
                CurrentKeepAliveCount,
                CurrentPublishingEnabled,
                MonitoredItemCount);

            m_logger.LogInformation(
                "Subscription {Context}, Id={SubscriptionId}, LastNotificationTime={LastNotificationTime:HH:mm:ss}, GoodPublishRequestCount={GoodPublishRequestCount}, PublishingInterval={PublishingInterval}, KeepAliveCount={KeepAliveCount}, PublishingEnabled={PublishingEnabled}, MonitoredItemCount={MonitoredItemCount}",
                context,
                Id,
                new DateTime(m_lastNotificationTime),
                Session?.GoodPublishRequestCount ?? 0,
                CurrentPublishingInterval,
                CurrentKeepAliveCount,
                CurrentPublishingEnabled,
                MonitoredItemCount);
        }

        /// <summary>
        /// Calculate the timeout of a publish request.
        /// </summary>
        private int BeginPublishTimeout()
        {
            return Math.Max(
                Math.Min(m_keepAliveInterval * 3, int.MaxValue),
                kMinKeepAliveTimerInterval);
        }

        /// <summary>
        /// Update the subscription with the given revised settings.
        /// </summary>
        private void ModifySubscription(
            double revisedPublishingInterval,
            uint revisedKeepAliveCount,
            uint revisedLifetimeCounter)
        {
            CreateOrModifySubscription(
                false,
                0,
                revisedPublishingInterval,
                revisedKeepAliveCount,
                revisedLifetimeCounter);
        }

        /// <summary>
        /// Update the subscription with the given revised settings.
        /// </summary>
        private void CreateSubscription(
            uint subscriptionId,
            double revisedPublishingInterval,
            uint revisedKeepAliveCount,
            uint revisedLifetimeCounter)
        {
            CreateOrModifySubscription(
                true,
                subscriptionId,
                revisedPublishingInterval,
                revisedKeepAliveCount,
                revisedLifetimeCounter);
        }

        /// <summary>
        /// Update the subscription with the given revised settings.
        /// </summary>
        private void CreateOrModifySubscription(
            bool created,
            uint subscriptionId,
            double revisedPublishingInterval,
            uint revisedKeepAliveCount,
            uint revisedLifetimeCounter)
        {
            // update current state.
            CurrentPublishingInterval = revisedPublishingInterval;
            CurrentKeepAliveCount = revisedKeepAliveCount;
            CurrentLifetimeCount = revisedLifetimeCounter;
            CurrentPriority = Priority;

            if (!created)
            {
                m_changeMask |= SubscriptionChangeMask.Modified;
            }
            else
            {
                CurrentPublishingEnabled = PublishingEnabled;
                TransferId = Id = subscriptionId;
                m_changeMask |= SubscriptionChangeMask.Created;
            }

            StartKeepAliveTimer();

            if (KeepAliveCount != revisedKeepAliveCount)
            {
                m_logger.LogInformation(
                    "For subscription {SubscriptionId}, Keep alive count was revised from {Previous} to {New}",
                    Id,
                    KeepAliveCount,
                    revisedKeepAliveCount);
            }

            if (LifetimeCount != revisedLifetimeCounter)
            {
                m_logger.LogInformation(
                    "For subscription {SubscriptionId}, Lifetime count was revised from {Previous} to {New}",
                    Id,
                    LifetimeCount,
                    revisedLifetimeCounter);
            }

            if (PublishingInterval != revisedPublishingInterval)
            {
                m_logger.LogInformation(
                    "For subscription {SubscriptionId}, Publishing interval was revised from {Previous} to {New}",
                    Id,
                    PublishingInterval,
                    revisedPublishingInterval);
            }

            if (revisedLifetimeCounter < revisedKeepAliveCount * 3)
            {
                m_logger.LogInformation(
                    "For subscription {SubscriptionId}, Revised lifetime counter (value={LifetimeCounter}) is less than three times the keep alive count (value={KeepAliveCount})",
                    Id,
                    revisedLifetimeCounter,
                    revisedKeepAliveCount);
            }

            if (CurrentPriority == 0)
            {
                m_logger.LogInformation("For subscription {SubscriptionId}, the priority was set to 0.", Id);
            }
        }

        /// <summary>
        /// Calculate the KeepAliveInterval based on <see cref="CurrentPublishingInterval"/> and <see cref="CurrentKeepAliveCount"/>
        /// </summary>
        private int CalculateKeepAliveInterval()
        {
            int keepAliveInterval = (int)
                Math.Min(CurrentPublishingInterval * (CurrentKeepAliveCount + 1), int.MaxValue);
            if (keepAliveInterval < kMinKeepAliveTimerInterval)
            {
                keepAliveInterval = (int)Math.Min(
                    PublishingInterval * (KeepAliveCount + 1),
                    int.MaxValue);
                keepAliveInterval = Math.Max(kMinKeepAliveTimerInterval, keepAliveInterval);
            }
            return keepAliveInterval;
        }

        /// <summary>
        /// Delete the subscription.
        /// Ignore errors, always reset all parameter.
        /// </summary>
        private void DeleteSubscription()
        {
            TransferId = Id = 0;
            CurrentPublishingInterval = 0;
            CurrentKeepAliveCount = 0;
            CurrentPublishingEnabled = false;
            CurrentPriority = 0;

            // update items.
            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    monitoredItem.SetDeleteResult(StatusCodes.Good, -1, null, null);
                }
            }

            m_deletedItems.Clear();

            m_changeMask |= SubscriptionChangeMask.Deleted;
        }

        /// <summary>
        /// Ensures sensible values for the counts.
        /// </summary>
        private void AdjustCounts(double sessionTimeout, ref uint keepAliveCount, ref uint lifetimeCount)
        {
            const uint kDefaultKeepAlive = 10;
            const uint kDefaultLifeTime = 1000;

            // keep alive count must be at least 1, 10 is a good default.
            if (keepAliveCount == 0)
            {
                m_logger.LogInformation(
                    "Adjusted KeepAliveCount from value={Previous} to value={New}, for subscription {SubscriptionId}.",
                    keepAliveCount,
                    kDefaultKeepAlive,
                    Id);
                keepAliveCount = kDefaultKeepAlive;
            }

            // ensure the lifetime is sensible given the sampling interval.
            if (PublishingInterval > 0)
            {
                if (MinLifetimeInterval > 0 && MinLifetimeInterval < sessionTimeout)
                {
                    m_logger.LogWarning(
                        "A smaller minLifetimeInterval {LifetimeInterval}ms than session timeout {SessionTimeout}ms configured for subscription {SubscriptionId}.",
                        MinLifetimeInterval,
                        sessionTimeout,
                        Id);
                }

                uint minLifetimeCount = (uint)(MinLifetimeInterval / PublishingInterval);

                if (lifetimeCount < minLifetimeCount)
                {
                    lifetimeCount = minLifetimeCount;

                    if (MinLifetimeInterval % PublishingInterval != 0)
                    {
                        lifetimeCount++;
                    }

                    m_logger.LogInformation(
                        "Adjusted LifetimeCount to value={New}, for subscription {SubscriptionId}. ",
                        lifetimeCount,
                        Id);
                }

                if (lifetimeCount * PublishingInterval < sessionTimeout)
                {
                    m_logger.LogWarning(
                        "Lifetime {LifetimeCount}ms configured for subscription {SubscriptionId} is less than session timeout {SessionTimeout}ms.",
                        lifetimeCount * PublishingInterval,
                        Id,
                        sessionTimeout);
                }
            }
            else if (lifetimeCount == 0)
            {
                // don't know what the sampling interval will be - use something large enough
                // to ensure the user does not experience unexpected drop outs.
                m_logger.LogInformation(
                    "Adjusted LifetimeCount from value={Previous}, to value={New}, for subscription {SubscriptionId}. ",
                    lifetimeCount,
                    kDefaultLifeTime,
                    Id);
                lifetimeCount = kDefaultLifeTime;
            }

            // validate spec: lifetimecount shall be at least 3*keepAliveCount
            uint minLifeTimeCount = 3 * keepAliveCount;
            if (lifetimeCount < minLifeTimeCount)
            {
                m_logger.LogInformation(
                    "Adjusted LifetimeCount from value={Previous}, to value={New}, for subscription {SubscriptionId}. ",
                    lifetimeCount,
                    minLifeTimeCount,
                    Id);
                lifetimeCount = minLifeTimeCount;
            }
        }

        /// <summary>
        /// Processes the incoming messages.
        /// </summary>
        private async Task OnMessageReceivedAsync(CancellationToken ct)
        {
            try
            {
                Interlocked.Increment(ref m_outstandingMessageWorkers);

                ISession? session = null;
                uint subscriptionId = 0;
                PublishStateChangedEventHandler? callback = null;

                // list of new messages to process.
                List<NotificationMessage>? messagesToProcess = null;

                // list of keep alive messages to process.
                List<IncomingMessage>? keepAliveToProcess = null;

                // list of new messages to republish.
                List<IncomingMessage>? messagesToRepublish = null;

                PublishStateChangedMask publishStateChangedMask = PublishStateChangedMask.None;

                lock (m_cache)
                {
                    if (m_incomingMessages == null)
                    {
                        return;
                    }

                    for (LinkedListNode<IncomingMessage>? ii = m_incomingMessages.First;
                        ii != null;
                        ii = ii.Next)
                    {
                        // update monitored items with unprocessed messages.
                        if (ii.Value.Message != null &&
                            !ii.Value.Processed &&
                            (!State.SequentialPublishing || ValidSequentialPublishMessage(ii.Value)))
                        {
                            (messagesToProcess ??= []).Add(ii.Value.Message);

                            // remove the oldest items.
                            while (m_messageCache.Count > MaxMessageCount)
                            {
                                m_messageCache.RemoveFirst();
                            }

                            m_messageCache.AddLast(ii.Value.Message);
                            ii.Value.Processed = true;

                            // Keep the last sequence number processed going up
                            if (ii.Value.SequenceNumber > m_lastSequenceNumberProcessed ||
                                (ii.Value.SequenceNumber == 1 &&
                                    m_lastSequenceNumberProcessed == uint.MaxValue))
                            {
                                m_lastSequenceNumberProcessed = ii.Value.SequenceNumber;
                                if (m_resyncLastSequenceNumberProcessed)
                                {
                                    m_logger.LogInformation(
                                        "SubscriptionId {SubscriptionId}: Resynced last sequence number processed to {SequenceNumber}.",
                                        Id,
                                        m_lastSequenceNumberProcessed);
                                    m_resyncLastSequenceNumberProcessed = false;
                                }
                            }
                        }
                        // process keep alive messages
                        else if (ii.Next == null && ii.Value.Message == null && !ii.Value.Processed)
                        {
                            (keepAliveToProcess ??= []).Add(ii.Value);
                            publishStateChangedMask |= PublishStateChangedMask.KeepAlive;
                        }
                        // check for missing messages.
                        else if (ii.Next != null &&
                            ii.Value.Message == null &&
                            !ii.Value.Processed &&
                            !ii.Value.Republished)
                        {
                            // tolerate if a single request was received out of order
                            if (ii.Next.Next != null &&
                                (HiResClock.TickCount -
                                    ii.Value.TickCount) > kRepublishMessageTimeout)
                            {
                                ii.Value.Republished = true;
                                publishStateChangedMask |= PublishStateChangedMask.Republish;

                                // only call republish if the sequence number is available
                                if (m_availableSequenceNumbers?.Contains(
                                    ii.Value.SequenceNumber) == true)
                                {
                                    (messagesToRepublish ??= []).Add(ii.Value);
                                }
                                else
                                {
                                    m_logger.LogInformation(
                                        "Skipped to receive RepublishAsync for subscription {SubscriptionId}-{SequenceNumber}-BadMessageNotAvailable",
                                        subscriptionId,
                                        ii.Value.SequenceNumber);
                                    ii.Value.RepublishStatus = StatusCodes.BadMessageNotAvailable;
                                }
                            }
                        }
#if DEBUG
                        // a message that is deferred because of a missing sequence number
                        else if (ii.Value.Message != null && !ii.Value.Processed)
                        {
                            m_logger.LogDebug(
                                "Subscription {SubscriptionId}: Delayed message with sequence number {SequenceNumber}, " +
                                "expected sequence number is {ExpectedSequenceNumber}.",
                                Id,
                                ii.Value.SequenceNumber,
                                m_lastSequenceNumberProcessed + 1);
                        }
#endif
                    }

                    session = Session;
                    subscriptionId = Id;
                    callback = m_PublishStatusChanged;
                }

                // process new keep alive messages.
                FastKeepAliveNotificationEventHandler? keepAliveCallback = FastKeepAliveCallback;
                if (keepAliveToProcess != null && keepAliveCallback != null)
                {
                    foreach (IncomingMessage message in keepAliveToProcess)
                    {
                        var keepAlive = new NotificationData
                        {
                            PublishTime = message.Timestamp,
                            SequenceNumber = message.SequenceNumber
                        };
                        keepAliveCallback(this, keepAlive);
                    }
                }

                // process new messages.
                if (messagesToProcess != null)
                {
                    int noNotificationsReceived;
                    FastDataChangeNotificationEventHandler? datachangeCallback
                        = FastDataChangeCallback;
                    FastEventNotificationEventHandler? eventCallback = FastEventCallback;

                    foreach (NotificationMessage message in messagesToProcess)
                    {
                        noNotificationsReceived = 0;
                        try
                        {
                            foreach (ExtensionObject notificationData in message.NotificationData)
                            {
                                if (notificationData.Body is DataChangeNotification datachange)
                                {
                                    datachange.PublishTime = message.PublishTime;
                                    datachange.SequenceNumber = message.SequenceNumber;
                                    datachange.MoreNotifications = message.MoreNotifications;

                                    noNotificationsReceived += datachange.MonitoredItems.Count;

                                    if (!DisableMonitoredItemCache)
                                    {
                                        SaveDataChange(message, datachange, message.StringTable);
                                    }

                                    datachangeCallback?.Invoke(
                                        this,
                                        datachange,
                                        message.StringTable);
                                }
                                else if (notificationData.Body is EventNotificationList events)
                                {
                                    events.PublishTime = message.PublishTime;
                                    events.SequenceNumber = message.SequenceNumber;
                                    events.MoreNotifications = message.MoreNotifications;

                                    noNotificationsReceived += events.Events.Count;

                                    if (!DisableMonitoredItemCache)
                                    {
                                        SaveEvents(message, events, message.StringTable);
                                    }

                                    eventCallback?.Invoke(this, events, message.StringTable);
                                }
                                else if (notificationData
                                    .Body is StatusChangeNotification statusChanged)
                                {
                                    statusChanged.PublishTime = message.PublishTime;
                                    statusChanged.SequenceNumber = message.SequenceNumber;
                                    statusChanged.MoreNotifications = message.MoreNotifications;

                                    m_logger.LogWarning(
                                        "StatusChangeNotification received with Status = {Status} for SubscriptionId={SubscriptionId}:.",
                                        statusChanged.Status.ToString(),
                                        Id);

                                    if (statusChanged.Status == StatusCodes
                                        .GoodSubscriptionTransferred)
                                    {
                                        publishStateChangedMask
                                            |= PublishStateChangedMask.Transferred;

                                        _ = ResetPublishTimerAndWorkerStateAsync(); // Do not block on ourselves but exit
                                    }
                                    else if (statusChanged.Status == StatusCodes.BadTimeout)
                                    {
                                        publishStateChangedMask |= PublishStateChangedMask.Timeout;
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            m_logger.LogError(
                                e,
                                "Error while processing incoming message #{SequenceNumber}.",
                                message.SequenceNumber);
                        }

                        if (MaxNotificationsPerPublish != 0 &&
                            noNotificationsReceived > MaxNotificationsPerPublish)
                        {
                            m_logger.LogWarning(
                                "For subscription {SubscriptionId}, more notifications were received={Count} than the max notifications per publish value={MaxNotificationsPerPublish}",
                                Id,
                                noNotificationsReceived,
                                MaxNotificationsPerPublish);
                        }
                    }

                    if (publishStateChangedMask != PublishStateChangedMask.None)
                    {
                        PublishingStateChanged(callback, publishStateChangedMask);
                    }
                }

                // do any re-publishes.
                if (messagesToRepublish != null && session != null && subscriptionId != 0)
                {
                    int count = messagesToRepublish.Count;
                    var tasks = new Task<(bool, ServiceResult)>[count];
                    for (int ii = 0; ii < count; ii++)
                    {
                        tasks[ii] = session.RepublishAsync(
                            subscriptionId,
                            messagesToRepublish[ii].SequenceNumber,
                            ct);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    lock (m_cache)
                    {
                        for (int ii = 0; ii < count; ii++)
                        {
                            bool success = false;
                            ServiceResult serviceResult = StatusCodes.BadMessageNotAvailable;
                            if (tasks[ii].IsCompleted)
                            {
                                (success, serviceResult) = tasks[ii].Result.ToTuple();
                            }
                            messagesToRepublish[ii].Republished = success;
                            messagesToRepublish[ii].RepublishStatus = serviceResult.StatusCode;
                        }
                    }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                m_logger.LogError(e, "Error while processing incoming messages.");
            }
            finally
            {
                Interlocked.Decrement(ref m_outstandingMessageWorkers);
            }
        }

        /// <summary>
        /// Throws an exception if the subscription is not in the correct state.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [MemberNotNull(nameof(Session))]
        private void VerifySessionAndSubscriptionState(bool verifyCreated)
        {
            if (verifyCreated && Id == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Subscription has not been created.");
            }

            if (!verifyCreated && Id != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Subscription has already been created.");
            }

            VerifySession();
        }

        /// <summary>
        /// Verify session is assigned.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [MemberNotNull(nameof(Session))]
        private void VerifySession()
        {
            if (Session is null) // Occurs only on Create() and CreateAsync()
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Subscription has not been assigned to a Session");
            }
        }

        /// <summary>
        /// Validates the sequence number of the incoming publish request.
        /// </summary>
        private bool ValidSequentialPublishMessage(IncomingMessage message)
        {
            // If sequential publishing is enabled, only release messages in perfect sequence.
            return message.SequenceNumber <= m_lastSequenceNumberProcessed + 1 ||
                // reconnect / transfer subscription case
                m_resyncLastSequenceNumberProcessed ||
                // release the first message after wrapping around.
                (message.SequenceNumber == 1 && m_lastSequenceNumberProcessed == uint.MaxValue);
        }

        /// <summary>
        /// Update the results to monitored items
        /// after updating the monitoring mode.
        /// </summary>
        private static bool UpdateMonitoringMode(
            IList<MonitoredItem> monitoredItems,
            List<ServiceResult?> errors,
            StatusCodeCollection results,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader,
            MonitoringMode monitoringMode)
        {
            // update results.
            bool noErrors = true;

            for (int ii = 0; ii < results.Count; ii++)
            {
                ServiceResult? error = null;

                if (StatusCode.IsBad(results[ii]))
                {
                    error = ClientBase.GetResult(results[ii], ii, diagnosticInfos, responseHeader);
                    noErrors = false;
                }
                else
                {
                    monitoredItems[ii].MonitoringMode = monitoringMode;
                    monitoredItems[ii].Status.SetMonitoringMode(monitoringMode);
                }

                errors.Add(error);
            }

            return noErrors;
        }

        /// <summary>
        /// Prepare the creation requests for all monitored items that have not yet been created.
        /// </summary>
        private async Task<(
            MonitoredItemCreateRequestCollection,
            List<MonitoredItem>
            )> PrepareItemsToCreateAsync(CancellationToken ct = default)
        {
            VerifySessionAndSubscriptionState(true);

            await ResolveItemNodeIdsAsync(ct).ConfigureAwait(false);

            var requestItems = new MonitoredItemCreateRequestCollection();
            var itemsToCreate = new List<MonitoredItem>();
            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    // ignore items that have been created or are being created.
                    if (monitoredItem.Status.Created || monitoredItem.Status.Creating)
                    {
                        continue;
                    }

                    // build item request.
                    var request = new MonitoredItemCreateRequest();

                    request.ItemToMonitor.NodeId = monitoredItem.ResolvedNodeId;
                    request.ItemToMonitor.AttributeId = monitoredItem.AttributeId;
                    request.ItemToMonitor.IndexRange = monitoredItem.IndexRange;
                    request.ItemToMonitor.DataEncoding = monitoredItem.Encoding;

                    request.MonitoringMode = monitoredItem.MonitoringMode;

                    request.RequestedParameters.ClientHandle = monitoredItem.ClientHandle;
                    request.RequestedParameters.SamplingInterval = monitoredItem.SamplingInterval;
                    request.RequestedParameters.QueueSize = monitoredItem.QueueSize;
                    request.RequestedParameters.DiscardOldest = monitoredItem.DiscardOldest;

                    if (monitoredItem.Filter != null)
                    {
                        request.RequestedParameters.Filter
                            = new ExtensionObject(monitoredItem.Filter);
                    }

                    requestItems.Add(request);
                    itemsToCreate.Add(monitoredItem);
                }

                // Mark all items as being created before releasing the lock
                // to prevent duplicate creation requests in multi-threaded scenarios
                foreach (MonitoredItem monitoredItem in itemsToCreate)
                {
                    monitoredItem.Status.Creating = true;
                }
            }
            return (requestItems, itemsToCreate);
        }

        /// <summary>
        /// Prepare the modify requests for all monitored items
        /// that need modification.
        /// </summary>
        private void PrepareItemsToModify(
            MonitoredItemModifyRequestCollection requestItems,
            List<MonitoredItem> itemsToModify)
        {
            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    // ignore items that have been created or modified.
                    if (!monitoredItem.Status.Created || !monitoredItem.AttributesModified)
                    {
                        continue;
                    }

                    // build item request.
                    var request = new MonitoredItemModifyRequest
                    {
                        MonitoredItemId = monitoredItem.Status.Id
                    };
                    request.RequestedParameters.ClientHandle = monitoredItem.ClientHandle;
                    request.RequestedParameters.SamplingInterval = monitoredItem.SamplingInterval;
                    request.RequestedParameters.QueueSize = monitoredItem.QueueSize;
                    request.RequestedParameters.DiscardOldest = monitoredItem.DiscardOldest;

                    if (monitoredItem.Filter != null)
                    {
                        request.RequestedParameters.Filter
                            = new ExtensionObject(monitoredItem.Filter);
                    }

                    requestItems.Add(request);
                    itemsToModify.Add(monitoredItem);
                }
            }
        }

        /// <summary>
        /// Transfer all monitored items and prepares the modify
        /// requests if transfer of client handles is not possible.
        /// </summary>
        private void TransferItems(
            UInt32Collection serverHandles,
            UInt32Collection clientHandles,
            out IList<MonitoredItem> itemsToModify)
        {
            lock (m_cache)
            {
                int count = clientHandles.Count;
                itemsToModify = new List<MonitoredItem>(count);
                var updatedMonitoredItems = new ConcurrentDictionary<uint, MonitoredItem>(1, count);
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    int index = serverHandles.FindIndex(
                        handle => handle == monitoredItem.Status.Id);
                    if (index >= 0 && index < count)
                    {
                        uint clientHandle = clientHandles[index];
                        updatedMonitoredItems[clientHandle] = monitoredItem;
                        monitoredItem.SetTransferResult(clientHandle);
                    }
                    else
                    {
                        // modify client handle on server
                        updatedMonitoredItems[monitoredItem.ClientHandle] = monitoredItem;
                        itemsToModify.Add(monitoredItem);
                    }
                }
                m_monitoredItems = updatedMonitoredItems;
            }
        }

        /// <summary>
        /// Prepare the ResolveItem to NodeId service call.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void PrepareResolveItemNodeIds(
            BrowsePathCollection browsePaths,
            List<MonitoredItem> itemsToBrowse)
        {
            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    if (!string.IsNullOrEmpty(monitoredItem.RelativePath) &&
                        monitoredItem.ResolvedNodeId.IsNull)
                    {
                        // cannot change the relative path after an item is created.
                        if (monitoredItem.Created)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidState,
                                "Cannot modify item path after it is created.");
                        }

                        var browsePath = new BrowsePath
                        {
                            StartingNode = monitoredItem.StartNodeId
                        };

                        // parse the relative path.
                        try
                        {
                            browsePath.RelativePath = RelativePath.Parse(
                                monitoredItem.RelativePath,
                                Session!.TypeTree);
                        }
                        catch (Exception e)
                        {
                            monitoredItem.SetError(new ServiceResult(e));
                            continue;
                        }

                        browsePaths.Add(browsePath);
                        itemsToBrowse.Add(monitoredItem);
                    }
                }
            }
        }

        /// <summary>
        /// Saves a data change in the monitored item cache.
        /// </summary>
        private void SaveDataChange(
            NotificationMessage message,
            DataChangeNotification notifications,
            IList<string> stringTable)
        {
            // check for empty monitored items list.
            if (notifications.MonitoredItems == null || notifications.MonitoredItems.Count == 0)
            {
                m_logger.LogInformation(
                    "Publish response contains empty MonitoredItems list for SubscriptionId={SubscriptionId}:.",
                    Id);
                return;
            }

            for (int ii = 0; ii < notifications.MonitoredItems.Count; ii++)
            {
                MonitoredItemNotification notification = notifications.MonitoredItems[ii];

                if (!m_monitoredItems.TryGetValue(notification.ClientHandle, out MonitoredItem? monitoredItem))
                {
                    m_logger.LogWarning(
                        "Publish response contains invalid MonitoredItem. " +
                        "SubscriptionId={SubscriptionId}, ClientHandle = {ClientHandle}",
                        Id,
                        notification.ClientHandle);
                    continue;
                }

                // save the message.
                notification.Message = message;

                // get diagnostic info.
                if (notifications.DiagnosticInfos.Count > ii)
                {
                    notification.DiagnosticInfo = notifications.DiagnosticInfos[ii];
                }

                // save in cache.
                monitoredItem.SaveValueInCache(notification);
            }
        }

        /// <summary>
        /// Saves events in the monitored item cache.
        /// </summary>
        private void SaveEvents(
            NotificationMessage message,
            EventNotificationList notifications,
            IList<string> stringTable)
        {
            for (int ii = 0; ii < notifications.Events.Count; ii++)
            {
                EventFieldList eventFields = notifications.Events[ii];

                MonitoredItem? monitoredItem;
                lock (m_cache)
                {
                    if (!m_monitoredItems.TryGetValue(eventFields.ClientHandle, out monitoredItem))
                    {
                        m_logger.LogWarning(
                            "Publish response contains invalid MonitoredItem." +
                            "SubscriptionId={SubscriptionId}, ClientHandle = {ClientHandle}",
                            Id,
                            eventFields.ClientHandle);
                        continue;
                    }
                }

                // save the message.
                eventFields.Message = message;

                // save in cache.
                monitoredItem.SaveValueInCache(eventFields);
            }
        }

        /// <summary>
        /// Find or create an entry for the incoming sequence number.
        /// </summary>
        /// <param name="utcNow">The current Utc time.</param>
        /// <param name="tickCount">The current monotonic time</param>
        /// <param name="sequenceNumber">The sequence number for the new entry.</param>
        private IncomingMessage FindOrCreateEntry(
            DateTime utcNow,
            int tickCount,
            uint sequenceNumber)
        {
            IncomingMessage? entry = null;
            m_incomingMessages ??= new LinkedList<IncomingMessage>();
            LinkedListNode<IncomingMessage>? node = m_incomingMessages.Last;

            Debug.Assert(m_cache.IsHeldByCurrentThread);
            while (node != null)
            {
                entry = node.Value;
                LinkedListNode<IncomingMessage>? previous = node.Previous;

                if (entry.SequenceNumber == sequenceNumber)
                {
                    entry.Timestamp = utcNow;
                    entry.TickCount = tickCount;
                    break;
                }

                if (entry.SequenceNumber < sequenceNumber)
                {
                    entry = new IncomingMessage
                    {
                        SequenceNumber = sequenceNumber,
                        Timestamp = utcNow,
                        TickCount = tickCount
                    };
                    m_incomingMessages.AddAfter(node, entry);
                    break;
                }

                node = previous;
                entry = null;
            }

            if (entry == null)
            {
                entry = new IncomingMessage
                {
                    SequenceNumber = sequenceNumber,
                    Timestamp = utcNow,
                    TickCount = tickCount
                };
                m_incomingMessages.AddLast(entry);
            }

            return entry;
        }

        /// <summary>
        /// Helper to callback event handlers and to catch exceptions.
        /// </summary>
        private void PublishingStateChanged(
            PublishStateChangedEventHandler? callback,
            PublishStateChangedMask newState)
        {
            try
            {
                callback?.Invoke(this, new PublishStateChangedEventArgs(newState));
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "Error while raising PublishStateChanged event for state {State}.",
                    newState.ToString());
            }
        }

        private List<MonitoredItem> m_deletedItems = [];
        private event SubscriptionStateChangedEventHandler? m_StateChanged;

        private SubscriptionChangeMask m_changeMask;
        private Timer? m_publishTimer;
        private long m_lastNotificationTime;
        private int m_lastNotificationTickCount;
        private int m_keepAliveInterval;
        private int m_publishLateCount;
        private event PublishStateChangedEventHandler? m_PublishStatusChanged;

        private bool m_disposed;
        private readonly Lock m_cache = new();
        private readonly LinkedList<NotificationMessage> m_messageCache = new();
        private IList<uint>? m_availableSequenceNumbers;
        private ConcurrentDictionary<uint, MonitoredItem> m_monitoredItems = new();
        private readonly AsyncAutoResetEvent m_messageWorkerEvent = new();
        private CancellationTokenSource? m_messageWorkerCts;
        private Task? m_messageWorkerTask;
        private int m_outstandingMessageWorkers;
        private uint m_lastSequenceNumberProcessed;
        private bool m_resyncLastSequenceNumberProcessed;
        private LinkedList<IncomingMessage>? m_incomingMessages;
        private ITelemetryContext? m_telemetry;
        private ILogger m_logger;

        /// <summary>
        /// A message received from the server cached until is processed or discarded.
        /// </summary>
        private class IncomingMessage
        {
            public uint SequenceNumber;
            public DateTime Timestamp;
            public int TickCount;
            public NotificationMessage? Message;
            public bool Processed;
            public bool Republished;
            public StatusCode RepublishStatus;
        }
    }

    /// <summary>
    /// Flags indicating what has changed in a subscription.
    /// </summary>
    [Flags]
    public enum SubscriptionChangeMask
    {
        /// <summary>
        /// The subscription has not changed.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// The subscription was created on the server.
        /// </summary>
        Created = 0x01,

        /// <summary>
        /// The subscription was deleted on the server.
        /// </summary>
        Deleted = 0x02,

        /// <summary>
        /// The subscription was modified on the server.
        /// </summary>
        Modified = 0x04,

        /// <summary>
        /// Monitored items were added to the subscription (but not created on the server)
        /// </summary>
        ItemsAdded = 0x08,

        /// <summary>
        /// Monitored items were removed to the subscription (but not deleted on the server)
        /// </summary>
        ItemsRemoved = 0x10,

        /// <summary>
        /// Monitored items were created on the server.
        /// </summary>
        ItemsCreated = 0x20,

        /// <summary>
        /// Monitored items were deleted on the server.
        /// </summary>
        ItemsDeleted = 0x40,

        /// <summary>
        /// Monitored items were modified on the server.
        /// </summary>
        ItemsModified = 0x80,

        /// <summary>
        /// Subscription was transferred on the server.
        /// </summary>
        Transferred = 0x100
    }

    /// <summary>
    /// Flags indicating what has changed in a publish state change.
    /// </summary>
    [Flags]
    public enum PublishStateChangedMask
    {
        /// <summary>
        /// The publish state has not changed.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// The publishing stopped.
        /// </summary>
        Stopped = 0x01,

        /// <summary>
        /// The publishing recovered.
        /// </summary>
        Recovered = 0x02,

        /// <summary>
        /// A keep alive message was received.
        /// </summary>
        KeepAlive = 0x04,

        /// <summary>
        /// A republish for a missing message was issued.
        /// </summary>
        Republish = 0x08,

        /// <summary>
        /// The publishing was transferred to another node.
        /// </summary>
        Transferred = 0x10,

        /// <summary>
        /// The publishing was timed out
        /// </summary>
        Timeout = 0x20,

        /// <summary>
        /// Session is not connected
        /// </summary>
        SessionNotConnected = 0x40
    }

    /// <summary>
    /// The delegate used to receive data change notifications via a direct function call instead of a .NET Event.
    /// </summary>
    public delegate void FastDataChangeNotificationEventHandler(
        Subscription subscription,
        DataChangeNotification notification,
        IList<string> stringTable);

    /// <summary>
    /// The delegate used to receive event notifications via a direct function call instead of a .NET Event.
    /// </summary>
    public delegate void FastEventNotificationEventHandler(
        Subscription subscription,
        EventNotificationList notification,
        IList<string> stringTable);

    /// <summary>
    /// The delegate used to receive keep alive notifications via a direct function call instead of a .NET Event.
    /// </summary>
    public delegate void FastKeepAliveNotificationEventHandler(
        Subscription subscription,
        NotificationData notification);

    /// <summary>
    /// The event arguments provided when the state of a subscription changes.
    /// </summary>
    public class SubscriptionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal SubscriptionStateChangedEventArgs(SubscriptionChangeMask changeMask)
        {
            Status = changeMask;
        }

        /// <summary>
        /// The changes that have affected the subscription.
        /// </summary>
        public SubscriptionChangeMask Status { get; }
    }

    /// <summary>
    /// The delegate used to receive subscription state change notifications.
    /// </summary>
    public delegate void SubscriptionStateChangedEventHandler(
        Subscription subscription,
        SubscriptionStateChangedEventArgs e);

    /// <summary>
    /// The event arguments provided when the state of a subscription changes.
    /// </summary>
    public class PublishStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal PublishStateChangedEventArgs(PublishStateChangedMask changeMask)
        {
            Status = changeMask;
        }

        /// <summary>
        /// The publish state changes.
        /// </summary>
        public PublishStateChangedMask Status { get; }
    }

    /// <summary>
    /// The delegate used to receive publish state change notifications.
    /// </summary>
    public delegate void PublishStateChangedEventHandler(
        Subscription subscription,
        PublishStateChangedEventArgs e);

    /// <summary>
    /// A collection of subscriptions.
    /// </summary>
    public class SubscriptionCollection : List<Subscription>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public SubscriptionCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The existing collection to use as the basis of creating this collection</param>
        public SubscriptionCollection(IEnumerable<Subscription> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max. capacity of the collection</param>
        public SubscriptionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (SubscriptionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new SubscriptionCollection();
            clone.AddRange(this.Select(item => (Subscription)item.Clone()));
            return clone;
        }

        /// <summary>
        /// Helper to clone a SubscriptionCollection with event handlers using the
        /// <see cref="Subscription.CloneSubscription(bool)"/> method.
        /// </summary>
        public virtual SubscriptionCollection CloneSubscriptions(bool copyEventhandlers)
        {
            var clone = new SubscriptionCollection();
            clone.AddRange(this.Select(item => item.CloneSubscription(copyEventhandlers)));
            return clone;
        }
    }
}
