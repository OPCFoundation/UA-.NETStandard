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
        #region Constants
        private const int kMinPublishRequestCountMax = 100;
        private const int kMaxPublishRequestCountMax = ushort.MaxValue;
        private const int kDefaultPublishRequestCount = 1;
        private const int kPublishRequestSequenceNumberOutOfOrderThreshold = 10;
        private const int kPublishRequestSequenceNumberOutdatedThreshold = 100;
        #endregion

        #region Fields
        private readonly ISubscriptionEngineContext m_context;
        private readonly ILogger m_logger;
        private readonly object m_acknowledgementsToSendLock = new();
        private List<SubscriptionAcknowledgement> m_acknowledgementsToSend = [];
#if DEBUG_SEQUENTIALPUBLISHING
        private Dictionary<uint, uint> m_latestAcknowledgementsSent = [];
#endif
        internal uint m_publishCounter;
        private int m_tooManyPublishRequests;
        private int m_minPublishRequestCount;
        private int m_maxPublishRequestCount;
        private bool m_disposed;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ClassicSubscriptionEngine"/> class.
        /// </summary>
        /// <param name="context">The session context that provides
        /// access to session state and services.</param>
        public ClassicSubscriptionEngine(ISubscriptionEngineContext context)
        {
            m_context = context
                ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry
                .CreateLogger<ClassicSubscriptionEngine>();
            m_minPublishRequestCount = kDefaultPublishRequestCount;
            m_maxPublishRequestCount = kMaxPublishRequestCountMax;
        }
        #endregion

        #region ISubscriptionEngine
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
                        $"Minimum publish request count must be between " +
                        $"{kDefaultPublishRequestCount} and " +
                        $"{kMinPublishRequestCountMax}.");
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
                        $"Maximum publish request count must be between " +
                        $"{kDefaultPublishRequestCount} and " +
                        $"{kMaxPublishRequestCountMax}.");
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
        #endregion

        #region IDisposable
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
        #endregion

        #region Internal Methods
        /// <summary>
        /// Sends a publish request to the server.
        /// </summary>
        /// <param name="timeout">The timeout for publish requests
        /// in milliseconds.</param>
        /// <returns>True if the request was sent successfully.</returns>
        internal bool BeginPublish(int timeout)
        {
            // do not publish if reconnecting or the session is
            // in closed state.
            if (!m_context.Connected)
            {
                m_logger.LogWarning(
                    "Publish skipped due to session not connected");
                return false;
            }

            if (m_context.Reconnecting)
            {
                m_logger.LogWarning(
                    "Publish skipped due to session reconnect");
                return false;
            }

            if (m_context.Closing)
            {
                m_logger.LogWarning(
                    "Publish cancelled due to session closed");
                return false;
            }

            // collect the current set of acknowledgements.
            List<SubscriptionAcknowledgement>? acknowledgementsToSend
                = null;
            lock (m_acknowledgementsToSendLock)
            {
                var result = m_context
                    .PrepareAcknowledgementsToSend(
                        m_acknowledgementsToSend);
                acknowledgementsToSend = result.toSend;
                m_acknowledgementsToSend = result.updatedPending;

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
                    Utils.IncrementIdentifier(ref m_publishCounter)
            };

            m_logger.LogTrace(
                "PUBLISH #{RequestHandle} SENT",
                requestHeader.RequestHandle);
            CoreClientUtils.EventLog.PublishStart(
                (int)requestHeader.RequestHandle);

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
                m_logger.LogError(
                    e,
                    "Unexpected error sending publish request.");
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

            m_logger.LogTrace(
                "PUBLISH #{RequestHandle} RECEIVED",
                requestHeader.RequestHandle);
            CoreClientUtils.EventLog.PublishStop(
                (int)requestHeader.RequestHandle);

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
                        m_logger.Log(
                            logLevel,
                            "Publish Ack Response. ResultCode={StatusCode}; " +
                            "SubscriptionId={SubscriptionId}",
                            code,
                            subscriptionId);
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
                    m_logger.LogWarning(
                        "Publish response discarded because session " +
                        "id changed: Old {PreviousSessionId} != " +
                        "New {SessionId}",
                        sessionId,
                        m_context.SessionId);
                    return;
                }

                m_logger.LogTrace(
                    "NOTIFICATION RECEIVED: SubId={SubscriptionId}" +
                    ", SeqNo={SequenceNumber}",
                    subscriptionId,
                    notificationMessage.SequenceNumber);
                CoreClientUtils.EventLog.NotificationReceived(
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
                    m_logger.LogWarning(
                        "No new publish sent because of " +
                        "reconnect in progress.");
                    return;
                }
            }
            catch (Exception e)
            {
                IReadOnlyList<Subscription> subscriptions =
                    m_context.Subscriptions;

                if (subscriptions.Count == 0)
                {
                    m_logger.LogWarning(
                        "Publish #{RequestHandle}, " +
                        "Subscription count = 0, Error: {Message}",
                        requestHeader.RequestHandle,
                        e.Message);
                }
                else
                {
                    m_logger.LogError(
                        "Publish #{RequestHandle}, " +
                        "Reconnecting={Reconnecting}, " +
                        "Error: {Message}",
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
                    m_logger.LogInformation(
                        "Publish abandoned after error {Message} " +
                        "due to session {SessionId} reconnecting",
                        e.Message,
                        sessionId);
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != m_context.SessionId)
                {
                    if (m_context.Connected)
                    {
                        m_logger.LogError(
                            "Publish abandoned after error " +
                            "{Message} because session id changed:" +
                            " Old {PreviousSessionId} != " +
                            "New {SessionId}",
                            e.Message,
                            sessionId,
                            m_context.SessionId);
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "Publish abandoned after error " +
                            "{Message} because session {SessionId}" +
                            " was closed.",
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
                        m_logger.LogInformation(
                            "PUBLISH - Too many requests, " +
                            "set limit to " +
                            "GoodPublishRequestCount=" +
                            "{GoodRequestCount}.",
                            m_tooManyPublishRequests);
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
                        m_logger.LogError(
                            e,
                            "PUBLISH #{RequestHandle} - " +
                            "Unhandled error {StatusCode} " +
                            "during Publish.",
                            requestHeader.RequestHandle,
                            error.StatusCode);
                    }

                    // throttle the next publish to reduce
                    // server load
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100)
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
            var availableSequenceNumberList =
                availableSequenceNumbers.ToList();

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

                    availableSequenceNumberList.Remove(
                        notificationMessage.SequenceNumber);
                }

                // match an acknowledgement to be sent back to the
                // server.
                for (int ii = 0;
                    ii < m_acknowledgementsToSend.Count;
                    ii++)
                {
                    SubscriptionAcknowledgement acknowledgement =
                        m_acknowledgementsToSend[ii];

                    if (acknowledgement.SubscriptionId !=
                        subscriptionId)
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                    }
                    else if (availableSequenceNumberList.Remove(
                                 acknowledgement.SequenceNumber))
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                        UpdateLatestSequenceNumberToSend(
                            ref latestSequenceNumberToSend,
                            acknowledgement.SequenceNumber);
                    }
                    // a publish response may be processed out of
                    // order, allow for a tolerance until the
                    // sequence number is removed.
                    else if (Math.Abs(
                            (int)(acknowledgement.SequenceNumber
                                - latestSequenceNumberToSend)) <
                        kPublishRequestSequenceNumberOutOfOrderThreshold)
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                    }
                    else
                    {
                        m_logger.LogWarning(
                            "SessionId {SessionId}, " +
                            "SubscriptionId {SubscriptionId}, " +
                            "Sequence number={SequenceNumber} " +
                            "was not received in the available " +
                            "sequence numbers.",
                            m_context.SessionId,
                            subscriptionId,
                            acknowledgement.SequenceNumber);
                    }
                }

                // Check for outdated sequence numbers. May have
                // been not acked due to a network glitch.
                if (latestSequenceNumberToSend != 0 &&
                    availableSequenceNumberList.Count > 0)
                {
                    foreach (uint sequenceNumber in
                        availableSequenceNumberList)
                    {
                        if ((int)(latestSequenceNumberToSend
                                - sequenceNumber) >
                            kPublishRequestSequenceNumberOutdatedThreshold)
                        {
                            AddAcknowledgementToSend(
                                acknowledgementsToSend,
                                subscriptionId,
                                sequenceNumber);
                            m_logger.LogWarning(
                                "SessionId {SessionId}, " +
                                "SubscriptionId " +
                                "{SubscriptionId}, " +
                                "Sequence " +
                                "number={SequenceNumber} " +
                                "was outdated, acknowledged.",
                                m_context.SessionId,
                                subscriptionId,
                                sequenceNumber);
                        }
                    }
                }

