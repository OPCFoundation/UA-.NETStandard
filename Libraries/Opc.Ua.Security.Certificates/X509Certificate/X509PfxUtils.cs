/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Utilities to create a Pfx.
    /// </summary>
    public static class X509PfxUtils
    {
        /// <summary>
        /// The size of the block used to test a sign or encrypt operation.
        /// </summary>
        public const int TestBlockSize = 0x20;

        /// <summary>
        /// Return the key usage flags of a certificate.
        /// </summary>
        private static X509KeyUsageFlags GetKeyUsage(X509Certificate2 cert)
        {
            var allFlags = X509KeyUsageFlags.None;
            foreach (X509KeyUsageExtension ext in cert.Extensions.OfType<X509KeyUsageExtension>())
            {
                allFlags |= ext.KeyUsages;
            }
            return allFlags;
        }

        /// <summary>
        /// Verify RSA key pair of two certificates.
        /// </summary>
        public static bool VerifyRSAKeyPair(
            X509Certificate2 certWithPublicKey,
            X509Certificate2 certWithPrivateKey,
            bool throwOnError = false)
        {
            bool result = false;
            try
            {
                // verify the public and private key match
                using (RSA rsaPrivateKey = certWithPrivateKey.GetRSAPrivateKey())
                using (RSA rsaPublicKey = certWithPublicKey.GetRSAPublicKey())
                {
                    // For non RSA certificates, RSA keys are null
                    if (rsaPrivateKey != null && rsaPublicKey != null)
                    {
                        X509KeyUsageFlags keyUsage = GetKeyUsage(certWithPublicKey);
                        if ((keyUsage & X509KeyUsageFlags.DataEncipherment) != 0)
                        {
                            result = VerifyRSAKeyPairCrypt(rsaPublicKey, rsaPrivateKey);
                        }
                        else if ((keyUsage & X509KeyUsageFlags.DigitalSignature) != 0)
                        {
                            result = VerifyRSAKeyPairSign(rsaPublicKey, rsaPrivateKey);
                        }
                        else
                        {
                            throw new CryptographicException("Don't know how to verify the public/private key pair.");
                        }
                    }
                    else
                    {
                        throw new CryptographicException("The certificate does not contain a RSA public/private key pair.");
                    }
                }
            }
            catch (Exception)
            {
                if (throwOnError)
                {
                    throwOnError = false;
                    throw;
                }
            }

            if (!result && throwOnError)
            {
                throw new CryptographicException("The public/private key pair in the certficates do not match.");
            }

            return result;
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
            X509Certificate2 certificate = null;

            // We need to try MachineKeySet first as UserKeySet in combination with PersistKeySet hangs ASP.Net WebApps on Azure
            X509KeyStorageFlags[] storageFlags = {
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.MachineKeySet,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet
            };

            // try some combinations of storage flags, support is platform dependent
            foreach (var flag in storageFlags)
            {
                try
                {
                    // merge first cert with private key into X509Certificate2
                    certificate = new X509Certificate2(
                        rawData,
                        password ?? String.Empty,
                        flag);
                    // can we really access the private key?
                    if (VerifyRSAKeyPair(certificate, certificate, true))
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
            }

            if (certificate == null)
            {
                throw new NotSupportedException("Creating X509Certificate from PKCS #12 store failed", ex);
            }

            return certificate;
        }

        /// <summary>
        /// Verify a RSA key pair using a encryption.
        /// </summary>
        internal static bool VerifyRSAKeyPairCrypt(
            RSA rsaPublicKey,
            RSA rsaPrivateKey)
        {
            byte[] testBlock = new byte[TestBlockSize];
            var rnd = new Random();
            rnd.NextBytes(testBlock);
            byte[] encryptedBlock = rsaPublicKey.Encrypt(testBlock, RSAEncryptionPadding.OaepSHA1);
            byte[] decryptedBlock = rsaPrivateKey.Decrypt(encryptedBlock, RSAEncryptionPadding.OaepSHA1);
            if (decryptedBlock != null)
            {
                return testBlock.SequenceEqual(decryptedBlock);
            }
            return false;
        }

        /// <summary>
        /// Verify a RSA key pair using a signature.
        /// </summary>
        internal static bool VerifyRSAKeyPairSign(
            RSA rsaPublicKey,
            RSA rsaPrivateKey)
        {
            byte[] testBlock = new byte[TestBlockSize];
            var rnd = new Random();
            rnd.NextBytes(testBlock);
            byte[] signature = rsaPrivateKey.SignData(testBlock, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            return rsaPublicKey.VerifyData(testBlock, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }

#if ECC_SUPPORT
        /// <summary>
        /// Verify ECDsa key pair of two certificates.
        /// </summary>
        public static bool VerifyECDsaKeyPair(
            X509Certificate2 certWithPublicKey,
            X509Certificate2 certWithPrivateKey,
            bool throwOnError = false)
        {
            bool result = false;
            using (ECDsa ecdsaPublicKey = certWithPrivateKey.GetECDsaPublicKey())
            using (ECDsa ecdsaPrivateKey = certWithPublicKey.GetECDsaPrivateKey())
            {
                try
                {
                    // verify the public and private key match
                    X509KeyUsageFlags keyUsage = GetKeyUsage(certWithPublicKey);
                    if ((keyUsage & X509KeyUsageFlags.DigitalSignature) != 0)
                    {
                        result = VerifyECDsaKeyPairSign(ecdsaPublicKey, ecdsaPrivateKey);
                    }
                    else
                    {
                        if (throwOnError)
                        {
                            throw new CryptographicException("Don't know how to verify the public/private key pair.");
                        }
                    }
                }
                catch (Exception)
                {
                    if (throwOnError)
                    {
                        throwOnError = false;
                        throw;
                    }
                }
            }
            if (!result && throwOnError)
            {
                throw new CryptographicException("The public/private key pair in the certficates do not match.");
            }
            return result;
        }

        /// <summary>
        /// Verify a ECDsa key pair using a signature.
        /// </summary>
        internal static bool VerifyECDsaKeyPairSign(
            ECDsa ecdsaPublicKey,
            ECDsa ecdsaPrivateKey)
        {
            byte[] testBlock = new byte[TestBlockSize];
            var rnd = new Random();
            rnd.NextBytes(testBlock);
            byte[] signature = ecdsaPrivateKey.SignData(testBlock, HashAlgorithmName.SHA256);
            return ecdsaPublicKey.VerifyData(testBlock, signature, HashAlgorithmName.SHA256);
        }
#endif
    }
}
