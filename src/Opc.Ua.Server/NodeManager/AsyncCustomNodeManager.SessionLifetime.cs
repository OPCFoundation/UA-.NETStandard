// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class AsyncCustomNodeManager
    {
        /// <summary>
        /// Registers awaited cleanup for session-owned resources composed into this node manager.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The base <see cref="SessionClosingAsync"/> awaits every registered handler, including
        /// when subscriptions are retained, and aggregates failures after attempting all handlers.
        /// Overrides must await the base implementation. Handlers run outside the registration
        /// gate and must not await disposal of this node manager from inside the callback.
        /// </para>
        /// <para>
        /// The returned lease unregisters without blocking an already admitted callback. It may
        /// be disposed from a callback. Manager disposal drains admitted dispatches and releases
        /// all registrations. A handler registered during a dispatch participates in subsequent
        /// dispatches; resource admission must independently reject an already closing session.
        /// </para>
        /// </remarks>
        /// <param name="handler">Cleanup to await for the closing session.</param>
        /// <returns>An idempotent, nonblocking registration lease owned by the resource.</returns>
        /// <exception cref="ArgumentNullException">The handler is null.</exception>
        /// <exception cref="ObjectDisposedException">The node manager is stopping.</exception>
        public IDisposable RegisterSessionClosingHandler(Func<NodeId, CancellationToken, ValueTask> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            lock (m_operationLifetimeLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(AsyncCustomNodeManager));
                }
                var registration = new SessionClosingHandlerRegistration(this, handler);
                m_sessionClosingHandlers.Add(registration);
                return registration;
            }
        }

        /// <summary>
        /// Dispatches the session-close snapshot without holding any node-manager gate across callbacks.
        /// </summary>
        private async ValueTask InvokeSessionClosingHandlersAsync(NodeId sessionId, CancellationToken cancellationToken)
        {
            NodeManagerOperation admittedOperation;
            SessionClosingHandlerRegistration[] registrations;
            lock (m_operationLifetimeLock)
            {
                if (m_sessionClosingHandlers.Count == 0)
                {
                    return;
                }
                registrations = [.. m_sessionClosingHandlers];
                admittedOperation = BeginNodeManagerOperation();
            }
            using NodeManagerOperation operation = admittedOperation;
            List<Exception>? errors = null;
            foreach (SessionClosingHandlerRegistration registration in registrations)
            {
                Func<NodeId, CancellationToken, ValueTask>? handler = registration.Handler;
                if (handler == null)
                {
                    continue;
                }
                try
                {
                    await handler(sessionId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
            if (errors != null)
            {
                throw new AggregateException("Session-resource cleanup failed.", errors);
            }
        }

        /// <summary>
        /// Releases callbacks after admitted operations have drained during manager disposal.
        /// </summary>
        private void DisposeSessionClosingHandlers()
        {
            SessionClosingHandlerRegistration[] registrations;
            lock (m_operationLifetimeLock)
            {
                registrations = [.. m_sessionClosingHandlers];
                m_sessionClosingHandlers.Clear();
            }
            foreach (SessionClosingHandlerRegistration registration in registrations)
            {
                registration.Dispose();
            }
        }

        /// <summary>Registrations protected by the manager's operation-admission gate.</summary>
        private readonly List<SessionClosingHandlerRegistration> m_sessionClosingHandlers = [];

        /// <summary>
        /// Removes a callback without waiting for a callback that may itself own this lease.
        /// </summary>
        private sealed class SessionClosingHandlerRegistration : IDisposable
        {
            /// <summary>
            /// Captures the manager and callback until registration is released.
            /// </summary>
            public SessionClosingHandlerRegistration(
                AsyncCustomNodeManager owner,
                Func<NodeId, CancellationToken, ValueTask> handler)
            {
                m_owner = owner;
                m_handler = handler;
            }

            /// <summary>
            /// Captures an admitted callback, or null after unregistering.
            /// </summary>
            public Func<NodeId, CancellationToken, ValueTask>? Handler => Volatile.Read(ref m_handler);

            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_handler, null);
                AsyncCustomNodeManager? owner = Interlocked.Exchange(ref m_owner, null);
                if (owner != null)
                {
                    lock (owner.m_operationLifetimeLock)
                    {
                        owner.m_sessionClosingHandlers.Remove(this);
                    }
                }
            }

            /// <summary>The registry owner, released exactly once.</summary>
            private AsyncCustomNodeManager? m_owner;

            /// <summary>The callback, cleared before removing the registration.</summary>
            private Func<NodeId, CancellationToken, ValueTask>? m_handler;
        }
    }
}
