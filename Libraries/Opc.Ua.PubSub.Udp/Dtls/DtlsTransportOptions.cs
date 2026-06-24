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
using Opc.Ua;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// IConfiguration-bindable DTLS transport settings for Part 14 §7.3.2.4.
    /// </summary>
    public sealed class DtlsTransportOptions
    {
        /// <summary>
        /// Default profile preferred when neither the endpoint nor configuration name a profile and
        /// no other enabled profile is selected at runtime.
        /// </summary>
        public const string DefaultProfileName = "ECC_nistP256_AesGcm";

        /// <summary>
        /// Optional preferred DTLS profile name from the Part 14 DTLS profile matrix. When set and the
        /// profile is enabled and supported by the runtime it is selected; otherwise the first
        /// enabled and supported profile is chosen at runtime. Cipher suites/profiles are never pinned
        /// by configuration: this is only a preference, not a hard requirement.
        /// </summary>
        public string? PreferredProfileName { get; set; }

        /// <summary>
        /// Profile names disabled at configuration time even if the runtime supports them. Matching is
        /// case-insensitive and selection fails closed when all supported profiles are disabled.
        /// </summary>
        public ISet<string> DisabledProfiles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
        /// Local ECC certificates with private keys used for CertificateVerify. Multiple certificates
        /// may be registered; the handshake selects the certificate whose ECDsa named curve matches the
        /// negotiated profile certificate curve, similar to how secure channels register an application
        /// certificate per certificate type.
        /// </summary>
        public IList<Certificate> LocalCertificates { get; } = [];

        /// <summary>
        /// Local certificate identifiers resolved from the configured certificate manager or store
        /// registry when a DTLS context is created. Resolved private-key certificates are merged with
        /// <see cref="LocalCertificates"/> before the handshake selects the certificate whose ECDsa
        /// named curve matches the negotiated profile certificate curve.
        /// </summary>
        public IList<CertificateIdentifier> LocalCertificateIdentifiers { get; } = [];

        /// <summary>
        /// Optional direct-construction peer certificate validator.
        /// </summary>
        public ICertificateValidatorEx? PeerCertificateValidator { get; set; }

        /// <summary>
        /// Requests DTLS 1.3 mutual authentication. When <see langword="false"/> (the default) the
        /// transport uses the one-way authentication model in which only the server presents a
        /// certificate; for Part 14 PubSub the publisher is normally authenticated at the message
        /// layer through SKS-managed security keys, so client certificates are not required at the
        /// DTLS layer. When <see langword="true"/> the server includes a CertificateRequest in its
        /// flight, the client answers with its Certificate and CertificateVerify, and the server
        /// validates the client chain through the same fail-closed certificate validator used for the
        /// server certificate. Enabling mutual authentication requires a configured peer certificate
        /// validator on the server and a local certificate on the client; otherwise the handshake
        /// fails closed.
        /// </summary>
        public bool RequireClientCertificate { get; set; }
    }
}
