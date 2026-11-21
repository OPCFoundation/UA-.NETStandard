// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: fluent registration of Kubernetes integration features on the
    /// <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class KubernetesServerBuilderExtensions
    {
        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers the shared in-cluster Kubernetes API client.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional Kubernetes API options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseKubernetes(
            this IOpcUaServerBuilder builder,
            Action<KubernetesServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new KubernetesServerOptions();
            configure?.Invoke(options);
            builder.Services.TryAddSingleton(_ => KubernetesApiClientFactory.Create(options));
            return builder;
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers Kubernetes Lease-backed leader election for ServiceLevel-driven
        /// server selection.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional leader election options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseKubernetesLeaderElection(
            this IOpcUaServerBuilder builder,
            Action<KubernetesLeaderElectionOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new KubernetesLeaderElectionOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton(_ => KubernetesApiClientFactory.Create(options.Kubernetes));
            builder.Services.TryAddSingleton<ISharedKeyValueStore>(_ => new InMemorySharedKeyValueStore());
            builder.Services.RemoveAll<ILeaderElection>();
            builder.Services.AddSingleton<ILeaderElection>(sp =>
            {
                IKubernetesApiClient apiClient = sp.GetRequiredService<IKubernetesApiClient>();
                if (apiClient.IsInCluster)
                {
                    ILogger<KubernetesLeaseLeaderElection>? logger = sp
                        .GetService<ILogger<KubernetesLeaseLeaderElection>>();
                    return new KubernetesLeaseLeaderElection(apiClient, options, logger: logger);
                }
                if (options.UseSharedStoreFallback)
                {
                    return new SharedStoreLeaseElection(
                        sp.GetRequiredService<ISharedKeyValueStore>(),
                        options.FallbackLeaseKey,
                        options.Kubernetes.NodeId,
                        options.LeaseDuration,
                        options.RenewInterval);
                }
                return new StaticLeaderElection(false);
            });

            return builder;
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers Kubernetes EndpointSlice peer discovery for a
        /// <c>RedundantServerSet</c>.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional peer discovery options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseKubernetesPeerDiscovery(
            this IOpcUaServerBuilder builder,
            Action<KubernetesPeerDiscoveryOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new KubernetesPeerDiscoveryOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton(_ => KubernetesApiClientFactory.Create(options.Kubernetes));
            builder.Services.TryAddSingleton<IKubernetesPeerDiscovery>(sp =>
                new KubernetesPeerDiscovery(sp.GetRequiredService<IKubernetesApiClient>(), options));
            // Bridge the Kubernetes discovery into the generic peer-discovery seam so the discovered
            // ServerUris update the redundant server set (published through FindServers) at runtime.
            return builder.UsePeerDiscovery(
                sp => new KubernetesPeerDiscoveryAdapter(sp.GetRequiredService<IKubernetesPeerDiscovery>()),
                pd => pd.RefreshInterval = options.RefreshInterval);
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers the Kubernetes readiness and liveness HTTP bridge that maps
        /// OPC UA <c>ServiceLevel</c> to pod readiness.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional readiness options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseKubernetesReadiness(
            this IOpcUaServerBuilder builder,
            Action<KubernetesReadinessOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new KubernetesReadinessOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton(sp =>
            {
                ILogger<KubernetesReadinessServer>? logger = sp.GetService<ILogger<KubernetesReadinessServer>>();
                return new KubernetesReadinessServer(
                    sp.GetRequiredService<IServiceLevelProvider>(),
                    options,
                    logger);
            });
            builder.Services.AddSingleton<IServerStartupTask>(sp =>
                new KubernetesReadinessStartupTask(sp.GetRequiredService<KubernetesReadinessServer>()));
            return builder;
        }
    }
}
