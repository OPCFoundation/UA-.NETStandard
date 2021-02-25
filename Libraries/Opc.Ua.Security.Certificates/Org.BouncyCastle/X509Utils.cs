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
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
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
        #endregion
    }
}
#endif
