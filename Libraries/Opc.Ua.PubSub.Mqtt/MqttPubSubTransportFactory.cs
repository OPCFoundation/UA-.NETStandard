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
 *
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
using Microsoft.Extensions.Options;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// <see cref="IPubSubTransportFactory"/> for the MQTT broker
    /// transport profiles
    /// (<see cref="Profiles.PubSubMqttJsonTransport"/> and
    /// <see cref="Profiles.PubSubMqttUadpTransport"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see> from the factory
    /// side. Two instances are registered with DI — one
    /// per encoding profile — so the transport registry can pick the
    /// right factory based on the connection's
    /// <c>TransportProfileUri</c> field.
    /// </para>
    /// <para>
    /// The factory resolves the password configured under
    /// <see cref="MqttConnectionOptions.PasswordSecretId"/> through
    /// the application's <see cref="ISecretRegistry"/> before handing
    /// the resolved bytes to the transport. The cleartext password is
    /// never serialized into configuration.
    /// </para>
    /// </remarks>
    public sealed class MqttPubSubTransportFactory : IPubSubTransportFactory
    {
        private const string DefaultSecretStoreType = "InMemory";

        private readonly IMqttClientFactory m_clientFactory;
        private readonly MqttConnectionOptions m_defaultOptions;
        private readonly ISecretRegistry? m_secretRegistry;
        private readonly IPubSubDiagnostics? m_diagnostics;
        private readonly string m_transportProfileUri;

        /// <summary>
        /// Initializes a new <see cref="MqttPubSubTransportFactory"/>.
        /// </summary>
        /// <param name="transportProfileUri">
        /// One of
        /// <see cref="Profiles.PubSubMqttJsonTransport"/> or
        /// <see cref="Profiles.PubSubMqttUadpTransport"/>. Required so
        /// the transport registry can dispatch to the right factory
        /// per connection profile.
        /// </param>
        /// <param name="clientFactory">
        /// <see cref="IMqttClientFactory"/> used to create the
        /// underlying client adapter. Wired by DI;
        /// tests inject a fake.
        /// </param>
        /// <param name="defaultOptions">
        /// Default connection options applied to each transport. The
        /// caller may override per-connection via the connection's
        /// <c>ConnectionProperties</c>.
        /// </param>
        /// <param name="secretRegistry">
        /// Optional <see cref="ISecretRegistry"/> used to resolve
        /// <see cref="MqttConnectionOptions.PasswordSecretId"/>.
        /// </param>
        /// <param name="diagnostics">
        /// Optional shared diagnostics sink. The DI container wires the
        /// per-component diagnostics container.
        /// </param>
        public MqttPubSubTransportFactory(
            string transportProfileUri,
            IMqttClientFactory clientFactory,
            IOptions<MqttConnectionOptions> defaultOptions,
            ISecretRegistry? secretRegistry = null,
            IPubSubDiagnostics? diagnostics = null)
        {
            if (string.IsNullOrEmpty(transportProfileUri))
            {
                throw new ArgumentException(
                    "transportProfileUri must be supplied.",
                    nameof(transportProfileUri));
            }
            if (!string.Equals(
                    transportProfileUri,
                    Profiles.PubSubMqttJsonTransport,
                    StringComparison.Ordinal)
                && !string.Equals(
                    transportProfileUri,
                    Profiles.PubSubMqttUadpTransport,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"transportProfileUri '{transportProfileUri}' is not an MQTT profile.",
                    nameof(transportProfileUri));
            }
            if (clientFactory is null)
            {
                throw new ArgumentNullException(nameof(clientFactory));
            }
            if (defaultOptions is null)
            {
                throw new ArgumentNullException(nameof(defaultOptions));
            }
            m_transportProfileUri = transportProfileUri;
            m_clientFactory = clientFactory;
            m_defaultOptions = defaultOptions.Value ?? new MqttConnectionOptions();
            m_secretRegistry = secretRegistry;
            m_diagnostics = diagnostics;
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_transportProfileUri;

        /// <inheritdoc/>
        public IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (connection.Address.IsNull)
            {
                throw new NotSupportedException(
                    "PubSubConnection.Address is required for MQTT transport.");
            }
            if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? networkAddress)
                || networkAddress is null)
            {
                throw new NotSupportedException(
                    "MQTT transport requires a NetworkAddressUrlDataType address payload.");
            }
            string? url = networkAddress.Url;
            if (string.IsNullOrEmpty(url))
            {
                throw new NotSupportedException(
                    "NetworkAddressUrlDataType.Url is required for MQTT transport.");
            }

            MqttEndpoint endpoint = MqttEndpointParser.Parse(url);
            MqttConnectionOptions options = CloneOptionsWithEndpoint(m_defaultOptions, url);
            ResolvePassword(options);

            PubSubTransportDirection direction = DetermineDirection(connection);
            return new MqttBrokerTransport(
                connection,
                endpoint,
                direction,
                options,
                m_clientFactory,
                telemetry,
                timeProvider,
                m_diagnostics);
        }

        private static MqttConnectionOptions CloneOptionsWithEndpoint(
            MqttConnectionOptions source,
            string endpointUrl)
        {
            return new MqttConnectionOptions
            {
                Endpoint = endpointUrl,
                ClientId = source.ClientId,
                ProtocolVersion = source.ProtocolVersion,
                CleanSession = source.CleanSession,
                KeepAlivePeriod = source.KeepAlivePeriod,
                UserName = source.UserName,
                PasswordSecretId = source.PasswordSecretId,
                Tls = source.Tls,
                Topics = source.Topics,
                ConnectTimeout = source.ConnectTimeout,
                MaxConcurrentSubscriptions = source.MaxConcurrentSubscriptions
            };
        }

        private void ResolvePassword(MqttConnectionOptions options)
        {
            if (string.IsNullOrEmpty(options.PasswordSecretId))
            {
                return;
            }
            if (m_secretRegistry is null)
            {
                throw new InvalidOperationException(
                    "MqttConnectionOptions.PasswordSecretId is set but no " +
                    "ISecretRegistry was registered with the transport factory.");
            }
            SecretIdentifier id = ParseSecretIdentifier(options.PasswordSecretId);
            ISecret? secret = m_secretRegistry.TryGet(id);
            if (secret is null)
            {
                throw new InvalidOperationException(
                    $"Password secret '{options.PasswordSecretId}' could not be " +
                    "resolved from the registered secret stores.");
            }
            try
            {
                options.PasswordBytes = secret.Bytes.ToArray();
            }
            finally
            {
                secret.Dispose();
            }
        }

        private static SecretIdentifier ParseSecretIdentifier(string secretId)
        {
            int separator = secretId.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator >= secretId.Length - 1)
            {
                return new SecretIdentifier(secretId, DefaultSecretStoreType);
            }
            string storeType = secretId.Substring(0, separator);
            string name = secretId.Substring(separator + 1);
            return new SecretIdentifier(name, storeType);
        }

        private static PubSubTransportDirection DetermineDirection(
            PubSubConnectionDataType connection)
        {
            PubSubTransportDirection direction = PubSubTransportDirection.None;
            if (!connection.WriterGroups.IsNull && connection.WriterGroups.Count > 0)
            {
                direction |= PubSubTransportDirection.Send;
            }
            if (!connection.ReaderGroups.IsNull && connection.ReaderGroups.Count > 0)
            {
                direction |= PubSubTransportDirection.Receive;
            }
            if (direction == PubSubTransportDirection.None)
            {
                direction = PubSubTransportDirection.SendReceive;
            }
            return direction;
        }
    }
}
