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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// The classic (V1) subscription engine implementation.
    /// Uses fire-and-forget publish requests with task continuations.
    /// </summary>
    public class ClassicSubscriptionEngine : ISubscriptionEngine
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ClassicSubscriptionEngine"/> class.
        /// </summary>
        /// <param name="context">The session context that provides
        /// access to session state and services.</param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// used for throttling and back-off delays. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        public ClassicSubscriptionEngine(
            ISubscriptionEngineContext context,
            TimeProvider? timeProvider = null)
        {
            m_context = context
                ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry
                .CreateLogger<ClassicSubscriptionEngine>();
            m_eventLogger = context.Telemetry
                .CreateLogger(ClientEventIds.LegacyCategoryName);
            m_minPublishRequestCount = kDefaultPublishRequestCount;
            m_maxPublishRequestCount = kMaxPublishRequestCountMax;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public int GoodPublishRequestCount
            => m_context.GoodPublishRequestCount;

        /// <inheritdoc/>
        public int BadPublishRequestCount => 0;

        /// <inheritdoc/>
        public int PublishWorkerCount => 0;

        /// <inheritdoc/>
        public int MinPublishRequestCount
        {
            get => m_minPublishRequestCount;
            set
            {
                if (value is >= kDefaultPublishRequestCount
                    and <= kMinPublishRequestCountMax)
                {
                    m_minPublishRequestCount = value;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(MinPublishRequestCount),
                        "Minimum publish request count must be between" +
                        $" {kDefaultPublishRequestCount} and {kMinPublishRequestCountMax}.");
                }
            }
        }

        /// <inheritdoc/>
        public int MaxPublishRequestCount
        {
            get => Math.Max(
                m_minPublishRequestCount,
                m_maxPublishRequestCount);
            set
            {
                if (value is >= kDefaultPublishRequestCount
                    and <= kMaxPublishRequestCountMax)
                {
                    m_maxPublishRequestCount = value;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(MaxPublishRequestCount),
                        "Maximum publish request count must be between " +
                        $"{kDefaultPublishRequestCount} and {kMaxPublishRequestCountMax}.");
                }
            }
        }

        /// <inheritdoc/>
        public void StartPublishing(int timeout, bool fullQueue)
        {
            int publishCount = GetDesiredPublishRequestCount(true);

            // refill pipeline. Send at least one publish request
            // if subscriptions are active.
            if (publishCount > 0 && BeginPublish(timeout))
            {
                int startCount = fullQueue
                    ? 1
                    : GoodPublishRequestCount + 1;
                for (int ii = startCount; ii < publishCount; ii++)
                {
                    if (!BeginPublish(timeout))
                    {
                        break;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask StopPublishingAsync(
            CancellationToken ct = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public void PausePublishing()
        {
            // The classic engine relies on the reconnect lock in
            // OnPublishComplete to gate processing during reconnect.
        }

        /// <inheritdoc/>
        public void ResumePublishing()
        {
            // The classic engine relies on the reconnect lock in
            // OnPublishComplete to gate processing during reconnect.
        }

        /// <inheritdoc/>
        public void NotifySubscriptionsChanged()
        {
            QueueBeginPublish();
        }

        /// <inheritdoc/>
        public ValueTask RecreateSubscriptionsAsync(
            NodeId? previousSessionId,
            CancellationToken ct = default)
        {
            // The classic engine does not own subscription state; the
            // host Session drives recreate via subscription templates
            // through Session.RecreateSubscriptionsAsync. No-op here.
            return default;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the engine and
        /// optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and
        /// unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            m_disposed = true;
        }

        /// <summary>
        /// Sends a publish request to the server.
        /// </summary>
        /// <param name="timeout">The timeout for publish requests
        /// in milliseconds.</param>
        /// <returns>True if the request was sent successfully.</returns>
        internal bool BeginPublish(int timeout)
        {
            // do not publish if reconnecting or the session is in closed state.
            if (!m_context.Connected)
            {
                m_logger.PublishSkippedSessionNotConnected();
                return false;
            }

            if (m_context.Reconnecting)
            {
                m_logger.PublishSkippedSessionReconnect();
                return false;
            }

            if (m_context.Closing)
            {
                m_logger.PublishCancelledSessionClosed();
                return false;
            }

            if (m_context.KeepAliveStopped)
            {
                m_logger.PublishSkippedSessionLostConnectionLast(m_context.LastKeepAliveTime);
                return false;
            }

            // collect the current set of acknowledgements.
            List<SubscriptionAcknowledgement>? acknowledgementsToSend = null;
            lock (m_acknowledgementsToSendLock)
            {
                (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending) =
                    m_context.PrepareAcknowledgementsToSend(m_acknowledgementsToSend);
                acknowledgementsToSend = toSend;
                m_acknowledgementsToSend = updatedPending;

#if DEBUG_SEQUENTIALPUBLISHING
                foreach (var toSend in acknowledgementsToSend)
                {
                    m_latestAcknowledgementsSent[toSend.SubscriptionId]
                        = toSend.SequenceNumber;
                }
#endif
            }

            uint timeoutHint = timeout > 0
                ? (uint)timeout
                : uint.MaxValue;
            timeoutHint = Math.Min(
                (uint)(m_context.OperationTimeout / 2),
                timeoutHint);

            // send publish request.
            var requestHeader = new RequestHeader
            {
                // ensure the publish request is discarded before the
                // timeout occurs to ensure the channel is dropped.
                TimeoutHint = timeoutHint,
                ReturnDiagnostics =
                    (uint)(int)m_context.ReturnDiagnostics,
                RequestHandle =
                    Utils.IncrementIdentifier(ref PublishCounter)
            };

            m_eventLogger.ClientEventPublishStart((int)requestHeader.RequestHandle);

            try
            {
                Activity? activity = m_context.Telemetry
                    .StartActivity();
                Task<PublishResponse> task = m_context.PublishAsync(
                    requestHeader,
                    acknowledgementsToSend,
                    default).AsTask();
                m_context.AsyncRequestStarted(
                    task,
                    activity,
                    requestHeader.RequestHandle,
                    DataTypes.PublishRequest);
                task.ConfigureAwait(false)
                    .GetAwaiter()
                    .OnCompleted(() => OnPublishComplete(
                        task,
                        m_context.SessionId,
                        acknowledgementsToSend,
                        requestHeader));
                return true;
            }
            catch (Exception e)
            {
                m_logger.UnexpectedErrorSendingPublishRequest(e);
                return false;
            }
        }

        /// <summary>
        /// Completes an asynchronous publish operation.
        /// </summary>
        private void OnPublishComplete(
            Task<PublishResponse> task,
            NodeId sessionId,
            List<SubscriptionAcknowledgement>?
                acknowledgementsToSend,
            RequestHeader requestHeader)
        {
            // extract state information.
            uint subscriptionId = 0;

            m_context.AsyncRequestCompleted(
                task,
                requestHeader.RequestHandle,
                DataTypes.PublishRequest);

            m_eventLogger.ClientEventPublishStop((int)requestHeader.RequestHandle);

            // Bail out early if the session has been disposed.
            if (m_context.Disposed)
            {
                return;
            }

            try
            {
                // gate entry if transfer/reactivate is busy
                try
                {
                    m_context.ReconnectLock.Wait();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                bool reconnecting = m_context.Reconnecting;

                try
                {
                    m_context.ReconnectLock.Release();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                // complete publish.
                PublishResponse response = task.Result;
                ResponseHeader responseHeader =
                    response.ResponseHeader;
                subscriptionId = response.SubscriptionId;
                ArrayOf<uint> availableSequenceNumbers =
                    response.AvailableSequenceNumbers;
                bool moreNotifications =
                    response.MoreNotifications;
                NotificationMessage notificationMessage =
                    response.NotificationMessage;
                ArrayOf<StatusCode> acknowledgeResults =
                    response.Results;

                LogLevel logLevel = LogLevel.Warning;
                foreach (StatusCode code in acknowledgeResults)
                {
                    if (StatusCode.IsBad(code) &&
                        code != StatusCodes.BadSequenceNumberUnknown)
                    {
                        if (m_logger.IsEnabled(logLevel))
                        {
                            m_logger.Log(
                                logLevel,
                                "Publish Ack Response. ResultCode={StatusCode}; " +
                                "SubscriptionId={SubscriptionId}",
                                code,
                                subscriptionId);
                        }
                        // only show the first error as warning
                        logLevel = LogLevel.Trace;
                    }
                }

                // nothing more to do if we were never connected
                if (sessionId.IsNull)
                {
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != m_context.SessionId)
                {
                    m_logger.PublishResponseDiscardedSessionIdChanged(
                        sessionId,
                        m_context.SessionId);
                    return;
                }

                m_eventLogger.ClientEventNotificationReceived(
                    (int)subscriptionId,
                    (int)notificationMessage.SequenceNumber);

                // process response.
                ProcessPublishResponse(
                    responseHeader,
                    subscriptionId,
                    availableSequenceNumbers,
                    moreNotifications,
                    notificationMessage);

                // nothing more to do if reconnecting.
                if (reconnecting)
                {
                    m_logger.NoNewPublishSentReconnectProgress();
                    return;
                }
            }
            catch (Exception e)
            {
                IReadOnlyList<Subscription> subscriptions =
                    m_context.Subscriptions;

                if (subscriptions.Count == 0)
                {
                    m_logger.PublishRequestHandleSubscriptionCountErrorMessage(
                        requestHeader.RequestHandle,
                        e.Message);
                }
                else
                {
                    m_logger.PublishRequestHandleReconnectingReconnectingErrorMessage(
                        requestHeader.RequestHandle,
                        m_context.Reconnecting,
                        e.Message);
                }

                // raise an error event.
                var error = new ServiceResult(e);

                // raise publish error even for BadNoSubscription
                // if there are active subscriptions.
                if (error.Code != StatusCodes.BadNoSubscription ||
                    subscriptions.Any(s => s.Created))
                {
                    m_context.OnPublishError(
                        error, subscriptionId, 0);
                }

                // ignore errors if reconnecting
                if (m_context.Reconnecting)
                {
                    m_logger.PublishAbandonedAfterErrorMessageSession(
                        e.Message,
                        sessionId);
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != m_context.SessionId)
                {
                    if (m_context.Connected)
                    {
                        m_logger.PublishAbandonedAfterErrorMessageSession2(
                            e.Message,
                            sessionId,
                            m_context.SessionId);
                    }
                    else
                    {
                        m_logger.PublishAbandonedAfterErrorMessageSession3(
                            e.Message,
                            sessionId);
                    }
                    return;
                }

                // try to acknowledge the notifications again in
                // the next publish.
                if (acknowledgementsToSend != null)
                {
                    lock (m_acknowledgementsToSendLock)
                    {
                        m_acknowledgementsToSend.AddRange(
                            acknowledgementsToSend);
                    }
                }

                // don't send another publish for these errors,
                // or throttle to avoid server overload.
                if (error.StatusCode ==
                    StatusCodes.BadTooManyPublishRequests)
                {
                    int tooManyPublishRequests =
                        GoodPublishRequestCount;
                    if (BelowPublishRequestLimit(
                            tooManyPublishRequests))
                    {
                        m_tooManyPublishRequests =
                            tooManyPublishRequests;
                        m_logger.PUBLISHTooManyRequestsSetLimit(m_tooManyPublishRequests);
                    }
                    return;
                }

                if (error.StatusCode ==
                        StatusCodes.BadNoSubscription ||
                    error.StatusCode ==
                        StatusCodes.BadSessionClosed ||
                    error.StatusCode ==
                        StatusCodes.BadSecurityChecksFailed ||
                    error.StatusCode ==
                        StatusCodes.BadCertificateInvalid ||
                    error.StatusCode ==
                        StatusCodes.BadServerHalted)
                {
                    return;
                }

                if (error.StatusCode ==
                        StatusCodes.BadSessionIdInvalid ||
                    error.StatusCode ==
                        StatusCodes.BadSecureChannelIdInvalid ||
                    error.StatusCode ==
                        StatusCodes.BadSecureChannelClosed)
                {
                    m_context.OnKeepAliveError(error);
                    return;
                }

                // Servers may return this error when overloaded
                if (error.StatusCode !=
                        StatusCodes.BadTimeout &&
                    error.StatusCode !=
                        StatusCodes.BadRequestTimeout)
                {
                    if (error.StatusCode !=
                            StatusCodes.BadTooManyOperations &&
                        error.StatusCode !=
                            StatusCodes.BadTcpServerTooBusy &&
                        error.StatusCode !=
                            StatusCodes.BadServerTooBusy)
                    {
                        m_logger.PUBLISHRequestHandleUnhandledErrorStatusCodeDuring(
                            e,
                            requestHeader.RequestHandle,
                            error.StatusCode);
                    }

                    // throttle the next publish to reduce
                    // server load
                    _ = Task.Run(async () =>
                    {
                        await m_timeProvider.Delay(TimeSpan.FromMilliseconds(100))
                            .ConfigureAwait(false);
                        QueueBeginPublish();
                    });
                    return;
                }
            }

            QueueBeginPublish();
        }

        /// <summary>
        /// Processes the response from a publish request.
        /// </summary>
        internal void ProcessPublishResponse(
            ResponseHeader responseHeader,
            uint subscriptionId,
            ArrayOf<uint> availableSequenceNumbers,
            bool moreNotifications,
            NotificationMessage notificationMessage)
        {
            Subscription? subscription = null;
            var availableSequenceNumberList = availableSequenceNumbers.ToList();

            // send notification that the server is alive.
            m_context.OnKeepAlive(
                m_context.ServerState,
                (DateTime)responseHeader.Timestamp);

            // collect the current set of acknowledgements.
            lock (m_acknowledgementsToSendLock)
            {
                // clear out acknowledgements for messages that the
                // server does not have any more.
                var acknowledgementsToSend =
                    new List<SubscriptionAcknowledgement>();

                uint latestSequenceNumberToSend = 0;

                // create an acknowledgement to be sent back to the
                // server.
                if (notificationMessage.NotificationData.Count > 0)
                {
                    AddAcknowledgementToSend(
                        acknowledgementsToSend,
                        subscriptionId,
                        notificationMessage.SequenceNumber);
                    UpdateLatestSequenceNumberToSend(
                        ref latestSequenceNumberToSend,
                        notificationMessage.SequenceNumber);

                    availableSequenceNumberList.Remove(notificationMessage.SequenceNumber);
                }

                // match an acknowledgement to be sent back to the
                // server.
                for (int ii = 0; ii < m_acknowledgementsToSend.Count; ii++)
                {
                    SubscriptionAcknowledgement acknowledgement = m_acknowledgementsToSend[ii];

                    if (acknowledgement.SubscriptionId != subscriptionId)
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                    }
                    else if (availableSequenceNumberList.Remove(acknowledgement.SequenceNumber))
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                        UpdateLatestSequenceNumberToSend(
                            ref latestSequenceNumberToSend,
                            acknowledgement.SequenceNumber);
                    }
                    // a publish response may be processed out of
                    // order, allow for a tolerance until the
                    // sequence number is removed.
                    else if (Math.Abs((int)(acknowledgement.SequenceNumber - latestSequenceNumberToSend)) <
                        kPublishRequestSequenceNumberOutOfOrderThreshold)
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                    }
                    else
                    {
                        m_logger.SessionIdSessionIdSubscriptionIdSubscriptionIdSequenceNumber(
                            m_context.SessionId,
                            subscriptionId,
                            acknowledgement.SequenceNumber);
                    }
                }

                // Check for outdated sequence numbers. May have
                // been not acked due to a network glitch.
                if (latestSequenceNumberToSend != 0 && availableSequenceNumberList.Count > 0)
                {
                    foreach (uint sequenceNumber in
                        availableSequenceNumberList)
                    {
                        if ((int)(latestSequenceNumberToSend - sequenceNumber) >
                            kPublishRequestSequenceNumberOutdatedThreshold)
                        {
                            AddAcknowledgementToSend(
                                acknowledgementsToSend,
                                subscriptionId,
                                sequenceNumber);
                            m_logger.SessionIdSessionIdSubscriptionIdSubscriptionIdSequenceNumber2(
                                m_context.SessionId,
                                subscriptionId,
                                sequenceNumber);
                        }
                    }
                }

                m_acknowledgementsToSend = acknowledgementsToSend;

                if (notificationMessage.IsEmpty && m_logger.IsEnabled(LogLevel.Trace))
                {
                    m_logger.EmptyNotificationMessageReceivedSessionIdSessionId(
                        m_context.SessionId,
                        notificationMessage.PublishTime.ToDateTime().ToLocalTime());
                }
            }

            bool subscriptionCreationInProgress = false;

            // find the subscription.
            foreach (Subscription current in m_context.Subscriptions)
            {
                if (current.Id == subscriptionId)
                {
                    subscription = current;
                    break;
                }
                if (current.Id == default)
                {
                    // Subscription is being created, disable
                    // cleanup mechanism
                    subscriptionCreationInProgress = true;
                }
            }

            // ignore messages with a subscription that has been
            // deleted.
            if (subscription != null)
            {
#if DEBUG
                // Validate publish time and reject old values.
                if (notificationMessage.PublishTime.AddMilliseconds(
                    subscription.CurrentPublishingInterval * subscription.CurrentLifetimeCount) <
                    DateTimeUtc.Now)
                {
                    m_logger.PublishTimePublishTimePublishResponseTooOld(
                        notificationMessage.PublishTime.ToLocalTime(),
                        subscription.Id);
                }

                // Validate publish time and reject future values.
                if (notificationMessage.PublishTime >
                    DateTimeUtc.Now.AddMilliseconds(
                        subscription.CurrentPublishingInterval * subscription.CurrentLifetimeCount))
                {
                    m_logger.PublishTimePublishTimePublishResponseNewerThan(
                        notificationMessage.PublishTime.ToLocalTime(),
                        subscription.Id);
                }
#endif
                // save the information that more notifications
                // are expected
                notificationMessage.MoreNotifications = moreNotifications;

                // save the string table that came with the
                // notification.
                notificationMessage.StringTable = responseHeader.StringTable;

                // update subscription cache.
                subscription.SaveMessageInCache(availableSequenceNumberList, notificationMessage);

                // raise the notification.
                var args = new NotificationEventArgs(
                    subscription,
                    notificationMessage,
                    responseHeader.StringTable);

                m_context.OnPublishNotification(subscription, args);
            }
            else if (m_context.DeleteSubscriptionsOnClose &&
                !m_context.Reconnecting &&
                !subscriptionCreationInProgress)
            {
                // Delete abandoned subscription from server.
                m_logger.ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId(subscriptionId);

                _ = Task.Run(
                    () => m_context.DeleteOrphanedSubscriptionAsync(subscriptionId));
            }
            else
            {
                // Do not delete publish requests of stale
                // subscriptions
                m_logger.ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId2(subscriptionId);
            }
        }

        /// <summary>
        /// Queues a publish request if there are not enough
        /// outstanding requests.
        /// </summary>
        private void QueueBeginPublish()
        {
            if (m_context.Disposed || m_disposed)
            {
                return;
            }

            int requestCount = GoodPublishRequestCount;
            int minPublishRequestCount = GetDesiredPublishRequestCount(false);

            if (requestCount < minPublishRequestCount)
            {
                BeginPublish(m_context.OperationTimeout);
            }
            else
            {
                m_logger.PUBLISHDidNotSendAnotherPublish(
                    requestCount,
                    minPublishRequestCount);
            }
        }

        /// <summary>
        /// Returns the desired number of active publish requests
        /// that should be used.
        /// </summary>
        /// <remarks>
        /// Returns 0 if there are no subscriptions.
        /// </remarks>
        /// <param name="createdOnly">False if called when re-queuing.</param>
        /// <returns>The number of desired publish requests for the session.</returns>
        protected virtual int GetDesiredPublishRequestCount(bool createdOnly)
        {
            IReadOnlyList<Subscription> subscriptions = m_context.Subscriptions;

            if (subscriptions.Count == 0)
            {
                return 0;
            }

            int publishCount;

            if (createdOnly)
            {
                int count = 0;
                foreach (Subscription subscription in subscriptions)
                {
                    if (subscription.Created)
                    {
                        count++;
                    }
                }

                if (count == 0)
                {
                    return 0;
                }
                publishCount = count;
            }
            else
            {
                publishCount = subscriptions.Count;
            }

            // If a dynamic limit was set because of
            // BadTooManyPublishRequest error, limit the number
            // of publish requests to this value.
            if (m_tooManyPublishRequests > 0 &&
                publishCount > m_tooManyPublishRequests)
            {
                publishCount = m_tooManyPublishRequests;
            }

            // Limit resulting to a number between min and max
            // request count. If max is below min, we honor the
            // min publish request count.
            if (publishCount > m_maxPublishRequestCount)
            {
                publishCount = m_maxPublishRequestCount;
            }
            if (publishCount < m_minPublishRequestCount)
            {
                publishCount = m_minPublishRequestCount;
            }
            return publishCount;
        }

        /// <summary>
        /// Adds an acknowledgement to the list to send.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="acknowledgementsToSend"/> is <c>null</c>.</exception>
        private void AddAcknowledgementToSend(
            List<SubscriptionAcknowledgement> acknowledgementsToSend,
            uint subscriptionId,
            uint sequenceNumber)
        {
            if (acknowledgementsToSend == null)
            {
                throw new ArgumentNullException(nameof(acknowledgementsToSend));
            }

            Debug.Assert(Monitor.IsEntered(m_acknowledgementsToSendLock));

            acknowledgementsToSend.Add(new SubscriptionAcknowledgement
            {
                SubscriptionId = subscriptionId,
                SequenceNumber = sequenceNumber
            });
        }

        /// <summary>
        /// Adds an acknowledgement to the pending list. Called
        /// by Session when re-creating subscriptions after
        /// transfer.
        /// </summary>
        /// <param name="subscriptionId">The subscription ID to
        /// acknowledge.</param>
        /// <param name="sequenceNumber">The sequence number to
        /// acknowledge.</param>
        internal void AddPendingAcknowledgement(
            uint subscriptionId,
            uint sequenceNumber)
        {
            lock (m_acknowledgementsToSendLock)
            {
                AddAcknowledgementToSend(
                    m_acknowledgementsToSend,
                    subscriptionId,
                    sequenceNumber);
            }
        }

        /// <summary>
        /// Drops every queued acknowledgement targeting the given
        /// <paramref name="subscriptionId"/>. Used by the
        /// recovery-on-unsolicited-transfer path to prevent
        /// <c>BadSubscriptionIdInvalid</c> ack responses from
        /// reaching the server after the local subscription has
        /// been invalidated and is about to be recreated. Servers
        /// that re-use subscription identifiers across generations
        /// (e.g. Kepware always starting at <c>1</c>) require this
        /// to run while the old id is still "uniquely dead" — i.e.
        /// before a recreate assigns a new id.
        /// </summary>
        /// <param name="subscriptionId">The subscription id whose
        /// queued acknowledgements should be dropped.</param>
        /// <returns>The number of queued acknowledgements that
        /// were dropped.</returns>
        internal int RemoveAcknowledgementsForSubscription(uint subscriptionId)
        {
            lock (m_acknowledgementsToSendLock)
            {
                int before = m_acknowledgementsToSend.Count;
                if (before == 0)
                {
                    return 0;
                }
                m_acknowledgementsToSend.RemoveAll(
                    ack => ack.SubscriptionId == subscriptionId);
                return before - m_acknowledgementsToSend.Count;
            }
        }

        /// <summary>
        /// Helper to update the latest sequence number to send.
        /// Handles wrap around of sequence numbers.
        /// </summary>
        private static void UpdateLatestSequenceNumberToSend(
            ref uint latestSequenceNumberToSend,
            uint sequenceNumber)
        {
            // Handle wrap around with subtraction and test
            // result is int. Assume sequence numbers to ack
            // do not differ by more than uint.Max / 2
            if (latestSequenceNumberToSend == 0 ||
                ((int)(sequenceNumber - latestSequenceNumberToSend)) > 0)
            {
                latestSequenceNumberToSend = sequenceNumber;
            }
        }

        /// <summary>
        /// Returns true if the Bad_TooManyPublishRequests limit
        /// has not been reached.
        /// </summary>
        /// <param name="requestCount">The actual number of
        /// publish requests.</param>
        /// <returns>If the publish request limit was reached.</returns>
        private bool BelowPublishRequestLimit(int requestCount)
        {
            return (m_tooManyPublishRequests == 0) ||
                (requestCount < m_tooManyPublishRequests);
        }

        /// <summary>
        /// Processes an error from a republish response.
        /// </summary>
        /// <param name="e">The exception.</param>
        /// <param name="subscriptionId">The subscription identifier.</param>
        /// <param name="sequenceNumber">The sequence number.</param>
        /// <returns>A tuple indicating whether the error was
        /// handled and the service result.</returns>
        internal (bool handled, ServiceResult error) ProcessRepublishResponseError(
            Exception e,
            uint subscriptionId,
            uint sequenceNumber)
        {
            var error = new ServiceResult(e);

            bool result = true;
            if (error.StatusCode == StatusCodes.BadSubscriptionIdInvalid ||
                error.StatusCode == StatusCodes.BadMessageNotAvailable)
            {
                m_logger.MessageSubscriptionIdSequenceNumberNoLongerAvailable(
                    subscriptionId,
                    sequenceNumber);
            }
            else if (error.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
            {
                m_logger.MessageSubscriptionIdSequenceNumberExceededSizeLimits(
                    e,
                    subscriptionId,
                    sequenceNumber);
                lock (m_acknowledgementsToSendLock)
                {
                    AddAcknowledgementToSend(
                        m_acknowledgementsToSend,
                        subscriptionId,
                        sequenceNumber);
                }
            }
            else
            {
                result = false;
                m_logger.UnexpectedErrorSendingRepublishRequest(e);
            }

            // raise an error event.
            m_context.OnPublishError(
                error, subscriptionId, sequenceNumber);

            return (result, error);
        }

        private const int kMinPublishRequestCountMax = 100;
        private const int kMaxPublishRequestCountMax = ushort.MaxValue;
        private const int kDefaultPublishRequestCount = 1;
        private const int kPublishRequestSequenceNumberOutOfOrderThreshold = 10;
        private const int kPublishRequestSequenceNumberOutdatedThreshold = 100;
        private readonly ISubscriptionEngineContext m_context;
        private readonly ILogger m_logger;
        private readonly ILogger m_eventLogger;
        private readonly TimeProvider m_timeProvider;
        private readonly object m_acknowledgementsToSendLock = new();
        private List<SubscriptionAcknowledgement> m_acknowledgementsToSend = [];
        internal uint PublishCounter;
        private int m_tooManyPublishRequests;
        private int m_minPublishRequestCount;
        private int m_maxPublishRequestCount;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ClassicSubscriptionEngine"/>.
    /// </summary>
    internal static partial class ClassicSubscriptionEngineLog
    {
        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 0, Level = LogLevel.Warning,
            Message = "Publish skipped due to session not connected")]
        public static partial void PublishSkippedSessionNotConnected(this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 1, Level = LogLevel.Warning,
            Message = "Publish skipped due to session reconnect")]
        public static partial void PublishSkippedSessionReconnect(this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 2, Level = LogLevel.Warning,
            Message = "Publish cancelled due to session closed")]
        public static partial void PublishCancelledSessionClosed(this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 3, Level = LogLevel.Warning,
            Message = "Publish skipped due to session lost connection. Last successful keepalive: {LastKeepAlive}")]
        public static partial void PublishSkippedSessionLostConnectionLast(this ILogger logger, DateTime lastKeepAlive);

        [LoggerMessage(
            EventId = ClientEventIds.LegacyPublishStartId,
            EventName = "PublishStart",
            Level = LogLevel.Trace,
            Message = "PUBLISH #{RequestHandle} SENT")]
        public static partial void ClientEventPublishStart(this ILogger logger, int requestHandle);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 5, Level = LogLevel.Error,
            Message = "Unexpected error sending publish request.")]
        public static partial void UnexpectedErrorSendingPublishRequest(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = ClientEventIds.LegacyPublishStopId,
            EventName = "PublishStop",
            Level = LogLevel.Trace,
            Message = "PUBLISH #{RequestHandle} RECEIVED")]
        public static partial void ClientEventPublishStop(this ILogger logger, int requestHandle);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 7, Level = LogLevel.Warning,
            Message = "Publish response discarded because session id changed: Old {PreviousSessionId} != New" +
                " {SessionId}")]
        public static partial void PublishResponseDiscardedSessionIdChanged(
            this ILogger logger,
            NodeId? previousSessionId,
            NodeId? sessionId);

        [LoggerMessage(
            EventId = ClientEventIds.LegacyNotificationReceivedId,
            EventName = "NotificationReceived",
            Level = LogLevel.Trace,
            Message = "NOTIFICATION RECEIVED: SubId={SubscriptionId}, SeqNo={SequenceNumber}")]
        public static partial void ClientEventNotificationReceived(
            this ILogger logger,
            int subscriptionId,
            int sequenceNumber);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 9, Level = LogLevel.Warning,
            Message = "No new publish sent because of reconnect in progress.")]
        public static partial void NoNewPublishSentReconnectProgress(this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 10, Level = LogLevel.Warning,
            Message = "Publish #{RequestHandle}, Subscription count = 0, Error: {Message}")]
        public static partial void PublishRequestHandleSubscriptionCountErrorMessage(
            this ILogger logger,
            uint requestHandle,
            string message);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 11, Level = LogLevel.Error,
            Message = "Publish #{RequestHandle}, Reconnecting={Reconnecting}, Error: {Message}")]
        public static partial void PublishRequestHandleReconnectingReconnectingErrorMessage(
            this ILogger logger,
            uint requestHandle,
            bool reconnecting,
            string message);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 12, Level = LogLevel.Information,
            Message = "Publish abandoned after error {Message} due to session {SessionId} reconnecting")]
        public static partial void PublishAbandonedAfterErrorMessageSession(
            this ILogger logger,
            string message,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 13, Level = LogLevel.Error,
            Message = "Publish abandoned after error {Message} because session id changed: " +
                "Old {PreviousSessionId} != New {SessionId}")]
        public static partial void PublishAbandonedAfterErrorMessageSession2(
            this ILogger logger,
            string message,
            NodeId? previousSessionId,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 14, Level = LogLevel.Information,
            Message = "Publish abandoned after error {Message} because session {SessionId} was closed.")]
        public static partial void PublishAbandonedAfterErrorMessageSession3(
            this ILogger logger,
            string message,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 15, Level = LogLevel.Information,
            Message = "PUBLISH - Too many requests, set limit to GoodPublishRequestCount={GoodRequestCount}.")]
        public static partial void PUBLISHTooManyRequestsSetLimit(this ILogger logger, int goodRequestCount);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 16, Level = LogLevel.Error,
            Message = "PUBLISH #{RequestHandle} - Unhandled error {StatusCode} during Publish.")]
        public static partial void PUBLISHRequestHandleUnhandledErrorStatusCodeDuring(
            this ILogger logger,
            Exception? exception,
            uint requestHandle,
            StatusCode statusCode);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 17, Level = LogLevel.Warning,
            Message = "SessionId {SessionId}, SubscriptionId {SubscriptionId}, Sequence number={SequenceNumber}" +
                " was not received in the available sequence numbers.")]
        public static partial void SessionIdSessionIdSubscriptionIdSubscriptionIdSequenceNumber(
            this ILogger logger,
            NodeId? sessionId,
            uint subscriptionId,
            uint sequenceNumber);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 18, Level = LogLevel.Warning,
            Message = "SessionId {SessionId}, SubscriptionId {SubscriptionId}, Sequence number={SequenceNumber}" +
                " was outdated, acknowledged.")]
        public static partial void SessionIdSessionIdSubscriptionIdSubscriptionIdSequenceNumber2(
            this ILogger logger,
            NodeId? sessionId,
            uint subscriptionId,
            uint sequenceNumber);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 19, Level = LogLevel.Trace,
            Message = "Empty notification message received for SessionId {SessionId} with PublishTime {PublishTime}")]
        public static partial void EmptyNotificationMessageReceivedSessionIdSessionId(
            this ILogger logger,
            NodeId? sessionId,
            DateTime publishTime);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 20, Level = LogLevel.Trace,
            Message = "PublishTime {PublishTime} in publish response is too old for SubscriptionId" +
                " {SubscriptionId}.")]
        public static partial void PublishTimePublishTimePublishResponseTooOld(
            this ILogger logger,
            DateTime publishTime,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 21, Level = LogLevel.Trace,
            Message = "PublishTime {PublishTime} in publish response is newer than actual time for SubscriptionId" +
                " {SubscriptionId}.")]
        public static partial void PublishTimePublishTimePublishResponseNewerThan(
            this ILogger logger,
            DateTime publishTime,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 22, Level = LogLevel.Warning,
            Message = "Received Publish Response for Unknown SubscriptionId={SubscriptionId}. Deleting abandoned" +
                " subscription from server.")]
        public static partial void ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId(
            this ILogger logger,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 23, Level = LogLevel.Warning,
            Message = "Received Publish Response for Unknown SubscriptionId={SubscriptionId}. Ignored.")]
        public static partial void ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId2(
            this ILogger logger,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 24, Level = LogLevel.Debug,
            Message = "PUBLISH - Did not send another publish request. " +
                "GoodPublishRequestCount={GoodRequestCount}, MinPublishRequestCount={MinRequestCount}")]
        public static partial void PUBLISHDidNotSendAnotherPublish(
            this ILogger logger,
            int goodRequestCount,
            int minRequestCount);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 25, Level = LogLevel.Warning,
            Message = "Message {SubscriptionId}-{SequenceNumber} no longer available.")]
        public static partial void MessageSubscriptionIdSequenceNumberNoLongerAvailable(
            this ILogger logger,
            uint subscriptionId,
            uint sequenceNumber);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 26, Level = LogLevel.Error,
            Message = "Message {SubscriptionId}-{SequenceNumber} exceeded size limits, ignored.")]
        public static partial void MessageSubscriptionIdSequenceNumberExceededSizeLimits(
            this ILogger logger,
            Exception? exception,
            uint subscriptionId,
            uint sequenceNumber);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 27, Level = LogLevel.Error,
            Message = "Unexpected error sending republish request.")]
        public static partial void UnexpectedErrorSendingRepublishRequest(this ILogger logger, Exception? exception);
    }

}
