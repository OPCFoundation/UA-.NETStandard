// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Server
{
    /// <summary>
    /// Dependency injection registration helpers for the ISA-95 Job Control
    /// provider layer. Registering the provider is optional; the
    /// <see cref="InMemoryIsa95JobControlProvider"/> constructor is public and can
    /// be used directly as a fallback.
    /// </summary>
    public static class Isa95JobControlServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the <see cref="InMemoryIsa95JobControlProvider"/> as a
        /// singleton together with all of its V1 and V2 provider facets. When a
        /// <see cref="TimeProvider"/> is registered it is used; otherwise
        /// <see cref="TimeProvider.System"/> is used.
        /// </summary>
        /// <param name="services">
        /// The service collection to register with.
        /// </param>
        /// <param name="configure">
        /// An optional callback used to configure the bounded options.
        /// </param>
        /// <returns>
        /// The same service collection to allow chaining.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddInMemoryIsa95JobControlProvider(
            this IServiceCollection services,
            Action<Isa95JobControlProviderOptions>? configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton(serviceProvider =>
            {
                var options = new Isa95JobControlProviderOptions();
                configure?.Invoke(options);
                TimeProvider timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
                return new InMemoryIsa95JobControlProvider(options, timeProvider);
            });

            services.TryAddSingleton<IIsa95JobOrderReceiverV1>(Resolve);
            services.TryAddSingleton<IIsa95JobResponseProviderV1>(Resolve);
            services.TryAddSingleton<IIsa95JobResponseReceiverV1>(Resolve);
            services.TryAddSingleton<IIsa95JobOrderReceiverV2>(Resolve);
            services.TryAddSingleton<IIsa95JobResponseProviderV2>(Resolve);
            services.TryAddSingleton<IIsa95JobResponseReceiverV2>(Resolve);
            services.TryAddSingleton<IIsa95JobStatusSourceV2>(Resolve);
            services.TryAddSingleton<IIsa95JobExecutionController>(Resolve);
            services.TryAddSingleton<IIsa95JobOrderCatalog>(Resolve);
            services.TryAddSingleton<IIsa95JobResponseCatalog>(Resolve);
            services.TryAddSingleton<IIsa95JobOrderCatalogChangeSource>(Resolve);
            services.TryAddSingleton<IIsa95JobResponseChangeSource>(Resolve);

            return services;
        }

        private static InMemoryIsa95JobControlProvider Resolve(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetRequiredService<InMemoryIsa95JobControlProvider>();
        }
    }
}
