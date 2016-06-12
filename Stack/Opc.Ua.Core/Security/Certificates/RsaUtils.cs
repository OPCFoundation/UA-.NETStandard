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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Defines functions to implement RSA cryptography.
    /// </summary>
    public static class RsaUtils
    {
        #region Public Methods
        /// <summary>
        /// Return the plaintext block size for RSA OAEP encryption.
        /// </summary>
        public static int GetPlainTextBlockSize(X509Certificate2 encryptingCertificate, bool useOaep)
        {
            using (RSA rsa = encryptingCertificate.GetRSAPublicKey())
            {
                if (rsa != null)
                {
                    if (useOaep)
                    {
                        return rsa.KeySize / 8 - 42;
                    }
                    else
                    {
                        return rsa.KeySize / 8 - 11;
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Return the ciphertext block size for RSA OAEP encryption.
        /// </summary>
        public static int GetCipherTextBlockSize(X509Certificate2 encryptingCertificate, bool useOaep)
        {
            using (RSA rsa = encryptingCertificate.GetRSAPublicKey())
            {
                if (rsa != null)
                {
                    return rsa.KeySize / 8;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns the length of a RSA PKCS#1 v1.5 signature of a SHA1 digest.
        /// </summary>
        public static int RsaPkcs15Sha1_GetSignatureLength(X509Certificate2 signingCertificate)
        {
            using (RSA rsa = signingCertificate.GetRSAPublicKey())
            {
                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
                }

                return rsa.KeySize / 8;
            }
        }

        /// <summary>
        /// Computes an RSA/SHA1 PKCS#1 v1.5 signature.
        /// </summary>
        public static byte[] RsaPkcs15Sha1_Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate)
        {
            // extract the private key.
            using (RSA rsa = signingCertificate.GetRSAPrivateKey())
            {

                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate.");
                }

                // create the signature.
                return rsa.SignData(dataToSign.Array, dataToSign.Offset, dataToSign.Count, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            }
        }

        /// <summary>
        /// Verifies an RSA/SHA1 PKCS#1 v1.5 signature.
        /// </summary>
        public static bool RsaPkcs15Sha1_Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate)
        {
            // extract the private key.
            using (RSA rsa = signingCertificate.GetRSAPublicKey())
            {

                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
                }

                // verify signature.
                return rsa.VerifyData(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            }
        }

        /// <summary>
        /// Encrypts the data using RSA PKCS#1 v1.5 encryption.
        /// </summary>
        public static byte[] Encrypt(
            byte[] dataToEncrypt,
            X509Certificate2 encryptingCertificate,
            bool useOaep)
        {
            int plaintextBlockSize = GetPlainTextBlockSize(encryptingCertificate, useOaep);
            int blockCount = ((dataToEncrypt.Length + 4) / plaintextBlockSize) + 1;
            int plainTextSize = blockCount * plaintextBlockSize;
            int cipherTextSize = blockCount * GetCipherTextBlockSize(encryptingCertificate, useOaep);

            byte[] plainText = new byte[plainTextSize];

            // encode length.
            plainText[0] = (byte)((0x000000FF & dataToEncrypt.Length));
            plainText[1] = (byte)((0x0000FF00 & dataToEncrypt.Length) >> 8);
            plainText[2] = (byte)((0x00FF0000 & dataToEncrypt.Length) >> 16);
            plainText[3] = (byte)((0xFF000000 & dataToEncrypt.Length) >> 24);

            // copy data.
            Array.Copy(dataToEncrypt, 0, plainText, 4, dataToEncrypt.Length);

            byte[] buffer = new byte[cipherTextSize];
            ArraySegment<byte> cipherText = Encrypt(new ArraySegment<byte>(plainText), encryptingCertificate, useOaep, new ArraySegment<byte>(buffer));
            System.Diagnostics.Debug.Assert(cipherText.Count == buffer.Length);

            return buffer;
        }

        /// <summary>
        /// Encrypts the data using RSA PKCS#1 v1.5 or OAEP encryption.
        /// </summary>
        public static ArraySegment<byte> Encrypt(
            ArraySegment<byte> dataToEncrypt,
            X509Certificate2 encryptingCertificate,
            bool useOaep,
            ArraySegment<byte> outputBuffer)
        {
            // get the encrypting key.
            using (RSA rsa = encryptingCertificate.GetRSAPublicKey())
            {

                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
                }

                int inputBlockSize = GetPlainTextBlockSize(encryptingCertificate, useOaep);
                int outputBlockSize = rsa.KeySize / 8;

                // verify the input data is the correct block size.
                if (dataToEncrypt.Count % inputBlockSize != 0)
                {
                    Utils.Trace("Message is not an integral multiple of the block size. Length = {0}, BlockSize = {1}.", dataToEncrypt.Count, inputBlockSize);
                }

                byte[] encryptedBuffer = outputBuffer.Array;

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
                        if (useOaep == true)
                        {
                            byte[] cipherText = rsa.Encrypt(input, RSAEncryptionPadding.OaepSHA1);
                            ostrm.Write(cipherText, 0, cipherText.Length);
                        }
                        else
                        {
                            byte[] cipherText = rsa.Encrypt(input, RSAEncryptionPadding.Pkcs1);
                            ostrm.Write(cipherText, 0, cipherText.Length);
                        }
                    }
                }

                // return buffer
                return new ArraySegment<byte>(
                    encryptedBuffer,
                    outputBuffer.Offset,
                    (dataToEncrypt.Count / inputBlockSize) * outputBlockSize);
            }
        }

        /// <summary>
        /// Encrypts the data using RSA PKCS#1 v1.5 encryption.
        /// </summary>
        public static byte[] Decrypt(
            ArraySegment<byte> dataToDecrypt,
            X509Certificate2 encryptingCertificate,
            bool useOaep)
        {
            int plainTextSize = dataToDecrypt.Count / GetCipherTextBlockSize(encryptingCertificate, useOaep);
            plainTextSize *= GetPlainTextBlockSize(encryptingCertificate, useOaep);

            byte[] buffer = new byte[plainTextSize];
            ArraySegment<byte> plainText = Decrypt(dataToDecrypt, encryptingCertificate, useOaep, new ArraySegment<byte>(buffer));
            System.Diagnostics.Debug.Assert(plainText.Count == buffer.Length);

            // decode length.
            int length = 0;

            length += (((int)plainText.Array[plainText.Offset + 0]));
            length += (((int)plainText.Array[plainText.Offset + 1]) << 8);
            length += (((int)plainText.Array[plainText.Offset + 2]) << 16);
            length += (((int)plainText.Array[plainText.Offset + 3]) << 24);

            byte[] decryptedData = new byte[length];
            Array.Copy(plainText.Array, plainText.Offset + 4, decryptedData, 0, length);

            return decryptedData;
        }

        /// <summary>
        /// Des the message using RSA OAEP encryption.
        /// </summary>
        public static ArraySegment<byte> Decrypt(
            ArraySegment<byte> dataToDecrypt,
            X509Certificate2 encryptingCertificate,
            bool useOaep,
            ArraySegment<byte> outputBuffer)
        {
            // get the encrypting key.
            using (RSA rsa = encryptingCertificate.GetRSAPrivateKey())
            {
                if (rsa == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate.");
                }

                int inputBlockSize = rsa.KeySize / 8;
                int outputBlockSize = GetPlainTextBlockSize(encryptingCertificate, useOaep);

                // verify the input data is the correct block size.
                if (dataToDecrypt.Count % inputBlockSize != 0)
                {
                    Utils.Trace("Message is not an integral multiple of the block size. Length = {0}, BlockSize = {1}.", dataToDecrypt.Count, inputBlockSize);
                }

                byte[] decryptedBuffer = outputBuffer.Array;

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
                        if (useOaep == true)
                        {
                            byte[] plainText = rsa.Decrypt(input, RSAEncryptionPadding.OaepSHA1);
                            ostrm.Write(plainText, 0, plainText.Length);
                        }
                        else
                        {
                            byte[] plainText = rsa.Decrypt(input, RSAEncryptionPadding.Pkcs1);
                            ostrm.Write(plainText, 0, plainText.Length);
                        }
                    }
                }

                // return buffers.
                return new ArraySegment<byte>(decryptedBuffer, outputBuffer.Offset, (dataToDecrypt.Count / inputBlockSize) * outputBlockSize);
            }
        }
        #endregion
    }
}
