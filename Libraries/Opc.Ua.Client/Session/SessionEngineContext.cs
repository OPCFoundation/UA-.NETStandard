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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    public partial class Session
    {
        /// <summary>
        /// Adapter that implements <see cref="ISubscriptionEngineContext"/>
        /// by delegating to the owning <see cref="Session"/> instance.
        /// </summary>
        private sealed class SessionEngineContext : ISubscriptionEngineContext
        {
            public SessionEngineContext(Session session)
            {
                m_session = session
                    ?? throw new ArgumentNullException(
                        nameof(session));
            }

            /// <inheritdoc/>
            public NodeId SessionId => m_session.SessionId;

            /// <inheritdoc/>
            public bool Reconnecting => m_session.Reconnecting;

            /// <inheritdoc/>
            public bool Connected => m_session.Connected;

            /// <inheritdoc/>
            public bool Closing => m_session.Closing;

            /// <inheritdoc/>
            public bool Disposed => m_session.Disposed;

            /// <inheritdoc/>
            public bool DeleteSubscriptionsOnClose
                => m_session.DeleteSubscriptionsOnClose;

            /// <inheritdoc/>
            public int OperationTimeout
                => m_session.OperationTimeout;

            /// <inheritdoc/>
            public ServerState ServerState
                => m_session.m_serverState;

            /// <inheritdoc/>
            public DiagnosticsMasks ReturnDiagnostics
                => m_session.ReturnDiagnostics;

            /// <inheritdoc/>
            public ITelemetryContext Telemetry
                => m_session.m_telemetry;

            /// <inheritdoc/>
            public IReadOnlyList<Subscription> Subscriptions
            {
                get
                {
                    lock (m_session.m_lock)
                    {
                        return [.. m_session.m_subscriptions];
                    }
                }
            }

            /// <inheritdoc/>
            public SemaphoreSlim ReconnectLock
                => m_session.m_reconnectLock;

            /// <inheritdoc/>
            public int GoodPublishRequestCount
                => m_session.GoodPublishRequestCount;

            /// <inheritdoc/>
            public ValueTask<PublishResponse> PublishAsync(
                RequestHeader requestHeader,
                ArrayOf<SubscriptionAcknowledgement>
                    acknowledgements,
                CancellationToken ct = default)
            {
                return m_session.PublishAsync(
                    requestHeader,
                    acknowledgements,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<TransferSubscriptionsResponse>
                TransferSubscriptionsAsync(
                    RequestHeader? requestHeader,
                    ArrayOf<uint> subscriptionIds,
                    bool sendInitialValues,
                    CancellationToken ct = default)
            {
                return m_session.TransferSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<DeleteSubscriptionsResponse>
                DeleteSubscriptionsAsync(
                    RequestHeader? requestHeader,
                    ArrayOf<uint> subscriptionIds,
                    CancellationToken ct = default)
            {
                return m_session.DeleteSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    ct);
            }

            /// <inheritdoc/>
            public void OnKeepAlive(
                ServerState serverState,
                DateTime timestamp)
            {
                m_session.OnKeepAlive(serverState, timestamp);
            }

            /// <inheritdoc/>
            public void OnPublishError(
                ServiceResult error,
                uint subscriptionId,
                uint sequenceNumber)
            {
                PublishErrorEventHandler? callback =
                    m_session.m_PublishError;

                if (callback != null)
                {
                    try
                    {
                        callback(
                            m_session,
                            new PublishErrorEventArgs(
                                error,
                                subscriptionId,
                                sequenceNumber));
                    }
                    catch (Exception e)
                    {
                        m_session.m_logger.LogError(
                            e,
                            "Session: Unexpected error invoking " +
                            "PublishErrorCallback.");
                    }
                }
            }

            /// <inheritdoc/>
            public void OnPublishNotification(
                Subscription subscription,
                NotificationEventArgs notification)
            {
                NotificationEventHandler? publishEventHandler =
                    m_session.m_Publish;

                if (publishEventHandler != null)
                {
                    _ = Task.Run(
                        () => RaisePublishNotification(
                            publishEventHandler, notification));
                }
            }

            /// <inheritdoc/>
            public void OnKeepAliveError(ServiceResult error)
            {
                m_session.OnKeepAliveError(error);
            }

            /// <inheritdoc/>
            public void AsyncRequestStarted(
                Task task,
                Activity? activity,
                uint requestHandle,
                uint requestTypeId)
            {
                m_session.AsyncRequestStarted(
                    task,
                    activity,
                    requestHandle,
                    requestTypeId);
            }

            /// <inheritdoc/>
            public void AsyncRequestCompleted(
                Task task,
                uint requestHandle,
                uint requestTypeId)
            {
                m_session.AsyncRequestCompleted(
                    task,
                    requestHandle,
                    requestTypeId);
            }

            /// <inheritdoc/>
            public (List<SubscriptionAcknowledgement> toSend,
                List<SubscriptionAcknowledgement> updatedPending)
                PrepareAcknowledgementsToSend(
                    List<SubscriptionAcknowledgement>
                        currentAcknowledgements)
            {
                PublishSequenceNumbersToAcknowledgeEventHandler?
                    callback =
                        m_session
                            .m_PublishSequenceNumbersToAcknowledge;

                List<SubscriptionAcknowledgement>?
                    acknowledgementsToSend = null;

                if (callback != null)
                {
                    try
                    {
                        var deferred =
                            new List<SubscriptionAcknowledgement>();
                        callback(
                            m_session,
                            new PublishSequenceNumbersToAcknowledgeEventArgs(
                                currentAcknowledgements,
                                deferred));
                        acknowledgementsToSend =
                            currentAcknowledgements;
                        return (acknowledgementsToSend, deferred);
                    }
                    catch (Exception e)
                    {
                        m_session.m_logger.LogError(
                            e,
                            "Session: Unexpected error invoking " +
                            "PublishSequenceNumbersToAcknowledge" +
                            "EventArgs.");
                    }
                }

                // No callback or callback threw — send all acks.
                return (currentAcknowledgements, []);
            }

            /// <inheritdoc/>
            public ValueTask DeleteOrphanedSubscriptionAsync(
                uint subscriptionId)
            {
                return m_session.DeleteSubscriptionAsync(
                    subscriptionId);
            }

            private void RaisePublishNotification(
                NotificationEventHandler callback,
                NotificationEventArgs args)
            {
                try
                {
                    if (args.Subscription.Id != 0)
                    {
                        callback(m_session, args);
                    }
                }
                catch (Exception e)
                {
                    m_session.m_logger.LogError(
                        e,
                        "Session: Unexpected error while " +
                        "raising Notification event.");
                }
            }

            private readonly Session m_session;
        }
    }
}
