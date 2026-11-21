// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="IRedundantServerSetProvider"/> whose peer set is
    /// updated at runtime from an <see cref="IPeerDiscovery"/>. <c>FindServers</c> therefore returns the
    /// currently-discovered replica set (OPC 10000-4 §6.6.2.4.5.1), reflecting scale-up and scale-down without
    /// a restart. Reads are lock-free (a volatile snapshot swap).
    /// </summary>
    public sealed class DiscoveredRedundantServerSetProvider : IRedundantServerSetProvider
    {
        /// <inheritdoc/>
        public ArrayOf<ApplicationDescription> GetRedundantServerSet()
        {
            return Volatile.Read(ref m_descriptions);
        }

        /// <summary>
        /// Replaces the published peer set with descriptions built from the discovered peers.
        /// </summary>
        /// <param name="peers">The discovered peers.</param>
        public void Update(ArrayOf<DiscoveredPeer> peers)
        {
            var descriptions = new List<ApplicationDescription>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                DiscoveredPeer peer = peers[i];
                if (peer == null || string.IsNullOrEmpty(peer.ServerUri) || peer.DiscoveryUrls.IsNull)
                {
                    continue;
                }

                descriptions.Add(new ApplicationDescription
                {
                    ApplicationUri = peer.ServerUri,
                    ApplicationName = new LocalizedText(peer.ServerUri),
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = peer.DiscoveryUrls
                });
            }

            Volatile.Write(ref m_descriptions, [.. descriptions]);
        }

        private ApplicationDescription[] m_descriptions = [];
    }
}
