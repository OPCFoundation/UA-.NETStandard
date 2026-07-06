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
                ? string.Concat("[", address.ToString(), "]")
                : address.ToString();
            return string.Concat(ApplicationScheme, "://", host, ":", ApplicationPort.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
