// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Net;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for the dependency-independent <see cref="DnsPeerDiscovery"/>.
    /// It resolves a single DNS name (for example a Kubernetes headless-service name, which yields one A/AAAA
    /// record per replica) into the redundant peer set.
    /// </summary>
    public sealed class DnsPeerDiscoveryOptions
    {
        /// <summary>
        /// Gets or sets the DNS name that resolves to one address per replica.
        /// </summary>
        public string HostName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the CRDT gossip port used to build each peer's gossip endpoint.
        /// </summary>
        public int GossipPort { get; set; } = 4840;

        /// <summary>
        /// Gets or sets the OPC UA endpoint port used to build each peer's discovery URL.
        /// </summary>
        public int ApplicationPort { get; set; } = 4840;

        /// <summary>
        /// Gets or sets the transport scheme used to build each peer's discovery URL.
        /// </summary>
        public string ApplicationScheme { get; set; } = Utils.UriSchemeOpcTcp;

        /// <summary>
        /// Gets or sets a value indicating whether resolved addresses that belong to the local host are
        /// excluded (so a replica does not list itself as a peer).
        /// </summary>
        public bool ExcludeLocalAddresses { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether a gossip endpoint is attached to each discovered peer. Set
        /// <c>false</c> to publish only client-facing discovery information.
        /// </summary>
        public bool IncludeGossipEndpoint { get; set; } = true;

        /// <summary>
        /// Builds a discovery URL for a resolved peer address.
        /// </summary>
        /// <param name="address">The peer address.</param>
        /// <returns>The discovery URL.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> is <c>null</c>.</exception>
        public string BuildDiscoveryUrl(IPAddress address)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }
            string host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"[{address}]"
                : address.ToString();
            return string.Concat(ApplicationScheme, "://", host, ":", ApplicationPort.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
