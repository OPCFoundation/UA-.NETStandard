/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

                if (ensurePrivateKeyAccessible)
                {
                    if (!X509Utils.VerifyRSAKeyPair(certificate, certificate))
                    {
                        Utils.LogWarning("Trying to add certificate to cache with invalid private key.");
                        return null;
                    }
                }

                // update the cache.
                m_certificates[certificate.Thumbprint] = certificate;

                if (m_certificates.Count > 100)
                {
                    Utils.LogWarning("Certificate cache has {0} certificates in it.", m_certificates.Count);
                }

            }
            return certificate;
        }

        /// <summary>
        /// Create a certificate for any use.
        /// </summary>
        /// <param name="subjectName">The subject of the certificate</param>
        /// <returns>Return the Certificate builder.</returns>
        public static ICertificateBuilder CreateCertificate(string subjectName)
        {
            return CertificateBuilder.Create(subjectName);
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
        public static ICertificateBuilder CreateCertificate(
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

            return CertificateBuilder.Create(subjectName)
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

        /// <summary>
        /// Revoke the certificate. 
        /// The CRL number is increased by one and the new CRL is returned.
        /// </summary>
        public static X509CRL RevokeCertificate(
            X509Certificate2 issuerCertificate,
            X509CRLCollection issuerCrls,
            X509Certificate2Collection revokedCertificates
            )
        {
            return RevokeCertificate(issuerCertificate, issuerCrls, revokedCertificates,
                DateTime.UtcNow, DateTime.UtcNow.AddMonths(12));
        }

        /// <summary>
        /// Revoke the certificates. 
        /// </summary>
        /// <remarks>
        /// Merge all existing revoked certificates from CRL list.
        /// Add serialnumbers of new revoked certificates.
        /// The CRL number is increased by one and the new CRL is returned.
        /// </remarks>
        public static X509CRL RevokeCertificate(
            X509Certificate2 issuerCertificate,
            X509CRLCollection issuerCrls,
            X509Certificate2Collection revokedCertificates,
            DateTime thisUpdate,
            DateTime nextUpdate
            )
        {
            if (!issuerCertificate.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Issuer certificate has no private key, cannot revoke certificate.");
            }

            BigInteger crlSerialNumber = 0;
            var crlRevokedList = new Dictionary<string, RevokedCertificate>();

            // merge all existing revocation list
            if (issuerCrls != null)
            {
                foreach (X509CRL issuerCrl in issuerCrls)
                {
                    var extension = X509Extensions.FindExtension<X509CrlNumberExtension>(issuerCrl.CrlExtensions);
                    if (extension != null &&
                        extension.CrlNumber > crlSerialNumber)
                    {
                        crlSerialNumber = extension.CrlNumber;
                    }
                    foreach (var revokedCertificate in issuerCrl.RevokedCertificates)
                    {
                        if (!crlRevokedList.ContainsKey(revokedCertificate.SerialNumber))
                        {
                            crlRevokedList[revokedCertificate.SerialNumber] = revokedCertificate;
                        }
                    }
                }
            }

            // add existing serial numbers
            if (revokedCertificates != null)
            {
                foreach (var cert in revokedCertificates)
                {
                    if (!crlRevokedList.ContainsKey(cert.SerialNumber))
                    {
                        var entry = new RevokedCertificate(cert.SerialNumber, CRLReason.PrivilegeWithdrawn);
                        crlRevokedList[cert.SerialNumber] = entry;
                    }
                }
            }

            CrlBuilder crlBuilder = CrlBuilder.Create(issuerCertificate.SubjectName)
                .AddRevokedCertificates(crlRevokedList.Values.ToList())
                .SetThisUpdate(thisUpdate)
                .SetNextUpdate(nextUpdate)
                .AddCRLExtension(X509Extensions.BuildAuthorityKeyIdentifier(issuerCertificate))
                .AddCRLExtension(X509Extensions.BuildCRLNumber(crlSerialNumber + 1));
            return new X509CRL(crlBuilder.CreateForRSA(issuerCertificate));
        }

#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
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
            return CertificateBuilder.CreateSigningRequest(
                certificate,
                domainNames);
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

            string passcode = X509Utils.GeneratePasscode();
            using (RSA rsaPrivateKey = certificateWithPrivateKey.GetRSAPrivateKey())
            {
                byte[] pfxData = CertificateBuilder.CreatePfxWithRSAPrivateKey(
                    certificate, certificate.FriendlyName, rsaPrivateKey, passcode);
                return X509Utils.CreateCertificateFromPKCS12(pfxData, passcode);
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

            string passcode = X509Utils.GeneratePasscode();
            byte[] pfxData = CertificateBuilder.CreatePfxWithRSAPrivateKey(
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
            ICertificateBuilder builder = null;
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
            builder.SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(hashSizeInBits));
            if (isCA)
            {
                builder.SetCAConstraint(pathLengthConstraint);
            }
            ICertificateBuilderCreateForRSA createBuilder;
            if (issuerCAKeyCert != null)
            {
                var issuerBuilder = builder.SetIssuer(issuerCAKeyCert);
                if (publicKey != null)
                {
                    createBuilder = issuerBuilder.SetRSAPublicKey(publicKey);
                }
                else
                {
                    createBuilder = issuerBuilder.SetRSAKeySize(keySize);
                }
            }
            else
            {
                createBuilder = builder.SetRSAKeySize(keySize);
            }
            return createBuilder.CreateForRSA();
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
                    if (subjectNameEntries[ii].StartsWith("CN=", StringComparison.Ordinal))
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
                builder.Append(':');
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
                if (!subjectName.Contains("DC=") && !subjectName.Contains('='))
                {
                    subjectName += Utils.Format(", DC={0}", domainNames[0]);
                }
                else
                {
                    subjectName = Utils.ReplaceDCLocalhost(subjectName, domainNames[0]);
                }
            }
        }
        #endregion

        private static Dictionary<string, X509Certificate2> m_certificates = new Dictionary<string, X509Certificate2>();
        private static object m_certificatesLock = new object();
    }
}
