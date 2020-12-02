/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/


using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Security.Certificates;



namespace Opc.Ua
{
    /// <summary>
    /// Creates certificates.
    /// </summary>
    public static class CertificateFactory
    {
        #region Public Constants
        /// <summary>
        /// The default key size for RSA certificates in bits.
        /// </summary>
        /// <remarks>
        /// Supported values are 1024(deprecated), 2048, 3072 or 4096.
        /// </remarks>
        public static readonly ushort DefaultKeySize = 2048;
        /// <summary>
        /// The default hash size for RSA certificates in bits.
        /// </summary>
        /// <remarks>
        /// Supported values are 160 for SHA-1(deprecated) or 256, 384 and 512 for SHA-2.
        /// </remarks>
        public static readonly ushort DefaultHashSize = 256;
        /// <summary>
        /// The default lifetime of certificates in months.
        /// </summary>
        public static readonly ushort DefaultLifeTime = 12;
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a certificate from a buffer with DER encoded certificate.
        /// </summary>
        /// <param name="encodedData">The encoded data.</param>
        /// <param name="useCache">if set to <c>true</c> the copy of the certificate in the cache is used.</param>
        /// <returns>The certificate.</returns>
        public static X509Certificate2 Create(byte[] encodedData, bool useCache)
        {
            if (useCache)
            {
                return Load(new X509Certificate2(encodedData), false);
            }
            return new X509Certificate2(encodedData);
        }

        /// <summary>
        /// Loads the cached version of a certificate.
        /// </summary>
        /// <param name="certificate">The certificate to load.</param>
        /// <param name="ensurePrivateKeyAccessible">If true a key container is created for a certificate that must be deleted by calling Cleanup.</param>
        /// <returns>The cached certificate.</returns>
        /// <remarks>
        /// This function is necessary because all private keys used for cryptography 
        /// operations must be in a key container. 
        /// Private keys stored in a PFX file have no key container by default.
        /// </remarks>
        public static X509Certificate2 Load(X509Certificate2 certificate, bool ensurePrivateKeyAccessible)
        {
            if (certificate == null)
            {
                return null;
            }

            lock (m_certificatesLock)
            {
                X509Certificate2 cachedCertificate = null;

                // check for existing cached certificate.
                if (m_certificates.TryGetValue(certificate.Thumbprint, out cachedCertificate))
                {
                    return cachedCertificate;
                }

                // nothing more to do if no private key or dont care about accessibility.
                if (!certificate.HasPrivateKey || !ensurePrivateKeyAccessible)
                {
                    return certificate;
                }

                // update the cache.
                m_certificates[certificate.Thumbprint] = certificate;

                if (m_certificates.Count > 100)
                {
                    Utils.Trace("WARNING - Process certificate cache has {0} certificates in it.", m_certificates.Count);
                }

            }
            return certificate;
        }

        /// <summary>
        /// Create a certificate for any use.
        /// </summary>
        /// <param name="subjectName">The subject of the certificate</param>
        /// <returns>Return the Certificate builder.</returns>
        public static CertificateBuilder CreateCertificate(string subjectName)
        {
            return new CertificateBuilder(subjectName);
        }

