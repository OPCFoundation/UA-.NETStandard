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
        internal bool IsExecutingRequest => m_inServiceDispatch.Value;

        /// <summary>
        /// Gets or sets how long a drain keeps waiting once every request it is waiting for has
        /// passed its deadline. Requests that carry no deadline never expire on their own, so this
        /// is the only bound that applies to them.
        /// <para>
        /// The bound exists because a lifecycle operation holds its semaphore across the drain. A
        /// request that is never completed would otherwise wedge every later lifecycle operation
        /// for the lifetime of the server.
        /// </para>
        /// </summary>
        internal TimeSpan RequestDrainTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Marks the calling flow, and everything it invokes, as serving a Client request.
        /// <para>
        /// This is entered by the service dispatcher rather than by request validation, because an
        /// <see cref="AsyncLocal{T}"/> written inside an <c>async</c> method is visible only to
        /// that method and its callees. Setting it while validating a request would therefore
        /// never reach the service handler that awaited the validation, which is exactly the code
        /// that must be prevented from re-entering the lifecycle API.
        /// </para>
        /// <para>
        /// A caller that invokes a service method directly, without passing through the service
        /// dispatcher, is not covered. <see cref="RequestDrainTimeout"/> bounds that case instead
        /// of relying on the guard.
        /// </para>
        /// </summary>
        /// <returns>The scope to dispose once the request has been dispatched.</returns>
        internal IDisposable EnterServiceDispatchScope()
        {
            bool previous = m_inServiceDispatch.Value;
            m_inServiceDispatch.Value = true;
            return new ServiceDispatchScope(this, previous);
        }

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

            return new RequestValidationScope(this, validationId);
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
            return new RequestExecutionScope(this, context);
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
        /// <exception cref="TimeoutException">
        /// The requests being waited for did not complete within their deadlines plus
        /// <see cref="RequestDrainTimeout"/>.
        /// </exception>
        internal async ValueTask WaitForCurrentRequestsAsync(
            CancellationToken ct = default)
        {
            RequestDrain requestDrain;
            TimeSpan budget;
            lock (m_requestsLock)
            {
                if (m_requests.Count == 0 &&
                    m_activeValidationScopes.Count == 0)
                {
                    return;
                }

                List<uint> awaited = CollectRequestsToAwait(out budget);
                if (awaited.Count == 0 && m_activeValidationScopes.Count == 0)
                {
                    return;
                }

                requestDrain = new RequestDrain(
                    awaited,
                    m_activeValidationScopes);
                m_requestDrains.Add(requestDrain);
            }

            using CancellationTokenRegistration registration = ct.Register(
                static state => ((RequestDrain)state!).Cancel(),
                requestDrain);
            try
            {
                Task completion = requestDrain.Completion;
                Task expiry = m_timeProvider.Delay(budget, ct);
                if (await Task.WhenAny(completion, expiry).ConfigureAwait(false) != completion)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException(
                        $"Timed out after {budget} waiting for the requests that were in flight to " +
                        "complete. A request that never completes blocks every NodeManager " +
                        "lifecycle operation, so the operation was abandoned instead of waiting " +
                        "indefinitely.");
                }

                await completion.ConfigureAwait(false);
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
        /// Returns the requests a drain starting now has to wait for, and how long it may wait.
        /// <para>
        /// Requests are cancelled once their deadline passes, so the budget is the longest
        /// deadline still outstanding plus <see cref="RequestDrainTimeout"/>, which covers both the
        /// teardown that follows cancellation and requests that carry no deadline at all.
        /// </para>
        /// <para>
        /// A request that is still registered long after it was cancelled is not waited for. It is
        /// not going to complete, and waiting for it would make every later lifecycle operation
        /// pay the full budget before failing. It is left registered so that a handler which does
        /// eventually finish still reports completion normally.
        /// </para>
        /// </summary>
        /// <param name="budget">The longest the drain may wait.</param>
        /// <returns>The ids of the requests to wait for.</returns>
        private List<uint> CollectRequestsToAwait(out TimeSpan budget)
        {
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
            DateTime abandoned = now - RequestDrainTimeout;
            var longest = TimeSpan.Zero;
            var awaited = new List<uint>(m_requests.Count);

            foreach (OperationContext request in m_requests.Values)
            {
                if (request.OperationDeadline < DateTime.MaxValue)
                {
                    if (request.OperationDeadline < abandoned)
                    {
                        continue;
                    }

                    TimeSpan remaining = request.OperationDeadline - now;
                    if (remaining > longest)
                    {
                        longest = remaining;
                    }
                }

                awaited.Add(request.RequestId);
            }

            budget = longest + RequestDrainTimeout;
            return awaited;
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
        private readonly AsyncLocal<bool> m_inServiceDispatch = new();


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
        /// Tracks one executing request.
        /// </summary>
        private sealed class RequestExecutionScope : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestExecutionScope"/> class.
            /// </summary>
            /// <param name="requestManager">The owning request manager.</param>
            /// <param name="context">The context of the request being executed.</param>
            public RequestExecutionScope(
                RequestManager requestManager,
                OperationContext context)
            {
                m_requestManager = requestManager;
                m_context = context;
            }

            /// <summary>
            /// Reports the request as completed.
            /// </summary>
            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    m_requestManager.CompleteRequest(m_context);
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly OperationContext m_context;
            private bool m_disposed;
        }

        /// <summary>
        /// Marks the calling flow as serving a Client request for as long as it is dispatched.
        /// </summary>
        private sealed class ServiceDispatchScope : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ServiceDispatchScope"/> class.
            /// </summary>
            /// <param name="requestManager">The owning request manager.</param>
            /// <param name="previous">The value to restore on dispose.</param>
            public ServiceDispatchScope(RequestManager requestManager, bool previous)
            {
                m_requestManager = requestManager;
                m_previous = previous;
            }

            /// <summary>
            /// Restores the value the calling flow had before the request was dispatched.
            /// </summary>
            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    m_requestManager.m_inServiceDispatch.Value = m_previous;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly bool m_previous;
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
            public RequestValidationScope(
                RequestManager requestManager,
                long validationId)
            {
                m_requestManager = requestManager;
                m_validationId = validationId;
            }

            /// <summary>
            /// Releases the drain that waits for this scope.
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
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly long m_validationId;
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
