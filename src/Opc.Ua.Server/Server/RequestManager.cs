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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages requests from within the server.
    /// </summary>
    public class RequestManager : IDisposable
    {
        /// <summary>
        /// Initilizes the manager.
        /// </summary>
        public RequestManager(IServerInternal server)
            : this(server, null)
        {
        }

        /// <summary>
        /// Initializes the manager with an explicit <see cref="TimeProvider"/>
        /// so the request-expiry timer can be mocked in tests.
        /// </summary>
        /// <param name="server">The server context.</param>
        /// <param name="timeProvider">The time provider used to schedule the
        /// request-expiry timer and to evaluate request deadlines, or
        /// <c>null</c> to use the time provider exposed by the server (or
        /// <see cref="TimeProvider.System"/> as a fallback).</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/>
        /// is <c>null</c>.</exception>
        public RequestManager(IServerInternal server, TimeProvider? timeProvider)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<RequestManager>();
            m_requests = [];
            m_requestTimer = null;
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
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                List<OperationContext>? operations;
                List<RequestDrain>? requestDrains;
                lock (m_requestsLock)
                {
                    operations = [.. m_requests.Values];
                    m_requests.Clear();
                    requestDrains = [.. m_requestDrains];
                    m_requestDrains.Clear();
                }

                foreach (OperationContext operation in operations)
                {
                    operation.RequestLifetime.TryCancel(StatusCodes.BadSessionClosed);
                }
                foreach (RequestDrain requestDrain in requestDrains)
                {
                    requestDrain.Cancel();
                }

                m_requestTimer?.Dispose();
                m_requestTimer = null;
            }
        }

        /// <summary>
        /// Raised when the status of an outstanding request changes.
        /// </summary>
        public event RequestCancelledEventHandler RequestCancelled
        {
            add
            {
                lock (m_lock)
                {
                    m_RequestCancelled += value;
                }
            }
            remove
            {
                lock (m_lock)
                {
                    m_RequestCancelled -= value;
                }
            }
        }

        /// <summary>
        /// Called when a new request arrives.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// A different request with the same request id is already active.
        /// </exception>
        public void RequestReceived(OperationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (m_requestsLock)
            {
                if (m_requests.TryGetValue(
                    context.RequestId,
                    out OperationContext? existingContext))
                {
                    if (ReferenceEquals(existingContext, context))
                    {
                        return;
                    }
                    throw new InvalidOperationException(
                        $"A different request with id {context.RequestId} is already active.");
                }
                m_requests.Add(context.RequestId, context);

                if (context.OperationDeadline < DateTime.MaxValue && m_requestTimer == null)
                {
                    m_requestTimer = m_timeProvider.CreateTimer(
                        OnTimerExpired,
                        null,
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(1));
                }
            }
        }

        /// <summary>
        /// Called when a request completes (normally or abnormally).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        [Obsolete("Requests are completed by disposing the OperationContext, which owns the request scope.")]
        public void RequestCompleted(OperationContext context)
        {
            CompleteRequest(context);
        }

        /// <summary>
        /// Reports a request as completed and releases any drain waiting for it.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        private void CompleteRequest(OperationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            bool removed;
            lock (m_requestsLock)
            {
                // remove the request.
                removed = m_requests.TryGetValue(
                    context.RequestId,
                    out OperationContext? existingContext) &&
                    ReferenceEquals(existingContext, context) &&
                    m_requests.Remove(context.RequestId);
                if (removed)
                {
                    for (int ii = m_requestDrains.Count - 1; ii >= 0; ii--)
                    {
                        if (m_requestDrains[ii].Complete(context.RequestId))
                        {
                            m_requestDrains.RemoveAt(ii);
                        }
                    }
                }
            }
            if (removed)
            {
                context.RequestLifetime?.MarkCompleted();
            }
        }

        /// <summary>
        /// Gets whether the calling flow is serving a Client request. A NodeManager lifecycle
        /// operation started from inside a request would wait for its own request to drain, so
        /// the lifecycle API uses this to reject such calls instead of deadlocking.
        /// </summary>
        internal bool IsExecutingRequest => m_currentRequestId.Value.HasValue;

        /// <summary>
        /// Enters a validation scope, which covers the window in which a request is being
        /// validated but is not yet tracked as an executing request.
        /// <para>
        /// Validation creates the <see cref="OperationContext"/>, resolves the Session, and only
        /// then hands the request to <see cref="EnterRequestScope"/>. Without this scope a request
        /// that finished validating could start touching a NodeManager after
        /// <see cref="WaitForCurrentRequestsAsync"/> had already reported that nothing is in
        /// flight, and a NodeManager could be retired while that request was using it. The scope
        /// registers a token that the drain waits for, so the gap is covered from end to end.
        /// </para>
        /// <para>
        /// Disposing the scope completes any context that was registered but never promoted,
        /// which is what happens when validation fails.
        /// </para>
        /// </summary>
        /// <returns>The scope to dispose once validation has finished.</returns>
        internal IDisposable EnterValidationScope()
        {
            long validationId = Interlocked.Increment(
                ref m_lastValidationScopeId);
            lock (m_requestsLock)
            {
                m_activeValidationScopes.Add(validationId);
            }

            uint? previousRequestId = m_currentRequestId.Value;
            m_currentRequestId.Value = uint.MaxValue;
            return new RequestValidationScope(this, validationId, previousRequestId);
        }

        /// <summary>
        /// Enters a request scope, which tracks a validated request for as long as it executes.
        /// Disposing the scope reports the request as completed, which releases any lifecycle
        /// operation waiting in <see cref="WaitForCurrentRequestsAsync"/>.
        /// </summary>
        /// <param name="context">The context of the request being executed.</param>
        /// <returns>The scope to dispose once the request has finished.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        internal IDisposable EnterRequestScope(OperationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            RequestReceived(context);
            uint? previousRequestId = m_currentRequestId.Value;
            m_currentRequestId.Value = context.RequestId;
            return new RequestExecutionScope(
                this,
                context,
                previousRequestId);
        }


        /// <summary>
        /// Waits until every request that is currently executing or being validated has finished.
        /// A lifecycle operation calls this before it retires a NodeManager, so that no request
        /// can still be dispatching to it once it is torn down.
        /// <para>
        /// Only the requests present when the call starts are awaited. Requests that arrive later
        /// already observe the new routing table, so they never reach the retired NodeManager.
        /// </para>
        /// </summary>
        /// <param name="ct">The token used to stop waiting.</param>
        internal async ValueTask WaitForCurrentRequestsAsync(
            CancellationToken ct = default)
        {
            RequestDrain requestDrain;
            lock (m_requestsLock)
            {
                if (m_requests.Count == 0 &&
                    m_activeValidationScopes.Count == 0)
                {
                    return;
                }

                requestDrain = new RequestDrain(
                    m_requests.Keys,
                    m_activeValidationScopes);
                m_requestDrains.Add(requestDrain);
            }

            using CancellationTokenRegistration registration = ct.Register(
                static state => ((RequestDrain)state!).Cancel(),
                requestDrain);
            try
            {
                await requestDrain.Completion.ConfigureAwait(false);
            }
            finally
            {
                lock (m_requestsLock)
                {
                    m_requestDrains.Remove(requestDrain);
                }
            }
        }

        /// <summary>
        /// Called when the client wishes to cancel one or more requests.
        /// </summary>
        public void CancelRequests(NodeId sessionId, uint requestHandle, out uint cancelCount)
        {
            var cancelledRequests = new List<uint>();

            // flag requests as cancelled.
            lock (m_requestsLock)
            {
                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.SessionId == sessionId &&
                        request.ClientHandle == requestHandle)
                    {
                        request.RequestLifetime.TryCancel(StatusCodes.BadRequestCancelledByRequest);
                        cancelledRequests.Add(request.RequestId);

                        // report the AuditCancelEventType
                        m_server.ReportAuditCancelEvent(
                            request.SessionId,
                            requestHandle,
                            StatusCodes.Good,
                            m_logger);
                    }
                }
            }

            // return the number of requests found.
            cancelCount = (uint)cancelledRequests.Count;

            // raise notifications.
            lock (m_lock)
            {
                for (int ii = 0; ii < cancelledRequests.Count; ii++)
                {
                    if (m_RequestCancelled != null)
                    {
                        try
                        {
                            m_RequestCancelled(
                                this,
                                cancelledRequests[ii],
                                StatusCodes.BadRequestCancelledByRequest);
                        }
                        catch (Exception e)
                        {
                            m_logger.UnexpectedErrorReportingRequestCancelledEvent(e);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks for any expired requests and changes their status.
        /// </summary>
        private void OnTimerExpired(object? state)
        {
            var expiredRequests = new List<uint>();

            // flag requests as expired.
            lock (m_requestsLock)
            {
                // find the completed request.
                bool deadlineExists = false;

                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.OperationDeadline < m_timeProvider.GetUtcNow().UtcDateTime)
                    {
                        request.RequestLifetime.TryCancel(StatusCodes.BadTimeout);
                        expiredRequests.Add(request.RequestId);
                    }
                    else if (request.OperationDeadline < DateTime.MaxValue)
                    {
                        deadlineExists = true;
                    }
                }

                // check if the timer can be cancelled.
                if (m_requestTimer != null && !deadlineExists)
                {
                    m_requestTimer.Dispose();
                    m_requestTimer = null;
                }
            }

            // raise notifications.
            lock (m_lock)
            {
                for (int ii = 0; ii < expiredRequests.Count; ii++)
                {
                    if (m_RequestCancelled != null)
                    {
                        try
                        {
                            m_RequestCancelled(this, expiredRequests[ii], StatusCodes.BadTimeout);
                        }
                        catch (Exception e)
                        {
                            m_logger.UnexpectedErrorReportingRequestCancelledEvent(e);
                        }
                    }
                }
            }
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly AsyncLocal<uint?> m_currentRequestId = new();


        private readonly Dictionary<uint, OperationContext> m_requests;
        private readonly List<RequestDrain> m_requestDrains = [];
        private readonly Lock m_requestsLock = new();
        private readonly HashSet<long> m_activeValidationScopes = [];
        private long m_lastValidationScopeId;
        private ITimer? m_requestTimer;
        private event RequestCancelledEventHandler? m_RequestCancelled;

        /// <summary>
        /// Waits for a fixed set of executing requests and validation scopes to finish. The set is
        /// captured when the drain is created, so requests that start afterwards do not extend it.
        /// </summary>
        private sealed class RequestDrain
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestDrain"/> class.
            /// </summary>
            /// <param name="requestIds">The requests executing when the drain started.</param>
            /// <param name="validationIds">The validation scopes open when the drain started.</param>
            public RequestDrain(
                IEnumerable<uint> requestIds,
                IEnumerable<long> validationIds)
            {
                m_requestIds = [.. requestIds];
                m_validationIds = [.. validationIds];
            }

            /// <summary>
            /// Gets the task that completes once everything the drain waits for has finished.
            /// </summary>
            public Task Completion => m_completion.Task;

            /// <summary>
            /// Reports that a request finished.
            /// </summary>
            /// <param name="requestId">The request that finished.</param>
            /// <returns><c>true</c> when nothing is left to wait for.</returns>
            public bool Complete(uint requestId)
            {
                m_requestIds.Remove(requestId);
                return TryComplete();
            }

            /// <summary>
            /// Reports that a validation scope closed.
            /// </summary>
            /// <param name="validationId">The validation scope that closed.</param>
            /// <returns><c>true</c> when nothing is left to wait for.</returns>
            public bool CompleteValidation(long validationId)
            {
                m_validationIds.Remove(validationId);
                return TryComplete();
            }

            /// <summary>
            /// Stops the drain because the caller cancelled the wait.
            /// </summary>
            public void Cancel()
            {
                m_completion.TrySetCanceled();
            }

            private readonly HashSet<uint> m_requestIds;
            private readonly HashSet<long> m_validationIds;

            private readonly TaskCompletionSource<bool> m_completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Completes the drain once no request and no validation scope is left.
            /// </summary>
            private bool TryComplete()
            {
                if (m_requestIds.Count == 0 &&
                    m_validationIds.Count == 0)
                {
                    m_completion.TrySetResult(true);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Tracks one executing request and restores the ambient request id of the calling flow
        /// when it is disposed.
        /// </summary>
        private sealed class RequestExecutionScope : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestExecutionScope"/> class.
            /// </summary>
            /// <param name="requestManager">The owning request manager.</param>
            /// <param name="context">The context of the request being executed.</param>
            /// <param name="previousRequestId">The request id to restore on dispose.</param>
            public RequestExecutionScope(
                RequestManager requestManager,
                OperationContext context,
                uint? previousRequestId)
            {
                m_requestManager = requestManager;
                m_context = context;
                m_previousRequestId = previousRequestId;
            }

            /// <summary>
            /// Reports the request as completed and restores the previous ambient request id.
            /// </summary>
            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    m_requestManager.CompleteRequest(m_context);
                    m_requestManager.m_currentRequestId.Value =
                        m_previousRequestId;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly OperationContext m_context;
            private readonly uint? m_previousRequestId;
            private bool m_disposed;
        }

        /// <summary>
        /// Tracks the window in which a request is being validated, so that a lifecycle operation
        /// cannot retire a NodeManager between the moment validation starts and the moment the
        /// validated request starts executing.
        /// <para>
        /// The scope carries an id of its own rather than the request id, because it opens before
        /// the <see cref="OperationContext"/> exists and therefore before a request id has been
        /// assigned. It tracks no contexts: a validated request is handed over explicitly, by
        /// attaching its execution scope to the context that <c>ValidateRequestAsync</c> returns.
        /// </para>
        /// </summary>
        private sealed class RequestValidationScope : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestValidationScope"/> class.
            /// </summary>
            /// <param name="requestManager">The owning request manager.</param>
            /// <param name="validationId">The id the drain waits for.</param>
            /// <param name="previousRequestId">The request id to restore on dispose.</param>
            public RequestValidationScope(
                RequestManager requestManager,
                long validationId,
                uint? previousRequestId)
            {
                m_requestManager = requestManager;
                m_validationId = validationId;
                m_previousRequestId = previousRequestId;
            }

            /// <summary>
            /// Releases the drain that waits for this scope and restores the ambient state of the
            /// calling flow.
            /// </summary>
            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    lock (m_requestManager.m_requestsLock)
                    {
                        m_requestManager.m_activeValidationScopes.Remove(
                            m_validationId);
                        for (int ii =
                            m_requestManager.m_requestDrains.Count - 1;
                            ii >= 0;
                            ii--)
                        {
                            if (m_requestManager.m_requestDrains[ii]
                                .CompleteValidation(m_validationId))
                            {
                                m_requestManager.m_requestDrains.RemoveAt(ii);
                            }
                        }
                    }
                    m_requestManager.m_currentRequestId.Value =
                        m_previousRequestId;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly long m_validationId;
            private readonly uint? m_previousRequestId;
            private bool m_disposed;
        }
    }

    /// <summary>
    /// Called when a request is cancelled.
    /// </summary>
    public delegate void RequestCancelledEventHandler(
        RequestManager source,
        uint requestId,
        StatusCode statusCode);

    /// <summary>
    /// Source-generated log messages for RequestManager.
    /// </summary>
    internal static partial class RequestManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.RequestManager + 0, Level = LogLevel.Error,
            Message = "Unexpected error reporting RequestCancelled event.")]
        public static partial void UnexpectedErrorReportingRequestCancelledEvent(this ILogger logger, Exception ex);
    }

}
