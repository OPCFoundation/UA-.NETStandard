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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Kafka;
using Opc.Ua.PubSub.Kafka.Internal;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IPubSubBuilder"/> extensions that register the
    /// Apache Kafka PubSub transport with the OPC UA PubSub DI surface.
    /// </summary>
    /// <remarks>
    /// Registers <em>two</em> <see cref="KafkaPubSubTransportFactory"/>
    /// instances — one for the JSON profile and one for the UADP
    /// profile — so that the runtime can match a
    /// <see cref="PubSubConnectionDataType"/> by its
    /// <c>TransportProfileUri</c>. The supported surface hangs off
    /// <see cref="IPubSubBuilder"/> (returned by
    /// <c>AddPubSub(pubsub =&gt; ...)</c>) because a transport only makes
    /// sense together with the PubSub feature. Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public static class KafkaTransportServiceCollectionExtensions
    {
        /// <summary>
        /// Default configuration section name read by
        /// <see cref="AddKafkaTransport(IPubSubBuilder, IConfiguration)"/>.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:PubSub:Kafka";

        /// <summary>
        /// Registers both Kafka factories (JSON + UADP) and binds
        /// <see cref="KafkaConnectionOptions"/> via the optional
        /// <paramref name="configure"/> callback.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configure">Optional options callback.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IPubSubBuilder AddKafkaTransport(
            this IPubSubBuilder builder,
            Action<KafkaConnectionOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                builder.Services.AddOptions<KafkaConnectionOptions>();
            }
            else
            {
                builder.Services.AddOptions<KafkaConnectionOptions>().Configure(configure);
            }
            RegisterShared(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers both Kafka factories (JSON + UADP) and binds
        /// <see cref="KafkaConnectionOptions"/> from the supplied root
        /// <paramref name="configuration"/> under
        /// <see cref="DefaultConfigurationSection"/>.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configuration">Root configuration.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IPubSubBuilder AddKafkaTransport(
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
            return builder.AddKafkaTransport(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers both Kafka factories (JSON + UADP) and binds
        /// <see cref="KafkaConnectionOptions"/> from the supplied
        /// section.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="section">Configuration section.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IPubSubBuilder AddKafkaTransport(
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
            builder.Services.AddOptions<KafkaConnectionOptions>().Bind(section);
            RegisterShared(builder.Services);
            return builder;
        }

        /// <summary>
        /// One-shot: registers a PubSub publisher and subscriber together with
        /// the Apache Kafka transport (JSON + UADP profiles) on a fresh OPC UA
        /// DI root.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional Kafka connection options callback.</param>
        /// <returns>The same <paramref name="services"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddKafkaPubSub(
            this IServiceCollection services,
            Action<KafkaConnectionOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOpcUa().AddKafkaPubSub(configure);
            return services;
        }

        /// <summary>
        /// One-shot: registers a PubSub publisher and subscriber together with
        /// the Apache Kafka transport (JSON + UADP profiles).
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Optional Kafka connection options callback.</param>
        /// <returns>The same <paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddKafkaPubSub(
            this IOpcUaBuilder builder,
            Action<KafkaConnectionOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddPubSub(pubsub =>
                pubsub.AddPublisher()
                    .AddSubscriber()
                    .AddKafkaTransport(configure));
            return builder;
        }

        private static void RegisterShared(IServiceCollection services)
        {
#if NET10_0_OR_GREATER
            services.TryAddSingleton<IKafkaClientFactory, DekafKafkaClientFactory>();
#else
            services.TryAddSingleton<IKafkaClientFactory, ConfluentKafkaClientFactory>();
#endif
            services.AddPubSubTransportFactory(sp =>
                new KafkaPubSubTransportFactory(
                    KafkaProfiles.PubSubKafkaJsonTransport,
                    sp.GetRequiredService<IKafkaClientFactory>(),
                    sp.GetRequiredService<IOptions<KafkaConnectionOptions>>(),
                    sp.GetService<ISecretRegistry>(),
                    sp.GetService<IPubSubDiagnostics>()));
            services.AddPubSubTransportFactory(sp =>
                new KafkaPubSubTransportFactory(
                    KafkaProfiles.PubSubKafkaUadpTransport,
                    sp.GetRequiredService<IKafkaClientFactory>(),
                    sp.GetRequiredService<IOptions<KafkaConnectionOptions>>(),
                    sp.GetService<ISecretRegistry>(),
                    sp.GetService<IPubSubDiagnostics>()));
        }
    }
}
