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
    internal sealed class ObservingSubscriptionEngineFactory(TimeProvider timeProvider) : ISubscriptionEngineFactory
    {
        public DefaultSubscriptionEngine Engine { get; private set; } = null!;

        public int CreateCount { get; private set; }

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

        public ValueTask<PublishAttempt> NextAttemptAsync(CancellationToken ct)
        {
            return m_attempts.Reader.ReadAsync(ct);
        }

        internal sealed record PublishAttempt(
            uint RequestHandle,
            ArrayOf<SubscriptionAcknowledgement> Acknowledgements,
            Task<PublishResponse> Operation,
            CancellationToken CancellationToken);

        private sealed class ForwardingContext(
            ISubscriptionEngineContext context,
            ChannelWriter<PublishAttempt> attempts) : ISubscriptionEngineContext
        {
            public NodeId SessionId => context.SessionId;

            public bool Reconnecting => context.Reconnecting;

            public bool Connected => context.Connected;

            public bool Closing => context.Closing;

            public bool KeepAliveStopped => context.KeepAliveStopped;

            public DateTime LastKeepAliveTime => context.LastKeepAliveTime;

            public bool Disposed => context.Disposed;

            public bool DeleteSubscriptionsOnClose => context.DeleteSubscriptionsOnClose;

            public int OperationTimeout => context.OperationTimeout;

            public ServerState ServerState => context.ServerState;

            public DiagnosticsMasks ReturnDiagnostics => context.ReturnDiagnostics;

            public ITelemetryContext Telemetry => context.Telemetry;

            public IReadOnlyList<Subscription> Subscriptions => context.Subscriptions;

            public SemaphoreSlim ReconnectLock => context.ReconnectLock;

            public int GoodPublishRequestCount => context.GoodPublishRequestCount;

            public ISubscriptionServiceSetClientMethods SubscriptionServiceSet => context.SubscriptionServiceSet;

            public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet => context.MonitoredItemServiceSet;

            public IMethodServiceSetClientMethods MethodServiceSet => context.MethodServiceSet;

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

            public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                bool sendInitialValues,
                CancellationToken ct = default)
            {
                return context.TransferSubscriptionsAsync(requestHeader, subscriptionIds, sendInitialValues, ct);
            }

            public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct = default)
            {
                return context.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct);
            }

            public void OnKeepAlive(ServerState serverState, DateTime timestamp)
            {
                context.OnKeepAlive(serverState, timestamp);
            }

            public void OnPublishError(ServiceResult error, uint subscriptionId, uint sequenceNumber)
            {
                context.OnPublishError(error, subscriptionId, sequenceNumber);
            }

            public void OnPublishNotification(Subscription subscription, NotificationEventArgs notification)
            {
                context.OnPublishNotification(subscription, notification);
            }

            public void OnKeepAliveError(ServiceResult error)
            {
                context.OnKeepAliveError(error);
            }

            public void AsyncRequestStarted(Task task, Activity? activity, uint requestHandle, uint requestTypeId)
            {
                context.AsyncRequestStarted(task, activity, requestHandle, requestTypeId);
            }

            public void AsyncRequestCompleted(Task task, uint requestHandle, uint requestTypeId)
            {
                context.AsyncRequestCompleted(task, requestHandle, requestTypeId);
            }

            public (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending)
                PrepareAcknowledgementsToSend(List<SubscriptionAcknowledgement> currentAcknowledgements)
            {
                return context.PrepareAcknowledgementsToSend(currentAcknowledgements);
            }

            public ValueTask DeleteOrphanedSubscriptionAsync(uint subscriptionId)
            {
                return context.DeleteOrphanedSubscriptionAsync(subscriptionId);
            }

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
