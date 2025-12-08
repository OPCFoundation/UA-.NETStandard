/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Represents a PKCS#10 Certificate Signing Request (CSR).
    /// </summary>
    /// <remarks>
    /// This class provides functionality to parse and verify PKCS#10 CSRs
    /// using .NET Framework APIs, eliminating the need for BouncyCastle.
    /// 
    /// Based on RFC 2986: PKCS #10: Certification Request Syntax Specification
    /// https://tools.ietf.org/html/rfc2986
    /// </remarks>
    public sealed class Pkcs10CertificationRequest
    {
        private readonly byte[] m_certificationRequestInfo;
        private readonly byte[] m_signature;
        private readonly string m_signatureAlgorithm;
        private readonly byte[] m_subjectPublicKeyInfo;
        private readonly X500DistinguishedName m_subject;
        private readonly byte[] m_attributes;

        /// <summary>
        /// Initializes a new instance of the Pkcs10CertificationRequest class from DER-encoded data.
        /// </summary>
        /// <param name="encodedRequest">The DER-encoded PKCS#10 certificate request.</param>
        /// <exception cref="ArgumentNullException">Thrown when encodedRequest is null.</exception>
        /// <exception cref="CryptographicException">Thrown when the request cannot be parsed.</exception>
        public Pkcs10CertificationRequest(byte[] encodedRequest)
        {
            if (encodedRequest == null)
            {
                throw new ArgumentNullException(nameof(encodedRequest));
            }

            try
            {
                // Parse the outer SEQUENCE
                var reader = new AsnReader(encodedRequest, AsnEncodingRules.DER);
                AsnReader sequenceReader = reader.ReadSequence();

                // Read CertificationRequestInfo
                m_certificationRequestInfo = sequenceReader.ReadEncodedValue().ToArray();

                // Parse CertificationRequestInfo to extract components
                (m_subject, m_subjectPublicKeyInfo, m_attributes) = 
                    ParseCertificationRequestInfo(m_certificationRequestInfo);

                // Read SignatureAlgorithm
                AsnReader algReader = sequenceReader.ReadSequence();
                m_signatureAlgorithm = algReader.ReadObjectIdentifier();

                // Read Signature (BIT STRING)
                m_signature = sequenceReader.ReadBitString(out int unusedBitCount);
                if (unusedBitCount != 0)
                {
                    throw new CryptographicException("Invalid signature bit string padding.");
                }

                sequenceReader.ThrowIfNotEmpty();
            }
            catch (AsnContentException ex)
            {
                throw new CryptographicException("Failed to parse PKCS#10 certificate request.", ex);
            }
        }

        /// <summary>
        /// Gets the subject distinguished name from the CSR.
        /// </summary>
        public X500DistinguishedName Subject => m_subject;

        /// <summary>
        /// Gets the subject public key info as DER-encoded bytes.
        /// </summary>
        public byte[] SubjectPublicKeyInfo => m_subjectPublicKeyInfo;

        /// <summary>
        /// Gets the attributes from the CSR.
        /// </summary>
        public byte[] Attributes => m_attributes;

        /// <summary>
        /// Verifies the signature of the certificate request.
        /// </summary>
        /// <returns>True if the signature is valid; otherwise, false.</returns>
        public bool Verify()
        {
            try
            {
                // Get the hash algorithm from the signature algorithm OID
                HashAlgorithmName hashAlgorithm = Oids.GetHashAlgorithmName(m_signatureAlgorithm);

                // Parse the public key to get the key for verification
                var publicKeyReader = new AsnReader(m_subjectPublicKeyInfo, AsnEncodingRules.DER);
                AsnReader pkSequence = publicKeyReader.ReadSequence();

                // Read algorithm identifier
                AsnReader algIdReader = pkSequence.ReadSequence();
                string keyAlgorithmOid = algIdReader.ReadObjectIdentifier();

                // Read public key (BIT STRING)
                byte[] publicKeyBytes = pkSequence.ReadBitString(out int unusedBitCount);
                if (unusedBitCount != 0)
                {
                    throw new CryptographicException("Invalid public key bit string padding.");
                }

                // Verify based on key type
                if (keyAlgorithmOid == Oids.Rsa)
                {
                    return VerifyRsaSignature(publicKeyBytes, hashAlgorithm);
                }
                else if (keyAlgorithmOid == Oids.ECPublicKey)
                {
                    return VerifyEcdsaSignature(publicKeyBytes, hashAlgorithm);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported key algorithm: {keyAlgorithmOid}");
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the certification request info as DER-encoded bytes.
        /// </summary>
        /// <returns>The certification request info bytes.</returns>
        public byte[] GetCertificationRequestInfo()
        {
            return m_certificationRequestInfo;
        }

        private static (X500DistinguishedName subject, byte[] subjectPublicKeyInfo, byte[] attributes) 
            ParseCertificationRequestInfo(byte[] certificationRequestInfo)
        {
            var infoReader = new AsnReader(certificationRequestInfo, AsnEncodingRules.DER);
            AsnReader infoSequence = infoReader.ReadSequence();

            // Read version (INTEGER)
            infoSequence.ReadInteger();

            // Read subject (Name - SEQUENCE)
            byte[] subjectBytes = infoSequence.ReadEncodedValue().ToArray();
            var subject = new X500DistinguishedName(subjectBytes);

            // Read SubjectPublicKeyInfo (SEQUENCE)
            byte[] subjectPublicKeyInfo = infoSequence.ReadEncodedValue().ToArray();

            // Read attributes [0] IMPLICIT
            byte[] attributes = null;
            if (infoSequence.HasData)
            {
                // Attributes are context-specific tag [0]
                attributes = infoSequence.ReadEncodedValue().ToArray();
            }

            return (subject, subjectPublicKeyInfo, attributes);
        }

        private bool VerifyRsaSignature(byte[] publicKeyBytes, HashAlgorithmName hashAlgorithm)
        {
            // Parse RSA public key from PKCS#1 format
            var keyReader = new AsnReader(publicKeyBytes, AsnEncodingRules.DER);
            AsnReader keySequence = keyReader.ReadSequence();

            // Read modulus and exponent
            byte[] modulus = keySequence.ReadIntegerBytes().ToArray();
            byte[] exponent = keySequence.ReadIntegerBytes().ToArray();

            // Create RSA parameters
            var rsaParameters = new RSAParameters
            {
                Modulus = modulus,
                Exponent = exponent
            };

            using var rsa = RSA.Create();
            rsa.ImportParameters(rsaParameters);

            // Verify signature using PKCS#1 v1.5 padding
            return rsa.VerifyData(
                m_certificationRequestInfo,
                m_signature,
                hashAlgorithm,
                RSASignaturePadding.Pkcs1);
        }

        private bool VerifyEcdsaSignature(byte[] publicKeyBytes, HashAlgorithmName hashAlgorithm)
        {
#if NET6_0_OR_GREATER
            // .NET 6+ has ImportSubjectPublicKeyInfo
            using var ecdsa = ECDsa.Create();
            
            try
            {
                ecdsa.ImportSubjectPublicKeyInfo(m_subjectPublicKeyInfo, out _);
                
                // Verify signature
                return ecdsa.VerifyData(
                    m_certificationRequestInfo,
                    m_signature,
                    hashAlgorithm);
            }
            catch
            {
                return false;
            }
#else
            // For .NET Framework 4.8 and .NET Standard 2.x, ECDSA CSR verification is not supported
            // This is acceptable as the GDS Server primarily uses RSA certificates
            throw new NotSupportedException(
                "ECDSA certificate signing request verification is not supported on this platform. " +
                "Please use .NET 6.0 or later.");
#endif
        }
    }
}
