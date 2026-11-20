/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="IPeerDiscovery"/> that discovers redundant peers from a
    /// Local Discovery Server (LDS / LDS-ME). The actual <c>FindServers</c> call is supplied as a delegate so
    /// the server-redundancy package stays free of a client dependency; the caller provides the discovery-client
    /// implementation (for example the sample uses <c>DiscoveryClient.FindServersAsync</c>). Discovered
    /// applications are published to the redundant server set; LDS yields OPC UA endpoints, not CRDT gossip
    /// endpoints, so <see cref="DiscoveredPeer.GossipEndpoint"/> is left unset.
    /// </summary>
    public sealed class LdsPeerDiscovery : IPeerDiscovery
    {
        /// <summary>
        /// Creates an LDS peer discovery.
        /// </summary>
        /// <param name="options">The LDS discovery options.</param>
        /// <param name="findServers">
        /// The <c>FindServers</c> delegate that queries the LDS and returns the known application descriptions.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> or <paramref name="findServers"/> is <c>null</c>.
        /// </exception>
        public LdsPeerDiscovery(
            LdsPeerDiscoveryOptions options,
            Func<CancellationToken, ValueTask<ArrayOf<ApplicationDescription>>> findServers)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_findServers = findServers ?? throw new ArgumentNullException(nameof(findServers));
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
        public async ValueTask<ArrayOf<DiscoveredPeer>> RefreshAsync(CancellationToken ct = default)
        {
            IReadOnlyList<DiscoveredPeer> discovered = await DiscoverAsync(ct).ConfigureAwait(false);
            return m_tracker.Apply(discovered);
        }

        private async ValueTask<IReadOnlyList<DiscoveredPeer>> DiscoverAsync(CancellationToken ct)
        {
            ArrayOf<ApplicationDescription> servers = await m_findServers(ct).ConfigureAwait(false);
            if (servers.IsNull || servers.Count == 0)
            {
                return [];
            }

            var peers = new List<DiscoveredPeer>(servers.Count);
            for (int i = 0; i < servers.Count; i++)
            {
                ApplicationDescription server = servers[i];
                if (server == null || string.IsNullOrEmpty(server.ApplicationUri))
                {
                    continue;
                }
                if (m_options.ServersOnly && !IsServer(server.ApplicationType))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(m_options.LocalApplicationUri) &&
                    string.Equals(server.ApplicationUri, m_options.LocalApplicationUri, StringComparison.Ordinal))
                {
                    continue;
                }

                peers.Add(new DiscoveredPeer(server.ApplicationUri, server.DiscoveryUrls));
            }

            return peers;
        }

        private static bool IsServer(ApplicationType type)
        {
            return type is ApplicationType.Server or ApplicationType.ClientAndServer;
        }

        private readonly LdsPeerDiscoveryOptions m_options;
        private readonly Func<CancellationToken, ValueTask<ArrayOf<ApplicationDescription>>> m_findServers;
        private readonly PeerSetTracker m_tracker = new();
    }
}
