/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.WebApi;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that wire the client-side
    /// Web API transport channel into the host's
    /// <see cref="ITransportBindingRegistry"/>. Calling
    /// <c>AddWebApiTransportChannel()</c> registers the
    /// <see cref="WebApiTransportChannelFactory"/> so any
    /// <c>ConfiguredEndpoint</c> with
    /// <see cref="Profiles.HttpsOpenApiTransport"/> profile is dispatched
    /// through Web API by the standard
    /// <c>ClientChannelManager</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pair this with the server-side
    /// <c>services.AddWebApiTransport()</c> for full bidirectional
    /// Web API support; client-only deployments only need this
    /// extension. The fluent
    /// <c>ManagedSessionBuilder.UseWebApiEndpoint(...)</c> registers a
    /// per-session factory and does not need this DI registration.
    /// </para>
    /// </remarks>
    public static class OpcUaWebApiClientBuilderExtensions
    {
        /// <summary>
        /// Registers the Web API transport channel factory with default
        /// <see cref="WebApiClientOptions"/>.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddWebApiTransportChannel(this IOpcUaBuilder builder)
        {
            return AddWebApiTransportChannel(builder, configure: null);
        }

        /// <summary>
        /// Registers the Web API transport channel factory with default
        /// <see cref="WebApiClientOptions"/> and keeps the client builder
        /// chain flowing.
        /// </summary>
        public static IOpcUaClientBuilder AddWebApiTransportChannel(
            this IOpcUaClientBuilder builder)
        {
            return AddWebApiTransportChannel(builder, configure: null);
        }

        /// <summary>
        /// Registers the Web API transport channel factory and lets the
        /// caller customize the default
        /// <see cref="WebApiClientOptions"/> applied to every channel
        /// constructed from this DI container.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">
        /// Optional callback that customizes the default
        /// <see cref="WebApiClientOptions"/>. Pass <c>null</c> to accept
        /// the built-in defaults (Compact encoding, no auth, system
        /// HttpClient).
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddWebApiTransportChannel(
            this IOpcUaBuilder builder,
            Action<WebApiClientOptions>? configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            IServiceCollection services = builder.Services;

            // Per-container default WebApiClientOptions. Channels resolve
            // this singleton when constructed by the factory below.
            services.TryAddSingleton(_ =>
            {
                var options = new WebApiClientOptions();
                configure?.Invoke(options);
                return options;
            });

            services.AddTransportBindingRegistry();
            services.AddSingleton<ITransportBindingConfigurator>(sp =>
            {
                WebApiClientOptions options = sp.GetRequiredService<WebApiClientOptions>();
                IOpcUaHttpClientFactory? httpClientFactory = sp.GetService<IOpcUaHttpClientFactory>();
                TimeProvider? timeProvider = sp.GetService<TimeProvider>();
                var httpsFactory = new WebApiTransportChannelFactory(
                    options, httpClientFactory, timeProvider);
                var wssFactory = new WebApiWssTransportChannelFactory(
                    options, timeProvider);

                return new TransportBindingConfigurator(registry =>
                {
                    registry.RegisterChannelFactory(httpsFactory);
                    registry.RegisterChannelFactory(wssFactory);
                });
            });

            return builder;
        }

        /// <summary>
        /// Registers the Web API transport channel factory and keeps the
        /// client builder chain flowing.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddWebApiTransportChannel(
            this IOpcUaClientBuilder builder,
            Action<WebApiClientOptions>? configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddWebApiTransportChannel(configure);
            return builder;
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
