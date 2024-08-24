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
using Opc.Ua.Types.Utils;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A subscription.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public partial class Subscription : IDisposable, ICloneable
    {
        const int kMinKeepAliveTimerInterval = 1000;
        const int kKeepAliveTimerMargin = 1000;
        const int kRepublishMessageTimeout = 2500;
        const int kRepublishMessageExpiredTimeout = 10000;

        #region Constructors
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
        public Subscription(Subscription template) : this(template, false)
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
                m_displayName = template.m_displayName;
                m_publishingInterval = template.m_publishingInterval;
                m_keepAliveCount = template.m_keepAliveCount;
                m_lifetimeCount = template.m_lifetimeCount;
                m_minLifetimeInterval = template.m_minLifetimeInterval;
                m_maxNotificationsPerPublish = template.m_maxNotificationsPerPublish;
                m_publishingEnabled = template.m_publishingEnabled;
                m_priority = template.m_priority;
                m_timestampsToReturn = template.m_timestampsToReturn;
                m_maxMessageCount = template.m_maxMessageCount;
                m_sequentialPublishing = template.m_sequentialPublishing;
                m_republishAfterTransfer = template.m_republishAfterTransfer;
                m_defaultItem = (MonitoredItem)template.m_defaultItem.Clone();
                m_handle = template.m_handle;
                m_disableMonitoredItemCache = template.m_disableMonitoredItemCache;
                m_transferId = template.m_transferId;

                if (copyEventHandlers)
                {
                    m_StateChanged = template.m_StateChanged;
                    m_publishStatusChanged = template.m_publishStatusChanged;
                    m_fastDataChangeCallback = template.m_fastDataChangeCallback;
                    m_fastEventCallback = template.m_fastEventCallback;
                    m_fastKeepAliveCallback = template.m_fastKeepAliveCallback;
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
            // stop the publish timer.
            Utils.SilentDispose(m_publishTimer);
            m_publishTimer = null;
            Utils.SilentDispose(m_messageWorkerCts);
            m_messageWorkerEvent.Set();
            m_messageWorkerCts = null;
            m_messageWorkerTask = null;
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
            m_transferId = m_id = 0;
            m_displayName = "Subscription";
            m_publishingInterval = 0;
            m_keepAliveCount = 0;
            m_keepAliveInterval = 0;
            m_lifetimeCount = 0;
            m_maxNotificationsPerPublish = 0;
            m_publishingEnabled = false;
            m_timestampsToReturn = TimestampsToReturn.Both;
            m_maxMessageCount = 10;
            m_republishAfterTransfer = false;
            m_outstandingMessageWorkers = 0;
            m_sequentialPublishing = false;
            m_lastSequenceNumberProcessed = 0;
            m_messageCache = new LinkedList<NotificationMessage>();
            m_monitoredItems = new SortedDictionary<uint, MonitoredItem>();
            m_deletedItems = new List<MonitoredItem>();
            m_messageWorkerEvent = new AsyncAutoResetEvent();
            m_messageWorkerCts = null;
            m_resyncLastSequenceNumberProcessed = false;

            m_defaultItem = new MonitoredItem {
                DisplayName = "MonitoredItem",
                SamplingInterval = -1,
                MonitoringMode = MonitoringMode.Reporting,
                QueueSize = 0,
                DiscardOldest = true
            };
        }
        #endregion

        #region IDisposable Members
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
            }
        }
        #endregion

        #region ICloneable Members
        /// <summary cref="ICloneable.Clone" />
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary cref="Object.MemberwiseClone" />
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
        #endregion

        #region Events
        /// <summary>
        /// Raised to indicate that the state of the subscription has changed.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event SubscriptionStateChangedEventHandler StateChanged
        {
            add { m_StateChanged += value; }
            remove { m_StateChanged -= value; }
        }

        /// <summary>
        /// Raised to indicate the publishing state for the subscription has stopped or resumed (see PublishingStopped property).
        /// </summary>
        public event PublishStateChangedEventHandler PublishStatusChanged
        {
            add
            {
                m_publishStatusChanged += value;
            }

            remove
            {
                m_publishStatusChanged -= value;
            }
        }
        #endregion

        #region Persistent Properties
        /// <summary>
        /// A display name for the subscription.
        /// </summary>
        [DataMember(Order = 1)]
        public string DisplayName
        {
            get => m_displayName;
            set => m_displayName = value;
        }

        /// <summary>
        /// The publishing interval.
        /// </summary>
        [DataMember(Order = 2)]
        public int PublishingInterval
        {
            get => m_publishingInterval;
            set => m_publishingInterval = value;
        }

        /// <summary>
        /// The keep alive count.
        /// </summary>
        [DataMember(Order = 3)]
        public uint KeepAliveCount
        {
            get => m_keepAliveCount;
            set => m_keepAliveCount = value;
        }

        /// <summary>
        /// The life time of of the subscription in counts of
        /// publish interval.
        /// LifetimeCount shall be at least 3*KeepAliveCount.
        /// </summary>
        [DataMember(Order = 4)]
        public uint LifetimeCount
        {
            get => m_lifetimeCount;
            set => m_lifetimeCount = value;
        }

        /// <summary>
        /// The maximum number of notifications per publish request.
        /// </summary>
        [DataMember(Order = 5)]
        public uint MaxNotificationsPerPublish
        {
            get => m_maxNotificationsPerPublish;
            set => m_maxNotificationsPerPublish = value;
        }

        /// <summary>
        /// Whether publishing is enabled.
        /// </summary>
        [DataMember(Order = 6)]
        public bool PublishingEnabled
        {
            get => m_publishingEnabled;
            set => m_publishingEnabled = value;
        }

        /// <summary>
        /// The priority assigned to subscription.
        /// </summary>
        [DataMember(Order = 7)]
        public byte Priority
        {
            get => m_priority;
            set => m_priority = value;
        }

        /// <summary>
        /// The timestamps to return with the notification messages.
        /// </summary>
        [DataMember(Order = 8)]
        public TimestampsToReturn TimestampsToReturn
        {
            get => m_timestampsToReturn;
            set => m_timestampsToReturn = value;
        }

        /// <summary>
        /// The maximum number of messages to keep in the internal cache.
        /// </summary>
        [DataMember(Order = 9)]
        public int MaxMessageCount
        {
            get
            {
                return m_maxMessageCount;
            }

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
        public MonitoredItem DefaultItem
        {
            get => m_defaultItem;
            set => m_defaultItem = value;
        }

        /// <summary>
        /// The minimum lifetime for subscriptions in milliseconds.
        /// </summary>
        [DataMember(Order = 12)]
        public uint MinLifetimeInterval
        {
            get => m_minLifetimeInterval;
            set => m_minLifetimeInterval = value;
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
        [DataMember(Order = 13)]
        public bool DisableMonitoredItemCache
        {
            get => m_disableMonitoredItemCache;
            set => m_disableMonitoredItemCache = value;
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
        [DataMember(Order = 14)]
        public bool SequentialPublishing
        {
            get
            {
                return m_sequentialPublishing;
            }
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
        public bool RepublishAfterTransfer
        {
            get { return m_republishAfterTransfer; }
            set { m_republishAfterTransfer = value; }
        }

        /// <summary>
        /// The unique identifier assigned by the server which can be used to transfer a session.
        /// </summary>
        [DataMember(Name = "TransferId", Order = 16)]
        public uint TransferId
        {
            get => m_transferId;
            set => m_transferId = value;
        }

        /// <summary>
        /// Gets or sets the fast data change callback.
        /// </summary>
        /// <value>The fast data change callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastDataChangeNotificationEventHandler FastDataChangeCallback
        {
            get => m_fastDataChangeCallback;
            set => m_fastDataChangeCallback = value;
        }

        /// <summary>
        /// Gets or sets the fast event callback.
        /// </summary>
        /// <value>The fast event callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastEventNotificationEventHandler FastEventCallback
        {
            get => m_fastEventCallback;
            set => m_fastEventCallback = value;
        }

        /// <summary>
        /// Gets or sets the fast keep alive callback.
        /// </summary>
        /// <value>The keep alive change callback.</value>
        /// <remarks>
        /// Only one callback is allowed at a time but it is more efficient to call than an event.
        /// </remarks>
        public FastKeepAliveNotificationEventHandler FastKeepAliveCallback
        {
            get => m_fastKeepAliveCallback;
            set => m_fastKeepAliveCallback = value;
        }

        /// <summary>
        /// The items to monitor.
        /// </summary>
        public IEnumerable<MonitoredItem> MonitoredItems
        {
            get
            {
                lock (m_cache)
                {
                    return new List<MonitoredItem>(m_monitoredItems.Values);
                }
            }
        }

        /// <summary>
        /// Allows the list of monitored items to be saved/restored when the object is serialized.
        /// </summary>
        [DataMember(Name = "MonitoredItems", Order = 11)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        private List<MonitoredItem> SavedMonitoredItems
        {
            get
            {
                lock (m_cache)
                {
                    return new List<MonitoredItem>(m_monitoredItems.Values);
                }
            }

            set
            {
                if (this.Created)
                {
                    throw new InvalidOperationException("Cannot update a subscription that has been created on the server.");
                }

                lock (m_cache)
                {
                    m_monitoredItems.Clear();
                    AddItems(value);
                }
            }
        }
        #endregion

        #region Dynamic Properties
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
        public ISession Session
        {
            get => m_session;
            protected internal set => m_session = value;
        }

        /// <summary>
        /// A local handle assigned to the subscription
        /// </summary>
        public object Handle
        {
            get => m_handle;
            set => m_handle = value;
        }

        /// <summary>
        /// The unique identifier assigned by the server.
        /// </summary>
        public uint Id => m_id;

        /// <summary>
        /// Whether the subscription has been created on the server.
        /// </summary>
        public bool Created => m_id != 0;

        /// <summary>
        /// The current publishing interval.
        /// </summary>
        [DataMember(Name = "CurrentPublishInterval", Order = 20)]
        public double CurrentPublishingInterval
        {
            get => m_currentPublishingInterval;
            set => m_currentPublishingInterval = value;
        }

        /// <summary>
        /// The current keep alive count.
        /// </summary>
        [DataMember(Name = "CurrentKeepAliveCount", Order = 21)]
        public uint CurrentKeepAliveCount
        {
            get => m_currentKeepAliveCount;
            set => m_currentKeepAliveCount = value;
        }

        /// <summary>
        /// The current lifetime count.
        /// </summary>
        [DataMember(Name = "CurrentLifetimeCount", Order = 22)]
        public uint CurrentLifetimeCount
        {
            get => m_currentLifetimeCount;
            set => m_currentLifetimeCount = value;
        }

        /// <summary>
        /// Whether publishing is currently enabled.
        /// </summary>
        public bool CurrentPublishingEnabled => m_currentPublishingEnabled;

        /// <summary>
        /// The priority assigned to subscription when it was created.
        /// </summary>
        public byte CurrentPriority => m_currentPriority;

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
                var ticks = Interlocked.Read(ref m_lastNotificationTime);
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
                    return new List<NotificationMessage>(m_messageCache);
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
                    return m_availableSequenceNumbers != null ?
                        (IEnumerable<uint>)new ReadOnlyList<uint>(m_availableSequenceNumbers) :
                        Enumerable.Empty<uint>();
                }
            }
        }

        /// <summary>
        /// Sends a notification that the state of the subscription has changed.
        /// </summary>
        public void ChangesCompleted()
        {
            m_StateChanged?.Invoke(this, new SubscriptionStateChangedEventArgs(m_changeMask));
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
                if (timeSinceLastNotification > m_keepAliveInterval + kKeepAliveTimerMargin)
                {
                    return true;
                }

                return false;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a subscription on the server and adds all monitored items.
        /// </summary>
        public void Create()
        {
            VerifySubscriptionState(false);

            // create the subscription.
            uint subscriptionId;
            double revisedPublishingInterval;
            uint revisedKeepAliveCount = m_keepAliveCount;
            uint revisedLifetimeCounter = m_lifetimeCount;

            AdjustCounts(ref revisedKeepAliveCount, ref revisedLifetimeCounter);

            m_session.CreateSubscription(
                null,
                m_publishingInterval,
                revisedLifetimeCounter,
                revisedKeepAliveCount,
                m_maxNotificationsPerPublish,
                m_publishingEnabled,
                m_priority,
                out subscriptionId,
                out revisedPublishingInterval,
                out revisedLifetimeCounter,
                out revisedKeepAliveCount);

            CreateSubscription(subscriptionId, revisedPublishingInterval, revisedKeepAliveCount, revisedLifetimeCounter);

            CreateItems();

            ChangesCompleted();

            TraceState("CREATED");
        }

        /// <summary>
        /// Called after the subscription was transferred.
        /// </summary>
        /// <param name="session">The session to which the subscription is transferred.</param>
        /// <param name="id">Id of the transferred subscription.</param>
        /// <param name="availableSequenceNumbers">The available sequence numbers on the server.</param>
        public bool Transfer(ISession session, uint id, UInt32Collection availableSequenceNumbers)
        {
            if (Created)
            {
                // handle the case when the client has the subscription template and reconnects
                if (id != m_id)
                {
                    return false;
                }

                // remove the subscription from disconnected session
                if (m_session?.RemoveTransferredSubscription(this) != true)
                {
                    Utils.LogError("SubscriptionId {0}: Failed to remove transferred subscription from owner SessionId={1}.", Id, m_session?.SessionId);
                    return false;
                }

                // remove default subscription template which was copied in Session.Create()
                var subscriptionsToRemove = session.Subscriptions.Where(s => !s.Created && s.TransferId == this.Id).ToList();
                session.RemoveSubscriptions(subscriptionsToRemove);

                // add transferred subscription to session
                if (!session.AddSubscription(this))
                {
                    Utils.LogError("SubscriptionId {0}: Failed to add transferred subscription to SessionId={1}.", Id, session.SessionId);
                    return false;
                }
            }
            else
            {
                // handle the case when the client restarts and loads the saved subscriptions from storage
                if (!GetMonitoredItems(out UInt32Collection serverHandles, out UInt32Collection clientHandles))
                {
                    Utils.LogError("SubscriptionId {0}: The server failed to respond to GetMonitoredItems after transfer.", Id);
                    return false;
                }

                int monitoredItemsCount = m_monitoredItems.Count;
                if (serverHandles.Count != monitoredItemsCount ||
                    clientHandles.Count != monitoredItemsCount)
                {
                    // invalid state
                    Utils.LogError("SubscriptionId {0}: Number of Monitored Items on client and server do not match after transfer {1}!={2}",
                        Id, serverHandles.Count, monitoredItemsCount);
                    return false;
                }

                // sets state to 'Created'
                m_id = id;
                TransferItems(serverHandles, clientHandles, out IList<MonitoredItem> itemsToModify);

                ModifyItems();
            }

            // add available sequence numbers to incoming 
            ProcessTransferredSequenceNumbers(availableSequenceNumbers);

            m_changeMask |= SubscriptionChangeMask.Transferred;
            ChangesCompleted();

            StartKeepAliveTimer();

            TraceState("TRANSFERRED");

            return true;
        }

#if CLIENT_ASYNC
        /// <summary>
        /// Called after the subscription was transferred.
        /// </summary>
        /// <param name="session">The session to which the subscription is transferred.</param>
        /// <param name="id">Id of the transferred subscription.</param>
        /// <param name="availableSequenceNumbers">The available sequence numbers on the server.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<bool> TransferAsync(ISession session, uint id, UInt32Collection availableSequenceNumbers, CancellationToken ct = default)
        {
            if (Created)
            {
                // handle the case when the client has the subscription template and reconnects
                if (id != m_id)
                {
                    return false;
                }

                // remove the subscription from disconnected session
                if (m_session?.RemoveTransferredSubscription(this) != true)
                {
                    Utils.LogError("SubscriptionId {0}: Failed to remove transferred subscription from owner SessionId={1}.", Id, m_session?.SessionId);
                    return false;
                }

                // remove default subscription template which was copied in Session.Create()
                var subscriptionsToRemove = session.Subscriptions.Where(s => !s.Created && s.TransferId == this.Id).ToList();
                await session.RemoveSubscriptionsAsync(subscriptionsToRemove, ct).ConfigureAwait(false);

                // add transferred subscription to session
                if (!session.AddSubscription(this))
                {
                    Utils.LogError("SubscriptionId {0}: Failed to add transferred subscription to SessionId={1}.", Id, session.SessionId);
                    return false;
                }
            }
            else
            {
                // handle the case when the client restarts and loads the saved subscriptions from storage
                bool success;
                UInt32Collection serverHandles;
                UInt32Collection clientHandles;
                (success, serverHandles, clientHandles) = await GetMonitoredItemsAsync(ct).ConfigureAwait(false);
                if (!success)
                {
                    Utils.LogError("SubscriptionId {0}: The server failed to respond to GetMonitoredItems after transfer.", Id);
                    return false;
                }

                int monitoredItemsCount = m_monitoredItems.Count;
                if (serverHandles.Count != monitoredItemsCount ||
                    clientHandles.Count != monitoredItemsCount)
                {
                    // invalid state
                    Utils.LogError("SubscriptionId {0}: Number of Monitored Items on client and server do not match after transfer {1}!={2}",
                        Id, serverHandles.Count, monitoredItemsCount);
                    return false;
                }

                // sets state to 'Created'
                m_id = id;
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
#endif

        /// <summary>
        /// Deletes a subscription on the server.
        /// </summary>
        public void Delete(bool silent)
        {
            if (!silent)
            {
                VerifySubscriptionState(true);
            }

            // nothing to do if not created.
            if (!this.Created)
            {
                return;
            }

            try
            {
                TraceState("DELETE");

                lock (m_cache)
                {
                    ResetPublishTimerAndWorkerState();
                }

                // delete the subscription.
                UInt32Collection subscriptionIds = new uint[] { m_id };

                StatusCodeCollection results;
                DiagnosticInfoCollection diagnosticInfos;

                ResponseHeader responseHeader = m_session.DeleteSubscriptions(
                    null,
                    subscriptionIds,
                    out results,
                    out diagnosticInfos);

                // validate response.
                ClientBase.ValidateResponse(results, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(results[0]))
                {
                    throw new ServiceResultException(ClientBase.GetResult(results[0], 0, diagnosticInfos, responseHeader));
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
        public void Modify()
        {
            VerifySubscriptionState(true);

            // modify the subscription.
            double revisedPublishingInterval;
            uint revisedKeepAliveCount = m_keepAliveCount;
            uint revisedLifetimeCounter = m_lifetimeCount;

            AdjustCounts(ref revisedKeepAliveCount, ref revisedLifetimeCounter);

            m_session.ModifySubscription(
                null,
                m_id,
                m_publishingInterval,
                revisedLifetimeCounter,
                revisedKeepAliveCount,
                m_maxNotificationsPerPublish,
                m_priority,
                out revisedPublishingInterval,
                out revisedLifetimeCounter,
                out revisedKeepAliveCount);

            // update current state.
            ModifySubscription(
                revisedPublishingInterval,
                revisedKeepAliveCount,
                revisedLifetimeCounter);

            ChangesCompleted();

            TraceState("MODIFIED");
        }

        /// <summary>
        /// Changes the publishing enabled state for the subscription.
        /// </summary>
        public void SetPublishingMode(bool enabled)
        {
            VerifySubscriptionState(true);

            // modify the subscription.
            UInt32Collection subscriptionIds = new uint[] { m_id };

            StatusCodeCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.SetPublishingMode(
                null,
                enabled,
                new uint[] { m_id },
                out results,
                out diagnosticInfos);

            // validate response.
            ClientBase.ValidateResponse(results, subscriptionIds);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);

            if (StatusCode.IsBad(results[0]))
            {
                throw new ServiceResultException(ClientBase.GetResult(results[0], 0, diagnosticInfos, responseHeader));
            }

            // update current state.
            m_currentPublishingEnabled = m_publishingEnabled = enabled;

            m_changeMask |= SubscriptionChangeMask.Modified;
            ChangesCompleted();

            TraceState(enabled ? "PUBLISHING ENABLED" : "PUBLISHING DISABLED");
        }

        /// <summary>
        /// Republishes the specified notification message.
        /// </summary>
        public NotificationMessage Republish(uint sequenceNumber)
        {
            VerifySubscriptionState(true);

            NotificationMessage message;

            m_session.Republish(
                null,
                m_id,
                sequenceNumber,
                out message);

            return message;
        }

        /// <summary>
        /// Applies any changes to the subscription items.
        /// </summary>
        public void ApplyChanges()
        {
            DeleteItems();
            ModifyItems();
            CreateItems();
        }

        /// <summary>
        /// Resolves all relative paths to nodes on the server.
        /// </summary>
        public void ResolveItemNodeIds()
        {
            VerifySubscriptionState(true);

            // collect list of browse paths.
            BrowsePathCollection browsePaths = new BrowsePathCollection();
            List<MonitoredItem> itemsToBrowse = new List<MonitoredItem>();

            PrepareResolveItemNodeIds(browsePaths, itemsToBrowse);

            // nothing to do.
            if (browsePaths.Count == 0)
            {
                return;
            }

            // translate browse paths.
            BrowsePathResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.TranslateBrowsePathsToNodeIds(
                null,
                browsePaths,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToBrowse[ii].SetResolvePathResult(results[ii], ii, diagnosticInfos, responseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsModified;
        }

        /// <summary>
        /// Creates all items that have not already been created.
        /// </summary>
        public IList<MonitoredItem> CreateItems()
        {
            List<MonitoredItem> itemsToCreate;
            MonitoredItemCreateRequestCollection requestItems = PrepareItemsToCreate(out itemsToCreate);

            if (requestItems.Count == 0)
            {
                return itemsToCreate;
            }

            // create monitored items.
            MonitoredItemCreateResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.CreateMonitoredItems(
                null,
                m_id,
                m_timestampsToReturn,
                requestItems,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, itemsToCreate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToCreate);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToCreate[ii].SetCreateResult(requestItems[ii], results[ii], ii, diagnosticInfos, responseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsCreated;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToCreate;
        }

        /// <summary>
        /// Modifies all items that have been changed.
        /// </summary>
        public IList<MonitoredItem> ModifyItems()
        {
            VerifySubscriptionState(true);

            MonitoredItemModifyRequestCollection requestItems = new MonitoredItemModifyRequestCollection();
            List<MonitoredItem> itemsToModify = new List<MonitoredItem>();

            PrepareItemsToModify(requestItems, itemsToModify);

            if (requestItems.Count == 0)
            {
                return itemsToModify;
            }

            // modify the subscription.
            MonitoredItemModifyResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.ModifyMonitoredItems(
                null,
                m_id,
                m_timestampsToReturn,
                requestItems,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, itemsToModify);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToModify);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToModify[ii].SetModifyResult(requestItems[ii], results[ii], ii, diagnosticInfos, responseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsModified;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToModify;
        }

        /// <summary>
        /// Deletes all items that have been marked for deletion.
        /// </summary>
        public IList<MonitoredItem> DeleteItems()
        {
            VerifySubscriptionState(true);

            if (m_deletedItems.Count == 0)
            {
                return new List<MonitoredItem>();
            }

            List<MonitoredItem> itemsToDelete = m_deletedItems;
            m_deletedItems = new List<MonitoredItem>();

            UInt32Collection monitoredItemIds = new UInt32Collection();

            foreach (MonitoredItem monitoredItem in itemsToDelete)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            StatusCodeCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.DeleteMonitoredItems(
                null,
                m_id,
                monitoredItemIds,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, monitoredItemIds);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, monitoredItemIds);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToDelete[ii].SetDeleteResult(results[ii], ii, diagnosticInfos, responseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsDeleted;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToDelete;
        }

        /// <summary>
        /// Set monitoring mode of items.
        /// </summary>
        public List<ServiceResult> SetMonitoringMode(
            MonitoringMode monitoringMode,
            IList<MonitoredItem> monitoredItems)
        {
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));

            VerifySubscriptionState(true);

            if (monitoredItems.Count == 0)
            {
                return null;
            }

            // get list of items to update.
            UInt32Collection monitoredItemIds = new UInt32Collection();
            foreach (MonitoredItem monitoredItem in monitoredItems)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            StatusCodeCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.SetMonitoringMode(
                null,
                m_id,
                monitoringMode,
                monitoredItemIds,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, monitoredItemIds);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, monitoredItemIds);

            // update results.
            List<ServiceResult> errors = new List<ServiceResult>();
            bool noErrors = UpdateMonitoringMode(
                monitoredItems, errors, results,
                diagnosticInfos, responseHeader,
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
        /// Adds the notification message to internal cache.
        /// </summary>
        public void SaveMessageInCache(
            IList<uint> availableSequenceNumbers,
            NotificationMessage message,
            IList<string> stringTable)
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
                    callback = m_publishStatusChanged;
                    TraceState("PUBLISHING RECOVERED");
                }

                DateTime now = DateTime.UtcNow;
                Interlocked.Exchange(ref m_lastNotificationTime, now.Ticks);
                int tickCount = HiResClock.TickCount;
                m_lastNotificationTickCount = tickCount;

                // save the string table that came with notification.
                message.StringTable = new List<string>(stringTable);

                // create queue for the first time.
                if (m_incomingMessages == null)
                {
                    m_incomingMessages = new LinkedList<IncomingMessage>();
                }

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
                        IncomingMessage placeholder = new IncomingMessage();
                        placeholder.SequenceNumber = entry.SequenceNumber + 1;
                        placeholder.Timestamp = now;
                        placeholder.TickCount = tickCount;
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
                        !(entry.Republished && (entry.RepublishStatus != StatusCodes.Good || (tickCount - entry.TickCount) > kRepublishMessageExpiredTimeout)))
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
                                Utils.LogWarning("SubscriptionId {0} skipping PublishResponse Sequence Number {1}", Id, entry.SequenceNumber);
                            }

                            m_lastSequenceNumberProcessed = entry.SequenceNumber;
                        }

                        m_incomingMessages.Remove(node);
                    }

                    node = next;
                }
            }

            // send notification that publishing received a keep alive or has to republish.
            if (callback != null)
            {
                try
                {
                    callback(this, new PublishStateChangedEventArgs(PublishStateChangedMask.Recovered));
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Error while raising PublishStateChanged event.");
                }
            }

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
        public void AddItem(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));

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
        public void AddItems(IEnumerable<MonitoredItem> monitoredItems)
        {
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));

            bool added = false;

            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in monitoredItems)
                {
                    if (!m_monitoredItems.ContainsKey(monitoredItem.ClientHandle))
                    {
                        m_monitoredItems.Add(monitoredItem.ClientHandle, monitoredItem);
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
        public void RemoveItem(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));

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
        public void RemoveItems(IEnumerable<MonitoredItem> monitoredItems)
        {
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));

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
                MonitoredItem monitoredItem = null;

                if (m_monitoredItems.TryGetValue(clientHandle, out monitoredItem))
                {
                    return monitoredItem;
                }

                return null;
            }
        }

        /// <summary>
        /// Tells the server to refresh all conditions being monitored by the subscription.
        /// </summary>
        public bool ConditionRefresh()
        {
            VerifySubscriptionState(true);

            try
            {
                m_session.Call(
                    ObjectTypeIds.ConditionType,
                    MethodIds.ConditionType_ConditionRefresh,
                    m_id);

                return true;
            }
            catch (ServiceResultException sre)
            {
                Utils.LogError(sre, "SubscriptionId {0}: Failed to call ConditionRefresh on server", m_id);
            }
            return false;
        }

        /// <summary>
        /// Call the ResendData method on the server for this subscription.
        /// </summary>
        public bool ResendData()
        {
            VerifySubscriptionState(true);

            try
            {
                m_session.Call(ObjectIds.Server, MethodIds.Server_ResendData, m_id);
                return true;
            }
            catch (ServiceResultException sre)
            {
                Utils.LogError(sre, "SubscriptionId {0}: Failed to call ResendData on server", m_id);
            }
            return false;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the available sequence numbers and queues after transfer.
        /// </summary>
        /// <remarks>
        /// If <see cref="RepublishAfterTransfer"/> is set to <c>true</c>, sequence numbers
        /// are queued for republish, otherwise ack may be sent.
        /// </remarks>
        /// <param name="availableSequenceNumbers">The list of available sequence numbers on the server.</param>
        private void ProcessTransferredSequenceNumbers(UInt32Collection availableSequenceNumbers)
        {
            lock (m_cache)
            {
                // reset incoming state machine and clear cache
                m_lastSequenceNumberProcessed = 0;
                m_resyncLastSequenceNumberProcessed = true;
                m_incomingMessages = new LinkedList<IncomingMessage>();

                // save available sequence numbers
                m_availableSequenceNumbers = (UInt32Collection)availableSequenceNumbers.MemberwiseClone();

                if (availableSequenceNumbers.Count != 0 && m_republishAfterTransfer)
                {
                    // create queue for the first time.
                    if (m_incomingMessages == null)
                    {
                        m_incomingMessages = new LinkedList<IncomingMessage>();
                    }

                    // update last sequence number processed
                    // available seq numbers may not be in order
                    foreach (var sequenceNumber in availableSequenceNumbers)
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
                        foreach (var sequenceNumber in availableSequenceNumbers)
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

                    Utils.LogInfo("SubscriptionId {0}: Republishing {1} messages, next sequencenumber {2} after transfer.",
                        m_id, republishMessages, m_lastSequenceNumberProcessed);

                    availableSequenceNumbers.Clear();
                }
            }
        }

        /// <summary>
        /// Call the GetMonitoredItems method on the server.
        /// </summary>
        private bool GetMonitoredItems(out UInt32Collection serverHandles, out UInt32Collection clientHandles)
        {
            serverHandles = new UInt32Collection();
            clientHandles = new UInt32Collection();
            try
            {
                var outputArguments = m_session.Call(ObjectIds.Server, MethodIds.Server_GetMonitoredItems, m_transferId);
                if (outputArguments != null && outputArguments.Count == 2)
                {
                    serverHandles.AddRange((uint[])outputArguments[0]);
                    clientHandles.AddRange((uint[])outputArguments[1]);
                    return true;
                }
            }
            catch (ServiceResultException sre)
            {
                Utils.LogError(sre, "SubscriptionId {0}: Failed to call GetMonitoredItems on server", m_id);
            }
            return false;
        }

#if CLIENT_ASYNC
        /// <summary>
        /// Call the GetMonitoredItems method on the server.
        /// </summary>
        private async Task<(bool, UInt32Collection, UInt32Collection)> GetMonitoredItemsAsync(CancellationToken ct = default)
        {
            var serverHandles = new UInt32Collection();
            var clientHandles = new UInt32Collection();
            try
            {
                var outputArguments = await m_session.CallAsync(ObjectIds.Server, MethodIds.Server_GetMonitoredItems, ct, m_transferId).ConfigureAwait(false);
                if (outputArguments != null && outputArguments.Count == 2)
                {
                    serverHandles.AddRange((uint[])outputArguments[0]);
                    clientHandles.AddRange((uint[])outputArguments[1]);
                    return (true, serverHandles, clientHandles);
                }
            }
            catch (ServiceResultException sre)
            {
                Utils.LogError(sre, "SubscriptionId {0}: Failed to call GetMonitoredItems on server", m_id);
            }
            return (false, serverHandles, clientHandles);
        }
#endif

        /// <summary>
        /// Starts a timer to ensure publish requests are sent frequently enough to detect network interruptions.
        /// </summary>
        private void StartKeepAliveTimer()
        {
            // stop the publish timer.
            lock (m_cache)
            {
                Utils.SilentDispose(m_publishTimer);
                m_publishTimer = null;

                Interlocked.Exchange(ref m_lastNotificationTime, DateTime.UtcNow.Ticks);
                m_lastNotificationTickCount = HiResClock.TickCount;
                m_keepAliveInterval = (int)(Math.Min(m_currentPublishingInterval * (m_currentKeepAliveCount + 1), Int32.MaxValue));
                if (m_keepAliveInterval < kMinKeepAliveTimerInterval)
                {
                    m_keepAliveInterval = (int)(Math.Min(m_publishingInterval * (m_keepAliveCount + 1), Int32.MaxValue));
                    m_keepAliveInterval = Math.Max(kMinKeepAliveTimerInterval, m_keepAliveInterval);
                }
#if NET6_0_OR_GREATER
                var publishTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(m_keepAliveInterval));
                _ = Task.Run(() => OnKeepAliveAsync(publishTimer));
                m_publishTimer = publishTimer;
#else
                m_publishTimer = new Timer(OnKeepAlive, m_keepAliveInterval, m_keepAliveInterval, m_keepAliveInterval);
#endif

                if (m_messageWorkerTask == null || m_messageWorkerTask.IsCompleted)
                {
                    Utils.SilentDispose(m_messageWorkerCts);
                    m_messageWorkerCts = new CancellationTokenSource();
                    var ct = m_messageWorkerCts.Token;
                    m_messageWorkerTask = Task.Run(() => {
                        return PublishResponseMessageWorkerAsync(ct);
                    });
                }
            }

            // start publishing. Fill the queue.
            m_session.StartPublishing(BeginPublishTimeout(), false);
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Checks if a notification has arrived. Sends a publish if it has not.
        /// </summary>
        private async Task OnKeepAliveAsync(PeriodicTimer publishTimer)
        {
            while (await publishTimer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                if (!PublishingStopped)
                {
                    continue;
                }

                HandleOnKeepAliveStopped();
            }
        }
#else
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
#endif

        /// <summary>
        /// Handles callback if publishing stopped. Sends a publish.
        /// </summary>
        private void HandleOnKeepAliveStopped()
        {
            // check if a publish has arrived.
            PublishStateChangedEventHandler callback = m_publishStatusChanged;

            Interlocked.Increment(ref m_publishLateCount);

            TraceState("PUBLISHING STOPPED");

            if (callback != null)
            {
                try
                {
                    callback(this, new PublishStateChangedEventArgs(PublishStateChangedMask.Stopped));
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Error while raising PublishStateChanged event.");
                }
            }

            // try to send a publish to recover stopped publishing.
            m_session?.BeginPublish(BeginPublishTimeout());
        }

        /// <summary>
        /// Publish response worker task for the subscriptions.
        /// </summary>
        private async Task PublishResponseMessageWorkerAsync(CancellationToken ct)
        {
            Utils.LogTrace("SubscriptionId {0} - Publish Thread {1:X8} Started.", m_id, Environment.CurrentManagedThreadId);

            bool cancelled;
            try
            {
                do
                {
                    await m_messageWorkerEvent.WaitAsync().ConfigureAwait(false);

                    cancelled = ct.IsCancellationRequested;
                    if (!cancelled)
                    {
                        await OnMessageReceivedAsync(ct).ConfigureAwait(false);
                        cancelled = ct.IsCancellationRequested;
                    }
                }
                while (!cancelled);
            }
            catch (OperationCanceledException)
            {
                // intentionally fall through
            }
            catch (Exception e)
            {
                Utils.LogError(e, "SubscriptionId {0} - Publish Worker Thread {1:X8} Exited Unexpectedly.", m_id, Environment.CurrentManagedThreadId);
                return;
            }

            Utils.LogTrace("SubscriptionId {0} - Publish Thread {1:X8} Exited Normally.", m_id, Environment.CurrentManagedThreadId);
        }

        /// <summary>
        /// Dumps the current state of the session queue.
        /// </summary>
        internal void TraceState(string context)
        {
            CoreClientUtils.EventLog.SubscriptionState(context, m_id, new DateTime(m_lastNotificationTime), m_session?.GoodPublishRequestCount ?? 0,
                m_currentPublishingInterval, m_currentKeepAliveCount, m_currentPublishingEnabled, MonitoredItemCount);
        }

        /// <summary>
        /// Calculate the timeout of a publish request.
        /// </summary>
        private int BeginPublishTimeout()
        {
            return Math.Max(Math.Min(m_keepAliveInterval * 3, Int32.MaxValue), kMinKeepAliveTimerInterval);
        }

        /// <summary>
        /// Update the subscription with the given revised settings.
        /// </summary>
        private void ModifySubscription(
            double revisedPublishingInterval,
            uint revisedKeepAliveCount,
            uint revisedLifetimeCounter
            )
        {
            CreateOrModifySubscription(false, 0,
                revisedPublishingInterval, revisedKeepAliveCount, revisedLifetimeCounter);
        }

        /// <summary>
        /// Update the subscription with the given revised settings.
        /// </summary>
        private void CreateSubscription(
            uint subscriptionId,
            double revisedPublishingInterval,
            uint revisedKeepAliveCount,
            uint revisedLifetimeCounter
            )
        {
            CreateOrModifySubscription(true, subscriptionId,
                revisedPublishingInterval, revisedKeepAliveCount, revisedLifetimeCounter);
        }

        /// <summary>
        /// Update the subscription with the given revised settings.
        /// </summary>
        private void CreateOrModifySubscription(
            bool created,
            uint subscriptionId,
            double revisedPublishingInterval,
            uint revisedKeepAliveCount,
            uint revisedLifetimeCounter
            )
        {
            // update current state.
            m_currentPublishingInterval = revisedPublishingInterval;
            m_currentKeepAliveCount = revisedKeepAliveCount;
            m_currentLifetimeCount = revisedLifetimeCounter;
            m_currentPriority = m_priority;

            if (!created)
            {
                m_changeMask |= SubscriptionChangeMask.Modified;
            }
            else
            {
                m_currentPublishingEnabled = m_publishingEnabled;
                m_transferId = m_id = subscriptionId;
                StartKeepAliveTimer();
                m_changeMask |= SubscriptionChangeMask.Created;
            }

            if (m_keepAliveCount != revisedKeepAliveCount)
            {
                Utils.LogInfo("For subscription {0}, Keep alive count was revised from {1} to {2}",
                    Id, m_keepAliveCount, revisedKeepAliveCount);
            }

            if (m_lifetimeCount != revisedLifetimeCounter)
            {
                Utils.LogInfo("For subscription {0}, Lifetime count was revised from {1} to {2}",
                    Id, m_lifetimeCount, revisedLifetimeCounter);
            }

            if (m_publishingInterval != revisedPublishingInterval)
            {
                Utils.LogInfo("For subscription {0}, Publishing interval was revised from {1} to {2}",
                    Id, m_publishingInterval, revisedPublishingInterval);
            }

            if (revisedLifetimeCounter < revisedKeepAliveCount * 3)
            {
                Utils.LogInfo("For subscription {0}, Revised lifetime counter (value={1}) is less than three times the keep alive count (value={2})", Id, revisedLifetimeCounter, revisedKeepAliveCount);
            }

            if (m_currentPriority == 0)
            {
                Utils.LogInfo("For subscription {0}, the priority was set to 0.", Id);
            }
        }

        /// <summary>
        /// Delete the subscription.
        /// Ignore errors, always reset all parameter.
        /// </summary>
        private void DeleteSubscription()
        {
            m_transferId = m_id = 0;
            m_currentPublishingInterval = 0;
            m_currentKeepAliveCount = 0;
            m_currentPublishingEnabled = false;
            m_currentPriority = 0;

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
                Utils.LogInfo("Adjusted KeepAliveCount from value={0}, to value={1}, for subscription {2}.",
                    keepAliveCount, kDefaultKeepAlive, Id);
                keepAliveCount = kDefaultKeepAlive;
            }

            // ensure the lifetime is sensible given the sampling interval.
            if (m_publishingInterval > 0)
            {
                if (m_minLifetimeInterval > 0 && m_minLifetimeInterval < m_session.SessionTimeout)
                {
                    Utils.LogWarning("A smaller minLifetimeInterval {0}ms than session timeout {1}ms configured for subscription {2}.",
                        m_minLifetimeInterval, m_session.SessionTimeout, Id);
                }

                uint minLifetimeCount = (uint)(m_minLifetimeInterval / m_publishingInterval);

                if (lifetimeCount < minLifetimeCount)
                {
                    lifetimeCount = minLifetimeCount;

                    if (m_minLifetimeInterval % m_publishingInterval != 0)
                    {
                        lifetimeCount++;
                    }

                    Utils.LogInfo("Adjusted LifetimeCount to value={0}, for subscription {1}. ",
                        lifetimeCount, Id);
                }

                if (lifetimeCount * m_publishingInterval < m_session.SessionTimeout)
                {
                    Utils.LogWarning("Lifetime {0}ms configured for subscription {1} is less than session timeout {2}ms.",
                        lifetimeCount * m_publishingInterval, Id, m_session.SessionTimeout);
                }
            }
            else if (lifetimeCount == 0)
            {
                // don't know what the sampling interval will be - use something large enough
                // to ensure the user does not experience unexpected drop outs.
                Utils.LogInfo("Adjusted LifetimeCount from value={0}, to value={1}, for subscription {2}. ",
                    lifetimeCount, kDefaultLifeTime, Id);
                lifetimeCount = kDefaultLifeTime;
            }

            // validate spec: lifetimecount shall be at least 3*keepAliveCount
            uint minLifeTimeCount = 3 * keepAliveCount;
            if (lifetimeCount < minLifeTimeCount)
            {
                Utils.LogInfo("Adjusted LifetimeCount from value={0}, to value={1}, for subscription {2}. ",
                    lifetimeCount, minLifeTimeCount, Id);
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

                    for (LinkedListNode<IncomingMessage> ii = m_incomingMessages.First; ii != null; ii = ii.Next)
                    {
                        // update monitored items with unprocessed messages.
                        if (ii.Value.Message != null && !ii.Value.Processed &&
                            (!m_sequentialPublishing || ValidSequentialPublishMessage(ii.Value)))
                        {
                            if (messagesToProcess == null)
                            {
                                messagesToProcess = new List<NotificationMessage>();
                            }

                            messagesToProcess.Add(ii.Value.Message);

                            // remove the oldest items.
                            while (m_messageCache.Count > m_maxMessageCount)
                            {
                                m_messageCache.RemoveFirst();
                            }

                            m_messageCache.AddLast(ii.Value.Message);
                            ii.Value.Processed = true;

                            // Keep the last sequence number processed going up
                            if (ii.Value.SequenceNumber > m_lastSequenceNumberProcessed ||
                               (ii.Value.SequenceNumber == 1 && m_lastSequenceNumberProcessed == uint.MaxValue))
                            {
                                m_lastSequenceNumberProcessed = ii.Value.SequenceNumber;
                                if (m_resyncLastSequenceNumberProcessed)
                                {
                                    Utils.LogInfo("SubscriptionId {0}: Resynced last sequence number processed to {1}.",
                                        Id, m_lastSequenceNumberProcessed);
                                    m_resyncLastSequenceNumberProcessed = false;
                                }
                            }
                        }

                        // process keep alive messages
                        else if (ii.Next == null && ii.Value.Message == null && !ii.Value.Processed)
                        {
                            if (keepAliveToProcess == null)
                            {
                                keepAliveToProcess = new List<IncomingMessage>();
                            }
                            keepAliveToProcess.Add(ii.Value);
                            publishStateChangedMask |= PublishStateChangedMask.KeepAlive;
                        }

                        // check for missing messages.
                        else if (ii.Next != null && ii.Value.Message == null && !ii.Value.Processed && !ii.Value.Republished)
                        {
                            // tolerate if a single request was received out of order
                            if (ii.Next.Next != null &&
                                (HiResClock.TickCount - ii.Value.TickCount) > kRepublishMessageTimeout)
                            {
                                ii.Value.Republished = true;
                                publishStateChangedMask |= PublishStateChangedMask.Republish;

                                // only call republish if the sequence number is available
                                if (m_availableSequenceNumbers?.Contains(ii.Value.SequenceNumber) == true)
                                {
                                    if (messagesToRepublish == null)
                                    {
                                        messagesToRepublish = new List<IncomingMessage>();
                                    }

                                    messagesToRepublish.Add(ii.Value);
                                }
                                else
                                {
                                    Utils.LogInfo("Skipped to receive RepublishAsync for {0}-{1}-BadMessageNotAvailable", subscriptionId, ii.Value.SequenceNumber);
                                    ii.Value.RepublishStatus = StatusCodes.BadMessageNotAvailable;
                                }
                            }
                        }
#if DEBUG
                        // a message that is deferred because of a missing sequence number
                        else if (ii.Value.Message != null && !ii.Value.Processed)
                        {
                            Utils.LogDebug("Subscription {0}: Delayed message with sequence number {1}, expected sequence number is {2}.",
                                Id, ii.Value.SequenceNumber, m_lastSequenceNumberProcessed + 1);
                        }
#endif
                    }

                    session = m_session;
                    subscriptionId = m_id;
                    callback = m_publishStatusChanged;
                }

                // process new keep alive messages.
                FastKeepAliveNotificationEventHandler keepAliveCallback = m_fastKeepAliveCallback;
                if (keepAliveToProcess != null && keepAliveCallback != null)
                {
                    foreach (IncomingMessage message in keepAliveToProcess)
                    {
                        var keepAlive = new NotificationData {
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
                    FastDataChangeNotificationEventHandler datachangeCallback = m_fastDataChangeCallback;
                    FastEventNotificationEventHandler eventCallback = m_fastEventCallback;

                    foreach (NotificationMessage message in messagesToProcess)
                    {
                        noNotificationsReceived = 0;
                        try
                        {
                            foreach (ExtensionObject notificationData in message.NotificationData)
                            {
                                var datachange = notificationData.Body as DataChangeNotification;

                                if (datachange != null)
                                {
                                    datachange.PublishTime = message.PublishTime;
                                    datachange.SequenceNumber = message.SequenceNumber;

                                    noNotificationsReceived += datachange.MonitoredItems.Count;

                                    if (!m_disableMonitoredItemCache)
                                    {
                                        SaveDataChange(message, datachange, message.StringTable);
                                    }

                                    datachangeCallback?.Invoke(this, datachange, message.StringTable);
                                }

                                var events = notificationData.Body as EventNotificationList;

                                if (events != null)
                                {
                                    events.PublishTime = message.PublishTime;
                                    events.SequenceNumber = message.SequenceNumber;

                                    noNotificationsReceived += events.Events.Count;

                                    if (!m_disableMonitoredItemCache)
                                    {
                                        SaveEvents(message, events, message.StringTable);
                                    }

                                    eventCallback?.Invoke(this, events, message.StringTable);
                                }

                                StatusChangeNotification statusChanged = notificationData.Body as StatusChangeNotification;

                                if (statusChanged != null)
                                {
                                    statusChanged.PublishTime = message.PublishTime;
                                    statusChanged.SequenceNumber = message.SequenceNumber;

                                    Utils.LogWarning("StatusChangeNotification received with Status = {0} for SubscriptionId={1}.",
                                        statusChanged.Status.ToString(), Id);

                                    if (statusChanged.Status == StatusCodes.GoodSubscriptionTransferred)
                                    {
                                        publishStateChangedMask |= PublishStateChangedMask.Transferred;
                                        ResetPublishTimerAndWorkerState();
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
                            Utils.LogError(e, "Error while processing incoming message #{0}.", message.SequenceNumber);
                        }

                        if (MaxNotificationsPerPublish != 0 && noNotificationsReceived > MaxNotificationsPerPublish)
                        {
                            Utils.LogWarning("For subscription {0}, more notifications were received={1} than the max notifications per publish value={2}",
                                Id, noNotificationsReceived, MaxNotificationsPerPublish);
                        }
                    }
                    if ((callback != null) && (publishStateChangedMask != PublishStateChangedMask.None))
                    {
                        try
                        {
                            callback(this, new PublishStateChangedEventArgs(publishStateChangedMask));
                        }
                        catch (Exception e)
                        {
                            Utils.LogError(e, "Error while raising PublishStateChanged event.");
                        }
                    }
                }

                // do any re-publishes.
                if (messagesToRepublish != null && session != null && subscriptionId != 0)
                {
                    int count = messagesToRepublish.Count;
                    var tasks = new Task<(bool, ServiceResult)>[count];
                    for (int ii = 0; ii < count; ii++)
                    {
                        tasks[ii] = session.RepublishAsync(subscriptionId, messagesToRepublish[ii].SequenceNumber, ct);
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
            catch (Exception e)
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
        private void VerifySubscriptionState(bool created)
        {
            if (created && m_id == 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Subscription has not been created.");
            }

            if (!created && m_id != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Subscription has already been created.");
            }

            if (!created && Session is null) // Occurs only on Create() and CreateAsync()
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Subscription has not been assigned to a Session");
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
                message.SequenceNumber == 1 && m_lastSequenceNumberProcessed == uint.MaxValue;
        }

        /// <summary>
        /// Update the results to monitored items
        /// after updating the monitoring mode.
        /// </summary>
        private bool UpdateMonitoringMode(
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
        private MonitoredItemCreateRequestCollection PrepareItemsToCreate(out List<MonitoredItem> itemsToCreate)
        {
            VerifySubscriptionState(true);

            ResolveItemNodeIds();

            MonitoredItemCreateRequestCollection requestItems = new MonitoredItemCreateRequestCollection();
            itemsToCreate = new List<MonitoredItem>();

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
                    MonitoredItemCreateRequest request = new MonitoredItemCreateRequest();

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
                        request.RequestedParameters.Filter = new ExtensionObject(monitoredItem.Filter);
                    }

                    requestItems.Add(request);
                    itemsToCreate.Add(monitoredItem);
                }
            }
            return requestItems;
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
                    MonitoredItemModifyRequest request = new MonitoredItemModifyRequest();

                    request.MonitoredItemId = monitoredItem.Status.Id;
                    request.RequestedParameters.ClientHandle = monitoredItem.ClientHandle;
                    request.RequestedParameters.SamplingInterval = monitoredItem.SamplingInterval;
                    request.RequestedParameters.QueueSize = monitoredItem.QueueSize;
                    request.RequestedParameters.DiscardOldest = monitoredItem.DiscardOldest;

                    if (monitoredItem.Filter != null)
                    {
                        request.RequestedParameters.Filter = new ExtensionObject(monitoredItem.Filter);
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
                itemsToModify = new List<MonitoredItem>();
                var updatedMonitoredItems = new SortedDictionary<uint, MonitoredItem>();
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    var index = serverHandles.FindIndex(handle => handle == monitoredItem.Status.Id);
                    if (index >= 0 && index < clientHandles.Count)
                    {
                        var clientHandle = clientHandles[index];
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
        private void PrepareResolveItemNodeIds(
            BrowsePathCollection browsePaths,
            List<MonitoredItem> itemsToBrowse)
        {
            lock (m_cache)
            {
                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    if (!String.IsNullOrEmpty(monitoredItem.RelativePath) && NodeId.IsNull(monitoredItem.ResolvedNodeId))
                    {
                        // cannot change the relative path after an item is created.
                        if (monitoredItem.Created)
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState, "Cannot modify item path after it is created.");
                        }

                        BrowsePath browsePath = new BrowsePath();

                        browsePath.StartingNode = monitoredItem.StartNodeId;

                        // parse the relative path.
                        try
                        {
                            browsePath.RelativePath = RelativePath.Parse(monitoredItem.RelativePath, m_session.TypeTree);
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
        private void SaveDataChange(NotificationMessage message, DataChangeNotification notifications, IList<string> stringTable)
        {
            // check for empty monitored items list.
            if (notifications.MonitoredItems == null || notifications.MonitoredItems.Count == 0)
            {
                Utils.LogInfo("Publish response contains empty MonitoredItems list for SubscriptionId = {0}.", m_id);
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
                        Utils.LogWarning("Publish response contains invalid MonitoredItem. SubscriptionId = {0}, ClientHandle = {1}", m_id, notification.ClientHandle);
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
        private void SaveEvents(NotificationMessage message, EventNotificationList notifications, IList<string> stringTable)
        {
            for (int ii = 0; ii < notifications.Events.Count; ii++)
            {
                EventFieldList eventFields = notifications.Events[ii];

                MonitoredItem monitoredItem = null;

                lock (m_cache)
                {
                    if (!m_monitoredItems.TryGetValue(eventFields.ClientHandle, out monitoredItem))
                    {
                        Utils.LogWarning("Publish response contains invalid MonitoredItem.SubscriptionId = {0}, ClientHandle = {1}", m_id, eventFields.ClientHandle);
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
        private IncomingMessage FindOrCreateEntry(DateTime utcNow, int tickCount, uint sequenceNumber)
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
                    entry = new IncomingMessage();
                    entry.SequenceNumber = sequenceNumber;
                    entry.Timestamp = utcNow;
                    entry.TickCount = tickCount;
                    m_incomingMessages.AddAfter(node, entry);
                    break;
                }

                node = previous;
                entry = null;
            }

            if (entry == null)
            {
                entry = new IncomingMessage();
                entry.SequenceNumber = sequenceNumber;
                entry.Timestamp = utcNow;
                entry.TickCount = tickCount;
                m_incomingMessages.AddLast(entry);
            }

            return entry;
        }
        #endregion

        #region Private Fields
        private string m_displayName;
        private int m_publishingInterval;
        private uint m_keepAliveCount;
        private uint m_lifetimeCount;
        private uint m_minLifetimeInterval;
        private uint m_maxNotificationsPerPublish;
        private bool m_publishingEnabled;
        private byte m_priority;
        private TimestampsToReturn m_timestampsToReturn;
        private List<MonitoredItem> m_deletedItems;
        private event SubscriptionStateChangedEventHandler m_StateChanged;
        private MonitoredItem m_defaultItem;
        private SubscriptionChangeMask m_changeMask;

        private ISession m_session;
        private object m_handle;
        private uint m_id;
        private uint m_transferId;
        private double m_currentPublishingInterval;
        private uint m_currentKeepAliveCount;
        private uint m_currentLifetimeCount;
        private bool m_currentPublishingEnabled;
        private byte m_currentPriority;
#if NET6_0_OR_GREATER
        private PeriodicTimer m_publishTimer;
#else
        private Timer m_publishTimer;
#endif
        private long m_lastNotificationTime;
        private int m_lastNotificationTickCount;
        private int m_keepAliveInterval;
        private int m_publishLateCount;
        private event PublishStateChangedEventHandler m_publishStatusChanged;

        private object m_cache = new object();
        private LinkedList<NotificationMessage> m_messageCache;
        private IList<uint> m_availableSequenceNumbers;
        private int m_maxMessageCount;
        private bool m_republishAfterTransfer;
        private SortedDictionary<uint, MonitoredItem> m_monitoredItems;
        private bool m_disableMonitoredItemCache;
        private FastDataChangeNotificationEventHandler m_fastDataChangeCallback;
        private FastEventNotificationEventHandler m_fastEventCallback;
        private FastKeepAliveNotificationEventHandler m_fastKeepAliveCallback;
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
        #endregion
    }

    #region SubscriptionChangeMask Enumeration
    /// <summary>
    /// Flags indicating what has changed in a subscription.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames"), Flags]
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
    #endregion

    #region PublishStateChangeMask Enumeration
    /// <summary>
    /// Flags indicating what has changed in a publish state change.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames"), Flags]
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
    }
    #endregion

    /// <summary>
    /// The delegate used to receive data change notifications via a direct function call instead of a .NET Event.
    /// </summary>
    public delegate void FastDataChangeNotificationEventHandler(Subscription subscription, DataChangeNotification notification, IList<string> stringTable);

    /// <summary>
    /// The delegate used to receive event notifications via a direct function call instead of a .NET Event.
    /// </summary>
    public delegate void FastEventNotificationEventHandler(Subscription subscription, EventNotificationList notification, IList<string> stringTable);

    /// <summary>
    /// The delegate used to receive keep alive notifications via a direct function call instead of a .NET Event.
    /// </summary>
    public delegate void FastKeepAliveNotificationEventHandler(Subscription subscription, NotificationData notification);

    #region SubscriptionStateChangedEventArgs Class
    /// <summary>
    /// The event arguments provided when the state of a subscription changes.
    /// </summary>
    public class SubscriptionStateChangedEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal SubscriptionStateChangedEventArgs(SubscriptionChangeMask changeMask)
        {
            m_changeMask = changeMask;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The changes that have affected the subscription.
        /// </summary>
        public SubscriptionChangeMask Status => m_changeMask;
        #endregion

        #region Private Fields
        private readonly SubscriptionChangeMask m_changeMask;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive subscription state change notifications.
    /// </summary>
    public delegate void SubscriptionStateChangedEventHandler(Subscription subscription, SubscriptionStateChangedEventArgs e);
    #endregion

    #region PublishStateChangedEventArgs Class
    /// <summary>
    /// The event arguments provided when the state of a subscription changes.
    /// </summary>
    public class PublishStateChangedEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal PublishStateChangedEventArgs(PublishStateChangedMask changeMask)
        {
            m_changeMask = changeMask;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The publish state changes.
        /// </summary>
        public PublishStateChangedMask Status => m_changeMask;
        #endregion

        #region Private Fields
        private readonly PublishStateChangedMask m_changeMask;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive publish state change notifications.
    /// </summary>
    public delegate void PublishStateChangedEventHandler(Subscription subscription, PublishStateChangedEventArgs e);
    #endregion

    /// <summary>
    /// A collection of subscriptions.
    /// </summary>
    [CollectionDataContract(Name = "ListOfSubscription", Namespace = Namespaces.OpcUaXsd, ItemName = "Subscription")]
    public partial class SubscriptionCollection : List<Subscription>, ICloneable
    {
        #region Constructors
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public SubscriptionCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The existing collection to use as the basis of creating this collection</param>
        public SubscriptionCollection(IEnumerable<Subscription> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max. capacity of the collection</param>
        public SubscriptionCollection(int capacity) : base(capacity) { }
        #endregion

        #region ICloneable Members
        /// <summary cref="ICloneable.Clone" />
        public virtual object Clone()
        {
            return (SubscriptionCollection)this.MemberwiseClone();
        }

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            SubscriptionCollection clone = new SubscriptionCollection();
            clone.AddRange(this.Select(item => (Subscription)item.Clone()));
            return clone;
        }

        /// <summary>
        /// Helper to clone a SubscriptionCollection with event handlers using the
        /// <see cref="Subscription.CloneSubscription(bool)"/> method.
        /// </summary>
        public virtual SubscriptionCollection CloneSubscriptions(bool copyEventhandlers)
        {
            SubscriptionCollection clone = new SubscriptionCollection();
            clone.AddRange(this.Select(item => (Subscription)item.CloneSubscription(copyEventhandlers)));
            return clone;
        }
        #endregion
    }
}
