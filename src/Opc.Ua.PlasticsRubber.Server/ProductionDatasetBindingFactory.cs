// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Resolves reusable infrastructure while keeping the authority and authorization instance-specific.
    /// </summary>
    public sealed class ProductionDatasetBindingFactory
    {
        /// <summary>
        /// Creates a DI-compatible factory with replaceable file transport and time.
        /// </summary>
        public ProductionDatasetBindingFactory(IFileTypeBinder fileBinder, TimeProvider? timeProvider = null)
        {
            m_fileBinder = fileBinder ?? throw new ArgumentNullException(nameof(fileBinder));
            m_clock = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Binds one generated management instance to its own authority.
        /// </summary>
        public ValueTask<ProductionDatasetBinding> BindAsync(
            AsyncCustomNodeManager owner,
            ProductionDatasetManagementState node,
            IProductionDatasetProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            ProductionDatasetOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return ProductionDatasetBinding.CreateAsync(
                owner, node, provider, accessPolicy, options, m_fileBinder, m_clock, cancellationToken);
        }

        /// <summary>
        /// The injectable FileType infrastructure.
        /// </summary>
        private readonly IFileTypeBinder m_fileBinder;

        /// <summary>
        /// The injectable transfer deadline clock.
        /// </summary>
        private readonly TimeProvider m_clock;
    }

    /// <summary>
    /// Registers chapter 20 infrastructure without installing an implicit allow-all policy or device authority.
    /// </summary>
    public static class ProductionDatasetServiceCollectionExtensions
    {
        /// <summary>
        /// Makes ProductionDatasetBindingFactory available to existing node-manager and hosting compositions.
        /// </summary>
        public static IServiceCollection AddProductionDatasets(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton<IFileTypeBinder, FileTypeBinder>();
            services.TryAddSingleton<ProductionDatasetBindingFactory>();
            return services;
        }
    }
}
