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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the publish queues for a session.
    /// </summary>
    internal sealed class SessionPublishQueue : IDisposable
    {
        /// <summary>
        /// Creates a new queue.
        /// </summary>
        public SessionPublishQueue(IServerInternal server, ISession session, int maxPublishRequests)
            : this(server, session, maxPublishRequests, timeProvider: null)
        {
        }

        /// <summary>
        /// Creates a new queue.
        /// </summary>
        public SessionPublishQueue(
            IServerInternal server,
            ISession session,
            int maxPublishRequests,
            TimeProvider? timeProvider)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<SessionPublishQueue>();
            m_backgroundWork = new BackgroundTaskScope(
                nameof(SessionPublishQueue),
                server.Telemetry);
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_queuedRequests = new LinkedList<QueuedPublishRequest>();
            m_queuedSubscriptions = new ConcurrentDictionary<uint, QueuedSubscription>();
            m_transferClaims = [];
            m_maxRequestCount = maxPublishRequests;
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
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
        /// Releases the queued requests and clears the queue state.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Signal only: Dispose is synchronous. A cleanup already running
                // finishes deleting the subscriptions it captured.
                m_backgroundWork.Dispose();

                lock (m_lock)
                {
                    while (m_queuedRequests.Count > 0)
                    {
                        QueuedPublishRequest request = m_queuedRequests.First!.Value;
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
                    m_transferClaims.Clear();
                }
            }
        }

        /// <summary>
        /// Waits for a subscription to be ready to publish. When the request parks (is
        /// queued to wait for the next notification), the supplied park sink is notified
        /// so the request-processing worker can be released for the duration of the wait.
        /// </summary>
        public Task<ISubscriptionPublishPipeline> PublishAsync(string secureChannelId,
                                                DateTime operationTimeout,
                                                bool requeue,
                                                IRequestParkSink? parkSink,
                                                CancellationToken cancellationToken)
        {
            if (m_queuedSubscriptions.IsEmpty)
            {
                return Task.FromException<ISubscriptionPublishPipeline>(
                    new ServiceResultException(StatusCodes.BadNoSubscription));
            }

            QueuedSubscription? subscriptionToPublish;
            lock (m_lock)
            {
                // find the waiting subscription with the highest priority.
                subscriptionToPublish = GetSubscriptionToPublish();

                if (subscriptionToPublish != null)
                {
                    return Task.FromResult(subscriptionToPublish.Subscription);
                }

                // check if queue is full.
                if (m_queuedRequests.Count >= m_maxRequestCount)
                {
                    return Task.FromException<ISubscriptionPublishPipeline>(
                        new ServiceResultException(StatusCodes.BadTooManyPublishRequests));
                }

                // add to queue.
                var request = new QueuedPublishRequest(secureChannelId, operationTimeout, m_timeProvider, cancellationToken);

                if (requeue)
                {
                    m_queuedRequests.AddFirst(request);
                }
                else
                {
                    m_queuedRequests.AddLast(request);
                }

                // The request is now parked waiting for a notification: release the
                // processing worker so it does not remain blocked for the whole wait.
                parkSink?.NotifyParked();

                return request.Tcs.Task;
            }
        }

        /// <summary>
        /// Clears the queues because the session is closing.
        /// </summary>
        /// <returns>The list of subscriptions in the queue.</returns>
        public IList<ISubscriptionPublishPipeline> Close()
        {
            var queuedSubscriptions = new List<ISubscriptionPublishPipeline>();
            var subscriptions = new List<ISubscriptionPublishPipeline>();

            lock (m_lock)
            {
                // TraceState("SESSION CLOSED");

                // set any waiting publish requests to Status BadSessionClosed.
                while (m_queuedRequests.Count > 0)
                {
                    QueuedPublishRequest request = m_queuedRequests.First!.Value;
                    m_queuedRequests.RemoveFirst();
                    request.Tcs.TrySetException(new ServiceResultException(StatusCodes.BadSessionClosed));
                    request.Dispose();
                }

                // tell the subscriptions that the session is closed.
                foreach (KeyValuePair<uint, QueuedSubscription> entry in m_queuedSubscriptions)
                {
                    queuedSubscriptions.Add(entry.Value.Subscription);
                }

                // clear the queue.
                m_queuedSubscriptions.Clear();
                m_transferClaims.Clear();
            }

            foreach (ISubscriptionPublishPipeline subscription in queuedSubscriptions)
            {
                if (subscription.SessionClosed(m_session))
                {
                    subscriptions.Add(subscription);
                }
            }

            return subscriptions;
        }

        /// <summary>
        /// Adds a subscription from the publish queue.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="subscription"/> is <c>null</c>.</exception>
        public void Add(ISubscriptionPublishPipeline subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            m_queuedSubscriptions[subscription.Id] = new QueuedSubscription(subscription);

            // TraceState("SUBSCRIPTION QUEUED");
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

            // remove the subscription from the queue.
            m_queuedSubscriptions.TryRemove(subscription.Id, out _);

            if (removeQueuedRequests)
            {
                RemoveQueuedRequests();
            }

            // TraceState("SUBSCRIPTION REMOVED");

        }

        /// <summary>
        /// Checks whether the exact subscription is still in this queue.
        /// </summary>
        internal bool ContainsSubscription(ISubscription subscription)
        {
            return m_queuedSubscriptions.TryGetValue(
                    subscription.Id,
                    out QueuedSubscription? queuedSubscription) &&
                ReferenceEquals(queuedSubscription.Subscription, subscription);
        }

        /// <summary>
        /// Claims and removes the exact subscription entry before transfer callbacks run.
        /// </summary>
        internal bool TryClaimForTransfer(
            Subscription subscription,
            ISession sourceSession,
            out SubscriptionTransferClaim? claim)
        {
            lock (m_lock)
            {
                claim = null;
                if (!m_queuedSubscriptions.TryGetValue(
                        subscription.Id,
                        out QueuedSubscription? queuedSubscription) ||
                    !ReferenceEquals(queuedSubscription.Subscription, subscription) ||
                    m_transferClaims.ContainsKey(subscription.Id))
                {
                    return false;
                }

                if (!subscription.TryBeginTransfer(sourceSession))
                {
                    return false;
                }
                if (!TryRemoveExact(queuedSubscription))
                {
                    subscription.AbortTransfer(sourceSession);
                    return false;
                }

                claim = new SubscriptionTransferClaim(queuedSubscription);
                m_transferClaims.Add(subscription.Id, claim);
                return true;
            }
        }

        /// <summary>
        /// Restores a previously claimed subscription entry when transfer preparation fails before ownership changes.
        /// </summary>
        /// <param name="claim">The queue entry claim to return to active publishing.</param>
        /// <returns><c>true</c> when the claim was current and the entry was restored.</returns>
        internal bool RestoreTransferClaim(SubscriptionTransferClaim claim)
        {
            lock (m_lock)
            {
                uint subscriptionId = claim.Entry.Subscription.Id;
                if (!m_transferClaims.TryGetValue(
                        subscriptionId,
                        out SubscriptionTransferClaim? currentClaim) ||
                    !ReferenceEquals(currentClaim, claim))
                {
                    return false;
                }

                m_transferClaims.Remove(subscriptionId);
                return m_queuedSubscriptions.TryAdd(subscriptionId, claim.Entry);
            }
        }

        /// <summary>
        /// Removes a transfer claim after the destination session has accepted the subscription.
        /// </summary>
        /// <param name="claim">The queue entry claim that completed.</param>
        internal void CompleteTransferClaim(SubscriptionTransferClaim claim)
        {
            lock (m_lock)
            {
                uint subscriptionId = claim.Entry.Subscription.Id;
                if (m_transferClaims.TryGetValue(
                        subscriptionId,
                        out SubscriptionTransferClaim? currentClaim) &&
                    ReferenceEquals(currentClaim, claim))
                {
                    m_transferClaims.Remove(subscriptionId);
                }
            }
        }

        internal bool TryRemoveForTransfer(ISubscription subscription)
        {
            lock (m_lock)
            {
                return m_queuedSubscriptions.TryGetValue(
                        subscription.Id,
                        out QueuedSubscription? queuedSubscription) &&
                    ReferenceEquals(queuedSubscription.Subscription, subscription) &&
                    TryRemoveExact(queuedSubscription);
            }
        }

        /// <summary>
        /// Removes outstanding requests if no subscriptions exist for the Session.
        /// </summary>
        public void RemoveQueuedRequests()
        {
            if (!m_queuedSubscriptions.IsEmpty)
            {
                return;
            }

            lock (m_lock)
            {
                // remove any outstanding publishes.
                while (m_queuedRequests.Count > 0)
                {
                    QueuedPublishRequest request = m_queuedRequests.First!.Value;
                    m_queuedRequests.RemoveFirst();
                    request.Tcs.TrySetException(new ServiceResultException(StatusCodes.BadNoSubscription));
                    request.Dispose();
                }
            }
        }

        /// <summary>
        /// Try to publish a custom status message
        /// using a queued publish request.
        /// Returns true if a queued request was found and processed.
        /// Returns the found publish request immediately to the caller.
        /// If status code is good, the caller is expected to publish any queued status messages.
        /// If status code is bad a ServiceResultException is thrown to the caller.
        /// </summary>
        public bool TryPublishCustomStatus(StatusCode statusCode)
        {
            lock (m_lock)
            {
                while (m_queuedRequests.Count > 0)
                {
                    QueuedPublishRequest request = m_queuedRequests.Last!.Value;
                    m_queuedRequests.RemoveLast();
                    if (request.Tcs.Task.IsCompleted)
                    {
                        request.Dispose();
                        continue;
                    }

                    // for good status codes return to caller (SubscriptionManager) with null subscription
                    // to publish queued StatusMessages from there
                    if (ServiceResult.IsGood(statusCode))
                    {
                        request.Tcs.TrySetResult(null!);
                    }
                    // throw a ServiceResultException for bad status codes
                    else
                    {
                        request.Tcs.TrySetException(new ServiceResultException(statusCode));
                    }
                    request.Dispose();
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Processes acknowledgements for previously published messages.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void Acknowledge(
            OperationContext context,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            out ArrayOf<StatusCode> acknowledgeResults,
            out ArrayOf<DiagnosticInfo> acknowledgeDiagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            bool diagnosticsExist = false;
            var acknowledgeResultList = new List<StatusCode>(subscriptionAcknowledgements.Count);
            var acknowledgeDiagnosticInfoList = new List<DiagnosticInfo>(subscriptionAcknowledgements.Count);

            for (int ii = 0; ii < subscriptionAcknowledgements.Count; ii++)
            {
                SubscriptionAcknowledgement acknowledgement = subscriptionAcknowledgements[ii];

                if (m_queuedSubscriptions.TryGetValue(acknowledgement.SubscriptionId, out QueuedSubscription? subscription))
                {
                    ServiceResult? result = subscription.Subscription.Acknowledge(
                        context,
                        acknowledgement.SequenceNumber);

                    if (ServiceResult.IsGood(result))
                    {
                        acknowledgeResultList.Add(StatusCodes.Good);

                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            acknowledgeDiagnosticInfoList.Add(null!);
                        }
                    }
                    else
                    {
                        acknowledgeResultList.Add(result!.Code);

                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo? diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                                m_server,
                                context,
                                result,
                                m_logger);
                            acknowledgeDiagnosticInfoList.Add(diagnosticInfo!);
                            diagnosticsExist = true;
                        }
                    }
                }
                else
                {
                    var result = new ServiceResult(StatusCodes.BadSubscriptionIdInvalid);
                    acknowledgeResultList.Add(result.Code);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo? diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            result,
                            m_logger);
                        acknowledgeDiagnosticInfoList.Add(diagnosticInfo!);
                        diagnosticsExist = true;
                    }
                }
            }

            if (!diagnosticsExist)
            {
                acknowledgeDiagnosticInfoList.Clear();
            }
            acknowledgeResults = acknowledgeResultList;
            acknowledgeDiagnosticInfos = acknowledgeDiagnosticInfoList;
        }

        /// <summary>
        /// Adds a subscription back into the queue because it has more notifications to publish.
        /// </summary>
        public void PublishCompleted(ISubscription subscription, bool moreNotifications)
        {
            if (!m_queuedSubscriptions.TryGetValue(
                    subscription.Id,
                    out QueuedSubscription? queuedSubscription))
            {
                lock (m_lock)
                {
                    PublishCompletedTransferClaimNoLock(subscription, moreNotifications);
                }
                return;
            }

            lock (m_lock)
            {
                if (m_queuedSubscriptions.TryGetValue(
                        subscription.Id,
                        out QueuedSubscription? currentSubscription) &&
                    ReferenceEquals(currentSubscription, queuedSubscription))
                {
                    // Flag the subscription as available and let the selection policy decide
                    // which of the available subscriptions is handed to a waiting request.
                    queuedSubscription.Publishing = false;
                    queuedSubscription.ReadyToPublish = moreNotifications;
                    queuedSubscription.Timestamp = DateTime.UtcNow;

                    if (moreNotifications)
                    {
                        AssignSubscriptionsToRequests();
                    }
                    return;
                }

                PublishCompletedTransferClaimNoLock(subscription, moreNotifications);
            }
        }

        /// <summary>
        /// Puts a subscription back in the queue to be published.
        /// </summary>
        public void Requeue(ISubscription subscription)
        {
            if (!m_queuedSubscriptions.TryGetValue(
                    subscription.Id,
                    out QueuedSubscription? queuedSubscription))
            {
                lock (m_lock)
                {
                    RequeueTransferClaimNoLock(subscription);
                }
                return;
            }

            lock (m_lock)
            {
                if (m_queuedSubscriptions.TryGetValue(
                        subscription.Id,
                        out QueuedSubscription? currentSubscription) &&
                    ReferenceEquals(currentSubscription, queuedSubscription))
                {
                    queuedSubscription.Publishing = false;
                    queuedSubscription.ReadyToPublish = true;
                    return;
                }

                RequeueTransferClaimNoLock(subscription);
            }
        }

        /// <summary>
        /// Checks the state of the subscriptions.
        /// </summary>
        public void PublishTimerExpired()
        {
            PublishTimerExpired(CapturePublishTimerSnapshot());
        }

        /// <summary>
        /// Captures the exact queue entries processed by one publish timer pass.
        /// </summary>
        internal IReadOnlyList<QueuedSubscription> CapturePublishTimerSnapshot()
        {
            var subscriptions = new List<QueuedSubscription>(m_queuedSubscriptions.Count);
            foreach (KeyValuePair<uint, QueuedSubscription> entry in m_queuedSubscriptions)
            {
                subscriptions.Add(entry.Value);
            }
            return subscriptions;
        }

        /// <summary>
        /// Checks the state of an exact publish timer snapshot.
        /// </summary>
        internal void PublishTimerExpired(IReadOnlyList<QueuedSubscription> queuedSubscriptions)
        {
            var subscriptionsToDelete = new List<ISubscription>();
            List<QueuedSubscription>? notifyingSubscriptions = null;

            // check each available subscription.
            for (int ii = 0; ii < queuedSubscriptions.Count; ii++)
            {
                QueuedSubscription subscription = queuedSubscriptions[ii];
                if (!IsCurrentSubscription(subscription))
                {
                    continue;
                }

                PublishingState state = subscription.Subscription.PublishTimerExpired();

                // check for expired subscription.
                if (state == PublishingState.Expired)
                {
                    var subscriptionManager = (SubscriptionManager)m_server.SubscriptionManager;
                    if (!subscriptionManager.TryClaimSubscriptionExpiration(
                            this,
                            m_session,
                            subscription))
                    {
                        continue;
                    }

                    subscriptionsToDelete.Add(subscription.Subscription);
                    subscriptionManager.SubscriptionExpired(subscription.Subscription);
                    continue;
                }

                // check if idle.
                if (state == PublishingState.Idle)
                {
                    lock (m_lock)
                    {
                        if (IsCurrentSubscriptionNoLock(subscription))
                        {
                            subscription.ReadyToPublish = false;
                        }
                    }
                    continue;
                }

                // do nothing if subscription has already been flagged as available.
                if (subscription.ReadyToPublish)
                {
                    continue;
                }

                // collect the subscription, it is assigned to a request further below.
                if (!subscription.Publishing)
                {
                    (notifyingSubscriptions ??= []).Add(subscription);
                }
            }

            if (notifyingSubscriptions != null)
            {
                lock (m_lock)
                {
                    // Flag every notifying subscription as available before any request is
                    // served. Assigning them one by one while iterating would hand the
                    // waiting requests out in the (unordered) iteration order of the
                    // subscription dictionary and bypass the priority and timestamp based
                    // selection policy applied by PublishAsync.
                    foreach (QueuedSubscription subscription in notifyingSubscriptions)
                    {
                        if (!IsCurrentSubscriptionNoLock(subscription) ||
                            subscription.Publishing ||
                            subscription.ReadyToPublish)
                        {
                            continue;
                        }

                        subscription.ReadyToPublish = true;
                        subscription.Timestamp = DateTime.UtcNow;
                    }

                    AssignSubscriptionsToRequests();
                }
            }

            // schedule cleanup on a background thread.
            SubscriptionManager.CleanupSubscriptions(
                m_server, subscriptionsToDelete, m_logger, m_backgroundWork);
        }

        /// <summary>
        /// Removes the exact queue entry captured by a publish timer pass.
        /// </summary>
        internal bool TryRemoveForExpiration(QueuedSubscription queuedSubscription)
        {
            lock (m_lock)
            {
                return TryRemoveExact(queuedSubscription);
            }
        }

        private bool IsCurrentSubscription(QueuedSubscription queuedSubscription)
        {
            return IsCurrentSubscriptionNoLock(queuedSubscription);
        }

        private void PublishCompletedTransferClaimNoLock(
            ISubscription subscription,
            bool moreNotifications)
        {
            if (m_transferClaims.TryGetValue(
                    subscription.Id,
                    out SubscriptionTransferClaim? transferClaim) &&
                ReferenceEquals(transferClaim.Entry.Subscription, subscription))
            {
                transferClaim.Entry.Publishing = false;
                transferClaim.Entry.ReadyToPublish = moreNotifications;
            }
        }

        private void RequeueTransferClaimNoLock(ISubscription subscription)
        {
            if (m_transferClaims.TryGetValue(
                    subscription.Id,
                    out SubscriptionTransferClaim? transferClaim) &&
                ReferenceEquals(transferClaim.Entry.Subscription, subscription))
            {
                transferClaim.Entry.Publishing = false;
                transferClaim.Entry.ReadyToPublish = true;
            }
        }

        private bool IsCurrentSubscriptionNoLock(QueuedSubscription queuedSubscription)
        {
            return m_queuedSubscriptions.TryGetValue(
                    queuedSubscription.Subscription.Id,
                    out QueuedSubscription? currentSubscription) &&
                ReferenceEquals(currentSubscription, queuedSubscription);
        }

        private bool TryRemoveExact(QueuedSubscription queuedSubscription)
        {
            var entry = new KeyValuePair<uint, QueuedSubscription>(
                queuedSubscription.Subscription.Id,
                queuedSubscription);
            return ((ICollection<KeyValuePair<uint, QueuedSubscription>>)m_queuedSubscriptions)
                .Remove(entry);
        }

        /// <summary>
        /// Hands the subscriptions that are ready to publish to the waiting publish
        /// requests. The subscriptions are selected with the same priority and timestamp
        /// based policy as <see cref="PublishAsync"/>, so the order in which subscriptions
        /// became ready does not determine which one is published first.
        /// </summary>
        private void AssignSubscriptionsToRequests()
        {
            while (m_queuedRequests.Count > 0)
            {
                QueuedSubscription? subscriptionToPublish = GetSubscriptionToPublish();

                if (subscriptionToPublish == null)
                {
                    break;
                }

                if (!TryAssignSubscriptionToRequest(subscriptionToPublish))
                {
                    // no usable request left, keep the subscription available.
                    subscriptionToPublish.Publishing = false;
                    break;
                }
            }
        }

        /// <summary>
        /// Completes the next usable publish request with the subscription. Returns false
        /// if no usable request is queued, in which case the subscription stays available.
        /// </summary>
        private bool TryAssignSubscriptionToRequest(QueuedSubscription subscription)
        {
            // find a request.
            while (m_queuedRequests.Count > 0)
            {
                QueuedPublishRequest request = m_queuedRequests.First!.Value;
                m_queuedRequests.RemoveFirst();

                if (request.Tcs.Task.IsCompleted)
                {
                    request.Dispose();
                    continue;
                }

                // check secure channel.
                if (!m_session.IsSecureChannelValid(request.SecureChannelId))
                {
                    m_logger.PublishAbandonedBecauseTheSecureChannelChanged(
                        m_session.Id,
                        subscription.Subscription.Id);
                    request.Tcs.TrySetException(new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid));
                    request.Dispose();
                    continue;
                }

                subscription.Publishing = true;

                if (!request.Tcs.TrySetResult(subscription.Subscription))
                {
                    // the request was cancelled or timed out in the meantime.
                    subscription.Publishing = false;
                    request.Dispose();
                    continue;
                }

                m_logger.PUBLISHIdAssignedToSubscriptionSubscriptionId(
                    request.SecureChannelId,
                    subscription.Subscription.Id,
                    m_session.Id);

                request.Dispose();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Selects a subscription to publish based on priority and timestamp.
        /// </summary>
        private QueuedSubscription? GetSubscriptionToPublish()
        {
            var availableSubscriptions = new List<QueuedSubscription>();

            foreach (KeyValuePair<uint, QueuedSubscription> entry in m_queuedSubscriptions)
            {
                QueuedSubscription subscription = entry.Value;
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
                QueuedSubscription? subscriptionToPublish = null;

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

                subscriptionToPublish!.Publishing = true;
                return subscriptionToPublish;
            }

            return null;
        }

        /// <summary>
        /// A request queued while waiting for a subscription to be ready to publish.
        /// </summary>
        private sealed class QueuedPublishRequest : IDisposable
        {
            public QueuedPublishRequest(
                string secureChannelId,
                DateTime operationTimeout,
                TimeProvider timeProvider,
                CancellationToken cancellationToken)
            {
                SecureChannelId = secureChannelId;
                OperationTimeout = operationTimeout;
                Tcs = new TaskCompletionSource<ISubscriptionPublishPipeline>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                m_cancellationTokenRegistration = cancellationToken.Register(
                    () => Tcs.TrySetCanceled());
                // Cancel publish request if it times out
                TimeSpan timeOut = operationTimeout < DateTime.MaxValue
                    ? operationTimeout.AddMilliseconds(500) - timeProvider.GetUtcNow().UtcDateTime
                    : TimeSpan.Zero;
                if (operationTimeout < DateTime.MaxValue && timeOut.TotalMilliseconds > 0)
                {
                    m_cancellationTokenSource = timeProvider.CreateCancellationTokenSource(timeOut);
                    m_cancellationTokenRegistration2 = m_cancellationTokenSource.Token.Register(
                    () => Tcs.TrySetException(new ServiceResultException(StatusCodes.BadTimeout)));
                }
            }

            public void Dispose()
            {
                m_cancellationTokenRegistration.Dispose();
                m_cancellationTokenSource?.Dispose();
                m_cancellationTokenRegistration2.Dispose();
            }

            public readonly string SecureChannelId;
            public readonly DateTime OperationTimeout;
            public readonly TaskCompletionSource<ISubscriptionPublishPipeline> Tcs;
            private readonly CancellationTokenRegistration m_cancellationTokenRegistration;
            private readonly CancellationTokenSource? m_cancellationTokenSource;
            private readonly CancellationTokenRegistration m_cancellationTokenRegistration2;
        }

        /// <summary>
        /// Stores a subscription that belongs to this Session Publish Queue.
        /// </summary>
        internal sealed class QueuedSubscription
        {
            /// <summary>
            /// Initializes the queue entry for a subscription owned by this session.
            /// </summary>
            /// <param name="subscription">The subscription tracked by the publish queue.</param>
            public QueuedSubscription(ISubscriptionPublishPipeline subscription)
            {
                Subscription = subscription;
                ReadyToPublish = false;
                Timestamp = DateTime.UtcNow;
            }

            /// <summary>
            /// Gets the subscription associated with the queue entry.
            /// </summary>
            public ISubscriptionPublishPipeline Subscription { get; }

            /// <summary>
            /// Gets or sets the UTC timestamp used for publish scheduling and timeout decisions.
            /// </summary>
            public DateTime Timestamp { get; set; }

            /// <summary>
            /// Gets or sets whether the subscription has notifications ready for a publish response.
            /// </summary>
            public bool ReadyToPublish { get; set; }

            /// <summary>
            /// Gets or sets whether the queue entry is currently assigned to an outstanding publish request.
            /// </summary>
            public bool Publishing { get; set; }
        }

        /// <summary>
        /// Holds the exact queue entry removed while a subscription is being transferred to another session.
        /// </summary>
        internal sealed class SubscriptionTransferClaim
        {
            /// <summary>
            /// Initializes a transfer claim for the removed queue entry.
            /// </summary>
            /// <param name="entry">The queue entry held outside active publishing during transfer.</param>
            public SubscriptionTransferClaim(QueuedSubscription entry)
            {
                Entry = entry;
            }

            /// <summary>
            /// Gets the queue entry that must be restored or completed exactly once.
            /// </summary>
            public QueuedSubscription Entry { get; }
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

            int subscriptionCount;
            int requestCount;
            int readyToPublishCount = 0;
            int expiredCount = 0;

            NodeId? sessionId;
            lock (m_lock)
            {
                sessionId = m_session?.Id;
                subscriptionCount = m_queuedSubscriptions.Count;
                requestCount = m_queuedRequests.Count;

                foreach (KeyValuePair<uint, QueuedSubscription> entry in m_queuedSubscriptions)
                {
                    if (entry.Value.ReadyToPublish)
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

            m_logger.PublishQueueContextSessionIdSessionIdSubscriptionCount(
                Utils.Format(context, args),
                sessionId,
                subscriptionCount,
                requestCount,
                readyToPublishCount,
                expiredCount);
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly BackgroundTaskScope m_backgroundWork;
        private readonly IServerInternal m_server;
        private readonly ISession m_session;
        private readonly LinkedList<QueuedPublishRequest> m_queuedRequests;
        private readonly ConcurrentDictionary<uint, QueuedSubscription> m_queuedSubscriptions;
        private readonly Dictionary<uint, SubscriptionTransferClaim> m_transferClaims;
        private readonly int m_maxRequestCount;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Source-generated log messages for SessionPublishQueue.
    /// </summary>
    internal static partial class SessionPublishQueueLog
    {
        /// <summary>
        /// Logs that a publish request was abandoned because its secure channel no longer matches the queued request.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SessionPublishQueue + 0, Level = LogLevel.Warning,
            Message = "Publish abandoned because the secure channel changed. " +
                "SessionId={SessionId}, SubscriptionId={SubscriptionId}")]
        public static partial void PublishAbandonedBecauseTheSecureChannelChanged(
            this ILogger logger,
            NodeId? sessionId,
            uint subscriptionId);

        /// <summary>
        /// Logs the trace-level assignment of a queued publish request to a subscription.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SessionPublishQueue + 1, Level = LogLevel.Trace,
            Message = "PUBLISH: #{Id} Assigned To Subscription({SubscriptionId}). SessionId={SessionId}")]
        public static partial void PUBLISHIdAssignedToSubscriptionSubscriptionId(
            this ILogger logger,
            string id,
            uint subscriptionId,
            NodeId? sessionId);

        /// <summary>
        /// Logs a trace-level snapshot of the publish queue counters for diagnostics.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SessionPublishQueue + 2, Level = LogLevel.Trace,
            Message = "PublishQueue {Context}, SessionId={SessionId}, SubscriptionCount={SubscriptionCount}, " +
                "RequestCount={RequestCount}, ReadyToPublishCount={ReadyToPublishCount}, " +
                "ExpiredCount={ExpiredCount}")]
        public static partial void PublishQueueContextSessionIdSessionIdSubscriptionCount(
            this ILogger logger,
            string? context,
            NodeId? sessionId,
            int subscriptionCount,
            int requestCount,
            int readyToPublishCount,
            int expiredCount);
    }

}