#if DEBUG_SEQUENTIALPUBLISHING
                uint lastSentSequenceNumber = 0;
                if (availableSequenceNumberList != null)
                {
                    foreach (uint availableSequenceNumber in
                        availableSequenceNumberList)
                    {
                        if (m_latestAcknowledgementsSent
                            .ContainsKey(subscriptionId))
                        {
                            lastSentSequenceNumber =
                                m_latestAcknowledgementsSent[
                                    subscriptionId];
                            if (((lastSentSequenceNumber
                                        >= availableSequenceNumber)
                                    && (lastSentSequenceNumber
                                        != uint.MaxValue))
                                || (lastSentSequenceNumber
                                        == availableSequenceNumber)
                                    && (lastSentSequenceNumber
                                        == uint.MaxValue))
                            {
                                m_logger.LogWarning(
                                    "Received sequence number " +
                                    "which was already " +
                                    "acknowledged={0}",
                                    availableSequenceNumber);
                            }
                        }
                    }
                }

                if (m_latestAcknowledgementsSent
                    .ContainsKey(subscriptionId))
                {
                    lastSentSequenceNumber =
                        m_latestAcknowledgementsSent[
                            subscriptionId];
                    if (((lastSentSequenceNumber
                                >= notificationMessage
                                    .SequenceNumber)
                            && (lastSentSequenceNumber
                                != uint.MaxValue))
                        || (lastSentSequenceNumber
                                == notificationMessage
                                    .SequenceNumber)
                            && (lastSentSequenceNumber
                                == uint.MaxValue))
                    {
                        m_logger.LogWarning(
                            "Received sequence number which " +
                            "was already acknowledged={0}",
                            notificationMessage.SequenceNumber);
                    }
                }
