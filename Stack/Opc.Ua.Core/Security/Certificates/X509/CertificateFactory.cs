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
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace Opc.Ua
{

#if !NETSTANDARD2_1
    /// <summary>
    /// Secure .Net Core Random Number generator wrapper for Bounce Castle.
    /// Creates an instance of RNGCryptoServiceProvider or an OpenSSL based version on other OS.
    /// </summary>
    public class CertificateFactoryRandomGenerator : IRandomGenerator, IDisposable
    {
        RandomNumberGenerator m_prg;

        /// <summary>
        /// Creates an instance of a crypthographic secure random number generator.
        /// </summary>
        public CertificateFactoryRandomGenerator()
        {
            m_prg = RandomNumberGenerator.Create();
        }

        /// <summary>
        /// Dispose the random number generator.
        /// </summary>
        public void Dispose()
        {
            m_prg.Dispose();
        }

        /// <summary>Add more seed material to the generator. Not needed here.</summary>
        public void AddSeedMaterial(byte[] seed) { }

        /// <summary>Add more seed material to the generator. Not needed here.</summary>
        public void AddSeedMaterial(long seed) { }

        /// <summary>
        /// Fills an array of bytes with a cryptographically strong
        /// random sequence of values.
        /// </summary>
        /// <param name="bytes">Array to be filled.</param>
        public void NextBytes(byte[] bytes)
        {
            m_prg.GetBytes(bytes);
        }

        /// <summary>
        /// Fills an array of bytes with a cryptographically strong
        /// random sequence of values.
        /// </summary>
        /// <param name="bytes">Array to receive bytes.</param>
        /// <param name="start">Index to start filling at.</param>
        /// <param name="len">Length of segment to fill.</param>
        public void NextBytes(byte[] bytes, int start, int len)
        {
            byte[] temp = new byte[len];
            m_prg.GetBytes(temp);
            Array.Copy(temp, 0, bytes, start, len);
        }
    }

    /// <summary>
    /// A converter class to create a X509Name object 
    /// from a X509Certificate subject.
    /// </summary>
    /// <remarks>
    /// Handles subtle differences in the string representation
    /// of the .NET and the Bouncy Castle implementation.
    /// </remarks>
    public class CertificateFactoryX509Name : X509Name
    {

        /// <summary>
        /// Create the X509Name from a distinguished name.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name.</param>
        public CertificateFactoryX509Name(string distinguishedName) :
            base(true, ConvertToX509Name(distinguishedName))
        {
        }

        /// <summary>
        /// Create the X509Name from a distinguished name.
        /// </summary>
        /// <param name="reverse">Reverse the order of the names.</param>
        /// <param name="distinguishedName">The distinguished name.</param>
        public CertificateFactoryX509Name(bool reverse, string distinguishedName) :
            base(reverse, ConvertToX509Name(distinguishedName))
        {
        }

        private static string ConvertToX509Name(string distinguishedName)
        {
            // convert from X509Certificate to bouncy castle DN entries
            return distinguishedName.Replace("S=", "ST=");
        }
    }
#endif
    /// <summary>
    /// Creates certificates.
    /// </summary>
    public static partial class CertificateFactory
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

                // save the key container so it can be deleted later.
                m_temporaryKeyContainers.Add(certificate);
            }

            return certificate;
        }

