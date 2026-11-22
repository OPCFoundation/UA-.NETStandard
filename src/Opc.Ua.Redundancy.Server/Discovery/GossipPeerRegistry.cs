// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Net;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the default <see cref="IGossipPeerSink"/>. It fans added endpoints out
    /// to every registered live transport and replays already-added endpoints to transports that register after
    /// the fact, so discovery and transport startup can happen in any order. Registered as a singleton so the
    /// replicated address-space and session transports and the discovery loop share one instance.
    /// </summary>
    public sealed class GossipPeerRegistry : IGossipPeerSink
    {
        /// <inheritdoc/>
        public void Register(Action<IPEndPoint> addPeer)
        {
            if (addPeer == null)
            {
                throw new ArgumentNullException(nameof(addPeer));
            }

            IPEndPoint[] replay;
            lock (m_lock)
            {
                m_sinks.Add(addPeer);
                replay = [.. m_endpoints];
            }

            foreach (IPEndPoint endpoint in replay)
            {
                addPeer(endpoint);
            }
        }

        /// <inheritdoc/>
        public void AddPeer(IPEndPoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            Action<IPEndPoint>[] sinks;
            lock (m_lock)
            {
                if (!m_endpoints.Add(endpoint))
                {
                    return;
                }
                sinks = [.. m_sinks];
            }

            foreach (Action<IPEndPoint> sink in sinks)
            {
                sink(endpoint);
            }
        }

        /// <inheritdoc/>
        public void AddPeers(IEnumerable<IPEndPoint> endpoints)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            foreach (IPEndPoint endpoint in endpoints)
            {
                AddPeer(endpoint);
            }
        }

        private readonly System.Threading.Lock m_lock = new();
        private readonly List<Action<IPEndPoint>> m_sinks = [];
        private readonly HashSet<IPEndPoint> m_endpoints = [];
    }
}
