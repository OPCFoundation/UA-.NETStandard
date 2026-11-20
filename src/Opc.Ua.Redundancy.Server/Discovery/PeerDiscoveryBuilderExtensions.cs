/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: fluent registration of dynamic peer discovery. A discovery mechanism
    /// (static configuration, DNS, an LDS(-ME), Kubernetes, …) keeps the redundant server set — and, for an
    /// active/active deployment, the CRDT gossip fabric — current at runtime. Static configuration is the
    /// fallback used when no dynamic mechanism is available.
    /// </summary>
    public static class PeerDiscoveryBuilderExtensions
    {
        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers a peer-discovery mechanism, the mutable redundant server
        /// set it feeds (published through <c>FindServers</c>), the shared gossip peer sink, and the refresh
        /// loop that applies discovered peers at runtime.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="discoveryFactory">Creates the discovery mechanism from the service provider.</param>
        /// <param name="configure">Optional refresh-loop options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="discoveryFactory"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder UsePeerDiscovery(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, IPeerDiscovery> discoveryFactory,
            Action<PeerDiscoveryOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (discoveryFactory == null)
            {
                throw new ArgumentNullException(nameof(discoveryFactory));
            }

            var options = new PeerDiscoveryOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton(discoveryFactory);
            builder.Services.TryAddSingleton<DiscoveredRedundantServerSetProvider>();
            builder.Services.AddSingleton<IRedundantServerSetProvider>(sp =>
            {
                DiscoveredRedundantServerSetProvider discovered = sp.GetRequiredService<DiscoveredRedundantServerSetProvider>();
                // Static configuration (ServerRedundancyOptions from AddServerRedundancy) is the fallback used
                // until dynamic discovery finds peers.
                ServerRedundancyOptions? configured = sp.GetService<ServerRedundancyOptions>();
                return configured == null
                    ? discovered
                    : new CompositeRedundantServerSetProvider(
                        discovered, new ConfiguredRedundantServerSetProvider(configured));
            });
            builder.Services.TryAddSingleton<GossipPeerRegistry>();
            builder.Services.TryAddSingleton<IGossipPeerSink>(
                sp => sp.GetRequiredService<GossipPeerRegistry>());
            builder.Services.AddSingleton<IServerStartupTask>(sp =>
                new PeerDiscoveryStartupTask(
                    sp.GetRequiredService<IPeerDiscovery>(),
                    sp.GetRequiredService<DiscoveredRedundantServerSetProvider>(),
                    options,
                    sp.GetService<IGossipPeerSink>()));

            return builder;
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers the fallback static peer set as an
        /// <see cref="IPeerDiscovery"/> mechanism.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="peers">The configured peers.</param>
        /// <param name="configure">Optional refresh-loop options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="peers"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder UseStaticPeerDiscovery(
            this IOpcUaServerBuilder builder,
            IEnumerable<DiscoveredPeer> peers,
            Action<PeerDiscoveryOptions>? configure = null)
        {
            if (peers == null)
            {
                throw new ArgumentNullException(nameof(peers));
            }

            var snapshot = new List<DiscoveredPeer>(peers);
            return builder.UsePeerDiscovery(_ => new StaticPeerDiscovery(snapshot), configure);
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers dependency-independent DNS peer discovery (one A/AAAA
        /// record per replica, for example a Kubernetes headless service).
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configureDns">Configures the DNS discovery options.</param>
        /// <param name="configure">Optional refresh-loop options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="configureDns"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder UseDnsPeerDiscovery(
            this IOpcUaServerBuilder builder,
            Action<DnsPeerDiscoveryOptions> configureDns,
            Action<PeerDiscoveryOptions>? configure = null)
        {
            if (configureDns == null)
            {
                throw new ArgumentNullException(nameof(configureDns));
            }

            var dnsOptions = new DnsPeerDiscoveryOptions();
            configureDns(dnsOptions);
            return builder.UsePeerDiscovery(_ => new DnsPeerDiscovery(dnsOptions), configure);
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers Local Discovery Server (LDS / LDS-ME) peer discovery.
        /// The <paramref name="findServers"/> delegate performs the <c>FindServers</c> call, keeping the
        /// server-redundancy package free of a client dependency.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="findServers">The <c>FindServers</c> delegate that queries the LDS.</param>
        /// <param name="configureLds">Optional LDS discovery options.</param>
        /// <param name="configure">Optional refresh-loop options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="findServers"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder UseLdsPeerDiscovery(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, Func<CancellationToken, ValueTask<ArrayOf<ApplicationDescription>>>> findServers,
            Action<LdsPeerDiscoveryOptions>? configureLds = null,
            Action<PeerDiscoveryOptions>? configure = null)
        {
            if (findServers == null)
            {
                throw new ArgumentNullException(nameof(findServers));
            }

            var ldsOptions = new LdsPeerDiscoveryOptions();
            configureLds?.Invoke(ldsOptions);
            return builder.UsePeerDiscovery(
                sp => new LdsPeerDiscovery(ldsOptions, findServers(sp)), configure);
        }
    }
}
