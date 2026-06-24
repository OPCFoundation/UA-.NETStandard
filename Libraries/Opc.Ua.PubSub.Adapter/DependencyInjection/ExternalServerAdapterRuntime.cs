/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Owns the lifetime of the external-server sessions and subscription
    /// coordinators created by the adapter composition steps. A single instance
    /// is registered as a singleton so the sessions are shared across the
    /// publisher, subscriber and action responders that target the same host and
    /// are disposed exactly once when the application shuts down. Subscription
    /// coordinators are started on application start and disposed before their
    /// sessions.
    /// </summary>
    internal sealed class ExternalServerAdapterRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Registers a session whose lifetime is owned by the runtime.
        /// </summary>
        /// <param name="session">
        /// The session to dispose on shutdown.
        /// </param>
        public void AddSession(IExternalServerSession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ExternalServerAdapterRuntime));
                }
                m_sessions.Add(session);
            }
        }

        /// <summary>
        /// Registers a subscription coordinator that is started on application
        /// start and disposed on shutdown.
        /// </summary>
        /// <param name="coordinator">
        /// The coordinator to start and dispose.
        /// </param>
        public void AddCoordinator(ExternalSubscriptionCoordinator coordinator)
        {
            if (coordinator is null)
            {
                throw new ArgumentNullException(nameof(coordinator));
            }
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ExternalServerAdapterRuntime));
                }
                m_coordinators.Add(coordinator);
            }
        }

        /// <summary>
        /// Starts every registered subscription coordinator. The call is
        /// idempotent: invoking it again once started is a no-op.
        /// </summary>
        /// <param name="ct">
        /// A token used to cancel the start.
        /// </param>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            ExternalSubscriptionCoordinator[] coordinators;
            lock (m_gate)
            {
                if (m_disposed || m_started)
                {
                    return;
                }
                m_started = true;
                coordinators = [.. m_coordinators];
            }

            foreach (ExternalSubscriptionCoordinator coordinator in coordinators)
            {
                await coordinator.StartAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ExternalSubscriptionCoordinator[] coordinators;
            IExternalServerSession[] sessions;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                coordinators = [.. m_coordinators];
                sessions = [.. m_sessions];
                m_coordinators.Clear();
                m_sessions.Clear();
            }

            foreach (ExternalSubscriptionCoordinator coordinator in coordinators)
            {
                await coordinator.DisposeAsync().ConfigureAwait(false);
            }
            foreach (IExternalServerSession session in sessions)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private readonly System.Threading.Lock m_gate = new();
        private readonly List<IExternalServerSession> m_sessions = [];
        private readonly List<ExternalSubscriptionCoordinator> m_coordinators = [];
        private bool m_started;
        private bool m_disposed;
    }
}
