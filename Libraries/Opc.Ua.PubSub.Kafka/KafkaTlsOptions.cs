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
    /// transport, the underlying librdkafka client consumes certificate
    /// and key material through file-system paths rather than the OPC UA
    /// certificate store, so this POCO references PEM file locations. No
    /// private key material is embedded in configuration files.
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
        /// broker certificate. Maps to the librdkafka
        /// <c>ssl.ca.location</c> property. <see langword="null"/> defers
        /// to the platform / runtime default trust store.
        /// </summary>
        public string? CaCertificatePath { get; set; }

        /// <summary>
        /// Path to a PEM file containing the client certificate presented
        /// during the TLS handshake for mutual TLS. Maps to the
        /// librdkafka <c>ssl.certificate.location</c> property.
        /// </summary>
        public string? ClientCertificatePath { get; set; }

        /// <summary>
        /// Path to a PEM file containing the client private key that
        /// matches <see cref="ClientCertificatePath"/>. Maps to the
        /// librdkafka <c>ssl.key.location</c> property.
        /// </summary>
        public string? ClientKeyPath { get; set; }
    }
}
