// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Redundancy;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Configures independently protected freshness for shared records.
    /// </summary>
    public static class SharedRecordFreshnessServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a freshness-protected view over the configured shared payload store.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="authorityStoreFactory">
        /// Resolves the separately configured durable authority. The factory must use a store
        /// with access controls that the payload-store writer cannot modify.
        /// </param>
        /// <param name="deploymentId">The deployment identity shared by participating replicas.</param>
        /// <param name="epoch">The enrolled authority epoch shared by participating replicas.</param>
        /// <returns>The service collection.</returns>
        /// <remarks>
        /// Registers <see cref="FreshnessProtectedKeyValueStore"/> independently of
        /// <see cref="ISharedKeyValueStore"/>. Enroll the authority before starting the deployment.
        /// The dependency provider or caller owns the stores and the record protector.
        /// </remarks>
        public static IServiceCollection AddSharedRecordFreshness(
            this IServiceCollection services,
            Func<IServiceProvider, ISharedKeyValueStore> authorityStoreFactory,
            Guid deploymentId,
            Guid epoch)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (authorityStoreFactory == null)
            {
                throw new ArgumentNullException(nameof(authorityStoreFactory));
            }

            return services.AddSingleton(provider => new FreshnessProtectedKeyValueStore(
                provider.GetRequiredService<ISharedKeyValueStore>(),
                authorityStoreFactory(provider),
                provider.GetRequiredService<IRecordProtector>(),
                deploymentId,
                epoch));
        }
    }
}
