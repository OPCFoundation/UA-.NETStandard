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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Defines internal functions to implement RSA cryptography.
    /// </summary>
    internal static class RsaUtils
    {
        #region Public Enum
        public enum Padding
        {
            Pkcs1,
            OaepSHA1,
            OaepSHA256
        };

        internal static RSAEncryptionPadding GetRSAEncryptionPadding(Padding padding)
        {
            switch (padding)
            {
                case Padding.Pkcs1: return RSAEncryptionPadding.Pkcs1;
                case Padding.OaepSHA1: return RSAEncryptionPadding.OaepSHA1;
                case Padding.OaepSHA256: return RSAEncryptionPadding.OaepSHA256;
            }
            throw new ServiceResultException("Invalid Padding");
        }
        #endregion
        #region Public Methods
        /// <summary>
        /// Return the plaintext block size for RSA OAEP encryption.
        /// </summary>
        internal static int GetPlainTextBlockSize(X509Certificate2 encryptingCertificate, Padding padding)
        {
            RSA rsa = null;
            try
            {
                rsa = encryptingCertificate.GetRSAPublicKey();
                return GetPlainTextBlockSize(rsa, padding);
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Return the plaintext block size for RSA OAEP encryption.
        /// </summary>
        internal static int GetPlainTextBlockSize(RSA rsa, Padding padding)
        {
            if (rsa != null)
            {
                switch (padding)
                {
                    case Padding.Pkcs1: return rsa.KeySize / 8 - 11;
                    case Padding.OaepSHA1: return rsa.KeySize / 8 - 42;
                    case Padding.OaepSHA256: return rsa.KeySize / 8 - 66;
                }
            }
            return -1;
        }

        /// <summary>
        /// Return the ciphertext block size for RSA OAEP encryption.
        /// </summary>
        internal static int GetCipherTextBlockSize(X509Certificate2 encryptingCertificate, Padding padding)
        {
            RSA rsa = null;
            try
            {
                rsa = encryptingCertificate.GetRSAPublicKey();
                return GetCipherTextBlockSize(rsa, padding);
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Return the ciphertext block size for RSA OAEP encryption.
        /// </summary>
        internal static int GetCipherTextBlockSize(RSA rsa, Padding padding)
        {
            if (rsa != null)
            {
                return rsa.KeySize / 8;
            }
            return -1;
        }

        /// <summary>
        /// Returns the length of a RSA PKCS#1 v1.5 signature of a digest.
        /// </summary>
        internal static int GetSignatureLength(X509Certificate2 signingCertificate)
        {
            RSA rsa = null;
            try
            {
                rsa = signingCertificate.GetRSAPublicKey();
                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
                }

                return rsa.KeySize / 8;
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Computes a RSA signature.
        /// </summary>
        internal static byte[] Rsa_Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding rsaSignaturePadding)
        {
            RSA rsa = null;
            try
            {
                // extract the private key.
                rsa = signingCertificate.GetRSAPrivateKey();
                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate.");
                }

                // create the signature.
                return rsa.SignData(dataToSign.Array, dataToSign.Offset, dataToSign.Count, hashAlgorithm, rsaSignaturePadding);
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Verifies a RSA signature.
        /// </summary>
        internal static bool Rsa_Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding rsaSignaturePadding)
        {
            RSA rsa = null;
            try
            {
                // extract the public key.
                rsa = signingCertificate.GetRSAPublicKey();
                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
                }

                // verify signature.
                return rsa.VerifyData(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, signature, hashAlgorithm, rsaSignaturePadding);
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Encrypts the data using RSA encryption.
        /// </summary>
        internal static byte[] Encrypt(
            byte[] dataToEncrypt,
            X509Certificate2 encryptingCertificate,
            Padding padding)
        {
            RSA rsa = null;
            try
            {

                rsa = encryptingCertificate.GetRSAPublicKey();
                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
                }

                int plaintextBlockSize = GetPlainTextBlockSize(rsa, padding);
                int blockCount = ((dataToEncrypt.Length + 4) / plaintextBlockSize) + 1;
                int plainTextSize = blockCount * plaintextBlockSize;
                int cipherTextSize = blockCount * GetCipherTextBlockSize(rsa, padding);

                byte[] plainText = new byte[plainTextSize];

                // encode length.
                plainText[0] = (byte)((0x000000FF & dataToEncrypt.Length));
                plainText[1] = (byte)((0x0000FF00 & dataToEncrypt.Length) >> 8);
                plainText[2] = (byte)((0x00FF0000 & dataToEncrypt.Length) >> 16);
                plainText[3] = (byte)((0xFF000000 & dataToEncrypt.Length) >> 24);

                // copy data.
                Array.Copy(dataToEncrypt, 0, plainText, 4, dataToEncrypt.Length);

                byte[] buffer = new byte[cipherTextSize];
                ArraySegment<byte> cipherText = Encrypt(new ArraySegment<byte>(plainText), rsa, padding, new ArraySegment<byte>(buffer));
                System.Diagnostics.Debug.Assert(cipherText.Count == buffer.Length);

                return buffer;
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Encrypts the data using RSA PKCS#1 v1.5 or OAEP encryption.
        /// </summary>
        private static ArraySegment<byte> Encrypt(
            ArraySegment<byte> dataToEncrypt,
            RSA rsa,
            Padding padding,
            ArraySegment<byte> outputBuffer)
        {
            int inputBlockSize = GetPlainTextBlockSize(rsa, padding);
            int outputBlockSize = GetCipherTextBlockSize(rsa, padding);

            // verify the input data is the correct block size.
            if (dataToEncrypt.Count % inputBlockSize != 0)
            {
                Utils.Trace("Message is not an integral multiple of the block size. Length = {0}, BlockSize = {1}.", dataToEncrypt.Count, inputBlockSize);
            }

            byte[] encryptedBuffer = outputBuffer.Array;
            RSAEncryptionPadding rsaPadding = GetRSAEncryptionPadding(padding);

            using (MemoryStream ostrm = new MemoryStream(
                encryptedBuffer,
                outputBuffer.Offset,
                outputBuffer.Count))
            {

                // encrypt body.
                byte[] input = new byte[inputBlockSize];

                for (int ii = dataToEncrypt.Offset; ii < dataToEncrypt.Offset + dataToEncrypt.Count; ii += inputBlockSize)
                {
                    Array.Copy(dataToEncrypt.Array, ii, input, 0, input.Length);
                    byte[] cipherText = rsa.Encrypt(input, rsaPadding);
                    ostrm.Write(cipherText, 0, cipherText.Length);
                }
            }

            // return buffer
            return new ArraySegment<byte>(
                encryptedBuffer,
                outputBuffer.Offset,
                (dataToEncrypt.Count / inputBlockSize) * outputBlockSize);
        }

        /// <summary>
        /// Decrypts the data using RSA encryption.
        /// </summary>
        internal static byte[] Decrypt(
            ArraySegment<byte> dataToDecrypt,
            X509Certificate2 encryptingCertificate,
            Padding padding)
        {
            RSA rsa = null;
            try
            {
                rsa = encryptingCertificate.GetRSAPrivateKey();
                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate.");
                }

                int plainTextSize = dataToDecrypt.Count / GetCipherTextBlockSize(rsa, padding);
                plainTextSize *= GetPlainTextBlockSize(encryptingCertificate, padding);

                byte[] buffer = new byte[plainTextSize];
                ArraySegment<byte> plainText = Decrypt(dataToDecrypt, rsa, padding, new ArraySegment<byte>(buffer));
                System.Diagnostics.Debug.Assert(plainText.Count == buffer.Length);

                // decode length.
                int length = 0;

                length += (((int)plainText.Array[plainText.Offset + 0]));
                length += (((int)plainText.Array[plainText.Offset + 1]) << 8);
                length += (((int)plainText.Array[plainText.Offset + 2]) << 16);
                length += (((int)plainText.Array[plainText.Offset + 3]) << 24);

                if (length > (plainText.Count - plainText.Offset - 4))
                {
                    throw ServiceResultException.Create(StatusCodes.BadEndOfStream, "Could not decrypt data. Invalid total length.");
                }

                byte[] decryptedData = new byte[length];
                Array.Copy(plainText.Array, plainText.Offset + 4, decryptedData, 0, length);

                return decryptedData;
            }
            finally
            {
                RsaUtils.RSADispose(rsa);
            }
        }

        /// <summary>
        /// Decrypts the message using RSA encryption.
        /// </summary>
        private static ArraySegment<byte> Decrypt(
            ArraySegment<byte> dataToDecrypt,
            RSA rsa,
            Padding padding,
            ArraySegment<byte> outputBuffer)
        {
            int inputBlockSize = GetCipherTextBlockSize(rsa, padding);
            int outputBlockSize = GetPlainTextBlockSize(rsa, padding);

            // verify the input data is the correct block size.
            if (dataToDecrypt.Count % inputBlockSize != 0)
            {
                Utils.Trace("Message is not an integral multiple of the block size. Length = {0}, BlockSize = {1}.", dataToDecrypt.Count, inputBlockSize);
            }

            byte[] decryptedBuffer = outputBuffer.Array;
            RSAEncryptionPadding rsaPadding = GetRSAEncryptionPadding(padding);

            using (MemoryStream ostrm = new MemoryStream(
                decryptedBuffer,
                outputBuffer.Offset,
                outputBuffer.Count))
            {

                // decrypt body.
                byte[] input = new byte[inputBlockSize];
                for (int ii = dataToDecrypt.Offset; ii < dataToDecrypt.Offset + dataToDecrypt.Count; ii += inputBlockSize)
                {
                    Array.Copy(dataToDecrypt.Array, ii, input, 0, input.Length);
                    byte[] plainText = rsa.Decrypt(input, rsaPadding);
                    ostrm.Write(plainText, 0, plainText.Length);
                }
            }

            // return buffers.
            return new ArraySegment<byte>(decryptedBuffer, outputBuffer.Offset, (dataToDecrypt.Count / inputBlockSize) * outputBlockSize);
        }

        /// <summary>
        /// Helper to test for RSASignaturePadding.Pss support, some platforms do not support it.
        /// </summary>
        internal static bool TryVerifyRSAPssSign(RSA publicKey, RSA privateKey)
        {
            try
            {
                Opc.Ua.Test.RandomSource randomSource = new Opc.Ua.Test.RandomSource();
                int blockSize = 0x10;
                byte[] testBlock = new byte[blockSize];
                randomSource.NextBytes(testBlock, 0, blockSize);
                byte[] signature = privateKey.SignData(testBlock, HashAlgorithmName.SHA1, RSASignaturePadding.Pss);
                return publicKey.VerifyData(testBlock, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pss);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Lazy helper to allow runtime to check for Pss support.
        /// </summary>
        internal static readonly Lazy<bool> IsSupportingRSAPssSign = new Lazy<bool>(() => {
#if NET46 || NET461 || NET462 || NET47
            // The Pss check returns false on .Net4.6/4.7, although it is always supported with certs.
            // but not supported with Mono
            return !Utils.IsRunningOnMono();
#else
            using (var rsa = RSA.Create())
            {
                return RsaUtils.TryVerifyRSAPssSign(rsa, rsa);
            }
#endif
        });

        /// <summary>
        /// Dispose RSA object only if not running on Mono runtime.
        /// Workaround due to a Mono bug in the X509Certificate2 implementation of RSA.
        /// see also: https://github.com/mono/mono/issues/6306
        /// On Mono GetRSAPrivateKey/GetRSAPublickey returns a reference instead of a disposable object.
        /// Calling Dispose on RSA makes the X509Certificate2 keys unusable on Mono.
        /// Only call dispose when using .Net and .Net Core runtimes.
        /// </summary>
        /// <param name="rsa">RSA object returned by GetRSAPublicKey/GetRSAPrivateKey</param>
        internal static void RSADispose(RSA rsa)
        {
            if (rsa != null &&
                !Utils.IsRunningOnMono())
            {
                rsa.Dispose();
            }
        }
        #endregion
    }
}
