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
                    if (m_disposed)
                    {
                        return;
                    }

                    m_disposed = true;
                    operations = [.. m_requests.Values];
                    m_requests.Clear();
                    requestDrains = [.. m_requestDrains];
                    m_requestDrains.Clear();
                    m_activeValidationScopes.Clear();
                    m_lifecycleExtension?.DisposeLocked();
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
        /// <exception cref="ServiceResultException">
        /// A registered request lifecycle extension closed request admission because the server
        /// is shutting down.
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
                        m_currentServiceDispatchScope.Value?
                            .RegisterRequest(context.RequestId);
                        return;
                    }
                    throw new InvalidOperationException(
                        $"A different request with id {context.RequestId} is already active.");
                }
                m_requests.Add(context.RequestId, context);
                m_currentServiceDispatchScope.Value?.RegisterRequest(context.RequestId);

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
        /// Reports whether the supplied context belongs to a request that is executing right now.
        /// A NodeManager lifecycle operation started from inside such a request would wait for its
        /// own request to drain, so the lifecycle API uses this to reject the call instead of
        /// deadlocking.
        /// <para>
        /// The context is supplied by the caller rather than discovered from ambient state, so the
        /// dependency is visible at the call site. A context that was never registered as a
        /// request - an internal operation, or a request that has already completed - is not
        /// executing and is therefore allowed through.
        /// </para>
        /// <para>
        /// A caller inside a request that supplies no context is not covered.
        /// <see cref="RequestDrainTimeout"/> bounds that case instead of relying on the guard.
        /// </para>
        /// </summary>
        /// <param name="context">The context the caller is operating under, or <c>null</c> when
        /// the caller is not operating on behalf of a request.</param>
        /// <returns><c>true</c> when the context is a request that is currently executing.</returns>
        internal bool IsExecutingRequest(IOperationContext? context)
        {
            if (context is not OperationContext operation)
            {
                return false;
            }

            lock (m_requestsLock)
            {
                return m_requests.TryGetValue(
                    operation.RequestId,
                    out OperationContext? existingContext) &&
                    ReferenceEquals(existingContext, operation);
            }
        }

        /// <summary>
        /// Gets the optional request lifecycle extension registered for server shutdown and
        /// NodeManager lifecycle coordination.
        /// </summary>
        internal RequestManagerLifecycleExtension? LifecycleExtension
        {
            get
            {
                lock (m_requestsLock)
                {
                    return m_lifecycleExtension;
                }
            }
        }

        /// <summary>
        /// Registers the optional request lifecycle extension. Calling this method more than once
        /// returns the extension that is already registered.
        /// </summary>
        /// <returns>The registered request lifecycle extension.</returns>
        /// <exception cref="ObjectDisposedException">
        /// The request manager has already been disposed.
        /// </exception>
        internal RequestManagerLifecycleExtension RegisterLifecycleExtension()
        {
            lock (m_requestsLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(RequestManager));
                }

                m_lifecycleExtension ??= new RequestManagerLifecycleExtension(this);
                return m_lifecycleExtension;
            }
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
        /// <exception cref="ServiceResultException">
        /// Request admission has been closed because the server is shutting down.
        /// </exception>
        internal IDisposable EnterValidationScope()
        {
            long validationId = Interlocked.Increment(
                ref m_lastValidationScopeId);
            lock (m_requestsLock)
            {
                m_activeValidationScopes.Add(validationId);
            }

            RequestValidationScope? previousScope = m_currentValidationScope.Value;
            var scope = new RequestValidationScope(
                this,
                validationId,
                previousScope);
            m_currentValidationScope.Value = scope;
            return scope;
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
            return new RequestExecutionScope(this, context, previousRequestId);
        }

        /// <summary>
        /// Enters a service dispatch scope for the current request-processing flow. The scope is
        /// opened at the top of request dispatch, before validation assigns a request id, and
        /// carries that id forward once <see cref="RequestReceived"/> registers the request. A
        /// NodeManager lifecycle operation started from inside a request callback reads the id
        /// through <see cref="GetCurrentRequestIdForLifecycleExtension"/> so it can exclude its own
        /// request from the drain it performs and avoid waiting on itself.
        /// <para>
        /// The scope is needed because the request id is assigned by validation, which runs in a
        /// deeper asynchronous flow whose ambient value never propagates back to the dispatch flow.
        /// The scope is a reference captured in the dispatch flow, so the id that validation stamps
        /// onto it is observed by the callback that runs the lifecycle operation.
        /// </para>
        /// </summary>
        /// <returns>The scope to dispose once the request has been dispatched.</returns>
        internal IDisposable EnterServiceDispatchScope()
        {
            ServiceDispatchScope? previousScope = m_currentServiceDispatchScope.Value;
            var scope = new ServiceDispatchScope(this, previousScope);
            m_currentServiceDispatchScope.Value = scope;
            return scope;
        }

        /// <summary>
        /// Waits until every request that is currently executing or being validated has finished.
        /// A lifecycle operation calls this before it retires a NodeManager, so that no request
        /// can still be dispatching to it once it is torn down.
        /// <para>
        /// For a lifecycle drain, only the requests present when the call starts are awaited.
        /// Requests that arrive later already observe the new routing table, so they never reach
        /// the retired NodeManager. Once shutdown closes request admission, the drain repeats its
        /// snapshot until every request admitted before closure has transitioned out of validation
        /// and completed.
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
            RequestDrain? requestDrain;
            TimeSpan budget;
            lock (m_requestsLock)
            {
                requestDrain = CreateRequestDrainLocked(out budget);
            }

            if (requestDrain is null)
            {
                return;
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

        private RequestDrain? CreateRequestDrainLocked(out TimeSpan budget)
        {
            List<uint> requestIds = CollectRequestsToAwait(out budget);

            if (requestIds.Count == 0 &&
                m_activeValidationScopes.Count == 0)
            {
                return null;
            }

            var requestDrain = new RequestDrain(
                requestIds,
                m_activeValidationScopes);
            m_requestDrains.Add(requestDrain);
            return requestDrain;
        }

        internal uint? GetCurrentRequestIdForLifecycleExtension()
        {
            return m_currentServiceDispatchScope.Value?.RequestId ??
                m_currentRequestId.Value;
        }

        internal void EnterLifecycleWaiter(
            RequestManagerLifecycleExtension extension,
            uint requestId)
        {
            lock (m_requestsLock)
            {
                EnsureLifecycleExtensionRegisteredLocked(extension);
                extension.EnterWaiterLocked(requestId, m_requests);
            }
        }

        internal void MarkLifecycleWaiterWaiting(
            RequestManagerLifecycleExtension extension,
            uint requestId)
        {
            lock (m_requestsLock)
            {
                EnsureLifecycleExtensionRegisteredLocked(extension);
                extension.MarkWaiterWaitingLocked(requestId, m_requestDrains);
            }
        }

        internal void ExitLifecycleWaiter(
            RequestManagerLifecycleExtension extension,
            uint requestId,
            bool waiting)
        {
            lock (m_requestsLock)
            {
                if (ReferenceEquals(m_lifecycleExtension, extension))
                {
                    extension.ExitWaiterLocked(requestId, waiting);
                }
            }
        }

        private void EnsureLifecycleExtensionRegisteredLocked(
            RequestManagerLifecycleExtension extension)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(RequestManager));
            }
            if (!ReferenceEquals(m_lifecycleExtension, extension))
            {
                throw new InvalidOperationException(
                    "The request lifecycle extension is not registered with this request manager.");
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
        /// <para>
        /// Publish requests are not waited for: they do not dispatch into NodeManagers and are
        /// parked until a notification or keep-alive is due.
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
                if (m_lifecycleExtension?.ShouldExcludeRequestLocked(request.RequestId) == true)
                {
                    continue;
                }

                // A Publish never dispatches into a NodeManager, but stays parked until the next
                // notification or keep-alive, so waiting for it would stall every drain.
                if (request.RequestType == RequestType.Publish)
                {
                    continue;
                }

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
            cancelCount = CancelMatchingRequests(sessionId, requestHandle, DateTime.MinValue);

            // report the AuditCancelEventType once per Cancel call (OPC 10000-5 6.4.11).
            m_server.ReportAuditCancelEvent(sessionId, requestHandle, StatusCodes.Good, m_logger);
        }

        /// <summary>
        /// Called when the client wishes to cancel one or more requests through the Cancel
        /// service. The AuditCancelEvent is reported once for the call and carries the
        /// ClientAuditEntryId and ClientUserId of the Cancel request (OPC 10000-5 6.4.3).
        /// </summary>
        /// <param name="context">The operation context of the Cancel request.</param>
        /// <param name="requestHandle">The requestHandle parameter of the Cancel call.</param>
        /// <param name="cancelCount">The number of cancelled requests.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void CancelRequests(OperationContext context, uint requestHandle, out uint cancelCount)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            cancelCount = CancelMatchingRequests(context.SessionId, requestHandle, context.ClientTimestamp);

            m_server.ReportAuditCancelEvent(context, requestHandle, StatusCodes.Good, m_logger);
        }

        /// <summary>
        /// Aborts every outstanding request of a Session that is being closed, so that they
        /// complete with <paramref name="statusCode"/> instead of running on against a Session
        /// that is torn down underneath them (OPC 10000-4 5.7.2.1).
        /// </summary>
        /// <param name="sessionId">The session being closed.</param>
        /// <param name="excludedRequestId">A request that must not be aborted (the CloseSession
        /// request driving the close), or 0.</param>
        /// <param name="statusCode">The status the aborted requests complete with.</param>
        /// <returns>The number of aborted requests.</returns>
        public uint CancelSessionRequests(NodeId sessionId, uint excludedRequestId, StatusCode statusCode)
        {
            if (sessionId.IsNull)
            {
                return 0;
            }

            // Collect under the lock, cancel after releasing it: cancelling runs the request's
            // cancellation callbacks and continuations inline, which may complete (and so
            // unregister) the request while m_requests would otherwise be enumerated.
            var matchingRequests = new List<OperationContext>();
            lock (m_requestsLock)
            {
                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.RequestId != excludedRequestId &&
                        request.SessionId == sessionId)
                    {
                        matchingRequests.Add(request);
                    }
                }

                // the Session's queued requests can no longer be admitted either.
                RemovePendingCancelsLocked(sessionId);
            }

            var cancelledRequests = new List<uint>(matchingRequests.Count);
            foreach (OperationContext request in matchingRequests)
            {
                if (TryCancelRequest(request, statusCode))
                {
                    cancelledRequests.Add(request.RequestId);
                }
            }

            RaiseRequestCancelled(cancelledRequests, statusCode);
            return (uint)cancelledRequests.Count;
        }

        /// <summary>
        /// Cancels a request without letting a failing cancellation callback escape to the
        /// caller, which is in the middle of cancelling other requests (or closing a Session).
        /// </summary>
        /// <returns><c>true</c> when this call cancelled the request.</returns>
        internal bool TryCancelRequest(OperationContext request, StatusCode statusCode)
        {
            try
            {
                return request.RequestLifetime.TryCancel(statusCode);
            }
            catch (Exception e)
            {
                // The token was cancelled; only one of its callbacks failed.
                m_logger.UnexpectedErrorCancellingRequest(e, request.RequestId);
                return true;
            }
        }

        private void RaiseRequestCancelled(List<uint> requestIds, StatusCode statusCode)
        {
            lock (m_lock)
            {
                for (int ii = 0; ii < requestIds.Count; ii++)
                {
                    if (m_RequestCancelled != null)
                    {
                        try
                        {
                            m_RequestCancelled(this, requestIds[ii], statusCode);
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
        /// Cancels the registered requests of the session with the given client handle and
        /// raises the <see cref="RequestCancelled"/> event for each of them.
        /// </summary>
        private uint CancelMatchingRequests(NodeId sessionId, uint requestHandle, DateTime cancelTimestamp)
        {
            var matchingRequests = new List<OperationContext>();

            // find the requests to cancel; they are cancelled after the lock is released so
            // their cancellation callbacks never run while m_requests is being enumerated.
            lock (m_requestsLock)
            {
                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.SessionId == sessionId &&
                        request.ClientHandle == requestHandle)
                    {
                        matchingRequests.Add(request);
                    }
                }

                // OPC 10000-4 5.7.5.2: all outstanding requests with the handle are cancelled,
                // including those still waiting in the server request queue, which are not
                // registered yet. Remember the cancellation for a while so they are rejected
                // when they reach validation. A Cancel without a timestamp can never prove
                // that a queued request was sent before it, so it is not remembered.
                if (!sessionId.IsNull && cancelTimestamp != DateTime.MinValue)
                {
                    AddPendingCancelLocked(sessionId, requestHandle, cancelTimestamp);
                }
            }

            // flag requests as cancelled.
            var cancelledRequests = new List<uint>(matchingRequests.Count);
            foreach (OperationContext request in matchingRequests)
            {
                TryCancelRequest(request, StatusCodes.BadRequestCancelledByClient);
                cancelledRequests.Add(request.RequestId);
            }

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
                                StatusCodes.BadRequestCancelledByClient);
                        }
                        catch (Exception e)
                        {
                            m_logger.UnexpectedErrorReportingRequestCancelledEvent(e);
                        }
                    }
                }
            }

            return (uint)cancelledRequests.Count;
        }

        /// <summary>
        /// Gets or sets how long a Cancel call keeps cancelling matching requests that reach
        /// validation after it, because they were still waiting in the server request queue.
        /// </summary>
        internal TimeSpan PendingCancelWindow { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Reports whether a request that is being admitted was already cancelled by a Cancel
        /// call that arrived while the request was still queued. A request whose timestamp is
        /// not strictly earlier than the Cancel's is not affected. Both the request and the Cancel must carry a
        /// RequestHeader.Timestamp; otherwise the request is admitted.
        /// </summary>
        /// <param name="context">The request being admitted.</param>
        /// <returns><c>true</c> when the request must complete with Bad_RequestCancelledByClient.</returns>
        internal bool IsCancelledBeforeAdmission(OperationContext context)
        {
            // Only a request the client provably sent before the Cancel is cancelled; without
            // a timestamp a later request reusing the handle (e.g. a client that always sends
            // 0) cannot be told apart.
            if (context == null ||
                context.RequestType == RequestType.Cancel ||
                context.SessionId.IsNull ||
                context.ClientTimestamp == DateTime.MinValue)
            {
                return false;
            }

            // Lock-free fast path for the common case that no Cancel is remembered. The request
            // was registered under m_requestsLock before this call, and a Cancel scans the
            // registered requests and remembers itself under the same lock: either the Cancel
            // saw (and cancelled) this request, or its update of the count is visible here.
            if (Volatile.Read(ref m_pendingCancelSessionCount) == 0)
            {
                return false;
            }

            lock (m_requestsLock)
            {
                if (!m_pendingCancels.TryGetValue(context.SessionId, out List<PendingCancel>? pendingCancels))
                {
                    return false;
                }

                DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
                foreach (PendingCancel pending in pendingCancels)
                {
                    // A request is cancelled only when it is strictly older than the Cancel;
                    // with a coarse client clock an equal timestamp may be a later request.
                    if (pending.ExpiresAt > now &&
                        pending.RequestHandle == context.ClientHandle &&
                        context.ClientTimestamp < pending.CancelTimestamp)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Remembers a Cancel call for the requests of the session that are still queued.
        /// Expired entries are purged only here, so admission never pays for the cleanup.
        /// </summary>
        private void AddPendingCancelLocked(NodeId sessionId, uint requestHandle, DateTime cancelTimestamp)
        {
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;

            // sessions that stopped cancelling are swept once per window, so their entries
            // do not linger after they expired.
            if (now >= m_nextPendingCancelSweep)
            {
                m_nextPendingCancelSweep = now + PendingCancelWindow;
                List<NodeId>? emptySessions = null;
                foreach (KeyValuePair<NodeId, List<PendingCancel>> entry in m_pendingCancels)
                {
                    RemoveExpiredPendingCancels(entry.Value, now);
                    if (entry.Value.Count == 0)
                    {
                        (emptySessions ??= []).Add(entry.Key);
                    }
                }

                if (emptySessions != null)
                {
                    foreach (NodeId emptySession in emptySessions)
                    {
                        m_pendingCancels.Remove(emptySession);
                    }
                }
            }

            if (!m_pendingCancels.TryGetValue(sessionId, out List<PendingCancel>? pendingCancels))
            {
                pendingCancels = [];
                m_pendingCancels.Add(sessionId, pendingCancels);
            }
            else
            {
                RemoveExpiredPendingCancels(pendingCancels, now);
            }

            // Each session is bounded on its own, so a session flooding Cancel only evicts its
            // own oldest entries and never those of another session.
            if (pendingCancels.Count >= kMaxPendingCancelsPerSession)
            {
                pendingCancels.RemoveRange(0, pendingCancels.Count - kMaxPendingCancelsPerSession + 1);
            }

            pendingCancels.Add(new PendingCancel(requestHandle, cancelTimestamp, now + PendingCancelWindow));
            Volatile.Write(ref m_pendingCancelSessionCount, m_pendingCancels.Count);
        }

        /// <summary>
        /// Removes the expired entries, which lead the list because it is in insertion order.
        /// </summary>
        private static void RemoveExpiredPendingCancels(List<PendingCancel> pendingCancels, DateTime now)
        {
            int expired = 0;
            while (expired < pendingCancels.Count && pendingCancels[expired].ExpiresAt <= now)
            {
                expired++;
            }

            if (expired > 0)
            {
                pendingCancels.RemoveRange(0, expired);
            }
        }

        /// <summary>
        /// Forgets the Cancel calls of a session whose requests are aborted because it closes.
        /// </summary>
        private void RemovePendingCancelsLocked(NodeId sessionId)
        {
            if (m_pendingCancels.Remove(sessionId))
            {
                Volatile.Write(ref m_pendingCancelSessionCount, m_pendingCancels.Count);
            }
        }

        /// <summary>
        /// A Cancel call remembered for requests that were still queued when it ran.
        /// </summary>
        private readonly record struct PendingCancel(
            uint RequestHandle,
            DateTime CancelTimestamp,
            DateTime ExpiresAt);

        /// <summary>
        /// Bounds the remembered Cancel calls of one session so a client flooding Cancel
        /// cannot grow them.
        /// </summary>
        private const int kMaxPendingCancelsPerSession = 64;

        /// <summary>
        /// Checks for any expired requests and changes their status.
        /// </summary>
        private void OnTimerExpired(object? state)
        {
            var expiredContexts = new List<OperationContext>();

            // find the expired requests; they are cancelled after the lock is released so their
            // cancellation callbacks never run while m_requests is being enumerated.
            lock (m_requestsLock)
            {
                // find the completed request.
                bool deadlineExists = false;

                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.OperationDeadline < m_timeProvider.GetUtcNow().UtcDateTime)
                    {
                        expiredContexts.Add(request);
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

            // flag requests as expired.
            var expiredRequests = new List<uint>(expiredContexts.Count);
            foreach (OperationContext request in expiredContexts)
            {
                TryCancelRequest(request, StatusCodes.BadTimeout);
                expiredRequests.Add(request.RequestId);
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
        private readonly AsyncLocal<ServiceDispatchScope?> m_currentServiceDispatchScope = new();
        private readonly AsyncLocal<RequestValidationScope?> m_currentValidationScope = new();
        private readonly Dictionary<uint, OperationContext> m_requests;
        private readonly List<RequestDrain> m_requestDrains = [];
        private readonly Lock m_requestsLock = new();
        private readonly HashSet<long> m_activeValidationScopes = [];
        private readonly Dictionary<NodeId, List<PendingCancel>> m_pendingCancels = [];
        private int m_pendingCancelSessionCount;
        private DateTime m_nextPendingCancelSweep;
        private long m_lastValidationScopeId;
        private RequestManagerLifecycleExtension? m_lifecycleExtension;
        private ITimer? m_requestTimer;
        private bool m_disposed;
        private event RequestCancelledEventHandler? m_RequestCancelled;

        /// <summary>
        /// Waits for a fixed set of executing requests and validation scopes to finish. The set is
        /// captured when the drain is created, so requests that start afterwards do not extend it.
        /// </summary>
        internal sealed class RequestDrain
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
            /// Stops waiting for a request after its lifecycle semaphore wait was queued.
            /// </summary>
            /// <param name="requestId">The lifecycle-waiting request.</param>
            /// <returns><c>true</c> when nothing is left to wait for.</returns>
            public bool Exclude(uint requestId)
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
            /// <param name="previousRequestId">The direct request id to restore on dispose.</param>
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
            /// Reports the request as completed.
            /// </summary>
            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    m_requestManager.CompleteRequest(m_context);
                    m_requestManager.m_currentRequestId.Value = m_previousRequestId;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly OperationContext m_context;
            private readonly uint? m_previousRequestId;
            private bool m_disposed;
        }

        /// <summary>
        /// Carries the executing request id into the service dispatch flow. It is opened at the top
        /// of request processing, before a request id exists, and stamped with the id once
        /// validation registers the request. It is held as a reference from the dispatch flow, so
        /// the stamped id is observed by a NodeManager lifecycle operation that runs inside a
        /// request callback beneath the dispatch.
        /// </summary>
        private sealed class ServiceDispatchScope : IDisposable
        {
            public ServiceDispatchScope(
                RequestManager requestManager,
                ServiceDispatchScope? previousScope)
            {
                m_requestManager = requestManager;
                m_previousScope = previousScope;
            }

            public uint? RequestId { get; private set; }

            public void RegisterRequest(uint requestId)
            {
                RequestId ??= requestId;
            }

            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    m_requestManager.m_currentServiceDispatchScope.Value = m_previousScope;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly ServiceDispatchScope? m_previousScope;
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
            /// <param name="previousScope">The validation scope to restore on dispose.</param>
            public RequestValidationScope(
                RequestManager requestManager,
                long validationId,
                RequestValidationScope? previousScope)
            {
                m_requestManager = requestManager;
                m_validationId = validationId;
                m_previousScope = previousScope;
            }

            public long ValidationId => m_validationId;

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
                    m_requestManager.m_currentValidationScope.Value = m_previousScope;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly long m_validationId;
            private readonly RequestValidationScope? m_previousScope;
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

        [LoggerMessage(EventId = ServerEventIds.RequestManager + 1, Level = LogLevel.Error,
            Message = "Unexpected error in a cancellation callback of request {RequestId}.")]
        public static partial void UnexpectedErrorCancellingRequest(this ILogger logger, Exception ex, uint requestId);
    }
}
