/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Text;
using Opc.Ua;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Asn1;

namespace Opc.Ua
{
    /// <summary>
    /// Secure .Net Core Random Number generator wrapper for Bounce Castle.
    /// Creates an instance of RNGCryptoServiceProvider or an OpenSSL based version on other OS.
    /// </summary>
    public class CertificateFactoryRandomGenerator : IRandomGenerator, IDisposable
    {
        RandomNumberGenerator m_prg;

        public CertificateFactoryRandomGenerator()
        {
            m_prg = RandomNumberGenerator.Create();
        }

        public void Dispose()
        {
            m_prg.Dispose();
        }

        /// <summary>Add more seed material to the generator. Not needed here.</summary>
        public void AddSeedMaterial(byte[] seed) { }

        /// <summary>Add more seed material to the generator. Not needed here.</summary>
        public void AddSeedMaterial(long seed) { }

        /// <summary>Fill byte array with random values.</summary>
        /// <param name="bytes">Array to be filled.</param>
        public void NextBytes(byte[] bytes)
        {
            m_prg.GetBytes(bytes);
        }

        /// <summary>Fill byte array with random values.</summary>
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
}


/// <summary>
/// Creates a manages certificates.
/// </summary>
public class CertificateFactory
{
    #region Public Constants
    /// <summary>
    /// The default certificate factory security parameter.
    /// </summary>
    public const ushort defaultKeySize = 2048;
    public const ushort defaultHashSize = 256;
    public const ushort defaultLifeTime = 12;
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
    /// This function is necessary because all private keys used for cryptography operations must be in a key conatiner. 
    /// Private keys stored in a PFX file have no key conatiner by default.
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
    /// <param name="applicationUri">The application uri (created if not specified).</param>
    /// <param name="applicationName">Name of the application (optional if subjectName is specified).</param>
    /// <param name="subjectName">The subject used to create the certificate (optional if applicationName is specified).</param>
    /// <param name="domainNames">The domain names that can be used to access the server machine (defaults to local computer name if not specified).</param>
    /// <param name="keySize">Size of the key (1024, 2048 or 4096).</param>
    /// <param name="lifetimeInMonths">The lifetime of the key in months.</param>
    /// <param name="hashSizeInBits">The hash size in bits.</param>
    /// <returns>The certificate with a private key.</returns>
    public static X509Certificate2 CreateCertificate(
        string storeType,
        string storePath,
        string applicationUri,
        string applicationName,
        string subjectName = null,
        IList<string> serverDomainNames = null,
        ushort keySize = defaultKeySize,
        ushort lifetimeInMonths = defaultLifeTime,
        ushort hashSizeInBits = defaultHashSize
        )
    {
        return CreateCertificate(
            storeType,
            storePath,
            null,
            applicationUri,
            applicationName,
            subjectName,
            serverDomainNames,
            keySize,
            DateTime.UtcNow - TimeSpan.FromDays(1),
            lifetimeInMonths,
            hashSizeInBits,
            false,
            null
            );
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
        bool isCA,
        X509Certificate2 issuerCAKeyCert)
    {
        if (issuerCAKeyCert != null)
        {
            if (!issuerCAKeyCert.HasPrivateKey)
            {
                throw new NotSupportedException("Cannot sign with a CA certificate without a private key.");
            }

            throw new NotSupportedException("Signing with an issuer CA certificate is currently unsupported.");
        }

        // set default values.
        X509Name subjectDN = SetSuitableDefaults(
            ref applicationUri,
            ref applicationName,
            ref subjectName,
            ref domainNames,
            ref keySize,
            ref lifetimeInMonths,
            isCA);

        using (var cfrg = new CertificateFactoryRandomGenerator())
        {
            // cert generators
            SecureRandom random = new SecureRandom(cfrg);
            X509V3CertificateGenerator cg = new X509V3CertificateGenerator();

            // Serial Number
            BigInteger serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            cg.SetSerialNumber(serialNumber);

            // self signed 
            X509Name issuerDN = subjectDN;
            cg.SetIssuerDN(issuerDN);
            cg.SetSubjectDN(subjectDN);

            // valid for
            cg.SetNotBefore(startTime);
            cg.SetNotAfter(startTime.AddMonths(lifetimeInMonths));

            // Private/Public Key
            AsymmetricCipherKeyPair subjectKeyPair;
            var keyGenerationParameters = new KeyGenerationParameters(random, keySize);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            subjectKeyPair = keyPairGenerator.GenerateKeyPair();
            cg.SetPublicKey(subjectKeyPair.Public);

            // add extensions
            // Subject key identifier
            cg.AddExtension(X509Extensions.SubjectKeyIdentifier.Id, false,
                new SubjectKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subjectKeyPair.Public)));

            // Basic constraints
            cg.AddExtension(X509Extensions.BasicConstraints.Id, true, new BasicConstraints(isCA));

