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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: adapts the Kubernetes EndpointSlice discovery
    /// (<see cref="IKubernetesPeerDiscovery"/>, which yields client-facing OPC UA ServerUris) to the generic
    /// <see cref="IPeerDiscovery"/> seam so the discovered peers feed the redundant server set published through
    /// <c>FindServers</c>. Kubernetes discovers OPC UA endpoints, not CRDT gossip endpoints, so
    /// <see cref="DiscoveredPeer.GossipEndpoint"/> is left unset; pair this with DNS discovery of the gossip
    /// headless service to also wire the active/active gossip fabric.
    /// </summary>
    public sealed class KubernetesPeerDiscoveryAdapter : IPeerDiscovery
    {
        /// <summary>
        /// Creates an adapter over a Kubernetes peer discovery.
        /// </summary>
        /// <param name="inner">The Kubernetes discovery to adapt.</param>
        /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <c>null</c>.</exception>
        public KubernetesPeerDiscoveryAdapter(IKubernetesPeerDiscovery inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_inner.PeerServerUrisChanged += OnPeerServerUrisChanged;
        }

        /// <inheritdoc/>
        public event Action<ArrayOf<DiscoveredPeer>>? PeersChanged;

        /// <inheritdoc/>
        public ArrayOf<DiscoveredPeer> Peers => Convert(m_inner.PeerServerUris);

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DiscoveredPeer>> RefreshAsync(CancellationToken ct = default)
        {
            ArrayOf<string> uris = await m_inner.RefreshAsync(ct).ConfigureAwait(false);
            return Convert(uris);
        }

        private void OnPeerServerUrisChanged(ArrayOf<string> uris)
        {
            PeersChanged?.Invoke(Convert(uris));
        }

        private static ArrayOf<DiscoveredPeer> Convert(ArrayOf<string> uris)
        {
            if (uris.IsNull || uris.Count == 0)
            {
                return [];
            }

            var peers = new List<DiscoveredPeer>(uris.Count);
            for (int i = 0; i < uris.Count; i++)
            {
                string uri = uris[i];
                if (!string.IsNullOrEmpty(uri))
                {
                    peers.Add(new DiscoveredPeer(uri, new[] { uri }));
                }
            }
            return peers.ToArray();
        }

        private readonly IKubernetesPeerDiscovery m_inner;
    }
}
