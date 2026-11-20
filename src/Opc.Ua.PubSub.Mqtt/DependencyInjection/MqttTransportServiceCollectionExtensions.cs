// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.Mqtt.Internal;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IPubSubBuilder"/> extensions that register the
    /// MQTT PubSub transport with the OPC UA PubSub DI surface.
    /// </summary>
    /// <remarks>
    /// Registers <em>two</em> <see cref="MqttPubSubTransportFactory"/>
    /// instances — one for the JSON profile and one for the UADP
    /// profile — so that the runtime can match an
    /// <see cref="PubSubConnectionDataType"/> by its
    /// <c>TransportProfileUri</c>. The supported surface hangs off
    /// <see cref="IPubSubBuilder"/> (returned by
    /// <c>AddPubSub(pubsub =&gt; ...)</c>) because a transport only makes
    /// sense together with the PubSub feature. Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 MQTT broker transport</see>.
    /// </remarks>
    public static class MqttTransportServiceCollectionExtensions
    {
        /// <summary>
        /// Default configuration section name read by
        /// <see cref="AddMqttTransport(IPubSubBuilder, IConfiguration)"/>.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:PubSub:Mqtt";

        /// <summary>
        /// Registers both MQTT factories (JSON + UADP) and binds
        /// <see cref="MqttConnectionOptions"/> via the optional
        /// <paramref name="configure"/> callback.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configure">Optional options callback.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IMqttTransportBuilder AddMqttTransport(
            this IPubSubBuilder builder,
            Action<MqttConnectionOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                builder.Services.AddOptions<MqttConnectionOptions>();
            }
            else
            {
                builder.Services.AddOptions<MqttConnectionOptions>().Configure(configure);
            }
            RegisterShared(builder.Services);
            return CreateMqttTransportBuilder(builder);
        }

        /// <summary>
        /// Registers both MQTT factories (JSON + UADP) and binds
        /// <see cref="MqttConnectionOptions"/> from the supplied root
        /// <paramref name="configuration"/> under
        /// <see cref="DefaultConfigurationSection"/>.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configuration">Root configuration.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IMqttTransportBuilder AddMqttTransport(
            this IPubSubBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddMqttTransport(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers both MQTT factories (JSON + UADP) and binds
        /// <see cref="MqttConnectionOptions"/> from the supplied
        /// section.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="section">Configuration section.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IMqttTransportBuilder AddMqttTransport(
            this IPubSubBuilder builder,
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
            builder.Services.AddOptions<MqttConnectionOptions>().Bind(section);
            RegisterShared(builder.Services);
            return CreateMqttTransportBuilder(builder);
        }

        /// <summary>
        /// Registers an additional MQTT connection options callback for broker,
        /// TLS and topic settings.
        /// </summary>
        /// <param name="builder">MQTT transport builder.</param>
        /// <param name="configure">Options callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IMqttTransportBuilder WithConnectionOptions(
            this IMqttTransportBuilder builder,
            Action<MqttConnectionOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.AddOptions<MqttConnectionOptions>().Configure(configure);
            return builder;
        }

        /// <summary>
        /// Registers additional MQTT connection options from the default MQTT configuration section.
        /// </summary>
        /// <param name="builder">MQTT transport builder.</param>
        /// <param name="configuration">Root configuration.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IMqttTransportBuilder WithConnectionOptions(
            this IMqttTransportBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return builder.WithConnectionOptions(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers additional MQTT connection options from the supplied configuration section.
        /// </summary>
        /// <param name="builder">MQTT transport builder.</param>
        /// <param name="section">Configuration section.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IMqttTransportBuilder WithConnectionOptions(
            this IMqttTransportBuilder builder,
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

            builder.Services.AddOptions<MqttConnectionOptions>().Bind(section);
            return builder;
        }

        /// <summary>
        /// Registers a PubSub publisher and subscriber with the MQTT transport.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configure">Optional MQTT transport builder callback.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddMqttPubSub(
            this IServiceCollection services,
            Action<IMqttTransportBuilder>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOpcUa().AddMqttPubSub(configure);
            return services;
        }

        /// <summary>
        /// Registers a PubSub publisher and subscriber with the MQTT transport.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configure">Optional MQTT transport builder callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddMqttPubSub(
            this IOpcUaBuilder builder,
            Action<IMqttTransportBuilder>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddPubSub(pubsub =>
            {
                IMqttTransportBuilder mqttBuilder = pubsub
                    .AddPublisher()
                    .AddSubscriber()
                    .AddMqttTransport();
                configure?.Invoke(mqttBuilder);
            });
            return builder;
        }

        private static void RegisterShared(IServiceCollection services)
        {
            services.TryAddSingleton<IMqttTrustedIssuerResolver>(sp =>
                new TrustedIssuerStoreResolver(sp.GetService<ApplicationConfiguration>()));
            services.TryAddSingleton<IMqttClientFactory, MqttClientAdapterFactory>();
            services.AddPubSubTransportFactory(sp =>
                    new MqttPubSubTransportFactory(
                        Profiles.PubSubMqttJsonTransport,
                        sp.GetRequiredService<IMqttClientFactory>(),
                        sp.GetRequiredService<IOptions<MqttConnectionOptions>>(),
                        sp.GetService<ISecretRegistry>(),
                        sp.GetService<IPubSubDiagnostics>()));
            services.AddPubSubTransportFactory(sp =>
                    new MqttPubSubTransportFactory(
                        Profiles.PubSubMqttUadpTransport,
                        sp.GetRequiredService<IMqttClientFactory>(),
                        sp.GetRequiredService<IOptions<MqttConnectionOptions>>(),
                        sp.GetService<ISecretRegistry>(),
                        sp.GetService<IPubSubDiagnostics>()));
        }

        private static IMqttTransportBuilder CreateMqttTransportBuilder(IPubSubBuilder builder)
        {
            return builder as IMqttTransportBuilder ?? new MqttTransportBuilder(builder);
        }
    }
}
