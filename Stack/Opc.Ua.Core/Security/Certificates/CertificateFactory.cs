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
#if !NETSTANDARD2_1
using Opc.Ua.Security.Certificates.BouncyCastle;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using X509Extensions = Opc.Ua.Security.Certificates.X509Extensions;
using X509Utils = Opc.Ua.Security.Certificates.X509Utils;
#endif

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
        /// Create a certificate for an OPC UA application.
        /// </summary>
        /// <param name="applicationUri">The application Uri</param>
        /// <param name="applicationName">The friendly name of the application</param>
        /// <param name="subjectName">The subject of the certificate</param>
        /// <param name="domainNames">The domain names for the alt name extension</param>
        /// <returns>
        /// Return the Certificate builder with X509 Subject Alt Name extension
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

        /// <summary>
        /// Creates a self-signed, signed or CA certificate.
        /// </summary>
        /// <param name="storeType">Type of certificate store (Directory) <see cref="CertificateStoreType"/>.</param>
        /// <param name="storePath">The store path (syntax depends on storeType).</param>
        /// <param name="password">The password to use to protect the certificate.</param>
        /// <param name="applicationUri">The application uri (created if not specified).</param>
        /// <param name="applicationName">Name of the application (optional if subjectName is specified).</param>
        /// <param name="subjectName">The subject used to create the certificate (optional if applicationName is specified).</param>
        /// <param name="domainNames">The domain names that can be used to access the server machine (defaults to local computer name if not specified).</param>
        /// <param name="keySize">Size of the key (1024, 2048 or 4096).</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="lifetimeInMonths">The lifetime of the key in months.</param>
        /// <param name="hashSizeInBits">The hash size in bits.</param>
        /// <param name="isCA">if set to <c>true</c> then a CA certificate is created.</param>
        /// <param name="issuerCAKeyCert">The CA cert with the CA private key.</param>
        /// <param name="publicKey">The public key if no new keypair is created.</param>
        /// <param name="pathLengthConstraint">The path length constraint for CA certs.</param>
        /// <returns>The certificate with a private key.</returns>
        [Obsolete("Use the new CreateCertificate methods with CertificateBuilder.")]
        public static X509Certificate2 CreateCertificate(
            string storeType,
            string storePath,
            string password,
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
            int pathLengthConstraint = 0)
        {
            return CreateCertificate(
                applicationUri, applicationName, subjectName, domainNames,
                keySize, startTime, lifetimeInMonths, hashSizeInBits,
                isCA, issuerCAKeyCert, publicKey, pathLengthConstraint)
                .AddToStore(storeType, storePath, password);
        }

#if !NETSTANDARD2_1
        /// <summary>
        /// Create a 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="keySize"></param>
        /// <returns></returns>
        public static X509Certificate2 CreateForRSA(this CertificateBuilder builder, int keySize = 0)
        {
            if (builder.HasPublicKey)
            {
                return builder.CreateForRSAWithPublicKey();
            }
            else
            {
                string passcode = Guid.NewGuid().ToString();
                return X509Utils.CreateCertificateFromPKCS12(builder.CreatePfxForRSA(passcode, keySize), passcode);
            }
        }
