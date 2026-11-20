// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Client;

namespace Opc.Ua.PlasticsRubber.Client
{
    /// <summary>
    /// Creates instance-scoped clients with injectable common limits and caller-owned sessions.
    /// </summary>
    public sealed class ProductionDatasetClientFactory
    {
        /// <summary>
        /// Captures immutable client limits.
        /// </summary>
        public ProductionDatasetClientFactory(ProductionDatasetClientOptions? options = null)
        {
            m_options = options ?? new ProductionDatasetClientOptions();
            m_options.Validate();
        }

        /// <summary>
        /// Creates a typed client without connecting, elevating identity, or taking session ownership.
        /// </summary>
        public ProductionDatasetClient Create(ISession session, NodeId managementId)
        {
            return new ProductionDatasetClient(session, managementId, m_options);
        }

        /// <summary>
        /// The shared immutable bounds.
        /// </summary>
        private readonly ProductionDatasetClientOptions m_options;
    }

    /// <summary>
    /// Adds the dataset client factory to the existing application service collection.
    /// </summary>
    public static class ProductionDatasetClientServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the client factory while preserving existing application registrations.
        /// </summary>
        public static IServiceCollection AddProductionDatasetClient(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton<ProductionDatasetClientFactory>();
            return services;
        }
    }
}
