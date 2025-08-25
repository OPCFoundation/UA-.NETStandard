/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A subscription.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class Subscription : IDisposable, ICloneable
    {
        private const int kMinKeepAliveTimerInterval = 1000;
        private const int kKeepAliveTimerMargin = 1000;
        private const int kRepublishMessageTimeout = 2500;
        private const int kRepublishMessageExpiredTimeout = 10000;

        /// <summary>
        /// Creates a empty object.
        /// </summary>
        public Subscription()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the subscription from a template.
        /// </summary>
        public Subscription(Subscription template)
            : this(template, false)
        {
        }

        /// <summary>
        /// Initializes the subscription from a template.
        /// </summary>
        /// <param name="template">The template.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        public Subscription(Subscription template, bool copyEventHandlers)
        {
            Initialize();

            if (template != null)
            {
                DisplayName = template.DisplayName;
                PublishingInterval = template.PublishingInterval;
                KeepAliveCount = template.KeepAliveCount;
                LifetimeCount = template.LifetimeCount;
                MinLifetimeInterval = template.MinLifetimeInterval;
                MaxNotificationsPerPublish = template.MaxNotificationsPerPublish;
                PublishingEnabled = template.PublishingEnabled;
                Priority = template.Priority;
                TimestampsToReturn = template.TimestampsToReturn;
                m_maxMessageCount = template.m_maxMessageCount;
                m_sequentialPublishing = template.m_sequentialPublishing;
                RepublishAfterTransfer = template.RepublishAfterTransfer;
                DefaultItem = (MonitoredItem)template.DefaultItem.Clone();
                Handle = template.Handle;
                DisableMonitoredItemCache = template.DisableMonitoredItemCache;
                TransferId = template.TransferId;

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
            Task workerTask;
            CancellationTokenSource workerCts;
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
                workerCts.Cancel();
                await workerTask.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "SubscriptionId {0} - Reset Publish Worker exception.", Id);
            }
            finally
            {
                Utils.SilentDispose(workerCts);
            }
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        protected void Initialize(StreamingContext context)
        {
            m_cache = new object();
            Initialize();
        }

        /// <summary>
        /// Sets the private members to default values.
        /// </summary>
        private void Initialize()
        {
            TransferId = Id = 0;
            DisplayName = "Subscription";
            PublishingInterval = 0;
            KeepAliveCount = 0;
            m_keepAliveInterval = 0;
            LifetimeCount = 0;
            MaxNotificationsPerPublish = 0;
            PublishingEnabled = false;
            TimestampsToReturn = TimestampsToReturn.Both;
            m_maxMessageCount = 10;
            RepublishAfterTransfer = false;
            m_outstandingMessageWorkers = 0;
            m_sequentialPublishing = false;
            m_lastSequenceNumberProcessed = 0;
            m_messageCache = new LinkedList<NotificationMessage>();
            m_monitoredItems = [];
            m_deletedItems = [];
            m_messageWorkerEvent = new AsyncAutoResetEvent();
            m_messageWorkerCts = null;
            m_resyncLastSequenceNumberProcessed = false;

            DefaultItem = new MonitoredItem
            {
                DisplayName = "MonitoredItem",
                SamplingInterval = -1,
                MonitoringMode = MonitoringMode.Reporting,
                QueueSize = 0,
                DiscardOldest = true
            };
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
        [DataMember(Order = 1)]
        public string DisplayName { get; set; }

        /// <summary>
        /// The publishing interval.
        /// </summary>
        [DataMember(Order = 2)]
        public int PublishingInterval { get; set; }

        /// <summary>
        /// The keep alive count.
        /// </summary>
        [DataMember(Order = 3)]
        public uint KeepAliveCount { get; set; }

        /// <summary>
        /// The life time of the subscription in counts of
        /// publish interval.
        /// LifetimeCount shall be at least 3*KeepAliveCount.
        /// </summary>
        [DataMember(Order = 4)]
        public uint LifetimeCount { get; set; }

        /// <summary>
        /// The maximum number of notifications per publish request.
        /// </summary>
        [DataMember(Order = 5)]
        public uint MaxNotificationsPerPublish { get; set; }

        /// <summary>
        /// Whether publishing is enabled.
        /// </summary>
        [DataMember(Order = 6)]
        public bool PublishingEnabled { get; set; }

        /// <summary>
        /// The priority assigned to subscription.
        /// </summary>
        [DataMember(Order = 7)]
        public byte Priority { get; set; }

        /// <summary>
        /// The timestamps to return with the notification messages.
        /// </summary>
        [DataMember(Order = 8)]
        public TimestampsToReturn TimestampsToReturn { get; set; }

        /// <summary>
        /// The maximum number of messages to keep in the internal cache.
        /// </summary>
        [DataMember(Order = 9)]
        public int MaxMessageCount
        {
            get => m_maxMessageCount;
            set
            {
                // lock needed to synchronize with message list processing
                lock (m_cache)
                {
                    m_maxMessageCount = value;
                }
            }
        }

        /// <summary>
        /// The default monitored item.
        /// </summary>
        [DataMember(Order = 10)]
        public MonitoredItem DefaultItem { get; set; }

        /// <summary>
        /// The minimum lifetime for subscriptions in milliseconds.
        /// </summary>
        [DataMember(Order = 12)]
        public uint MinLifetimeInterval { get; set; }

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
        [DataMember(Order = 13)]
        public bool DisableMonitoredItemCache { get; set; }

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
        [DataMember(Order = 14)]
        public bool SequentialPublishing
        {
            get => m_sequentialPublishing;
            set
            {
                // synchronize with message list processing
                lock (m_cache)
                {
                    m_sequentialPublishing = value;
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
        [DataMember(Name = "RepublishAfterTransfer", Order = 15)]
        public bool RepublishAfterTransfer { get; set; }

        /// <summary>
        /// The unique identifier assigned by the server which can be used to transfer a session.
        /// </summary>
        [DataMember(Name = "TransferId", Order = 16)]
        public uint TransferId { get; set; }

        /// <summary>
        /// Gets or sets the fast data change callback.
        /// </summary>
        /// <value>The fast data change callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastDataChangeNotificationEventHandler FastDataChangeCallback { get; set; }

        /// <summary>
        /// Gets or sets the fast event callback.
        /// </summary>
        /// <value>The fast event callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastEventNotificationEventHandler FastEventCallback { get; set; }

        /// <summary>
        /// Gets or sets the fast keep alive callback.
        /// </summary>
        /// <value>The keep alive change callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastKeepAliveNotificationEventHandler FastKeepAliveCallback { get; set; }

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
        /// Allows the list of monitored items to be saved/restored when the object is serialized.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [DataMember(Name = "MonitoredItems", Order = 11)]
        internal List<MonitoredItem> SavedMonitoredItems
        {
            get
            {
                lock (m_cache)
                {
                    return [.. m_monitoredItems.Values];
                }
            }
            set
            {
                if (Created)
                {
                    throw new InvalidOperationException(
                        "Cannot update a subscription that has been created on the server.");
                }

                lock (m_cache)
                {
                    m_monitoredItems.Clear();
                    AddItems(value);
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
        public ISession Session { get; protected internal set; }

        /// <summary>
        /// A local handle assigned to the subscription
        /// </summary>
        public object Handle { get; set; }

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
        [DataMember(Name = "CurrentPublishInterval", Order = 20)]
        public double CurrentPublishingInterval { get; set; }

        /// <summary>
        /// The current keep alive count.
        /// </summary>
        [DataMember(Name = "CurrentKeepAliveCount", Order = 21)]
        public uint CurrentKeepAliveCount { get; set; }

        /// <summary>
        /// The current lifetime count.
        /// </summary>
        [DataMember(Name = "CurrentLifetimeCount", Order = 22)]
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
                        return m_messageCache.Last.Value.PublishTime;
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
                        return m_messageCache.Last.Value.SequenceNumber;
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
                        return (uint)m_messageCache.Last.Value.NotificationData.Count;
                    }
                }

                return 0;
            }
        }

        /// <summary>
        /// The last notification received from the server.
        /// </summary>
        public NotificationMessage LastNotification
        {
            get
            {
                lock (m_cache)
                {
                    if (m_messageCache.Count > 0)
                    {
                        return m_messageCache.Last.Value;
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
                        ? new ReadOnlyList<uint>(m_availableSequenceNumbers)
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
                Utils.LogError(
                    ex,
                    "Subscription state change callback exception with change mask 0x{0:X2}",
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
            VerifySubscriptionState(false);

            // create the subscription.
            uint revisedMaxKeepAliveCount = KeepAliveCount;
            uint revisedLifetimeCount = LifetimeCount;

            AdjustCounts(ref revisedMaxKeepAliveCount, ref revisedLifetimeCount);

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
            if (!silent)
            {
                VerifySubscriptionState(true);
            }

            // nothing to do if not created.
            if (!Created)
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
                    throw new ServiceResultException(e, StatusCodes.BadUnexpectedError);
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
            VerifySubscriptionState(true);

            // modify the subscription.
            uint revisedKeepAliveCount = KeepAliveCount;
            uint revisedLifetimeCounter = LifetimeCount;

            AdjustCounts(ref revisedKeepAliveCount, ref revisedLifetimeCounter);

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
            VerifySubscriptionState(true);

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
            VerifySubscriptionState(true);

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
            await DeleteItemsAsync(ct).ConfigureAwait(false);
            await ModifyItemsAsync(ct).ConfigureAwait(false);
            await CreateItemsAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves all relative paths to nodes on the server.
        /// </summary>
        public async Task ResolveItemNodeIdsAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

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

            (requestItems, itemsToCreate) = await PrepareItemsToCreateAsync(ct)
                .ConfigureAwait(false);

            if (requestItems.Count == 0)
            {
                return itemsToCreate;
            }

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

            m_changeMask |= SubscriptionChangeMask.ItemsCreated;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToCreate;
        }

        /// <summary>
        /// Modifies all items that have been changed.
        /// </summary>
        public async Task<IList<MonitoredItem>> ModifyItemsAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            var requestItems = new MonitoredItemModifyRequestCollection();
            var itemsToModify = new List<MonitoredItem>();

            PrepareItemsToModify(requestItems, itemsToModify);

            if (requestItems.Count == 0)
            {
                return itemsToModify;
            }

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
            VerifySubscriptionState(true);

            if (m_deletedItems.Count == 0)
            {
                return [];
            }

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
        /// Set monitoring mode of items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="monitoredItems"/>
        /// is <c>null</c>.</exception>
        public async Task<List<ServiceResult>> SetMonitoringModeAsync(
            MonitoringMode monitoringMode,
            IList<MonitoredItem> monitoredItems,
            CancellationToken ct = default)
        {
            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            VerifySubscriptionState(true);

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
            var errors = new List<ServiceResult>();
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
        /// Tells the server to refresh all conditions being monitored by the subscription.
        /// </summary>
        public async Task<bool> ConditionRefreshAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

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
            VerifySubscriptionState(true);

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
                    Utils.LogError(
                        "SubscriptionId {0}: Failed to remove transferred subscription from owner SessionId={1}.",
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
                    Utils.LogError(
                        "SubscriptionId {0}: Failed to add transferred subscription to SessionId={1}.",
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
                    Utils.LogError(
                        "SubscriptionId {0}: The server failed to respond to GetMonitoredItems after transfer.",
                        Id);
                    return false;
                }

                int monitoredItemsCount = m_monitoredItems.Count;
                if (serverHandles.Count != monitoredItemsCount ||
                    clientHandles.Count != monitoredItemsCount)
                {
                    // invalid state
                    Utils.LogError(
                        "SubscriptionId {0}: Number of Monitored Items on client and server do not match after transfer {1}!={2}",
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

            StartKeepAliveTimer();

            TraceState("TRANSFERRED ASYNC");

            return true;
        }

        /// <summary>
        /// Adds the notification message to internal cache.
        /// </summary>
        public void SaveMessageInCache(
            IList<uint> availableSequenceNumbers,
            NotificationMessage message)
        {
            PublishStateChangedEventHandler callback = null;

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
                LinkedListNode<IncomingMessage> node = m_incomingMessages.First;

                while (node != null)
                {
                    entry = node.Value;
                    LinkedListNode<IncomingMessage> next = node.Next;

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
                    LinkedListNode<IncomingMessage> next = node.Next;

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
                                Utils.LogWarning(
                                    "SubscriptionId {0} skipping PublishResponse Sequence Number {1}",
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
                if (m_monitoredItems.ContainsKey(monitoredItem.ClientHandle))
                {
                    return;
                }

                m_monitoredItems.Add(monitoredItem.ClientHandle, monitoredItem);
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
#if NETFRAMEWORK || NETSTANDARD2_0
                    if (!m_monitoredItems.ContainsKey(monitoredItem.ClientHandle))
                    {
                        m_monitoredItems.Add(monitoredItem.ClientHandle, monitoredItem);
#else
                    if (m_monitoredItems.TryAdd(monitoredItem.ClientHandle, monitoredItem))
                    {
#endif
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
                if (!m_monitoredItems.Remove(monitoredItem.ClientHandle))
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
                    if (m_monitoredItems.Remove(monitoredItem.ClientHandle))
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
        public MonitoredItem FindItemByClientHandle(uint clientHandle)
        {
            lock (m_cache)
            {
                if (m_monitoredItems.TryGetValue(clientHandle, out MonitoredItem monitoredItem))
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
            VerifySubscriptionState(true);
            try
            {
                await Session.CallAsync(ObjectIds.Server, MethodIds.Server_ResendData, ct, Id)
                    .ConfigureAwait(false);
                return true;
            }
            catch (ServiceResultException sre)
            {
                Utils.LogError(sre, "SubscriptionId {0}: Failed to call ResendData on server", Id);
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
            var serverHandles = new UInt32Collection();
            var clientHandles = new UInt32Collection();
            try
            {
                IList<object> outputArguments = await Session.CallAsync(
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
                Utils.LogError(
                    sre,
                    "SubscriptionId {0}: Failed to call GetMonitoredItems on server",
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
            uint revisedLifetimeInHours = lifetimeInHours;

            try
            {
                IList<object> outputArguments = await Session
                    .CallAsync(
                        ObjectIds.Server,
                        MethodIds.Server_SetSubscriptionDurable,
                        ct,
                        TransferId,
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
                Utils.LogError(
                    sre,
                    "SubscriptionId {0}: Failed to call SetSubscriptionDurable on server",
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
                m_incomingMessages = new LinkedList<IncomingMessage>();

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

                    Utils.LogInfo(
                        "SubscriptionId {0}: Republishing {1} messages, next sequencenumber {2} after transfer.",
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
            Session.StartPublishing(BeginPublishTimeout(), false);
        }

        /// <summary>
        /// Checks if a notification has arrived. Sends a publish if it has not.
        /// </summary>
        private void OnKeepAlive(object state)
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
            PublishStateChangedEventHandler callback = m_PublishStatusChanged;

            Interlocked.Increment(ref m_publishLateCount);

            TraceState("PUBLISHING STOPPED");

            PublishingStateChanged(callback, PublishStateChangedMask.Stopped);

            // try to send a publish to recover stopped publishing.
            Session?.BeginPublish(BeginPublishTimeout());
        }

        /// <summary>
        /// Publish response worker task for the subscriptions.
        /// </summary>
        private async Task PublishResponseMessageWorkerAsync(CancellationToken ct)
        {
            Utils.LogTrace(
                "SubscriptionId {0} - Publish Thread {1:X8} Started.",
                Id,
                Environment.CurrentManagedThreadId);

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
                Utils.LogError(
                    e,
                    "SubscriptionId {0} - Publish Worker Thread {1:X8} Exited Unexpectedly.",
                    Id,
                    Environment.CurrentManagedThreadId);
                return;
            }

            Utils.LogTrace(
                "SubscriptionId {0} - Publish Thread {1:X8} Exited Normally.",
                Id,
                Environment.CurrentManagedThreadId);
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
                Utils.LogInfo(
                    "For subscription {0}, Keep alive count was revised from {1} to {2}",
                    Id,
                    KeepAliveCount,
                    revisedKeepAliveCount);
            }

            if (LifetimeCount != revisedLifetimeCounter)
            {
                Utils.LogInfo(
                    "For subscription {0}, Lifetime count was revised from {1} to {2}",
                    Id,
                    LifetimeCount,
                    revisedLifetimeCounter);
            }

            if (PublishingInterval != revisedPublishingInterval)
            {
                Utils.LogInfo(
                    "For subscription {0}, Publishing interval was revised from {1} to {2}",
                    Id,
                    PublishingInterval,
                    revisedPublishingInterval);
            }

            if (revisedLifetimeCounter < revisedKeepAliveCount * 3)
            {
                Utils.LogInfo(
                    "For subscription {0}, Revised lifetime counter (value={1}) is less than three times the keep alive count (value={2})",
                    Id,
                    revisedLifetimeCounter,
                    revisedKeepAliveCount);
            }

            if (CurrentPriority == 0)
            {
                Utils.LogInfo("For subscription {0}, the priority was set to 0.", Id);
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
        private void AdjustCounts(ref uint keepAliveCount, ref uint lifetimeCount)
        {
            const uint kDefaultKeepAlive = 10;
            const uint kDefaultLifeTime = 1000;

            // keep alive count must be at least 1, 10 is a good default.
            if (keepAliveCount == 0)
            {
                Utils.LogInfo(
                    "Adjusted KeepAliveCount from value={0}, to value={1}, for subscription {2}.",
                    keepAliveCount,
                    kDefaultKeepAlive,
                    Id);
                keepAliveCount = kDefaultKeepAlive;
            }

            // ensure the lifetime is sensible given the sampling interval.
            if (PublishingInterval > 0)
            {
                if (MinLifetimeInterval > 0 && MinLifetimeInterval < Session.SessionTimeout)
                {
                    Utils.LogWarning(
                        "A smaller minLifetimeInterval {0}ms than session timeout {1}ms configured for subscription {2}.",
                        MinLifetimeInterval,
                        Session.SessionTimeout,
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

                    Utils.LogInfo(
                        "Adjusted LifetimeCount to value={0}, for subscription {1}. ",
                        lifetimeCount,
                        Id);
                }

                if (lifetimeCount * PublishingInterval < Session.SessionTimeout)
                {
                    Utils.LogWarning(
                        "Lifetime {0}ms configured for subscription {1} is less than session timeout {2}ms.",
                        lifetimeCount * PublishingInterval,
                        Id,
                        Session.SessionTimeout);
                }
            }
            else if (lifetimeCount == 0)
            {
                // don't know what the sampling interval will be - use something large enough
                // to ensure the user does not experience unexpected drop outs.
                Utils.LogInfo(
                    "Adjusted LifetimeCount from value={0}, to value={1}, for subscription {2}. ",
                    lifetimeCount,
                    kDefaultLifeTime,
                    Id);
                lifetimeCount = kDefaultLifeTime;
            }

            // validate spec: lifetimecount shall be at least 3*keepAliveCount
            uint minLifeTimeCount = 3 * keepAliveCount;
            if (lifetimeCount < minLifeTimeCount)
            {
                Utils.LogInfo(
                    "Adjusted LifetimeCount from value={0}, to value={1}, for subscription {2}. ",
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

                ISession session = null;
                uint subscriptionId = 0;
                PublishStateChangedEventHandler callback = null;

                // list of new messages to process.
                List<NotificationMessage> messagesToProcess = null;

                // list of keep alive messages to process.
                List<IncomingMessage> keepAliveToProcess = null;

                // list of new messages to republish.
                List<IncomingMessage> messagesToRepublish = null;

                PublishStateChangedMask publishStateChangedMask = PublishStateChangedMask.None;

                lock (m_cache)
                {
                    if (m_incomingMessages == null)
                    {
                        return;
                    }

                    for (LinkedListNode<IncomingMessage> ii = m_incomingMessages.First;
                        ii != null;
                        ii = ii.Next)
                    {
                        // update monitored items with unprocessed messages.
                        if (ii.Value.Message != null &&
                            !ii.Value.Processed &&
                            (!m_sequentialPublishing || ValidSequentialPublishMessage(ii.Value)))
                        {
                            (messagesToProcess ??= []).Add(ii.Value.Message);

                            // remove the oldest items.
                            while (m_messageCache.Count > m_maxMessageCount)
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
                                    Utils.LogInfo(
                                        "SubscriptionId {0}: Resynced last sequence number processed to {1}.",
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
                                    Utils.LogInfo(
                                        "Skipped to receive RepublishAsync for {0}-{1}-BadMessageNotAvailable",
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
                            Utils.LogDebug(
                                "Subscription {0}: Delayed message with sequence number {1}, expected sequence number is {2}.",
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
                FastKeepAliveNotificationEventHandler keepAliveCallback = FastKeepAliveCallback;
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
                    FastDataChangeNotificationEventHandler datachangeCallback
                        = FastDataChangeCallback;
                    FastEventNotificationEventHandler eventCallback = FastEventCallback;

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

                                    Utils.LogWarning(
                                        "StatusChangeNotification received with Status = {0} for SubscriptionId={1}.",
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
                            Utils.LogError(
                                e,
                                "Error while processing incoming message #{0}.",
                                message.SequenceNumber);
                        }

                        if (MaxNotificationsPerPublish != 0 &&
                            noNotificationsReceived > MaxNotificationsPerPublish)
                        {
                            Utils.LogWarning(
                                "For subscription {0}, more notifications were received={1} than the max notifications per publish value={2}",
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
                Utils.LogError(e, "Error while processing incoming messages.");
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
        private void VerifySubscriptionState(bool created)
        {
            if (created && Id == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Subscription has not been created.");
            }

            if (!created && Id != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Subscription has already been created.");
            }

            if (!created && Session is null) // Occurs only on Create() and CreateAsync()
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
            List<ServiceResult> errors,
            StatusCodeCollection results,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader,
            MonitoringMode monitoringMode)
        {
            // update results.
            bool noErrors = true;

            for (int ii = 0; ii < results.Count; ii++)
            {
                ServiceResult error = null;

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
            VerifySubscriptionState(true);

            await ResolveItemNodeIdsAsync(ct).ConfigureAwait(false);

            var requestItems = new MonitoredItemCreateRequestCollection();
            var itemsToCreate = new List<MonitoredItem>();
            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    // ignore items that have been created.
                    if (monitoredItem.Status.Created)
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
                var updatedMonitoredItems = new Dictionary<uint, MonitoredItem>(count);
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
                        NodeId.IsNull(monitoredItem.ResolvedNodeId))
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
                                Session.TypeTree);
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
                Utils.LogInfo(
                    "Publish response contains empty MonitoredItems list for SubscriptionId = {0}.",
                    Id);
                return;
            }

            for (int ii = 0; ii < notifications.MonitoredItems.Count; ii++)
            {
                MonitoredItemNotification notification = notifications.MonitoredItems[ii];

                // lookup monitored item,
                MonitoredItem monitoredItem = null;

                lock (m_cache)
                {
                    if (!m_monitoredItems.TryGetValue(notification.ClientHandle, out monitoredItem))
                    {
                        Utils.LogWarning(
                            "Publish response contains invalid MonitoredItem. SubscriptionId = {0}, ClientHandle = {1}",
                            Id,
                            notification.ClientHandle);
                        continue;
                    }
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

                MonitoredItem monitoredItem = null;

                lock (m_cache)
                {
                    if (!m_monitoredItems.TryGetValue(eventFields.ClientHandle, out monitoredItem))
                    {
                        Utils.LogWarning(
                            "Publish response contains invalid MonitoredItem.SubscriptionId = {0}, ClientHandle = {1}",
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
            IncomingMessage entry = null;
            LinkedListNode<IncomingMessage> node = m_incomingMessages.Last;

            Debug.Assert(Monitor.IsEntered(m_cache));
            while (node != null)
            {
                entry = node.Value;
                LinkedListNode<IncomingMessage> previous = node.Previous;

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
            PublishStateChangedEventHandler callback,
            PublishStateChangedMask newState)
        {
            try
            {
                callback?.Invoke(this, new PublishStateChangedEventArgs(newState));
            }
            catch (Exception e)
            {
                Utils.LogError(
                    e,
                    "Error while raising PublishStateChanged event for state {0}.",
                    newState.ToString());
            }
        }

        private List<MonitoredItem> m_deletedItems;
        private event SubscriptionStateChangedEventHandler m_StateChanged;

        private SubscriptionChangeMask m_changeMask;
        private Timer m_publishTimer;
        private long m_lastNotificationTime;
        private int m_lastNotificationTickCount;
        private int m_keepAliveInterval;
        private int m_publishLateCount;
        private event PublishStateChangedEventHandler m_PublishStatusChanged;

        private bool m_disposed;
        private object m_cache = new();
        private LinkedList<NotificationMessage> m_messageCache;
        private IList<uint> m_availableSequenceNumbers;
        private int m_maxMessageCount;
        private Dictionary<uint, MonitoredItem> m_monitoredItems;
        private AsyncAutoResetEvent m_messageWorkerEvent;
        private CancellationTokenSource m_messageWorkerCts;
        private Task m_messageWorkerTask;
        private int m_outstandingMessageWorkers;
        private bool m_sequentialPublishing;
        private uint m_lastSequenceNumberProcessed;
        private bool m_resyncLastSequenceNumberProcessed;

        /// <summary>
        /// A message received from the server cached until is processed or discarded.
        /// </summary>
        private class IncomingMessage
        {
            public uint SequenceNumber;
            public DateTime Timestamp;
            public int TickCount;
            public NotificationMessage Message;
            public bool Processed;
            public bool Republished;
            public StatusCode RepublishStatus;
        }

        private LinkedList<IncomingMessage> m_incomingMessages;
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
        Timeout = 0x20
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
    [CollectionDataContract(
        Name = "ListOfSubscription",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Subscription")]
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
