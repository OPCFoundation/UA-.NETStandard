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

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Connection-level options for the MQTT broker transport. Bound
    /// from <c>IConfiguration</c> via
    /// <c>EnableConfigurationBindingGenerator</c>, instantiated by the
    /// fluent DI surface, or supplied directly to the
    /// <see cref="MqttPubSubTransportFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors the MQTT connection property surface defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.4">
    /// Part 14 §7.3.4.4 Connection properties</see>. Credentials are
    /// looked up through the OPC UA secret store via
    /// <see cref="PasswordSecretId"/>; no plain-text password field is
    /// ever exposed.
    /// </para>
    /// <para>
    /// All <see cref="TimeSpan"/> properties accept ISO-8601 duration
    /// strings when bound from <c>IConfiguration</c>.
    /// </para>
    /// </remarks>
    public sealed class MqttConnectionOptions
    {
        /// <summary>
        /// Broker endpoint URL — <c>mqtt://host[:port]</c> for
        /// plaintext (port 1883 default) or
        /// <c>mqtts://host[:port]</c> for TLS (port 8883 default).
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Optional MQTT <c>ClientID</c>; when
        /// <see langword="null"/> the transport derives one from the
        /// PubSubConnection's PublisherId per Part 14 §7.3.4.4.
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Negotiated MQTT protocol version. Defaults to MQTT 5.0
        /// (§7.3.4.4); set to <see cref="MqttProtocolVersion.V311"/>
        /// for brokers that don't support the v5 properties.
        /// </summary>
        public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.V500;

        /// <summary>
        /// MQTT CleanSession flag. Defaults to <see langword="true"/>
        /// so the broker does not retain subscription state across
        /// reconnects.
        /// </summary>
        public bool CleanSession { get; set; } = true;

        /// <summary>
        /// MQTT keep-alive period. Defaults to 60 seconds (the broker
        /// default recommended by the MQTT specification).
        /// </summary>
        public TimeSpan KeepAlivePeriod { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Optional MQTT user name.
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// Identifier of the password secret in the application's
        /// <c>ISecretStore</c>. The transport factory resolves the
        /// secret at connect time so the configuration file never
        /// carries the cleartext password. <see langword="null"/>
        /// disables password authentication.
        /// </summary>
        public string? PasswordSecretId { get; set; }

        /// <summary>
        /// Authentication profile URI used to select SASL authentication per Part 14 §7.3.4.3.
        /// </summary>
        public string? AuthenticationProfileUri { get; set; }

        /// <summary>
        /// Resource URI associated with <see cref="AuthenticationProfileUri"/>.
        /// </summary>
        public string? ResourceUri { get; set; }

        /// <summary>
        /// MQTT Last-Will topic for publisher status presence messages.
        /// </summary>
        public string? WillTopic { get; set; }

        /// <summary>
        /// MQTT Last-Will QoS for publisher status presence messages.
        /// </summary>
        public MqttQualityOfService WillQos { get; set; } = MqttQualityOfService.AtLeastOnce;

        /// <summary>
        /// MQTT Last-Will retain flag for publisher status presence messages.
        /// </summary>
        public bool WillRetain { get; set; } = true;

        /// <summary>
        /// TLS options. <see langword="null"/> picks up scheme-derived
        /// defaults (TLS off for <c>mqtt://</c>, on for
        /// <c>mqtts://</c>).
        /// </summary>
        public MqttTlsOptions? Tls { get; set; }

        /// <summary>
        /// Allows MQTT user credentials to be sent over plaintext
        /// <c>mqtt://</c> connections. Defaults to <see langword="false"/>.
        /// </summary>
        public bool AllowCredentialsOverPlaintext { get; set; }

        /// <summary>
        /// Topic-level options (prefix, retain flags, default QoS).
        /// </summary>
        public MqttTopicOptions Topics { get; set; } = new MqttTopicOptions();

        /// <summary>
        /// Timeout applied to the initial CONNECT exchange.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Maximum number of topic filters the adapter may install on
        /// a single subscriber connection. The default of 64 matches
        /// the common per-connection budget of public brokers.
        /// </summary>
        public int MaxConcurrentSubscriptions { get; set; } = 64;

        /// <summary>
        /// Maximum size (in bytes) of a single UADP NetworkMessage
        /// before the publisher chunks it via
        /// <see cref="Encoding.Uadp.UadpChunker"/>. The
        /// default of 65535 matches the MQTT v3.1.1 maximum single
        /// PUBLISH payload size; raise on broker / client pairs that
        /// negotiate a larger MQTT v5 maximum packet size.
        /// </summary>
        /// <remarks>
        /// Implements
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.4">
        /// Part 14 §7.2.4.4.4 ChunkedNetworkMessage</see>.
        /// </remarks>
        public int MaxNetworkMessageSize { get; set; } = 65535;

        /// <summary>
        /// Resolved password bytes populated by the transport factory
        /// after looking up <see cref="PasswordSecretId"/> in the
        /// secret store. Not bound from configuration; never persisted
        /// or serialized. Adapter implementations consume this value
        /// when issuing the MQTT CONNECT packet.
        /// </summary>
        internal byte[]? PasswordBytes { get; set; }

        /// <summary>
        /// Encoded Last-Will payload populated by publisher presence scheduling.
        /// </summary>
        internal byte[]? WillPayload { get; set; }
    }
}
