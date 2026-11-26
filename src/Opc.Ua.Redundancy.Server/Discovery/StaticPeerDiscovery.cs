// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the fallback <see cref="IPeerDiscovery"/> that returns a fixed peer
    /// set supplied at configuration time. It is the fallback used when no dynamic discovery mechanism (DNS,
    /// LDS, Kubernetes) is available.
    /// </summary>
    public sealed class StaticPeerDiscovery : IPeerDiscovery
    {
        /// <summary>
        /// Creates a static discovery over a fixed peer set.
        /// </summary>
        /// <param name="peers">The configured peers.</param>
        /// <exception cref="ArgumentNullException"><paramref name="peers"/> is <c>null</c>.</exception>
        public StaticPeerDiscovery(IEnumerable<DiscoveredPeer> peers)
        {
            if (peers == null)
            {
                throw new ArgumentNullException(nameof(peers));
            }
            m_peers = [.. peers];
        }

        /// <inheritdoc/>
        public event Action<ArrayOf<DiscoveredPeer>>? PeersChanged
        {
            add => m_tracker.Changed += value;
            remove => m_tracker.Changed -= value;
        }

        /// <inheritdoc/>
        public ArrayOf<DiscoveredPeer> Peers => m_tracker.Current;

        /// <inheritdoc/>
        public ValueTask<ArrayOf<DiscoveredPeer>> RefreshAsync(CancellationToken ct = default)
        {
            return new ValueTask<ArrayOf<DiscoveredPeer>>(m_tracker.Apply(m_peers));
        }

        private readonly DiscoveredPeer[] m_peers;
        private readonly PeerSetTracker m_tracker = new();
    }
}
