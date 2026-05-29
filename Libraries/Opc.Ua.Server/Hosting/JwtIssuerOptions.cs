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

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Opc.Ua.Identity;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Configuration for one trusted JWT issuer under
    /// <c>OpcUa:Server:Identity:Issuers</c>.
    /// </summary>
    public sealed class JwtIssuerOptions
    {
        /// <summary>
        /// Gets or sets the expected JWT <c>iss</c> claim.
        /// </summary>
        public string IssuerUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional RFC 7517 JWKS endpoint URI for key rotation.
        /// </summary>
        public string? JwksUri { get; set; }

        /// <summary>
        /// Gets inline static public keys for this issuer.
        /// </summary>
        public IList<JwtStaticKeyOptions> StaticKeys { get; } = [];

        /// <summary>
        /// Gets the JWS algorithms this issuer is allowed to use.
        /// </summary>
        public IList<string> Algorithms { get; } = ["RS256"];

        /// <summary>
        /// Gets or sets the expected audience for this issuer. When empty, the
        /// shared <see cref="DefaultAuthenticatorOptions.ExpectedAudience"/> is used.
        /// </summary>
        public string? Audience { get; set; }

        /// <summary>
        /// Validates the required issuer and key-source settings.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The issuer URI is empty or neither <see cref="JwksUri"/> nor
        /// <see cref="StaticKeys"/> is configured.
        /// </exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(IssuerUri))
            {
                throw new InvalidOperationException("JWT issuer configuration requires IssuerUri.");
            }
            if (string.IsNullOrWhiteSpace(JwksUri) && StaticKeys.Count == 0)
            {
                throw new InvalidOperationException(
                    "JWT issuer configuration requires JwksUri or at least one StaticKeys entry.");
            }
        }

        internal IReadOnlyList<string> GetEffectiveAlgorithms()
        {
            if (Algorithms.Count == 0)
            {
                return new[] { "RS256" };
            }

            var values = new List<string>(Algorithms.Count);
            foreach (string algorithm in Algorithms)
            {
                if (!string.IsNullOrWhiteSpace(algorithm))
                {
                    values.Add(algorithm);
                }
            }

            return values.Count == 0 ? new[] { "RS256" } : values.AsReadOnly();
        }
    }

    /// <summary>
    /// Inline JWK-compatible public-key material for a configured JWT issuer.
    /// </summary>
    public sealed class JwtStaticKeyOptions
    {
        /// <summary>
        /// Gets or sets the optional JOSE <c>kid</c> header value.
        /// </summary>
        public string? Kid { get; set; }

        /// <summary>
        /// Gets or sets the JWS algorithm for this key.
        /// </summary>
        public string Algorithm { get; set; } = "RS256";

        /// <summary>
        /// Gets or sets an RSA public key PEM in SubjectPublicKeyInfo or PKCS#1 form.
        /// </summary>
        public string? RsaPublicKeyPem { get; set; }

        /// <summary>
        /// Gets or sets the base64url-encoded RSA modulus (<c>n</c>).
        /// </summary>
        public string? RsaModulus { get; set; }

        /// <summary>
        /// Gets or sets the base64url-encoded RSA exponent (<c>e</c>).
        /// </summary>
        public string? RsaExponent { get; set; }

        /// <summary>
        /// Gets or sets the EC curve name (<c>P-256</c>, <c>P-384</c>, or <c>P-521</c>).
        /// </summary>
        public string? EcCurve { get; set; }

        /// <summary>
        /// Gets or sets the base64url-encoded EC public X coordinate.
        /// </summary>
        public string? EcX { get; set; }

        /// <summary>
        /// Gets or sets the base64url-encoded EC public Y coordinate.
        /// </summary>
        public string? EcY { get; set; }

        internal IssuerVerificationKey CreateVerificationKey()
        {
            if (string.IsNullOrWhiteSpace(Algorithm))
            {
                throw new InvalidOperationException("Static JWT key configuration requires Algorithm.");
            }

            if (!string.IsNullOrWhiteSpace(RsaPublicKeyPem))
            {
                return CreateRsaVerificationKey(ImportRsaPublicKeyPem(RsaPublicKeyPem!), Algorithm);
            }

            if (!string.IsNullOrWhiteSpace(RsaModulus) && !string.IsNullOrWhiteSpace(RsaExponent))
            {
                var parameters = new RSAParameters
                {
                    Modulus = Base64UrlDecode(RsaModulus!),
                    Exponent = Base64UrlDecode(RsaExponent!)
                };
                return CreateRsaVerificationKey(parameters, Algorithm);
            }

            if (!string.IsNullOrWhiteSpace(EcCurve) &&
                !string.IsNullOrWhiteSpace(EcX) &&
                !string.IsNullOrWhiteSpace(EcY))
            {
                var parameters = new ECParameters
                {
                    Curve = ToCurve(EcCurve!),
                    Q = new ECPoint
                    {
                        X = Base64UrlDecode(EcX!),
                        Y = Base64UrlDecode(EcY!)
                    }
                };
                ECDsa? ecdsa = null;
                try
                {
                    ecdsa = ECDsa.Create(parameters);
                    var key = new IssuerVerificationKey(Kid, ecdsa, Algorithm);
                    ecdsa = null;
                    return key;
                }
                finally
                {
                    ecdsa?.Dispose();
                }
            }

            throw new InvalidOperationException(
                "Static JWT key configuration requires RSA or EC public key material.");
        }

        private IssuerVerificationKey CreateRsaVerificationKey(RSAParameters parameters, string algorithm)
        {
            RSA? rsa = null;
            try
            {
                rsa = RSA.Create();
                rsa.ImportParameters(parameters);
                var key = new IssuerVerificationKey(Kid, rsa, algorithm);
                rsa = null;
                return key;
            }
            finally
            {
                rsa?.Dispose();
            }
        }

        private static RSAParameters ImportRsaPublicKeyPem(string pem)
        {
            string normalized = ReplaceOrdinal(pem, "\r", string.Empty);
            const string spkiHeader = "-----BEGIN PUBLIC KEY-----";
            const string spkiFooter = "-----END PUBLIC KEY-----";
            const string pkcs1Header = "-----BEGIN RSA PUBLIC KEY-----";
            const string pkcs1Footer = "-----END RSA PUBLIC KEY-----";

            if (TryReadPemBlock(normalized, pkcs1Header, pkcs1Footer, out byte[]? pkcs1))
            {
                return ReadRsaPublicKey(pkcs1!);
            }
            if (TryReadPemBlock(normalized, spkiHeader, spkiFooter, out byte[]? spki))
            {
                return ReadSubjectPublicKeyInfo(spki!);
            }

            throw new InvalidOperationException(
                "RsaPublicKeyPem must contain a PUBLIC KEY or RSA PUBLIC KEY PEM block.");
        }

        private static bool TryReadPemBlock(
            string pem,
            string header,
            string footer,
            out byte[]? der)
        {
            int headerIndex = pem.IndexOf(header, StringComparison.Ordinal);
            if (headerIndex < 0)
            {
                der = null;
                return false;
            }

            int contentStart = headerIndex + header.Length;
            int footerIndex = pem.IndexOf(footer, contentStart, StringComparison.Ordinal);
            if (footerIndex < 0)
            {
                throw new InvalidOperationException("PEM block is missing its footer.");
            }

            string base64 = ReplaceOrdinal(
                ReplaceOrdinal(
                    ReplaceOrdinal(pem[contentStart..footerIndex], "\n", string.Empty),
                    " ",
                    string.Empty),
                "\t",
                string.Empty);
            der = Convert.FromBase64String(base64);
            return true;
        }

        private static RSAParameters ReadSubjectPublicKeyInfo(byte[] der)
        {
            var reader = new DerReader(der);
            DerReader sequence = reader.ReadSequence();
            sequence.ReadSequence();
            byte[] publicKey = sequence.ReadBitString();
            sequence.EnsureEnd();
            reader.EnsureEnd();
            return ReadRsaPublicKey(publicKey);
        }

        private static RSAParameters ReadRsaPublicKey(byte[] der)
        {
            var reader = new DerReader(der);
            DerReader sequence = reader.ReadSequence();
            byte[] modulus = TrimUnsignedInteger(sequence.ReadInteger());
            byte[] exponent = TrimUnsignedInteger(sequence.ReadInteger());
            sequence.EnsureEnd();
            reader.EnsureEnd();
            return new RSAParameters { Modulus = modulus, Exponent = exponent };
        }

        private static byte[] TrimUnsignedInteger(byte[] value)
        {
            int offset = 0;
            while (offset < value.Length - 1 && value[offset] == 0)
            {
                offset++;
            }

            if (offset == 0)
            {
                return value;
            }

            var trimmed = new byte[value.Length - offset];
            Buffer.BlockCopy(value, offset, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        private static string ReplaceOrdinal(string value, string oldValue, string newValue)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            return value.Replace(oldValue, newValue, StringComparison.Ordinal);
#else
            return value.Replace(oldValue, newValue);
#endif
        }

        private static ECCurve ToCurve(string curveName)
        {
            return curveName switch
            {
                "P-256" or "prime256v1" or "secp256r1" => ECCurve.NamedCurves.nistP256,
                "P-384" or "secp384r1" => ECCurve.NamedCurves.nistP384,
                "P-521" or "secp521r1" => ECCurve.NamedCurves.nistP521,
                _ => throw new NotSupportedException(
                    $"JWT static key EC curve '{curveName}' is not supported.")
            };
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }

        private sealed class DerReader
        {
            private readonly byte[] m_data;
            private int m_offset;

            public DerReader(byte[] data)
            {
                m_data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public DerReader ReadSequence()
            {
                byte[] value = ReadElement(0x30);
                return new DerReader(value);
            }

            public byte[] ReadInteger()
            {
                return ReadElement(0x02);
            }

            public byte[] ReadBitString()
            {
                byte[] value = ReadElement(0x03);
                if (value.Length == 0 || value[0] != 0)
                {
                    throw new InvalidOperationException("RSA public-key BIT STRING has unsupported padding.");
                }

                var result = new byte[value.Length - 1];
                Buffer.BlockCopy(value, 1, result, 0, result.Length);
                return result;
            }

            public void EnsureEnd()
            {
                if (m_offset != m_data.Length)
                {
                    throw new InvalidOperationException("Unexpected trailing DER data.");
                }
            }

            private byte[] ReadElement(byte expectedTag)
            {
                if (m_offset >= m_data.Length || m_data[m_offset++] != expectedTag)
                {
                    throw new InvalidOperationException("Unexpected DER tag while reading RSA public key.");
                }

                int length = ReadLength();
                if (length < 0 || length > m_data.Length - m_offset)
                {
                    throw new InvalidOperationException("Invalid DER length while reading RSA public key.");
                }

                var value = new byte[length];
                Buffer.BlockCopy(m_data, m_offset, value, 0, length);
                m_offset += length;
                return value;
            }

            private int ReadLength()
            {
                if (m_offset >= m_data.Length)
                {
                    throw new InvalidOperationException("Missing DER length.");
                }

                int first = m_data[m_offset++];
                if ((first & 0x80) == 0)
                {
                    return first;
                }

                int lengthBytes = first & 0x7F;
                if (lengthBytes == 0 || lengthBytes > 4 || lengthBytes > m_data.Length - m_offset)
                {
                    throw new InvalidOperationException("Invalid DER length encoding.");
                }

                int length = 0;
                for (int i = 0; i < lengthBytes; i++)
                {
                    length = (length << 8) | m_data[m_offset++];
                }
                return length;
            }
        }
    }
}
