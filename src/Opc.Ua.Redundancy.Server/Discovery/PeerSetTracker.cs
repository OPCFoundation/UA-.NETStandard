/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Internal composition helper for <see cref="IPeerDiscovery"/> implementations: it stores the last
    /// discovered set, compares snapshots by (ServerUri, gossip endpoint) identity, and raises
    /// <see cref="Changed"/> only on a real change. Discovery mechanisms hold one of these rather than deriving
    /// from a shared base, so the public discovery types stay sealed.
    /// </summary>
    internal sealed class PeerSetTracker
    {
        public event Action<ArrayOf<DiscoveredPeer>>? Changed;

        public ArrayOf<DiscoveredPeer> Current => Volatile.Read(ref m_peers);

        /// <summary>
        /// Applies a freshly discovered peer set, raising <see cref="Changed"/> when it differs from the
        /// previous set.
        /// </summary>
        /// <param name="discovered">The freshly discovered peers.</param>
        /// <returns>The new snapshot.</returns>
        public ArrayOf<DiscoveredPeer> Apply(IReadOnlyList<DiscoveredPeer> discovered)
        {
            DiscoveredPeer[] next = [.. discovered];

            DiscoveredPeer[] previous = Volatile.Read(ref m_peers);
            if (!SetsMatch(previous, next))
            {
                Volatile.Write(ref m_peers, next);
                Changed?.Invoke(next);
            }

            return next;
        }

        private static bool SetsMatch(DiscoveredPeer[] a, DiscoveredPeer[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DiscoveredPeer peer in a)
            {
                seen.Add(Identity(peer));
            }
            foreach (DiscoveredPeer peer in b)
            {
                if (!seen.Contains(Identity(peer)))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Identity(DiscoveredPeer peer)
        {
            return peer.GossipEndpoint == null
                ? peer.ServerUri
                : $"{peer.ServerUri}|{peer.GossipEndpoint}";
        }

        private DiscoveredPeer[] m_peers = [];
    }
}
