// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Builds non-transparent <c>RedundantServerSet</c> peer <see cref="ApplicationDescription"/> entries from
    /// <see cref="ServerRedundancyOptions"/> for <c>FindServers</c> (OPC 10000-4 §6.6.2.4.5.1).
    /// </summary>
    public sealed class ConfiguredRedundantServerSetProvider : IRedundantServerSetProvider
    {
        /// <summary>
        /// Creates the provider.
        /// </summary>
        /// <param name="options">The redundancy options.</param>
        public ConfiguredRedundantServerSetProvider(ServerRedundancyOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ArrayOf<ApplicationDescription> GetRedundantServerSet()
        {
            if (!m_options.IsNonTransparentMode)
            {
                return [];
            }

            var descriptions = new List<ApplicationDescription>(m_options.RedundantPeers.Count);
            foreach (RedundantPeer peer in m_options.RedundantPeers)
            {
                if (string.IsNullOrEmpty(peer.ApplicationUri) || peer.DiscoveryUrls.IsNull)
                {
                    continue;
                }

                descriptions.Add(new ApplicationDescription
                {
                    ApplicationUri = peer.ApplicationUri,
                    ApplicationName = peer.ApplicationName.IsNull
                        ? new LocalizedText(peer.ApplicationUri)
                        : peer.ApplicationName,
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = peer.DiscoveryUrls
                });
            }

            return new ArrayOf<ApplicationDescription>(descriptions.ToArray());
        }

        private readonly ServerRedundancyOptions m_options;
    }
}