        /// <summary>
        /// Create a certificate for a OPC UA application.
        /// </summary>
        /// <param name="applicationUri">The application Uri</param>
        /// <param name="applicationName">The friendly name of the application</param>
        /// <param name="subjectName">The subject of the certificate</param>
        /// <param name="domainNames">The domain names for the alt name extension</param>
        /// <returns>
        /// Return the Certificate builder with X509 Alt Name extension
        /// to create the certificate.
        /// </returns>
        public static CertificateBuilder CreateCertificate(
            string applicationUri,
            string applicationName,
            string subjectName,
            IList<string> domainNames)
        {
            SetSuitableDefaults(
                ref applicationUri,
                ref applicationName,
                ref subjectName,
                ref domainNames);

            return new CertificateBuilder(subjectName)
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domainNames));
        }

        public static X509Certificate2 CreateCertificate(
            string applicationUri,
            string applicationName,
            string subjectName,
            IList<String> domainNames,
            ushort keySize,
            DateTime startTime,
            ushort lifetimeInMonths,
            ushort hashSizeInBits,
            bool isCA = false,
            X509Certificate2 issuerCAKeyCert = null,
            byte[] publicKey = null,
            int pathLengthConstraint = 0,
            string extensionUrl = null)
        {
            CertificateBuilder builder = null;
            if (isCA)
            {
                builder = CreateCertificate(subjectName);
            }
            else
            {
                builder = CreateCertificate(
                    applicationUri,
                    applicationName,
                    subjectName,
                    domainNames);
            }
            builder.SetNotBefore(startTime);
            builder.SetNotAfter(startTime.AddMonths(lifetimeInMonths));
            builder.SetHashAlgorithm(GetRSAHashAlgorithmName(hashSizeInBits));
            if (isCA)
            {
                builder.SetCAConstraint(pathLengthConstraint);
            }
            if (issuerCAKeyCert != null)
            {
                if (publicKey != null)
                {
                    builder.SetIssuer(issuerCAKeyCert);
                    builder.SetRSAPublicKey(publicKey);
                }
                else
                {
                    builder.SetIssuer(issuerCAKeyCert);
                }
            }
            if (extensionUrl != null)
            {
                if (issuerCAKeyCert == null && publicKey != null)
                {
                    builder.AddExtension(X509Extensions.BuildX509AuthorityInformationAccess(new string[] { extensionUrl }));
                }
                else if (issuerCAKeyCert != null)
                {
                    builder.AddExtension(X509Extensions.BuildX509CRLDistributionPoints(extensionUrl));
                }
            }
            return builder.CreateForRSA(keySize);
        }

        /// <summary>
        /// Revoke the certificate. 
        /// The CRL number is increased by one and the new CRL is returned.
        /// </summary>
        public static X509CRL RevokeCertificate(
            X509Certificate2 issuerCertificate,
            List<X509CRL> issuerCrls,
            X509Certificate2Collection revokedCertificates
            )
        {
            return RevokeCertificate(issuerCertificate, issuerCrls, revokedCertificates,
                DateTime.UtcNow, DateTime.UtcNow.AddMonths(12));
        }

        /// <summary>
        /// Revoke the certificate. 
        /// The CRL number is increased by one and the new CRL is returned.
        /// </summary>
        public static X509CRL RevokeCertificate(
            X509Certificate2 issuerCertificate,
            List<X509CRL> issuerCrls,
            X509Certificate2Collection revokedCertificates,
            DateTime thisUpdate,
            DateTime nextUpdate
            )
        {
            if (!issuerCertificate.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Issuer certificate has no private key, cannot revoke certificate.");
            }

            System.Numerics.BigInteger crlSerialNumber = 0;
            IList<string> serialNumbers = new List<string>();

            // merge all existing revocation list
            if (issuerCrls != null)
            {
#if TODO
                Org.BouncyCastle.X509.X509CrlParser parser = new Org.BouncyCastle.X509.X509CrlParser();
                foreach (X509CRL issuerCrl in issuerCrls)
                {

                    Org.BouncyCastle.X509.X509Crl crl = parser.ReadCrl(issuerCrl.RawData);
                    // TODO: add serialnumbers
                    var crlVersion = new System.Numerics.BigInteger(GetCrlNumber(crl).ToByteArrayUnsigned());
                    if (crlVersion > crlSerialNumber)
                    {
                        crlSerialNumber = crlVersion;
                    }
                }
#endif
            }

            // add existing serial numbers
            if (revokedCertificates != null)
            {
                foreach (var cert in revokedCertificates)
                {
                    serialNumbers.Add(cert.SerialNumber);
                }
            }

            var hashAlgorithmName = Oids.GetHashAlgorithmName(issuerCertificate.SignatureAlgorithm.Value);
            CrlBuilder crlBuilder = new CrlBuilder(issuerCertificate.SubjectName, hashAlgorithmName)
                .AddRevokedSerialNumbers(serialNumbers.ToArray(), CRLReason.PrivilegeWithdrawn)
                .SetThisUpdate(thisUpdate)
                .SetNextUpdate(nextUpdate)
                .AddCRLExtension(X509Extensions.BuildAuthorityKeyIdentifier(issuerCertificate))
                .AddCRLExtension(X509Extensions.BuildCRLNumber(crlSerialNumber + 1));
            return new X509CRL(crlBuilder.CreateForRSA(issuerCertificate));
        }

