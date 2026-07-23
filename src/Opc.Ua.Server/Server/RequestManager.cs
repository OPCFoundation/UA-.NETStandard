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
                m_currentValidationScope.Value?.Register(context);

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
        public void RequestCompleted(OperationContext context)
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

        internal bool IsExecutingRequest => m_currentRequestId.Value.HasValue;

        internal IDisposable EnterValidationScope()
        {
            long validationId = Interlocked.Increment(
                ref m_lastValidationScopeId);
            lock (m_requestsLock)
            {
                m_activeValidationScopes.Add(validationId);
            }

            uint? previousRequestId = m_currentRequestId.Value;
            RequestValidationScope? previousValidationScope =
                m_currentValidationScope.Value;
            m_currentRequestId.Value = uint.MaxValue;
            var scope = new RequestValidationScope(
                this,
                validationId,
                previousRequestId,
                previousValidationScope);
            m_currentValidationScope.Value = scope;
            return scope;
        }

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

        internal void PromoteValidatedRequest(OperationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            m_currentValidationScope.Value?.Promote(context);
        }

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

        private readonly AsyncLocal<RequestValidationScope?>
            m_currentValidationScope = new();

        private readonly Dictionary<uint, OperationContext> m_requests;
        private readonly List<RequestDrain> m_requestDrains = [];
        private readonly Lock m_requestsLock = new();
        private readonly HashSet<long> m_activeValidationScopes = [];
        private long m_lastValidationScopeId;
        private ITimer? m_requestTimer;
        private event RequestCancelledEventHandler? m_RequestCancelled;

        private sealed class RequestDrain
        {
            public RequestDrain(
                IEnumerable<uint> requestIds,
                IEnumerable<long> validationIds)
            {
                m_requestIds = [.. requestIds];
                m_validationIds = [.. validationIds];
            }

            public Task Completion => m_completion.Task;

            public bool Complete(uint requestId)
            {
                m_requestIds.Remove(requestId);
                return TryComplete();
            }

            public bool CompleteValidation(long validationId)
            {
                m_validationIds.Remove(validationId);
                return TryComplete();
            }

            public void Cancel()
            {
                m_completion.TrySetCanceled();
            }

            private readonly HashSet<uint> m_requestIds;
            private readonly HashSet<long> m_validationIds;

            private readonly TaskCompletionSource<bool> m_completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

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

        private sealed class RequestExecutionScope : IDisposable
        {
            public RequestExecutionScope(
                RequestManager requestManager,
                OperationContext context,
                uint? previousRequestId)
            {
                m_requestManager = requestManager;
                m_context = context;
                m_previousRequestId = previousRequestId;
            }

            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    m_requestManager.RequestCompleted(m_context);
                    m_requestManager.m_currentRequestId.Value =
                        m_previousRequestId;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly OperationContext m_context;
            private readonly uint? m_previousRequestId;
            private bool m_disposed;
        }

        private sealed class RequestValidationScope : IDisposable
        {
            public RequestValidationScope(
                RequestManager requestManager,
                long validationId,
                uint? previousRequestId,
                RequestValidationScope? previousValidationScope)
            {
                m_requestManager = requestManager;
                m_validationId = validationId;
                m_previousRequestId = previousRequestId;
                m_previousValidationScope = previousValidationScope;
            }

            public void Register(OperationContext context)
            {
                if (!m_registeredContexts.Contains(context))
                {
                    m_registeredContexts.Add(context);
                }
            }

            public void Promote(OperationContext context)
            {
                m_registeredContexts.Remove(context);
            }

            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    foreach (OperationContext context in m_registeredContexts)
                    {
                        m_requestManager.RequestCompleted(context);
                    }
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
                    m_requestManager.m_currentValidationScope.Value =
                        m_previousValidationScope;
                }
            }

            private readonly RequestManager m_requestManager;
            private readonly long m_validationId;
            private readonly uint? m_previousRequestId;
            private readonly RequestValidationScope? m_previousValidationScope;
            private readonly List<OperationContext> m_registeredContexts = [];
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
