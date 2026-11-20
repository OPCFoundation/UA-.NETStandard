/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Redundancy;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers process-local lease publication services.
    /// </summary>
    public static class LeasePublicationServiceCollectionExtensions
    {
        /// <summary>
        /// Registers one process-local lease publication authority shared by this service provider.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
        /// <remarks>
        /// This registration coordinates only participants that resolve the same authority instance. It does not
        /// provide durable or cross-process high availability.
        /// </remarks>
        public static IServiceCollection AddInMemoryLeasePublicationAuthority(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.TryAddSingleton<ILeasePublicationAuthority, InMemoryLeasePublicationAuthority>();
            return services;
        }

        /// <summary>
        /// Registers the supplied in-memory authority as the authority shared by this service provider.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="authority">The authority instance to register.</param>
        /// <returns>The service collection.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> or <paramref name="authority"/> is null.
        /// </exception>
        public static IServiceCollection AddInMemoryLeasePublicationAuthority(
            this IServiceCollection services,
            InMemoryLeasePublicationAuthority authority)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.TryAddSingleton<ILeasePublicationAuthority>(authority);
            return services;
        }
    }
}
