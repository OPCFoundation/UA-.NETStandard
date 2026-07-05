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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Server.Hosting;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.WotCon.Server</c>: register an OPC UA WoT Connectivity
    /// (OPC 10100-1) node manager that attaches to the regular OPC UA
    /// server hosted via <c>.AddServer(...)</c>.
    /// </summary>
    /// <remarks>
    /// The WoT Connectivity feature is <strong>not</strong> a standalone
    /// server. <c>.AddServer(...)</c> must be called first on the same
    /// <see cref="IOpcUaBuilder"/>; the hosted server will then pick up
    /// the <see cref="OpcUaServerNodeManagerRegistration"/> registered
    /// by <see cref="AddWotConServer(IOpcUaBuilder, Action{WotConnectivityServerOptions})"/>
    /// and attach a <see cref="WotConnectivityNodeManagerFactory"/> at
    /// start.
    /// </remarks>
    public static class OpcUaWotConServerBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddWotConServer(IOpcUaBuilder, IConfiguration)"/>
        /// overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:WotCon:Server";

        /// <summary>
        /// Registers a regular OPC UA server and the WoT Connectivity node manager in one call.
        /// </summary>
        public static IWotConServerBuilder AddWotConnectivityServer(
            this IOpcUaBuilder builder,
            Action<OpcUaServerOptions> configureServer,
            Action<WotConnectivityServerOptions> configureWotCon)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configureServer is null)
            {
                throw new ArgumentNullException(nameof(configureServer));
            }
            if (configureWotCon is null)
            {
                throw new ArgumentNullException(nameof(configureWotCon));
            }

            builder.AddServer(configureServer);
            return builder.AddWotConServer(configureWotCon);
        }

        /// <summary>
        /// Registers a WoT Connectivity node manager (OPC 10100-1)
        /// attached to the regular OPC UA server, returning an
        /// <see cref="IWotConServerBuilder"/> for chaining
        /// asset-provider / discovery-provider registrations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Calling this method twice on the same
        /// <see cref="IServiceCollection"/> throws
        /// <see cref="InvalidOperationException"/>: at most one WoT
        /// Connectivity feature may be registered per service collection.
        /// </para>
        /// <para>
        /// At server start the registered
        /// <see cref="IWotAssetProviderFactory"/> services are merged
        /// into <see cref="WotConnectivityServerOptions.Bindings"/> and
        /// the registered <see cref="IWotAssetDiscoveryProvider"/>
        /// service (if any) is assigned to
        /// <see cref="WotConnectivityServerOptions.Discovery"/> when
        /// the option has not already been set.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Required callback used to populate
        /// <see cref="WotConnectivityServerOptions"/>.</param>
        /// <returns>An <see cref="IWotConServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A WoT Connectivity
        /// feature is already registered.</exception>
        public static IWotConServerBuilder AddWotConServer(
            this IOpcUaBuilder builder,
            Action<WotConnectivityServerOptions> configure)
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

            builder.Services.AddOptions<WotConnectivityServerOptions>().Configure(configure);
            RegisterCommonServices(builder.Services);

            return new WotConServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers a WoT Connectivity node manager with options bound
        /// from the supplied <paramref name="configuration"/> section
        /// <see cref="DefaultConfigurationSection"/>
        /// (<c>OpcUa:WotCon:Server</c>).
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:WotCon:Server</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A WoT Connectivity
        /// feature is already registered.</exception>
        public static IWotConServerBuilder AddWotConServer(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddWotConServer(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers a WoT Connectivity node manager with options bound
        /// from the supplied <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A WoT Connectivity
        /// feature is already registered.</exception>
        public static IWotConServerBuilder AddWotConServer(
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

            builder.Services.AddOptions<WotConnectivityServerOptions>().Bind(section);
            RegisterCommonServices(builder.Services);

            return new WotConServerBuilder(builder.Services);
        }

        private static void EnsureFirstRegistration(IServiceCollection services)
        {
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(WotConServerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddWotConServer has already been called. At most one WoT Connectivity feature may be registered per service collection.");
                }
            }
            services.AddSingleton<WotConServerRegistrationMarker>();
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.AddSingleton(sp =>
            {
                WotConnectivityServerOptions configured =
                    sp.GetRequiredService<IOptions<WotConnectivityServerOptions>>().Value
                    ?? throw new InvalidOperationException(
                        "WotConnectivityServerOptions could not be resolved.");

                var merged = new WotConnectivityServerOptions
                {
                    AssetNamespaceUri = configured.AssetNamespaceUri,
                    ThingDescriptionStorageFolder = configured.ThingDescriptionStorageFolder,
                    MaxThingDescriptionSize = configured.MaxThingDescriptionSize,
                    MaxOpenFileHandlesPerAsset = configured.MaxOpenFileHandlesPerAsset,
                    Discovery = configured.Discovery,
                    License = configured.License,
                    ManagementAccess = configured.ManagementAccess
                };
                foreach (IWotAssetProviderFactory binding in configured.Bindings)
                {
                    merged.Bindings.Add(binding);
                }
                foreach (KeyValuePair<string, WotConfigurationParameter> kvp in configured.Configuration)
                {
                    merged.Configuration[kvp.Key] = kvp.Value;
                }

                foreach (IWotAssetProviderFactory binding in sp.GetServices<IWotAssetProviderFactory>())
                {
                    merged.Bindings.Add(binding);
                }
                if (merged.Discovery is null)
                {
                    IWotAssetDiscoveryProvider? discovery =
                        sp.GetService<IWotAssetDiscoveryProvider>();
                    if (discovery is not null)
                    {
                        merged.Discovery = discovery;
                    }
                }

                return new WotConnectivityNodeManagerFactory(merged);
            });

            services.AddSingleton(sp =>
                new OpcUaServerNodeManagerRegistration(
                    sp.GetRequiredService<WotConnectivityNodeManagerFactory>()));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WotConServerStartupValidator>());

            services.AddOpcUa();
        }

        private static bool HasOpcUaServerHostedService(IServiceCollection services)
        {
            return services.Any(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType?.Name == "OpcUaServerHostedService");
        }

        private sealed class WotConServerBuilder : IWotConServerBuilder
        {
            public WotConServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IWotConServerBuilder AddAssetProvider<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>()
                where TFactory : class, IWotAssetProviderFactory
            {
                Services.AddSingleton<TFactory>();
                Services.AddSingleton<IWotAssetProviderFactory>(
                    sp => sp.GetRequiredService<TFactory>());
                return this;
            }

            public IWotConServerBuilder AddAssetProvider(
                Func<IServiceProvider, IWotAssetProviderFactory> factory)
            {
                if (factory is null)
                {
                    throw new ArgumentNullException(nameof(factory));
                }
                Services.AddSingleton(factory);
                return this;
            }

            public IWotConServerBuilder AddDiscoveryProvider<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IWotAssetDiscoveryProvider
            {
                Services.TryAddSingleton<IWotAssetDiscoveryProvider, T>();
                return this;
            }
        }

        /// <summary>
        /// Enables the built-in OPC TCP transport and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddOpcTcpTransport(this IWotConServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddOpcTcpTransport();
            return builder;
        }

        /// <summary>
        /// Enables HTTPS transport and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddHttpsTransport(this IWotConServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddHttpsTransport();
            return builder;
        }

        /// <summary>
        /// Enables HTTPS transport with one-shot options and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddHttpsTransport(
            this IWotConServerBuilder builder,
            Action<OpcUaHttpsTransportOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddHttpsTransport(configure);
            return builder;
        }

        /// <summary>
        /// Enables WSS transport and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddWssTransport(this IWotConServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddWssTransport();
            return builder;
        }

        /// <summary>
        /// Enables WSS transport with one-shot options and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddWssTransport(
            this IWotConServerBuilder builder,
            Action<OpcUaWssTransportOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddWssTransport(configure);
            return builder;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Enables the Kestrel OPC TCP listener and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddKestrelOpcTcpTransport(this IWotConServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddKestrelOpcTcpTransport();
            return builder;
        }

        /// <summary>
        /// Enables the OPC UA REST Web API transport and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddWebApiTransport(this IWotConServerBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddWebApiTransport();
            return builder;
        }

        /// <summary>
        /// Enables the OPC UA REST Web API transport and returns the same WoT Connectivity builder.
        /// </summary>
        public static IWotConServerBuilder AddWebApiTransport(
            this IWotConServerBuilder builder,
            Action<Opc.Ua.Bindings.WebApi.WebApiTransportOptions>? configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa().AddWebApiTransport(configure);
            return builder;
        }
#endif

        /// <summary>
        /// Configures server-side reverse connect on the paired OPC UA server.
        /// </summary>
        public static IWotConServerBuilder AddReverseConnect(
            this IWotConServerBuilder builder,
            Action<ServerReverseConnectOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.Configure<OpcUaServerOptions>(options =>
            {
                options.ReverseConnect ??= new ServerReverseConnectOptions();
                configure(options.ReverseConnect);
            });
            return builder;
        }

        private sealed class WotConServerStartupValidator : IHostedService
        {
            private readonly IServiceProvider m_services;

            public WotConServerStartupValidator(IServiceProvider services)
            {
                m_services = services ?? throw new ArgumentNullException(nameof(services));
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                bool hasServer = m_services
                    .GetServices<IConfigureOptions<OpcUaServerOptions>>()
                    .Any();
                if (!hasServer)
                {
                    throw new InvalidOperationException(
                        "AddWotConServer registers an OPC UA server node manager and requires AddServer on the same " +
                        "IOpcUaBuilder. Call AddServer before AddWotConServer.");
                }

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class WotConServerRegistrationMarker;
    }
}