#endif

                m_acknowledgementsToSend = acknowledgementsToSend;

                if (notificationMessage.IsEmpty)
                {
                    m_logger.LogTrace(
                        "Empty notification message received " +
                        "for SessionId {SessionId} with " +
                        "PublishTime {PublishTime}",
                        m_context.SessionId,
                        notificationMessage.PublishTime
                            .ToDateTime().ToLocalTime());
                }
            }

            bool subscriptionCreationInProgress = false;
            IReadOnlyList<Subscription> subscriptions =
                m_context.Subscriptions;

            // find the subscription.
            foreach (Subscription current in subscriptions)
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
                if (notificationMessage.PublishTime
                        .AddMilliseconds(
                            subscription.CurrentPublishingInterval
                            * subscription.CurrentLifetimeCount)
                    < DateTimeUtc.Now)
                {
                    m_logger.LogTrace(
                        "PublishTime {PublishTime} in publish " +
                        "response is too old for " +
                        "SubscriptionId {SubscriptionId}.",
                        notificationMessage.PublishTime
                            .ToLocalTime(),
                        subscription.Id);
                }

                // Validate publish time and reject future values.
                if (notificationMessage.PublishTime >
                    DateTimeUtc.Now.AddMilliseconds(
                        subscription.CurrentPublishingInterval
                        * subscription.CurrentLifetimeCount))
                {
                    m_logger.LogTrace(
                        "PublishTime {PublishTime} in publish " +
                        "response is newer than actual time " +
                        "for SubscriptionId " +
                        "{SubscriptionId}.",
                        notificationMessage.PublishTime
                            .ToLocalTime(),
                        subscription.Id);
                }
