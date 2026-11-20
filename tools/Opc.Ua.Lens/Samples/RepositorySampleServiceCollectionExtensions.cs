/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UaLens.Samples
{
    /// <summary>
    /// Explicit opt-in registration only; no hosted service, restore hook or new tool kind.
    /// The parent owns operation authorization, trusted-root selection and UI integration.
    /// Dispose the provider asynchronously after stopping any owned sample.
    /// </summary>
    internal static class RepositorySampleServiceCollectionExtensions
    {
        public static IServiceCollection AddUaLensRepositorySamples(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<RepositorySampleFiles>();
            services.TryAddSingleton<IRepositorySamplePortBinder, RepositorySampleSocketBinder>();
            services.TryAddSingleton<IRepositorySamplePortAllocator, RepositorySamplePortAllocator>();
            services.TryAddSingleton<IRepositorySampleRuntime, RepositorySampleRuntime>();
            services.TryAddSingleton<IRepositorySampleProbe, RepositorySampleDiscoveryProbe>();
            services.TryAddSingleton<IRepositorySampleService>(provider => new RepositorySampleService(
                provider.GetRequiredService<RepositorySampleFiles>(),
                provider.GetRequiredService<IRepositorySamplePortAllocator>(),
                provider.GetRequiredService<IRepositorySampleRuntime>(),
                provider.GetRequiredService<IRepositorySampleProbe>(),
                provider.GetRequiredService<TimeProvider>()));
            return services;
        }
    }
}
