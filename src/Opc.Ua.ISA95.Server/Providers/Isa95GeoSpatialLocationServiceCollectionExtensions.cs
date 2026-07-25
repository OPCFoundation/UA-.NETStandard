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
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Server
{
    /// <summary>
    /// Dependency injection registration helpers for the OPC-10030 geospatial
    /// location provider. Registration is optional; the
    /// <see cref="Isa95GeoSpatialLocationProvider"/> constructor is public and
    /// can be used directly as a fallback.
    /// </summary>
    public static class Isa95GeoSpatialLocationServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the in-memory <see cref="Isa95GeoSpatialLocationProvider"/>
        /// as the singleton <see cref="IIsa95GeoSpatialLocationProvider"/>.
        /// </summary>
        /// <param name="services">
        /// The service collection to register with.
        /// </param>
        /// <param name="configure">
        /// An optional callback used to seed the provider.
        /// </param>
        /// <returns>
        /// The same service collection to allow chaining.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddIsa95GeoSpatialLocationProvider(
            this IServiceCollection services,
            Action<Isa95GeoSpatialLocationProvider>? configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IIsa95GeoSpatialLocationProvider>(_ =>
            {
                var provider = new Isa95GeoSpatialLocationProvider();
                configure?.Invoke(provider);
                return provider;
            });

            return services;
        }
    }
}