#endif

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
            RSA rsaPrivateKey = PEMReader.ImportPrivateKeyFromPEM(pemDataBlob, password);
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
                var san = X509Extensions.FindExtension<X509SubjectAltNameExtension>(certificate);
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
                var generalNames = new List<Org.BouncyCastle.Asn1.X509.GeneralName>();

                string applicationUri = X509Utils.GetApplicationUriFromCertificate(certificate);
                if (applicationUri != null)
                {
                    generalNames.Add(new Org.BouncyCastle.Asn1.X509.GeneralName(Org.BouncyCastle.Asn1.X509.GeneralName.UniformResourceIdentifier, applicationUri));
                }

                if (domainNames.Count > 0)
                {
                    generalNames.AddRange(Opc.Ua.Security.Certificates.BouncyCastle.X509Extensions.CreateSubjectAlternateNameDomains(domainNames));
                }

                if (generalNames.Count > 0)
                {
                    IList oids = new ArrayList();
                    IList values = new ArrayList();
                    oids.Add(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName);
                    values.Add(new Org.BouncyCastle.Asn1.X509.X509Extension(false,
                        new DerOctetString(new Org.BouncyCastle.Asn1.X509.GeneralNames(generalNames.ToArray()).GetDerEncoded())));
                    var attribute = new Org.BouncyCastle.Asn1.Pkcs.AttributePkcs(Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
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

            string passcode = Guid.NewGuid().ToString();
            RSA rsaPrivateKey = null;
            try
            {
                rsaPrivateKey = certificateWithPrivateKey.GetRSAPrivateKey();
                byte[] pfxData = CertificateBuilder.CreatePfxWithPrivateKey(
                    certificate, certificate.FriendlyName, rsaPrivateKey, passcode);
                return X509Utils.CreateCertificateFromPKCS12(pfxData, passcode);
            }
            finally
            {
                RsaUtils.RSADispose(rsaPrivateKey);
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
            RSA privateKey = PEMReader.ImportPrivateKeyFromPEM(pemDataBlob, password);
            if (privateKey == null)
            {
                throw new ServiceResultException("PEM data blob does not contain a private key.");
            }

            string passcode = Guid.NewGuid().ToString();
            byte[] pfxData = CertificateBuilder.CreatePfxWithPrivateKey(
                certificate, certificate.FriendlyName, privateKey, passcode);
            return X509Utils.CreateCertificateFromPKCS12(pfxData, passcode);
        }
#endif
        #endregion

        #region Internal Methods
        /// <summary>
        /// Creates a self-signed, signed or CA certificate.
        /// </summary>
        /// <param name="applicationUri">The application uri (created if not specified).</param>
        /// <param name="applicationName">Name of the application (optional if subjectName is specified).</param>
        /// <param name="subjectName">The subject used to create the certificate (optional if applicationName is specified).</param>
        /// <param name="domainNames">The domain names that can be used to access the server machine (defaults to local computer name if not specified).</param>
        /// <param name="keySize">Size of the key (1024, 2048 or 4096).</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="lifetimeInMonths">The lifetime of the key in months.</param>
        /// <param name="hashSizeInBits">The hash size in bits.</param>
        /// <param name="isCA">if set to <c>true</c> then a CA certificate is created.</param>
        /// <param name="issuerCAKeyCert">The CA cert with the CA private key.</param>
        /// <param name="publicKey">The public key if no new keypair is created.</param>
        /// <param name="pathLengthConstraint">The path length constraint for CA certs.</param>
        /// <returns>The certificate with a private key.</returns>
        [Obsolete("Use the new CreateCertificate methods with CertificateBuilder.")]
        internal static X509Certificate2 CreateCertificate(
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
        int pathLengthConstraint = 0)
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
            return builder.CreateForRSA(keySize);
        }
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
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.Modulus),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.Exponent));
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
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.Modulus),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.Exponent),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.D),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.P),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.Q),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.DP),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.DQ),
                    new Org.BouncyCastle.Math.BigInteger(1, rsaParams.InverseQ));
                return keyParams;
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Read the Crl number from a X509Crl.
        /// </summary>
        private static Org.BouncyCastle.Math.BigInteger GetCrlNumber(Org.BouncyCastle.X509.X509Crl crl)
        {
            Org.BouncyCastle.Math.BigInteger crlNumber = Org.BouncyCastle.Math.BigInteger.One;
            try
            {
                Asn1Object asn1Object = GetExtensionValue(crl, Org.BouncyCastle.Asn1.X509.X509Extensions.CrlNumber);
                if (asn1Object != null)
                {
                    crlNumber = Org.BouncyCastle.Asn1.X509.CrlNumber.GetInstance(asn1Object).PositiveValue;
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
        private static Asn1Object GetExtensionValue(Org.BouncyCastle.X509.IX509Extension extension, DerObjectIdentifier oid)
        {
            Asn1OctetString asn1Octet = extension.GetExtensionValue(oid);
            if (asn1Octet != null)
            {
                return Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(asn1Octet);
            }
            return null;
        }


        /// <summary>
        /// Read the Common Name from a certificate.
        /// </summary>
        private static string GetCertificateCommonName(Org.BouncyCastle.X509.X509Certificate certificate)
        {
            var subjectDN = certificate.SubjectDN.GetValueList(Org.BouncyCastle.Asn1.X509.X509Name.CN);
            if (subjectDN.Count > 0)
            {
                return subjectDN[0].ToString();
            }
            return string.Empty;
        }

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
#endif
        /// <summary>
        /// Get the hash algorithm from the hash size in bits.
        /// </summary>
        /// <param name="hashSizeInBits"></param>
        /// <returns></returns>
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
        #endregion

        private static Dictionary<string, X509Certificate2> m_certificates = new Dictionary<string, X509Certificate2>();
        private static object m_certificatesLock = new object();
    }
}
