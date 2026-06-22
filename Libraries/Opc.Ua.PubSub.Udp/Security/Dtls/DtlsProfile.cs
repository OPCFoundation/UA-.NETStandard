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

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// DTLS 1.3 PubSub profile descriptor from Part 14 §7.3.2.4.
    /// </summary>
    public sealed record DtlsProfile
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsProfile"/>.
        /// </summary>
        public DtlsProfile(
            string name,
            DtlsCipherSuite cipherSuite,
            DtlsNamedCurve keyExchangeCurve,
            DtlsNamedCurve certificateCurve,
            bool isMandatory)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("DTLS profile name is required.", nameof(name));
            }

            Name = name;
            CipherSuite = cipherSuite;
            KeyExchangeCurve = keyExchangeCurve;
            CertificateCurve = certificateCurve;
            IsMandatory = isMandatory;
        }

        /// <summary>
        /// OPC profile name as listed in the PubSub DTLS profile matrix.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// TLS 1.3 cipher suite selected by the profile.
        /// </summary>
        public DtlsCipherSuite CipherSuite { get; }

        /// <summary>
        /// ECDHE named group required for the handshake.
        /// </summary>
        public DtlsNamedCurve KeyExchangeCurve { get; }

        /// <summary>
        /// ECC certificate curve required for peer authentication.
        /// </summary>
        public DtlsNamedCurve CertificateCurve { get; }

        /// <summary>
        /// Indicates a mandatory OPC UA PubSub profile that must fail closed
        /// on the .NET BCL because Curve25519 / Curve448 are unavailable.
        /// </summary>
        public bool IsMandatory { get; }
    }

    /// <summary>
    /// TLS 1.3 cipher suites used by Part 14 DTLS profiles.
    /// </summary>
    public enum DtlsCipherSuite
    {
        /// <summary>
        /// TLS_AES_128_GCM_SHA256.
        /// </summary>
        TlsAes128GcmSha256,

        /// <summary>
        /// TLS_AES_256_GCM_SHA384.
        /// </summary>
        TlsAes256GcmSha384,

        /// <summary>
        /// TLS_CHACHA20_POLY1305_SHA256.
        /// </summary>
        TlsChaCha20Poly1305Sha256,

        /// <summary>
        /// OPC integrity-only TLS_SHA256_SHA256 profile.
        /// </summary>
        TlsSha256Sha256,

        /// <summary>
        /// OPC integrity-only TLS_SHA384_SHA384 profile.
        /// </summary>
        TlsSha384Sha384
    }

    /// <summary>
    /// DTLS named groups referenced by the PubSub profile matrix.
    /// </summary>
    public enum DtlsNamedCurve
    {
        /// <summary>
        /// NIST P-256 / secp256r1.
        /// </summary>
        NistP256,

        /// <summary>
        /// NIST P-384 / secp384r1.
        /// </summary>
        NistP384,

        /// <summary>
        /// BrainpoolP256r1.
        /// </summary>
        BrainpoolP256r1,

        /// <summary>
        /// BrainpoolP384r1.
        /// </summary>
        BrainpoolP384r1,

        /// <summary>
        /// Curve25519, unsupported by the portable .NET BCL.
        /// </summary>
        Curve25519,

        /// <summary>
        /// Curve448, unsupported by the portable .NET BCL.
        /// </summary>
        Curve448
    }
}
