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

#if !NETSTANDARD2_1
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
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

    /// <summary>
    /// Wrapper for a password string.
    /// </summary>
    internal class Password
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

    /// <summary>
    /// Helpers to create certificates, CRLs and extensions.
    /// </summary>
    internal static class X509Utils
    {
        #region Internal Methods
        /// <summary>
        /// Create a Pfx blob with a private key by combining 
        /// a bouncy castle X509Certificate and a private key.
        /// </summary>
        internal static byte[] CreatePfxWithPrivateKey(
            Org.BouncyCastle.X509.X509Certificate certificate,
            string friendlyName,
            AsymmetricKeyParameter privateKey,
            string passcode,
            SecureRandom random)
        {
            // create pkcs12 store for cert and private key
            using (MemoryStream pfxData = new MemoryStream())
            {
                Pkcs12StoreBuilder builder = new Pkcs12StoreBuilder();
                builder.SetUseDerEncoding(true);
                Pkcs12Store pkcsStore = builder.Build();
                X509CertificateEntry[] chain = new X509CertificateEntry[1];
                chain[0] = new X509CertificateEntry(certificate);
                if (string.IsNullOrEmpty(friendlyName))
                {
                    friendlyName = GetCertificateCommonName(certificate);
                }
                pkcsStore.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(privateKey), chain);
                pkcsStore.Save(pfxData, passcode.ToCharArray(), random);
                return pfxData.ToArray();
            }
        }

        /// <summary>
        /// Helper to get the Bouncy Castle hash algorithm name by .NET name .
        /// </summary>
        internal static string GetRSAHashAlgorithm(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == HashAlgorithmName.SHA1)
            {
                return "SHA1WITHRSA";
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA256)
            {
                return "SHA256WITHRSA";
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA384)
            {
                return "SHA384WITHRSA";
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA512)
            {
                return "SHA512WITHRSA";
            }
            throw new CryptographicException($"The hash algorithm {hashAlgorithmName} is not supported");
        }

        /// <summary>
        /// Get public key parameters from a X509Certificate2
        /// </summary>
        internal static RsaKeyParameters GetPublicKeyParameter(X509Certificate2 certificate)
        {
            RSA rsa = null;
            try
            {
                rsa = certificate.GetRSAPublicKey();
                return GetPublicKeyParameter(rsa);
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Get public key parameters from a RSA.
        /// </summary>
        internal static RsaKeyParameters GetPublicKeyParameter(RSA rsa)
        {
            RSAParameters rsaParams = rsa.ExportParameters(false);
            return new RsaKeyParameters(
                false,
                new BigInteger(1, rsaParams.Modulus),
                new BigInteger(1, rsaParams.Exponent));
        }

        /// <summary>
        /// Get private key parameters from a X509Certificate2.
        /// The private key must be exportable.
        /// </summary>
        internal static RsaPrivateCrtKeyParameters GetPrivateKeyParameter(X509Certificate2 certificate)
        {
            RSA rsa = null;
            try
            {
                // try to get signing/private key from certificate passed in
                rsa = certificate.GetRSAPrivateKey();
                return GetPrivateKeyParameter(rsa);
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Get private key parameters from a RSA private key.
        /// The private key must be exportable.
        /// </summary>
        internal static RsaPrivateCrtKeyParameters GetPrivateKeyParameter(RSA rsa)
        {
            RSAParameters rsaParams = rsa.ExportParameters(true);
            return new RsaPrivateCrtKeyParameters(
                new BigInteger(1, rsaParams.Modulus),
                new BigInteger(1, rsaParams.Exponent),
                new BigInteger(1, rsaParams.D),
                new BigInteger(1, rsaParams.P),
                new BigInteger(1, rsaParams.Q),
                new BigInteger(1, rsaParams.DP),
                new BigInteger(1, rsaParams.DQ),
                new BigInteger(1, rsaParams.InverseQ));
        }


        /// <summary>
        /// Get the serial number from a certificate as BigInteger.
        /// </summary>
        internal static BigInteger GetSerialNumber(X509Certificate2 certificate)
        {
            byte[] serialNumber = certificate.GetSerialNumber();
            return new BigInteger(1, serialNumber.Reverse().ToArray());
        }

        /// <summary>
        /// Read the Common Name from a certificate.
        /// </summary>
        internal static string GetCertificateCommonName(Org.BouncyCastle.X509.X509Certificate certificate)
        {
            var subjectDN = certificate.SubjectDN.GetValueList(X509Name.CN);
            if (subjectDN.Count > 0)
            {
                return subjectDN[0].ToString();
            }
            return string.Empty;
        }


        /// <summary>
        /// Read the Crl number from a X509Crl.
        /// </summary>
        internal static BigInteger GetCrlNumber(X509Crl crl)
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
        internal static Asn1Object GetExtensionValue(IX509Extension extension, DerObjectIdentifier oid)
        {
            Asn1OctetString asn1Octet = extension.GetExtensionValue(oid);
            if (asn1Octet != null)
            {
                return Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(asn1Octet);
            }
            return null;
        }
        #endregion
    }
}
#endif
