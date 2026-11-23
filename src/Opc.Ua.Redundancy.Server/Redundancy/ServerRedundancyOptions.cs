// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Options used to publish the local Server's OPC 10000-4 §6.6.2 redundancy metadata in the standard
    /// <c>Server.ServerRedundancy</c> nodes.
    /// </summary>
    public sealed class ServerRedundancyOptions
    {
        /// <summary>
        /// Gets or sets the OPC UA redundancy mode reported by
        /// <c>Server.ServerRedundancy.RedundancySupport</c>.
        /// </summary>
        public RedundancySupport Mode
        {
            get => m_mode;
            set => m_mode = value;
        }

        /// <summary>
        /// Gets or sets an optional shared, non-secret seed for deterministic EventIds in Transparent
        /// or HotAndMirrored mode. By default, the replica node identity is used when configured;
        /// otherwise a fixed domain seed is used.
        /// </summary>
        /// <remarks>
        /// Use the same seed, logical identities and source occurrence timestamps on every replica.
        /// This does not replicate the transitions, condition branches or acknowledgement state.
        /// </remarks>
        public string? DeterministicEventIdSeed { get; set; }

        /// <summary>
        /// Gets the peer ServerIds in Transparent mode. Use each peer's <see cref="CurrentServerId"/>,
        /// not its ApplicationUri. In non-transparent modes this is the URI-only fallback for legacy
        /// configuration that cannot provide discovery URLs; prefer <see cref="RedundantPeers"/>.
        /// </summary>
        public IList<string> PeerServerUris { get; } = [];

        /// <summary>
        /// Gets the canonical rich peer descriptions used by <c>FindServers</c> for a non-transparent
        /// <c>RedundantServerSet</c> (OPC 10000-4 §6.6.2.4.5.1) and by the flat redundancy URI variables.
        /// </summary>
        public IList<RedundantPeer> RedundantPeers { get; } = [];

        /// <summary>
        /// Gets or sets this Server's identifier within a Transparent <c>RedundantServerSet</c>.
        /// </summary>
        public string CurrentServerId { get; set; } = Environment.MachineName;

        /// <summary>
        /// Retained for compatibility. Peer service levels are now read from live status records.
        /// </summary>
        [Obsolete("Peer service levels are read from live status records; this setting has no effect.")]
        public byte PeerServiceLevel { get; set; } = ServiceLevels.Maximum;

        /// <summary>
        /// Gets or sets how often a server publishes its status and refreshes the peer array.
        /// </summary>
        public TimeSpan PeerStatusUpdateInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets or sets how long a peer status record is valid. Must exceed the update interval.
        /// After expiry, the peer is reported as <see cref="ServerState.Unknown"/> with service level 0.
        /// </summary>
        public TimeSpan PeerStatusLifetime { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets a value indicating whether non-transparent modes add
        /// the <c>NTRS</c> discovery capability.
        /// </summary>
        public bool AdvertiseNtrsCapability { get; set; } = true;

        /// <summary>
        /// Gets a value indicating whether the configured mode is non-transparent redundancy.
        /// </summary>
        public bool IsNonTransparentMode =>
            Mode is RedundancySupport.Cold or
                RedundancySupport.Warm or
                RedundancySupport.Hot or
                RedundancySupport.HotAndMirrored;

        /// <summary>
        /// Adds a canonical redundant peer description.
        /// </summary>
        /// <param name="applicationUri">The peer application URI.</param>
        /// <param name="discoveryUrls">The peer discovery URLs.</param>
        /// <returns>The added peer.</returns>
        public RedundantPeer AddRedundantPeer(string applicationUri, ArrayOf<string> discoveryUrls)
        {
            var peer = new RedundantPeer(applicationUri, discoveryUrls);
            RedundantPeers.Add(peer);
            return peer;
        }

        /// <summary>
        /// Gets the peer application URIs used for redundancy variables, derived from the canonical
        /// <see cref="RedundantPeers"/> list plus the URI-only <see cref="PeerServerUris"/> fallback.
        /// </summary>
        /// <returns>The configured peer application URIs.</returns>
        public ArrayOf<string> GetPeerApplicationUris()
        {
            return new ArrayOf<string>(RedundantPeers
                .Select(peer => peer.ApplicationUri)
                .Concat(PeerServerUris)
                .Where(uri => !string.IsNullOrEmpty(uri))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        }

        /// <summary>
        /// Gets the configured array identifiers for the specified mode.
        /// </summary>
        internal ArrayOf<string> GetPeerIds(RedundancySupport mode)
        {
            return mode == RedundancySupport.Transparent
                ? new ArrayOf<string>(PeerServerUris
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray())
                : GetPeerApplicationUris();
        }

        private volatile RedundancySupport m_mode;
    }
}
