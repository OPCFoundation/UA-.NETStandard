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
