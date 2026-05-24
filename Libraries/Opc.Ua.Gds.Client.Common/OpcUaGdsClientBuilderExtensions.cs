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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Gds.Client.Common</c>: register the GDS / push-config
    /// clients for dependency-injected applications.
    /// </summary>
    public static class OpcUaGdsClientBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddGdsClient(IOpcUaBuilder, IConfiguration)"/>
        /// overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Gds:Client";

        /// <summary>
        /// Registers the OPC UA GDS client services. The
        /// <see cref="ApplicationConfiguration"/> must be pre-registered.
        /// An optional <see cref="ISessionFactory"/> may be registered to
        /// override the default factory.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Optional callback used to populate
        /// <see cref="GdsClientOptions"/>.</param>
        /// <returns>An <see cref="IGdsClientBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IGdsClientBuilder AddGdsClient(
            this IOpcUaBuilder builder,
            Action<GdsClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOptions();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }

            RegisterCoreServices(builder.Services);

            return new GdsClientBuilder(builder.Services);
        }

        /// <summary>
        /// Registers the OPC UA GDS client services with options bound
        /// from the supplied <paramref name="configuration"/> section
        /// <see cref="DefaultConfigurationSection"/>.
        /// </summary>
        /// <remarks>
        /// Uses reflection-based configuration binding. AOT consumers
        /// should prefer
        /// <see cref="AddGdsClient(IOpcUaBuilder, Action{GdsClientOptions})"/>.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing the
        /// <c>OpcUa:Gds:Client</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        [RequiresUnreferencedCode(
            "Binds GdsClientOptions using reflection-based configuration binding. " +
            "Use the Action<GdsClientOptions> overload for trim/AOT consumers.")]
        [RequiresDynamicCode(
            "Binds GdsClientOptions using reflection-based configuration binding. " +
            "Use the Action<GdsClientOptions> overload for AOT consumers.")]
        public static IGdsClientBuilder AddGdsClient(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddGdsClient(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the OPC UA GDS client services with options bound
        /// from the supplied <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// Uses reflection-based configuration binding. AOT consumers
        /// should prefer
        /// <see cref="AddGdsClient(IOpcUaBuilder, Action{GdsClientOptions})"/>.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        [RequiresUnreferencedCode(
            "Binds GdsClientOptions using reflection-based configuration binding. " +
            "Use the Action<GdsClientOptions> overload for trim/AOT consumers.")]
        [RequiresDynamicCode(
            "Binds GdsClientOptions using reflection-based configuration binding. " +
            "Use the Action<GdsClientOptions> overload for AOT consumers.")]
        public static IGdsClientBuilder AddGdsClient(
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

            builder.Services.AddOptions();
            builder.Services.Configure<GdsClientOptions>(section.Bind);

            RegisterCoreServices(builder.Services);

            return new GdsClientBuilder(builder.Services);
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton(sp =>
            {
                ApplicationConfiguration configuration = sp
                    .GetRequiredService<ApplicationConfiguration>();
                ISessionFactory? sessionFactory = sp.GetService<ISessionFactory>();
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
                ISessionFactory? sessionFactory = sp.GetService<ISessionFactory>();
                GdsClientOptions options = sp
                    .GetRequiredService<IOptions<GdsClientOptions>>().Value;
                return new ServerPushConfigurationClient(
                    configuration,
                    options,
                    sessionFactory: sessionFactory);
            });

            OpcUaServiceCollectionExtensions.AddOpcUa(services);
        }

        private sealed class GdsClientBuilder : IGdsClientBuilder
        {
            public GdsClientBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
