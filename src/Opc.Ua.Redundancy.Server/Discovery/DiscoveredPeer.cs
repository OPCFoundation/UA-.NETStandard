// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Net;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: one discovered redundant-server peer. It carries the client-facing
    /// discovery identity (<see cref="ServerUri"/> + <see cref="DiscoveryUrls"/> published through
    /// <c>FindServers</c>) and, optionally, the CRDT <see cref="GossipEndpoint"/> used to wire the replication
    /// fabric.
    /// </summary>
    public sealed class DiscoveredPeer
    {
        /// <summary>
        /// Creates a discovered peer.
        /// </summary>
        /// <param name="serverUri">The peer's ApplicationUri / ServerUri.</param>
        /// <param name="discoveryUrls">The peer's discovery URLs, if known.</param>
        /// <param name="gossipEndpoint">The peer's CRDT gossip endpoint, if known.</param>
        /// <exception cref="ArgumentException"><paramref name="serverUri"/> is null or empty.</exception>
        public DiscoveredPeer(
            string serverUri,
            ArrayOf<string> discoveryUrls = default,
            IPEndPoint? gossipEndpoint = null)
        {
            if (string.IsNullOrEmpty(serverUri))
            {
                throw new ArgumentException("A discovered peer requires a ServerUri.", nameof(serverUri));
            }
            ServerUri = serverUri;
            DiscoveryUrls = discoveryUrls;
            GossipEndpoint = gossipEndpoint;
        }

        /// <summary>
        /// Gets the peer's ApplicationUri / ServerUri.
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// Gets the peer's discovery URLs published to clients through <c>FindServers</c>.
        /// </summary>
        public ArrayOf<string> DiscoveryUrls { get; }

        /// <summary>
        /// Gets the peer's CRDT gossip endpoint, or <c>null</c> when the discovery mechanism only yields
        /// client-facing discovery information.
        /// </summary>
        public IPEndPoint? GossipEndpoint { get; }
    }
}
