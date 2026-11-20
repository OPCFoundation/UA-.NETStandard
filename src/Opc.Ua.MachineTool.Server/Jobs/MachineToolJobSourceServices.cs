// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Registers the optional reference source as one coherent, independently owned source per DI scope.
    /// Applications can instead construct it directly and pass the same instance to both machine bindings.
    /// </summary>
    public static class MachineToolJobSourceServices
    {
        /// <summary>
        /// Registers the source and its existing provider facets with scoped lifetime. Use one scope per machine;
        /// the scope owns the source, while its TimeProvider and telemetry services remain borrowed.
        /// </summary>
        public static IServiceCollection AddMachineToolJobSource(
            this IServiceCollection services,
            MachineToolJobSourceOptions options,
            MachineToolSnapshot? monitoring = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            services.AddScoped(provider => new MachineToolJobSource(options, monitoring,
                provider.GetService<TimeProvider>(), provider.GetService<ITelemetryContext>()));
            services.AddScoped<IMachineToolProvider>(Resolve);
            services.AddScoped<IIsa95JobOrderReceiverV2>(Resolve);
            services.AddScoped<IIsa95JobResponseProviderV2>(Resolve);
            services.AddScoped<IIsa95JobResponseReceiverV2>(Resolve);
            services.AddScoped<IIsa95JobExecutionController>(Resolve);
            services.AddScoped<IIsa95JobOrderCatalog>(Resolve);
            services.AddScoped<IIsa95JobResponseCatalog>(Resolve);
            services.AddScoped<IIsa95JobStatusSourceV2>(Resolve);
            services.AddScoped<IIsa95JobOrderCatalogChangeSource>(Resolve);
            services.AddScoped<IIsa95JobResponseChangeSource>(Resolve);
            return services;
        }

        /// <summary>
        /// Resolves exactly the same source for every facet within a machine scope.
        /// </summary>
        private static MachineToolJobSource Resolve(IServiceProvider provider) =>
            provider.GetRequiredService<MachineToolJobSource>();
    }
}
