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
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Registers an opt-in transactional provider without changing existing native registries.
    /// </summary>
    public static class XRegistryTransactionalServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the provider and its shared operation interface. Register a durable
        /// IXRegistryTransactionStore first to replace the process-local default.
        /// </summary>
        public static IServiceCollection AddXRegistryTransactions(
            this IServiceCollection services, XRegistryTransactionalOptions options)
        {
            services.ThrowIfNull(nameof(services));
            options.ThrowIfNull(nameof(options));
            services.AddSingleton(options);
            services.TryAddSingleton<IXRegistryTransactionStore, InMemoryXRegistryTransactionStore>();
            services.TryAddSingleton(provider => new XRegistryTransactionalEndpoint(
                provider.GetRequiredService<XRegistryTransactionalOptions>(),
                provider.GetRequiredService<IXRegistryTransactionStore>(),
                provider.GetService<TimeProvider>()));
            services.TryAddSingleton<IXRegistryEndpoint>(provider =>
                provider.GetRequiredService<XRegistryTransactionalEndpoint>());
            services.TryAddSingleton<IXRegistryOperationJournalEndpoint>(provider =>
                provider.GetRequiredService<XRegistryTransactionalEndpoint>());
            services.TryAddSingleton<IXRegistryPreparedEndpoint>(provider =>
                provider.GetRequiredService<XRegistryTransactionalEndpoint>());
            return services;
        }
    }
}
