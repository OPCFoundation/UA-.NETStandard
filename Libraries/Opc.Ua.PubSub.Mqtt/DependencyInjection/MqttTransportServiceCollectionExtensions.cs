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
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.PubSub.Transports;

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
        public static IPubSubBuilder AddMqttTransport(
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
            return builder;
        }

        /// <summary>
        /// Registers both MQTT factories (JSON + UADP) and binds
        /// <see cref="MqttConnectionOptions"/> from the supplied root
        /// <paramref name="configuration"/> under
        /// <see cref="DefaultConfigurationSection"/>.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configuration">Root configuration.</param>
        public static IPubSubBuilder AddMqttTransport(
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
        public static IPubSubBuilder AddMqttTransport(
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
            return builder;
        }

        /// <summary>
        /// Obsolete forwarder kept for source compatibility. Add the MQTT
        /// transport through the <see cref="IPubSubBuilder"/> returned by
        /// <c>AddPubSub(pubsub =&gt; pubsub.AddMqttTransport())</c> instead.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configure">Optional options callback.</param>
        [Obsolete("Add the MQTT transport on the IPubSubBuilder: " +
            "AddPubSub(pubsub => pubsub.AddMqttTransport()).")]
        public static IOpcUaBuilder AddMqttTransport(
            this IOpcUaBuilder builder,
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
            return builder;
        }

        private static void RegisterShared(IServiceCollection services)
        {
            services.TryAddSingleton<IMqttClientFactory, MqttClientAdapterFactory>();
            services.Add(
                ServiceDescriptor.Singleton<IPubSubTransportFactory>(sp =>
                    new MqttPubSubTransportFactory(
                        Profiles.PubSubMqttJsonTransport,
                        sp.GetRequiredService<IMqttClientFactory>(),
                        sp.GetRequiredService<IOptions<MqttConnectionOptions>>(),
                        sp.GetService<ISecretRegistry>(),
                        sp.GetService<IPubSubDiagnostics>())));
            services.Add(
                ServiceDescriptor.Singleton<IPubSubTransportFactory>(sp =>
                    new MqttPubSubTransportFactory(
                        Profiles.PubSubMqttUadpTransport,
                        sp.GetRequiredService<IMqttClientFactory>(),
                        sp.GetRequiredService<IOptions<MqttConnectionOptions>>(),
                        sp.GetService<ISecretRegistry>(),
                        sp.GetService<IPubSubDiagnostics>())));
        }
    }
}
