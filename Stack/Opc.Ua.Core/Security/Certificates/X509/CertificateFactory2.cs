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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

using X509Extensions = Opc.Ua.Security.Certificates.X509Extensions;

namespace Opc.Ua
{
    /// <summary>
    /// Creates certificates.
    /// </summary>
    public static partial class CertificateFactory
    {
#if NETSTANDARD2_1
#if test
        // CertificateFactory.CreateCertificate(subjectName)
        var builder = CertificateFactory.CreateCertificate(applicationUri, applicationName, subjectName)
            .AddDomainNames(domainNames)
            .SetLifeTime()
            .SetNotBefore()
            .SetNotAfter()
            //.SetCAIssuer(3)
            .AddExtension(ext)
            //.SetSecurityProfile(xyz).SetRSA().SetECDsa.SetRSA(key,hashname)
            //.SetIssuerCertificate(cert)

            certa = builder.CreateForRSA
            certb = CreateForECDSa()

            builder.CreateForProfile()

#endif
        public static CertificateBuilder CreateCertificate(string subjectName)
        {
            return new CertificateBuilder(subjectName);
        }

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

            var builder = new CertificateBuilder(subjectName);
            var altName = new X509SubjectAltNameExtension(applicationUri, domainNames);
            builder.AddExtension(altName);
            return builder;
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
            if (extensionUrl!=null)
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
        /// Creates a self signed application instance certificate.
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
        /// <param name="extensionUrl"></param>
        /// <returns>The certificate with a private key.</returns>
        public static X509Certificate2 CreateCertificateOld(
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
            SetSuitableDefaults(
                ref applicationUri,
                ref applicationName,
                ref subjectName,
                ref domainNames,
                ref keySize,
                ref lifetimeInMonths);

            X500DistinguishedName subjectDN = new X500DistinguishedName(subjectName);

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
                authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(Utils.FromHexString(ski.SubjectKeyIdentifier), subjectDN, serialNumber.Reverse().ToArray());
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
                            new Oid(Oids.ServerAuthentication),
                            new Oid(Oids.ClientAuthentication)
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

            return certificate;
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
#endif

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
    }
}

