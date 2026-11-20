// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// TLS configuration for an Apache Kafka connection. The
    /// connection's <see cref="KafkaConnectionOptions.Endpoint"/>
    /// scheme (<c>kafka</c> vs <c>kafkas</c>) drives the default
    /// <see cref="UseTls"/> value; callers may override afterwards.
    /// </summary>
    /// <remarks>
    /// Backs the TLS transport surface required by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. Unlike the MQTT
    /// transport, the Kafka client consumes certificate and key material
    /// through file-system paths rather than the OPC UA certificate store,
    /// so this POCO references PEM file locations. No private key material
    /// is embedded in configuration files.
    /// </remarks>
    public sealed class KafkaTlsOptions
    {
        /// <summary>
        /// Enables TLS on the underlying broker connection. Defaults to
        /// <see langword="false"/>; the transport factory sets it to
        /// <see langword="true"/> automatically when the endpoint scheme
        /// is <c>kafkas</c>.
        /// </summary>
        public bool UseTls { get; set; }

        /// <summary>
        /// When <see langword="true"/> the client verifies the broker
        /// certificate against the configured trust anchors; when
        /// <see langword="false"/> certificate verification is disabled.
        /// Disabling verification should only be used for local
        /// development.
        /// </summary>
        public bool ValidateServerCertificate { get; set; } = true;

        /// <summary>
        /// Path to a PEM file containing the certificate authority (CA)
        /// certificates that form the trust chain used to validate the
        /// broker certificate. <see langword="null"/> defers to the
        /// platform / runtime default trust store.
        /// </summary>
        public string? CaCertificatePath { get; set; }

        /// <summary>
        /// Path to a PEM file containing the client certificate presented
        /// during the TLS handshake for mutual TLS.
        /// </summary>
        public string? ClientCertificatePath { get; set; }

        /// <summary>
        /// Path to a PEM file containing the client private key that
        /// matches <see cref="ClientCertificatePath"/>.
        /// </summary>
        public string? ClientKeyPath { get; set; }

        /// <summary>
        /// Creates a deep copy of these TLS options so per-connection
        /// mutations (for example the automatic <c>kafkas</c> TLS upgrade)
        /// never leak back into a shared default options instance.
        /// </summary>
        /// <returns>A new, independent <see cref="KafkaTlsOptions"/>.</returns>
        public KafkaTlsOptions Clone()
        {
            return new KafkaTlsOptions
            {
                UseTls = UseTls,
                ValidateServerCertificate = ValidateServerCertificate,
                CaCertificatePath = CaCertificatePath,
                ClientCertificatePath = ClientCertificatePath,
                ClientKeyPath = ClientKeyPath
            };
        }
    }
}