#if NETSTANDARD2_1
        /// <summary>
        /// Creates a certificate signing request from an existing certificate.
        /// </summary>
        public static byte[] CreateSigningRequest(
            X509Certificate2 certificate,
            IList<String> domainNames = null
            )
        {
            if (!certificate.HasPrivateKey)
            {
                throw new NotSupportedException("Need a certificate with a private key.");
            }

            RSA rsaPublicKey = certificate.GetRSAPublicKey();
            var request = new CertificateRequest(certificate.SubjectName, rsaPublicKey,
                Oids.GetHashAlgorithmName(certificate.SignatureAlgorithm.Value), RSASignaturePadding.Pkcs1);

            var alternateName = X509Extensions.FindExtension<X509SubjectAltNameExtension>(certificate);
            domainNames = domainNames ?? new List<String>();
            if (alternateName != null)
            {
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

            string applicationUri = X509Utils.GetApplicationUriFromCertificate(certificate);

            // Subject Alternative Name
            var subjectAltName = new X509SubjectAltNameExtension(applicationUri, domainNames);
            request.CertificateExtensions.Add(new X509Extension(subjectAltName, false));

            using (RSA rsa = certificate.GetRSAPrivateKey())
            {
                var x509SignatureGenerator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
                return request.CreateSigningRequest(x509SignatureGenerator);
            }
        }


        /// <summary>
        /// Create a X509Certificate2 with a private key by combining 
        /// the new certificate with a private key from an existing certificate
        /// </summary>
        public static X509Certificate2 CreateCertificateWithPrivateKey(
            X509Certificate2 certificate,
            X509Certificate2 certificateWithPrivateKey)
        {
            if (!certificateWithPrivateKey.HasPrivateKey)
            {
                throw new NotSupportedException("Need a certificate with a private key.");
            }

            if (!X509Utils.VerifyRSAKeyPair(certificate, certificateWithPrivateKey))
            {
                throw new NotSupportedException("The public and the private key pair doesn't match.");
            }

            return certificate.CopyWithPrivateKey(certificateWithPrivateKey.GetRSAPrivateKey());
        }

        /// <summary>
        /// Create a X509Certificate2 with a private key by combining 
        /// the certificate with a private key from a PEM stream
        /// </summary>
        public static X509Certificate2 CreateCertificateWithPEMPrivateKey(
            X509Certificate2 certificate,
            byte[] pemDataBlob,
            string password = null)
        {
            RSA rsaPrivateKey = RSA.Create();
            PemReader pemReader;
            using (StreamReader pemStreamReader = new StreamReader(new MemoryStream(pemDataBlob), Encoding.UTF8, true))
            {
                if (String.IsNullOrEmpty(password))
                {
                    pemReader = new PemReader(pemStreamReader);
                }
                else
                {
                    Password pwFinder = new Password(password.ToCharArray());
                    pemReader = new PemReader(pemStreamReader, pwFinder);
                }
                try
                {
                    // find the private key in the PEM blob
                    var pemObject = pemReader.ReadObject();
                    while (pemObject != null)
                    {
                        Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters privateKey = null;
                        var keypair = pemObject as Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair;
                        if (keypair != null)
                        {
                            privateKey = keypair.Private as Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters;
                            break;
                        }

                        if (privateKey == null)
                        {
                            privateKey = pemObject as Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters;
                        }

                        if (privateKey != null)
                        {
                            rsaPrivateKey.ImportParameters(DotNetUtilities.ToRSAParameters(privateKey));
                            break;
                        }

                        // read next object
                        pemObject = pemReader.ReadObject();
                    }
                }
                finally
                {
                    pemReader.Reader.Dispose();
                }
            }

            if (rsaPrivateKey == null)
            {
                throw new ServiceResultException("PEM data blob does not contain a private key.");
            }

            return new X509Certificate2(certificate.RawData).CopyWithPrivateKey(rsaPrivateKey);

        }
#else
        /// <summary>
        /// Creates a certificate signing request from an existing certificate.
        /// </summary>
        public static byte[] CreateSigningRequest(
            X509Certificate2 certificate,
            IList<String> domainNames = null
            )
        {
            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                SecureRandom random = new SecureRandom(cfrg);

                // try to get signing/private key from certificate passed in
                AsymmetricKeyParameter signingKey = GetPrivateKeyParameter(certificate);
                RsaKeyParameters publicKey = GetPublicKeyParameter(certificate);

                ISignatureFactory signatureFactory =
                    new Asn1SignatureFactory(GetRSAHashAlgorithm(DefaultHashSize), signingKey, random);

                Asn1Set attributes = null;
                var san = Security.Certificates.X509Extensions.FindExtension<X509SubjectAltNameExtension>(certificate);
                X509SubjectAltNameExtension alternateName = new X509SubjectAltNameExtension(san, san.Critical);

                domainNames = domainNames ?? new List<String>();
                if (alternateName != null)
                {
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
                List<GeneralName> generalNames = new List<GeneralName>();

                string applicationUri = X509Utils.GetApplicationUriFromCertificate(certificate);
                if (applicationUri != null)
                {
                    generalNames.Add(new GeneralName(GeneralName.UniformResourceIdentifier, applicationUri));
                }

                if (domainNames.Count > 0)
                {
                    generalNames.AddRange(CreateSubjectAlternateNameDomains(domainNames));
                }

                if (generalNames.Count > 0)
                {
                    IList oids = new ArrayList();
                    IList values = new ArrayList();
                    oids.Add(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName);
                    values.Add(new Org.BouncyCastle.Asn1.X509.X509Extension(false,
                        new DerOctetString(new GeneralNames(generalNames.ToArray()).GetDerEncoded())));
                    AttributePkcs attribute = new AttributePkcs(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
                        new DerSet(new Org.BouncyCastle.Asn1.X509.X509Extensions(oids, values)));
                    attributes = new DerSet(attribute);
                }

                Pkcs10CertificationRequest pkcs10CertificationRequest = new Pkcs10CertificationRequest(
                    signatureFactory,
                    new CertificateFactoryX509Name(false, certificate.Subject),
                    publicKey,
                    attributes);

                return pkcs10CertificationRequest.GetEncoded();
            }
        }

        /// <summary>
        /// Create a X509Certificate2 with a private key by combining 
        /// the new certificate with a private key from an existing certificate
        /// </summary>
        public static X509Certificate2 CreateCertificateWithPrivateKey(
            X509Certificate2 certificate,
            X509Certificate2 certificateWithPrivateKey)
        {
            if (!certificateWithPrivateKey.HasPrivateKey)
            {
                throw new NotSupportedException("Need a certificate with a private key.");
            }

            if (!X509Utils.VerifyRSAKeyPair(certificate, certificateWithPrivateKey))
            {
                throw new NotSupportedException("The public and the private key pair doesn't match.");
            }

            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                SecureRandom random = new SecureRandom(cfrg);
                Org.BouncyCastle.X509.X509Certificate x509 = new X509CertificateParser().ReadCertificate(certificate.RawData);
                return CreateCertificateWithPrivateKey(x509, certificate.FriendlyName, GetPrivateKeyParameter(certificateWithPrivateKey), random);
            }
        }

        /// <summary>
        /// Create a X509Certificate2 with a private key by combining 
        /// the certificate with a private key from a PEM stream
        /// </summary>
        public static X509Certificate2 CreateCertificateWithPEMPrivateKey(
            X509Certificate2 certificate,
            byte[] pemDataBlob,
            string password = null)
        {
            AsymmetricKeyParameter privateKey = null;
            PemReader pemReader;
            using (StreamReader pemStreamReader = new StreamReader(new MemoryStream(pemDataBlob), Encoding.UTF8, true))
            {
                if (String.IsNullOrEmpty(password))
                {
                    pemReader = new PemReader(pemStreamReader);
                }
                else
                {
                    Password pwFinder = new Password(password.ToCharArray());
                    pemReader = new PemReader(pemStreamReader, pwFinder);
                }
                try
                {
                    // find the private key in the PEM blob
                    var pemObject = pemReader.ReadObject();
                    while (pemObject != null)
                    {
                        privateKey = pemObject as RsaPrivateCrtKeyParameters;
                        if (privateKey != null)
                        {
                            break;
                        }

                        AsymmetricCipherKeyPair keypair = pemObject as AsymmetricCipherKeyPair;
                        if (keypair != null)
                        {
                            privateKey = keypair.Private;
                            break;
                        }

                        // read next object
                        pemObject = pemReader.ReadObject();
                    }
                }
                finally
                {
                    pemReader.Reader.Dispose();
                }
            }

            if (privateKey == null)
            {
                throw new ServiceResultException("PEM data blob does not contain a private key.");
            }

            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                SecureRandom random = new SecureRandom(cfrg);
                Org.BouncyCastle.X509.X509Certificate x509 = new X509CertificateParser().ReadCertificate(certificate.RawData);
                return CreateCertificateWithPrivateKey(x509, certificate.FriendlyName, privateKey, random);
            }
        }
