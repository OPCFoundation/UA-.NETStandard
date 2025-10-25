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
using System.Threading;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the publish queues for a session.
    /// </summary>
    public class SessionPublishQueue : IDisposable
    {
        /// <summary>
        /// Creates a new queue.
        /// </summary>
        public SessionPublishQueue(IServerInternal server, ISession session, int maxPublishRequests)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<SessionPublishQueue>();
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_queuedRequests = new LinkedList<QueuedPublishRequest>();
            m_readyToPublish = new Queue<ISubscription>();
            m_queuedSubscriptions = [];
            m_maxRequestCount = maxPublishRequests;
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
                    while (m_queuedRequests.Count > 0)
                    {
                        QueuedPublishRequest request = m_queuedRequests.First.Value;
                        m_queuedRequests.RemoveFirst();

                        try
                        {
                            request.Tcs.TrySetException(new ServiceResultException(StatusCodes.BadServerHalted));
                            request.Dispose();
                        }
                        catch
                        {
                            // ignore errors.
                        }
                    }

                    m_queuedSubscriptions.Clear();
                }
            }
        }

        /// <summary>
        /// Waits for a subscription to be ready to publish.
        /// </summary>
        public Task<ISubscription> PublishAsync(string secureChannelId,
                                                DateTime operationTimeout,
                                                bool requeue,
                                                CancellationToken cancellationToken)
        {
            lock (m_lock)
            {
                // find the waiting subscription with the highest priority.
                QueuedSubscription subscriptionToPublish = GetSubscriptionToPublish();

                // check if a subscription is already waiting.
                if (subscriptionToPublish != null)
                {
                    // reset subscriptions waiting flag.
                    m_subscriptionsWaiting = false;
                    for (int jj = 0; jj < m_queuedSubscriptions.Count; jj++)
                    {
                        if (m_queuedSubscriptions[jj].ReadyToPublish)
                        {
                            m_subscriptionsWaiting = true;
                            break;
                        }
                    }

                    subscriptionToPublish.Publishing = true;
                    return Task.FromResult(subscriptionToPublish.Subscription);
                }

                // check for pending status message.
                if (m_customStatusToReturn != null)
                {
                    return Task.FromException<ISubscription>(
                        new ServiceResultException(m_customStatusToReturn.Value));
                }

                // check if queue is full.
                if (m_queuedRequests.Count >= m_maxRequestCount)
                {
                    return Task.FromException<ISubscription>(
                        new ServiceResultException(StatusCodes.BadTooManyPublishRequests));
                }

                // add to queue.
                var request = new QueuedPublishRequest(secureChannelId, operationTimeout, cancellationToken);

                if (requeue)
                {
                    m_queuedRequests.AddFirst(request);
                }
                else
                {
                    m_queuedRequests.AddLast(request);
                }

                return request.Tcs.Task;
            }
        }

        /// <summary>
        /// Clears the queues because the session is closing.
        /// </summary>
        /// <returns>The list of subscriptions in the queue.</returns>
        public IList<ISubscription> Close()
        {
            lock (m_lock)
            {
                // TraceState("SESSION CLOSED");

                // set any waiting publish requests to Status BadSessionClosed.
                while (m_queuedRequests.Count > 0)
                {
                    QueuedPublishRequest request = m_queuedRequests.First.Value;
                    m_queuedRequests.RemoveFirst();
                    request.Tcs.TrySetException(new ServiceResultException(StatusCodes.BadSessionClosed));
                    request.Dispose();
                }

                // tell the subscriptions that the session is closed.
                var subscriptions = new ISubscription[m_queuedSubscriptions.Count];

                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    subscriptions[ii] = m_queuedSubscriptions[ii].Subscription;
                    subscriptions[ii].SessionClosed();
                }

                // clear the queue.
                m_queuedSubscriptions.Clear();

                return subscriptions;
            }
        }

        /// <summary>
        /// Adds a subscription from the publish queue.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="subscription"/> is <c>null</c>.</exception>
        public void Add(ISubscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            lock (m_lock)
            {
                var queuedSubscription = new QueuedSubscription(subscription);

                m_queuedSubscriptions.Add(queuedSubscription);

                // TraceState("SUBSCRIPTION QUEUED");
            }
        }

        /// <summary>
        /// Removes a subscription from the publish queue.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="subscription"/> is <c>null</c>.</exception>
        public void Remove(ISubscription subscription, bool removeQueuedRequests)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            lock (m_lock)
            {
                // remove the subscription from the queue.
                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    if (ReferenceEquals(m_queuedSubscriptions[ii].Subscription, subscription))
                    {
                        m_queuedSubscriptions.RemoveAt(ii);
                        break;
                    }
                }

                if (removeQueuedRequests)
                {
                    RemoveQueuedRequests();
                }

                // TraceState("SUBSCRIPTION REMOVED");
            }
        }

        /// <summary>
        /// Removes outstanding requests if no subscriptions exist for the Session.
        /// </summary>
        public void RemoveQueuedRequests()
        {
            lock (m_lock)
            {
                // remove any outstanding publishes.
                if (m_queuedSubscriptions.Count == 0)
                {
                    while (m_queuedRequests.Count > 0)
                    {
                        QueuedPublishRequest request = m_queuedRequests.First.Value;
                        m_queuedRequests.RemoveFirst();
                        request.Tcs.TrySetException(new ServiceResultException(StatusCodes.BadNoSubscription));
                        request.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Try to publish a custom status message
        /// using a queued publish request.
        /// </summary>
        public bool TryPublishCustomStatus(StatusCode statusCode)
        {
            lock (m_lock)
            {
                if (m_queuedRequests.Count > 0)
                {
                    QueuedPublishRequest request = m_queuedRequests.Last.Value;
                    if (request.Tcs.Task.IsCompleted)
                    {
                        request.Dispose();
                        return false;
                    }

                    request.Tcs.TrySetException(new ServiceResultException(statusCode));
                    request.Dispose();
                    return true;
                }

                m_customStatusToReturn = statusCode;
                return false;
            }
        }

        /// <summary>
        /// Processes acknowledgements for previously published messages.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void Acknowledge(
            OperationContext context,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out StatusCodeCollection acknowledgeResults,
            out DiagnosticInfoCollection acknowledgeDiagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (subscriptionAcknowledgements == null)
            {
                throw new ArgumentNullException(nameof(subscriptionAcknowledgements));
            }

            lock (m_lock)
            {
                bool diagnosticsExist = false;
                acknowledgeResults = new StatusCodeCollection(subscriptionAcknowledgements.Count);
                acknowledgeDiagnosticInfos = new DiagnosticInfoCollection(
                    subscriptionAcknowledgements.Count);

                for (int ii = 0; ii < subscriptionAcknowledgements.Count; ii++)
                {
                    SubscriptionAcknowledgement acknowledgement = subscriptionAcknowledgements[ii];

                    bool found = false;

                    for (int jj = 0; jj < m_queuedSubscriptions.Count; jj++)
                    {
                        QueuedSubscription subscription = m_queuedSubscriptions[jj];

                        if (subscription.Subscription.Id == acknowledgement.SubscriptionId)
                        {
                            ServiceResult result = subscription.Subscription.Acknowledge(
                                context,
                                acknowledgement.SequenceNumber);

                            if (ServiceResult.IsGood(result))
                            {
                                acknowledgeResults.Add(StatusCodes.Good);

                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    acknowledgeDiagnosticInfos.Add(null);
                                }
                            }
                            else
                            {
                                acknowledgeResults.Add(result.Code);

                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    DiagnosticInfo diagnosticInfo = ServerUtils
                                        .CreateDiagnosticInfo(
                                            m_server,
                                            context,
                                            result,
                                            m_logger);
                                    acknowledgeDiagnosticInfos.Add(diagnosticInfo);
                                    diagnosticsExist = true;
                                }
                            }

                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        var result = new ServiceResult(StatusCodes.BadSubscriptionIdInvalid);
                        acknowledgeResults.Add(result.Code);

                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                result,
                                m_logger);
                            acknowledgeDiagnosticInfos.Add(diagnosticInfo);
                            diagnosticsExist = true;
                        }
                    }
                }

                if (!diagnosticsExist)
                {
                    acknowledgeDiagnosticInfos.Clear();
                }
            }
        }

        /// <summary>
        /// Adds a subscription back into the queue because it has more notifications to publish.
        /// </summary>
        public void PublishCompleted(ISubscription subscription, bool moreNotifications)
        {
            lock (m_lock)
            {
                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    if (ReferenceEquals(m_queuedSubscriptions[ii].Subscription, subscription))
                    {
                        m_queuedSubscriptions[ii].Publishing = false;

                        if (moreNotifications)
                        {
                            AssignSubscriptionToRequest(m_queuedSubscriptions[ii]);
                        }
                        else
                        {
                            m_queuedSubscriptions[ii].ReadyToPublish = false;
                            m_queuedSubscriptions[ii].Timestamp = DateTime.UtcNow;
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Puts a subscription back in the queue to be published.
        /// </summary>
        public void Requeue(ISubscription subscription)
        {
            lock (m_lock)
            {
                m_readyToPublish.Enqueue(subscription);
            }
        }

        /// <summary>
        /// Checks the state of the subscriptions.
        /// </summary>
        public void PublishTimerExpired()
        {
            var subscriptionsToDelete = new List<ISubscription>();

            lock (m_lock)
            {
                var liveSubscriptions = new List<QueuedSubscription>(m_queuedSubscriptions.Count);

                // check each available subscription.
                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    QueuedSubscription subscription = m_queuedSubscriptions[ii];

                    PublishingState state = subscription.Subscription.PublishTimerExpired();

                    // check for expired subscription.
                    if (state == PublishingState.Expired)
                    {
                        subscriptionsToDelete.Add(subscription.Subscription);
                        ((SubscriptionManager)m_server.SubscriptionManager).SubscriptionExpired(
                            subscription.Subscription);
                        continue;
                    }

                    liveSubscriptions.Add(subscription);

                    // check if idle.
                    if (state == PublishingState.Idle)
                    {
                        subscription.ReadyToPublish = false;
                        continue;
                    }

                    // do nothing if subscription has already been flagged as available.
                    if (subscription.ReadyToPublish)
                    {
                        if (subscription.ReadyToPublish && m_queuedRequests.Count == 0)
                        {
                            if (!m_subscriptionsWaiting)
                            {
                                m_subscriptionsWaiting = true;
                                // TraceState("SUBSCRIPTIONS WAITING");
                            }
                        }

                        continue;
                    }

                    // assign subscription to request if one is available.
                    if (!subscription.Publishing)
                    {
                        AssignSubscriptionToRequest(subscription);
                    }
                }

                // only keep the live subscriptions.
                m_queuedSubscriptions = liveSubscriptions;

                // schedule cleanup on a background thread.
                SubscriptionManager.CleanupSubscriptions(m_server, subscriptionsToDelete, m_logger);
            }
        }

        /// <summary>
        /// Checks the state of the subscriptions.
        /// </summary>
        private void AssignSubscriptionToRequest(QueuedSubscription subscription)
        {
            lock (m_lock)
            {
                // find a request.
                while (m_queuedRequests.Count > 0)
                {
                    QueuedPublishRequest request = m_queuedRequests.First.Value;
                    m_queuedRequests.RemoveFirst();

                    if (request.Tcs.Task.IsCompleted)
                    {
                        request.Dispose();
                        continue;
                    }

                    // check secure channel.
                    if (!m_session.IsSecureChannelValid(request.SecureChannelId))
                    {
                        m_logger.LogWarning("Publish abandoned because the secure channel changed.");
                        request.Tcs.TrySetException(new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid));
                        request.Dispose();
                        continue;
                    }

                    m_logger.LogTrace(
                        "PUBLISH: #{Id} Assigned To Subscription({SubscriptionId}).",
                        request.SecureChannelId,
                        subscription.Subscription.Id);

                    subscription.Publishing = true;
                    request.Tcs.TrySetResult(subscription.Subscription);
                    request.Dispose();
                    return;
                }

                // mark it as available.
                subscription.ReadyToPublish = true;
                subscription.Timestamp = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Selects a subscription to publish based on priority and timestamp.
        /// </summary>
        private QueuedSubscription GetSubscriptionToPublish()
        {
            var availableSubscriptions = new List<QueuedSubscription>();

            for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
            {
                QueuedSubscription subscription = m_queuedSubscriptions[ii];

                if (subscription.ReadyToPublish && !subscription.Publishing)
                {
                    availableSubscriptions.Add(subscription);
                }
            }

            // find the subscription that has been waiting the longest.
            if (availableSubscriptions.Count > 0)
            {
                byte maxPriority = 0;
                DateTime earliestTimestamp = DateTime.MaxValue;
                QueuedSubscription subscriptionToPublish = null;

                for (int ii = 0; ii < availableSubscriptions.Count; ii++)
                {
                    QueuedSubscription subscription = availableSubscriptions[ii];
                    byte priority = subscription.Subscription.Priority;

                    if (priority > maxPriority)
                    {
                        maxPriority = priority;
                        earliestTimestamp = DateTime.MaxValue;
                    }

                    if (priority >= maxPriority && earliestTimestamp > subscription.Timestamp)
                    {
                        earliestTimestamp = subscription.Timestamp;
                        subscriptionToPublish = subscription;
                    }
                }

                return subscriptionToPublish;
            }

            return null;
        }

        /// <summary>
        /// A request queued while waiting for a subscription to be ready to publish.
        /// </summary>
        private sealed class QueuedPublishRequest : IDisposable
        {
            public QueuedPublishRequest(string secureChannelId, DateTime operationTimeout, CancellationToken cancellationToken)
            {
                SecureChannelId = secureChannelId;
                OperationTimeout = operationTimeout;
                Tcs = new TaskCompletionSource<ISubscription>();
                m_cancellationTokenRegistration = cancellationToken.Register(
                    () => Tcs.TrySetCanceled());
                // Cancell publish request if it times out
                if (operationTimeout < DateTime.MaxValue)
                {
                    m_cancellationTokenSource = new CancellationTokenSource(operationTimeout.AddMilliseconds(500) - DateTime.UtcNow);
                    m_cancellationTokenRegistration2 = m_cancellationTokenSource.Token.Register(
                    () => Tcs.TrySetException(new ServiceResultException(StatusCodes.BadTimeout)));
                }
            }

            public void Dispose()
            {
                m_cancellationTokenRegistration.Dispose();
                m_cancellationTokenSource.Dispose();
                m_cancellationTokenRegistration2.Dispose();
            }

            public readonly string SecureChannelId;
            public readonly DateTime OperationTimeout;
            public readonly TaskCompletionSource<ISubscription> Tcs;
            private readonly CancellationTokenRegistration m_cancellationTokenRegistration;
            private readonly CancellationTokenSource m_cancellationTokenSource;
            private readonly CancellationTokenRegistration m_cancellationTokenRegistration2;
        }

        /// <summary>
        /// Stores a subscription that belongs to this Session Publish Queue.
        /// </summary>
        private sealed class QueuedSubscription
        {
            public QueuedSubscription(ISubscription subscription)
            {
                Subscription = subscription;
                ReadyToPublish = false;
                Timestamp = DateTime.UtcNow;
            }

            public ISubscription Subscription { get; }
            public DateTime Timestamp { get; set; }
            public bool ReadyToPublish { get; set; }
            public bool Publishing { get; set; }
        }

        /// <summary>
        /// Dumps the current state of the session queue.
        /// </summary>
        internal void TraceState(string context, params object[] args)
        {
            // Pseudocode:
            // 1. Fast exit if trace not enabled.
            // 2. Format context with args (InvariantCulture).
            // 3. Under lock gather:
            //    - sessionId
            //    - subscriptionCount
            //    - requestCount
            //    - readyToPublishCount
            //    - expiredCount
            // 4. Emit single structured LogTrace with constant template.
            if (!m_logger.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            object sessionId = null;
            int subscriptionCount;
            int requestCount;
            int readyToPublishCount = 0;
            int expiredCount = 0;

            lock (m_lock)
            {
                sessionId = m_session?.Id;
                subscriptionCount = m_queuedSubscriptions.Count;
                requestCount = m_queuedRequests.Count;

                for (int i = 0; i < m_queuedSubscriptions.Count; i++)
                {
                    if (m_queuedSubscriptions[i].ReadyToPublish)
                    {
                        readyToPublishCount++;
                    }
                }

                foreach (QueuedPublishRequest request in m_queuedRequests)
                {
                    if (request.OperationTimeout < DateTime.UtcNow)
                    {
                        expiredCount++;
                    }
                }
            }

            m_logger.LogTrace(
                "PublishQueue {Context}, SessionId={SessionId}, SubscriptionCount={SubscriptionCount}, RequestCount={RequestCount}, ReadyToPublishCount={ReadyToPublishCount}, ExpiredCount={ExpiredCount}",
                Utils.Format(context, args),
                sessionId,
                subscriptionCount,
                requestCount,
                readyToPublishCount,
                expiredCount);
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly IServerInternal m_server;
        private readonly ISession m_session;
        private readonly LinkedList<QueuedPublishRequest> m_queuedRequests;
        private readonly Queue<ISubscription> m_readyToPublish;
        private List<QueuedSubscription> m_queuedSubscriptions;
        private readonly int m_maxRequestCount;
        private StatusCode? m_customStatusToReturn;
        private bool m_subscriptionsWaiting;
    }
}
