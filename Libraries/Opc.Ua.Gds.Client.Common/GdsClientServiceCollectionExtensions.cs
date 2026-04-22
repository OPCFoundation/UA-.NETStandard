/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Options;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Service collection extensions for registering the GDS client services
    /// with a Microsoft dependency injection container.
    /// </summary>
    public static class GdsClientServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the OPC UA GDS client services with the supplied service
        /// collection. The <see cref="ApplicationConfiguration"/> must be
        /// pre-registered. An optional <see cref="ISessionFactory"/> may be
        /// registered to override the default factory.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional callback used to bind
        /// <see cref="GdsClientOptions"/>.</param>
        public static IServiceCollection AddOpcUaGdsClient(
            this IServiceCollection services,
            Action<GdsClientOptions> configure = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddOptions();

            if (configure != null)
            {
                services.Configure(configure);
            }

            services.TryAddSingleton(sp =>
            {
                ApplicationConfiguration configuration = sp
                    .GetRequiredService<ApplicationConfiguration>();
                ISessionFactory sessionFactory = sp.GetService<ISessionFactory>();
                GdsClientOptions options = sp
                    .GetRequiredService<IOptions<GdsClientOptions>>().Value;
                return new GlobalDiscoveryServerClient(
                    configuration,
                    options,
                    adminUserIdentity: null,
                    sessionFactory: sessionFactory);
            });

            services.TryAddSingleton(sp =>
            {
                ApplicationConfiguration configuration = sp
                    .GetRequiredService<ApplicationConfiguration>();
                ISessionFactory sessionFactory = sp.GetService<ISessionFactory>();
                GdsClientOptions options = sp
                    .GetRequiredService<IOptions<GdsClientOptions>>().Value;
                return new ServerPushConfigurationClient(
                    configuration,
                    options,
                    sessionFactory: sessionFactory);
            });

            return services;
        }
    }
}
