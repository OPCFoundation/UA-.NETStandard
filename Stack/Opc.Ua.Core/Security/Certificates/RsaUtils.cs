/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Defines internal functions to implement RSA cryptography.
    /// </summary>
    internal static class RsaUtils
    {
        public enum Padding
        {
            Pkcs1,
            OaepSHA1,
            OaepSHA256
        }

        internal static RSAEncryptionPadding GetRSAEncryptionPadding(Padding padding)
        {
            switch (padding)
            {
                case Padding.Pkcs1:
                    return RSAEncryptionPadding.Pkcs1;
                case Padding.OaepSHA1:
                    return RSAEncryptionPadding.OaepSHA1;
                case Padding.OaepSHA256:
                    return RSAEncryptionPadding.OaepSHA256;
                default:
                    throw ServiceResultException.Unexpected($"Unexpected Padding {padding}");
            }
        }

        /// <summary>
        /// Return the plaintext block size for RSA OAEP encryption.
        /// </summary>
        internal static int GetPlainTextBlockSize(
            X509Certificate2 encryptingCertificate,
            Padding padding)
        {
            using RSA rsa = encryptingCertificate.GetRSAPublicKey();
            return GetPlainTextBlockSize(rsa, padding);
        }

        /// <summary>
        /// Return the plaintext block size for RSA OAEP encryption.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static int GetPlainTextBlockSize(RSA rsa, Padding padding)
        {
            if (rsa != null)
            {
                switch (padding)
                {
                    case Padding.Pkcs1:
                        return (rsa.KeySize / 8) - 11;
                    case Padding.OaepSHA1:
                        return (rsa.KeySize / 8) - 42;
                    case Padding.OaepSHA256:
                        return (rsa.KeySize / 8) - 66;
                    default:
                        throw ServiceResultException.Unexpected($"Unexpected Padding {padding}");
                }
            }
            return -1;
        }

        /// <summary>
        /// Return the ciphertext block size for RSA OAEP encryption.
        /// </summary>
        internal static int GetCipherTextBlockSize(X509Certificate2 encryptingCertificate)
        {
            using RSA rsa = encryptingCertificate.GetRSAPublicKey();
            return GetCipherTextBlockSize(rsa);
        }

        /// <summary>
        /// Return the ciphertext block size for RSA OAEP encryption.
        /// </summary>
        internal static int GetCipherTextBlockSize(RSA rsa)
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
        /// <exception cref="ServiceResultException"></exception>
        internal static int GetSignatureLength(X509Certificate2 signingCertificate)
        {
            using RSA rsa =
                signingCertificate.GetRSAPublicKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");
            return rsa.KeySize / 8;
        }

        /// <summary>
        /// Computes a RSA signature.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static byte[] Rsa_Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding rsaSignaturePadding)
        {
            // extract the private key.
            using RSA rsa =
                signingCertificate.GetRSAPrivateKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No private key for certificate.");

            // create the signature.
            return rsa.SignData(
                dataToSign.Array,
                dataToSign.Offset,
                dataToSign.Count,
                hashAlgorithm,
                rsaSignaturePadding);
        }

        /// <summary>
        /// Verifies a RSA signature.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static bool Rsa_Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding rsaSignaturePadding)
        {
            // extract the public key.
            using RSA rsa =
                signingCertificate.GetRSAPublicKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");

            // verify signature.
            return rsa.VerifyData(
                dataToVerify.Array,
                dataToVerify.Offset,
                dataToVerify.Count,
                signature,
                hashAlgorithm,
                rsaSignaturePadding);
        }

        /// <summary>
        /// Encrypts the data using RSA encryption.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static byte[] Encrypt(
            ReadOnlySpan<byte> dataToEncrypt,
            X509Certificate2 encryptingCertificate,
            Padding padding,
            ILogger logger)
        {
            using RSA rsa =
                encryptingCertificate.GetRSAPublicKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");

            int plaintextBlockSize = GetPlainTextBlockSize(rsa, padding);
            int blockCount = ((dataToEncrypt.Length + 4) / plaintextBlockSize) + 1;
            int plainTextSize = blockCount * plaintextBlockSize;
            int cipherTextSize = blockCount * GetCipherTextBlockSize(rsa);

            byte[] plainText = new byte[plainTextSize];

            // encode length.
            plainText[0] = (byte)(0x000000FF & dataToEncrypt.Length);
            plainText[1] = (byte)((0x0000FF00 & dataToEncrypt.Length) >> 8);
            plainText[2] = (byte)((0x00FF0000 & dataToEncrypt.Length) >> 16);
            plainText[3] = (byte)((0xFF000000 & dataToEncrypt.Length) >> 24);

            // copy data.
            dataToEncrypt.CopyTo(plainText.AsSpan(4, dataToEncrypt.Length));

            byte[] buffer = new byte[cipherTextSize];
            ArraySegment<byte> cipherText = Encrypt(
                new ArraySegment<byte>(plainText),
                rsa,
                padding,
                new ArraySegment<byte>(buffer),
                logger);
            System.Diagnostics.Debug.Assert(cipherText.Count == buffer.Length);
            Array.Clear(plainText, 0, plainText.Length);

            return buffer;
        }

        /// <summary>
        /// Encrypts the data using RSA PKCS#1 v1.5 or OAEP encryption.
        /// </summary>
        private static ArraySegment<byte> Encrypt(
            ArraySegment<byte> dataToEncrypt,
            RSA rsa,
            Padding padding,
            ArraySegment<byte> outputBuffer,
            ILogger logger)
        {
            int inputBlockSize = GetPlainTextBlockSize(rsa, padding);
            int outputBlockSize = GetCipherTextBlockSize(rsa);

            // verify the input data is the correct block size.
            if (dataToEncrypt.Count % inputBlockSize != 0)
            {
                logger.LogError(
                    "Message is not an integral multiple of the block size. Length = {Length}, BlockSize = {BlockSize}.",
                    dataToEncrypt.Count,
                    inputBlockSize);
            }

            byte[] encryptedBuffer = outputBuffer.Array;
            RSAEncryptionPadding rsaPadding = GetRSAEncryptionPadding(padding);

            using (var ostrm = new MemoryStream(
                encryptedBuffer,
                outputBuffer.Offset,
                outputBuffer.Count))
            {
                // encrypt body.
                byte[] input = new byte[inputBlockSize];

                for (int ii = dataToEncrypt.Offset;
                    ii < dataToEncrypt.Offset + dataToEncrypt.Count;
                    ii += inputBlockSize)
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
                dataToEncrypt.Count / inputBlockSize * outputBlockSize);
        }

        /// <summary>
        /// Decrypts the data using RSA encryption.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static byte[] Decrypt(
            ArraySegment<byte> dataToDecrypt,
            X509Certificate2 encryptingCertificate,
            Padding padding,
            ILogger logger)
        {
            using RSA rsa =
                encryptingCertificate.GetRSAPrivateKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No private key for certificate.");

            int plainTextSize = dataToDecrypt.Count / GetCipherTextBlockSize(rsa);
            plainTextSize *= GetPlainTextBlockSize(encryptingCertificate, padding);

            byte[] buffer = new byte[plainTextSize];
            ArraySegment<byte> plainText = Decrypt(
                dataToDecrypt,
                rsa,
                padding,
                new ArraySegment<byte>(buffer),
                logger);
            System.Diagnostics.Debug.Assert(plainText.Count == buffer.Length);

            // decode length.
            int length = 0;

            length += plainText.Array[plainText.Offset + 0];
            length += plainText.Array[plainText.Offset + 1] << 8;
            length += plainText.Array[plainText.Offset + 2] << 16;
            length += plainText.Array[plainText.Offset + 3] << 24;

            if (length > (plainText.Count - plainText.Offset - 4))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEndOfStream,
                    "Could not decrypt data. Invalid total length.");
            }

            byte[] decryptedData = new byte[length];
            Array.Copy(plainText.Array, plainText.Offset + 4, decryptedData, 0, length);
            Array.Clear(buffer, 0, buffer.Length);

            return decryptedData;
        }

        /// <summary>
        /// Decrypts the message using RSA encryption.
        /// </summary>
        private static ArraySegment<byte> Decrypt(
            ArraySegment<byte> dataToDecrypt,
            RSA rsa,
            Padding padding,
            ArraySegment<byte> outputBuffer,
            ILogger logger)
        {
            int inputBlockSize = GetCipherTextBlockSize(rsa);
            int outputBlockSize = GetPlainTextBlockSize(rsa, padding);

            // verify the input data is the correct block size.
            if (dataToDecrypt.Count % inputBlockSize != 0)
            {
                logger.LogError(
                    "Message is not an integral multiple of the block size. Length = {Length}, BlockSize = {BlockSize}.",
                    dataToDecrypt.Count,
                    inputBlockSize);
            }

            byte[] decryptedBuffer = outputBuffer.Array;
            RSAEncryptionPadding rsaPadding = GetRSAEncryptionPadding(padding);

            using (var ostrm = new MemoryStream(
                decryptedBuffer,
                outputBuffer.Offset,
                outputBuffer.Count))
            {
                // decrypt body.
                byte[] input = new byte[inputBlockSize];
                for (int ii = dataToDecrypt.Offset;
                    ii < dataToDecrypt.Offset + dataToDecrypt.Count;
                    ii += inputBlockSize)
                {
                    Array.Copy(dataToDecrypt.Array, ii, input, 0, input.Length);
                    byte[] plainText = rsa.Decrypt(input, rsaPadding);
                    ostrm.Write(plainText, 0, plainText.Length);
                }
            }

            // return buffers.
            return new ArraySegment<byte>(
                decryptedBuffer,
                outputBuffer.Offset,
                dataToDecrypt.Count / inputBlockSize * outputBlockSize);
        }

        /// <summary>
        /// Helper to test for RSASignaturePadding.Pss support, some platforms do not support it.
        /// </summary>
        internal static bool TryVerifyRSAPssSign(RSA publicKey, RSA privateKey)
        {
            try
            {
                var randomSource = new Test.RandomSource();
                const int blockSize = 0x10;
                byte[] testBlock = new byte[blockSize];
                randomSource.NextBytes(testBlock, 0, blockSize);
                byte[] signature = privateKey.SignData(
                    testBlock,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss);
                return publicKey.VerifyData(
                    testBlock,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Lazy helper to allow runtime to check for Pss support.
        /// </summary>
        internal static readonly Lazy<bool> IsSupportingRSAPssSign = new(() =>
        {
#if NETFRAMEWORK
            // The Pss check returns false on .Net4.6/4.7, although it is always supported with certs.
            // but not supported with Mono
            return !Utils.IsRunningOnMono();
#else
            using var rsa = RSA.Create();
            return TryVerifyRSAPssSign(rsa, rsa);
#endif
        });
    }
}
