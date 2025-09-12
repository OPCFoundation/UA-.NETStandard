/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages a subscription created by a client.
    /// </summary>
    public class Subscription : ISubscription
    {
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public Subscription(
            IServerInternal server,
            ISession session,
            uint subscriptionId,
            double publishingInterval,
            uint maxLifetimeCount,
            uint maxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            bool publishingEnabled,
            uint maxMessageCount)
        {
            Id = subscriptionId;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<Subscription>();
            m_publishingInterval = publishingInterval;
            m_maxLifetimeCount = maxLifetimeCount;
            m_maxKeepAliveCount = maxKeepAliveCount;
            m_maxNotificationsPerPublish = maxNotificationsPerPublish;
            m_publishingEnabled = publishingEnabled;
            Priority = priority;
            m_publishTimerExpiry = HiResClock.TickCount64 + (long)publishingInterval;
            m_keepAliveCounter = 0;
            m_lifetimeCounter = 0;
            m_waitingForPublish = false;
            m_maxMessageCount = maxMessageCount;
            m_sentMessages = [];
            m_supportsDurable = m_server.MonitoredItemQueueFactory.SupportsDurableQueues;
            IsDurable = false;

            m_monitoredItems = [];
            m_itemsToCheck = new LinkedList<IMonitoredItem>();
            m_itemsToPublish = new LinkedList<IMonitoredItem>();
            m_itemsToTrigger = [];

            // m_itemsReadyToPublish         = new Queue<IMonitoredItem>();
            // m_itemsNotificationsAvailable = new LinkedList<IMonitoredItem>();
            m_sequenceNumber = 1;

            // initialize diagnostics.
            Diagnostics = new SubscriptionDiagnosticsDataType
            {
                SessionId = Session.Id,
                SubscriptionId = Id,
                Priority = priority,
                PublishingInterval = publishingInterval,
                MaxKeepAliveCount = maxKeepAliveCount,
                MaxLifetimeCount = maxLifetimeCount,
                MaxNotificationsPerPublish = maxNotificationsPerPublish,
                PublishingEnabled = publishingEnabled,
                ModifyCount = 0,
                EnableCount = 0,
                DisableCount = 0,
                RepublishMessageRequestCount = 0,
                RepublishMessageCount = 0,
                TransferRequestCount = 0,
                TransferredToSameClientCount = 0,
                TransferredToAltClientCount = 0,
                PublishRequestCount = 0,
                DataChangeNotificationsCount = 0,
                EventNotificationsCount = 0,
                NotificationsCount = 0,
                LatePublishRequestCount = 0,
                CurrentKeepAliveCount = 0,
                CurrentLifetimeCount = 0,
                UnacknowledgedMessageCount = 0,
                DiscardedMessageCount = 0,
                MonitoredItemCount = 0,
                DisabledMonitoredItemCount = 0,
                MonitoringQueueOverflowCount = 0,
                NextSequenceNumber = (uint)m_sequenceNumber
            };

            ServerSystemContext systemContext = m_server.DefaultSystemContext.Copy(session);

            m_diagnosticsId = server.DiagnosticsNodeManager.CreateSubscriptionDiagnostics(
                systemContext,
                Diagnostics,
                OnUpdateDiagnostics);

            TraceState(LogLevel.Information, TraceStateId.Config, "CREATED");
        }

        /// <summary>
        /// Initialize subscription after a restart from a template
        /// </summary>
        public Subscription(IServerInternal server, IStoredSubscription storedSubscription)
        {
            if (server.IsRunning)
            {
                throw new InvalidOperationException(
                    "Subscription restore can only occur on startup");
            }

            m_server = server;
            m_logger = server.Telemetry.CreateLogger<Subscription>();
            Session = null;
            Id = storedSubscription.Id;
            m_publishingInterval = storedSubscription.PublishingInterval;
            m_maxLifetimeCount = storedSubscription.MaxLifetimeCount;
            m_lifetimeCounter = storedSubscription.LifetimeCounter;
            m_maxKeepAliveCount = storedSubscription.MaxKeepaliveCount;
            m_maxNotificationsPerPublish = storedSubscription.MaxNotificationsPerPublish;
            m_publishingEnabled = false;
            Priority = storedSubscription.Priority;
            m_publishTimerExpiry = HiResClock.TickCount64 +
                (long)storedSubscription.PublishingInterval;
            m_keepAliveCounter = 0;
            m_waitingForPublish = false;
            m_maxMessageCount = storedSubscription.MaxMessageCount;
            m_sentMessages = storedSubscription.SentMessages;
            m_supportsDurable = m_server.MonitoredItemQueueFactory.SupportsDurableQueues;
            IsDurable = storedSubscription.IsDurable;
            m_savedOwnerIdentity = new UserIdentity(storedSubscription.UserIdentityToken, m_logger);
            m_sequenceNumber = storedSubscription.SequenceNumber;
            m_lastSentMessage = storedSubscription.LastSentMessage;

            m_monitoredItems = [];
            m_itemsToCheck = new LinkedList<IMonitoredItem>();
            m_itemsToPublish = new LinkedList<IMonitoredItem>();
            m_itemsToTrigger = [];

            // initialize diagnostics.
            Diagnostics = new SubscriptionDiagnosticsDataType
            {
                SubscriptionId = Id,
                Priority = Priority,
                PublishingInterval = m_publishingInterval,
                MaxKeepAliveCount = m_maxKeepAliveCount,
                MaxLifetimeCount = m_maxLifetimeCount,
                MaxNotificationsPerPublish = m_maxNotificationsPerPublish,
                PublishingEnabled = m_publishingEnabled,
                ModifyCount = 0,
                EnableCount = 0,
                DisableCount = 0,
                RepublishMessageRequestCount = 0,
                RepublishMessageCount = 0,
                TransferRequestCount = 0,
                TransferredToSameClientCount = 0,
                TransferredToAltClientCount = 0,
                PublishRequestCount = 0,
                DataChangeNotificationsCount = 0,
                EventNotificationsCount = 0,
                NotificationsCount = 0,
                LatePublishRequestCount = 0,
                CurrentKeepAliveCount = 0,
                CurrentLifetimeCount = 0,
                UnacknowledgedMessageCount = 0,
                DiscardedMessageCount = 0,
                MonitoredItemCount = 0,
                DisabledMonitoredItemCount = 0,
                MonitoringQueueOverflowCount = 0,
                NextSequenceNumber = (uint)m_sequenceNumber
            };

            ServerSystemContext systemContext = m_server.DefaultSystemContext.Copy();

            m_diagnosticsId = server.DiagnosticsNodeManager.CreateSubscriptionDiagnostics(
                systemContext,
                Diagnostics,
                OnUpdateDiagnostics);

            TraceState(LogLevel.Information, TraceStateId.Config, "RESTORED");

            RestoreMonitoredItems(storedSubscription.MonitoredItems);
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
                lock (m_lock)
                {
                    foreach (KeyValuePair<uint, LinkedListNode<IMonitoredItem>> monitoredItemKVP in m_monitoredItems)
                    {
                        Utils.SilentDispose(monitoredItemKVP.Value?.Value);
                    }

                    m_monitoredItems.Clear();
                    m_sentMessages.Clear();
                    m_itemsToCheck.Clear();
                    m_itemsToPublish.Clear();
                }
            }
        }

        /// <summary>
        /// The session that owns the monitored item.
        /// </summary>
        public ISession Session { get; private set; }

        /// <summary>
        /// The unique identifier assigned to the subscription.
        /// </summary>
        public uint Id { get; }

        /// <summary>
        /// The subscriptions owner identity.
        /// </summary>
        public IUserIdentity EffectiveIdentity
            => Session != null ? Session.EffectiveIdentity : m_savedOwnerIdentity;

        /// <summary>
        /// Queues an item that is ready to publish.
        /// </summary>
        public void ItemReadyToPublish(IMonitoredItem monitoredItem)
        {
            /*
            lock (m_itemsReadyToPublish)
            {
                m_itemsReadyToPublish.Enqueue(monitoredItem);
            }
            */
        }

        /// <summary>
        /// Tells the subscription that notifications are available but the item is not ready to publish.
        /// </summary>
        public void ItemNotificationsAvailable(IMonitoredItem monitoredItem)
        {
            /*
            lock (m_itemsReadyToPublish)
            {
                m_itemsNotificationsAvailable.AddLast(monitoredItem);
            }
            */
        }

        /// <summary>
        /// The identifier for the session that owns the subscription.
        /// </summary>
        public NodeId SessionId
        {
            get
            {
                lock (m_lock)
                {
                    if (Session == null)
                    {
                        return null;
                    }

                    return Session.Id;
                }
            }
        }

        /// <summary>
        /// True if the subscription is set to durable and supports long lifetime and queue size
        /// </summary>
        public bool IsDurable { get; private set; }

        /// <summary>
        /// Gets the lock that must be acquired before accessing the contents of the Diagnostics property.
        /// </summary>
        public object DiagnosticsLock => Diagnostics;

        /// <summary>
        /// Gets the lock that must be acquired before updating the contents of the Diagnostics property.
        /// </summary>
        public object DiagnosticsWriteLock
        {
            get
            {
                // mark diagnostic nodes dirty
                if (m_server != null && m_server.DiagnosticsNodeManager != null)
                {
                    m_server.DiagnosticsNodeManager.ForceDiagnosticsScan();
                }
                return DiagnosticsLock;
            }
        }

        /// <summary>
        /// Gets the current diagnostics for the subscription.
        /// </summary>
        public SubscriptionDiagnosticsDataType Diagnostics { get; }

        /// <summary>
        /// The publishing rate for the subscription.
        /// </summary>
        public double PublishingInterval
        {
            get
            {
                lock (m_lock)
                {
                    return m_publishingInterval;
                }
            }
        }

        /// <summary>
        /// The number of monitored items.
        /// </summary>
        public int MonitoredItemCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_monitoredItems.Count;
                }
            }
        }

        /// <summary>
        /// The priority assigned to the subscription.
        /// </summary>
        public byte Priority { get; private set; }

        /// <summary>
        /// Deletes the subscription.
        /// </summary>
        public void Delete(OperationContext context)
        {
            // delete the diagnostics.
            if (m_diagnosticsId != null && !m_diagnosticsId.IsNullNodeId)
            {
                ServerSystemContext systemContext = m_server.DefaultSystemContext.Copy(Session);
                m_server.DiagnosticsNodeManager
                    .DeleteSubscriptionDiagnostics(systemContext, m_diagnosticsId);
            }

            lock (m_lock)
            {
                try
                {
                    TraceState(LogLevel.Information, TraceStateId.Deleted, "DELETED");

                    // the context may be null if the server is cleaning up expired subscriptions.
                    // in this case we create a context with a dummy request and use the current session.
                    if (context == null)
                    {
                        var requestHeader = new RequestHeader
                        {
                            ReturnDiagnostics = (int)DiagnosticsMasks.OperationSymbolicIdAndText
                        };
                        context = new OperationContext(requestHeader, RequestType.Unknown);
                    }

                    DeleteMonitoredItems(
                        context,
                        [.. m_monitoredItems.Keys],
                        true,
                        out StatusCodeCollection results,
                        out DiagnosticInfoCollection diagnosticInfos);
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Delete items for subscription failed.");
                }
            }
        }

        /// <summary>
        /// Checks if the subscription is ready to publish.
        /// </summary>
        public PublishingState PublishTimerExpired()
        {
            lock (m_lock)
            {
                long currentTime = HiResClock.TickCount64;

                // check if publish interval has elapsed.
                if (m_publishTimerExpiry >= currentTime)
                {
                    // check if waiting for publish.
                    if (m_waitingForPublish)
                    {
                        return PublishingState.WaitingForPublish;
                    }

                    return PublishingState.Idle;
                }

                // set next expiry time.
                while (m_publishTimerExpiry < currentTime)
                {
                    m_publishTimerExpiry += (long)m_publishingInterval;
                }

                // check lifetime has elapsed.
                if (m_waitingForPublish)
                {
                    m_lifetimeCounter++;

                    lock (DiagnosticsWriteLock)
                    {
                        Diagnostics.LatePublishRequestCount++;
                        Diagnostics.CurrentLifetimeCount = m_lifetimeCounter;
                    }

                    if (m_lifetimeCounter >= m_maxLifetimeCount)
                    {
                        TraceState(LogLevel.Information, TraceStateId.Deleted, "EXPIRED");
                        return PublishingState.Expired;
                    }
                }

                // increment keep alive counter.
                m_keepAliveCounter++;

                lock (DiagnosticsWriteLock)
                {
                    Diagnostics.CurrentKeepAliveCount = m_keepAliveCounter;
                }

                // check for monitored items.
                if (m_publishingEnabled && Session != null)
                {
                    // check for monitored items that are ready to publish.
                    LinkedListNode<IMonitoredItem> current = m_itemsToCheck.First;
                    bool itemsTriggered = false;

                    while (current != null)
                    {
                        LinkedListNode<IMonitoredItem> next = current.Next;
                        IMonitoredItem monitoredItem = current.Value;

                        // check if the item is ready to publish.
                        if (monitoredItem.IsReadyToPublish || monitoredItem.IsResendData)
                        {
                            m_itemsToCheck.Remove(current);
                            m_itemsToPublish.AddLast(current);
                        }

                        // update any triggered items.

                        if (monitoredItem.IsReadyToTrigger &&
                            m_itemsToTrigger.TryGetValue(
                                current.Value.Id,
                                out List<ITriggeredMonitoredItem> triggeredItems))
                        {
                            for (int ii = 0; ii < triggeredItems.Count; ii++)
                            {
                                if (triggeredItems[ii].SetTriggered())
                                {
                                    itemsTriggered = true;
                                }
                            }

                            // clear ReadyToTrigger flag after trigger
                            monitoredItem.IsReadyToTrigger = false;
                        }

                        current = next;
                    }

                    // need to go through the list again if items were triggered.
                    if (itemsTriggered)
                    {
                        current = m_itemsToCheck.First;

                        while (current != null)
                        {
                            LinkedListNode<IMonitoredItem> next = current.Next;
                            IMonitoredItem monitoredItem = current.Value;

                            if (monitoredItem.IsReadyToPublish)
                            {
                                m_itemsToCheck.Remove(current);
                                m_itemsToPublish.AddLast(current);
                            }

                            current = next;
                        }
                    }

                    if (m_itemsToPublish.Count > 0)
                    {
                        if (!m_waitingForPublish)
                        {
                            // TraceState(LogLevel.Trace, TraceStateId.Deleted, "READY TO PUBLISH");
                        }

                        m_waitingForPublish = true;
                        return PublishingState.NotificationsAvailable;
                    }
                }

                // check if keep alive expired.
                if (m_keepAliveCounter >= m_maxKeepAliveCount)
                {
                    if (!m_waitingForPublish)
                    {
                        // TraceState(LogLevel.Trace, TraceStateId.Items, "READY TO KEEPALIVE");
                    }

                    m_waitingForPublish = true;
                    return PublishingState.NotificationsAvailable;
                }

                // do nothing.
                return PublishingState.Idle;
            }
        }

        /// <summary>
        /// Transfers the subscription to a new session.
        /// </summary>
        /// <param name="context">The session to which the subscription is transferred.</param>
        /// <param name="sendInitialValues">Whether the first Publish response shall contain current values.</param>
        public void TransferSession(OperationContext context, bool sendInitialValues)
        {
            // locked by caller
            Session = context.Session;

            var monitoredItems = m_monitoredItems.Select(v => v.Value.Value).ToList();
            var errors = new List<ServiceResult>(monitoredItems.Count);
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                errors.Add(null);
            }

            m_server.NodeManager
                .TransferMonitoredItems(context, sendInitialValues, monitoredItems, errors);

            int badTransfers = 0;
            for (int ii = 0; ii < errors.Count; ii++)
            {
                if (ServiceResult.IsBad(errors[ii]))
                {
                    badTransfers++;
                }
            }

            if (badTransfers > 0)
            {
                m_logger.LogTrace("Failed to transfer {Count} Monitored Items", badTransfers);
            }

            lock (DiagnosticsWriteLock)
            {
                Diagnostics.SessionId = Session.Id;
            }
        }

        /// <summary>
        /// Initiates resending of all data monitored items in a Subscription
        /// </summary>
        public void ResendData(OperationContext context)
        {
            // check session.
            VerifySession(context);
            lock (m_lock)
            {
                foreach (IMonitoredItem monitoredItem in m_monitoredItems.Select(v => v.Value.Value)
                    .ToList())
                {
                    monitoredItem.SetupResendDataTrigger();
                }
            }
        }

        /// <summary>
        /// Tells the subscription that the owning session is being closed.
        /// </summary>
        public void SessionClosed()
        {
            lock (m_lock)
            {
                if (Session != null)
                {
                    m_savedOwnerIdentity = Session.EffectiveIdentity;
                    Session = null;
                }
            }

            lock (DiagnosticsWriteLock)
            {
                Diagnostics.SessionId = null;
            }
        }

        /// <summary>
        /// Resets the keepalive counter.
        /// </summary>
        private void ResetKeepaliveCount()
        {
            m_keepAliveCounter = 0;

            lock (DiagnosticsWriteLock)
            {
                Diagnostics.CurrentKeepAliveCount = 0;
            }
        }

        /// <summary>
        /// Resets the lifetime count.
        /// </summary>
        private void ResetLifetimeCount()
        {
            m_lifetimeCounter = 0;

            lock (DiagnosticsWriteLock)
            {
                Diagnostics.CurrentLifetimeCount = 0;
            }
        }

        /// <summary>
        /// Update the monitoring queue overflow count.
        /// </summary>
        public void QueueOverflowHandler()
        {
            lock (DiagnosticsWriteLock)
            {
                Diagnostics.MonitoringQueueOverflowCount++;
            }
        }

        /// <summary>
        /// Removes a message from the message queue.
        /// </summary>
        public ServiceResult Acknowledge(OperationContext context, uint sequenceNumber)
        {
            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();

                // find message in queue.
                for (int ii = 0; ii < m_sentMessages.Count; ii++)
                {
                    if (m_sentMessages[ii].SequenceNumber == sequenceNumber)
                    {
                        if (m_lastSentMessage > ii)
                        {
                            m_lastSentMessage--;
                        }

                        m_sentMessages.RemoveAt(ii);
                        return null;
                    }
                }

                if (sequenceNumber == 0)
                {
                    return StatusCodes.BadSequenceNumberInvalid;
                }

                // TraceState(LogLevel.Trace, TraceStateId.Items, "ACK " + sequenceNumber.ToString());

                // message not found.
                return StatusCodes.BadSequenceNumberUnknown;
            }
        }

        /// <summary>
        /// Returns all available notifications.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public NotificationMessage Publish(
            OperationContext context,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            NotificationMessage message = null;

            lock (m_lock)
            {
                moreNotifications = false;
                availableSequenceNumbers = null;

                // check if expired.
                if (m_expired)
                {
                    return null;
                }

                try
                {
                    // update diagnostics.
                    lock (DiagnosticsWriteLock)
                    {
                        Diagnostics.PublishRequestCount++;
                    }

                    message = InnerPublish(
                        context,
                        out availableSequenceNumbers,
                        out moreNotifications);

                    lock (DiagnosticsWriteLock)
                    {
                        Diagnostics.UnacknowledgedMessageCount = (uint)availableSequenceNumbers
                            .Count;
                    }
                }
                finally
                {
                    // clear counters on success.
                    if (message != null)
                    {
                        // TraceState(LogLevel.Trace, TraceStateId.Items, Utils.Format("PUBLISH #{0}", message.SequenceNumber));
                        ResetKeepaliveCount();
                        m_waitingForPublish = moreNotifications;
                        ResetLifetimeCount();
                    }
                }
            }

            return message;
        }

        /// <summary>
        /// Publishes a timeout status message.
        /// </summary>
        public NotificationMessage PublishTimeout()
        {
            NotificationMessage message = null;

            lock (m_lock)
            {
                m_expired = true;

                message = new NotificationMessage
                {
                    SequenceNumber = (uint)m_sequenceNumber,
                    PublishTime = DateTime.UtcNow
                };

                Utils.IncrementIdentifier(ref m_sequenceNumber);

                lock (DiagnosticsWriteLock)
                {
                    Diagnostics.NextSequenceNumber = (uint)m_sequenceNumber;
                }

                var notification = new StatusChangeNotification { Status = StatusCodes.BadTimeout };
                message.NotificationData.Add(new ExtensionObject(notification));
            }

            return message;
        }

        /// <summary>
        /// Publishes a SubscriptionTransferred status message.
        /// </summary>
        public NotificationMessage SubscriptionTransferred()
        {
            NotificationMessage message = null;

            lock (m_lock)
            {
                message = new NotificationMessage
                {
                    SequenceNumber = (uint)m_sequenceNumber,
                    PublishTime = DateTime.UtcNow
                };

                Utils.IncrementIdentifier(ref m_sequenceNumber);

                lock (DiagnosticsWriteLock)
                {
                    Diagnostics.NextSequenceNumber = (uint)m_sequenceNumber;
                }

                var notification = new StatusChangeNotification
                {
                    Status = StatusCodes.GoodSubscriptionTransferred
                };
                message.NotificationData.Add(new ExtensionObject(notification));
            }

            return message;
        }

        /// <summary>
        /// Returns all available notifications.
        /// </summary>
        private NotificationMessage InnerPublish(
            OperationContext context,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications)
        {
            // check session.
            VerifySession(context);

            // TraceState(LogLevel.Trace, TraceStateId.Items, "PUBLISH");

            // check if a keep alive should be sent if there is no data.
            bool keepAliveIfNoData = m_keepAliveCounter >= m_maxKeepAliveCount;

            availableSequenceNumbers = [];

            moreNotifications = false;

            if (m_lastSentMessage < m_sentMessages.Count)
            {
                // return the available sequence numbers.
                for (int ii = 0; ii <= m_lastSentMessage && ii < m_sentMessages.Count; ii++)
                {
                    availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
                }

                moreNotifications = m_waitingForPublish = m_lastSentMessage < m_sentMessages.Count -
                    1;

                // TraceState(LogLevel.Trace, TraceStateId.Items, "PUBLISH QUEUED MESSAGE");
                return m_sentMessages[m_lastSentMessage++];
            }

            var messages = new List<NotificationMessage>();

            if (m_publishingEnabled)
            {
                DateTime start1 = DateTime.UtcNow;

                // collect notifications to publish.
                var events = new Queue<EventFieldList>();
                var datachanges = new Queue<MonitoredItemNotification>();
                var datachangeDiagnostics = new Queue<DiagnosticInfo>();

                // check for monitored items that are ready to publish.
                LinkedListNode<IMonitoredItem> current = m_itemsToPublish.First;

                //Limit the amount of values a monitored item publishes at once
                uint maxNotificationsPerMonitoredItem =
                    m_maxNotificationsPerPublish == 0
                        ? uint.MaxValue
                        : m_maxNotificationsPerPublish * 3;

                while (current != null)
                {
                    LinkedListNode<IMonitoredItem> next = current.Next;
                    IMonitoredItem monitoredItem = current.Value;
                    bool hasMoreValuesToPublish;

                    if ((monitoredItem.MonitoredItemType & MonitoredItemTypeMask.DataChange) != 0)
                    {
                        hasMoreValuesToPublish = ((IDataChangeMonitoredItem)monitoredItem).Publish(
                            context,
                            datachanges,
                            datachangeDiagnostics,
                            maxNotificationsPerMonitoredItem);
                    }
                    else
                    {
                        hasMoreValuesToPublish = ((IEventMonitoredItem)monitoredItem).Publish(
                            context,
                            events,
                            maxNotificationsPerMonitoredItem);
                    }

                    // if item has more values to publish leave it at the front of the list
                    // to execute publish in next cycle, no checking needed
                    // if no more values to publish are left add it to m_itemsToCheck
                    // to check status on next publish cylce
                    if (!hasMoreValuesToPublish)
                    {
                        m_itemsToPublish.Remove(current);
                        m_itemsToCheck.AddLast(current);
                    }

                    // check there are enough notifications for a message.
                    if (m_maxNotificationsPerPublish > 0 &&
                        events.Count + datachanges.Count > m_maxNotificationsPerPublish)
                    {
                        // construct message.
                        int eventCount = events.Count;
                        int dataChangeCount = datachanges.Count;

                        NotificationMessage message = ConstructMessage(
                            events,
                            datachanges,
                            datachangeDiagnostics,
                            out int notificationCount);

                        // add to list of messages to send.
                        messages.Add(message);

                        lock (DiagnosticsWriteLock)
                        {
                            Diagnostics.DataChangeNotificationsCount += (uint)(dataChangeCount -
                                datachanges.Count);
                            Diagnostics.EventNotificationsCount += (uint)(eventCount -
                                events.Count);
                            Diagnostics.NotificationsCount += (uint)notificationCount;
                        }

                        //stop fetching messages from MIs when message queue is full to avoid discards
                        // use m_maxMessageCount - 2 to put remaining values into the last allowed message (each MI is allowed to publish 3 up to messages at once)
                        if (messages.Count >= m_maxMessageCount - 2)
                        {
                            break;
                        }
                    }

                    current = next;
                }

                // publish the remaining notifications.
                while (events.Count + datachanges.Count > 0)
                {
                    // construct message.
                    int eventCount = events.Count;
                    int dataChangeCount = datachanges.Count;

                    NotificationMessage message = ConstructMessage(
                        events,
                        datachanges,
                        datachangeDiagnostics,
                        out int notificationCount);

                    // add to list of messages to send.
                    messages.Add(message);

                    lock (DiagnosticsWriteLock)
                    {
                        Diagnostics.DataChangeNotificationsCount += (uint)(dataChangeCount -
                            datachanges.Count);
                        Diagnostics.EventNotificationsCount += (uint)(eventCount - events.Count);
                        Diagnostics.NotificationsCount += (uint)notificationCount;
                    }
                }

                // check for missing notifications.
                if (!keepAliveIfNoData && messages.Count == 0)
                {
                    m_logger.LogError("Oops! MonitoredItems queued but no notifications available.");

                    m_waitingForPublish = false;

                    return null;
                }

                DateTime end1 = DateTime.UtcNow;

                double delta1 = ((double)(end1.Ticks - start1.Ticks)) /
                    TimeSpan.TicksPerMillisecond;

                if (delta1 > 200)
                {
                    TraceState(
                        LogLevel.Trace,
                        TraceStateId.Publish,
                        Utils.Format("PUBLISHING DELAY ({0}ms)", delta1));
                }
            }

            if (messages.Count == 0)
            {
                // create a keep alive message.
                var message = new NotificationMessage
                {
                    // use the sequence number for the next message.
                    SequenceNumber = (uint)m_sequenceNumber,
                    PublishTime = DateTime.UtcNow
                };

                // return the available sequence numbers.
                for (int ii = 0; ii <= m_lastSentMessage && ii < m_sentMessages.Count; ii++)
                {
                    availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
                }

                // TraceState(LogLevel.Trace, TraceStateId.Items, "PUBLISH KEEPALIVE");
                return message;
            }

            // have to drop unsent messages if out of queue space.
            int overflowCount = messages.Count - (int)m_maxMessageCount;
            if (overflowCount > 0)
            {
                m_logger.LogWarning(
                    "WARNING: QUEUE OVERFLOW. Dropping {Count} Messages. Increase MaxMessageQueueSize. SubId={SubscriptionId}, MaxMessageQueueSize={MaxMessageCount}",
                    overflowCount,
                    Id,
                    m_maxMessageCount);
                messages.RemoveRange(0, overflowCount);
            }

            // remove old messages if queue is full.
            if (m_sentMessages.Count > m_maxMessageCount - messages.Count)
            {
                lock (DiagnosticsWriteLock)
                {
                    Diagnostics.UnacknowledgedMessageCount += (uint)messages.Count;
                }

                if (m_maxMessageCount <= messages.Count)
                {
                    m_sentMessages.Clear();
                }
                else
                {
                    m_sentMessages.RemoveRange(0, messages.Count);
                }
            }

            // save new message
            m_lastSentMessage = m_sentMessages.Count;
            m_sentMessages.AddRange(messages);

            // check if there are more notifications to send.
            moreNotifications = m_waitingForPublish = messages.Count > 1;

            // return the available sequence numbers.
            for (int ii = 0; ii <= m_lastSentMessage && ii < m_sentMessages.Count; ii++)
            {
                availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
            }

            // TraceState(LogLevel.Trace, TraceStateId.Items, "PUBLISH NEW MESSAGE");
            return m_sentMessages[m_lastSentMessage++];
        }

        /// <summary>
        /// Returns the available sequence numbers for retransmission
        /// For example used in Transfer Subscription
        /// </summary>
        public UInt32Collection AvailableSequenceNumbersForRetransmission()
        {
            var availableSequenceNumbers = new UInt32Collection();
            // Assumption we do not check lastSentMessage < sentMessages.Count because
            // in case of subscription transfer original client might have crashed by handling message,
            // therefor new client should have to chance to process all available messages
            for (int ii = 0; ii < m_sentMessages.Count; ii++)
            {
                availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
            }
            return availableSequenceNumbers;
        }

        /// <summary>
        /// Construct a message from the queues.
        /// </summary>
        private NotificationMessage ConstructMessage(
            Queue<EventFieldList> events,
            Queue<MonitoredItemNotification> datachanges,
            Queue<DiagnosticInfo> datachangeDiagnostics,
            out int notificationCount)
        {
            notificationCount = 0;

            var message = new NotificationMessage
            {
                SequenceNumber = (uint)m_sequenceNumber,
                PublishTime = DateTime.UtcNow
            };

            Utils.IncrementIdentifier(ref m_sequenceNumber);

            lock (DiagnosticsWriteLock)
            {
                Diagnostics.NextSequenceNumber = (uint)m_sequenceNumber;
            }

            // add events.
            if (events.Count > 0 && notificationCount < m_maxNotificationsPerPublish)
            {
                var notification = new EventNotificationList();

                while (events.Count > 0 && notificationCount < m_maxNotificationsPerPublish)
                {
                    notification.Events.Add(events.Dequeue());
                    notificationCount++;
                }

                message.NotificationData.Add(new ExtensionObject(notification));
            }

            // add datachanges (space permitting).
            if (datachanges.Count > 0 && notificationCount < m_maxNotificationsPerPublish)
            {
                bool diagnosticsExist = false;
                var notification = new DataChangeNotification
                {
                    MonitoredItems = new MonitoredItemNotificationCollection(datachanges.Count),
                    DiagnosticInfos = new DiagnosticInfoCollection(datachanges.Count)
                };

                while (datachanges.Count > 0 && notificationCount < m_maxNotificationsPerPublish)
                {
                    MonitoredItemNotification datachange = datachanges.Dequeue();
                    notification.MonitoredItems.Add(datachange);

                    DiagnosticInfo diagnosticInfo = datachangeDiagnostics.Dequeue();

                    if (diagnosticInfo != null)
                    {
                        diagnosticsExist = true;
                    }

                    notification.DiagnosticInfos.Add(diagnosticInfo);

                    notificationCount++;
                }

                // clear diagnostics if not used.
                if (!diagnosticsExist)
                {
                    notification.DiagnosticInfos.Clear();
                }

                message.NotificationData.Add(new ExtensionObject(notification));
            }

            return message;
        }

        /// <summary>
        /// Returns a cached notification message.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public NotificationMessage Republish(
            OperationContext context,
            uint retransmitSequenceNumber)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (DiagnosticsWriteLock)
            {
                Diagnostics.RepublishMessageRequestCount++;
            }

            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();

                lock (DiagnosticsWriteLock)
                {
                    Diagnostics.RepublishRequestCount++;
                    Diagnostics.RepublishMessageRequestCount++;
                }

                // find message.
                foreach (NotificationMessage sentMessage in m_sentMessages)
                {
                    if (sentMessage.SequenceNumber == retransmitSequenceNumber)
                    {
                        lock (DiagnosticsWriteLock)
                        {
                            Diagnostics.RepublishMessageCount++;
                        }

                        return sentMessage;
                    }
                }

                // message not available.
                throw new ServiceResultException(StatusCodes.BadMessageNotAvailable);
            }
        }

        /// <summary>
        /// Updates the publishing parameters for the subscription.
        /// </summary>
        public void Modify(
            OperationContext context,
            double publishingInterval,
            uint maxLifetimeCount,
            uint maxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority)
        {
            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();

                m_maxLifetimeCount = maxLifetimeCount;

                // update publishing interval.
                if (publishingInterval != m_publishingInterval)
                {
                    m_publishingInterval = publishingInterval;
                    m_publishTimerExpiry = HiResClock.TickCount64 + (long)publishingInterval;
                    ResetKeepaliveCount();
                }

                // update keep alive count.
                if (maxKeepAliveCount != m_maxKeepAliveCount)
                {
                    m_maxKeepAliveCount = maxKeepAliveCount;
                }

                m_maxNotificationsPerPublish = maxNotificationsPerPublish;

                // update priority.
                Priority = priority;

                // update diagnostics
                lock (DiagnosticsWriteLock)
                {
                    Diagnostics.ModifyCount++;
                    Diagnostics.PublishingInterval = m_publishingInterval;
                    Diagnostics.MaxKeepAliveCount = m_maxKeepAliveCount;
                    Diagnostics.MaxLifetimeCount = m_maxLifetimeCount;
                    Diagnostics.Priority = Priority;
                    Diagnostics.MaxNotificationsPerPublish = m_maxNotificationsPerPublish;
                }

                TraceState(LogLevel.Information, TraceStateId.Config, "MODIFIED");
            }
        }

        /// <summary>
        /// Enables/disables publishing for the subscription.
        /// </summary>
        public void SetPublishingMode(OperationContext context, bool publishingEnabled)
        {
            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();

                // update publishing interval.
                if (publishingEnabled != m_publishingEnabled)
                {
                    m_publishingEnabled = publishingEnabled;

                    // update diagnostics
                    lock (DiagnosticsWriteLock)
                    {
                        Diagnostics.PublishingEnabled = m_publishingEnabled;

                        if (m_publishingEnabled)
                        {
                            Diagnostics.EnableCount++;
                        }
                        else
                        {
                            Diagnostics.DisableCount++;
                        }
                    }
                }

                TraceState(
                    LogLevel.Information,
                    TraceStateId.Config,
                    publishingEnabled ? "ENABLED" : "DISABLED");
            }
        }

        /// <summary>
        /// Updates the triggers for the monitored item.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void SetTriggering(
            OperationContext context,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (linksToAdd == null)
            {
                throw new ArgumentNullException(nameof(linksToAdd));
            }

            if (linksToRemove == null)
            {
                throw new ArgumentNullException(nameof(linksToRemove));
            }

            // allocate results.
            bool diagnosticsExist = false;
            addResults = [];
            addDiagnosticInfos = null;
            removeResults = [];
            removeDiagnosticInfos = null;

            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                addDiagnosticInfos = [];
                removeDiagnosticInfos = [];
            }

            // build list of items to modify.
            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();

                // look up triggering item.

                if (!m_monitoredItems.TryGetValue(
                    triggeringItemId,
                    out LinkedListNode<IMonitoredItem> triggerNode))
                {
                    throw new ServiceResultException(StatusCodes.BadMonitoredItemIdInvalid);
                }

                // lookup existing list.

                if (!m_itemsToTrigger.TryGetValue(
                    triggeringItemId,
                    out List<ITriggeredMonitoredItem> triggeredItems))
                {
                    m_itemsToTrigger[triggeringItemId] = triggeredItems = [];
                }

                // remove old links.
                for (int ii = 0; ii < linksToRemove.Count; ii++)
                {
                    removeResults.Add(StatusCodes.Good);

                    bool found = false;

                    for (int jj = 0; jj < triggeredItems.Count; jj++)
                    {
                        if (triggeredItems[jj].Id == linksToRemove[ii])
                        {
                            found = true;
                            triggeredItems.RemoveAt(jj);
                            break;
                        }
                    }

                    if (!found)
                    {
                        removeResults[ii] = StatusCodes.BadMonitoredItemIdInvalid;

                        // update diagnostics.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                removeResults[ii]);
                            diagnosticsExist = true;
                            removeDiagnosticInfos.Add(diagnosticInfo);
                        }

                        continue;
                    }

                    // update diagnostics.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        removeDiagnosticInfos.Add(null);
                    }
                }

                // add new links.
                for (int ii = 0; ii < linksToAdd.Count; ii++)
                {
                    addResults.Add(StatusCodes.Good);

                    if (!m_monitoredItems.TryGetValue(
                        linksToAdd[ii],
                        out LinkedListNode<IMonitoredItem> node))
                    {
                        addResults[ii] = StatusCodes.BadMonitoredItemIdInvalid;

                        // update diagnostics.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                addResults[ii]);
                            diagnosticsExist = true;
                            addDiagnosticInfos.Add(diagnosticInfo);
                        }

                        continue;
                    }

                    // check if triggering interface is supported.

                    if (node.Value is not ITriggeredMonitoredItem triggeredItem)
                    {
                        addResults[ii] = StatusCodes.BadNotSupported;

                        // update diagnostics.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                addResults[ii]);
                            diagnosticsExist = true;
                            addDiagnosticInfos.Add(diagnosticInfo);
                        }

                        continue;
                    }

                    // add value if not already in list.
                    bool found = false;

                    for (int jj = 0; jj < triggeredItems.Count; jj++)
                    {
                        if (triggeredItems[jj].Id == triggeredItem.Id)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        triggeredItems.Add(triggeredItem);
                    }

                    // update diagnostics.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        addDiagnosticInfos.Add(null);
                    }
                }

                // remove an empty list.
                if (triggeredItems.Count == 0)
                {
                    m_itemsToTrigger.Remove(triggeringItemId);
                }

                // clear diagnostics if not required.
                if (!diagnosticsExist)
                {
                    addDiagnosticInfos?.Clear();

                    removeDiagnosticInfos?.Clear();
                }
            }
        }

        /// <summary>
        /// Adds monitored items to a subscription.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void CreateMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (itemsToCreate == null)
            {
                throw new ArgumentNullException(nameof(itemsToCreate));
            }

            int count = itemsToCreate.Count;

            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();
            }

            // create the monitored items.
            var monitoredItems = new List<IMonitoredItem>(count);
            var errors = new List<ServiceResult>(count);
            var filterResults = new List<MonitoringFilterResult>(count);

            for (int ii = 0; ii < count; ii++)
            {
                monitoredItems.Add(null);
                errors.Add(null);
                filterResults.Add(null);
            }

            m_server.NodeManager.CreateMonitoredItems(
                context,
                Id,
                m_publishingInterval,
                timestampsToReturn,
                itemsToCreate,
                errors,
                filterResults,
                monitoredItems,
                IsDurable);

            // allocate results.
            bool diagnosticsExist = false;
            results = new MonitoredItemCreateResultCollection(count);
            diagnosticInfos = null;

            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos = new DiagnosticInfoCollection(count);
            }

            lock (m_lock)
            {
                // check session again after CreateMonitoredItems.
                VerifySession(context);

                for (int ii = 0; ii < errors.Count; ii++)
                {
                    // update results.
                    MonitoredItemCreateResult result = null;

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        result = new MonitoredItemCreateResult { StatusCode = errors[ii].Code };

                        if (filterResults[ii] != null)
                        {
                            result.FilterResult = new ExtensionObject(filterResults[ii]);
                        }
                    }
                    else
                    {
                        IMonitoredItem monitoredItem = monitoredItems[ii];

                        if (monitoredItem != null)
                        {
                            monitoredItem.SubscriptionCallback = this;

                            LinkedListNode<IMonitoredItem> node = m_itemsToCheck.AddLast(
                                monitoredItem);
                            m_monitoredItems.Add(monitoredItem.Id, node);

                            errors[ii] = monitoredItem.GetCreateResult(out result);

                            // update sampling interval diagnostics.
                            AddItemToSamplingInterval(
                                result.RevisedSamplingInterval,
                                itemsToCreate[ii].MonitoringMode);
                        }
                    }

                    results.Add(result);

                    // update diagnostics.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo diagnosticInfo = null;

                        if (errors[ii] != null && errors[ii].Code != StatusCodes.Good)
                        {
                            diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                errors[ii]);
                            diagnosticsExist = true;
                        }

                        diagnosticInfos.Add(diagnosticInfo);
                    }
                }

                // clear diagnostics if not required.
                if (!diagnosticsExist && diagnosticInfos != null)
                {
                    diagnosticInfos.Clear();
                }

                TraceState(LogLevel.Information, TraceStateId.Items, "ITEMS CREATED");
            }
        }

        /// <summary>
        /// Adds an item to the sampling interval.
        /// </summary>
        private void AddItemToSamplingInterval(
            double samplingInterval,
            MonitoringMode monitoringMode)
        {
            // update diagnostics
            lock (DiagnosticsWriteLock)
            {
                if (monitoringMode == MonitoringMode.Disabled)
                {
                    Diagnostics.DisabledMonitoredItemCount++;
                }
                Diagnostics.MonitoredItemCount++;
            }
        }

        /// <summary>
        /// Adds an item to the sampling interval.
        /// </summary>
        private static void ModifyItemSamplingInterval(
            double oldInterval,
            double newInterval,
            MonitoringMode monitoringMode)
        {
            // TBD
        }

        /// <summary>
        /// Removes an item from the sampling interval.
        /// </summary>
        private void RemoveItemToSamplingInterval(
            double samplingInterval,
            MonitoringMode monitoringMode)
        {
            // update diagnostics
            lock (DiagnosticsWriteLock)
            {
                if (monitoringMode == MonitoringMode.Disabled)
                {
                    Diagnostics.DisabledMonitoredItemCount--;
                }
                Diagnostics.MonitoredItemCount--;
            }
        }

        /// <summary>
        /// Changes the monitoring mode for an item.
        /// </summary>
        private void ModifyItemMonitoringMode(
            double samplingInterval,
            MonitoringMode oldMode,
            MonitoringMode newMode)
        {
            if (newMode != oldMode)
            {
                // update diagnostics
                lock (DiagnosticsWriteLock)
                {
                    if (newMode == MonitoringMode.Disabled)
                    {
                        Diagnostics.DisabledMonitoredItemCount++;
                    }
                    else
                    {
                        Diagnostics.DisabledMonitoredItemCount--;
                    }
                }
            }
        }

        /// <summary>
        /// Modifies monitored items in a subscription.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (itemsToModify == null)
            {
                throw new ArgumentNullException(nameof(itemsToModify));
            }

            int count = itemsToModify.Count;

            // allocate results.
            bool diagnosticsExist = false;
            results = new MonitoredItemModifyResultCollection(count);
            diagnosticInfos = null;

            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos = new DiagnosticInfoCollection(count);
            }

            // build list of items to modify.
            var monitoredItems = new List<IMonitoredItem>(count);
            var errors = new List<ServiceResult>(count);
            var filterResults = new List<MonitoringFilterResult>(count);
            double[] originalSamplingIntervals = new double[count];

            bool validItems = false;

            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();

                for (int ii = 0; ii < count; ii++)
                {
                    filterResults.Add(null);

                    if (!m_monitoredItems.TryGetValue(
                            itemsToModify[ii].MonitoredItemId,
                            out LinkedListNode<IMonitoredItem> node))
                    {
                        monitoredItems.Add(null);
                        errors.Add(StatusCodes.BadMonitoredItemIdInvalid);

                        // update diagnostics.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                errors[ii]);
                            diagnosticsExist = true;
                            diagnosticInfos.Add(diagnosticInfo);
                        }

                        continue;
                    }

                    IMonitoredItem monitoredItem = node.Value;
                    monitoredItems.Add(monitoredItem);
                    originalSamplingIntervals[ii] = monitoredItem.SamplingInterval;

                    errors.Add(null);
                    validItems = true;

                    // update diagnostics.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
            }

            // update items.
            if (validItems)
            {
                m_server.NodeManager.ModifyMonitoredItems(
                    context,
                    timestampsToReturn,
                    monitoredItems,
                    itemsToModify,
                    errors,
                    filterResults);
            }

            lock (m_lock)
            {
                // create results.
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    ServiceResult error = errors[ii];

                    MonitoredItemModifyResult result = null;

                    if (ServiceResult.IsGood(error))
                    {
                        error = monitoredItems[ii].GetModifyResult(out result);
                    }

                    result ??= new MonitoredItemModifyResult();

                    if (error == null)
                    {
                        result.StatusCode = StatusCodes.Good;
                    }
                    else
                    {
                        result.StatusCode = error.StatusCode;
                    }

                    // update diagnostics.
                    if (ServiceResult.IsGood(error))
                    {
                        ModifyItemSamplingInterval(
                            originalSamplingIntervals[ii],
                            result.RevisedSamplingInterval,
                            monitoredItems[ii].MonitoringMode);
                    }

                    if (filterResults[ii] != null)
                    {
                        result.FilterResult = new ExtensionObject(filterResults[ii]);
                    }

                    results.Add(result);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0 &&
                        error != null &&
                        error.Code != StatusCodes.Good)
                    {
                        diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            error);
                        diagnosticsExist = true;
                    }
                }

                // clear diagnostics if not required.
                if (!diagnosticsExist && diagnosticInfos != null)
                {
                    diagnosticInfos.Clear();
                }

                TraceState(LogLevel.Information, TraceStateId.Items, "ITEMS MODIFIED");
            }
        }

        /// <summary>
        /// Deletes the monitored items in a subscription.
        /// </summary>
        public void DeleteMonitoredItems(
            OperationContext context,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            DeleteMonitoredItems(
                context,
                monitoredItemIds,
                false,
                out results,
                out diagnosticInfos);
        }

        /// <summary>
        /// Deletes the monitored items in a subscription.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        private void DeleteMonitoredItems(
            OperationContext context,
            UInt32Collection monitoredItemIds,
            bool doNotCheckSession,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItemIds == null)
            {
                throw new ArgumentNullException(nameof(monitoredItemIds));
            }

            int count = monitoredItemIds.Count;

            bool diagnosticsExist = false;
            results = new StatusCodeCollection(count);
            diagnosticInfos = null;

            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos = new DiagnosticInfoCollection(count);
            }

            // build list of items to modify.
            var monitoredItems = new List<IMonitoredItem>(count);
            var errors = new List<ServiceResult>(count);
            double[] originalSamplingIntervals = new double[count];
            var originalMonitoringModes = new MonitoringMode[count];

            bool validItems = false;

            lock (m_lock)
            {
                // check session.
                if (!doNotCheckSession)
                {
                    VerifySession(context);
                }

                // clear lifetime counter.
                ResetLifetimeCount();

                for (int ii = 0; ii < count; ii++)
                {
                    if (!m_monitoredItems.TryGetValue(
                        monitoredItemIds[ii],
                        out LinkedListNode<IMonitoredItem> node))
                    {
                        monitoredItems.Add(null);
                        errors.Add(StatusCodes.BadMonitoredItemIdInvalid);

                        // update diagnostics.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                errors[ii]);
                            diagnosticsExist = true;
                            diagnosticInfos.Add(diagnosticInfo);
                        }

                        continue;
                    }

                    IMonitoredItem monitoredItem = node.Value;
                    monitoredItems.Add(monitoredItem);

                    // remove the item from the internal lists.
                    m_monitoredItems.Remove(monitoredItemIds[ii]);
                    m_itemsToTrigger.Remove(monitoredItemIds[ii]);

                    //remove the links towards the deleted monitored item
                    List<ITriggeredMonitoredItem> triggeredItems = null;
                    foreach (KeyValuePair<uint, List<ITriggeredMonitoredItem>> item in m_itemsToTrigger)
                    {
                        triggeredItems = item.Value;
                        for (int jj = 0; jj < triggeredItems.Count; jj++)
                        {
                            if (triggeredItems[jj].Id == monitoredItemIds[ii])
                            {
                                triggeredItems.RemoveAt(jj);
                                break;
                            }
                        }
                    }

                    node.List?.Remove(node);

                    originalSamplingIntervals[ii] = monitoredItem.SamplingInterval;
                    originalMonitoringModes[ii] = monitoredItem.MonitoringMode;

                    errors.Add(null);
                    validItems = true;

                    // update diagnostics.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
            }

            // update items.
            if (validItems)
            {
                m_server.NodeManager.DeleteMonitoredItems(context, Id, monitoredItems, errors);
            }

            //dispose monitored Items
            foreach (IMonitoredItem monitoredItem in monitoredItems)
            {
                monitoredItem?.Dispose();
            }

            lock (m_lock)
            {
                // update diagnostics.
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    ServiceResult error = errors[ii];

                    if (error == null)
                    {
                        results.Add(StatusCodes.Good);
                    }
                    else
                    {
                        results.Add(error.StatusCode);
                    }

                    // update diagnostics.
                    if (ServiceResult.IsGood(error))
                    {
                        RemoveItemToSamplingInterval(
                            originalSamplingIntervals[ii],
                            originalMonitoringModes[ii]);
                    }

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0 &&
                        error != null &&
                        error.Code != StatusCodes.Good)
                    {
                        diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            error);
                        diagnosticsExist = true;
                    }
                }

                // clear diagnostics if not required.
                if (!diagnosticsExist && diagnosticInfos != null)
                {
                    diagnosticInfos.Clear();
                }

                TraceState(LogLevel.Information, TraceStateId.Items, "ITEMS DELETED");
            }
        }

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItemIds == null)
            {
                throw new ArgumentNullException(nameof(monitoredItemIds));
            }

            int count = monitoredItemIds.Count;

            bool diagnosticsExist = false;
            results = new StatusCodeCollection(count);
            diagnosticInfos = null;

            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos = new DiagnosticInfoCollection(count);
            }

            // build list of items to modify.
            var monitoredItems = new List<IMonitoredItem>(count);
            var errors = new List<ServiceResult>(count);
            var originalMonitoringModes = new MonitoringMode[count];

            bool validItems = false;

            lock (m_lock)
            {
                // check session.
                VerifySession(context);

                // clear lifetime counter.
                ResetLifetimeCount();

                for (int ii = 0; ii < count; ii++)
                {
                    if (!m_monitoredItems.TryGetValue(
                        monitoredItemIds[ii],
                        out LinkedListNode<IMonitoredItem> node))
                    {
                        monitoredItems.Add(null);
                        errors.Add(StatusCodes.BadMonitoredItemIdInvalid);

                        // update diagnostics.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                errors[ii]);
                            diagnosticsExist = true;
                            diagnosticInfos.Add(diagnosticInfo);
                        }

                        continue;
                    }

                    IMonitoredItem monitoredItem = node.Value;
                    monitoredItems.Add(monitoredItem);
                    originalMonitoringModes[ii] = monitoredItem.MonitoringMode;

                    errors.Add(null);
                    validItems = true;

                    // update diagnostics.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
            }

            // update items.
            if (validItems)
            {
                m_server.NodeManager
                    .SetMonitoringMode(context, monitoringMode, monitoredItems, errors);
            }

            lock (m_lock)
            {
                // update diagnostics.
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    ServiceResult error = errors[ii];

                    if (error == null)
                    {
                        results.Add(StatusCodes.Good);
                    }
                    else
                    {
                        results.Add(error.StatusCode);
                    }

                    // update diagnostics.
                    if (ServiceResult.IsGood(error))
                    {
                        ModifyItemMonitoringMode(
                            monitoredItems[ii].SamplingInterval,
                            originalMonitoringModes[ii],
                            monitoringMode);
                    }

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0 &&
                        error != null &&
                        error.Code != StatusCodes.Good)
                    {
                        diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            error);
                        diagnosticsExist = true;
                    }
                }

                // clear diagnostics if not required.
                if (!diagnosticsExist && diagnosticInfos != null)
                {
                    diagnosticInfos.Clear();
                }

                if (monitoringMode == MonitoringMode.Disabled)
                {
                    TraceState(LogLevel.Information, TraceStateId.Monitor, "MONITORING DISABLED");
                }
                else if (monitoringMode == MonitoringMode.Reporting)
                {
                    TraceState(LogLevel.Information, TraceStateId.Monitor, "REPORTING");
                }
                else
                {
                    TraceState(LogLevel.Information, TraceStateId.Monitor, "SAMPLING");
                }
            }
        }

        /// <summary>
        /// Verifies that a condition refresh operation is permitted.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ValidateConditionRefresh(OperationContext context)
        {
            lock (m_lock)
            {
                VerifySession(context);

                if (m_refreshInProgress)
                {
                    throw new ServiceResultException(StatusCodes.BadRefreshInProgress);
                }
            }
        }

        /// <summary>
        /// Verifies that a condition refresh operation is permitted.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ValidateConditionRefresh2(OperationContext context, uint monitoredItemId)
        {
            ValidateConditionRefresh(context);

            lock (m_lock)
            {
                if (!m_monitoredItems.ContainsKey(monitoredItemId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadMonitoredItemIdInvalid,
                        "Cannot refresh conditions for a monitored item that does not exist.");
                }
            }
        }

        /// <summary>
        /// Refreshes the conditions.
        /// </summary>
        public void ConditionRefresh()
        {
            var monitoredItems = new List<IEventMonitoredItem>();

            lock (m_lock)
            {
                // build list of items to refresh.
                foreach (LinkedListNode<IMonitoredItem> monitoredItem in m_monitoredItems.Values)
                {
                    if (monitoredItem.Value is IEventMonitoredItem eventMonitoredItem &&
                        eventMonitoredItem.EventFilter != null)
                    {
                        // add to list that gets reported to the NodeManagers.
                        monitoredItems.Add(eventMonitoredItem);
                    }
                }

                // nothing to do if no event subscriptions.
                if (monitoredItems.Count == 0)
                {
                    return;
                }
            }

            ConditionRefresh(monitoredItems, 0);
        }

        /// <summary>
        /// Refreshes the conditions.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ConditionRefresh2(uint monitoredItemId)
        {
            var monitoredItems = new List<IEventMonitoredItem>();

            lock (m_lock)
            {
                // build list of items to refresh.
                if (m_monitoredItems.TryGetValue(
                    monitoredItemId,
                    out LinkedListNode<IMonitoredItem> monitoredItem))
                {
                    if (monitoredItem.Value is IEventMonitoredItem eventMonitoredItem &&
                        eventMonitoredItem.EventFilter != null)
                    {
                        // add to list that gets reported to the NodeManagers.
                        monitoredItems.Add(eventMonitoredItem);
                    }
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadMonitoredItemIdInvalid,
                        "Cannot refresh conditions for a monitored item that does not exist.");
                }

                // nothing to do if no event subscriptions.
                if (monitoredItems.Count == 0)
                {
                    return;
                }
            }

            ConditionRefresh(monitoredItems, monitoredItemId);
        }

        /// <summary>
        /// Refreshes the conditions.  Works for both ConditionRefresh and ConditionRefresh2
        /// </summary>
        private void ConditionRefresh(
            List<IEventMonitoredItem> monitoredItems,
            uint monitoredItemId)
        {
            ServerSystemContext systemContext = m_server.DefaultSystemContext.Copy(Session);

            string messageTemplate = Utils.Format(
                "Condition refresh {{0}} for subscription {0}.",
                Id);
            if (monitoredItemId > 0)
            {
                messageTemplate = Utils.Format(
                    "Condition refresh {{0}} for subscription {0}, monitored item {1}.",
                    Id,
                    monitoredItemId);
            }

            lock (m_lock)
            {
                // generate start event.
                var e = new RefreshStartEventState(null);

                var message = new TranslationInfo(
                    "RefreshStartEvent",
                    "en-US",
                    Utils.Format(messageTemplate, "started"));

                e.Initialize(systemContext, null, EventSeverity.Low, new LocalizedText(message));

                e.SetChildValue(systemContext, BrowseNames.SourceNode, m_diagnosticsId, false);
                e.SetChildValue(
                    systemContext,
                    BrowseNames.SourceName,
                    Utils.Format("Subscription/{0}", Id),
                    false);
                e.SetChildValue(systemContext, BrowseNames.ReceiveTime, DateTime.UtcNow, false);

                // build list of items to refresh.
                foreach (IEventMonitoredItem monitoredItem in monitoredItems)
                {
                    IEventMonitoredItem eventMonitoredItem = monitoredItem;

                    if (eventMonitoredItem != null && eventMonitoredItem.EventFilter != null)
                    {
                        // queue start refresh event.
                        eventMonitoredItem.QueueEvent(e, true);
                    }
                }

                // nothing to do if no event subscriptions.
                if (monitoredItems.Count == 0)
                {
                    return;
                }
            }

            // tell the NodeManagers to report the current state of the conditions.
            try
            {
                m_refreshInProgress = true;

                var operationContext = new OperationContext(Session, DiagnosticsMasks.None);
                m_server.NodeManager.ConditionRefresh(operationContext, monitoredItems);
            }
            finally
            {
                m_refreshInProgress = false;
            }

            lock (m_lock)
            {
                // generate start event.
                var e = new RefreshEndEventState(null);

                var message = new TranslationInfo(
                    "RefreshEndEvent",
                    "en-US",
                    Utils.Format(messageTemplate, "completed"));

                e.Initialize(systemContext, null, EventSeverity.Low, new LocalizedText(message));

                e.SetChildValue(systemContext, BrowseNames.SourceNode, m_diagnosticsId, false);
                e.SetChildValue(
                    systemContext,
                    BrowseNames.SourceName,
                    Utils.Format("Subscription/{0}", Id),
                    false);
                e.SetChildValue(systemContext, BrowseNames.ReceiveTime, DateTime.UtcNow, false);

                // send refresh end event.
                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    IEventMonitoredItem monitoredItem = monitoredItems[ii];

                    if (monitoredItem.EventFilter != null)
                    {
                        monitoredItem.QueueEvent(e, true);
                    }
                }

                // TraceState("CONDITION REFRESH");
            }
        }

        /// <summary>
        /// Sets the subscription to durable mode.
        /// </summary>
        public ServiceResult SetSubscriptionDurable(uint maxLifetimeCount)
        {
            lock (m_lock)
            {
                if (!m_supportsDurable)
                {
                    m_logger.LogError(
                        "SetSubscriptionDurable requested for subscription with id {SubscriptionId}, but no IMonitoredItemQueueFactory that supports durable queues was registered",
                        Id);
                    TraceState(
                        LogLevel.Information,
                        TraceStateId.Config,
                        "SetSubscriptionDurable Failed");
                    return StatusCodes.BadNotSupported;
                }

                IsDurable = true;

                // clear lifetime counter.
                ResetLifetimeCount();

                m_maxLifetimeCount = maxLifetimeCount;

                // update diagnostics
                lock (DiagnosticsWriteLock)
                {
                    Diagnostics.ModifyCount++;
                    Diagnostics.MaxLifetimeCount = m_maxLifetimeCount;
                }

                TraceState(LogLevel.Information, TraceStateId.Config, "SET DURABLE");

                return StatusCodes.Good;
            }
        }

        /// <summary>
        /// Gets the monitored items for the subscription.
        /// </summary>
        public void GetMonitoredItems(out uint[] serverHandles, out uint[] clientHandles)
        {
            lock (m_lock)
            {
                serverHandles = new uint[m_monitoredItems.Count];
                clientHandles = new uint[m_monitoredItems.Count];

                int ii = 0;

                foreach (KeyValuePair<uint, LinkedListNode<IMonitoredItem>> entry in m_monitoredItems)
                {
                    serverHandles[ii] = entry.Key;
                    clientHandles[ii] = entry.Value.Value.ClientHandle;
                    ii++;
                }
            }
        }

        /// <summary>
        /// Return a StorableSubscription for restore after a server restart
        /// </summary>
        public IStoredSubscription ToStorableSubscription()
        {
            var monitoredItemsToStore = new List<IStoredMonitoredItem>();

            foreach (KeyValuePair<uint, LinkedListNode<IMonitoredItem>> kvp in m_monitoredItems)
            {
                monitoredItemsToStore.Add(kvp.Value.Value.ToStorableMonitoredItem());
            }

            return new StoredSubscription
            {
                SentMessages = m_sentMessages,
                Id = Id,
                SequenceNumber = m_sequenceNumber,
                LastSentMessage = m_lastSentMessage,
                LifetimeCounter = m_lifetimeCounter,
                MaxKeepaliveCount = m_maxKeepAliveCount,
                MaxLifetimeCount = m_maxLifetimeCount,
                MaxMessageCount = m_maxMessageCount,
                MaxNotificationsPerPublish = m_maxNotificationsPerPublish,
                Priority = Priority,
                PublishingInterval = PublishingInterval,
                UserIdentityToken = EffectiveIdentity?.GetIdentityToken(),
                MonitoredItems = monitoredItemsToStore,
                IsDurable = IsDurable
            };
        }

        /// <summary>
        /// Restore MonitoredItems after a Server restart
        /// </summary>
        protected virtual void RestoreMonitoredItems(
            IEnumerable<IStoredMonitoredItem> storedMonitoredItems)
        {
            int count = storedMonitoredItems.Count();

            // create the monitored items.
            var monitoredItems = new List<IMonitoredItem>(count);

            for (int ii = 0; ii < count; ii++)
            {
                monitoredItems.Add(null);
            }

            m_server.NodeManager.RestoreMonitoredItems(
                [.. storedMonitoredItems],
                monitoredItems,
                m_savedOwnerIdentity);

            lock (m_lock)
            {
                foreach (IMonitoredItem monitoredItem in monitoredItems)
                {
                    // skip MonitoredItem if recreation failed
                    if (monitoredItem == null)
                    {
                        continue;
                    }
                    monitoredItem.SubscriptionCallback = this;

                    LinkedListNode<IMonitoredItem> node = m_itemsToCheck.AddLast(monitoredItem);
                    m_monitoredItems.Add(monitoredItem.Id, node);

                    // update sampling interval diagnostics.
                    AddItemToSamplingInterval(
                        monitoredItem.SamplingInterval,
                        monitoredItem.MonitoringMode);
                }

                TraceState(LogLevel.Information, TraceStateId.Items, "ITEMS RESTORED");
            }
        }

        /// <summary>
        /// Returns a copy of the current diagnostics.
        /// </summary>
        private ServiceResult OnUpdateDiagnostics(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            lock (DiagnosticsLock)
            {
                value = Utils.Clone(Diagnostics);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Throws an exception if the session is not the owner.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void VerifySession(OperationContext context)
        {
            if (m_expired)
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            if (!ReferenceEquals(context.Session, Session))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSubscriptionIdInvalid,
                    "Subscription belongs to a different session.");
            }
        }

        /// <summary>
        /// The states to log.
        /// </summary>
        private enum TraceStateId
        {
            Config,
            Items,
            Monitor,
            Publish,
            Deleted
        }

        /// <summary>
        /// Dumps the current state of the session queue.
        /// </summary>
        private void TraceState(LogLevel logLevel, TraceStateId id, string context)
        {
            if (!m_logger.IsEnabled(logLevel))
            {
                return;
            }

            // save counters
            Monitor.Enter(m_lock);

            long sequenceNumber = m_sequenceNumber;
            int itemsToCheck = m_itemsToCheck.Count;
            int monitoredItems = m_monitoredItems.Count;
            int itemsToPublish = m_itemsToPublish.Count;
            int sentMessages = m_sentMessages.Count;
            bool publishingEnabled = m_publishingEnabled;
            bool waitingForPublish = m_waitingForPublish;

            Monitor.Exit(m_lock);

            switch (id)
            {
                case TraceStateId.Deleted:
                    m_logger.Log(
                        logLevel,
                        "Subscription {Subscription}, SessionId={SessionId}, Id={SubscriptionId}, SeqNo={SequenceNumber}, MessageCount={MessageCount}",
                        context,
                        Session?.Id,
                        Id,
                        sequenceNumber,
                        sentMessages);
                    break;
                case TraceStateId.Config:
                    m_logger.Log(
                        logLevel,
                        "Subscription {Subscription}, SessionId={SessionId}, Id={SubscriptionId}, Priority={Priority}, Publishing={Publishing}, KeepAlive={KeepAlive}, LifeTime={LifeTime}, MaxNotifications={MaxNotifications}, Enabled={Enabled}",
                        context,
                        Session?.Id,
                        Id,
                        Priority,
                        m_publishingInterval,
                        m_maxKeepAliveCount,
                        m_maxLifetimeCount,
                        m_maxNotificationsPerPublish,
                        publishingEnabled);
                    break;
                case TraceStateId.Items:
                    m_logger.Log(
                        logLevel,
                        "Subscription {Subscription}, Id={SubscriptionId}, ItemCount={ItemCount}, ItemsToCheck={ItemsToCheck}, ItemsToPublish={ItemsToPublish}",
                        context,
                        Id,
                        monitoredItems,
                        itemsToCheck,
                        itemsToPublish);
                    break;
                case TraceStateId.Publish:
                case TraceStateId.Monitor:
                    m_logger.Log(
                        logLevel,
                        "Subscription {Subscription}, Id={SubscriptionId}, KeepAliveCounter={keepAliveCounter}, LifeTimeCount={LifeTimeCount}, WaitingForPublish={WaitingForPublish}, SeqNo={SequenceNumber}, ItemCount={ItemCount}, ItemsToCheck={ItemsToCheck}, ItemsToPublish={ItemsToPublish}, MessageCount={MessageCount}",
                        context,
                        Id,
                        m_keepAliveCounter,
                        m_lifetimeCounter,
                        waitingForPublish,
                        sequenceNumber,
                        monitoredItems,
                        itemsToCheck,
                        itemsToPublish,
                        sentMessages);
                    break;
            }
        }

        private readonly object m_lock = new();
        private readonly IServerInternal m_server;
        private IUserIdentity m_savedOwnerIdentity;
        private double m_publishingInterval;
        private uint m_maxLifetimeCount;
        private uint m_maxKeepAliveCount;
        private uint m_maxNotificationsPerPublish;
        private bool m_publishingEnabled;
        private long m_publishTimerExpiry;
        private uint m_keepAliveCounter;
        private uint m_lifetimeCounter;
        private bool m_waitingForPublish;
        private readonly List<NotificationMessage> m_sentMessages;
        private int m_lastSentMessage;
        private long m_sequenceNumber;
        private readonly uint m_maxMessageCount;
        private readonly Dictionary<uint, LinkedListNode<IMonitoredItem>> m_monitoredItems;
        private readonly LinkedList<IMonitoredItem> m_itemsToCheck;
        private readonly LinkedList<IMonitoredItem> m_itemsToPublish;
        private readonly NodeId m_diagnosticsId;
        private bool m_refreshInProgress;
        private bool m_expired;
        private readonly Dictionary<uint, List<ITriggeredMonitoredItem>> m_itemsToTrigger;
        private readonly bool m_supportsDurable;
        private readonly ILogger m_logger;
    }
}
