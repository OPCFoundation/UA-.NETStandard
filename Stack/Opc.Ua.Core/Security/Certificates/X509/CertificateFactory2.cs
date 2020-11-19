/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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

#if NETSTANDARD2_1
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Security.Certificates.X509;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using X509Extensions = Opc.Ua.Security.Certificates.X509.X509Extensions;

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

            lock (m_certificates)
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

                // save the key container so it can be deleted later.
                m_temporaryKeyContainers.Add(certificate);
            }

            return certificate;
        }
        /// <summary>
        /// Creates a self signed application instance certificate.
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
        /// <param name="extensionUrl"></param>
        /// <returns>The certificate with a private key.</returns>
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
            int pathLengthConstraint = 0,
            string extensionUrl = null)
        {
            RSA rsaPublicKey = null;
            if (publicKey != null)
            {
                int bytes;
                try
                {
                    rsaPublicKey = RSA.Create();
                    rsaPublicKey.ImportSubjectPublicKeyInfo(publicKey, out bytes);
                }
                catch (Exception e)
                {
                    throw new ArgumentException("Failed to decode the public key.", e);
                }

                if (publicKey.Length != bytes)
                {
                    throw new ArgumentException("Decoded the public key but extra bytes were found.");
                }
            }

            if (rsaPublicKey != null)
            {
                if (rsaPublicKey.KeySize != keySize)
                {
                    throw new ArgumentException($"Public key size {rsaPublicKey.KeySize} does not match expected key size {keySize}");
                }
            }

            int serialNumberLength = isCA ? 20 : 10;

            // new serial number
            byte[] serialNumber = new byte[serialNumberLength];
            RandomNumberGenerator.Fill(serialNumber);
            serialNumber[0] &= 0x7F;

            // set default values.
            X500DistinguishedName subjectDN = SetSuitableDefaults(
                ref applicationUri,
                ref applicationName,
                ref subjectName,
                ref domainNames,
                ref keySize,
                ref lifetimeInMonths);

            DateTime notBefore = startTime;
            DateTime notAfter = startTime + TimeSpan.FromDays(lifetimeInMonths * 30);

            RSA rsaKeyPair = null;
            if (rsaPublicKey == null)
            {
                rsaKeyPair = RSA.Create(keySize);
                rsaPublicKey = rsaKeyPair;
            }
            var request = new CertificateRequest(subjectDN, rsaPublicKey, GetRSAHashAlgorithmName(hashSizeInBits), RSASignaturePadding.Pkcs1);

            // Basic constraints
            if (!isCA && issuerCAKeyCert == null)
            {
                // self signed
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(true, true, 0, true));
            }
            else if (isCA && pathLengthConstraint >= 0)
            {
                // CA with constraints
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(true, true, pathLengthConstraint, true));
            }
            else
            {
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(isCA, false, 0, true));
            }

            // Subject Key Identifier
            var ski = new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                X509SubjectKeyIdentifierHashAlgorithm.Sha1,
                false);
            request.CertificateExtensions.Add(ski);

            // Authority Key Identifier
            X509Extension authorityKeyIdentifier = null;
            if (issuerCAKeyCert != null)
            {
                authorityKeyIdentifier = X509Extensions.BuildAuthorityKeyIdentifier(issuerCAKeyCert);
            }
            else
            {
                authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(subjectDN, serialNumber.Reverse().ToArray(), Utils.FromHexString(ski.SubjectKeyIdentifier));
            }
            request.CertificateExtensions.Add(authorityKeyIdentifier);

            if (isCA)
            {
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                        true));
                if (extensionUrl != null)
                {
                    // add CRL endpoint, if available
                    request.CertificateExtensions.Add(
                        X509Extensions.BuildX509CRLDistributionPoints(PatchExtensionUrl(extensionUrl, serialNumber))
                        );
                }
            }
            else
            {
                // Key Usage
                X509KeyUsageFlags defaultFlags =
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.DataEncipherment |
                        X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyEncipherment;
                if (issuerCAKeyCert == null)
                {
                    // self signed case
                    defaultFlags |= X509KeyUsageFlags.KeyCertSign;
                }
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(defaultFlags, true));

                // Enhanced key usage
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection {
                            new Oid(OidConstants.ServerAuthentication),
                            new Oid(OidConstants.ClientAuthentication)
                        }, true));

                // Subject Alternative Name
                request.CertificateExtensions.Add(new X509SubjectAltNameExtension(applicationUri, domainNames));

                if (issuerCAKeyCert != null &&
                    extensionUrl != null)
                {   // add Authority Information Access, if available
                    request.CertificateExtensions.Add(
                        X509Extensions.BuildX509AuthorityInformationAccess(new string[] { PatchExtensionUrl(extensionUrl, issuerCAKeyCert.SerialNumber) })
                        );
                }
            }

            if (issuerCAKeyCert != null)
            {
                if (notAfter > issuerCAKeyCert.NotAfter)
                {
                    notAfter = issuerCAKeyCert.NotAfter;
                }
                if (notBefore < issuerCAKeyCert.NotBefore)
                {
                    notBefore = issuerCAKeyCert.NotBefore;
                }
            }

            X509Certificate2 certificate;
            X509Certificate2 signedCert;
            if (issuerCAKeyCert != null)
            {
                var issuerSubjectName = issuerCAKeyCert != null ? issuerCAKeyCert.SubjectName : subjectDN;
                using (RSA rsa = issuerCAKeyCert.GetRSAPrivateKey())
                {
                    var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
                    signedCert = request.Create(
                        issuerCAKeyCert,
                        notBefore,
                        notAfter,
                        serialNumber
                        );
                }
            }
            else
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsaPublicKey, RSASignaturePadding.Pkcs1);
                signedCert = request.Create(
                    subjectDN,
                    generator,
                    notBefore,
                    notAfter,
                    serialNumber
                    );
            }

            // convert to X509Certificate2
            if (rsaKeyPair == null)
            {
                // create the cert without the private key
                certificate = signedCert;
            }
            else
            {
                // note: this cert has a private key!
                certificate = signedCert.CopyWithPrivateKey(rsaKeyPair);
            }

            // add cert to the store.
            if (!String.IsNullOrEmpty(storePath) && !String.IsNullOrEmpty(storeType))
            {
                using (ICertificateStore store = CertificateStoreIdentifier.CreateStore(storeType))
                {
                    if (store == null)
                    {
                        throw new ArgumentException("Invalid store type");
                    }

                    store.Open(storePath);
                    store.Add(certificate, password).Wait();
                    store.Close();
                }
            }

            return certificate;
        }

        /// <summary>
        /// Creates a certificate from a PKCS #12 store with a private key.
        /// </summary>
        /// <param name="rawData">The raw PKCS #12 store data.</param>
        /// <param name="password">The password to use to access the store.</param>
        /// <returns>The certificate with a private key.</returns>
        public static X509Certificate2 CreateCertificateFromPKCS12(
            byte[] rawData,
            string password
            )
        {
            Exception ex = null;
            int flagsRetryCounter = 0;
            X509Certificate2 certificate = null;

            // We need to try MachineKeySet first as UserKeySet in combination with PersistKeySet hangs ASP.Net WebApps on Azure
            X509KeyStorageFlags[] storageFlags = {
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.MachineKeySet,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet
            };

            // try some combinations of storage flags, support is platform dependent
            while (certificate == null &&
                flagsRetryCounter < storageFlags.Length)
            {
                try
                {
                    // merge first cert with private key into X509Certificate2
                    certificate = new X509Certificate2(
                        rawData,
                        password ?? String.Empty,
                        storageFlags[flagsRetryCounter]);
                    // can we really access the private key?
                    if (X509Utils.VerifyRSAKeyPair(certificate, certificate, true))
                    {
                        return certificate;
                    }
                }
                catch (Exception e)
                {
                    ex = e;
                    certificate?.Dispose();
                    certificate = null;
                }
                flagsRetryCounter++;
            }

            if (certificate == null)
            {
                throw new NotSupportedException("Creating X509Certificate from PKCS #12 store failed", ex);
            }

            return certificate;
        }

        /// <summary>
        /// Revoke the CA signed certificate. 
        /// The issuer CA public key, the private key and the crl reside in the storepath.
        /// The CRL number is increased by one and existing CRL for the issuer are deleted from the store.
        /// </summary>
        public static async Task<X509CRL> RevokeCertificateAsync(
            string storePath,
            X509Certificate2 certificate,
            string issuerKeyFilePassword = null
            )
        {
            X509CRL updatedCRL = null;
            try
            {
                string subjectName = certificate.IssuerName.Name;
                string keyId = null;
                string serialNumber = null;

                // caller may want to create empty CRL using the CA cert itself
                bool isCACert = X509Utils.IsCertificateAuthority(certificate);

                // find the authority key identifier.
                X509AuthorityKeyIdentifierExtension authority = X509Extensions.FindExtension<X509AuthorityKeyIdentifierExtension>(certificate);

                if (authority != null)
                {
                    keyId = authority.KeyIdentifier;
                    serialNumber = authority.SerialNumber;
                }
                else
                {
                    throw new ArgumentException("Certificate does not contain an Authority Key");
                }

                if (!isCACert)
                {
                    if (serialNumber == certificate.SerialNumber ||
                        X509Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer))
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Cannot revoke self signed certificates");
                    }
                }

                X509Certificate2 certCA = null;
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    if (store == null)
                    {
                        throw new ArgumentException("Invalid store path/type");
                    }
                    certCA = await FindIssuerCABySerialNumberAsync(store, certificate.Issuer, serialNumber);

                    if (certCA == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Cannot find issuer certificate in store.");
                    }

                    if (!certCA.HasPrivateKey)
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Issuer certificate has no private key, cannot revoke certificate.");
                    }

                    CertificateIdentifier certCAIdentifier = new CertificateIdentifier(certCA) {
                        StorePath = storePath,
                        StoreType = CertificateStoreIdentifier.DetermineStoreType(storePath)
                    };
                    X509Certificate2 certCAWithPrivateKey = await certCAIdentifier.LoadPrivateKey(issuerKeyFilePassword);

                    if (certCAWithPrivateKey == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Failed to load issuer private key. Is the password correct?");
                    }

                    List<X509CRL> certCACrl = store.EnumerateCRLs(certCA, false);

                    var certificateCollection = new X509Certificate2Collection() { };
                    if (!isCACert)
                    {
                        certificateCollection.Add(certificate);
                    }
                    updatedCRL = RevokeCertificate(certCAWithPrivateKey, certCACrl, certificateCollection);

                    store.AddCRL(updatedCRL);

                    // delete outdated CRLs from store
                    foreach (X509CRL caCrl in certCACrl)
                    {
                        store.DeleteCRL(caCrl);
                    }
                    store.Close();
                }
            }
            catch (Exception)
            {
                throw;
            }
            return updatedCRL;
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

            System.Numerics.BigInteger crlSerialNumber;
            IList<string> serialNumbers = new List<string>();

            // merge all existing revocation list
            if (issuerCrls != null)
            {
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
            }

            // add existing serial numbers
            if (revokedCertificates != null)
            {
                foreach (var cert in revokedCertificates)
                {
                    serialNumbers.Add(cert.SerialNumber);
                }
            }

            var hashAlgorithmName = HashAlgorithmName.SHA256;
            CrlBuilder crlBuilder = new CrlBuilder(issuerCertificate.SubjectName, serialNumbers.ToArray(), hashAlgorithmName);
            crlBuilder.ThisUpdate = thisUpdate;
            crlBuilder.NextUpdate = nextUpdate;
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildAuthorityKeyIdentifier(issuerCertificate));
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(crlSerialNumber + 1));
            byte[] crlRawData = crlBuilder.GetEncoded();
            using (RSA rsa = issuerCertificate.GetRSAPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
                byte[] signature = generator.SignData(crlRawData, hashAlgorithmName);
                var crlSigner = new X509Signature(crlRawData, signature, hashAlgorithmName);
                byte[] crlWithSignature = crlSigner.GetEncoded();
                return new X509CRL(crlWithSignature);
            }
        }


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
                GetRSAHashAlgorithmName(certificate.SignatureAlgorithm), RSASignaturePadding.Pkcs1);

            X509SubjectAltNameExtension alternateName = null;
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid || extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    alternateName = new X509SubjectAltNameExtension(extension, extension.Critical);
                    break;
                }
            }

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
            var subjectAltName = X509Extensions.BuildSubjectAlternativeName(applicationUri, domainNames);
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

        /// <summary>
        /// Returns a byte array containing the cert in PEM format.
        /// </summary>
        public static byte[] ExportCertificateAsPEM(X509Certificate2 certificate)
        {
            return EncodeAsPem(certificate.RawData, "CERTIFICATE");
        }

        /// <summary>
        /// Returns a byte array containing the public key in PEM format.
        /// </summary>
        public static byte[] ExportPublicKeyAsPEM(
            X509Certificate2 certificate
            )
        {
            byte[] exportedPublicKey = null;
            RSA rsaPublicKey = null;
            try
            {
                rsaPublicKey = certificate.GetRSAPublicKey();
                exportedPublicKey = rsaPublicKey.ExportSubjectPublicKeyInfo();
            }
            finally
            {
                RsaUtils.RSADispose(rsaPublicKey);
            }
            return EncodeAsPem(exportedPublicKey, "PUBLIC KEY");
        }

        /// <summary>
        /// Returns a byte array containing the private key in PEM format.
        /// </summary>
        public static byte[] ExportPrivateKeyAsPEM(
            X509Certificate2 certificate,
            string password = null
            )
        {
            byte[] exportedPkcs8PrivateKey = null;
            RSA rsaPrivateKey = null;
            try
            {
                rsaPrivateKey = certificate.GetRSAPrivateKey();
                // write private key as PKCS#8
                exportedPkcs8PrivateKey = String.IsNullOrEmpty(password) ?
                    rsaPrivateKey.ExportPkcs8PrivateKey() :
                    rsaPrivateKey.ExportEncryptedPkcs8PrivateKey(password.ToCharArray(),
                        new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, HashAlgorithmName.SHA1, 2000));
            }
            finally
            {
                RsaUtils.RSADispose(rsaPrivateKey);
            }
            return EncodeAsPem(exportedPkcs8PrivateKey,
                String.IsNullOrEmpty(password) ? "PRIVATE KEY" : "ENCRYPTED PRIVATE KEY");
        }
        #endregion

        #region Private Methods
        private static byte[] EncodeAsPem(byte[] content, string contentType)
        {
            const int LineLength = 64;
            string base64 = Convert.ToBase64String(content);
            using (TextWriter textWriter = new StringWriter())
            {
                textWriter.WriteLine("-----BEGIN {0}-----", contentType);
                while (base64.Length > LineLength)
                {
                    textWriter.WriteLine(base64.Substring(0, LineLength));
                    base64 = base64.Substring(LineLength);
                }
                textWriter.WriteLine(base64);
                textWriter.WriteLine("-----END {0}-----", contentType);
                return Encoding.ASCII.GetBytes(textWriter.ToString());
            }
        }

        /// <summary>
        /// Sets the parameters to suitable defaults.
        /// </summary>
        private static X500DistinguishedName SetSuitableDefaults(
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

            return new X500DistinguishedName(subjectName);
        }

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

        private static HashAlgorithmName GetRSAHashAlgorithmName(Oid signatureAlgorithm)
        {
            switch (signatureAlgorithm.Value)
            {
                case OidConstants.RsaPkcs1Sha1:
                    return HashAlgorithmName.SHA1;
                case OidConstants.RsaPkcs1Sha256:
                    return HashAlgorithmName.SHA256;
                case OidConstants.RsaPkcs1Sha384:
                    return HashAlgorithmName.SHA384;
                case OidConstants.RsaPkcs1Sha512:
                    return HashAlgorithmName.SHA512;
            }
            throw new NotSupportedException($"Signature algorithm {signatureAlgorithm.FriendlyName} is not supported.");
        }

        /// <summary>
        /// Convert a hex string to a byte array.
        /// </summary>
        /// <param name="hexString">The hex string</param>
        internal static byte[] HexToByteArray(string hexString)
        {
            byte[] bytes = new byte[hexString.Length / 2];

            for (int i = 0; i < hexString.Length; i += 2)
            {
                string s = hexString.Substring(i, 2);
                bytes[i / 2] = byte.Parse(s, System.Globalization.NumberStyles.HexNumber, null);
            }

            return bytes;
        }

        /// <summary>
        /// Read the Crl number from a X509Crl.
        /// </summary>
        public static BigInteger GetCrlNumber(Org.BouncyCastle.X509.X509Crl crl)
        {
            var crlNumber = BigInteger.One;
            try
            {
                Org.BouncyCastle.Asn1.Asn1Object asn1Object = GetExtensionValue(crl, Org.BouncyCastle.Asn1.X509.X509Extensions.CrlNumber);
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
        private static Org.BouncyCastle.Asn1.Asn1Object GetExtensionValue(Org.BouncyCastle.X509.IX509Extension extension, Org.BouncyCastle.Asn1.DerObjectIdentifier oid)
        {
            Org.BouncyCastle.Asn1.Asn1OctetString asn1Octet = extension.GetExtensionValue(oid);
            if (asn1Octet != null)
            {
                return Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(asn1Octet);
            }
            return null;
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

        /// <summary>
        /// Get the certificate by issuer and serial number.
        /// </summary>
        private static async Task<X509Certificate2> FindIssuerCABySerialNumberAsync(
            ICertificateStore store,
            string issuer,
            string serialnumber)
        {
            X509Certificate2Collection certificates = await store.Enumerate();

            foreach (var certificate in certificates)
            {
                if (X509Utils.CompareDistinguishedName(certificate.Subject, issuer) &&
                    Utils.IsEqual(certificate.SerialNumber, serialnumber))
                {
                    return certificate;
                }
            }

            return null;
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
        #endregion

        private static Dictionary<string, X509Certificate2> m_certificates = new Dictionary<string, X509Certificate2>();
        private static List<X509Certificate2> m_temporaryKeyContainers = new List<X509Certificate2>();
    }
}
#endif
