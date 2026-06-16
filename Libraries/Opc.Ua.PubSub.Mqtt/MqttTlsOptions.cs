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

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// TLS configuration for an MQTT connection. The connection's
    /// <see cref="MqttConnectionOptions.Endpoint"/> scheme
    /// (<c>mqtt</c> vs <c>mqtts</c>) drives the default
    /// <see cref="UseTls"/> value; callers may override afterwards.
    /// </summary>
    /// <remarks>
    /// Backs the MQTT TLS transport surface required by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>. Client
    /// certificates are resolved through the application's certificate
    /// store, not embedded in this POCO, so configuration files never
    /// carry private key material.
    /// </remarks>
    public sealed class MqttTlsOptions
    {
        /// <summary>
        /// Enables TLS on the underlying socket. Defaults to
        /// <see langword="false"/>; the transport factory sets it to
        /// <see langword="true"/> automatically when the endpoint
        /// scheme is <c>mqtts</c>.
        /// </summary>
        public bool UseTls { get; set; }

        /// <summary>
        /// When <see langword="true"/> the adapter validates the
        /// broker certificate via the application's
        /// <c>CertificateValidator</c>; when
        /// <see langword="false"/> all server certificates are
        /// accepted. Disabling validation should only be used for
        /// local development.
        /// </summary>
        public bool ValidateServerCertificate { get; set; } = true;

        /// <summary>
        /// Subject DN of a client certificate to present during the
        /// TLS handshake. Looked up in the application's
        /// <c>ICertificateStore</c>; never embedded directly so
        /// private key material is not stored in configuration files.
        /// </summary>
        public string? ClientCertificateSubject { get; set; }

        /// <summary>
        /// Optional allow-list of TLS cipher suites the adapter may
        /// negotiate. <see langword="null"/> defers to the OS / runtime
        /// default policy.
        /// </summary>
        public string[]? AllowedCipherSuites { get; set; }
    }
}
