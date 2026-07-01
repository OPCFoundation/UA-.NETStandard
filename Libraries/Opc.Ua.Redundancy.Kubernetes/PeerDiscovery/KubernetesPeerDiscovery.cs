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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: EndpointSlice-backed Kubernetes peer discovery for
    /// non-transparent <c>RedundantServerSet</c> ServerUris.
    /// </summary>
    public sealed class KubernetesPeerDiscovery : IKubernetesPeerDiscovery
    {
        /// <summary>
        /// Creates a Kubernetes peer discovery service using the in-cluster API client.
        /// </summary>
        /// <param name="options">The peer discovery options.</param>
        public KubernetesPeerDiscovery(KubernetesPeerDiscoveryOptions options)
            : this(CreateApiClient(options), options)
        {
        }

        private static IKubernetesApiClient CreateApiClient(KubernetesPeerDiscoveryOptions options)
        {
            return KubernetesApiClientFactory.Create(
                (options ?? throw new ArgumentNullException(nameof(options))).Kubernetes);
        }

        internal KubernetesPeerDiscovery(IKubernetesApiClient apiClient, KubernetesPeerDiscoveryOptions options)
        {
            m_apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_namespace = KubernetesApiClientFactory.ResolveNamespace(m_options.Kubernetes, m_apiClient);
        }

        /// <inheritdoc/>
        public event Action<ArrayOf<string>>? PeerServerUrisChanged;

        /// <inheritdoc/>
        public ArrayOf<string> PeerServerUris
        {
            get
            {
                lock (m_lock)
                {
                    return m_peerServerUris;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<string>> RefreshAsync(CancellationToken ct = default)
        {
            if (!m_apiClient.IsInCluster)
            {
                SetPeers(ArrayOf<string>.Empty);
                return PeerServerUris;
            }

            KubernetesEndpointSliceList slices = await m_apiClient
                .ListEndpointSlicesAsync(m_namespace, m_options.ServiceName, ct)
                .ConfigureAwait(false);
            ArrayOf<string> peers = ToPeerUris(slices, m_options);
            SetPeers(peers);
            return peers;
        }

        /// <summary>
        /// Copies discovered peer ServerUris into the base redundancy options.
        /// </summary>
        /// <param name="options">The redundancy options to populate.</param>
        public void Populate(ServerRedundancyOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.PeerServerUris.Clear();
            foreach (string uri in PeerServerUris.Span)
            {
                options.PeerServerUris.Add(uri);
            }
        }

        internal static ArrayOf<string> ToPeerUris(
            KubernetesEndpointSliceList slices,
            KubernetesPeerDiscoveryOptions options)
        {
            if (slices == null)
            {
                throw new ArgumentNullException(nameof(slices));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var uris = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KubernetesEndpointSlice slice in slices.Items)
            {
                int port = SelectPort(slice, options);
                foreach (KubernetesEndpoint endpoint in slice.Endpoints)
                {
                    if (endpoint.Conditions?.Ready == false)
                    {
                        continue;
                    }

                    foreach (string address in endpoint.Addresses)
                    {
                        if (string.IsNullOrWhiteSpace(address) ||
                            string.Equals(address, options.LocalAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        uris.Add(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{options.UriScheme}://{address}:{port}"));
                    }
                }
            }

            return new ArrayOf<string>(uris.ToArray().AsMemory());
        }

        private static int SelectPort(KubernetesEndpointSlice slice, KubernetesPeerDiscoveryOptions options)
        {
            KubernetesEndpointPort? named = slice.Ports.FirstOrDefault(port =>
                string.Equals(port.Name, options.PortName, StringComparison.OrdinalIgnoreCase));
            return named?.Port ?? slice.Ports.FirstOrDefault(port => port.Port.HasValue)?.Port ?? options.Port;
        }

        private void SetPeers(ArrayOf<string> peers)
        {
            bool changed;
            lock (m_lock)
            {
                changed = !m_peerServerUris.Span.SequenceEqual(peers.Span);
                m_peerServerUris = peers;
            }
            if (changed)
            {
                PeerServerUrisChanged?.Invoke(peers);
            }
        }

        private readonly IKubernetesApiClient m_apiClient;
        private readonly KubernetesPeerDiscoveryOptions m_options;
        private readonly string m_namespace;
        private readonly Lock m_lock = new();
        private ArrayOf<string> m_peerServerUris = ArrayOf<string>.Empty;
    }
}