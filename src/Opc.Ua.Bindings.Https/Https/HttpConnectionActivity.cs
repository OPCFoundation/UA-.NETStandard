// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Tracks the physical HTTP connections of one admission scope so that a
    /// connection with no request in flight can be closed when a new
    /// connection finds the connection capacity exhausted.
    /// </summary>
    /// <remarks>
    /// A physical HTTP connection holds its admission lease for its whole
    /// lifetime, including the keep-alive time between requests. Without
    /// reclamation, peers that keep connections open with an occasional
    /// request hold every slot and every new connection is refused. An idle
    /// keep-alive connection is reclaimed as cheaply as a SecureChannel
    /// without a Session: its client opens a new connection for its next
    /// request. A WebSocket is never reclaimed here: its upgrade request stays
    /// in flight for the lifetime of the socket, which is reclaimed by the
    /// WebSocket's own policy.
    /// </remarks>
    internal sealed class HttpConnectionActivityRegistry
    {
        /// <summary>
        /// Creates a registry that orders idle connections by the given clock.
        /// </summary>
        public HttpConnectionActivityRegistry(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// The number of tracked connections.
        /// </summary>
        public int Count => m_connections.Count;

        /// <summary>
        /// Starts tracking an admitted physical connection.
        /// </summary>
        public HttpConnectionActivity Register(UaScConnectionAdmission.Lease lease)
        {
            var activity = new HttpConnectionActivity(lease, m_timeProvider);
            m_connections.TryAdd(activity, 0);
            return activity;
        }

        /// <summary>
        /// Stops tracking a connection that is closing.
        /// </summary>
        public void Unregister(HttpConnectionActivity activity)
        {
            m_connections.TryRemove(activity, out _);
        }

        /// <summary>
        /// Closes the connection that has been idle longest. Returns false when
        /// every tracked connection has a request in flight.
        /// </summary>
        /// <remarks>
        /// A connection admitted entirely from a protected floor is never
        /// reclaimed: closing it frees no capacity a new connection could use
        /// unless it belongs to the same protected owner.
        /// </remarks>
        public bool TryReclaimIdle()
        {
            HttpConnectionActivity[] candidates = [.. m_connections.Keys];
            Array.Sort(candidates, static (left, right) => left.LastActivity.CompareTo(right.LastActivity));
            foreach (HttpConnectionActivity candidate in candidates)
            {
                if (candidate.UsesSharedCapacity && candidate.TryReclaim())
                {
                    m_connections.TryRemove(candidate, out _);
                    return true;
                }
            }
            return false;
        }

        private readonly TimeProvider m_timeProvider;
        private readonly ConcurrentDictionary<HttpConnectionActivity, byte> m_connections = new();
    }

    /// <summary>
    /// The request activity of one physical HTTP connection.
    /// </summary>
    internal sealed class HttpConnectionActivity
    {
        /// <summary>
        /// Starts idle as of the admission of the connection.
        /// </summary>
        internal HttpConnectionActivity(UaScConnectionAdmission.Lease lease, TimeProvider timeProvider)
        {
            m_lease = lease;
            m_timeProvider = timeProvider;
            m_lastActivity = timeProvider.GetTimestamp();
        }

        /// <summary>
        /// The monotonic timestamp of the admission or of the end of the last request.
        /// </summary>
        public long LastActivity => Interlocked.Read(ref m_lastActivity);

        /// <summary>
        /// Whether closing the connection frees shared connection capacity.
        /// </summary>
        public bool UsesSharedCapacity => m_lease.UsesSharedConnectionCapacity;

        /// <summary>
        /// Marks a request in flight. Returns false when the connection was
        /// reclaimed, in which case the request must not be processed.
        /// </summary>
        public bool TryBeginRequest()
        {
            while (true)
            {
                int state = Volatile.Read(ref m_state);
                if (state < 0)
                {
                    return false;
                }
                if (Interlocked.CompareExchange(ref m_state, state + 1, state) == state)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Marks the end of a request started with <see cref="TryBeginRequest"/>.
        /// </summary>
        public void EndRequest()
        {
            Interlocked.Exchange(ref m_lastActivity, m_timeProvider.GetTimestamp());
            Interlocked.Decrement(ref m_state);
        }

        /// <summary>
        /// Claims and closes the connection if no request is in flight.
        /// </summary>
        public bool TryReclaim()
        {
            if (Interlocked.CompareExchange(ref m_state, kReclaimed, 0) != 0)
            {
                return false;
            }
            // Before a transport is attached the lease aborts the connection.
            m_lease.Close();
            return true;
        }

        private const int kReclaimed = -1;
        private readonly UaScConnectionAdmission.Lease m_lease;
        private readonly TimeProvider m_timeProvider;
        private long m_lastActivity;

        /// <summary>
        /// The number of requests in flight, or <see cref="kReclaimed"/>.
        /// </summary>
        private int m_state;
    }
}
