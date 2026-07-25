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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.ISA95.Server.Providers;
#pragma warning restore IDE0005

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
            services.TryAddSingleton<IIsa95JobOrderCatalogChangeSource>(Resolve);

            return services;
        }

        private static InMemoryIsa95JobControlProvider Resolve(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetRequiredService<InMemoryIsa95JobControlProvider>();
        }
    }
}