#if !NETSTANDARD2_1
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
        /// <returns>The certificate with a private key.</returns>
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
            int pathLengthConstraint = 0)
        {
            if (issuerCAKeyCert != null)
            {
                if (!issuerCAKeyCert.HasPrivateKey)
                {
                    throw new NotSupportedException("Cannot sign with a CA certificate without a private key.");
                }
            }

            if (publicKey != null && issuerCAKeyCert == null)
            {
                throw new NotSupportedException("Cannot use a public key without a CA certificate with a private key.");
            }

            // set default values and validate parameters
            SetSuitableDefaults(
                ref applicationUri,
                ref applicationName,
                ref subjectName,
                ref domainNames,
                ref keySize,
                ref lifetimeInMonths);

            X509Name subjectDN = new CertificateFactoryX509Name(subjectName);

            using (var cfrg = new CertificateFactoryRandomGenerator())
            {
                // cert generators
                SecureRandom random = new SecureRandom(cfrg);
                X509V3CertificateGenerator cg = new X509V3CertificateGenerator();

                // Serial Number
                BigInteger serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
                cg.SetSerialNumber(serialNumber);

                // subject and issuer DN
                X509Name issuerDN = null;
                if (issuerCAKeyCert != null)
                {
                    issuerDN = new CertificateFactoryX509Name(issuerCAKeyCert.Subject);
                }
                else
                {
                    // self signed 
                    issuerDN = subjectDN;
                }
                cg.SetIssuerDN(issuerDN);
                cg.SetSubjectDN(subjectDN);

                // valid for
                cg.SetNotBefore(startTime);
                cg.SetNotAfter(startTime.AddMonths(lifetimeInMonths));

                // set Private/Public Key
                AsymmetricKeyParameter subjectPublicKey;
                AsymmetricKeyParameter subjectPrivateKey;
                if (publicKey == null)
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
                    subjectPublicKey = PublicKeyFactory.CreateKey(publicKey);
                    subjectPrivateKey = null;
                }
                cg.SetPublicKey(subjectPublicKey);

                // add extensions
                // Subject key identifier
                cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectKeyIdentifier.Id, false,
                    new SubjectKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subjectPublicKey)));

                // Basic constraints
                BasicConstraints basicConstraints = new BasicConstraints(isCA);
                if (isCA && pathLengthConstraint >= 0)
                {
                    basicConstraints = new BasicConstraints(pathLengthConstraint);
                }
                else if (!isCA && issuerCAKeyCert == null)
                {   // self-signed
                    basicConstraints = new BasicConstraints(0);
                }
                cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.BasicConstraints.Id, true, basicConstraints);

                // Authority Key identifier references the issuer cert or itself when self signed
                AsymmetricKeyParameter issuerPublicKey;
                BigInteger issuerSerialNumber;
                if (issuerCAKeyCert != null)
                {
                    issuerPublicKey = GetPublicKeyParameter(issuerCAKeyCert);
                    issuerSerialNumber = GetSerialNumber(issuerCAKeyCert);
                    if (startTime.AddMonths(lifetimeInMonths) > issuerCAKeyCert.NotAfter)
                    {
                        cg.SetNotAfter(issuerCAKeyCert.NotAfter);
                    }
                }
                else
                {
                    issuerPublicKey = subjectPublicKey;
                    issuerSerialNumber = serialNumber;
                }

                cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.AuthorityKeyIdentifier.Id, false,
                    new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(issuerPublicKey),
                        new GeneralNames(new GeneralName(issuerDN)), issuerSerialNumber));

                if (!isCA)
                {
                    // Key usage 
                    var keyUsage = KeyUsage.DataEncipherment | KeyUsage.DigitalSignature |
                            KeyUsage.NonRepudiation | KeyUsage.KeyEncipherment;
                    if (issuerCAKeyCert == null)
                    {   // only self signed certs need KeyCertSign flag.
                        keyUsage |= KeyUsage.KeyCertSign;
                    }
                    cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.KeyUsage, true,
                        new KeyUsage(keyUsage));

                    // Extended Key usage
                    cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.ExtendedKeyUsage, true,
                        new ExtendedKeyUsage(new List<DerObjectIdentifier>() {
                    new DerObjectIdentifier("1.3.6.1.5.5.7.3.1"), // server auth
                    new DerObjectIdentifier("1.3.6.1.5.5.7.3.2"), // client auth
                        }));

                    // subject alternate name
                    List<GeneralName> generalNames = new List<GeneralName>();
                    generalNames.Add(new GeneralName(GeneralName.UniformResourceIdentifier, applicationUri));
                    generalNames.AddRange(CreateSubjectAlternateNameDomains(domainNames));
                    cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName, false, new GeneralNames(generalNames.ToArray()));
                }
                else
                {
                    // Key usage CA
                    cg.AddExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.KeyUsage, true,
                        new KeyUsage(KeyUsage.CrlSign | KeyUsage.DigitalSignature | KeyUsage.KeyCertSign));
                }

                // sign certificate
                AsymmetricKeyParameter signingKey;
                if (issuerCAKeyCert != null)
                {
                    // signed by issuer
                    signingKey = GetPrivateKeyParameter(issuerCAKeyCert);
                }
                else
                {
                    // self signed
                    signingKey = subjectPrivateKey;
                }
                ISignatureFactory signatureFactory =
                            new Asn1SignatureFactory(GetRSAHashAlgorithm(hashSizeInBits), signingKey, random);
                Org.BouncyCastle.X509.X509Certificate x509 = cg.Generate(signatureFactory);

                // convert to X509Certificate2
                X509Certificate2 certificate = null;
                if (subjectPrivateKey == null)
                {
                    // create the cert without the private key
                    certificate = new X509Certificate2(x509.GetEncoded());
                }
                else
                {
                    // note: this cert has a private key!
                    certificate = CreateCertificateWithPrivateKey(x509, null, subjectPrivateKey, random);
                }

                Utils.Trace(Utils.TraceMasks.Security, "Created new certificate: {0}", certificate.Thumbprint);

                return certificate;
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

        /// <summary>
        /// Get the serial number from a certificate as BigInteger.
        /// </summary>
        private static BigInteger GetSerialNumber(X509Certificate2 certificate)
        {
            byte[] serialNumber = certificate.GetSerialNumber();
            Array.Reverse(serialNumber);
            return new BigInteger(1, serialNumber);
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

        private static Dictionary<string, X509Certificate2> m_certificates = new Dictionary<string, X509Certificate2>();
        private static object m_certificatesLock = new object();
        private static List<X509Certificate2> m_temporaryKeyContainers = new List<X509Certificate2>();
    }
}
