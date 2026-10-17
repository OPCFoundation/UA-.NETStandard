/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
