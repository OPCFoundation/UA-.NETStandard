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

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Transport-level security protocol negotiated with the Kafka
    /// brokers, mirroring the librdkafka <c>security.protocol</c>
    /// property.
    /// </summary>
    /// <remarks>
    /// Implements the security profile selector of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public enum KafkaSecurityProtocol
    {
        /// <summary>
        /// Unauthenticated plaintext transport (<c>PLAINTEXT</c>).
        /// </summary>
        Plaintext,

        /// <summary>
        /// TLS-protected transport without SASL (<c>SSL</c>).
        /// </summary>
        Ssl,

        /// <summary>
        /// SASL authentication over plaintext transport
        /// (<c>SASL_PLAINTEXT</c>).
        /// </summary>
        SaslPlaintext,

        /// <summary>
        /// SASL authentication over TLS transport (<c>SASL_SSL</c>).
        /// </summary>
        SaslSsl
    }

    /// <summary>
    /// SASL authentication mechanism used when the negotiated
    /// <see cref="KafkaSecurityProtocol"/> carries SASL, mirroring the
    /// librdkafka <c>sasl.mechanism</c> property.
    /// </summary>
    /// <remarks>
    /// Implements the SASL mechanism selector of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public enum KafkaSaslMechanism
    {
        /// <summary>
        /// No SASL mechanism is configured.
        /// </summary>
        None,

        /// <summary>
        /// SASL/PLAIN username and password mechanism.
        /// </summary>
        Plain,

        /// <summary>
        /// SASL/SCRAM-SHA-256 challenge-response mechanism.
        /// </summary>
        ScramSha256,

        /// <summary>
        /// SASL/SCRAM-SHA-512 challenge-response mechanism.
        /// </summary>
        ScramSha512,

        /// <summary>
        /// SASL/OAUTHBEARER token mechanism.
        /// </summary>
        OAuthBearer
    }

    /// <summary>
    /// Offset reset policy applied when a consumer group has no
    /// committed offset for a partition, mirroring the librdkafka
    /// <c>auto.offset.reset</c> property.
    /// </summary>
    /// <remarks>
    /// Implements the subscriber start-offset policy of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public enum KafkaAutoOffsetReset
    {
        /// <summary>
        /// Start consuming from the most recent record
        /// (<c>latest</c>).
        /// </summary>
        Latest,

        /// <summary>
        /// Start consuming from the earliest retained record
        /// (<c>earliest</c>).
        /// </summary>
        Earliest
    }

    /// <summary>
    /// Connection-level options for the Apache Kafka broker transport.
    /// Bound from <c>IConfiguration</c> via
    /// <c>EnableConfigurationBindingGenerator</c>, instantiated by the
    /// fluent DI surface, or supplied directly to the
    /// <see cref="KafkaPubSubTransportFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors the connection property surface defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. Credentials are
    /// looked up through the OPC UA secret store via
    /// <see cref="PasswordSecretId"/>; no plain-text password field is
    /// ever exposed.
    /// </para>
    /// <para>
    /// All <see cref="TimeSpan"/> properties accept ISO-8601 duration
    /// strings when bound from <c>IConfiguration</c>.
    /// </para>
    /// </remarks>
    public sealed class KafkaConnectionOptions
    {
        /// <summary>
        /// Broker endpoint URL — <c>kafka://host[:port][,host[:port]...]</c>
        /// for plaintext or <c>kafkas://host[:port][,...]</c> for TLS.
        /// The default port is 9092.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Comma-separated <c>host:port</c> bootstrap server list passed
        /// to librdkafka <c>bootstrap.servers</c>. Populated by the
        /// transport factory from <see cref="Endpoint"/> when not set
        /// explicitly.
        /// </summary>
        public string BootstrapServers { get; set; } = string.Empty;

        /// <summary>
        /// Optional Kafka <c>client.id</c>; when <see langword="null"/>
        /// the transport derives one from the PubSubConnection's
        /// PublisherId.
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Consumer <c>group.id</c> used by subscriber connections so
        /// partitions and committed offsets are shared across the group.
        /// </summary>
        public string? GroupId { get; set; }

        /// <summary>
        /// Transport security protocol. Defaults to
        /// <see cref="KafkaSecurityProtocol.Plaintext"/>; the transport
        /// factory upgrades it to an SSL variant when the endpoint scheme
        /// is <c>kafkas</c>.
        /// </summary>
        public KafkaSecurityProtocol SecurityProtocol { get; set; }
            = KafkaSecurityProtocol.Plaintext;

        /// <summary>
        /// SASL authentication mechanism. Defaults to
        /// <see cref="KafkaSaslMechanism.None"/> (no SASL).
        /// </summary>
        public KafkaSaslMechanism SaslMechanism { get; set; } = KafkaSaslMechanism.None;

        /// <summary>
        /// Optional SASL user name.
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// Identifier of the password secret in the application's
        /// <c>ISecretStore</c>. The transport factory resolves the secret
        /// at connect time so the configuration file never carries the
        /// cleartext password. <see langword="null"/> disables password
        /// authentication.
        /// </summary>
        public string? PasswordSecretId { get; set; }

        /// <summary>
        /// Authentication profile URI used to select SASL authentication
        /// per Part 14 §7.3.4.3.
        /// </summary>
        public string? AuthenticationProfileUri { get; set; }

        /// <summary>
        /// Resource URI associated with
        /// <see cref="AuthenticationProfileUri"/>.
        /// </summary>
        public string? ResourceUri { get; set; }

        /// <summary>
        /// Allows SASL credentials to be sent over plaintext
        /// <c>kafka://</c> connections. Defaults to
        /// <see langword="false"/>.
        /// </summary>
        public bool AllowCredentialsOverPlaintext { get; set; }

        /// <summary>
        /// TLS options. <see langword="null"/> picks up scheme-derived
        /// defaults (TLS off for <c>kafka://</c>, on for
        /// <c>kafkas://</c>).
        /// </summary>
        public KafkaTlsOptions? Tls { get; set; }

        /// <summary>
        /// Producer delivery guarantee mapped to the librdkafka
        /// <c>acks</c> / <c>enable.idempotence</c> settings. Defaults to
        /// <see cref="KafkaQualityOfService.AtLeastOnce"/>.
        /// </summary>
        public KafkaQualityOfService DeliveryGuarantee { get; set; }
            = KafkaQualityOfService.AtLeastOnce;

        /// <summary>
        /// Consumer offset reset policy. Defaults to
        /// <see cref="KafkaAutoOffsetReset.Latest"/>.
        /// </summary>
        public KafkaAutoOffsetReset AutoOffsetReset { get; set; } = KafkaAutoOffsetReset.Latest;

        /// <summary>
        /// Whether the consumer commits offsets automatically. When
        /// <see langword="false"/> the transport commits each record
        /// after it has been queued for delivery. Defaults to
        /// <see langword="true"/>.
        /// </summary>
        public bool EnableAutoCommit { get; set; } = true;

        /// <summary>
        /// Topic-level options (fallback topic prefix).
        /// </summary>
        public KafkaTopicOptions Topics { get; set; } = new KafkaTopicOptions();

        /// <summary>
        /// Timeout applied to the initial metadata / connection exchange.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Maximum time the producer waits for a record to be delivered
        /// before failing it, mapped to librdkafka
        /// <c>message.timeout.ms</c>.
        /// </summary>
        public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum size (in bytes) of a single Kafka record, mapped to
        /// librdkafka <c>message.max.bytes</c>. The default of 1048576
        /// matches the common broker default.
        /// </summary>
        public int MaxMessageSize { get; set; } = 1048576;

        /// <summary>
        /// Resolved password bytes populated by the transport factory
        /// after looking up <see cref="PasswordSecretId"/> in the secret
        /// store. Not bound from configuration; never persisted or
        /// serialized. Adapter implementations consume this value when
        /// establishing the SASL credentials.
        /// </summary>
        internal byte[]? PasswordBytes { get; set; }
    }
}
