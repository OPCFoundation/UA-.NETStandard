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
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// IConfiguration-bindable DTLS transport settings for Part 14 §7.3.2.4.
    /// </summary>
    public sealed class DtlsTransportOptions
    {
        /// <summary>
        /// Default profile chosen when the endpoint does not carry an explicit profile.
        /// </summary>
        public const string DefaultProfileName = "ECC_nistP256_AesGcm";

        /// <summary>
        /// DTLS profile name from the Part 14 DTLS profile matrix.
        /// </summary>
        public string ProfileName { get; set; } = DefaultProfileName;

        /// <summary>
        /// Maximum DTLS handshake datagram size before RFC 9147 handshake fragmentation is required.
        /// </summary>
        public int MaxHandshakeDatagramSize { get; set; } = 1200;

        /// <summary>
        /// Initial retransmission timeout for RFC 9147 handshake flights.
        /// </summary>
        public TimeSpan InitialRetransmissionTimeout { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum retransmission timeout for RFC 9147 handshake flights.
        /// </summary>
        public TimeSpan MaxRetransmissionTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Enables DTLS 1.3 stateless HelloRetryRequest cookies for listeners.
        /// </summary>
        public bool RequireHelloRetryRequestCookie { get; set; } = true;

        /// <summary>
        /// Local ECC certificate with private key used for CertificateVerify.
        /// </summary>
        public X509Certificate2? LocalCertificate { get; set; }

        /// <summary>
        /// Optional local certificate chain sent in the TLS Certificate message.
        /// </summary>
        public IList<X509Certificate2> LocalCertificateChain { get; } = [];

        /// <summary>
        /// Optional direct-construction peer certificate validator.
        /// </summary>
        public ICertificateValidatorEx? PeerCertificateValidator { get; set; }
    }
}
