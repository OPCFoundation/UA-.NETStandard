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
            m_requests = new RequestTable<OperationContext>();
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
                lock (m_requestsLock)
                {
                    if (m_disposed)
                    {
                        return;
                    }

                    m_disposed = true;
                    operations = m_requests.Clear();
                }

                foreach (OperationContext operation in operations)
                {
                    operation.RequestLifetime.TryCancel(StatusCodes.BadSessionClosed);
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
        /// <remarks>
        /// Requests are tracked by their context; registering the same context again does
        /// nothing.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void RequestReceived(OperationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            m_requests.Add(context);

            // Registration takes no lock. A Session close marks the Session before it sweeps the
            // registered requests, and a Cancel remembers itself before it scans them; the fence
            // orders this registration before the caller checks either mark, so the sweep sees
            // the request or the request sees the mark.
            Interlocked.MemoryBarrier();

            if (context.OperationDeadline < DateTime.MaxValue)
            {
                // Counted before the timer is checked, so a timer that is being stopped for
                // lack of deadlines either sees the count change or is seen as stopped here.
                Interlocked.Increment(ref m_deadlineRegistrations);
                if (Volatile.Read(ref m_requestTimer) == null)
                {
                    lock (m_requestsLock)
                    {
                        m_requestTimer ??= m_timeProvider.CreateTimer(
                            OnTimerExpired,
                            null,
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(1));
                    }
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
        /// Reports a request as completed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        private void CompleteRequest(OperationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // remove the request, only when it is this context.
            bool removed = m_requests.Remove(context);
            if (removed)
            {
                context.RequestLifetime?.MarkCompleted();
            }
        }

        /// <summary>
        /// Gets the number of tracked requests in a snapshot, for diagnostics outside the dispatch hot path.
        /// </summary>
        internal int TrackedRequestCount => m_requests.GetEntries().Count;

        /// <summary>
        /// Reports whether the supplied context belongs to a request that is executing right now.
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

            return m_requests.Contains(operation);
        }

        /// <summary>
        /// Enters a request scope, which tracks a validated request for as long as it executes.
        /// Disposing the scope reports the request as completed.
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
        /// Enters the request scope of <paramref name="context"/> like
        /// <see cref="EnterRequestScope"/> and attaches it to the context, so that disposing the
        /// context completes the request.
        /// </summary>
        /// <remarks>
        /// The context completes its own request without an ambient identity or allocated scope object.
        /// </remarks>
        /// <param name="context">The context of the request being executed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        internal void AttachRequestScope(OperationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            RequestReceived(context);

            context.AttachRequestCompletion(this);
        }

        /// <summary>
        /// Completes the request of a context that <see cref="AttachRequestScope(OperationContext)"/>
        /// attached without a scope object; called once, when the context is disposed.
        /// </summary>
        internal void CompleteAttachedRequest(OperationContext context)
        {
            CompleteRequest(context);
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
                foreach (OperationContext request in m_requests.GetEntries())
                {
                    // an in-flight ActivateSession has no Session context yet, but it is
                    // aborted with the Session it is activating.
                    if (request.RequestId != excludedRequestId &&
                        (request.SessionId == sessionId ||
                            request.ActivationTargetSessionId == sessionId))
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
                // OPC 10000-4 5.7.5.2: all outstanding requests with the handle are cancelled,
                // including those still waiting in the server request queue, which are not
                // registered yet. Remember the cancellation for a while so they are rejected
                // when they reach validation. A Cancel without a timestamp can never prove
                // that a queued request was sent before it, so it is not remembered.
                // Remembered before the scan: a request registering concurrently is either
                // found by the scan or finds the remembered Cancel in IsCancelledBeforeAdmission.
                if (!sessionId.IsNull && cancelTimestamp != DateTime.MinValue)
                {
                    AddPendingCancelLocked(sessionId, requestHandle, cancelTimestamp);
                }

                foreach (OperationContext request in m_requests.GetEntries())
                {
                    if (request.SessionId == sessionId &&
                        request.ClientHandle == requestHandle)
                    {
                        matchingRequests.Add(request);
                    }
                }
            }

            // flag requests as cancelled. A request that another cancellation (a timeout, a
            // Session close or an earlier Cancel) already claimed, or that completed after
            // the snapshot, was not cancelled by this call and is neither counted nor reported.
            var cancelledRequests = new List<uint>(matchingRequests.Count);
            foreach (OperationContext request in matchingRequests)
            {
                if (TryCancelRequest(request, StatusCodes.BadRequestCancelledByClient))
                {
                    cancelledRequests.Add(request.RequestId);
                }
            }

            RaiseRequestCancelled(cancelledRequests, StatusCodes.BadRequestCancelledByClient);
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
            // was registered (followed by a full fence) before this call, and a Cancel remembers
            // itself (with a full fence) before it scans the registered requests: either the
            // Cancel saw (and cancelled) this request, or its update of the count is visible here.
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
            Interlocked.Exchange(ref m_pendingCancelSessionCount, m_pendingCancels.Count);
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
                long deadlineRegistrations = Interlocked.Read(ref m_deadlineRegistrations);

                // find the completed request.
                bool deadlineExists = false;

                foreach (OperationContext request in m_requests.GetEntries())
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

                // check if the timer can be cancelled. A request registering a deadline
                // concurrently either changed the count or finds the timer stopped and starts it.
                if (m_requestTimer != null && !deadlineExists)
                {
                    ITimer timer = Interlocked.Exchange(ref m_requestTimer, null)!;
                    if (Interlocked.Read(ref m_deadlineRegistrations) != deadlineRegistrations)
                    {
                        m_requestTimer = timer;
                    }
                    else
                    {
                        timer.Dispose();
                    }
                }
            }

            // flag requests as expired; a request another cancellation already claimed, or
            // that completed after the snapshot, did not time out and is not reported.
            var expiredRequests = new List<uint>(expiredContexts.Count);
            foreach (OperationContext request in expiredContexts)
            {
                if (TryCancelRequest(request, StatusCodes.BadTimeout))
                {
                    expiredRequests.Add(request.RequestId);
                }
            }

            RaiseRequestCancelled(expiredRequests, StatusCodes.BadTimeout);
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;

        /// <summary>
        /// The executing requests. Registration and completion
        /// take no lock; timeouts and Cancel run under
        /// <see cref="m_requestsLock"/>.
        /// </summary>
        private readonly RequestTable<OperationContext> m_requests;
        private readonly Lock m_requestsLock = new();

        /// <summary>
        /// The number of requests registered with a deadline, so a stopping timer notices them.
        /// </summary>
        private long m_deadlineRegistrations;
        private readonly Dictionary<NodeId, List<PendingCancel>> m_pendingCancels = [];
        private int m_pendingCancelSessionCount;
        private DateTime m_nextPendingCancelSweep;

        private ITimer? m_requestTimer;
        private bool m_disposed;
        private event RequestCancelledEventHandler? m_RequestCancelled;

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
