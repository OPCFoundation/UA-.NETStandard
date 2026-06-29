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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Redundancy;

using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.K8s
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
            builder.Services.AddSingleton<IServerStartupTask>(sp =>
                new KubernetesPeerDiscoveryStartupTask(
                    sp.GetRequiredService<IKubernetesPeerDiscovery>(),
                    options));
            return builder;
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

            builder.Services.TryAddSingleton<IServiceLevelProvider>(_ => new ConstantServiceLevelProvider());
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