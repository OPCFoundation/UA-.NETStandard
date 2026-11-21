// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Fluent registration of the extension-beyond-§6.6 <c>GetEndpoints</c> load-direction feature that directs a
    /// Client to the best member of a <c>RedundantServerSet</c>.
    /// </summary>
    public static class LoadDirectionBuilderExtensions
    {
        /// <summary>
        /// Registers <c>GetEndpoints</c> load direction: peers publish their health <c>ServiceLevel</c>, load weight,
        /// and endpoints to the shared store, and a request on the configured balancing discovery URL is answered with
        /// the best peer's endpoints. Requires a shared store (register <c>UseReplicatedAddressSpace</c> /
        /// <c>UseRedundancyConsistency</c>); pair with <c>AddServerServiceLevel(...)</c> for a real health signal and
        /// register an <see cref="ILoadWeightProvider"/> for load-aware balancing. It complements — and never
        /// replaces — the standard client-driven <c>RedundantServerArray</c> / <c>ServiceLevel</c> Failover.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the load-direction options (at minimum the balancing URL).</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseServerLoadDirection(
            this IOpcUaServerBuilder builder,
            Action<LoadDirectionOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new LoadDirectionOptions();
            configure?.Invoke(options);
            builder.Services.AddSingleton(options);
            builder.Services.TryAddSingleton<ILoadWeightProvider>(_ => new ConstantLoadWeightProvider());

            if (options.StrongEligibility)
            {
                builder.Services.AddSingleton<IStrongKeyspaceProvider>(
                    new LoadDirectionStrongKeyspaceProvider(options));
            }

            builder.Services.AddSingleton(sp => new ServerLoadDirector(
                ResolveServiceLevelProvider(sp),
                sp.GetRequiredService<ILoadWeightProvider>(),
                options,
                sp.GetService<ILogger<ServerLoadDirector>>()));
            builder.Services.AddSingleton<IGetEndpointsDirector>(
                sp => sp.GetRequiredService<ServerLoadDirector>());

            builder.Services.AddSingleton<IServerStartupTask>(sp => new LoadDirectionStartupTask(
                sp.GetRequiredService<ISharedKeyValueStore>(),
                ResolveProtector(sp),
                options,
                sp.GetRequiredService<ServerLoadDirector>(),
                ResolveTimeProvider(sp)));
            builder.Services.AddSingleton<IServerStartupTask>(sp => new PeerDirectionPublishStartupTask(
                sp.GetRequiredService<ISharedKeyValueStore>(),
                ResolveProtector(sp),
                options,
                ResolveServiceLevelProvider(sp),
                sp.GetRequiredService<ILoadWeightProvider>(),
                ResolveTimeProvider(sp)));

            return builder;
        }

        private static IServiceLevelProvider ResolveServiceLevelProvider(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<IServiceLevelProvider>() ?? new ConstantServiceLevelProvider();
        }

        private static IRecordProtector ResolveProtector(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<IRecordProtector>() ?? NullRecordProtector.Instance;
        }

        private static TimeProvider ResolveTimeProvider(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        }
    }
}