            // Authority Key identifier
            var issuerKeyPair = subjectKeyPair;
            var issuerSerialNumber = serialNumber;
            cg.AddExtension(X509Extensions.AuthorityKeyIdentifier.Id, false,
                new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subjectKeyPair.Public),
                    new GeneralNames(new GeneralName(issuerDN)), issuerSerialNumber));

            if (!isCA)
            {
                // Key usage 
                cg.AddExtension(X509Extensions.KeyUsage, true,
                    new KeyUsage(KeyUsage.DataEncipherment | KeyUsage.DigitalSignature |
                        KeyUsage.NonRepudiation | KeyUsage.KeyCertSign | KeyUsage.KeyEncipherment));

                // Extended Key usage
                cg.AddExtension(X509Extensions.ExtendedKeyUsage, true,
                    new ExtendedKeyUsage(new List<DerObjectIdentifier>() {
                    new DerObjectIdentifier("1.3.6.1.5.5.7.3.1"), // server auth
                    new DerObjectIdentifier("1.3.6.1.5.5.7.3.2"), // client auth
                    }));

                // subject alternate name
                cg.AddExtension(X509Extensions.SubjectAlternativeName, false,
                    new GeneralNames(new GeneralName[] {
                    new GeneralName(GeneralName.UniformResourceIdentifier, applicationUri),
                    new GeneralName(GeneralName.DnsName, domainNames[0])}));
            }
            else
            {
                // Key usage CA
                cg.AddExtension(X509Extensions.KeyUsage, true,
                    new KeyUsage(KeyUsage.CrlSign | KeyUsage.DigitalSignature | KeyUsage.KeyCertSign));
            }

            // sign certificate
            ISignatureFactory signatureFactory =
                new Asn1SignatureFactory((hashSizeInBits < 256) ? "SHA1WITHRSA" : "SHA256WITHRSA", subjectKeyPair.Private, random);
            Org.BouncyCastle.X509.X509Certificate x509 = cg.Generate(signatureFactory);

            // create pkcs12 store for cert and private key
            X509Certificate2 certificate = null;
            using (MemoryStream pfxData = new MemoryStream())
            {
                Pkcs12Store pkcsStore = new Pkcs12StoreBuilder().Build();
                X509CertificateEntry[] chain = new X509CertificateEntry[1];
                string passcode = Guid.NewGuid().ToString();
                chain[0] = new X509CertificateEntry(x509);
                pkcsStore.SetKeyEntry(applicationName, new AsymmetricKeyEntry(subjectKeyPair.Private), chain);
                pkcsStore.Save(pfxData, passcode.ToCharArray(), random);

                // merge into X509Certificate2
                certificate = CreateCertificateFromPKCS12(pfxData.ToArray(), passcode);
            }

            Utils.Trace(Utils.TraceMasks.Security, "Created new certificate: {0}", certificate.Thumbprint);

            // add cert to the store.
            if (!String.IsNullOrEmpty(storePath))
            {
                ICertificateStore store = null;
                if (storeType == CertificateStoreType.X509Store)
                {
                    store = new X509CertificateStore();
                }
                else if (storeType == CertificateStoreType.Directory)
                {
                    store = new DirectoryCertificateStore();
                }
                else
                {
                    throw new ArgumentException("Invalid store type");
                }

                store.Open(storePath);
                store.Add(certificate, password);
                store.Close();
                store.Dispose();
            }

            // note: this cert has a private key!
            return certificate;
        }
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
                    (password == null) ? String.Empty : password,
                    storageFlags[flagsRetryCounter]);
                // can we really access the private key?
                using (RSA rsa = certificate.GetRSAPrivateKey()) { }
            }
            catch (Exception e)
            {
                ex = e;
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
    #endregion
    #region Private Methods
    /// <summary>
    /// Sets the parameters to suitable defaults.
    /// </summary>
    private static X509Name SetSuitableDefaults(
        ref string applicationUri,
        ref string applicationName,
        ref string subjectName,
        ref IList<String> domainNames,
        ref ushort keySize,
        ref ushort lifetimeInMonths,
        bool isCA)
    {
        // enforce recommended keysize unless lower value is enforced.
        if (keySize < 1024)
        {
            keySize = defaultKeySize;
        }

        if (keySize % 1024 != 0)
        {
            throw new ArgumentNullException("keySize", "KeySize must be a multiple of 1024.");
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
            subjectNameEntries = Utils.ParseDistinguishedName(subjectName);
        }

        // check the application name.
        if (String.IsNullOrEmpty(applicationName))
        {
            if (subjectNameEntries == null)
            {
                throw new ArgumentNullException("applicationName", "Must specify a applicationName or a subjectName.");
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
            throw new ArgumentNullException("applicationName", "Must specify a applicationName or a subjectName.");
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
            throw new ArgumentNullException("applicationUri", "Must specify a valid URL.");
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
            if (!subjectName.Contains("DC="))
            {
                subjectName += Utils.Format(", DC={0}", domainNames[0]);
            }
            else
            {
                subjectName = Utils.ReplaceDCLocalhost(subjectName, domainNames[0]);
            }
        }

        // Convert a few known entries named different in .Net and Bouncy Castle
        subjectName = subjectName.Replace("S=", "ST=");

        return new X509Name(true, subjectName);
    }
    #endregion

    private static Dictionary<string, X509Certificate2> m_certificates = new Dictionary<string, X509Certificate2>();
    private static List<X509Certificate2> m_temporaryKeyContainers = new List<X509Certificate2>();
}