#endif
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets the parameters to suitable defaults.
        /// </summary>
        private static void SetSuitableDefaults(
            ref string applicationUri,
            ref string applicationName,
            ref string subjectName,
            ref IList<String> domainNames,
            ref ushort keySize,
            ref ushort lifetimeInMonths)
        {
            // enforce recommended keysize unless lower value is enforced.
            if (keySize < 1024)
            {
                keySize = DefaultKeySize;
            }

            if (keySize % 1024 != 0)
            {
                throw new ArgumentNullException(nameof(keySize), "KeySize must be a multiple of 1024.");
            }

            // enforce minimum lifetime.
            if (lifetimeInMonths < 1)
            {
                lifetimeInMonths = 1;
            }

            SetSuitableDefaults(
                ref applicationUri,
                ref applicationName,
                ref subjectName,
                ref domainNames);
        }

        /// <summary>
        /// Sets the parameters to suitable defaults.
        /// </summary>
        private static void SetSuitableDefaults(
            ref string applicationUri,
            ref string applicationName,
            ref string subjectName,
            ref IList<String> domainNames)
        {
            // parse the subject name if specified.
            List<string> subjectNameEntries = null;

            if (!String.IsNullOrEmpty(subjectName))
            {
                subjectNameEntries = X509Utils.ParseDistinguishedName(subjectName);
            }

            // check the application name.
            if (String.IsNullOrEmpty(applicationName))
            {
                if (subjectNameEntries == null)
                {
                    throw new ArgumentNullException(nameof(applicationName), "Must specify a applicationName or a subjectName.");
                }

                // use the common name as the application name.
                for (int ii = 0; ii < subjectNameEntries.Count; ii++)
                {
                    if (subjectNameEntries[ii].StartsWith("CN="))
                    {
                        applicationName = subjectNameEntries[ii].Substring(3).Trim();
                        break;
                    }
                }
            }

            if (String.IsNullOrEmpty(applicationName))
            {
                throw new ArgumentNullException(nameof(applicationName), "Must specify a applicationName or a subjectName.");
            }

            // remove special characters from name.
            StringBuilder buffer = new StringBuilder();

            for (int ii = 0; ii < applicationName.Length; ii++)
            {
                char ch = applicationName[ii];

                if (Char.IsControl(ch) || ch == '/' || ch == ',' || ch == ';')
                {
                    ch = '+';
                }

                buffer.Append(ch);
            }

            applicationName = buffer.ToString();

            // ensure at least one host name.
            if (domainNames == null || domainNames.Count == 0)
            {
                domainNames = new List<string>();
                domainNames.Add(Utils.GetHostName());
            }

            // create the application uri.
            if (String.IsNullOrEmpty(applicationUri))
            {
                StringBuilder builder = new StringBuilder();

                builder.Append("urn:");
                builder.Append(domainNames[0]);
                builder.Append(":");
                builder.Append(applicationName);

                applicationUri = builder.ToString();
            }

            Uri uri = Utils.ParseUri(applicationUri);

            if (uri == null)
            {
                throw new ArgumentNullException(nameof(applicationUri), "Must specify a valid URL.");
            }

            // create the subject name,
            if (String.IsNullOrEmpty(subjectName))
            {
                subjectName = Utils.Format("CN={0}", applicationName);
            }

            if (!subjectName.Contains("CN="))
            {
                subjectName = Utils.Format("CN={0}", subjectName);
            }

            if (domainNames != null && domainNames.Count > 0)
            {
                if (!subjectName.Contains("DC=") && !subjectName.Contains("="))
                {
                    subjectName += Utils.Format(", DC={0}", domainNames[0]);
                }
                else
                {
                    subjectName = Utils.ReplaceDCLocalhost(subjectName, domainNames[0]);
                }
            }
        }

