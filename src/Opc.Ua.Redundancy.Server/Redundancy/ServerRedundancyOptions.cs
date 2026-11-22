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
        public RedundancySupport Mode { get; set; } = RedundancySupport.None;

        /// <summary>
        /// Gets URI-only fallback peer ServerUris for legacy configuration that cannot provide discovery URLs.
        /// Prefer <see cref="RedundantPeers"/> or <see cref="AddRedundantPeer"/> as the canonical redundancy
        /// peer input; those entries drive both <c>FindServers</c> and the flat redundancy URI variables.
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
        /// Gets or sets the <c>ServiceLevel</c> published for each configured peer Server in
        /// <c>Server.ServerRedundancy.RedundantServerArray</c>.
        /// </summary>
        public byte PeerServiceLevel { get; set; } = ServiceLevels.Maximum;

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
    }
}
