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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency-injection extensions provided by <c>Opc.Ua.OpenUsd.Client</c>: register
    /// the singleton <see cref="OpenUsdConnectorFactory"/> so DI-hosted client hosts can
    /// resolve it and produce an <see cref="OpenUsdConnector"/> per connected session.
    /// The public <see cref="OpenUsdConnector"/> constructors remain the non-DI fallback.
    /// </summary>
    public static class OpcUaOpenUsdConnectorBuilderExtensions
    {
        /// <summary>
        /// Registers the singleton <see cref="OpenUsdConnectorFactory"/> and the
        /// <see cref="OpenUsdConnectorOptions"/> it uses, optionally configured via
        /// <paramref name="configure"/>. Idempotent; repeated calls do not add a second
        /// descriptor.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddOpenUsdConnector(
            this IServiceCollection services, Action<OpenUsdConnectorOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOpcUa();

            var options = new OpenUsdConnectorOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
            services.TryAddSingleton<OpenUsdConnectorFactory>();
            return services;
        }

        /// <summary>
        /// Registers the OpenUSD connector factory on the OPC UA builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddOpenUsdConnector(
            this IOpcUaBuilder builder, Action<OpenUsdConnectorOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpenUsdConnector(configure);
            return builder;
        }

        /// <summary>
        /// Registers the OpenUSD connector factory and keeps the client builder chain
        /// flowing (e.g. <c>AddClient(...).AddOpenUsdConnector()</c>).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddOpenUsdConnector(
            this IOpcUaClientBuilder builder, Action<OpenUsdConnectorOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpenUsdConnector(configure);
            return builder;
        }
    }
}
