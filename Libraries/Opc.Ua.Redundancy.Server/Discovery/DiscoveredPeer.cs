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
