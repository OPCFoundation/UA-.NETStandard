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
using Opc.Ua.Redaction;

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
            m_outcomeDispatcher = new AcknowledgementOutcomeDispatcher(m_logger);
            m_eventLogger = context.Telemetry
                .CreateLogger(ClientEventIds.LegacyCategoryName);
            m_minPublishRequestCount = kDefaultPublishRequestCount;
            m_maxPublishRequestCount = kMaxPublishRequestCountMax;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_backgroundWork = new BackgroundTaskScope(
                nameof(ClassicSubscriptionEngine),
                context.Telemetry);
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

            if (publishCount <= 0)
            {
                return;
            }

            // Refill the pipeline up to the desired count. The reservation is
            // the real bound: it counts the requests the session still expects
            // plus the ones sent but not yet recorded, so this both refuses to
            // overshoot and still refills a pipeline whose requests the session
            // has written off.
            int startCount = fullQueue
                ? 0
                : GoodPublishRequestCount;
            for (int ii = startCount; ii < publishCount; ii++)
            {
                if (!TryReservePublishRequest(publishCount, out _))
                {
                    break;
                }

                if (!BeginPublishCore(timeout))
                {
                    Interlocked.Decrement(ref m_unrecordedPublishRequests);
                    break;
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
            if (disposing)
            {
                // Signal only: Dispose is synchronous. The throttled republish and
                // the orphan cleanup both stop as soon as the token trips.
                m_backgroundWork.Dispose();

                // Acknowledgements that were never sent or are still in flight end with the engine.
                List<AcknowledgementOutcome>? abandoned = null;
                lock (m_acknowledgementsToSendLock)
                {
                    foreach (SubscriptionAcknowledgement acknowledgement in m_acknowledgementScopes.Keys.ToList())
                    {
                        EndAcknowledgementLocked(ref abandoned, acknowledgement,
                            AcknowledgementDisposition.Abandoned, StatusCodes.BadShutdown);
                    }
                    m_acknowledgementScopes.Clear();
                    m_outstandingHandles.Clear();
                }
                ReportOutcomes(abandoned);
                _ = m_outcomeDispatcher.CompleteAsync().AsTask();
            }
        }

        /// <summary>
        /// Sends a publish request to the server.
        /// </summary>
        /// <remarks>
        /// This is the recovery nudge behind
        /// <see cref="Subscription.HandleOnKeepAliveStopped"/>: a subscription
        /// that has seen no notification asks for one more publish so the
        /// server has a request to answer. It is bounded by the same
        /// reservation as the automatic top up, because sending past the
        /// desired count cannot help - the server already holds that many
        /// requests and answers the surplus with
        /// <see cref="StatusCodes.BadTooManyPublishRequests"/> - while every
        /// subscription firing this nudge at once would otherwise multiply the
        /// pipeline by the number of subscriptions. A drained pipeline always
        /// has room, so the nudge still gets through when it is the outstanding
        /// requests that have stopped coming back.
        /// </remarks>
        /// <param name="timeout">The timeout for publish requests
        /// in milliseconds.</param>
        /// <returns>
        /// True if the pipeline holds the requested publish request when this
        /// returns, whether it was sent here or was already outstanding.
        /// </returns>
        internal bool BeginPublish(int timeout)
        {
            // At least one, so an empty pipeline is always refillable even
            // when no subscription has been created yet.
            int limit = Math.Max(1, GetDesiredPublishRequestCount(false));

            if (!TryReservePublishRequest(limit, out _))
            {
                // The pipeline already holds the requests this nudge asks for,
                // so the caller's intent is met without sending. Sending anyway
                // cannot help: the server holds that many requests already and
                // answers the surplus with BadTooManyPublishRequests.
                return true;
            }

            if (!BeginPublishCore(timeout))
            {
                Interlocked.Decrement(ref m_unrecordedPublishRequests);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sends a publish request. The caller must have reserved the request
        /// in <see cref="m_unrecordedPublishRequests"/>. The reservation is
        /// released here once the session has recorded the request, or by the
        /// caller if this returns <c>false</c>.
        /// </summary>
        private bool BeginPublishCore(int timeout)
        {
            // do not publish if reconnecting or the session is in closed state.
            if (!m_context.Connected)
            {
                m_logger.PublishSkippedSessionNotConnected();
                return false;
            }

            if (m_context.Reconnecting)
            {
                m_logger.PublishSkippedSessionReconnect(m_context.SessionId);
                return false;
            }

            if (m_context.Closing)
            {
                m_logger.PublishCancelledSessionClosed(m_context.SessionId);
                return false;
            }

            if (m_context.KeepAliveStopped)
            {
                m_logger.PublishSkippedSessionLostConnectionLast(m_context.LastKeepAliveTime);
                return false;
            }

            // collect the current set of acknowledgements.
            List<SubscriptionAcknowledgement>? acknowledgementsToSend = null;
            ISubscriptionAcknowledgementProvider? provider = Acknowledgements?.Provider;
            if (provider != null)
            {
                acknowledgementsToSend = TakeApprovedAcknowledgements(provider) ?? [];
            }
            else
            {
                List<AcknowledgementOutcome>? ended = null;
                lock (m_acknowledgementsToSendLock)
                {
                    // Tracked only with an acknowledgement observer: validate before and after the
                    // legacy handler, and end the entries it dropped.
                    bool tracked = m_acknowledgementScopes.Count != 0;
                    List<SubscriptionAcknowledgement>? presented = null;
                    if (tracked)
                    {
                        RemoveStaleLocked(ref ended);
                        presented = [.. m_acknowledgementsToSend];
                    }
                    (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending) =
                        m_context.PrepareAcknowledgementsToSend(m_acknowledgementsToSend);
                    acknowledgementsToSend = toSend;
                    m_acknowledgementsToSend = updatedPending;
                    if (presented != null)
                    {
                        EndDroppedLocked(presented, toSend, updatedPending, ref ended);
                        RemoveStaleLocked(toSend, ref ended);
                    }

#if DEBUG_SEQUENTIALPUBLISHING
                    foreach (var toSend in acknowledgementsToSend)
                    {
                        m_latestAcknowledgementsSent[toSend.SubscriptionId]
                            = toSend.SequenceNumber;
                    }
#endif
                }
                ReportOutcomes(ended);
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
                RequestHandle = ClientBase.NewSharedRequestHandle()
            };

            m_eventLogger.ClientEventPublishStart(
                (int)requestHeader.RequestHandle,
                m_context.SessionId);

            try
            {
                Activity? activity = CachedActivitySource.StartActivity();
                Task<PublishResponse> task = m_context.PublishAsync(
                    requestHeader,
                    acknowledgementsToSend,
                    default).AsTask();
                m_context.AsyncRequestStarted(
                    task,
                    activity,
                    requestHeader.RequestHandle,
                    DataTypes.PublishRequest);

                // The session now counts this request, so the reservation that
                // stood in for it is released. Releasing after the record is
                // published keeps the total conservative: the request is
                // briefly counted twice rather than not at all.
                Interlocked.Decrement(ref m_unrecordedPublishRequests);

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

            m_eventLogger.ClientEventPublishStop(
                (int)requestHeader.RequestHandle,
                sessionId);

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
                                "SubscriptionId={SubscriptionId}; SessionId={SessionId}",
                                code,
                                subscriptionId,
                                sessionId);
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

                ReportAcknowledgementResults(acknowledgementsToSend, acknowledgeResults);

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
                    (int)notificationMessage.SequenceNumber,
                    sessionId);

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
                        e.Message,
                        sessionId);
                }
                else
                {
                    m_logger.PublishRequestHandleReconnectingReconnectingErrorMessage(
                        requestHeader.RequestHandle,
                        m_context.Reconnecting,
                        e.Message,
                        sessionId);
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
                    if (Acknowledgements != null)
                    {
                        // Tracked acknowledgements end exactly once; send them after the reconnect.
                        RequeueFailedAcknowledgements(acknowledgementsToSend);
                    }
                    m_logger.PublishAbandonedAfterErrorMessageSession(
                        e.Message,
                        sessionId);
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != m_context.SessionId)
                {
                    if (Acknowledgements != null)
                    {
                        // The session was recreated in place. A subscription transferred to the new
                        // session keeps its lifetime, so the acknowledgement is sent there; one that
                        // was recreated, or whose message the server no longer holds, is ended by
                        // the validation before the next request. Each still ends exactly once.
                        RequeueFailedAcknowledgements(acknowledgementsToSend);
                    }
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
                RequeueFailedAcknowledgements(acknowledgementsToSend);

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
                        m_logger.PUBLISHTooManyRequestsSetLimit(
                            m_tooManyPublishRequests,
                            sessionId);
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
                            error.StatusCode,
                            sessionId);
                    }

                    // throttle the next publish to reduce
                    // server load
                    m_backgroundWork.Run(
                        "ThrottleNextPublish",
                        async ct =>
                        {
                            await m_timeProvider.Delay(TimeSpan.FromMilliseconds(100), ct)
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
        /// <param name="responseHeader">The response header.</param>
        /// <param name="subscriptionId">The subscription the message belongs to.</param>
        /// <param name="availableSequenceNumbers">The sequence numbers the
        /// server still holds for the subscription.</param>
        /// <param name="moreNotifications">Whether more notifications are pending.</param>
        /// <param name="notificationMessage">The notification message.</param>
        /// <param name="republished">The message came from a Republish
        /// response, which carries no available sequence numbers: the empty
        /// list then does not mean the server holds nothing.</param>
        internal void ProcessPublishResponse(
            ResponseHeader responseHeader,
            uint subscriptionId,
            ArrayOf<uint> availableSequenceNumbers,
            bool moreNotifications,
            NotificationMessage notificationMessage,
            bool republished = false)
        {
            Subscription? subscription = null;
            var availableSequenceNumberList = availableSequenceNumbers.ToList();

            // send notification that the server is alive.
            m_context.OnKeepAlive(
                m_context.ServerState,
                (DateTime)responseHeader.Timestamp);

            // collect the current set of acknowledgements.
            AcknowledgementScope? receivedScope = null;
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
                    receivedScope = AddAcknowledgementToSend(
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
                    else if (republished)
                    {
                        // a republish response cannot tell which messages the
                        // server still holds, so keep every pending ack.
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
                    // The distance is compared without Math.Abs, which throws
                    // an OverflowException for int.MinValue (a distance of 2^31).
                    else if (IsWithinOutOfOrderThreshold(
                        acknowledgement.SequenceNumber,
                        latestSequenceNumberToSend))
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

                List<AcknowledgementOutcome>? pruned = null;
                if (m_acknowledgementScopes.Count != 0)
                {
                    var kept = new HashSet<SubscriptionAcknowledgement>(
                        acknowledgementsToSend, IdentityComparer<SubscriptionAcknowledgement>.Instance);
                    foreach (SubscriptionAcknowledgement previous in m_acknowledgementsToSend)
                    {
                        if (!kept.Contains(previous))
                        {
                            // The server no longer lists the message.
                            EndAcknowledgementLocked(ref pruned, previous, AcknowledgementDisposition.NotRetained,
                                StatusCodes.BadSequenceNumberUnknown);
                        }
                    }
                }
                m_acknowledgementsToSend = acknowledgementsToSend;
                ReportOutcomes(pruned);

                if (notificationMessage.IsEmpty && m_logger.IsEnabled(LogLevel.Trace))
                {
                    m_logger.EmptyNotificationMessageReceivedSessionIdSessionId(
                        m_context.SessionId,
                        notificationMessage.PublishTime.ToDateTime().ToLocalTime());
                }
            }

            bool subscriptionCreationInProgress = false;

            // find the subscription that most recently claimed the id.
            foreach (Subscription current in m_context.Subscriptions)
            {
                if (current.Id == subscriptionId)
                {
                    subscription = SelectLatestClaim(subscription, current);
                }
                else if (current.Id == default)
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
                // The publish-time validation below is diagnostics only, so
                // skip the comparisons and the local-time conversions unless
                // the messages would actually be emitted.
                if (m_logger.IsEnabled(LogLevel.Trace))
                {
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
                }

                // save the information that more notifications
                // are expected
                notificationMessage.MoreNotifications = moreNotifications;

                // save the string table that came with the
                // notification.
                notificationMessage.StringTable = responseHeader.StringTable;

                // update subscription cache with the scope tracked at receipt, so the
                // delivered notifications carry the handle the engine acknowledges.
                if (republished)
                {
                    subscription.SaveRepublishedMessageInCache(notificationMessage, receivedScope);
                }
                else
                {
                    subscription.SaveMessageInCache(availableSequenceNumbers, notificationMessage, receivedScope);
                }

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
                m_logger.ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId(
                    subscriptionId,
                    m_context.SessionId);

                m_backgroundWork.Run(
                    "DeleteOrphanedSubscription",
                    async _ => await m_context
                        .DeleteOrphanedSubscriptionAsync(subscriptionId)
                        .ConfigureAwait(false));
            }
            else
            {
                // Do not delete publish requests of stale
                // subscriptions
                m_logger.ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId2(
                    subscriptionId,
                    m_context.SessionId);
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

            int minPublishRequestCount = GetDesiredPublishRequestCount(false);

            // Reserve the slot before issuing. Reading a count and then
            // sending is a check-then-act: concurrent publish completions all
            // observe the same value, all conclude they are below the limit,
            // and each sends a request, overshooting by the number of
            // completions that overlap.
            if (!TryReservePublishRequest(minPublishRequestCount, out int requestCount))
            {
                m_logger.PUBLISHDidNotSendAnotherPublish(
                    requestCount,
                    minPublishRequestCount,
                    m_context.SessionId);
                return;
            }

            if (!BeginPublishCore(m_context.OperationTimeout))
            {
                Interlocked.Decrement(ref m_unrecordedPublishRequests);
            }
        }

        /// <summary>
        /// Atomically reserves capacity for one more publish request if fewer
        /// than <paramref name="limit"/> are outstanding.
        /// </summary>
        /// <remarks>
        /// Outstanding means the requests the session still expects to return
        /// (<see cref="ISubscriptionEngineContext.GoodPublishRequestCount"/>)
        /// plus the requests this engine has sent that the session has not
        /// recorded yet. The session's count is authoritative: it drops a
        /// request as soon as the session writes it off, which is what
        /// <see cref="Session.OnKeepAlive"/> does to the whole pipeline when
        /// keep alives recover. Counting only what this engine has sent would
        /// hold those write offs forever and the pipeline would never refill.
        /// The unrecorded count closes the opposite gap: the session only
        /// counts a request once <c>AsyncRequestStarted</c> has recorded it,
        /// which happens after the request was issued, so concurrent callers
        /// would otherwise all read the same lagging value and each send.
        /// </remarks>
        /// <param name="limit">The maximum number of outstanding requests.</param>
        /// <param name="outstanding">The number observed outstanding.</param>
        /// <returns>True if a slot was reserved.</returns>
        private bool TryReservePublishRequest(int limit, out int outstanding)
        {
            while (true)
            {
                // Read the unrecorded count first. A request that moves from
                // unrecorded to recorded between the two reads is then counted
                // twice rather than missed, which errs towards sending less.
                int unrecorded = Volatile.Read(ref m_unrecordedPublishRequests);
                int current = m_context.GoodPublishRequestCount + unrecorded;

                if (current >= limit)
                {
                    outstanding = current;
                    return false;
                }

                if (Interlocked.CompareExchange(
                        ref m_unrecordedPublishRequests,
                        unrecorded + 1,
                        unrecorded) == unrecorded)
                {
                    outstanding = current;
                    return true;
                }
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
        /// Gets the session's acknowledgement configuration, or <c>null</c>.
        /// </summary>
        private SessionAcknowledgements? Acknowledgements =>
            (m_context as IAcknowledgementPolicyCapability)?.Acknowledgements;

        /// <summary>
        /// Finds the subscription that most recently claimed a server id. An older
        /// subscription still registered under the id is marked superseded, so that its
        /// acknowledgements are never sent under the reused id.
        /// </summary>
        /// <param name="subscriptionId">The server id.</param>
        /// <param name="includeSuperseded">Whether a superseded subscription is returned
        /// when no other subscription holds the id.</param>
        private Subscription? FindSubscription(uint subscriptionId, bool includeSuperseded = false)
        {
            Subscription? owner = null;
            foreach (Subscription subscription in m_context.Subscriptions)
            {
                if (subscription.Id == subscriptionId)
                {
                    owner = SelectLatestClaim(owner, subscription);
                }
            }
            return owner != null && (includeSuperseded || !owner.IsIdClaimSuperseded) ? owner : null;
        }

        /// <summary>
        /// Returns the subscription with the later claim of a shared server id and marks the
        /// other one superseded.
        /// </summary>
        private static Subscription SelectLatestClaim(Subscription? current, Subscription candidate)
        {
            if (current == null || ReferenceEquals(current, candidate))
            {
                return candidate;
            }
            if (candidate.IdClaim > current.IdClaim)
            {
                current.SupersedeIdClaim();
                return candidate;
            }
            candidate.SupersedeIdClaim();
            return current;
        }

        /// <summary>
        /// Takes the acknowledgements the provider approves for the next Publish request.
        /// The provider is called without holding the acknowledgement lock. A decision that
        /// does not complete synchronously keeps its acknowledgements pending until a later
        /// request, so the synchronous publish path never blocks on it.
        /// </summary>
        private List<SubscriptionAcknowledgement>? TakeApprovedAcknowledgements(
            ISubscriptionAcknowledgementProvider provider)
        {
            List<AcknowledgementOutcome>? retired = null;
            List<SubscriptionAcknowledgement>? approved = null;
            List<SubscriptionAcknowledgement>? candidates = null;
            AcknowledgementHandle[]? handles = null;
            lock (m_acknowledgementsToSendLock)
            {
                if (m_pendingDecision is { } pending)
                {
                    if (pending.Decision.IsCompleted)
                    {
                        m_pendingDecision = null;
                        if (pending.Decision.Status == TaskStatus.RanToCompletion)
                        {
                            approved = TakeApprovedLocked(pending.Candidates, pending.Decision.Result, ref retired);
                        }
                        else
                        {
                            m_logger.AcknowledgementDecisionFailed(
                                pending.Decision.Exception, pending.Candidates.Count);
                        }
                    }
                }
                else
                {
                    RemoveStaleLocked(ref retired);
                    if (m_acknowledgementsToSend.Count != 0)
                    {
                        candidates = [.. m_acknowledgementsToSend];
                        handles = new AcknowledgementHandle[candidates.Count];
                        for (int i = 0; i < handles.Length; i++)
                        {
                            handles[i] = GetHandleLocked(candidates[i]);
                        }
                    }
                }
            }
            ReportOutcomes(retired);
            if (candidates == null || handles == null)
            {
                return approved;
            }

            ValueTask<AcknowledgementDecision> decision;
            try
            {
                decision = provider.DecideAsync(handles, m_backgroundWork.ShutdownToken);
            }
            catch (Exception ex)
            {
                // Fail closed: the acknowledgements stay pending.
                m_logger.AcknowledgementDecisionFailed(ex, candidates.Count);
                return null;
            }
            if (decision.IsCompletedSuccessfully)
            {
                retired = null;
                lock (m_acknowledgementsToSendLock)
                {
                    approved = TakeApprovedLocked(candidates, decision.Result, ref retired);
                }
                ReportOutcomes(retired);
                return approved;
            }
            Task<AcknowledgementDecision> task = decision.AsTask();
            lock (m_acknowledgementsToSendLock)
            {
                m_pendingDecision = (task, candidates);
            }
            // Publish again once the decision is known.
            task.ContinueWith(
                static (_, state) => ((ClassicSubscriptionEngine)state!).QueueBeginPublish(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return null;
        }

        /// <summary>
        /// Removes the approved acknowledgements of a decided batch from the pending ones and
        /// returns them. Acknowledgements that ended in the meantime are skipped, and those
        /// whose subscription was deleted or recreated while the provider decided are retired.
        /// </summary>
        private List<SubscriptionAcknowledgement>? TakeApprovedLocked(
            List<SubscriptionAcknowledgement> candidates,
            AcknowledgementDecision decision,
            ref List<AcknowledgementOutcome>? retired)
        {
            RemoveStaleLocked(ref retired);
            bool[] deferred = new bool[candidates.Count];
            for (int i = 0; i < decision.Deferred.Count; i++)
            {
                int index = decision.Deferred[i];
                if ((uint)index < (uint)deferred.Length)
                {
                    deferred[index] = true;
                }
            }
            var approved = new HashSet<SubscriptionAcknowledgement>(IdentityComparer<SubscriptionAcknowledgement>.Instance);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!deferred[i])
                {
                    approved.Add(candidates[i]);
                }
            }
            if (approved.Count == 0)
            {
                return null;
            }
            var toSend = new List<SubscriptionAcknowledgement>(approved.Count);
            m_acknowledgementsToSend.RemoveAll(acknowledgement =>
            {
                if (approved.Contains(acknowledgement))
                {
                    toSend.Add(acknowledgement);
                    return true;
                }
                return false;
            });
            return toSend.Count == 0 ? null : toSend;
        }

        /// <summary>
        /// Gets the handle of a tracked acknowledgement.
        /// </summary>
        private AcknowledgementHandle GetHandleLocked(SubscriptionAcknowledgement acknowledgement)
        {
            return m_acknowledgementScopes.TryGetValue(acknowledgement, out AcknowledgementScope? scope) &&
                scope != null
                ? new AcknowledgementHandle(scope, acknowledgement.SequenceNumber)
                : default;
        }

        /// <summary>
        /// Reports how the acknowledgements of a successful Publish request ended.
        /// </summary>
        private void ReportAcknowledgementResults(
            List<SubscriptionAcknowledgement>? sent,
            ArrayOf<StatusCode> results)
        {
            if (sent == null || sent.Count == 0)
            {
                return;
            }
            List<AcknowledgementOutcome>? outcomes = null;
            lock (m_acknowledgementsToSendLock)
            {
                if (m_acknowledgementScopes.Count == 0)
                {
                    return;
                }
                for (int i = 0; i < sent.Count; i++)
                {
                    StatusCode status = i < results.Count ? results[i] : StatusCodes.BadUnexpectedError;
                    EndAcknowledgementLocked(ref outcomes, sent[i],
                        StatusCode.IsGood(status)
                            ? AcknowledgementDisposition.Acknowledged
                            : AcknowledgementDisposition.NotRetained,
                        status);
                }
            }
            ReportOutcomes(outcomes);
        }

        /// <summary>
        /// Ends a tracked acknowledgement and adds its outcome.
        /// </summary>
        private void EndAcknowledgementLocked(
            ref List<AcknowledgementOutcome>? outcomes,
            SubscriptionAcknowledgement acknowledgement,
            AcknowledgementDisposition disposition,
            StatusCode status)
        {
            if (m_acknowledgementScopes.Remove(acknowledgement, out AcknowledgementScope? scope) && scope != null)
            {
                var handle = new AcknowledgementHandle(scope, acknowledgement.SequenceNumber);
                m_outstandingHandles.Remove(handle);
                (outcomes ??= []).Add(new AcknowledgementOutcome(handle, disposition, status));
            }
        }

        /// <summary>
        /// Ends the pending acknowledgements whose subscription was deleted or recreated since
        /// they were queued: sending them could confirm a message of a subscription that reuses
        /// the id.
        /// </summary>
        private void RemoveStaleLocked(ref List<AcknowledgementOutcome>? retired)
        {
            RemoveStaleLocked(m_acknowledgementsToSend, ref retired);
        }

        /// <summary>
        /// Ends the tracked acknowledgements of a list whose subscription was deleted,
        /// recreated or superseded since they were queued, and removes them from the list.
        /// </summary>
        private void RemoveStaleLocked(
            List<SubscriptionAcknowledgement> acknowledgements,
            ref List<AcknowledgementOutcome>? retired)
        {
            if (m_acknowledgementScopes.Count == 0 || acknowledgements.Count == 0)
            {
                return;
            }
            Dictionary<uint, Subscription>? owners = null;
            int kept = 0;
            for (int i = 0; i < acknowledgements.Count; i++)
            {
                SubscriptionAcknowledgement acknowledgement = acknowledgements[i];
                if (m_acknowledgementScopes.TryGetValue(acknowledgement, out AcknowledgementScope? scope) &&
                    scope != null &&
                    !IsCurrent(scope, owners ??= GetOwnersLocked()))
                {
                    EndAcknowledgementLocked(ref retired, acknowledgement,
                        AcknowledgementDisposition.OwnerRetired, StatusCodes.BadSubscriptionIdInvalid);
                    continue;
                }
                acknowledgements[kept++] = acknowledgement;
            }
            acknowledgements.RemoveRange(kept, acknowledgements.Count - kept);

            static bool IsCurrent(AcknowledgementScope scope, Dictionary<uint, Subscription> owners)
            {
                if (!owners.TryGetValue(scope.SubscriptionId, out Subscription? owner) ||
                    !ReferenceEquals(owner, scope.Owner))
                {
                    return false;
                }
                AcknowledgementScope current = owner.GetAcknowledgementScope();
                return current.Generation == scope.Generation && current.SubscriptionId == scope.SubscriptionId;
            }
        }

        /// <summary>
        /// Ends the tracked acknowledgements, pending or in flight, of a subscription lifetime
        /// that ended, without waiting for another Publish request: deleting or removing the
        /// last subscription may stop publishing altogether.
        /// </summary>
        /// <param name="subscription">The subscription whose lifetime ended.</param>
        /// <param name="includeCurrentLifetime">Whether the subscription's current lifetime
        /// ended too, because it no longer belongs to this session.</param>
        internal void RetireAcknowledgementsOf(Subscription subscription, bool includeCurrentLifetime = false)
        {
            List<AcknowledgementOutcome>? retired = null;
            lock (m_acknowledgementsToSendLock)
            {
                if (m_acknowledgementScopes.Count == 0)
                {
                    return;
                }
                AcknowledgementScope current = subscription.GetAcknowledgementScope();
                List<SubscriptionAcknowledgement>? ended = null;
                foreach (KeyValuePair<SubscriptionAcknowledgement, AcknowledgementScope?> entry in
                    m_acknowledgementScopes)
                {
                    if (entry.Value != null &&
                        ReferenceEquals(entry.Value.Owner, subscription) &&
                        (includeCurrentLifetime || !entry.Value.Equals(current)))
                    {
                        (ended ??= []).Add(entry.Key);
                    }
                }
                if (ended == null)
                {
                    return;
                }
                var removed = new HashSet<SubscriptionAcknowledgement>(
                    ended, IdentityComparer<SubscriptionAcknowledgement>.Instance);
                foreach (SubscriptionAcknowledgement acknowledgement in ended)
                {
                    EndAcknowledgementLocked(ref retired, acknowledgement,
                        AcknowledgementDisposition.OwnerRetired, StatusCodes.BadSubscriptionIdInvalid);
                }
                m_acknowledgementsToSend.RemoveAll(removed.Contains);
            }
            ReportOutcomes(retired);
        }

        /// <summary>
        /// Puts the acknowledgements of a failed Publish request back into the pending ones.
        /// When acknowledgements are tracked, those that ended while the request was in
        /// flight (for example because their subscription was deleted) are not requeued: they
        /// would no longer be validated and could be sent under a reused id.
        /// </summary>
        private void RequeueFailedAcknowledgements(List<SubscriptionAcknowledgement>? acknowledgements)
        {
            if (acknowledgements == null || acknowledgements.Count == 0)
            {
                return;
            }
            lock (m_acknowledgementsToSendLock)
            {
                if (Acknowledgements == null)
                {
                    m_acknowledgementsToSend.AddRange(acknowledgements);
                    return;
                }
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    if (m_acknowledgementScopes.ContainsKey(acknowledgement))
                    {
                        m_acknowledgementsToSend.Add(acknowledgement);
                    }
                }
            }
        }

        /// <summary>
        /// Ends the presented acknowledgements the legacy deferral handler neither sent nor
        /// retained.
        /// </summary>
        private void EndDroppedLocked(
            List<SubscriptionAcknowledgement> presented,
            List<SubscriptionAcknowledgement> toSend,
            List<SubscriptionAcknowledgement> retained,
            ref List<AcknowledgementOutcome>? ended)
        {
            var kept = new HashSet<SubscriptionAcknowledgement>(
                toSend, IdentityComparer<SubscriptionAcknowledgement>.Instance);
            kept.UnionWith(retained);
            foreach (SubscriptionAcknowledgement acknowledgement in presented)
            {
                if (!kept.Contains(acknowledgement))
                {
                    EndAcknowledgementLocked(ref ended, acknowledgement,
                        AcknowledgementDisposition.Abandoned, StatusCodes.BadRequestCancelledByClient);
                }
            }
        }

        /// <summary>
        /// Gets the current owner of each server id. Superseded subscriptions own no id.
        /// </summary>
        private Dictionary<uint, Subscription> GetOwnersLocked()
        {
            var owners = new Dictionary<uint, Subscription>();
            foreach (Subscription subscription in m_context.Subscriptions)
            {
                uint id = subscription.Id;
                if (id != 0)
                {
                    owners[id] = SelectLatestClaim(
                        owners.TryGetValue(id, out Subscription? current) ? current : null, subscription);
                }
            }
            foreach (KeyValuePair<uint, Subscription> owner in owners.ToList())
            {
                if (owner.Value.IsIdClaimSuperseded)
                {
                    owners.Remove(owner.Key);
                }
            }
            return owners;
        }

        /// <summary>
        /// Hands outcomes to the observer, outside any lock.
        /// </summary>
        private void ReportOutcomes(List<AcknowledgementOutcome>? outcomes)
        {
            m_outcomeDispatcher.Report(Acknowledgements?.Observer, outcomes);
        }

        /// <summary>
        /// Compares instances by object identity.
        /// </summary>
        /// <typeparam name="T">The compared reference type.</typeparam>
        private sealed class IdentityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            /// <summary>
            /// The shared instance.
            /// </summary>
            public static IdentityComparer<T> Instance { get; } = new();

            /// <inheritdoc/>
            public bool Equals(T? x, T? y)
            {
                return ReferenceEquals(x, y);
            }

            /// <inheritdoc/>
            public int GetHashCode(T obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>
        /// Adds an acknowledgement to the list to send.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="acknowledgementsToSend"/> is <c>null</c>.</exception>
        /// <returns>The scope the acknowledgement is tracked with, or <c>null</c>.</returns>
        private AcknowledgementScope? AddAcknowledgementToSend(
            List<SubscriptionAcknowledgement> acknowledgementsToSend,
            uint subscriptionId,
            uint sequenceNumber)
        {
            if (acknowledgementsToSend == null)
            {
                throw new ArgumentNullException(nameof(acknowledgementsToSend));
            }

            Debug.Assert(m_acknowledgementsToSendLock.IsHeldByCurrentThread);

            var acknowledgement = new SubscriptionAcknowledgement
            {
                SubscriptionId = subscriptionId,
                SequenceNumber = sequenceNumber
            };
            AcknowledgementScope? scope = null;
            SessionAcknowledgements? acknowledgements = Acknowledgements;
            if (acknowledgements != null)
            {
                scope = FindSubscription(subscriptionId)?.GetAcknowledgementScope();
                if (scope != null)
                {
                    var handle = new AcknowledgementHandle(scope, sequenceNumber);
                    if (!m_outstandingHandles.Add(handle))
                    {
                        // Already pending or in flight: a second entry would end twice.
                        return scope;
                    }
                    // Tracked before the message is delivered, so it can be confirmed.
                    (acknowledgements.Provider as IAcknowledgementTracker)?.Track(handle);
                }
                m_acknowledgementScopes[acknowledgement] = scope;
            }
            acknowledgementsToSend.Add(acknowledgement);
            return scope;
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
                List<AcknowledgementOutcome>? retired = null;
                foreach (SubscriptionAcknowledgement acknowledgement in m_acknowledgementsToSend)
                {
                    if (acknowledgement.SubscriptionId == subscriptionId)
                    {
                        EndAcknowledgementLocked(ref retired, acknowledgement,
                            AcknowledgementDisposition.OwnerRetired, StatusCodes.BadSubscriptionIdInvalid);
                    }
                }
                m_acknowledgementsToSend.RemoveAll(
                    ack => ack.SubscriptionId == subscriptionId);
                ReportOutcomes(retired);
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
                    sequenceNumber,
                    m_context.SessionId);
            }
            else if (error.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
            {
                m_logger.MessageSubscriptionIdSequenceNumberExceededSizeLimits(
                    e,
                    subscriptionId,
                    sequenceNumber,
                    m_context.SessionId);
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

        /// <summary>
        /// Returns true if the wrap-aware distance between two sequence
        /// numbers is below the out-of-order tolerance in either direction.
        /// </summary>
        internal static bool IsWithinOutOfOrderThreshold(uint sequenceNumber, uint latestSequenceNumber)
        {
            int delta = unchecked((int)(sequenceNumber - latestSequenceNumber));
            return delta > -kPublishRequestSequenceNumberOutOfOrderThreshold &&
                delta < kPublishRequestSequenceNumberOutOfOrderThreshold;
        }

        private const int kMinPublishRequestCountMax = 100;
        private const int kMaxPublishRequestCountMax = ushort.MaxValue;
        private const int kDefaultPublishRequestCount = 1;
        private const int kPublishRequestSequenceNumberOutOfOrderThreshold = 10;
        private const int kPublishRequestSequenceNumberOutdatedThreshold = 100;

        /// <summary>
        /// The activity source for publish requests, resolved once per telemetry
        /// context: the GetActivitySource extension walks the stack to find the
        /// calling assembly, which is too expensive to repeat for every request.
        /// </summary>
        private ActivitySource CachedActivitySource
        {
            get
            {
                ITelemetryContext telemetry = m_context.Telemetry;
                Tuple<ITelemetryContext, ActivitySource>? cached = m_activitySource;
                if (cached == null || !ReferenceEquals(cached.Item1, telemetry))
                {
                    cached = Tuple.Create(telemetry, telemetry.GetActivitySource());
                    m_activitySource = cached;
                }
                return cached.Item2;
            }
        }

        private readonly ISubscriptionEngineContext m_context;
        private readonly ILogger m_logger;
        private readonly ILogger m_eventLogger;
        private readonly TimeProvider m_timeProvider;
        private readonly BackgroundTaskScope m_backgroundWork;
        private readonly Lock m_acknowledgementsToSendLock = new();
        private List<SubscriptionAcknowledgement> m_acknowledgementsToSend = [];

        /// <summary>
        /// Acknowledgement scope of each tracked acknowledgement. Only maintained when the
        /// session has an acknowledgement configuration. Guarded by
        /// <see cref="m_acknowledgementsToSendLock"/>.
        /// </summary>
        private readonly Dictionary<SubscriptionAcknowledgement, AcknowledgementScope?> m_acknowledgementScopes =
            new(IdentityComparer<SubscriptionAcknowledgement>.Instance);

        /// <summary>
        /// Handles of the tracked acknowledgements that are pending or in flight, so that each
        /// handle has a single entry and ends exactly once. Guarded by
        /// <see cref="m_acknowledgementsToSendLock"/>.
        /// </summary>
        private readonly HashSet<AcknowledgementHandle> m_outstandingHandles = [];

        /// <summary>
        /// A provider decision that did not complete synchronously, with the acknowledgements
        /// it decides about. Guarded by <see cref="m_acknowledgementsToSendLock"/>.
        /// </summary>
        private (Task<AcknowledgementDecision> Decision, List<SubscriptionAcknowledgement> Candidates)?
            m_pendingDecision;

        private readonly AcknowledgementOutcomeDispatcher m_outcomeDispatcher;
        private Tuple<ITelemetryContext, ActivitySource>? m_activitySource;
        private int m_unrecordedPublishRequests;
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
            Message = "Publish skipped due to session reconnect, SessionId={SessionId}")]
        public static partial void PublishSkippedSessionReconnect(this ILogger logger, NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 2, Level = LogLevel.Warning,
            Message = "Publish cancelled due to session closed, SessionId={SessionId}")]
        public static partial void PublishCancelledSessionClosed(this ILogger logger, NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 3, Level = LogLevel.Warning,
            Message = "Publish skipped due to session lost connection. Last successful keepalive: {LastKeepAlive}")]
        public static partial void PublishSkippedSessionLostConnectionLast(this ILogger logger, DateTime lastKeepAlive);

        [LoggerMessage(
            EventId = ClientEventIds.LegacyPublishStartId,
            EventName = "PublishStart",
            Level = LogLevel.Trace,
            Message = "PUBLISH #{RequestHandle} SENT, SessionId={SessionId}")]
        public static partial void ClientEventPublishStart(
            this ILogger logger,
            int requestHandle,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 5, Level = LogLevel.Error,
            Message = "Unexpected error sending publish request.")]
        public static partial void UnexpectedErrorSendingPublishRequest(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = ClientEventIds.LegacyPublishStopId,
            EventName = "PublishStop",
            Level = LogLevel.Trace,
            Message = "PUBLISH #{RequestHandle} RECEIVED, SessionId={SessionId}")]
        public static partial void ClientEventPublishStop(
            this ILogger logger,
            int requestHandle,
            NodeId? sessionId);

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
            Message = "NOTIFICATION RECEIVED: SubId={SubscriptionId}, SeqNo={SequenceNumber}," +
                " SessionId={SessionId}")]
        public static partial void ClientEventNotificationReceived(
            this ILogger logger,
            int subscriptionId,
            int sequenceNumber,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 9, Level = LogLevel.Warning,
            Message = "No new publish sent because of reconnect in progress.")]
        public static partial void NoNewPublishSentReconnectProgress(this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 10, Level = LogLevel.Warning,
            Message = "Publish #{RequestHandle}, Subscription count = 0, Error: {Message}," +
                " SessionId={SessionId}")]
        public static partial void PublishRequestHandleSubscriptionCountErrorMessage(
            this ILogger logger,
            uint requestHandle,
            [SensitiveText] string message,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 11, Level = LogLevel.Error,
            Message = "Publish #{RequestHandle}, Reconnecting={Reconnecting}, Error: {Message}," +
                " SessionId={SessionId}")]
        public static partial void PublishRequestHandleReconnectingReconnectingErrorMessage(
            this ILogger logger,
            uint requestHandle,
            bool reconnecting,
            [SensitiveText] string message,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 12, Level = LogLevel.Information,
            Message = "Publish abandoned after error {Message} due to session {SessionId} reconnecting")]
        public static partial void PublishAbandonedAfterErrorMessageSession(
            this ILogger logger,
            [SensitiveText] string message,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 13, Level = LogLevel.Error,
            Message = "Publish abandoned after error {Message} because session id changed: " +
                "Old {PreviousSessionId} != New {SessionId}")]
        public static partial void PublishAbandonedAfterErrorMessageSession2(
            this ILogger logger,
            [SensitiveText] string message,
            NodeId? previousSessionId,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 14, Level = LogLevel.Information,
            Message = "Publish abandoned after error {Message} because session {SessionId} was closed.")]
        public static partial void PublishAbandonedAfterErrorMessageSession3(
            this ILogger logger,
            [SensitiveText] string message,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 15, Level = LogLevel.Information,
            Message = "PUBLISH - Too many requests, set limit to GoodPublishRequestCount={GoodRequestCount}." +
                " SessionId={SessionId}")]
        public static partial void PUBLISHTooManyRequestsSetLimit(
            this ILogger logger,
            int goodRequestCount,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 16, Level = LogLevel.Error,
            Message = "PUBLISH #{RequestHandle} - Unhandled error {StatusCode} during Publish." +
                " SessionId={SessionId}")]
        public static partial void PUBLISHRequestHandleUnhandledErrorStatusCodeDuring(
            this ILogger logger,
            Exception? exception,
            uint requestHandle,
            StatusCode statusCode,
            NodeId? sessionId);

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
                " subscription from server. SessionId={SessionId}")]
        public static partial void ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId(
            this ILogger logger,
            uint subscriptionId,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 23, Level = LogLevel.Warning,
            Message = "Received Publish Response for Unknown SubscriptionId={SubscriptionId}. Ignored." +
                " SessionId={SessionId}")]
        public static partial void ReceivedPublishResponseUnknownSubscriptionIdSubscriptionId2(
            this ILogger logger,
            uint subscriptionId,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 24, Level = LogLevel.Debug,
            Message = "PUBLISH - Did not send another publish request. " +
                "GoodPublishRequestCount={GoodRequestCount}, MinPublishRequestCount={MinRequestCount}," +
                " SessionId={SessionId}")]
        public static partial void PUBLISHDidNotSendAnotherPublish(
            this ILogger logger,
            int goodRequestCount,
            int minRequestCount,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 25, Level = LogLevel.Warning,
            Message = "Message {SubscriptionId}-{SequenceNumber} no longer available. SessionId={SessionId}")]
        public static partial void MessageSubscriptionIdSequenceNumberNoLongerAvailable(
            this ILogger logger,
            uint subscriptionId,
            uint sequenceNumber,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 26, Level = LogLevel.Error,
            Message = "Message {SubscriptionId}-{SequenceNumber} exceeded size limits, ignored." +
                " SessionId={SessionId}")]
        public static partial void MessageSubscriptionIdSequenceNumberExceededSizeLimits(
            this ILogger logger,
            Exception? exception,
            uint subscriptionId,
            uint sequenceNumber,
            NodeId? sessionId);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 28, Level = LogLevel.Error,
            Message = "The acknowledgement provider failed; {Count} acknowledgements stay pending.")]
        public static partial void AcknowledgementDecisionFailed(
            this ILogger logger,
            Exception? exception,
            int count);

        [LoggerMessage(EventId = ClientEventIds.ClassicSubscriptionEngine + 27, Level = LogLevel.Error,
            Message = "Unexpected error sending republish request.")]
        public static partial void UnexpectedErrorSendingRepublishRequest(this ILogger logger, Exception? exception);
    }

}