#endif
                // save the information that more notifications
                // are expected
                notificationMessage.MoreNotifications =
                    moreNotifications;

                // save the string table that came with the
                // notification.
                notificationMessage.StringTable =
                    responseHeader.StringTable;

                // update subscription cache.
                subscription.SaveMessageInCache(
                    availableSequenceNumberList,
                    notificationMessage);

                // raise the notification.
                var args = new NotificationEventArgs(
                    subscription,
                    notificationMessage,
                    responseHeader.StringTable);

                m_context.OnPublishNotification(
                    subscription, args);
            }
            else if (m_context.DeleteSubscriptionsOnClose &&
                     !m_context.Reconnecting &&
                     !subscriptionCreationInProgress)
            {
                // Delete abandoned subscription from server.
                m_logger.LogWarning(
                    "Received Publish Response for Unknown " +
                    "SubscriptionId={SubscriptionId}. " +
                    "Deleting abandoned subscription " +
                    "from server.",
                    subscriptionId);

                _ = Task.Run(
                    () => m_context
                        .DeleteOrphanedSubscriptionAsync(
                            subscriptionId));
            }
            else
            {
                // Do not delete publish requests of stale
                // subscriptions
                m_logger.LogWarning(
                    "Received Publish Response for Unknown " +
                    "SubscriptionId={SubscriptionId}. Ignored.",
                    subscriptionId);
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
            int minPublishRequestCount =
                GetDesiredPublishRequestCount(false);

            if (requestCount < minPublishRequestCount)
            {
                BeginPublish(m_context.OperationTimeout);
            }
            else
            {
                m_logger.LogDebug(
                    "PUBLISH - Did not send another publish " +
                    "request. GoodPublishRequestCount=" +
                    "{GoodRequestCount}, " +
                    "MinPublishRequestCount=" +
                    "{MinRequestCount}",
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
        /// <param name="createdOnly">False if called when
        /// re-queuing.</param>
        /// <returns>The number of desired publish requests for the
        /// session.</returns>
        protected virtual int GetDesiredPublishRequestCount(
            bool createdOnly)
        {
            IReadOnlyList<Subscription> subscriptions =
                m_context.Subscriptions;

            if (subscriptions.Count == 0)
            {
                return 0;
            }

            int publishCount;

            if (createdOnly)
            {
                int count = 0;
                foreach (Subscription subscription
                    in subscriptions)
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
        private void AddAcknowledgementToSend(
            List<SubscriptionAcknowledgement>
                acknowledgementsToSend,
            uint subscriptionId,
            uint sequenceNumber)
        {
            if (acknowledgementsToSend == null)
            {
                throw new ArgumentNullException(
                    nameof(acknowledgementsToSend));
            }

            Debug.Assert(
                Monitor.IsEntered(m_acknowledgementsToSendLock));

            var acknowledgement =
                new SubscriptionAcknowledgement
                {
                    SubscriptionId = subscriptionId,
                    SequenceNumber = sequenceNumber
                };

            acknowledgementsToSend.Add(acknowledgement);
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
                ((int)(sequenceNumber
                    - latestSequenceNumberToSend)) > 0)
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
        /// <returns>If the publish request limit was
        /// reached.</returns>
        private bool BelowPublishRequestLimit(int requestCount)
        {
            return (m_tooManyPublishRequests == 0) ||
                   (requestCount < m_tooManyPublishRequests);
        }

        /// <summary>
        /// Processes an error from a republish response.
        /// </summary>
        /// <param name="e">The exception.</param>
        /// <param name="subscriptionId">The subscription
        /// identifier.</param>
        /// <param name="sequenceNumber">The sequence
        /// number.</param>
        /// <returns>A tuple indicating whether the error was
        /// handled and the service result.</returns>
        internal (bool handled, ServiceResult error)
            ProcessRepublishResponseError(
                Exception e,
                uint subscriptionId,
                uint sequenceNumber)
        {
            var error = new ServiceResult(e);

            bool result = true;
            if (error.StatusCode ==
                    StatusCodes.BadSubscriptionIdInvalid ||
                error.StatusCode ==
                    StatusCodes.BadMessageNotAvailable)
            {
                m_logger.LogWarning(
                    "Message {SubscriptionId}-" +
                    "{SequenceNumber} no longer available.",
                    subscriptionId,
                    sequenceNumber);
            }
            else if (error.StatusCode ==
                     StatusCodes.BadEncodingLimitsExceeded)
            {
                m_logger.LogError(
                    e,
                    "Message {SubscriptionId}-" +
                    "{SequenceNumber} exceeded size limits, " +
                    "ignored.",
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
                m_logger.LogError(
                    e,
                    "Unexpected error sending " +
                    "republish request.");
            }

            // raise an error event.
            m_context.OnPublishError(
                error, subscriptionId, sequenceNumber);

            return (result, error);
        }
        #endregion
    }
}
