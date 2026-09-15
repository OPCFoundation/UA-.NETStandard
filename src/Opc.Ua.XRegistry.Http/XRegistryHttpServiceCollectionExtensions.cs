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
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    /// <summary>
    /// Registers an injectable HTTP endpoint with an isolated named HTTP client.
    /// </summary>
    public static class XRegistryHttpServiceCollectionExtensions
    {
        /// <summary>
        /// Registers one default IXRegistryEndpoint and returns its HTTP client builder.
        /// Add credential-provider handlers here, never mutation retry handlers.
        /// Automatic redirects, ambient credentials and cookies are disabled.
        /// </summary>
        /// <param name="services">
        /// The service collection that receives transient registrations for the concrete endpoint and
        /// <see cref="IXRegistryEndpoint"/>.
        /// </param>
        /// <param name="registryRoot">
        /// The absolute registry root without credentials, query or fragment. HTTPS is required unless
        /// <paramref name="options"/> explicitly permits loopback HTTP.
        /// </param>
        /// <param name="options">
        /// The transport limits and backend qualification, or <see langword="null"/> to use the defaults.
        /// </param>
        /// <param name="clientName">
        /// The nonblank name of the HTTP client that isolates the upstream credential profile.
        /// Defaults to <c>xregistry</c>.
        /// </param>
        /// <returns>The named client builder on which credential-provider handlers can be configured.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> or <paramref name="registryRoot"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="clientName"/> is blank, or the registry root is invalid.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A transport limit is outside its supported range.
        /// </exception>
        public static IHttpClientBuilder AddXRegistryHttpEndpoint(
            this IServiceCollection services,
            Uri registryRoot,
            XRegistryHttpOptions? options = null,
            string clientName = "xregistry")
        {
            services.ThrowIfNull(nameof(services));
            registryRoot.ThrowIfNull(nameof(registryRoot));
            if (string.IsNullOrWhiteSpace(clientName))
            {
                throw new ArgumentException("An isolated HTTP client name is required.", nameof(clientName));
            }
            options ??= new XRegistryHttpOptions();
            _ = new XRegistryHttpAddress(registryRoot, options);
            IHttpClientBuilder builder = services.AddHttpClient(clientName)
                .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    UseDefaultCredentials = false
                });
            services.AddTransient(provider => new XRegistryHttpEndpoint(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName), registryRoot, options));
            services.AddTransient<IXRegistryEndpoint>(static provider =>
                provider.GetRequiredService<XRegistryHttpEndpoint>());
            return builder;
        }
    }
}
