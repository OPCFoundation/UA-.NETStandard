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
