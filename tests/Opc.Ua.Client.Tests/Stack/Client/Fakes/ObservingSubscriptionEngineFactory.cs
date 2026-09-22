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
 *
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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Creates real subscription engines limited to one concurrent publish request and observes calls into
    /// the real session publish path, including requests that have not yet reached the transport.
    /// </summary>
    /// <param name="timeProvider">The clock used by the engines for publishing and retry timers.</param>
    internal sealed class ObservingSubscriptionEngineFactory(TimeProvider timeProvider) : ISubscriptionEngineFactory
    {
        /// <summary>
        /// Gets the most recently created real subscription engine, available after <see cref="Create"/>.
        /// </summary>
        public DefaultSubscriptionEngine Engine { get; private set; } = null!;

        /// <summary>
        /// Gets the number of engines created so tests can detect unwanted engine replacement during recovery.
        /// </summary>
        public int CreateCount { get; private set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Wraps the supplied context without replacing its publish operations and limits publish concurrency to one.
        /// </remarks>
        public ISubscriptionEngine Create(ISubscriptionEngineContext context)
        {
            Engine = new DefaultSubscriptionEngine(new ForwardingContext(context, m_attempts.Writer), timeProvider)
            {
                MinPublishRequestCount = 1,
                MaxPublishRequestCount = 1
            };
            CreateCount++;
            return Engine;
        }

        /// <summary>
        /// Dequeues the next attempt after the real context has been invoked, even if the request is still
        /// waiting for channel readiness.
        /// </summary>
        /// <param name="ct">The token used to cancel waiting for an observation, not the observed publish.</param>
        public ValueTask<PublishAttempt> NextAttemptAsync(CancellationToken ct)
        {
            return m_attempts.Reader.ReadAsync(ct);
        }

        /// <summary>
        /// Captures a real publish invocation and its completion task independently of transport receipt.
        /// </summary>
        /// <param name="RequestHandle">The request handle recorded after invoking the real context.</param>
        /// <param name="Acknowledgements">The acknowledgements passed to the context for this attempt.</param>
        /// <param name="Operation">The real publish task, which may still be waiting for channel readiness.</param>
        /// <param name="CancellationToken">The token passed to the real publish operation.</param>
        internal sealed record PublishAttempt(
            uint RequestHandle,
            ArrayOf<SubscriptionAcknowledgement> Acknowledgements,
            Task<PublishResponse> Operation,
            CancellationToken CancellationToken);

        /// <summary>
        /// Delegates engine services and callbacks to the real session context while recording publish operations
        /// after they have entered that context.
        /// </summary>
        /// <param name="context">The real session context whose behavior is preserved.</param>
        /// <param name="attempts">The queue that receives publish observations.</param>
        private sealed class ForwardingContext(
            ISubscriptionEngineContext context,
            ChannelWriter<PublishAttempt> attempts) : ISubscriptionEngineContext
        {
            /// <inheritdoc/>
            public NodeId SessionId => context.SessionId;

            /// <inheritdoc/>
            public bool Reconnecting => context.Reconnecting;

            /// <inheritdoc/>
            public bool Connected => context.Connected;

            /// <inheritdoc/>
            public bool Closing => context.Closing;

            /// <inheritdoc/>
            public bool KeepAliveStopped => context.KeepAliveStopped;

            /// <inheritdoc/>
            public DateTime LastKeepAliveTime => context.LastKeepAliveTime;

            /// <inheritdoc/>
            public bool Disposed => context.Disposed;

            /// <inheritdoc/>
            public bool DeleteSubscriptionsOnClose => context.DeleteSubscriptionsOnClose;

            /// <inheritdoc/>
            public int OperationTimeout => context.OperationTimeout;

            /// <inheritdoc/>
            public ServerState ServerState => context.ServerState;

            /// <inheritdoc/>
            public DiagnosticsMasks ReturnDiagnostics => context.ReturnDiagnostics;

            /// <inheritdoc/>
            public ITelemetryContext Telemetry => context.Telemetry;

            /// <inheritdoc/>
            public IReadOnlyList<Subscription> Subscriptions => context.Subscriptions;

            /// <inheritdoc/>
            public SemaphoreSlim ReconnectLock => context.ReconnectLock;

            /// <inheritdoc/>
            public int GoodPublishRequestCount => context.GoodPublishRequestCount;

            /// <inheritdoc/>
            public ISubscriptionServiceSetClientMethods SubscriptionServiceSet => context.SubscriptionServiceSet;

            /// <inheritdoc/>
            public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet => context.MonitoredItemServiceSet;

            /// <inheritdoc/>
            public IMethodServiceSetClientMethods MethodServiceSet => context.MethodServiceSet;

            /// <inheritdoc/>
            /// <remarks>
            /// Records the actual operation only after delegation has entered the session/channel path.
            /// </remarks>
            public ValueTask<PublishResponse> PublishAsync(
                RequestHeader requestHeader,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                CancellationToken ct = default)
            {
                // Enter the actual Session/ChannelEntry path before publishing the observation.
                Task<PublishResponse> operation = context.PublishAsync(requestHeader, acknowledgements, ct).AsTask();
                if (!attempts.TryWrite(new PublishAttempt(
                    requestHeader.RequestHandle, acknowledgements, operation, ct)))
                {
                    throw new InvalidOperationException("The publish observation queue was unexpectedly closed.");
                }
                return new ValueTask<PublishResponse>(operation);
            }

            /// <inheritdoc/>
            public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                bool sendInitialValues,
                CancellationToken ct = default)
            {
                return context.TransferSubscriptionsAsync(requestHeader, subscriptionIds, sendInitialValues, ct);
            }

            /// <inheritdoc/>
            public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct = default)
            {
                return context.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct);
            }

            /// <inheritdoc/>
            public void OnKeepAlive(ServerState serverState, DateTime timestamp)
            {
                context.OnKeepAlive(serverState, timestamp);
            }

            /// <inheritdoc/>
            public void OnPublishError(ServiceResult error, uint subscriptionId, uint sequenceNumber)
            {
                context.OnPublishError(error, subscriptionId, sequenceNumber);
            }

            /// <inheritdoc/>
            public void OnPublishNotification(Subscription subscription, NotificationEventArgs notification)
            {
                context.OnPublishNotification(subscription, notification);
            }

            /// <inheritdoc/>
            public void OnKeepAliveError(ServiceResult error)
            {
                context.OnKeepAliveError(error);
            }

            /// <inheritdoc/>
            public void AsyncRequestStarted(Task task, Activity? activity, uint requestHandle, uint requestTypeId)
            {
                context.AsyncRequestStarted(task, activity, requestHandle, requestTypeId);
            }

            /// <inheritdoc/>
            public void AsyncRequestCompleted(Task task, uint requestHandle, uint requestTypeId)
            {
                context.AsyncRequestCompleted(task, requestHandle, requestTypeId);
            }

            /// <inheritdoc/>
            public (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending)
                PrepareAcknowledgementsToSend(List<SubscriptionAcknowledgement> currentAcknowledgements)
            {
                return context.PrepareAcknowledgementsToSend(currentAcknowledgements);
            }

            /// <inheritdoc/>
            public ValueTask DeleteOrphanedSubscriptionAsync(uint subscriptionId)
            {
                return context.DeleteOrphanedSubscriptionAsync(subscriptionId);
            }

            /// <inheritdoc/>
            public bool TryDispatchToSessionSubscription(
                uint subscriptionId,
                NotificationMessage message,
                ArrayOf<uint> availableSequenceNumbers,
                ArrayOf<string> stringTable,
                bool moreNotifications)
            {
                return context.TryDispatchToSessionSubscription(
                    subscriptionId, message, availableSequenceNumbers, stringTable, moreNotifications);
            }
        }

        private readonly Channel<PublishAttempt> m_attempts = Channel.CreateUnbounded<PublishAttempt>();
    }
}
