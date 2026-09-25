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
