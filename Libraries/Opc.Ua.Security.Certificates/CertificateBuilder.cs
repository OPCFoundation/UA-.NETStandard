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
#if NETSTANDARD2_1

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
using System.Collections.Generic;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Builds a Certificate.
    /// </summary>
    public class CertificateBuilder : IX509Certificate
    {
        #region Constructors
        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        public CertificateBuilder(X500DistinguishedName subjectName)
            : this()
        {
            m_issuerName = m_subjectName = subjectName;
        }

        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        public CertificateBuilder(string subjectName)
            : this()
        {
            m_issuerName = m_subjectName = new X500DistinguishedName(subjectName);
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        private CertificateBuilder()
        {
            m_rsaPublicKey = null;
            m_keySize = 0;
            m_ecdsaPublicKey = null;
            m_curve = null;
            m_notBefore = DateTime.UtcNow.AddDays(-1).Date;
            m_notAfter = NotBefore.AddMonths(Defaults.LifeTime);
            m_hashAlgorithmName = Defaults.HashAlgorithmName;
            m_serialNumberLength = Defaults.SerialNumberLengthMin;
            m_extensions = new List<X509Extension>();
        }
        #endregion

        #region ICertificate Interface
        /// <inheritdoc/>
        public X500DistinguishedName SubjectName => m_subjectName;

        /// <inheritdoc/>
        public X500DistinguishedName IssuerName => m_issuerName;

        /// <inheritdoc/>
        public DateTime NotBefore => m_notBefore;

        /// <inheritdoc/>
        public DateTime NotAfter => m_notAfter;

        /// <inheritdoc/>
        public string SerialNumber => m_serialNumber.ToHexString(true);

        /// <inheritdoc/>
        public byte[] GetSerialNumber() { return m_serialNumber; }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName => m_hashAlgorithmName;

        /// <inheritdoc/>
        public IReadOnlyList<X509Extension> Extensions => m_extensions.AsReadOnly();

        /// <inheritdoc/>
        public bool HasPublicKey => m_rsaPublicKey != null;
        #endregion

        #region Public Methods
        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public X509Certificate2 CreateForRSA()
        {

            CreateDefaults();

            if (m_rsaPublicKey != null &&
               (m_issuerCAKeyCert == null || !m_issuerCAKeyCert.HasPrivateKey))
            {
                throw new NotSupportedException("Cannot use a public key without a issuer certificate with a private key.");
            }

            RSA rsaKeyPair = null;
            RSA rsaPublicKey = m_rsaPublicKey;
            if (rsaPublicKey == null)
            {
                rsaKeyPair = RSA.Create(m_keySize == 0 ? Defaults.RSAKeySize : m_keySize);
                rsaPublicKey = rsaKeyPair;
            }

            var padding = RSASignaturePadding.Pkcs1;
            var request = new CertificateRequest(SubjectName, rsaPublicKey, HashAlgorithmName, padding);

            CreateExtensions(request);

            X509Certificate2 signedCert;
            if (m_issuerCAKeyCert != null)
            {
                var issuerSubjectName = m_issuerCAKeyCert.SubjectName;
                using (RSA rsaIssuerKey = m_issuerCAKeyCert.GetRSAPrivateKey())
                {
                    signedCert = request.Create(
                        m_issuerCAKeyCert.SubjectName,
                        X509SignatureGenerator.CreateForRSA(rsaIssuerKey, padding),
                        NotBefore,
                        NotAfter,
                        m_serialNumber
                        );
                }
            }
            else
            {
                signedCert = request.Create(
                    SubjectName,
                    X509SignatureGenerator.CreateForRSA(rsaKeyPair, padding),
                    NotBefore,
                    NotAfter,
                    m_serialNumber
                    );
            }

            return (rsaKeyPair == null) ? signedCert : signedCert.CopyWithPrivateKey(rsaKeyPair);
        }

        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public X509Certificate2 CreateForRSA(X509SignatureGenerator generator)
        {

            CreateDefaults();

            if (m_issuerCAKeyCert == null)
            {
                throw new NotSupportedException("X509 Signature generator requires an issuer certificate.");
            }

            RSA rsaKeyPair = null;
            RSA rsaPublicKey = m_rsaPublicKey;
            if (rsaPublicKey == null)
            {
                rsaKeyPair = RSA.Create(m_keySize == 0 ? Defaults.RSAKeySize : m_keySize);
                rsaPublicKey = rsaKeyPair;
            }

            var request = new CertificateRequest(SubjectName, rsaPublicKey, HashAlgorithmName, RSASignaturePadding.Pkcs1);

            CreateExtensions(request);

            X509Certificate2 signedCert;

            var issuerSubjectName = m_issuerCAKeyCert.SubjectName;
            signedCert = request.Create(
                m_issuerCAKeyCert.SubjectName,
                generator,
                m_notBefore,
                m_notAfter,
                m_serialNumber
                );

            return (rsaKeyPair == null) ? signedCert : signedCert.CopyWithPrivateKey(rsaKeyPair);
        }

        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public X509Certificate2 CreateForECDsa()
        {
            if (m_ecdsaPublicKey != null && m_issuerCAKeyCert == null)
            {
                throw new NotSupportedException("Cannot use a public key without a issuer certificate with a private key.");
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

            CreateExtensions(request);

            X509Certificate2 signedCert;
            if (m_issuerCAKeyCert != null)
            {
                using (ECDsa issuerKey = m_issuerCAKeyCert.GetECDsaPrivateKey())
                {
                    signedCert = request.Create(
                        m_issuerCAKeyCert.SubjectName,
                        X509SignatureGenerator.CreateForECDsa(issuerKey),
                        m_notBefore,
                        m_notAfter,
                        m_serialNumber
                        );
                }
            }
            else
            {
                signedCert = request.Create(
                    SubjectName,
                    X509SignatureGenerator.CreateForECDsa(key),
                    m_notBefore,
                    m_notAfter,
                    m_serialNumber
                    );
            }

            // return a X509Certificate2
            return (key == null) ? signedCert : signedCert.CopyWithPrivateKey(key);
        }

        /// <summary>
        /// Create the ECC certificate with signature using an external generator.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public X509Certificate2 CreateForECDsa(X509SignatureGenerator generator, ECCurve curve)
        {
            if (m_issuerCAKeyCert == null)
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

            CreateExtensions(request);

            X509Certificate2 signedCert = request.Create(
                m_issuerCAKeyCert.SubjectName,
                generator,
                m_notBefore,
                m_notAfter,
                m_serialNumber
                );

            // return a X509Certificate2
            return (key == null) ? signedCert : signedCert.CopyWithPrivateKey(key);
        }

        public CertificateBuilder SetSerialNumberLength(int length)
        {
            if (length > Defaults.SerialNumberLengthMax || length == 0)
            {
                throw new ArgumentOutOfRangeException("SerialNumber length out of Range");
            }
            m_serialNumberLength = length;
            m_presetSerial = false;
            return this;
        }

        public CertificateBuilder SetSerialNumber(byte[] serialNumber)
        {
            if (serialNumber.Length > Defaults.SerialNumberLengthMax ||
                serialNumber.Length == 0)
            {
                throw new ArgumentOutOfRangeException("SerialNumber length out of Range");
            }
            serialNumber[0] &= 0x7f;
            m_serialNumber = serialNumber;
            m_serialNumberLength = serialNumber.Length;
            m_presetSerial = true;
            return this;
        }

        public CertificateBuilder CreateSerialNumber()
        {
            NewSerialNumber();
            m_presetSerial = true;
            return this;
        }

        public CertificateBuilder SetNotBefore(DateTime notBefore)
        {
            m_notBefore = notBefore;
            return this;
        }

        public CertificateBuilder SetNotAfter(DateTime notAfter)
        {
            m_notAfter = notAfter;
            return this;
        }

        public CertificateBuilder SetHashAlgorithm(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == null) throw new ArgumentNullException(nameof(hashAlgorithmName));
            m_hashAlgorithmName = hashAlgorithmName;
            return this;
        }

        public CertificateBuilder SetCAConstraint(int pathLengthConstraint = -1)
        {
            m_isCA = true;
            m_pathLengthConstraint = pathLengthConstraint;
            m_serialNumberLength = Defaults.SerialNumberLengthMax;
            return this;
        }

        public CertificateBuilder SetECDsaPublicKey(byte[] publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            int bytes;
            try
            {
                m_ecdsaPublicKey = ECDsa.Create();
                m_ecdsaPublicKey.ImportSubjectPublicKeyInfo(publicKey, out bytes);
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

        public CertificateBuilder SetECDsaPublicKey(ECDsa publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_ecdsaPublicKey = publicKey;
            return this;
        }

        public CertificateBuilder SetRSAPublicKey(byte[] publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            int bytes;
            try
            {
                m_rsaPublicKey = RSA.Create();
                m_rsaPublicKey.ImportSubjectPublicKeyInfo(publicKey, out bytes);
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

        public CertificateBuilder SetRSAPublicKey(RSA publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_rsaPublicKey = publicKey;
            return this;
        }

        public CertificateBuilder SetRSAKeySize(int keySize)
        {
            if (keySize % 1024 != 0 || keySize < Defaults.RSAKeySizeMin || keySize > Defaults.RSAKeySizeMax)
            {
                throw new ArgumentException(nameof(keySize), "KeySize must be a multiple of 1024 or is not in the allowed range.");
            }

            m_keySize = keySize;
            return this;
        }

        public CertificateBuilder SetECCurve(ECCurve curve)
        {
            m_curve = curve;
            return this;
        }

        public CertificateBuilder SetIssuer(X509Certificate2 issuerCertificate)
        {
            if (issuerCertificate == null) throw new ArgumentNullException(nameof(issuerCertificate));
            m_issuerCAKeyCert = issuerCertificate;
            // the issuer may have a different key algorithm, enforce to create a new public key
            m_rsaPublicKey = null;
            return this;
        }

        public CertificateBuilder AddExtension(X509Extension extension)
        {
            m_extensions.Add(extension);
            return this;
        }
        #endregion

        #region Private Methods
        private void CreateDefaults()
        {
            if (!m_presetSerial)
            {
                NewSerialNumber();
            }
            m_presetSerial = false;

            // lifetime must be in range of issuer
            if (m_issuerCAKeyCert != null)
            {
                if (NotAfter > m_issuerCAKeyCert.NotAfter)
                {
                    m_notAfter = m_issuerCAKeyCert.NotAfter;
                }
                if (NotBefore < m_issuerCAKeyCert.NotBefore)
                {
                    m_notBefore = m_issuerCAKeyCert.NotBefore;
                }
            }

        }

        private void CreateExtensions(CertificateRequest request)
        {

            // Basic Constraints
            X509BasicConstraintsExtension bc = GetBasicContraints();
            request.CertificateExtensions.Add(bc);

            // Subject Key Identifier
            var ski = new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                X509SubjectKeyIdentifierHashAlgorithm.Sha1,
                false);
            request.CertificateExtensions.Add(ski);

            // Authority Key Identifier
            X509Extension authorityKeyIdentifier = m_issuerCAKeyCert != null
                ? X509Extensions.BuildAuthorityKeyIdentifier(m_issuerCAKeyCert)
                : new X509AuthorityKeyIdentifierExtension(
                    ski.SubjectKeyIdentifier.FromHexString(),
                    m_subjectName,
                    m_serialNumber.Reverse().ToArray()
                    );
            request.CertificateExtensions.Add(authorityKeyIdentifier);

            X509KeyUsageFlags keyUsageFlags;
            if (m_isCA)
            {
                keyUsageFlags = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign;
            }
            else
            {
                // Key Usage
                keyUsageFlags =
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.DataEncipherment |
                    X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyEncipherment;
                if (m_issuerCAKeyCert == null)
                {
                    // self signed case
                    keyUsageFlags |= X509KeyUsageFlags.KeyCertSign;
                }
            }

            request.CertificateExtensions.Add(
                                new X509KeyUsageExtension(
                                    keyUsageFlags,
                                    true));

            if (!m_isCA)
            {
                // Enhanced key usage 
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection {
                            new Oid(Oids.ServerAuthentication),
                            new Oid(Oids.ClientAuthentication)
                        }, true));
            }

            foreach (var extension in m_extensions)
            {
                request.CertificateExtensions.Add(extension);
            }
        }

        private X509BasicConstraintsExtension GetBasicContraints()
        {
            // Basic constraints
            if (!m_isCA && m_issuerCAKeyCert == null)
            {
                // self signed
                return new X509BasicConstraintsExtension(true, true, 0, true);
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

        /// <summary>
        /// Create a new random serial number.
        /// </summary>
        private void NewSerialNumber()
        {
            // new serial number
            m_serialNumber = new byte[m_serialNumberLength];
            RandomNumberGenerator.Fill(m_serialNumber);
            m_serialNumber[0] &= 0x7F;
        }
        #endregion

        #region Private Fields
        private List<X509Extension> m_extensions;
        private bool m_isCA;
        private int m_pathLengthConstraint;
        private int m_serialNumberLength;
        private bool m_presetSerial;
        private RSA m_rsaPublicKey;
        private int m_keySize;
        private ECDsa m_ecdsaPublicKey;
        private ECCurve? m_curve;
        private X509Certificate2 m_issuerCAKeyCert;
        private DateTime m_notBefore;
        private DateTime m_notAfter;
        private byte[] m_serialNumber;
        private HashAlgorithmName m_hashAlgorithmName;
        private X500DistinguishedName m_subjectName;
        private X500DistinguishedName m_issuerName;
        #endregion
    }
}
#endif
