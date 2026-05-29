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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Lds.Server;
using Opc.Ua.Lds.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Lds.Server</c>: register an OPC UA Local Discovery Server
    /// (LDS / LDS-ME) hosted as an <see cref="IHostedService"/> so the
    /// .NET Generic Host owns its lifetime, logging pipeline and Ctrl+C /
    /// SIGTERM handling.
    /// </summary>
    public static class OpcUaLdsServerBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddLdsServer(IOpcUaBuilder, IConfiguration)"/> overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Lds";

        /// <summary>
        /// Registers an OPC UA Local Discovery Server hosted as an
        /// <see cref="IHostedService"/> and returns an
        /// <see cref="ILdsServerBuilder"/> for chained service registration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The hosted service builds an <see cref="ApplicationConfiguration"/>
        /// from the supplied <see cref="LdsServerOptions"/>, ensures the
        /// application instance certificate is present, then starts an
        /// <see cref="LdsServer"/> via the application instance. Stop is
        /// signalled by the host.
        /// </para>
        /// <para>
        /// Calling this method twice on the same
        /// <see cref="IServiceCollection"/> throws
        /// <see cref="InvalidOperationException"/>: at most one LDS may be
        /// registered per service collection. An LDS can coexist with a
        /// regular OPC UA server / GDS server / WoT Connectivity server
        /// registered via their own <c>.AddServer()</c> / <c>.AddGdsServer()</c>
        /// / <c>.AddWotConServer()</c> methods.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Required callback used to populate
        /// <see cref="LdsServerOptions"/>.</param>
        /// <returns>An <see cref="ILdsServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA LDS server
        /// is already registered.</exception>
        public static ILdsServerBuilder AddLdsServer(
            this IOpcUaBuilder builder,
            Action<LdsServerOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            EnsureFirstRegistration(builder.Services);

            builder.Services.AddOptions<LdsServerOptions>().Configure(configure);
            RegisterCommonServices(builder.Services);

            return new LdsServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers an OPC UA Local Discovery Server hosted as an
        /// <see cref="IHostedService"/> with options bound from the supplied
        /// <paramref name="configuration"/> section
        /// <see cref="DefaultConfigurationSection"/> (<c>OpcUa:Lds</c>).
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:Lds</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA LDS server
        /// is already registered.</exception>
        public static ILdsServerBuilder AddLdsServer(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddLdsServer(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers an OPC UA Local Discovery Server hosted as an
        /// <see cref="IHostedService"/> with options bound from the supplied
        /// <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA LDS server
        /// is already registered.</exception>
        public static ILdsServerBuilder AddLdsServer(
            this IOpcUaBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            EnsureFirstRegistration(builder.Services);

            builder.Services.AddOptions<LdsServerOptions>().Bind(section);
            RegisterCommonServices(builder.Services);

            return new LdsServerBuilder(builder.Services);
        }

        private static void EnsureFirstRegistration(IServiceCollection services)
        {
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(LdsServerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddLdsServer has already been called. At most one LDS may be registered per service collection.");
                }
            }
            services.AddSingleton<LdsServerRegistrationMarker>();
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            services.AddHostedService<LdsServerHostedService>();
            IOpcUaBuilder opcUa = OpcUaServiceCollectionExtensions.AddOpcUa(services);
            opcUa.AddApplicationInstance();
        }

        private sealed class LdsServerBuilder : ILdsServerBuilder
        {
            public LdsServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        private sealed class LdsServerRegistrationMarker
        {
        }
    }
}
