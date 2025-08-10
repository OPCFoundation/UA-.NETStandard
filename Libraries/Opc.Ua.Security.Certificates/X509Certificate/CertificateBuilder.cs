/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
#if NET472_OR_GREATER
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Opc.Ua.Security.Certificates.BouncyCastle;
#endif
using ECCurve = System.Security.Cryptography.ECCurve;
using X509Extension = System.Security.Cryptography.X509Certificates.X509Extension;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Builds a Certificate.
    /// </summary>
    public sealed class CertificateBuilder : CertificateBuilderBase
    {
        /// <summary>
        /// Create a Certificate builder.
        /// </summary>
        public static ICertificateBuilder Create(X500DistinguishedName subjectName)
        {
            return new CertificateBuilder(subjectName);
        }

        /// <summary>
        /// Create a Certificate builder.
        /// </summary>
        public static ICertificateBuilder Create(string subjectName)
        {
            return new CertificateBuilder(subjectName);
        }

        /// <summary>
        /// Constructor of a Certificate builder.
        /// </summary>
        private CertificateBuilder(X500DistinguishedName subjectName)
            : base(subjectName)
        {
        }

        /// <summary>
        /// Constructor of a Certificate builder.
        /// </summary>
        private CertificateBuilder(string subjectName)
            : base(subjectName)
        {
        }

        /// <inheritdoc/>
        public override X509Certificate2 CreateForRSA()
        {
            CreateDefaults();

            if (m_rsaPublicKey != null &&
               (IssuerCAKeyCert == null || !IssuerCAKeyCert.HasPrivateKey))
            {
                throw new NotSupportedException("Cannot use a public key without an issuer certificate with a private key.");
            }

            RSA rsaKeyPair = null;
            RSA rsaPublicKey = m_rsaPublicKey;
            if (rsaPublicKey == null)
            {
                rsaKeyPair = RSA.Create(m_keySize == 0 ? X509Defaults.RSAKeySize : m_keySize);
                rsaPublicKey = rsaKeyPair;
            }

            RSASignaturePadding padding = RSASignaturePadding.Pkcs1;
            var request = new CertificateRequest(SubjectName, rsaPublicKey, HashAlgorithmName, padding);

            CreateX509Extensions(request, false);

            X509Certificate2 signedCert;
            byte[] serialNumber = [.. m_serialNumber.Reverse()];
            if (IssuerCAKeyCert != null)
            {
                using RSA rsaIssuerKey = IssuerCAKeyCert.GetRSAPrivateKey();
                signedCert = request.Create(
                    IssuerCAKeyCert.SubjectName,
                    X509SignatureGenerator.CreateForRSA(rsaIssuerKey, padding),
                    NotBefore,
                    NotAfter,
                    serialNumber
                    );
            }
            else
            {
                signedCert = request.Create(
                    SubjectName,
                    X509SignatureGenerator.CreateForRSA(rsaKeyPair, padding),
                    NotBefore,
                    NotAfter,
                    serialNumber
                    );
            }

            return (rsaKeyPair == null) ? signedCert : signedCert.CopyWithPrivateKey(rsaKeyPair);
        }

        /// <inheritdoc/>
        public override X509Certificate2 CreateForRSA(X509SignatureGenerator generator)
        {
            CreateDefaults();

            if (m_rsaPublicKey == null && IssuerCAKeyCert == null)
            {
                throw new NotSupportedException("Need an issuer certificate or a public key for a signature generator.");
            }

            X500DistinguishedName issuerSubjectName = SubjectName;
            if (IssuerCAKeyCert != null)
            {
                issuerSubjectName = IssuerCAKeyCert.SubjectName;
            }

            RSA rsaKeyPair = null;
            RSA rsaPublicKey = m_rsaPublicKey;
            if (rsaPublicKey == null)
            {
                rsaKeyPair = RSA.Create(m_keySize == 0 ? X509Defaults.RSAKeySize : m_keySize);
                rsaPublicKey = rsaKeyPair;
            }

            var request = new CertificateRequest(SubjectName, rsaPublicKey, HashAlgorithmName, RSASignaturePadding.Pkcs1);

            CreateX509Extensions(request, false);

            X509Certificate2 signedCert = request.Create(
                issuerSubjectName,
                generator,
                NotBefore,
                NotAfter,
                [.. m_serialNumber.Reverse()]
                );

            return (rsaKeyPair == null) ? signedCert : signedCert.CopyWithPrivateKey(rsaKeyPair);
        }

#if ECC_SUPPORT
        /// <inheritdoc/>
        public override X509Certificate2 CreateForECDsa()
        {
            if (m_ecdsaPublicKey != null && IssuerCAKeyCert == null)
            {
                throw new NotSupportedException("Cannot use a public key without an issuer certificate with a private key.");
            }

            if (m_ecdsaPublicKey == null && m_curve == null)
            {
                throw new NotSupportedException("Need a public key or a ECCurve to create the certificate.");
            }

            CreateDefaults();

            ECDsa key = null;
            ECDsa publicKey = m_ecdsaPublicKey;
            if (publicKey == null)
            {
                key = ECDsa.Create((ECCurve)m_curve);
                publicKey = key;
            }

            var request = new CertificateRequest(SubjectName, publicKey, HashAlgorithmName);

            CreateX509Extensions(request, true);

            byte[] serialNumber = [.. m_serialNumber.Reverse()];

            X509Certificate2 cert;
            if (IssuerCAKeyCert != null)
            {
                using ECDsa issuerKey = IssuerCAKeyCert.GetECDsaPrivateKey();
                cert = request.Create(
                    IssuerCAKeyCert.SubjectName,
                    X509SignatureGenerator.CreateForECDsa(issuerKey),
                    NotBefore,
                    NotAfter,
                    serialNumber
                    );
            }
            else
            {
                cert = request.Create(
                    SubjectName,
                    X509SignatureGenerator.CreateForECDsa(key),
                    NotBefore,
                    NotAfter,
                    serialNumber
                    );
            }

            return (key == null) ? cert : cert.CopyWithPrivateKey(key);
        }

        /// <inheritdoc/>
        public override X509Certificate2 CreateForECDsa(X509SignatureGenerator generator)
        {
            if (IssuerCAKeyCert == null)
            {
                throw new NotSupportedException("X509 Signature generator requires an issuer certificate.");
            }

            if (m_ecdsaPublicKey == null && m_curve == null)
            {
                throw new NotSupportedException("Need a public key or a ECCurve to create the certificate.");
            }

            CreateDefaults();

            ECDsa key = null;
            ECDsa publicKey = m_ecdsaPublicKey;
            if (publicKey == null)
            {
                key = ECDsa.Create((ECCurve)m_curve);
                publicKey = key;
            }

            var request = new CertificateRequest(SubjectName, publicKey, HashAlgorithmName);

            CreateX509Extensions(request, true);

            X509Certificate2 signedCert = request.Create(
                IssuerCAKeyCert.SubjectName,
                generator,
                NotBefore,
                NotAfter,
                [.. m_serialNumber.Reverse()]
                );

            // return a X509Certificate2
            return (key == null) ? signedCert : signedCert.CopyWithPrivateKey(key);
        }

        /// <inheritdoc/>
        public override ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(byte[] publicKey)
        {
            if (publicKey == null)
            {
                throw new ArgumentNullException(nameof(publicKey));
            }

            int bytes = 0;
            try
            {
                m_ecdsaPublicKey = ECDsa.Create();
#if NET472_OR_GREATER

                if (Org.BouncyCastle.Security.PublicKeyFactory.CreateKey(publicKey) is not Org.BouncyCastle.Crypto.Parameters.ECPublicKeyParameters asymmetricPubKeyParameters)
                {
                    throw new ArgumentException("Invalid public key format or key type.");
                }

                var asn1Obj = Asn1Object.FromByteArray(publicKey);
                var publicKeyInfo = SubjectPublicKeyInfo.GetInstance(asn1Obj);
                Asn1Encodable algParams = publicKeyInfo.Algorithm.Parameters;
                var x962Params = X962Parameters.GetInstance(algParams);

                var ecParameters = new ECParameters();

                Org.BouncyCastle.Crypto.Parameters.ECDomainParameters domainParameters = asymmetricPubKeyParameters.Parameters;
                Org.BouncyCastle.Math.EC.ECPoint q = asymmetricPubKeyParameters.Q;
                // calculate keySize round up (bitLength + 7) / 8
                int keySizeBytes = (domainParameters.N.BitLength + 7) / 8;

                if (x962Params.IsNamedCurve)
                {
                    // Named
                    var namedCurveOid = (DerObjectIdentifier)x962Params.Parameters;
                    string curveName = namedCurveOid.Id;
                    ecParameters.Curve = ECCurve.CreateFromOid(new Oid(curveName));
                }
                else
                {
                    // Explicit parameters
                    byte[] a = X509Utils.PadWithLeadingZeros(domainParameters.Curve.A.ToBigInteger().ToByteArrayUnsigned(), keySizeBytes);
                    byte[] b = X509Utils.PadWithLeadingZeros(domainParameters.Curve.B.ToBigInteger().ToByteArrayUnsigned(), keySizeBytes);
                    ecParameters.Curve = X509Utils.IdentifyEccCurveByCoefficients(a, b);
                }

                byte[] x = X509Utils.PadWithLeadingZeros(q.AffineXCoord.ToBigInteger().ToByteArrayUnsigned(), keySizeBytes);
                byte[] y = X509Utils.PadWithLeadingZeros(q.AffineYCoord.ToBigInteger().ToByteArrayUnsigned(), keySizeBytes);
                // Use the Q point
                ecParameters.Q = new ECPoint
                {
                    X = x,
                    Y = y
                };

                m_ecdsaPublicKey.ImportParameters(ecParameters);
                bytes = publicKey.Length;

#else
                m_ecdsaPublicKey.ImportSubjectPublicKeyInfo(publicKey, out bytes);
#endif
                SetECCurve(m_ecdsaPublicKey.ExportParameters(false).Curve);
            }
            catch (Exception e)
            {
                throw new ArgumentException("Failed to decode the public key.", e);
            }

            if (publicKey.Length != bytes)
            {
                throw new ArgumentException("Decoded the public key but extra bytes were found.");
            }
            return this;
#endif
        }

        /// <inheritdoc/>
        public override ICertificateBuilderCreateForRSAAny SetRSAPublicKey(byte[] publicKey)
        {
            if (publicKey == null)
            {
                throw new ArgumentNullException(nameof(publicKey));
            }

            int bytes = 0;
            try
            {
#if NET472_OR_GREATER
                m_rsaPublicKey = X509Utils.SetRSAPublicKey(publicKey);
                bytes = publicKey.Length;
#else
                m_rsaPublicKey = RSA.Create();
                m_rsaPublicKey.ImportSubjectPublicKeyInfo(publicKey, out bytes);
#endif
            }
            catch (Exception e)
            {
                throw new ArgumentException("Failed to decode the public key.", e);
            }

            if (publicKey.Length != bytes)
            {
                throw new ArgumentException("Decoded the public key but extra bytes were found.");
            }
            return this;
        }

        /// <summary>
        /// Create some defaults needed to build the certificate.
        /// </summary>
        private void CreateDefaults()
        {
            if (!m_presetSerial)
            {
                NewSerialNumber();
            }
            m_presetSerial = false;

            ValidateSettings();
        }

        /// <summary>
        /// Create the X509 extensions to build the certificate.
        /// </summary>
        /// <param name="request">A certificate request.</param>
        /// <param name="forECDsa">If the certificate is for ECDsa, not RSA.</param>
        private void CreateX509Extensions(CertificateRequest request, bool forECDsa)
        {
            // Basic Constraints
            if (m_extensions.FindExtension<X509BasicConstraintsExtension>() == null)
            {
                X509BasicConstraintsExtension bc = GetBasicContraints();
                request.CertificateExtensions.Add(bc);
            }

            // Subject Key Identifier
            var ski = new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                X509SubjectKeyIdentifierHashAlgorithm.Sha1,
                false);
            if (m_extensions.FindExtension<X509SubjectKeyIdentifierExtension>() == null)
            {
                request.CertificateExtensions.Add(ski);
            }

            // Authority Key Identifier
            if (m_extensions.FindExtension<X509AuthorityKeyIdentifierExtension>() == null)
            {
                X509Extension authorityKeyIdentifier = IssuerCAKeyCert != null
                    ? IssuerCAKeyCert.BuildAuthorityKeyIdentifier()
                    : new X509AuthorityKeyIdentifierExtension(
                        ski.SubjectKeyIdentifier.FromHexString(),
                        IssuerName,
                        m_serialNumber);
                request.CertificateExtensions.Add(authorityKeyIdentifier);
            }

            // Key usage extensions
            if (m_extensions.FindExtension<X509KeyUsageExtension>() == null)
            {
                X509KeyUsageFlags keyUsageFlags;
                if (m_isCA)
                {
                    keyUsageFlags = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign;
                }
                else
                {
                    if (forECDsa)
                    {
                        // Key Usage for ECDsa
                        keyUsageFlags = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation
                            | X509KeyUsageFlags.KeyAgreement;
                    }
                    else
                    {
                        // Key usage for RSA
                        keyUsageFlags = X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.KeyEncipherment
                            | X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation;
                    }
                    if (IssuerCAKeyCert == null)
                    {
                        // self signed case
                        keyUsageFlags |= X509KeyUsageFlags.KeyCertSign;
                    }
                }

                request.CertificateExtensions.Add(
                                    new X509KeyUsageExtension(
                                        keyUsageFlags,
                                        true));
            }

            if (!m_isCA && !forECDsa && m_extensions.FindExtension<X509EnhancedKeyUsageExtension>() == null)
            {
                // Enhanced key usage
                request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [
                            new Oid(Oids.ServerAuthentication),
                            new Oid(Oids.ClientAuthentication)
                    ], true));
            }

            foreach (X509Extension extension in m_extensions)
            {
                request.CertificateExtensions.Add(extension);
            }
        }

        /// <summary>
        /// Set the basic constraints for various cases.
        /// </summary>
        private X509BasicConstraintsExtension GetBasicContraints()
        {
            // Basic constraints
            if (!m_isCA && IssuerCAKeyCert == null)
            {
                // see Mantis https://mantis.opcfoundation.org/view.php?id=8370
                // self signed application certificates shall set the CA bit to false
                return new X509BasicConstraintsExtension(false, false, 0, true);
            }
            else if (m_isCA && m_pathLengthConstraint >= 0)
            {
                // CA with constraints
                return new X509BasicConstraintsExtension(true, true, m_pathLengthConstraint, true);
            }
            else
            {
                return new X509BasicConstraintsExtension(m_isCA, false, 0, true);
            }
        }
    }
}
#endif
