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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a dependency-independent <see cref="IPeerDiscovery"/> that resolves a
    /// single DNS name into the redundant peer set. A headless service (one A/AAAA record per replica, as
    /// published by Kubernetes and most service meshes) is the intended source, so no cloud-provider SDK is
    /// required. Addresses that belong to the local host are excluded by default so a replica never lists
    /// itself.
    /// </summary>
    public sealed class DnsPeerDiscovery : IPeerDiscovery
    {
        /// <summary>
        /// Creates a DNS peer discovery.
        /// </summary>
        /// <param name="options">The DNS discovery options.</param>
        /// <param name="resolver">
        /// An optional DNS resolver override (for testing). Defaults to <c>Dns.GetHostAddressesAsync</c>.
        /// </param>
        /// <param name="localAddresses">
        /// An optional local-address provider override (for testing). Defaults to resolving the local host name.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public DnsPeerDiscovery(
            DnsPeerDiscoveryOptions options,
            Func<string, CancellationToken, ValueTask<IPAddress[]>>? resolver = null,
            Func<CancellationToken, ValueTask<ISet<IPAddress>>>? localAddresses = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_resolver = resolver ?? DefaultResolveAsync;
            m_localAddresses = localAddresses ?? DefaultLocalAddressesAsync;
        }

        /// <inheritdoc/>
        public event Action<ArrayOf<DiscoveredPeer>>? PeersChanged
        {
            add => m_tracker.Changed += value;
            remove => m_tracker.Changed -= value;
        }

        /// <inheritdoc/>
        public ArrayOf<DiscoveredPeer> Peers => m_tracker.Current;

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DiscoveredPeer>> RefreshAsync(CancellationToken ct = default)
        {
            IReadOnlyList<DiscoveredPeer> discovered = await DiscoverAsync(ct).ConfigureAwait(false);
            return m_tracker.Apply(discovered);
        }

        private async ValueTask<IReadOnlyList<DiscoveredPeer>> DiscoverAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(m_options.HostName))
            {
                return Array.Empty<DiscoveredPeer>();
            }

            ISet<IPAddress> local = m_options.ExcludeLocalAddresses
                ? await m_localAddresses(ct).ConfigureAwait(false)
                : s_noAddresses;

            IPAddress[] addresses = await m_resolver(m_options.HostName, ct).ConfigureAwait(false);

            var peers = new List<DiscoveredPeer>(addresses.Length);
            var seen = new HashSet<IPAddress>();
            foreach (IPAddress address in addresses)
            {
                if (m_options.ExcludeLocalAddresses && local.Contains(address))
                {
                    continue;
                }
                if (!seen.Add(address))
                {
                    continue;
                }

                string discoveryUrl = m_options.BuildDiscoveryUrl(address);
                string[] discoveryUrls = [discoveryUrl];
                IPEndPoint? gossip = m_options.IncludeGossipEndpoint
                    ? new IPEndPoint(address, m_options.GossipPort)
                    : null;
                peers.Add(new DiscoveredPeer(discoveryUrl, discoveryUrls, gossip));
            }

            return peers;
        }

        private static async ValueTask<IPAddress[]> DefaultResolveAsync(string host, CancellationToken ct)
        {
#if NET6_0_OR_GREATER
            return await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
#else
            ct.ThrowIfCancellationRequested();
            return await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
#endif
        }

        private static async ValueTask<ISet<IPAddress>> DefaultLocalAddressesAsync(CancellationToken ct)
        {
            var local = new HashSet<IPAddress>
            {
                IPAddress.Loopback,
                IPAddress.IPv6Loopback
            };
            try
            {
#if NET6_0_OR_GREATER
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(Dns.GetHostName(), ct)
                    .ConfigureAwait(false);
#else
                ct.ThrowIfCancellationRequested();
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(Dns.GetHostName())
                    .ConfigureAwait(false);
#endif
                foreach (IPAddress address in addresses)
                {
                    local.Add(address);
                }
            }
            catch (SocketException)
            {
                // Fall back to loopback-only when the local host name cannot be resolved.
            }
            return local;
        }

        private static readonly ISet<IPAddress> s_noAddresses = new HashSet<IPAddress>();
        private readonly DnsPeerDiscoveryOptions m_options;
        private readonly Func<string, CancellationToken, ValueTask<IPAddress[]>> m_resolver;
        private readonly Func<CancellationToken, ValueTask<ISet<IPAddress>>> m_localAddresses;
        private readonly PeerSetTracker m_tracker = new();
    }
}
