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
                : string.Concat(peer.ServerUri, "|", peer.GossipEndpoint.ToString());
        }

        private DiscoveredPeer[] m_peers = [];
    }
}
