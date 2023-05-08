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
using System.Text;
using System.Threading;
using System.Globalization;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic session manager object for a server.
    /// </summary>
    public class SubscriptionManager : IDisposable, ISubscriptionManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the manager with its configuration.
        /// </summary>
        public SubscriptionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            m_server = server;

            m_minPublishingInterval = configuration.ServerConfiguration.MinPublishingInterval;
            m_maxPublishingInterval = configuration.ServerConfiguration.MaxPublishingInterval;
            m_publishingResolution = configuration.ServerConfiguration.PublishingResolution;
            m_maxSubscriptionLifetime = (uint)configuration.ServerConfiguration.MaxSubscriptionLifetime;
            m_minSubscriptionLifetime = (uint)configuration.ServerConfiguration.MinSubscriptionLifetime;
            m_maxMessageCount = (uint)configuration.ServerConfiguration.MaxMessageQueueSize;
            m_maxNotificationsPerPublish = (uint)configuration.ServerConfiguration.MaxNotificationsPerPublish;
            m_maxPublishRequestCount = configuration.ServerConfiguration.MaxPublishRequestCount;
            m_maxSubscriptionCount = configuration.ServerConfiguration.MaxSubscriptionCount;

            m_subscriptions = new Dictionary<uint, Subscription>();
            m_publishQueues = new Dictionary<NodeId, SessionPublishQueue>();
            m_statusMessages = new Dictionary<NodeId, Queue<StatusMessage>>();

            // create a event to signal shutdown.
            m_shutdownEvent = new ManualResetEvent(true);

            // create queue and event for condition refresh worker
            m_conditionRefreshEvent = new ManualResetEvent(false);
            m_conditionRefreshQueue = new Queue<ConditionRefreshTask>();
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
                List<Subscription> subscriptions = null;
                List<SessionPublishQueue> publishQueues = null;

                lock (m_lock)
                {
                    publishQueues = new List<SessionPublishQueue>(m_publishQueues.Values);
                    m_publishQueues.Clear();

                    subscriptions = new List<Subscription>(m_subscriptions.Values);
                    m_subscriptions.Clear();
                }

                foreach (SessionPublishQueue publishQueue in publishQueues)
                {
                    Utils.SilentDispose(publishQueue);
                }

                foreach (Subscription subscription in subscriptions)
                {
                    Utils.SilentDispose(subscription);
                }

            }
        }
        #endregion

        #region ISubscriptionManager Members
        /// <summary>
        /// Raised after a new subscription is created.
        /// </summary>
        public event SubscriptionEventHandler SubscriptionCreated
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SubscriptionCreated += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_SubscriptionCreated -= value;
                }
            }
        }

        /// <summary>
        /// Raised before a subscription is deleted.
        /// </summary>
        public event SubscriptionEventHandler SubscriptionDeleted
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SubscriptionDeleted += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_SubscriptionDeleted -= value;
                }
            }
        }

        /// <summary>
        /// Returns all of the subscriptions known to the subscription manager.
        /// </summary>
        /// <returns>A list of the subscriptions.</returns>
        public IList<Subscription> GetSubscriptions()
        {
            List<Subscription> subscriptions = new List<Subscription>();

            lock (m_lock)
            {
                subscriptions.AddRange(m_subscriptions.Values);
            }

            return subscriptions;
        }

        /// <summary>
        /// Raises an event related to a subscription.
        /// </summary>
        protected virtual void RaiseSubscriptionEvent(Subscription subscription, bool deleted)
        {
            SubscriptionEventHandler handler = null;

            lock (m_eventLock)
            {
                handler = m_SubscriptionCreated;

                if (deleted)
                {
                    handler = m_SubscriptionDeleted;
                }
            }

            if (handler != null)
            {
                try
                {
                    handler(subscription, deleted);
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Subscription event handler raised an exception.");
                }
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Starts up the manager makes it ready to create subscriptions.
        /// </summary>
        public virtual void Startup()
        {
            lock (m_lock)
            {
                m_shutdownEvent.Reset();

                Task.Factory.StartNew(() => {
                    PublishSubscriptions(m_publishingResolution);
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);

                m_conditionRefreshEvent.Reset();

                Task.Factory.StartNew(() => {
                    ConditionRefreshWorker();
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);
            }
        }

        /// <summary>
        /// Closes all subscriptions and rejects any new requests.
        /// </summary>
        public virtual void Shutdown()
        {
            lock (m_lock)
            {
                // stop the publishing thread.
                m_shutdownEvent.Set();

                // trigger the condition refresh thread.
                m_conditionRefreshEvent.Set();

                // dispose of publish queues.
                foreach (SessionPublishQueue queue in m_publishQueues.Values)
                {
                    queue.Dispose();
                }

                m_publishQueues.Clear();

                // dispose of subscriptions objects.
                foreach (Subscription subscription in m_subscriptions.Values)
                {
                    subscription.Dispose();
                }

                m_subscriptions.Clear();
            }
        }

        /// <summary>
        /// Signals that a session is closing.
        /// </summary>
        public virtual void SessionClosing(OperationContext context, NodeId sessionId, bool deleteSubscriptions)
        {
            // close the publish queue for the session.
            SessionPublishQueue queue = null;
            IList<Subscription> subscriptionsToDelete = null;
            uint publishingIntervalCount = 0;

            lock (m_lock)
            {
                if (m_publishQueues.TryGetValue(sessionId, out queue))
                {
                    m_publishQueues.Remove(sessionId);
                    subscriptionsToDelete = queue.Close();

                    // remove the subscriptions.
                    if (deleteSubscriptions && subscriptionsToDelete != null)
                    {
                        for (int ii = 0; ii < subscriptionsToDelete.Count; ii++)
                        {
                            m_subscriptions.Remove(subscriptionsToDelete[ii].Id);
                        }
                    }
                }
            }

            //remove the expired subscription status change notifications for this session
            lock (m_statusMessagesLock)
            {
                Queue<StatusMessage> statusQueue = null;
                if (m_statusMessages.TryGetValue(sessionId, out statusQueue))
                {
                    m_statusMessages.Remove(sessionId);
                }
            }

            // process all subscriptions in the queue.
            if (subscriptionsToDelete != null)
            {
                for (int ii = 0; ii < subscriptionsToDelete.Count; ii++)
                {
                    Subscription subscription = subscriptionsToDelete[ii];

                    // delete the subscription.
                    if (deleteSubscriptions)
                    {
                        // raise subscription event.
                        RaiseSubscriptionEvent(subscription, true);

                        // delete subscription.
                        subscription.Delete(context);

                        // get the count for the diagnostics.
                        publishingIntervalCount = GetPublishingIntervalCount();

                        lock (m_server.DiagnosticsWriteLock)
                        {
                            ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                            diagnostics.CurrentSubscriptionCount--;
                            diagnostics.PublishingIntervalCount = publishingIntervalCount;
                        }
                    }

                    // mark the subscriptions as abandoned.
                    else
                    {
                        lock (m_lock)
                        {
                            if (m_abandonedSubscriptions == null)
                            {
                                m_abandonedSubscriptions = new List<Subscription>();
                            }

                            m_abandonedSubscriptions.Add(subscription);
                            Utils.LogWarning("Subscription {0}, Id={1}.", "ABANDONED", subscription.Id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        public void ConditionRefresh(OperationContext context, uint subscriptionId)
        {
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSubscriptionIdInvalid,
                        "Cannot refresh conditions for a subscription that does not exist.");
                }
            }

            // ensure a condition refresh is allowed.
            subscription.ValidateConditionRefresh(context);

            var conditionRefreshTask = new ConditionRefreshTask(subscription, 0);

            ServiceResultException serviceResultException = null;
            lock (m_conditionRefreshLock)
            {
                if (!m_conditionRefreshQueue.Contains(conditionRefreshTask))
                {
                    m_conditionRefreshQueue.Enqueue(conditionRefreshTask);
                }
                else
                {
                    serviceResultException = new ServiceResultException(StatusCodes.BadRefreshInProgress);
                }

                // trigger the refresh worker.
                m_conditionRefreshEvent.Set();
            }

            if (serviceResultException != null)
            {
                throw serviceResultException;
            }
        }

        /// <summary>
        /// Refreshes the conditions for the specified subscription and monitored item.
        /// </summary>
        public void ConditionRefresh2(OperationContext context, uint subscriptionId, uint monitoredItemId)
        {
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSubscriptionIdInvalid,
                        "Cannot refresh conditions for a subscription that does not exist.");
                }
            }

            // ensure a condition refresh is allowed.
            subscription.ValidateConditionRefresh2(context, monitoredItemId);

            var conditionRefreshTask = new ConditionRefreshTask(subscription, monitoredItemId);

            lock (m_conditionRefreshLock)
            {
                if (!m_conditionRefreshQueue.Contains(conditionRefreshTask))
                {
                    m_conditionRefreshQueue.Enqueue(conditionRefreshTask);
                }
                else
                {
                    throw new ServiceResultException(StatusCodes.BadRefreshInProgress);
                }

                // trigger the refresh worker.
                m_conditionRefreshEvent.Set();
            }
        }

        /// <summary>
        /// Completes a refresh conditions request.
        /// </summary>
        private void DoConditionRefresh(Subscription subscription)
        {
            try
            {
                Utils.LogTrace("Subscription ConditionRefresh started, Id={0}.", subscription.Id);
                subscription.ConditionRefresh();
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Subscription - DoConditionRefresh Exited Unexpectedly");
            }
        }

        /// <summary>
        /// Completes a refresh conditions request.
        /// </summary>
        private void DoConditionRefresh2(Subscription subscription, uint monitoredItemId)
        {
            try
            {
                Utils.LogTrace("Subscription ConditionRefresh2 started, Id={0}, MonitoredItemId={1}.",
                    subscription.Id, monitoredItemId);
                subscription.ConditionRefresh2(monitoredItemId);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Subscription - DoConditionRefresh2 Exited Unexpectedly");
            }
        }

        /// <summary>
        /// Deletes the specified subscription.
        /// </summary>
        public StatusCode DeleteSubscription(OperationContext context, uint subscriptionId)
        {
            uint publishingIntervalCount = 0;
            int monitoredItemCount = 0;
            Subscription subscription = null;

            lock (m_lock)
            {
                // remove from publish queue.
                if (m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    NodeId sessionId = subscription.SessionId;

                    if (!NodeId.IsNull(sessionId))
                    {
                        // check that the subscription is the owner.
                        if (context != null && !Object.ReferenceEquals(context.Session, subscription.Session))
                        {
                            throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                        }

                        SessionPublishQueue queue = null;

                        if (m_publishQueues.TryGetValue(sessionId, out queue))
                        {
                            queue.Remove(subscription);
                        }
                    }
                }

                // check for abandoned subscription.
                if (m_abandonedSubscriptions != null)
                {
                    for (int ii = 0; ii < m_abandonedSubscriptions.Count; ii++)
                    {
                        if (m_abandonedSubscriptions[ii].Id == subscriptionId)
                        {
                            m_abandonedSubscriptions.RemoveAt(ii);
                            Utils.LogWarning("Subscription {0}, Id={1}.", "DELETED(ABANDONED)", subscriptionId);
                            break;
                        }
                    }
                }

                // remove subscription.
                m_subscriptions.Remove(subscriptionId);
            }

            if (subscription != null)
            {
                monitoredItemCount = subscription.MonitoredItemCount;

                // raise subscription event.
                RaiseSubscriptionEvent(subscription, true);

                // delete subscription.
                subscription.Delete(context);

                // get the count for the diagnostics.
                publishingIntervalCount = GetPublishingIntervalCount();

                lock (m_server.DiagnosticsWriteLock)
                {
                    ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                    diagnostics.CurrentSubscriptionCount--;
                    diagnostics.PublishingIntervalCount = publishingIntervalCount;
                }

                if (context != null && context.Session != null)
                {
                    lock (context.Session.DiagnosticsLock)
                    {
                        SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                        diagnostics.CurrentSubscriptionsCount--;
                        UpdateCurrentMonitoredItemsCount(diagnostics, -monitoredItemCount);
                    }
                }

                return StatusCodes.Good;
            }

            return StatusCodes.BadSubscriptionIdInvalid;
        }

        /// <summary>
        /// Updates the current monitored item count for the session.
        /// </summary>
        private void UpdateCurrentMonitoredItemsCount(SessionDiagnosticsDataType diagnostics, int change)
        {
            long monitoredItemsCount = (long)diagnostics.CurrentMonitoredItemsCount;
            monitoredItemsCount += change;

            if (monitoredItemsCount > 0)
            {
                diagnostics.CurrentMonitoredItemsCount = (uint)monitoredItemsCount;
            }
            else
            {
                diagnostics.CurrentMonitoredItemsCount = 0;
            }
        }

        /// <summary>
        /// Gets the total number of publishing intervals in use.
        /// </summary>
        private uint GetPublishingIntervalCount()
        {
            Dictionary<double, uint> publishingDiagnostics = new Dictionary<double, uint>();

            lock (m_lock)
            {
                foreach (Subscription subscription in m_subscriptions.Values)
                {
                    double publishingInterval = subscription.PublishingInterval;

                    uint total = 0;

                    if (!publishingDiagnostics.TryGetValue(publishingInterval, out total))
                    {
                        total = 0;
                    }

                    publishingDiagnostics[publishingInterval] = total + 1;
                }
            }

            return (uint)publishingDiagnostics.Count;
        }

        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        public virtual void CreateSubscription(
            OperationContext context,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            lock (m_lock)
            {
                if (m_subscriptions.Count >= m_maxSubscriptionCount)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManySubscriptions);
                }
            }

            subscriptionId = 0;
            revisedPublishingInterval = 0;
            revisedLifetimeCount = 0;
            revisedMaxKeepAliveCount = 0;

            uint publishingIntervalCount = 0;
            Subscription subscription = null;

            // get sessin from context.
            Session session = context.Session;

            // assign new identifier.
            subscriptionId = Utils.IncrementIdentifier(ref m_lastSubscriptionId);

            // calculate publishing interval.
            revisedPublishingInterval = CalculatePublishingInterval(requestedPublishingInterval);

            // calculate the keep alive count.
            revisedMaxKeepAliveCount = CalculateKeepAliveCount(revisedPublishingInterval, requestedMaxKeepAliveCount);

            // calculate the lifetime count.
            revisedLifetimeCount = CalculateLifetimeCount(revisedPublishingInterval, revisedMaxKeepAliveCount, requestedLifetimeCount);

            // calculate the max notification count.
            maxNotificationsPerPublish = CalculateMaxNotificationsPerPublish(maxNotificationsPerPublish);

            // create the subscription.
            subscription = CreateSubscription(
                context,
                subscriptionId,
                revisedPublishingInterval,
                revisedLifetimeCount,
                revisedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                publishingEnabled);

            lock (m_lock)
            {
                // save subscription.
                m_subscriptions.Add(subscriptionId, subscription);

                // create/update publish queue.
                SessionPublishQueue queue = null;

                if (!m_publishQueues.TryGetValue(session.Id, out queue))
                {
                    m_publishQueues[session.Id] = queue = new SessionPublishQueue(m_server, session, m_maxPublishRequestCount);
                }

                queue.Add(subscription);

                // get the count for the diagnostics.
                publishingIntervalCount = GetPublishingIntervalCount();
            }

            lock (m_statusMessagesLock)
            {
                Queue<StatusMessage> messagesQueue = null;
                if (!m_statusMessages.TryGetValue(session.Id, out messagesQueue))
                {
                    m_statusMessages[session.Id] = new Queue<StatusMessage>();
                }
            }

            lock (m_server.DiagnosticsWriteLock)
            {
                ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                diagnostics.CurrentSubscriptionCount++;
                diagnostics.CumulatedSubscriptionCount++;
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            }

            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    diagnostics.CurrentSubscriptionsCount++;
                }
            }

            // raise subscription event.
            RaiseSubscriptionEvent(subscription, false);
        }

        /// <summary>
        /// Deletes group of subscriptions.
        /// </summary>
        public void DeleteSubscriptions(
            OperationContext context,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            bool diagnosticsExist = false;
            results = new StatusCodeCollection(subscriptionIds.Count);
            diagnosticInfos = new DiagnosticInfoCollection(subscriptionIds.Count);

            foreach (uint subscriptionId in subscriptionIds)
            {
                try
                {
                    StatusCode result = DeleteSubscription(context, subscriptionId);
                    results.Add(result);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
                catch (Exception e)
                {
                    ServiceResult result = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, String.Empty);
                    results.Add(result.Code);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_server, context, result);
                        diagnosticInfos.Add(diagnosticInfo);
                        diagnosticsExist = true;
                    }
                }
            }

            if (!diagnosticsExist)
            {
                diagnosticInfos.Clear();
            }
        }

        /// <summary>
        /// Publishes a subscription.
        /// </summary>
        public NotificationMessage Publish(
            OperationContext context,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            AsyncPublishOperation operation,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out StatusCodeCollection acknowledgeResults,
            out DiagnosticInfoCollection acknowledgeDiagnosticInfos)
        {
            availableSequenceNumbers = null;
            moreNotifications = false;

            // get publish queue for session.
            SessionPublishQueue queue = null;

            lock (m_lock)
            {
                if (!m_publishQueues.TryGetValue(context.Session.Id, out queue))
                {
                    if (m_subscriptions.Count == 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadNoSubscription);
                    }

                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }
            }

            // acknowlege previous messages.
            queue.Acknowledge(
                context,
                subscriptionAcknowledgements,
                out acknowledgeResults,
                out acknowledgeDiagnosticInfos);

            // update diagnostics.
            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    diagnostics.CurrentPublishRequestsInQueue++;
                }
            }

            // save results for asynchrounous operation.
            if (operation != null)
            {
                operation.Response.Results = acknowledgeResults;
                operation.Response.DiagnosticInfos = acknowledgeDiagnosticInfos;
            }

            // gets the next message that is ready to publish.
            NotificationMessage message = GetNextMessage(
                context,
                queue,
                operation,
                out subscriptionId,
                out availableSequenceNumbers,
                out moreNotifications);

            // if no message and no async operation then a timeout occurred.
            if (message == null && operation == null)
            {
                throw new ServiceResultException(StatusCodes.BadTimeout);
            }

            // return message.
            return message;
        }

        /// <summary>
        /// Called when a subscription expires.
        /// </summary>
        /// <param name="subscription">The subscription.</param>
        internal void SubscriptionExpired(Subscription subscription)
        {
            lock (m_statusMessagesLock)
            {
                StatusMessage message = new StatusMessage();
                message.SubscriptionId = subscription.Id;
                message.Message = subscription.PublishTimeout();

                Queue<StatusMessage> queue = null;

                if (subscription.SessionId != null && m_statusMessages.TryGetValue(subscription.SessionId, out queue))
                {
                    queue.Enqueue(message);
                }
            }
        }

        /// <summary>
        /// Completes the publish.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="operation">The asynchronous operation.</param>
        /// <returns>
        /// True if successful. False if the request has been requeued.
        /// </returns>
        public bool CompletePublish(
            OperationContext context,
            AsyncPublishOperation operation)
        {
            // get publish queue for session.
            SessionPublishQueue queue = null;

            lock (m_lock)
            {
                if (!m_publishQueues.TryGetValue(context.Session.Id, out queue))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }
            }

            uint subscriptionId = 0;
            UInt32Collection availableSequenceNumbers = null;
            bool moreNotifications = false;

            NotificationMessage message = null;

            Utils.LogTrace("Publish #{0} ReceivedFromClient", context.ClientHandle);
            bool requeue = false;

            do
            {
                // wait for a subscription to publish.
                Subscription subscription = queue.CompletePublish(requeue, operation, operation.Calldata);

                if (subscription == null)
                {
                    return false;
                }

                subscriptionId = subscription.Id;
                moreNotifications = false;

                // publish notifications.
                try
                {
                    requeue = false;

                    message = subscription.Publish(
                        context,
                        out availableSequenceNumbers,
                        out moreNotifications);

                    // a null message indicates a false alarm and that there were no notifications
                    // to publish and that the request needs to be requeued.
                    if (message != null)
                    {
                        break;
                    }

                    Utils.LogTrace("Publish False Alarm - Request #{0} Requeued.", context.ClientHandle);
                    requeue = true;
                }
                finally
                {
                    queue.PublishCompleted(subscription, moreNotifications);
                }
            }
            while (requeue);

            // fill in response if operation completed.
            if (message != null)
            {
                operation.Response.SubscriptionId = subscriptionId;
                operation.Response.AvailableSequenceNumbers = availableSequenceNumbers;
                operation.Response.MoreNotifications = moreNotifications;
                operation.Response.NotificationMessage = message;

                // update diagnostics.
                if (context.Session != null)
                {
                    lock (context.Session.DiagnosticsLock)
                    {
                        SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                        diagnostics.CurrentPublishRequestsInQueue--;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Publishes a subscription.
        /// </summary>
        public NotificationMessage GetNextMessage(
            OperationContext context,
            SessionPublishQueue queue,
            AsyncPublishOperation operation,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications)
        {
            subscriptionId = 0;
            availableSequenceNumbers = null;
            moreNotifications = false;

            NotificationMessage message = null;

            try
            {
                Utils.LogTrace("Publish #{0} ReceivedFromClient", context.ClientHandle);

                // check for status messages.
                lock (m_statusMessagesLock)
                {
                    Queue<StatusMessage> statusQueue = null;

                    if (m_statusMessages.TryGetValue(context.SessionId, out statusQueue))
                    {
                        if (statusQueue.Count > 0)
                        {
                            StatusMessage status = statusQueue.Dequeue();
                            subscriptionId = status.SubscriptionId;
                            return status.Message;
                        }
                    }
                }

                bool requeue = false;

                do
                {
                    // wait for a subscription to publish.
                    Subscription subscription = queue.Publish(
                        context.ClientHandle,
                        context.OperationDeadline,
                        requeue,
                        operation);

                    if (subscription == null)
                    {
                        Utils.LogTrace("Publish #{0} Timeout", context.ClientHandle);
                        return null;
                    }

                    subscriptionId = subscription.Id;
                    moreNotifications = false;

                    // publish notifications.
                    try
                    {
                        requeue = false;

                        message = subscription.Publish(
                            context,
                            out availableSequenceNumbers,
                            out moreNotifications);

                        // a null message indicates a false alarm and that there were no notifications
                        // to publish and that the request needs to be requeued.
                        if (message != null)
                        {
                            break;
                        }

                        Utils.LogTrace("Publish False Alarm - Request #{0} Requeued.", context.ClientHandle);
                        requeue = true;
                    }
                    finally
                    {
                        queue.PublishCompleted(subscription, moreNotifications);
                    }
                }
                while (requeue);
            }
            finally
            {
                // update diagnostics.
                if (context.Session != null)
                {
                    lock (context.Session.DiagnosticsLock)
                    {
                        SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                        diagnostics.CurrentPublishRequestsInQueue--;
                    }
                }
            }

            return message;
        }

        /// <summary>
        /// Modifies an existing subscription.
        /// </summary>
        public void ModifySubscription(
            OperationContext context,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            revisedPublishingInterval = requestedPublishingInterval;
            revisedLifetimeCount = requestedLifetimeCount;
            revisedMaxKeepAliveCount = requestedMaxKeepAliveCount;

            uint publishingIntervalCount = 0;

            // find subscription.
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }
            }

            double publishingInterval = subscription.PublishingInterval;

            // calculate publishing interval.
            revisedPublishingInterval = CalculatePublishingInterval(requestedPublishingInterval);

            // calculate the keep alive count.
            revisedMaxKeepAliveCount = CalculateKeepAliveCount(revisedPublishingInterval, requestedMaxKeepAliveCount);

            // calculate the lifetime count.
            revisedLifetimeCount = CalculateLifetimeCount(revisedPublishingInterval, revisedMaxKeepAliveCount, requestedLifetimeCount);

            // calculate the max notification count.
            maxNotificationsPerPublish = CalculateMaxNotificationsPerPublish(maxNotificationsPerPublish);

            // update the subscription.
            subscription.Modify(
                context,
                revisedPublishingInterval,
                revisedLifetimeCount,
                revisedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority);

            // get the count for the diagnostics.
            publishingIntervalCount = GetPublishingIntervalCount();

            lock (m_server.DiagnosticsWriteLock)
            {
                ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            }
        }

        /// <summary>
        /// Sets the publishing mode for a set of subscriptions.
        /// </summary>
        public void SetPublishingMode(
            OperationContext context,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            bool diagnosticsExist = false;

            results = new StatusCodeCollection(subscriptionIds.Count);
            diagnosticInfos = new DiagnosticInfoCollection(subscriptionIds.Count);

            for (int ii = 0; ii < subscriptionIds.Count; ii++)
            {
                try
                {
                    // find subscription.
                    Subscription subscription = null;

                    lock (m_lock)
                    {
                        if (!m_subscriptions.TryGetValue(subscriptionIds[ii], out subscription))
                        {
                            throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                        }
                    }

                    // update the subscription.
                    subscription.SetPublishingMode(context, publishingEnabled);

                    // save results.
                    results.Add(StatusCodes.Good);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
                catch (Exception e)
                {
                    ServiceResult result = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, String.Empty);
                    results.Add(result.Code);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_server, context, result);
                        diagnosticInfos.Add(diagnosticInfo);
                        diagnosticsExist = true;
                    }
                }

                if (!diagnosticsExist)
                {
                    diagnosticInfos.Clear();
                }
            }
        }

        /// <summary>
        /// Attaches a groups of subscriptions to a different session.
        /// </summary>
        public void TransferSubscriptions(
            OperationContext context,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = new TransferResultCollection();
            diagnosticInfos = new DiagnosticInfoCollection();

            Utils.LogInfo("TransferSubscriptions to SessionId={0}, Count={1}, sendInitialValues={2}",
                context.Session.Id, subscriptionIds.Count, sendInitialValues);

            for (int ii = 0; ii < subscriptionIds.Count; ii++)
            {
                TransferResult result = new TransferResult();
                try
                {
                    // find subscription.
                    Subscription subscription = null;
                    Session ownerSession = null;

                    lock (m_lock)
                    {
                        if (!m_subscriptions.TryGetValue(subscriptionIds[ii], out subscription))
                        {
                            result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                            results.Add(result);
                            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                            {
                                diagnosticInfos.Add(null);
                            }
                            continue;
                        }

                        lock (subscription.DiagnosticsLock)
                        {
                            SubscriptionDiagnosticsDataType diagnostics = subscription.Diagnostics;
                            diagnostics.TransferRequestCount++;
                        }

                        // check if new and old sessions are different
                        ownerSession = subscription.Session;
                        if (ownerSession != null)
                        {
                            if (!NodeId.IsNull(ownerSession.Id) && ownerSession.Id == context.Session.Id)
                            {
                                result.StatusCode = StatusCodes.BadNothingToDo;
                                results.Add(result);
                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    diagnosticInfos.Add(null);
                                }
                                continue;
                            }
                        }
                    }

                    // get the identity of the current or last owner
                    UserIdentityToken ownerIdentity = subscription.OwnerIdentity;

                    // Validate the identity of the user who owns/owned the subscription
                    // is the same as the new owner.
                    bool validIdentity = Utils.IsEqualUserIdentity(ownerIdentity, context.Session.IdentityToken);

                    // Test if anonymous user is using a
                    // secure session using Sign or SignAndEncrypt
                    if (validIdentity && (ownerIdentity is AnonymousIdentityToken))
                    {
                        var securityMode = context.ChannelContext.EndpointDescription.SecurityMode;
                        if (securityMode != MessageSecurityMode.Sign &&
                            securityMode != MessageSecurityMode.SignAndEncrypt)
                        {
                            validIdentity = false;
                        }
                    }

                    // continue if identity check failed
                    if (!validIdentity)
                    {
                        result.StatusCode = StatusCodes.BadUserAccessDenied;
                        results.Add(result);
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos.Add(null);
                        }
                        continue;
                    }

                    // transfer session, add subscription to publish queue
                    lock (m_lock)
                    {
                        subscription.TransferSession(context, sendInitialValues);

                        // remove from queue in old session
                        if (ownerSession != null)
                        {
                            if (m_publishQueues.TryGetValue(ownerSession.Id, out var ownerPublishQueue) &&
                                ownerPublishQueue != null)
                            {
                                ownerPublishQueue.Remove(subscription);
                            }
                        }

                        // add to queue in new session, create queue if necessary
                        if (!m_publishQueues.TryGetValue(context.SessionId, out var publishQueue) ||
                            publishQueue == null)
                        {
                            m_publishQueues[context.SessionId] = publishQueue =
                                new SessionPublishQueue(m_server, context.Session, m_maxPublishRequestCount);
                        }
                        publishQueue.Add(subscription);
                    }

                    lock (m_statusMessagesLock)
                    {
                        var processedQueue = new Queue<StatusMessage>();
                        if (m_statusMessages.TryGetValue(context.SessionId, out var messagesQueue) &&
                            messagesQueue != null)
                        {
                            // There must not be any messages left from
                            // the transferred subscription
                            foreach (var statusMessage in messagesQueue)
                            {
                                if (statusMessage.SubscriptionId == subscription.Id)
                                {
                                    continue;
                                }
                                processedQueue.Enqueue(statusMessage);
                            }
                        }
                        m_statusMessages[context.SessionId] = processedQueue;
                    }

                    if (context.Session != null)
                    {
                        lock (context.Session.DiagnosticsLock)
                        {
                            SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                            diagnostics.CurrentSubscriptionsCount++;
                        }
                    }

                    // raise subscription event.
                    RaiseSubscriptionEvent(subscription, false);
                    result.StatusCode = StatusCodes.Good;

                    // Notify old session with Good_SubscriptionTransferred.
                    if (ownerSession != null)
                    {
                        lock (ownerSession.DiagnosticsLock)
                        {
                            SessionDiagnosticsDataType diagnostics = ownerSession.SessionDiagnostics;
                            diagnostics.CurrentSubscriptionsCount--;
                        }
                        // add the Good_SubscriptionTransferred
                        lock (m_statusMessagesLock)
                        {
                            if (!NodeId.IsNull(ownerSession.Id) && m_statusMessages.TryGetValue(ownerSession.Id, out var queue))
                            {
                                var message = new StatusMessage {
                                    SubscriptionId = subscription.Id,
                                    Message = subscription.SubscriptionTransferred()
                                };
                                queue.Enqueue(message);
                            }
                        }
                    }

                    // Return the sequence numbers that are available for retransmission.
                    result.AvailableSequenceNumbers = subscription.AvailableSequenceNumbersForRetransmission();

                    lock (subscription.DiagnosticsLock)
                    {
                        SubscriptionDiagnosticsDataType diagnostics = subscription.Diagnostics;
                        diagnostics.TransferredToSameClientCount++;
                    }

                    // save results.
                    results.Add(result);
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }

                    Utils.LogInfo("Transferred subscription Id {0} to SessionId {1}", subscription.Id, context.Session.Id);
                }
                catch (Exception e)
                {
                    result.StatusCode = StatusCodes.Bad;
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(new DiagnosticInfo(e, context.DiagnosticsMask, false, null));
                    }
                }

                for (int i = 0; i < results.Count; i++)
                {
                    m_server.ReportAuditTransferSubscriptionEvent(context.AuditEntryId, context.Session, results[i].StatusCode);
                }
            }
        }

        /// <summary>
        /// Republishes a previously published notification message.
        /// </summary>
		public NotificationMessage Republish(
            OperationContext context,
            uint subscriptionId,
            uint retransmitSequenceNumber)
        {
            // find subscription.
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }
            }

            // fetch the message.
            return subscription.Republish(context, retransmitSequenceNumber);
        }

        /// <summary>
        /// Updates the triggers for the monitored item.
        /// </summary>
        public void SetTriggering(
            OperationContext context,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            // find subscription.
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }
            }

            // update the triggers.
            subscription.SetTriggering(
                context,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                out addResults,
                out addDiagnosticInfos,
                out removeResults,
                out removeDiagnosticInfos);
        }

        /// <summary>
        /// Adds monitored items to a subscription.
        /// </summary>
        public void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            int monitoredItemCountIncrement = 0;

            // find subscription.
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }
            }

            int currentMonitoredItemCount = subscription.MonitoredItemCount;

            // create the items.
            subscription.CreateMonitoredItems(
                context,
                timestampsToReturn,
                itemsToCreate,
                out results,
                out diagnosticInfos);

            monitoredItemCountIncrement = subscription.MonitoredItemCount - currentMonitoredItemCount;

            // update diagnostics.
            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    UpdateCurrentMonitoredItemsCount(diagnostics, monitoredItemCountIncrement);
                }
            }
        }

        /// <summary>
        /// Modifies monitored items in a subscription.
        /// </summary>
        public void ModifyMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            // find subscription.
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }
            }

            // modify the items.
            subscription.ModifyMonitoredItems(
                context,
                timestampsToReturn,
                itemsToModify,
                out results,
                out diagnosticInfos);
        }

        /// <summary>
        /// Deletes the monitored items in a subscription.
        /// </summary>
        public void DeleteMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            int monitoredItemCountIncrement = 0;

            // find subscription.
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }
            }

            int currentMonitoredItemCount = subscription.MonitoredItemCount;

            // create the items.
            subscription.DeleteMonitoredItems(
                context,
                monitoredItemIds,
                out results,
                out diagnosticInfos);

            monitoredItemCountIncrement = subscription.MonitoredItemCount - currentMonitoredItemCount;

            // update diagnostics.
            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    UpdateCurrentMonitoredItemsCount(diagnostics, monitoredItemCountIncrement);
                }
            }
        }

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        public void SetMonitoringMode(
            OperationContext context,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            // find subscription.
            Subscription subscription = null;

            lock (m_lock)
            {
                if (!m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }
            }

            // create the items.
            subscription.SetMonitoringMode(
                context,
                monitoringMode,
                monitoredItemIds,
                out results,
                out diagnosticInfos);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Calculates the publishing interval.
        /// </summary>
        protected virtual double CalculatePublishingInterval(double publishingInterval)
        {
            if (Double.IsNaN(publishingInterval) || publishingInterval < m_minPublishingInterval)
            {
                publishingInterval = m_minPublishingInterval;
            }

            if (publishingInterval > m_maxPublishingInterval)
            {
                publishingInterval = m_maxPublishingInterval;
            }

            if (publishingInterval < m_publishingResolution)
            {
                publishingInterval = m_publishingResolution;
            }

            if (publishingInterval % m_publishingResolution != 0)
            {
                publishingInterval = (((int)publishingInterval) / ((int)m_publishingResolution) + 1) * m_publishingResolution;
            }

            return publishingInterval;
        }

        /// <summary>
        /// Calculates the keep alive count.
        /// </summary>
        protected virtual uint CalculateKeepAliveCount(double publishingInterval, uint keepAliveCount)
        {
            // set default.
            if (keepAliveCount == 0)
            {
                keepAliveCount = 3;
            }

            double keepAliveInterval = keepAliveCount * publishingInterval;

            // keep alive interval cannot be longer than the max subscription lifetime.
            if (keepAliveInterval > m_maxSubscriptionLifetime)
            {
                keepAliveCount = (uint)(m_maxSubscriptionLifetime / publishingInterval);

                if (keepAliveCount < UInt32.MaxValue)
                {
                    if (m_maxSubscriptionLifetime % publishingInterval != 0)
                    {
                        keepAliveCount++;
                    }
                }

                keepAliveInterval = keepAliveCount * publishingInterval;
            }

            // the time between publishes cannot exceed the max publishing interval.
            if (keepAliveInterval > m_maxPublishingInterval)
            {
                keepAliveCount = (uint)(m_maxPublishingInterval / publishingInterval);

                if (keepAliveCount < UInt32.MaxValue)
                {
                    if (m_maxPublishingInterval % publishingInterval != 0)
                    {
                        keepAliveCount++;
                    }
                }
            }

            return keepAliveCount;
        }

        /// <summary>
        /// Calculates the lifetime count.
        /// </summary>
        protected virtual uint CalculateLifetimeCount(double publishingInterval, uint keepAliveCount, uint lifetimeCount)
        {
            double lifetimeInterval = lifetimeCount * publishingInterval;

            // lifetime cannot be longer than the max subscription lifetime.
            if (lifetimeInterval > m_maxSubscriptionLifetime)
            {
                lifetimeCount = (uint)(m_maxSubscriptionLifetime / publishingInterval);

                if (lifetimeCount < UInt32.MaxValue)
                {
                    if (m_maxSubscriptionLifetime % publishingInterval != 0)
                    {
                        lifetimeCount++;
                    }
                }
            }

            // the lifetime must be greater than the keepalive.
            if (keepAliveCount < UInt32.MaxValue / 3)
            {
                if (keepAliveCount * 3 > lifetimeCount)
                {
                    lifetimeCount = keepAliveCount * 3;
                }

                lifetimeInterval = lifetimeCount * publishingInterval;
            }
            else
            {
                lifetimeCount = UInt32.MaxValue;
                lifetimeInterval = Double.MaxValue;
            }

            // apply the minimum.
            if (m_minSubscriptionLifetime > publishingInterval && m_minSubscriptionLifetime > lifetimeInterval)
            {
                lifetimeCount = (uint)(m_minSubscriptionLifetime / publishingInterval);

                if (lifetimeCount < UInt32.MaxValue)
                {
                    if (m_minSubscriptionLifetime % publishingInterval != 0)
                    {
                        lifetimeCount++;
                    }
                }
            }

            return lifetimeCount;
        }

        /// <summary>
        /// Calculates the maximum number of notifications per publish.
        /// </summary>
        protected virtual uint CalculateMaxNotificationsPerPublish(uint maxNotificationsPerPublish)
        {
            if (maxNotificationsPerPublish == 0 || maxNotificationsPerPublish > m_maxNotificationsPerPublish)
            {
                return m_maxNotificationsPerPublish;
            }

            return maxNotificationsPerPublish;
        }

        /// <summary>
        /// Creates a new instance of a subscription.
        /// </summary>
        protected virtual Subscription CreateSubscription(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            uint lifetimeCount,
            uint keepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            bool publishingEnabled)
        {
            Subscription subscription = new Subscription(
                m_server,
                context.Session,
                subscriptionId,
                publishingInterval,
                lifetimeCount,
                keepAliveCount,
                maxNotificationsPerPublish,
                priority,
                publishingEnabled,
                m_maxMessageCount);

            return subscription;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Periodically checks if the sessions have timed out.
        /// </summary>
        private void PublishSubscriptions(object data)
        {
            try
            {
                Utils.LogInfo("Subscription - Publish Thread {0:X8} Started.", Environment.CurrentManagedThreadId);

                int sleepCycle = Convert.ToInt32(data, CultureInfo.InvariantCulture);
                int timeToWait = sleepCycle;

                do
                {
                    DateTime start = DateTime.UtcNow;

                    SessionPublishQueue[] queues = null;
                    Subscription[] abandonedSubscriptions = null;

                    lock (m_lock)
                    {
                        // collect active session queues.
                        queues = new SessionPublishQueue[m_publishQueues.Count];
                        m_publishQueues.Values.CopyTo(queues, 0);

                        // collect abandoned subscriptions.
                        if (m_abandonedSubscriptions != null && m_abandonedSubscriptions.Count > 0)
                        {
                            abandonedSubscriptions = new Subscription[m_abandonedSubscriptions.Count];

                            for (int ii = 0; ii < abandonedSubscriptions.Length; ii++)
                            {
                                abandonedSubscriptions[ii] = m_abandonedSubscriptions[ii];
                            }
                        }
                    }

                    // check the publish timer for each subscription.
                    for (int ii = 0; ii < queues.Length; ii++)
                    {
                        queues[ii].PublishTimerExpired();
                    }

                    // check the publish timer for each abandoned subscription.
                    if (abandonedSubscriptions != null)
                    {
                        List<Subscription> subscriptionsToDelete = new List<Subscription>();

                        for (int ii = 0; ii < abandonedSubscriptions.Length; ii++)
                        {
                            Subscription subscription = abandonedSubscriptions[ii];

                            if (subscription.PublishTimerExpired() != PublishingState.Expired)
                            {
                                continue;
                            }

                            if (subscriptionsToDelete == null)
                            {
                                subscriptionsToDelete = new List<Subscription>();
                            }

                            subscriptionsToDelete.Add(subscription);
                            SubscriptionExpired(subscription);
                            Utils.LogInfo("Subscription - Abandoned Subscription Id={0} Delete Scheduled.", subscription.Id);
                        }

                        // schedule cleanup on a background thread.
                        if (subscriptionsToDelete.Count > 0)
                        {
                            lock (m_lock)
                            {
                                for (int ii = 0; ii < subscriptionsToDelete.Count; ii++)
                                {
                                    m_abandonedSubscriptions.Remove(subscriptionsToDelete[ii]);
                                }
                            }

                            CleanupSubscriptions(m_server, subscriptionsToDelete);
                        }
                    }

                    if (m_shutdownEvent.WaitOne(timeToWait))
                    {
                        Utils.LogInfo("Subscription - Publish Thread {0:X8} Exited Normally.", Environment.CurrentManagedThreadId);
                        break;
                    }

                    int delay = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                    timeToWait = sleepCycle;
                }
                while (true);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Subscription - Publish Thread {0:X8} Exited Unexpectedly.", Environment.CurrentManagedThreadId);
            }
        }

        /// <summary>
        /// A single thread to execute the condition refresh.
        /// </summary>
        private void ConditionRefreshWorker()
        {
            try
            {
                Utils.LogInfo("Subscription - ConditionRefresh Thread {0:X8} Started.", Environment.CurrentManagedThreadId);

                do
                {
                    ConditionRefreshTask conditionRefreshTask = null; ;

                    lock (m_conditionRefreshLock)
                    {
                        if (m_conditionRefreshQueue.Count > 0)
                        {
                            conditionRefreshTask = m_conditionRefreshQueue.Dequeue();
                        }
                        else
                        {
                            m_conditionRefreshEvent.Reset();
                        }
                    }

                    if (conditionRefreshTask == null)
                    {
                        m_conditionRefreshEvent.WaitOne();
                    }
                    else
                    {
                        if (conditionRefreshTask.MonitoredItemId == 0)
                        {
                            DoConditionRefresh(conditionRefreshTask.Subscription);
                        }
                        else
                        {
                            DoConditionRefresh2(conditionRefreshTask.Subscription, conditionRefreshTask.MonitoredItemId);
                        }
                    }

                    // use shutdown event to end loop
                    if (m_shutdownEvent.WaitOne(0))
                    {
                        Utils.LogInfo("Subscription - ConditionRefresh Thread {0:X8} Exited Normally.", Environment.CurrentManagedThreadId);
                        break;
                    }
                }
                while (true);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Subscription - ConditionRefresh Thread {0:X8} Exited Unexpectedly.", Environment.CurrentManagedThreadId);
            }
        }

        /// <summary>
        /// Cleanups the subscriptions.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="subscriptionsToDelete">The subscriptions to delete.</param>
        internal static void CleanupSubscriptions(IServerInternal server, IList<Subscription> subscriptionsToDelete)
        {
            if (subscriptionsToDelete != null && subscriptionsToDelete.Count > 0)
            {
                Utils.LogInfo("Server - {0} Subscriptions scheduled for delete.", subscriptionsToDelete.Count);

                Task.Run(() => {
                    CleanupSubscriptions(new object[] { server, subscriptionsToDelete });
                });
            }
        }

        /// <summary>
        /// Deletes any expired subscriptions.
        /// </summary>
        internal static void CleanupSubscriptions(object data)
        {
            try
            {
                Utils.LogInfo("Server - CleanupSubscriptions Task Started");

                object[] args = (object[])data;

                IServerInternal server = (IServerInternal)args[0];
                List<Subscription> subscriptions = (List<Subscription>)args[1];

                foreach (Subscription subscription in subscriptions)
                {
                    server.DeleteSubscription(subscription.Id);
                }

                Utils.LogInfo("Server - CleanupSubscriptions Task Completed");
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Server - CleanupSubscriptions Task Halted Unexpectedly");
            }
        }
        #endregion

        #region StatusMessage Class
        private class StatusMessage
        {
            public uint SubscriptionId;
            public NotificationMessage Message;
        }
        #endregion

        #region ConditionRefreshTask Class
        private class ConditionRefreshTask
        {
            public ConditionRefreshTask(Subscription subscription, uint monitoredItemId)
            {
                Subscription = subscription;
                MonitoredItemId = monitoredItemId;
            }

            public Subscription Subscription { get; private set; }
            public uint MonitoredItemId { get; private set; }

            public override bool Equals(Object obj)
            {
                var crt = obj as ConditionRefreshTask;

                if (crt != null)
                {
                    if (Subscription?.Id == crt.Subscription?.Id &&
                        MonitoredItemId == crt.MonitoredItemId)
                    {
                        return true;
                    }
                }

                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Subscription.Id, MonitoredItemId);
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private long m_lastSubscriptionId;
        private IServerInternal m_server;
        private double m_minPublishingInterval;
        private double m_maxPublishingInterval;
        private int m_publishingResolution;
        private uint m_maxSubscriptionLifetime;
        private uint m_minSubscriptionLifetime;
        private uint m_maxMessageCount;
        private uint m_maxNotificationsPerPublish;
        private int m_maxPublishRequestCount;
        private int m_maxSubscriptionCount;
        private Dictionary<uint, Subscription> m_subscriptions;
        private List<Subscription> m_abandonedSubscriptions;
        private Dictionary<NodeId, Queue<StatusMessage>> m_statusMessages;
        private Dictionary<NodeId, SessionPublishQueue> m_publishQueues;
        private ManualResetEvent m_shutdownEvent;
        private Queue<ConditionRefreshTask> m_conditionRefreshQueue;
        private ManualResetEvent m_conditionRefreshEvent;

        private object m_statusMessagesLock = new object();
        private object m_eventLock = new object();
        private object m_conditionRefreshLock = new object();
        private event SubscriptionEventHandler m_SubscriptionCreated;
        private event SubscriptionEventHandler m_SubscriptionDeleted;
        #endregion
    }

    /// <summary>
    /// Provides access to the subscription manager within the server.
    /// </summary>
    /// <remarks>
    /// Sinks that receive these events must not block the thread.
    /// </remarks>
    public interface ISubscriptionManager
    {
        /// <summary>
        /// Raised after a new subscription is created.
        /// </summary>
        event SubscriptionEventHandler SubscriptionCreated;

        /// <summary>
        /// Raised before a subscription is deleted.
        /// </summary>
        event SubscriptionEventHandler SubscriptionDeleted;

        /// <summary>
        /// Returns all of the subscriptions known to the subscription manager.
        /// </summary>
        /// <returns>A list of the subscriptions.</returns>
        IList<Subscription> GetSubscriptions();
    }

    /// <summary>
    /// The delegate for functions used to receive subscription related events.
    /// </summary>
    /// <param name="subscription">The subscription that was affected.</param>
    /// <param name="deleted">True if the subscription was deleted.</param>
    public delegate void SubscriptionEventHandler(Subscription subscription, bool deleted);
}
