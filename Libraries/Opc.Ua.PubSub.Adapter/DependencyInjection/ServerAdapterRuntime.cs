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
    internal sealed class ServerAdapterRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="ServerAdapterRuntime"/>.
        /// </summary>
        public ServerAdapterRuntime()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="ServerAdapterRuntime"/> with the supplied
        /// session factory.
        /// </summary>
        /// <param name="sessionFactory">
        /// Factory used by the pooled-session acquisition path.
        /// </param>
        public ServerAdapterRuntime(IServerSessionFactory? sessionFactory)
        {
            m_sessionFactory = sessionFactory;
        }

        /// <summary>
        /// Registers a session whose lifetime is owned by the runtime.
        /// </summary>
        /// <param name="session">
        /// The session to dispose on shutdown.
        /// </param>
        public void AddSession(IServerSession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ServerAdapterRuntime));
                }
                m_sessions.Add(session);
            }
        }

        /// <summary>
        /// Acquires a reference-counted session for the supplied connection
        /// options, reusing an existing session when the connection identity is
        /// equal.
        /// </summary>
        /// <param name="connection">
        /// Connection identity for the pooled session.
        /// </param>
        /// <param name="telemetry">
        /// Telemetry used when the session has to be created.
        /// </param>
        /// <returns>
        /// A lease that releases the session when disposed.
        /// </returns>
        public ServerSessionLease AcquireSession(
            ServerConnectionOptions connection,
            ITelemetryContext telemetry)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            IServerSessionFactory factory = m_sessionFactory
                ?? throw new InvalidOperationException(
                    "A session factory is required before pooled sessions can be acquired.");
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ServerAdapterRuntime));
                }

                ServerConnectionOptions key = CloneConnectionOptions(connection);
                if (!m_pooledSessions.TryGetValue(key, out PooledSession? entry))
                {
                    entry = new PooledSession(key, factory.Create(connection, telemetry));
                    m_pooledSessions.Add(key, entry);
                }
                entry.ReferenceCount++;
                return new ServerSessionLease(this, entry.Key, entry.Session);
            }
        }

        /// <summary>
        /// Registers a subscription coordinator that is started on application
        /// start and disposed on shutdown.
        /// </summary>
        /// <param name="coordinator">
        /// The coordinator to start and dispose.
        /// </param>
        public void AddCoordinator(SubscriptionCoordinator coordinator)
        {
            if (coordinator is null)
            {
                throw new ArgumentNullException(nameof(coordinator));
            }
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ServerAdapterRuntime));
                }
                m_coordinators.Add(coordinator);
            }
        }

        /// <summary>
        /// Registers and starts a subscription coordinator when the runtime is
        /// already started.
        /// </summary>
        /// <param name="coordinator">
        /// The coordinator to own.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the start.
        /// </param>
        public async ValueTask AddCoordinatorAsync(
            SubscriptionCoordinator coordinator,
            CancellationToken ct = default)
        {
            if (coordinator is null)
            {
                throw new ArgumentNullException(nameof(coordinator));
            }

            bool start;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ServerAdapterRuntime));
                }
                m_coordinators.Add(coordinator);
                start = m_started;
            }

            if (start)
            {
                await coordinator.StartAsync(ct).ConfigureAwait(false);
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
            SubscriptionCoordinator[] coordinators;
            lock (m_gate)
            {
                if (m_disposed || m_started)
                {
                    return;
                }
                m_started = true;
                coordinators = [.. m_coordinators];
            }

            foreach (SubscriptionCoordinator coordinator in coordinators)
            {
                await coordinator.StartAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            SubscriptionCoordinator[] coordinators;
            IServerSession[] sessions;
            PooledSession[] pooledSessions;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                coordinators = [.. m_coordinators];
                sessions = [.. m_sessions];
                pooledSessions = [.. m_pooledSessions.Values];
                m_coordinators.Clear();
                m_sessions.Clear();
                m_pooledSessions.Clear();
            }

            foreach (SubscriptionCoordinator coordinator in coordinators)
            {
                await coordinator.DisposeAsync().ConfigureAwait(false);
            }
            foreach (IServerSession session in sessions)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            foreach (PooledSession session in pooledSessions)
            {
                await session.Session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async ValueTask ReleaseSessionAsync(ServerConnectionOptions key)
        {
            PooledSession? session = null;
            lock (m_gate)
            {
                if (!m_pooledSessions.TryGetValue(key, out PooledSession? entry))
                {
                    return;
                }

                entry.ReferenceCount--;
                if (entry.ReferenceCount == 0)
                {
                    m_pooledSessions.Remove(key);
                    session = entry;
                }
            }

            if (session is not null)
            {
                await session.Session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ServerConnectionOptions CloneConnectionOptions(ServerConnectionOptions options)
        {
            return new ServerConnectionOptions
            {
                EndpointUrl = options.EndpointUrl,
                SecurityMode = options.SecurityMode,
                SecurityPolicyUri = options.SecurityPolicyUri,
                UserIdentity = options.UserIdentity,
                UserName = options.UserName,
                Password = options.Password,
                SessionName = options.SessionName,
                SessionTimeout = options.SessionTimeout,
                ApplicationConfiguration = options.ApplicationConfiguration,
                ApplicationName = options.ApplicationName
            };
        }

        private sealed class PooledSession
        {
            public PooledSession(ServerConnectionOptions key, IServerSession session)
            {
                Key = key;
                Session = session;
            }

            public ServerConnectionOptions Key { get; }

            public IServerSession Session { get; }

            public int ReferenceCount { get; set; }
        }

        private readonly IServerSessionFactory? m_sessionFactory;
        private readonly System.Threading.Lock m_gate = new();
        private readonly List<IServerSession> m_sessions = [];
        private readonly List<SubscriptionCoordinator> m_coordinators = [];
        private readonly Dictionary<ServerConnectionOptions, PooledSession> m_pooledSessions = [];
        private bool m_started;
        private bool m_disposed;

        /// <summary>
        /// Reference-counted pooled external-server session lease.
        /// </summary>
        public sealed class ServerSessionLease : IAsyncDisposable
        {
            internal ServerSessionLease(
                ServerAdapterRuntime owner,
                ServerConnectionOptions key,
                IServerSession session)
            {
                m_owner = owner;
                m_key = key;
                Session = session;
            }

            /// <summary>
            /// Gets the leased session.
            /// </summary>
            public IServerSession Session { get; }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                await m_owner.ReleaseSessionAsync(m_key).ConfigureAwait(false);
            }

            private readonly ServerAdapterRuntime m_owner;
            private readonly ServerConnectionOptions m_key;
            private bool m_disposed;
        }
    }
}
