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
#if !NETSTANDARD2_1

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
using System.Collections.Generic;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Opc.Ua.Security.Certificates.BouncyCastle;
using X509Extension = System.Security.Cryptography.X509Certificates.X509Extension;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Builds a Certificate.
    /// </summary>
    public class CertificateBuilder
    {
        #region Constructors
        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        public CertificateBuilder(X500DistinguishedName subjectName)
            : this()
        {
            m_subjectName = subjectName;
        }

        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        public CertificateBuilder(string subjectName)
            : this()
        {
            m_subjectName = new X500DistinguishedName(subjectName);
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        private CertificateBuilder()
        {
            m_notBefore = DateTime.UtcNow.AddDays(-1).Date;
            m_notAfter = NotBefore.AddMonths(Defaults.LifeTime);
            m_hashAlgorithmName = Defaults.HashAlgorithmName;
            m_serialNumberLength = Defaults.SerialNumberLengthMin;
            m_extensions = new List<X509Extension>();
        }
        #endregion

        #region ICertificate Interface
        /// <inheritdoc/>
        public X500DistinguishedName SubjectName
        {
            get
            {
                return m_subjectName;
            }
        }

        /// <inheritdoc/>
        public string Subject => SubjectName.Name;

        /// <inheritdoc/>
        public X500DistinguishedName IssuerName
        {
            get
            {
                return m_issuerName;
            }
        }

        /// <inheritdoc/>
        public string Issuer => IssuerName.Name;

        /// <inheritdoc/>
        public DateTime NotBefore
        {
            get
            {
                return m_notBefore;
            }
        }

        /// <inheritdoc/>
        public DateTime NotAfter
        {
            get
            {
                return m_notAfter;
            }
        }

        /// <inheritdoc/>
        public byte[] SerialNumber
        {
            get
            {
                return m_serialNumber;
            }
        }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName
        {
            get
            {
                return m_hashAlgorithmName;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<X509Extension> Extensions => m_extensions.AsReadOnly();
        #endregion

        #region Public Methods
        /// <summary>
        /// Sign the RSA certificate with public key.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public X509Certificate2 SignForRSA()
        {
            if (m_rsaPublicKey == null)
            {
                throw new CryptographicException("Need a public key for ths signed certificate.");
            }

            if (m_issuerCAKeyCert == null || !m_issuerCAKeyCert.HasPrivateKey)
            {
                throw new NotSupportedException("Cannot sign a public key without a issuer certificate with a private key.");
            }

            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                // cert generators
                CreateDefaults(cfrg);

                X509V3CertificateGenerator cg = new X509V3CertificateGenerator();
                CreateMandatoryFields(cg);

                // set public key
                AsymmetricKeyParameter subjectPublicKey = GetPublicKeyParameter(m_rsaPublicKey);
                cg.SetPublicKey(subjectPublicKey);

                CreateExtensions(cg, subjectPublicKey);

                // sign certificate by issuer
                AsymmetricKeyParameter signingKey = X509Utils.GetPrivateKeyParameter(m_issuerCAKeyCert);

                SecureRandom random = new SecureRandom(cfrg);
                ISignatureFactory signatureFactory =
                            new Asn1SignatureFactory(X509Utils.GetRSAHashAlgorithm(HashAlgorithmName), signingKey, random);
                Org.BouncyCastle.X509.X509Certificate x509 = cg.Generate(signatureFactory);

                // create the signed cert
                return new X509Certificate2(x509.GetEncoded());
            }
        }

        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The Pfx with certificate and private key.</returns>
        public byte[] CreatePfxForRSA(string passcode, int keySize = 0)
        {
            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                // cert generators
                SecureRandom random = new SecureRandom(cfrg);

                CreateDefaults(cfrg);

                if (m_rsaPublicKey != null &&
                   (m_issuerCAKeyCert == null || !m_issuerCAKeyCert.HasPrivateKey))
                {
                    throw new NotSupportedException("Cannot use a public key without a issuer certificate with a private key.");
                }

                X509V3CertificateGenerator cg = new X509V3CertificateGenerator();
                CreateMandatoryFields(cg);

                // set Private/Public Key
                AsymmetricKeyParameter subjectPublicKey;
                AsymmetricKeyParameter subjectPrivateKey;
                if (m_rsaPublicKey == null)
                {
                    var keyGenerationParameters = new KeyGenerationParameters(random, keySize);
                    var keyPairGenerator = new RsaKeyPairGenerator();
                    keyPairGenerator.Init(keyGenerationParameters);
                    AsymmetricCipherKeyPair subjectKeyPair = keyPairGenerator.GenerateKeyPair();
                    subjectPublicKey = subjectKeyPair.Public;
                    subjectPrivateKey = subjectKeyPair.Private;
                }
                else
                {
                    // special case, if a cert is signed by CA, the private key of the cert is not needed
                    subjectPublicKey = GetPublicKeyParameter(m_rsaPublicKey);
                    //PublicKeyFactory.CreateKey(publicKey);
                    subjectPrivateKey = null;
                }
                cg.SetPublicKey(subjectPublicKey);

                CreateExtensions(cg, subjectPublicKey);

                // sign certificate
                AsymmetricKeyParameter signingKey;
                if (m_issuerCAKeyCert != null)
                {
                    // signed by issuer
                    signingKey = X509Utils.GetPrivateKeyParameter(m_issuerCAKeyCert);
                }
                else
                {
                    // self signed
                    signingKey = subjectPrivateKey;
                }
                ISignatureFactory signatureFactory =
                            new Asn1SignatureFactory(X509Utils.GetRSAHashAlgorithm(HashAlgorithmName), signingKey, random);
                Org.BouncyCastle.X509.X509Certificate x509 = cg.Generate(signatureFactory);

                // convert to X509Certificate2
                byte[] certificate;
                if (subjectPrivateKey == null)
                {
                    // create the cert without the private key
                    certificate = x509.GetEncoded();//new X509Certificate2(x509.GetEncoded());
                }
                else
                {
                    // note: this cert has a private key!
                    certificate = X509Utils.CreatePfxWithPrivateKey(x509, null, subjectPrivateKey, passcode, random);
                }

                return certificate;
            }
        }

        public bool HasPublicKey => m_rsaPublicKey != null;

        public CertificateBuilder SetSerialNumberLength(int length)
        {
            if (length > Defaults.SerialNumberLengthMax || length == 0)
            {
                throw new ArgumentOutOfRangeException("SerialNumber length out of Range");
            }
            m_serialNumberLength = length;
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

        public CertificateBuilder SetRSAPublicKey(byte[] publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            try
            {
                var asymmetricKeyParameter = PublicKeyFactory.CreateKey(publicKey);
                var rsaKeyParameters = asymmetricKeyParameter as RsaKeyParameters;
                var parameters = new RSAParameters {
                    Exponent = rsaKeyParameters.Exponent.ToByteArrayUnsigned(),
                    Modulus = rsaKeyParameters.Modulus.ToByteArrayUnsigned()
                };
                RSA rsaPublicKey = RSA.Create();
                rsaPublicKey.ImportParameters(parameters);
                m_rsaPublicKey = rsaPublicKey;
            }
            catch (Exception e)
            {
                m_rsaPublicKey = null;
                throw new ArgumentException("Failed to decode and import the public key.", e);
            }
            return this;
        }

        public CertificateBuilder SetRSAPublicKey(RSA publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_rsaPublicKey = publicKey;
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
        /// <summary>
        /// Create a new serial number and validate lifetime.
        /// </summary>
        /// <param name="random"></param>
        private void CreateDefaults(IRandomGenerator random)
        {
            // recreate serial number for a new cert
            m_serialNumber = new byte[m_serialNumberLength];
            random.NextBytes(SerialNumber);
            SerialNumber[0] &= 0x7f;

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

        /// <summary>
        /// Set all mandatory fields.
        /// </summary>
        /// <param name="cg">The cert generator</param>
        private void CreateMandatoryFields(X509V3CertificateGenerator cg)
        {
            m_subjectDN = new CertificateFactoryX509Name(m_subjectName.Name);
            // subject and issuer DN
            m_issuerDN = null;
            if (m_issuerCAKeyCert != null)
            {
                m_issuerDN = new CertificateFactoryX509Name(m_issuerCAKeyCert.Subject);
            }
            else
            {
                // self signed 
                m_issuerDN = m_subjectDN;
            }
            cg.SetIssuerDN(m_issuerDN);
            cg.SetSubjectDN(m_subjectDN);

            // valid for
            cg.SetNotBefore(NotBefore);
            cg.SetNotAfter(NotAfter);

            // serial number
            cg.SetSerialNumber(new BigInteger(1, SerialNumber.Reverse().ToArray()));
        }

        /// <summary>
        /// Create the extensions.
        /// </summary>
        /// <param name="cg"></param>
        /// <param name="subjectPublicKey"></param>
        private void CreateExtensions(X509V3CertificateGenerator cg, AsymmetricKeyParameter subjectPublicKey)
        {
            // Subject key identifier
            cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectKeyIdentifier.Id, false,
                new SubjectKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subjectPublicKey)));

            // Basic constraints
            BasicConstraints basicConstraints = new BasicConstraints(m_isCA);
            if (m_isCA && m_pathLengthConstraint >= 0)
            {
                basicConstraints = new BasicConstraints(m_pathLengthConstraint);
            }
            else if (!m_isCA && m_issuerCAKeyCert == null)
            {   // self-signed
                basicConstraints = new BasicConstraints(0);
            }
            cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.BasicConstraints.Id, true, basicConstraints);

            // Authority Key identifier references the issuer cert or itself when self signed
            AsymmetricKeyParameter issuerPublicKey;
            BigInteger issuerSerialNumber;
            if (m_issuerCAKeyCert != null)
            {
                issuerPublicKey = X509Utils.GetPublicKeyParameter(m_issuerCAKeyCert);
                issuerSerialNumber = X509Utils.GetSerialNumber(m_issuerCAKeyCert);
            }
            else
            {
                issuerPublicKey = subjectPublicKey;
                issuerSerialNumber = new BigInteger(1, SerialNumber.Reverse().ToArray());
            }

            cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.AuthorityKeyIdentifier.Id, false,
                new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(issuerPublicKey),
                    new GeneralNames(new GeneralName(m_issuerDN)), issuerSerialNumber));

            if (!m_isCA)
            {
                // Key usage 
                var keyUsage = KeyUsage.DataEncipherment | KeyUsage.DigitalSignature |
                        KeyUsage.NonRepudiation | KeyUsage.KeyEncipherment;
                if (m_issuerCAKeyCert == null)
                {   // only self signed certs need KeyCertSign flag.
                    keyUsage |= KeyUsage.KeyCertSign;
                }
                cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.KeyUsage, true,
                    new KeyUsage(keyUsage));

                // Extended Key usage
                cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.ExtendedKeyUsage, true,
                    new ExtendedKeyUsage(new List<DerObjectIdentifier>() {
                    new DerObjectIdentifier(Oids.ServerAuthentication), // server auth
                    new DerObjectIdentifier(Oids.ClientAuthentication), // client auth
                    }));
            }
            else
            {
                // Key usage CA
                cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.KeyUsage, true,
                    new KeyUsage(KeyUsage.CrlSign | KeyUsage.DigitalSignature | KeyUsage.KeyCertSign));
            }

            foreach (var extension in m_extensions)
            {
                cg.AddExtension(extension.Oid.Value, extension.Critical, Asn1Object.FromByteArray(extension.RawData));
            }

        }

        /// <summary>
        /// Get public key parameters from a RSA
        /// </summary>
        private static RsaKeyParameters GetPublicKeyParameter(RSA rsa)
        {
            RSAParameters rsaParams = rsa.ExportParameters(false);
            return new RsaKeyParameters(
                false,
                new BigInteger(1, rsaParams.Modulus),
                new BigInteger(1, rsaParams.Exponent));
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
        #endregion

        #region Private Fields
        private List<X509Extension> m_extensions;
        private bool m_isCA;
        private int m_pathLengthConstraint;
        private int m_serialNumberLength;
        private RSA m_rsaPublicKey;
        private X509Certificate2 m_issuerCAKeyCert;
        private X509Name m_issuerDN;
        private X509Name m_subjectDN;
        private DateTime m_notBefore;
        private DateTime m_notAfter;
        private byte[] m_serialNumber;
        private HashAlgorithmName m_hashAlgorithmName;
        private readonly X500DistinguishedName m_subjectName;
        private readonly X500DistinguishedName m_issuerName;
        #endregion
    }
}
#endif