#if !NETSTANDARD2_1
        /// <summary>
        /// helper to build alternate name domains list for certs.
        /// </summary>
        private static List<GeneralName> CreateSubjectAlternateNameDomains(IList<String> domainNames)
        {
            // subject alternate name
            List<GeneralName> generalNames = new List<GeneralName>();
            for (int i = 0; i < domainNames.Count; i++)
            {
                int domainType = GeneralName.OtherName;
                switch (Uri.CheckHostName(domainNames[i]))
                {
                    case UriHostNameType.Dns: domainType = GeneralName.DnsName; break;
                    case UriHostNameType.IPv4:
                    case UriHostNameType.IPv6: domainType = GeneralName.IPAddress; break;
                    default: continue;
                }
                generalNames.Add(new GeneralName(domainType, domainNames[i]));
            }
            return generalNames;
        }

        /// <summary>
        /// helper to get the Bouncy Castle hash algorithm name by hash size in bits.
        /// </summary>
        /// <param name="hashSizeInBits"></param>
        private static string GetRSAHashAlgorithm(uint hashSizeInBits)
        {
            if (hashSizeInBits <= 160)
            {
                return "SHA1WITHRSA";
            }

            if (hashSizeInBits <= 224)
            {
                return "SHA224WITHRSA";
            }
            else if (hashSizeInBits <= 256)
            {
                return "SHA256WITHRSA";
            }
            else if (hashSizeInBits <= 384)
            {
                return "SHA384WITHRSA";
            }
            else
            {
                return "SHA512WITHRSA";
            }
        }

        /// <summary>
        /// Get public key parameters from a X509Certificate2
        /// </summary>
        private static RsaKeyParameters GetPublicKeyParameter(X509Certificate2 certificate)
        {
            RSA rsa = null;
            try
            {
                rsa = certificate.GetRSAPublicKey();
                RSAParameters rsaParams = rsa.ExportParameters(false);
                return new RsaKeyParameters(
                    false,
                    new BigInteger(1, rsaParams.Modulus),
                    new BigInteger(1, rsaParams.Exponent));
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Get private key parameters from a X509Certificate2.
        /// The private key must be exportable.
        /// </summary>
        private static RsaPrivateCrtKeyParameters GetPrivateKeyParameter(X509Certificate2 certificate)
        {
            RSA rsa = null;
            try
            {
                // try to get signing/private key from certificate passed in
                rsa = certificate.GetRSAPrivateKey();
                RSAParameters rsaParams = rsa.ExportParameters(true);
                var keyParams = new RsaPrivateCrtKeyParameters(
                    new BigInteger(1, rsaParams.Modulus),
                    new BigInteger(1, rsaParams.Exponent),
                    new BigInteger(1, rsaParams.D),
                    new BigInteger(1, rsaParams.P),
                    new BigInteger(1, rsaParams.Q),
                    new BigInteger(1, rsaParams.DP),
                    new BigInteger(1, rsaParams.DQ),
                    new BigInteger(1, rsaParams.InverseQ));
                return keyParams;
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }
#endif
        /// <summary>
        /// Read the Crl number from a X509Crl.
        /// </summary>
        private static BigInteger GetCrlNumber(X509Crl crl)
        {
            BigInteger crlNumber = BigInteger.One;
            try
            {
                Asn1Object asn1Object = GetExtensionValue(crl, Org.BouncyCastle.Asn1.X509.X509Extensions.CrlNumber);
                if (asn1Object != null)
                {
                    crlNumber = CrlNumber.GetInstance(asn1Object).PositiveValue;
                }
            }
            finally
            {
            }
            return crlNumber;
        }

        /// <summary>
        /// Get the value of an extension oid.
        /// </summary>
        private static Asn1Object GetExtensionValue(IX509Extension extension, DerObjectIdentifier oid)
        {
            Asn1OctetString asn1Octet = extension.GetExtensionValue(oid);
            if (asn1Octet != null)
            {
                return Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(asn1Octet);
            }
            return null;
        }

#if !NETSTANDARD2_1
        /// <summary>
        /// Create a X509Certificate2 with a private key by combining 
        /// a bouncy castle X509Certificate and a private key
        /// </summary>
        private static X509Certificate2 CreateCertificateWithPrivateKey(
            Org.BouncyCastle.X509.X509Certificate certificate,
            string friendlyName,
            AsymmetricKeyParameter privateKey,
            SecureRandom random)
        {
            // create pkcs12 store for cert and private key
            using (MemoryStream pfxData = new MemoryStream())
            {
                Pkcs12StoreBuilder builder = new Pkcs12StoreBuilder();
                builder.SetUseDerEncoding(true);
                Pkcs12Store pkcsStore = builder.Build();
                X509CertificateEntry[] chain = new X509CertificateEntry[1];
                string passcode = Guid.NewGuid().ToString();
                chain[0] = new X509CertificateEntry(certificate);
                if (string.IsNullOrEmpty(friendlyName))
                {
                    friendlyName = GetCertificateCommonName(certificate);
                }
                pkcsStore.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(privateKey), chain);
                pkcsStore.Save(pfxData, passcode.ToCharArray(), random);

                // merge into X509Certificate2
                return X509Utils.CreateCertificateFromPKCS12(pfxData.ToArray(), passcode);
            }
        }

        /// <summary>
        /// Read the Common Name from a certificate.
        /// </summary>
        private static string GetCertificateCommonName(Org.BouncyCastle.X509.X509Certificate certificate)
        {
            var subjectDN = certificate.SubjectDN.GetValueList(X509Name.CN);
            if (subjectDN.Count > 0)
            {
                return subjectDN[0].ToString();
            }
            return string.Empty;
        }
#endif

        private class Password
            : IPasswordFinder
        {
            private readonly char[] password;

            public Password(
                char[] word)
            {
                this.password = (char[])word.Clone();
            }

            public char[] GetPassword()
            {
                return (char[])password.Clone();
            }
        }

        #endregion

        #region Private Methods
        private static HashAlgorithmName GetRSAHashAlgorithmName(uint hashSizeInBits)
        {
            if (hashSizeInBits <= 160)
            {
                return HashAlgorithmName.SHA1;
            }
            else if (hashSizeInBits <= 256)
            {
                return HashAlgorithmName.SHA256;
            }
            else if (hashSizeInBits <= 384)
            {
                return HashAlgorithmName.SHA384;
            }
            else
            {
                return HashAlgorithmName.SHA512;
            }
        }

        /// <summary>
        /// Patch serial number in a Url. byte version.
        /// </summary>
        private static string PatchExtensionUrl(string extensionUrl, byte[] serialNumber)
        {
            string serial = BitConverter.ToString(serialNumber).Replace("-", "");
            return PatchExtensionUrl(extensionUrl, serial);
        }

        /// <summary>
        /// Patch serial number in a Url. string version.
        /// </summary>
        private static string PatchExtensionUrl(string extensionUrl, string serial)
        {
            return extensionUrl.Replace("%serial%", serial.ToLower());
        }
        #endregion

        private static Dictionary<string, X509Certificate2> m_certificates = new Dictionary<string, X509Certificate2>();
        private static object m_certificatesLock = new object();
    }
}
