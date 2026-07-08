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
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Server;
using Opc.Ua.PubSub.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaServerBuilder"/> extensions provided by
    /// <c>Opc.Ua.PubSub.Server</c>: register an OPC UA PubSub node
    /// manager that mounts the standard <c>PublishSubscribe</c>
    /// Object onto the regular OPC UA server hosted via
    /// <c>.AddServer(...)</c>.
    /// </summary>
    /// <remarks>
    /// The PubSub server feature is <strong>not</strong> a
    /// standalone server. <c>.AddServer(...)</c> must be called
    /// first on the same <see cref="IOpcUaBuilder"/>; the hosted
    /// server will then pick up the
    /// <see cref="OpcUaServerNodeManagerRegistration"/> registered
    /// by these extensions and attach a
    /// <see cref="PubSubNodeManagerFactory"/> at start. The runtime
    /// <see cref="IPubSubApplication"/> registered by
    /// <c>OpcUaPubSubBuilderExtensions.AddPubSub(IOpcUaBuilder, ...)</c>
    /// must already be present in the service collection — the
    /// extensions throw <see cref="InvalidOperationException"/>
    /// otherwise.
    /// </remarks>
    public static class OpcUaServerBuilderPubSubExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by
        /// the <see cref="AddPubSub(IOpcUaServerBuilder, IConfiguration)"/>
        /// overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Server:PubSub";

        /// <summary>
        /// Registers the PubSub runtime and hosted OPC UA server PubSub node manager in the correct order.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configureServer">OPC UA hosted server options callback.</param>
        /// <param name="configurePubSub">Optional PubSub runtime callback.</param>
        /// <param name="configurePubSubServer">Optional PubSub server options callback.</param>
        /// <returns>An <see cref="IPubSubServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IPubSubServerBuilder AddPubSubServer(
            this IOpcUaBuilder builder,
            Action<OpcUaServerOptions> configureServer,
            Action<IPubSubBuilder>? configurePubSub = null,
            Action<PubSubServerOptions>? configurePubSubServer = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configureServer is null)
            {
                throw new ArgumentNullException(nameof(configureServer));
            }

            builder.AddPubSub(configurePubSub ?? (_ => { }));
            return builder.AddServer(configureServer).AddPubSub(configurePubSubServer);
        }

        /// <summary>
        /// Registers the PubSub runtime and hosted OPC UA server PubSub node manager from configuration.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configuration">Root configuration.</param>
        /// <param name="configurePubSub">Optional PubSub runtime callback.</param>
        /// <returns>An <see cref="IPubSubServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IPubSubServerBuilder AddPubSubServer(
            this IOpcUaBuilder builder,
            IConfiguration configuration,
            Action<IPubSubBuilder>? configurePubSub = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (configurePubSub is null)
            {
                builder.AddPubSub(configuration);
            }
            else
            {
                builder.AddPubSub(pubsub =>
                {
                    pubsub.Configure(options => configuration
                        .GetSection(OpcUaPubSubBuilderExtensions.DefaultConfigurationSection)
                        .Bind(options));
                    configurePubSub(pubsub);
                });
            }
            return builder.AddServer(configuration).AddPubSub(configuration);
        }

        /// <summary>
        /// Registers a PubSub node manager attached to the regular
        /// OPC UA server, returning an
        /// <see cref="IPubSubServerBuilder"/> for chaining.
        /// </summary>
        /// <param name="builder">OPC UA server builder.</param>
        /// <param name="configure">Optional options mutation
        /// callback.</param>
        /// <returns>An <see cref="IPubSubServerBuilder"/> for
        /// chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The PubSub runtime (<see cref="IPubSubApplication"/>) has
        /// not been registered on the same service collection; or
        /// the PubSub server feature has already been registered.
        /// </exception>
        public static IPubSubServerBuilder AddPubSub(
            this IOpcUaServerBuilder builder,
            Action<PubSubServerOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            EnsurePubSubRuntimeRegistered(builder.Services);
            EnsureFirstRegistration(builder.Services);

            if (configure is null)
            {
                builder.Services.AddOptions<PubSubServerOptions>();
            }
            else
            {
                builder.Services.AddOptions<PubSubServerOptions>().Configure(configure);
            }
            RegisterCommonServices(builder.Services);
            return new PubSubServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers a PubSub node manager with options bound from
        /// the supplied <paramref name="configuration"/> root using
        /// the default <c>OpcUa:Server:PubSub</c> section.
        /// </summary>
        /// <param name="builder">OPC UA server builder.</param>
        /// <param name="configuration">Configuration root.</param>
        /// <returns>An <see cref="IPubSubServerBuilder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="configuration"/>
        /// is <see langword="null"/>.
        /// </exception>
        public static IPubSubServerBuilder AddPubSub(
            this IOpcUaServerBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddPubSub(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers a PubSub node manager with options bound from
        /// the supplied <paramref name="section"/>.
        /// </summary>
        /// <param name="builder">OPC UA server builder.</param>
        /// <param name="section">Configuration section.</param>
        /// <returns>An <see cref="IPubSubServerBuilder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="section"/>
        /// is <see langword="null"/>.
        /// </exception>
        public static IPubSubServerBuilder AddPubSub(
            this IOpcUaServerBuilder builder,
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
            EnsurePubSubRuntimeRegistered(builder.Services);
            EnsureFirstRegistration(builder.Services);

            builder.Services.AddOptions<PubSubServerOptions>().Bind(section);
            RegisterCommonServices(builder.Services);
            return new PubSubServerBuilder(builder.Services);
        }

        private static void EnsurePubSubRuntimeRegistered(IServiceCollection services)
        {
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(IPubSubApplication))
                {
                    return;
                }
            }
            throw new InvalidOperationException(
                "AddPubSub(IOpcUaServerBuilder) requires the PubSub runtime to be registered first. "
                + "Call IOpcUaBuilder.AddPubSub(...) on the same IServiceCollection before AddServer().AddPubSub().");
        }

        private static void EnsureFirstRegistration(IServiceCollection services)
        {
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(PubSubServerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddPubSub(IOpcUaServerBuilder) has already been called on this service collection.");
                }
            }
            services.AddSingleton<PubSubServerRegistrationMarker>();
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            services.TryAddSingleton<IPubSubIdAllocator, InMemoryPubSubIdAllocator>();

            services.AddSingleton(sp =>
            {
                PubSubServerOptions options =
                    sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value
                    ?? throw new InvalidOperationException(
                        "PubSubServerOptions could not be resolved.");
                IPubSubApplication application = sp.GetRequiredService<IPubSubApplication>();
                IPubSubKeyServiceServer? keyService = sp.GetService<IPubSubKeyServiceServer>();
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                IEnumerable<PubSubActionMethodRegistration> registrations =
                    sp.GetServices<PubSubActionMethodRegistration>();
                IEnumerable<PushSecurityKeyProvider> pushProviders = sp.GetServices<PushSecurityKeyProvider>();
                return new PubSubNodeManagerFactory(
                    application,
                    keyService,
                    options,
                    telemetry,
                    registrations,
                    pushProviders,
                    sp.GetRequiredService<IPubSubIdAllocator>());
            });

            services.AddSingleton(sp =>
                new OpcUaServerNodeManagerRegistration(
                    sp.GetRequiredService<PubSubNodeManagerFactory>()));
        }

        private sealed class PubSubServerRegistrationMarker;

        private sealed class PubSubServerBuilder : IPubSubServerBuilder
        {
            public PubSubServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IPubSubServerBuilder Configure(Action<PubSubServerOptions> configure)
            {
                if (configure is null)
                {
                    throw new ArgumentNullException(nameof(configure));
                }
                Services.AddOptions<PubSubServerOptions>().Configure(configure);
                return this;
            }

            public IPubSubServerBuilder ExposeSecurityKeyService()
            {
                Services.AddOptions<PubSubServerOptions>().Configure(opt => opt.ExposeSecurityKeyService = true);
                return this;
            }

            public IPubSubServerBuilder WithDefaultSecurityGroup(string id)
            {
                if (string.IsNullOrEmpty(id))
                {
                    throw new ArgumentException("Security group id must be non-empty.", nameof(id));
                }
                Services.AddOptions<PubSubServerOptions>().Configure(opt => opt.DefaultSecurityGroupId = id);
                return this;
            }
        }
    }
}
