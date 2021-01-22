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
#if !NETSTANDARD2_1 && !NET472

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
using System.Collections.Generic;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Opc.Ua.Security.Certificates.BouncyCastle;
using System.Collections;
using Org.BouncyCastle.Pkcs;
using System.Diagnostics;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Builds a Certificate.
    /// </summary>
    public class CertificateBuilder : CertificateBuilderBase
    {
        #region Constructors
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
        /// Initialize a Certificate builder.
        /// </summary>
        private CertificateBuilder(X500DistinguishedName subjectName)
            : base(subjectName)
        {
        }

        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        private CertificateBuilder(string subjectName)
            : base(subjectName)
        {
        }
        #endregion

        #region Public Methods
        /// <inheritdoc/>
        public override X509Certificate2 CreateForRSA()
        {
            if (m_rsaPublicKey != null)
            {
                return CreateForRSAWithPublicKey();
            }
            else
            {
                string passcode = Guid.NewGuid().ToString();
                return X509PfxUtils.CreateCertificateFromPKCS12(CreatePfxForRSA(passcode), passcode);
            }
        }

        /// <inheritdoc/>
        public override X509Certificate2 CreateForRSA(X509SignatureGenerator generator)
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            ISignatureFactory signatureFactory = new X509SignatureFactory(HashAlgorithmName, generator);
            if (m_rsaPublicKey != null)
            {
                return CreateForRSAWithPublicKey(signatureFactory);
            }
            else
            {
                string passcode = Guid.NewGuid().ToString();
                return X509PfxUtils.CreateCertificateFromPKCS12(CreatePfxForRSA(passcode, signatureFactory), passcode);
            }
        }

        /// <inheritdoc/>
        public override ICertificateBuilderCreateForRSAAny SetRSAPublicKey(byte[] publicKey)
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
                throw new ArgumentException("Failed to decode and import the public key.", e);
            }
            return this;
        }

        /// <summary>
        /// Create a Pfx with a private key by combining 
        /// an existing X509Certificate2 and a RSA private key.
        /// </summary>
        public static byte[] CreatePfxWithRSAPrivateKey(
            X509Certificate2 certificate,
            string friendlyName,
            RSA privateKey,
            string passcode)
        {
            var x509 = new X509CertificateParser().ReadCertificate(certificate.RawData);
            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                return X509Utils.CreatePfxWithPrivateKey(
                    x509, friendlyName,
                    X509Utils.GetPrivateKeyParameter(privateKey),
                    passcode,
                    new SecureRandom(cfrg));
            }
        }

        /// <summary>
        /// Creates a certificate signing request from an
        /// existing certificate with a private key.
        /// </summary>
        public static byte[] CreateSigningRequest(
            X509Certificate2 certificate,
            IList<String> domainNames = null
            )
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                SecureRandom random = new SecureRandom(cfrg);

                // try to get signing/private key from certificate passed in
                AsymmetricKeyParameter signingKey = X509Utils.GetPrivateKeyParameter(certificate);
                RsaKeyParameters publicKey = X509Utils.GetPublicKeyParameter(certificate);

                ISignatureFactory signatureFactory =
                    new Asn1SignatureFactory(X509Utils.GetRSAHashAlgorithm(X509Defaults.HashAlgorithmName), signingKey, random);

                Asn1Set attributes = null;
                var san = X509Extensions.FindExtension<X509SubjectAltNameExtension>(certificate);
                X509SubjectAltNameExtension alternateName = new X509SubjectAltNameExtension(san, san.Critical);

                string applicationUri = null;
                domainNames = domainNames ?? new List<String>();
                if (alternateName != null)
                {
                    if (alternateName.Uris.Count > 0)
                    {
                        applicationUri = alternateName.Uris[0];
                    }
                    foreach (var name in alternateName.DomainNames)
                    {
                        if (!domainNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            domainNames.Add(name);
                        }
                    }
                    foreach (var ipAddress in alternateName.IPAddresses)
                    {
                        if (!domainNames.Any(s => s.Equals(ipAddress, StringComparison.OrdinalIgnoreCase)))
                        {
                            domainNames.Add(ipAddress);
                        }
                    }
                }

                // build CSR extensions
                var generalNames = new List<GeneralName>();

                if (applicationUri != null)
                {
                    generalNames.Add(new GeneralName(GeneralName.UniformResourceIdentifier, applicationUri));
                }

                if (domainNames.Count > 0)
                {
                    generalNames.AddRange(BouncyCastle.X509Extensions.CreateSubjectAlternateNameDomains(domainNames));
                }

                if (generalNames.Count > 0)
                {
                    IList oids = new ArrayList();
                    IList values = new ArrayList();
                    oids.Add(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName);
                    values.Add(new Org.BouncyCastle.Asn1.X509.X509Extension(false,
                        new DerOctetString(new GeneralNames(generalNames.ToArray()).GetDerEncoded())));
                    var attribute = new Org.BouncyCastle.Asn1.Pkcs.AttributePkcs(Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
                        new DerSet(new Org.BouncyCastle.Asn1.X509.X509Extensions(oids, values)));
                    attributes = new DerSet(attribute);
                }

                var pkcs10CertificationRequest = new Pkcs10CertificationRequest(
                    signatureFactory,
                    new CertificateFactoryX509Name(false, certificate.Subject),
                    publicKey,
                    attributes);

                return pkcs10CertificationRequest.GetEncoded();
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Create a new serial number and validate lifetime.
        /// </summary>
        /// <param name="random"></param>
        private void CreateDefaults(IRandomGenerator random = null)
        {
            if (!m_presetSerial)
            {
                NewSerialNumber(random);
            }
            m_presetSerial = false;

            ValidateSettings();
        }

        /// <summary>
        /// Set all mandatory fields.
        /// </summary>
        /// <param name="cg">The cert generator</param>
        private void CreateMandatoryFields(X509V3CertificateGenerator cg)
        {
            m_subjectDN = new CertificateFactoryX509Name(SubjectName.Name);
            // subject and issuer DN
            m_issuerDN = null;
            if (IssuerCAKeyCert != null)
            {
                m_issuerDN = new CertificateFactoryX509Name(IssuerCAKeyCert.Subject);
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
            cg.SetSerialNumber(new BigInteger(1, m_serialNumber.Reverse().ToArray()));
        }

        /// <summary>
        /// Create the extensions.
        /// </summary>
        /// <param name="cg">The cert generator.</param>
        /// <param name="subjectPublicKey">The public key to use for the extensions.</param>
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
            else if (!m_isCA && IssuerCAKeyCert == null)
            {   // self-signed
                basicConstraints = new BasicConstraints(0);
            }
            cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.BasicConstraints.Id, true, basicConstraints);

            // Authority Key identifier references the issuer cert or itself when self signed
            AsymmetricKeyParameter issuerPublicKey;
            BigInteger issuerSerialNumber;
            if (IssuerCAKeyCert != null)
            {
                issuerPublicKey = X509Utils.GetPublicKeyParameter(IssuerCAKeyCert);
                issuerSerialNumber = X509Utils.GetSerialNumber(IssuerCAKeyCert);
            }
            else
            {
                issuerPublicKey = subjectPublicKey;
                issuerSerialNumber = new BigInteger(1, m_serialNumber.Reverse().ToArray());
            }

            cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.AuthorityKeyIdentifier.Id, false,
                new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(issuerPublicKey),
                    new GeneralNames(new GeneralName(m_issuerDN)), issuerSerialNumber));

            if (!m_isCA)
            {
                // Key usage 
                var keyUsage = KeyUsage.DataEncipherment | KeyUsage.DigitalSignature |
                        KeyUsage.NonRepudiation | KeyUsage.KeyEncipherment;
                if (IssuerCAKeyCert == null)
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
        /// Create the RSA certificate with a given public key.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        private X509Certificate2 CreateForRSAWithPublicKey(ISignatureFactory signatureFactory = null)
        {
            // Cases locked out by API flow
            Debug.Assert(m_rsaPublicKey != null, "Need a public key for the certificate.");
            Debug.Assert(IssuerCAKeyCert != null, "Need a issuer certificate to sign.");
            if (!IssuerCAKeyCert.HasPrivateKey && signatureFactory == null)
            {
                throw new NotSupportedException("Need an issuer certificate with a private key or a signature generator.");
            }

            // cert generators
            CreateDefaults();

            var cg = new X509V3CertificateGenerator();
            CreateMandatoryFields(cg);

            // set public key
            AsymmetricKeyParameter subjectPublicKey = X509Utils.GetPublicKeyParameter(m_rsaPublicKey);
            cg.SetPublicKey(subjectPublicKey);

            CreateExtensions(cg, subjectPublicKey);

            // sign certificate by issuer
            if (signatureFactory == null)
            {
                AsymmetricKeyParameter signingKey = X509Utils.GetPrivateKeyParameter(IssuerCAKeyCert);
                signatureFactory = new Asn1SignatureFactory(X509Utils.GetRSAHashAlgorithm(HashAlgorithmName), signingKey);
            }
            Org.BouncyCastle.X509.X509Certificate x509 = cg.Generate(signatureFactory);

            // create the signed cert
            return new X509Certificate2(x509.GetEncoded());
        }

        /// <summary>
        /// Create the RSA certificate as Pfx byte array with a private key.
        /// </summary>
        /// <returns>
        /// Returns the Pfx with certificate and private key.
        /// </returns>
        private byte[] CreatePfxForRSA(string passcode, ISignatureFactory signatureFactory = null)
        {
            // Cases locked out by API flow
            Debug.Assert(m_rsaPublicKey == null, "A public key is not supported for the certificate.");

            if (signatureFactory != null && IssuerCAKeyCert == null)
            {
                throw new NotSupportedException("Need an issuer certificate for a signature generator.");
            }

            if (IssuerCAKeyCert != null &&
                (!IssuerCAKeyCert.HasPrivateKey && signatureFactory == null))
            {
                throw new NotSupportedException("Need an issuer certificate with a private key or a signature generator.");
            }

            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                // cert generators
                SecureRandom random = new SecureRandom(cfrg);

                CreateDefaults(cfrg);

                X509V3CertificateGenerator cg = new X509V3CertificateGenerator();
                CreateMandatoryFields(cg);

                // create Private/Public Keypair
                AsymmetricKeyParameter subjectPublicKey = null;
                AsymmetricKeyParameter subjectPrivateKey = null;
                using (var rsa = new RSACryptoServiceProvider(m_keySize == 0 ? X509Defaults.RSAKeySize : m_keySize))
                {
                    subjectPublicKey = X509Utils.GetPublicKeyParameter(rsa);
                    subjectPrivateKey = X509Utils.GetPrivateKeyParameter(rsa);
                }

                cg.SetPublicKey(subjectPublicKey);
                CreateExtensions(cg, subjectPublicKey);

                // sign certificate
                if (signatureFactory == null)
                {
                    AsymmetricKeyParameter signingKey;
                    if (IssuerCAKeyCert != null)
                    {
                        // signed by issuer
                        signingKey = X509Utils.GetPrivateKeyParameter(IssuerCAKeyCert);
                    }
                    else
                    {
                        // self signed
                        signingKey = subjectPrivateKey;
                    }
                    signatureFactory = new Asn1SignatureFactory(
                        X509Utils.GetRSAHashAlgorithm(HashAlgorithmName), signingKey, random);
                }
                Org.BouncyCastle.X509.X509Certificate x509 = cg.Generate(signatureFactory);

                // note: this Pfx has a private key!
                return X509Utils.CreatePfxWithPrivateKey(x509, null, subjectPrivateKey, passcode, random);
            }
        }

        /// <summary>
        /// Create a new random serial number.
        /// </summary>
        private void NewSerialNumber(IRandomGenerator random)
        {
            if (random == null)
            {
                NewSerialNumber();
            }
            else
            {
                m_serialNumber = new byte[m_serialNumberLength];
                random.NextBytes(m_serialNumber);
                m_serialNumber[m_serialNumberLength - 1] &= 0x7f;
            }
        }
        #endregion

        #region Private Fields
        private X509Name m_issuerDN;
        private X509Name m_subjectDN;
        #endregion
    }
}
#endif
