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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Context provided by the session to the
    /// <see cref="ISubscriptionEngine"/>. Decouples the engine from the
    /// Session implementation, providing only the services and state the
    /// engine needs to operate the publish loop.
    /// </summary>
    public interface ISubscriptionEngineContext
    {
        /// <summary>
        /// The current session ID. Used to validate publish responses
        /// belong to the current session.
        /// </summary>
        NodeId SessionId { get; }

        /// <summary>
        /// Whether the session is currently in a reconnecting state.
        /// Publish responses received during reconnect should be
        /// discarded or deferred.
        /// </summary>
        bool Reconnecting { get; }

        /// <summary>
        /// Whether the session is currently connected to the server.
        /// </summary>
        bool Connected { get; }

        /// <summary>
        /// Whether the session is in the process of closing.
        /// </summary>
        bool Closing { get; }

        /// <summary>
        /// Whether the session has been disposed. The engine should
        /// stop processing if this is true.
        /// </summary>
        bool Disposed { get; }

        /// <summary>
        /// Whether subscriptions should be deleted from the server
        /// when the session is closed.
        /// </summary>
        bool DeleteSubscriptionsOnClose { get; }

        /// <summary>
        /// The operation timeout for publish requests in milliseconds.
        /// </summary>
        int OperationTimeout { get; }

        /// <summary>
        /// The current server state as known by the session.
        /// </summary>
        ServerState ServerState { get; }

        /// <summary>
        /// The diagnostics mask to include in publish request headers.
        /// </summary>
        DiagnosticsMasks ReturnDiagnostics { get; }

        /// <summary>
        /// Telemetry context for logging and metrics.
        /// </summary>
        ITelemetryContext Telemetry { get; }

#if OPCUA_V1_CLIENT
        /// <summary>
        /// The list of subscriptions managed by this session.
        /// The engine uses this to look up target subscriptions
        /// from publish responses.
        /// </summary>
        IReadOnlyList<Subscription> Subscriptions { get; }
#endif

        /// <summary>
        /// Send a Publish request to the server.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="acknowledgements">Subscription acknowledgements
        /// to send with this request.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The publish response.</returns>
        ValueTask<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            ArrayOf<SubscriptionAcknowledgement> acknowledgements,
            CancellationToken ct = default);

        /// <summary>
        /// Transfer subscriptions to this session.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionIds">IDs of subscriptions to
        /// transfer.</param>
        /// <param name="sendInitialValues">Whether to send initial
        /// values for monitored items.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The transfer response.</returns>
        ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct = default);

        /// <summary>
        /// Delete subscriptions on the server.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionIds">IDs of subscriptions to
        /// delete.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The delete response.</returns>
        ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default);

        /// <summary>
        /// Raise keep-alive notification on the session, indicating
        /// the server is alive and responsive.
        /// </summary>
        /// <param name="serverState">Current server state.</param>
        /// <param name="timestamp">Timestamp of the response.</param>
        void OnKeepAlive(ServerState serverState, DateTime timestamp);

        /// <summary>
        /// Raise a publish error event on the session.
        /// </summary>
        /// <param name="error">The error that occurred.</param>
        /// <param name="subscriptionId">The subscription ID
        /// associated with the error.</param>
        /// <param name="sequenceNumber">The sequence number of the
        /// failed notification.</param>
        void OnPublishError(
            ServiceResult error,
            uint subscriptionId,
            uint sequenceNumber);

#if OPCUA_V1_CLIENT
        /// <summary>
        /// Raise a publish notification event on the session.
        /// </summary>
        /// <param name="subscription">The subscription that received
        /// the notification.</param>
        /// <param name="notification">The notification event args.</param>
        void OnPublishNotification(
            Subscription subscription,
            NotificationEventArgs notification);
#endif

        /// <summary>
        /// Notify the session that a keep-alive error was detected,
        /// which may trigger a reconnect.
        /// </summary>
        /// <param name="error">The keep-alive error.</param>
        void OnKeepAliveError(ServiceResult error);

        /// <summary>
        /// Track the start of an async publish request for diagnostics.
        /// </summary>
        /// <param name="task">The task representing the request.</param>
        /// <param name="activity">The diagnostics activity, if any.</param>
        /// <param name="requestHandle">The request handle.</param>
        /// <param name="requestTypeId">The request type identifier.</param>
        void AsyncRequestStarted(
            Task task,
            Activity? activity,
            uint requestHandle,
            uint requestTypeId);

        /// <summary>
        /// Track the completion of an async publish request.
        /// </summary>
        /// <param name="task">The completed task.</param>
        /// <param name="requestHandle">The request handle.</param>
        /// <param name="requestTypeId">The request type identifier.</param>
        void AsyncRequestCompleted(
            Task task,
            uint requestHandle,
            uint requestTypeId);

        /// <summary>
        /// The semaphore used to gate publish completions during
        /// reconnect or transfer operations.
        /// </summary>
        SemaphoreSlim ReconnectLock { get; }

        /// <summary>
        /// Prepare acknowledgements to send with the next publish
        /// request. Invokes the PublishSequenceNumbersToAcknowledge
        /// event on the session, allowing callers to defer specific
        /// acknowledgements.
        /// </summary>
        /// <param name="currentAcknowledgements">The current list of
        /// pending acknowledgements.</param>
        /// <returns>A tuple where toSend is the list to include in the
        /// publish request and updatedPending replaces the pending
        /// list.</returns>
        (List<SubscriptionAcknowledgement> toSend,
            List<SubscriptionAcknowledgement> updatedPending)
            PrepareAcknowledgementsToSend(
                List<SubscriptionAcknowledgement> currentAcknowledgements);

        /// <summary>
        /// Delete an orphaned subscription that was received in a
        /// publish response but is not in the session's subscription
        /// list.
        /// </summary>
        /// <param name="subscriptionId">The subscription ID to
        /// delete.</param>
        ValueTask DeleteOrphanedSubscriptionAsync(uint subscriptionId);

        /// <summary>
        /// The number of good outstanding publish requests that
        /// are not defunct.
        /// </summary>
        int GoodPublishRequestCount { get; }

        /// <summary>
        /// Subscription service set client methods exposed by the
        /// underlying session. Used by V2 engines to drive the full
        /// subscription lifecycle (Create / Modify / SetPublishingMode
        /// / Republish / Delete).
        /// </summary>
        ISubscriptionServiceSetClientMethods SubscriptionServiceSet { get; }

        /// <summary>
        /// Monitored item service set client methods exposed by the
        /// underlying session. Used by V2 engines to drive monitored
        /// item lifecycle (Create / Modify / Delete /
        /// SetMonitoringMode / SetTriggering).
        /// </summary>
        IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet { get; }

        /// <summary>
        /// Method service set client methods exposed by the underlying
        /// session. Used by V2 engines to invoke server methods.
        /// </summary>
        IMethodServiceSetClientMethods MethodServiceSet { get; }
    }
}
