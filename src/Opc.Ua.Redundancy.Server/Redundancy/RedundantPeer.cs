// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Describes a peer Server in a non-transparent <c>RedundantServerSet</c>.
    /// </summary>
    public sealed class RedundantPeer
    {
        /// <summary>
        /// Creates an empty peer description.
        /// </summary>
        public RedundantPeer()
        {
        }

        /// <summary>
        /// Creates a peer description.
        /// </summary>
        /// <param name="applicationUri">The peer ServerUri / ApplicationUri.</param>
        /// <param name="discoveryUrls">The peer discovery URLs.</param>
        public RedundantPeer(string applicationUri, ArrayOf<string> discoveryUrls)
        {
            ApplicationUri = applicationUri;
            DiscoveryUrls = discoveryUrls;
        }

        /// <summary>
        /// Gets or sets the peer ServerUri / ApplicationUri.
        /// </summary>
        public string ApplicationUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the peer Server application name.
        /// </summary>
        public LocalizedText ApplicationName { get; set; } = LocalizedText.Null;

        /// <summary>
        /// Gets or sets the peer discovery URLs.
        /// </summary>
        public ArrayOf<string> DiscoveryUrls { get; set; } = [];
    }
}
