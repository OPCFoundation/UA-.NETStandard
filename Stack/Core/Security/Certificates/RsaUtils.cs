/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;

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
            RSA rsa = encryptingCertificate.GetRSAPublicKey();

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

            return -1;
        }

        /// <summary>
        /// Return the ciphertext block size for RSA OAEP encryption.
        /// </summary>
        public static int GetCipherTextBlockSize(X509Certificate2 encryptingCertificate, bool useOaep)
        {
            RSA rsa = encryptingCertificate.GetRSAPublicKey();

            if (rsa != null)
            {
                return rsa.KeySize / 8;
            }

            return -1;
        }

        /// <summary>
        /// Returns the length of a RSA PKCS#1 v1.5 signature of a SHA1 digest.
        /// </summary>
        public static int RsaPkcs15Sha1_GetSignatureLength(X509Certificate2 signingCertificate)
        {
            RSA rsa = signingCertificate.GetRSAPublicKey();

            if (rsa == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
            }

            return rsa.KeySize / 8;
        }

        /// <summary>
        /// Computes an RSA/SHA1 PKCS#1 v1.5 signature.
        /// </summary>
        public static byte[] RsaPkcs15Sha1_Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate)
        {
            // extract the private key.
            RSA rsa = signingCertificate.GetRSAPrivateKey();

            if (rsa == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate.");
            }

            // compute the hash of message.
            MemoryStream istrm = new MemoryStream(dataToSign.Array, dataToSign.Offset, dataToSign.Count, false);

            // create the hmac.
            HashAlgorithmProvider sha1Provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            IBuffer buffer = CryptographicBuffer.CreateFromByteArray(istrm.ToArray());
            buffer = sha1Provider.HashData(buffer);
            byte[] digest = new byte[buffer.Length];
            CryptographicBuffer.CopyToByteArray(buffer, out digest);

            istrm.Dispose();

            // create the signature.
            return rsa.SignHash(digest, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
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
            RSA rsa = signingCertificate.GetRSAPublicKey();

            if (rsa == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
            }

            // compute the hash of message.
            MemoryStream istrm = new MemoryStream(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, false);

            HashAlgorithmProvider sha1Provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            IBuffer buffer = CryptographicBuffer.CreateFromByteArray(istrm.ToArray());
            buffer = sha1Provider.HashData(buffer);
            byte[] digest = new byte[buffer.Length];
            CryptographicBuffer.CopyToByteArray(buffer, out digest);

            istrm.Dispose();

            // verify signature.
            return rsa.VerifyHash(digest, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
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
            RSA rsa = encryptingCertificate.GetRSAPublicKey();

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

            MemoryStream ostrm = new MemoryStream(
                encryptedBuffer,
                outputBuffer.Offset,
                outputBuffer.Count);

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

            ostrm.Dispose();

            // return buffer
            return new ArraySegment<byte>(
                encryptedBuffer,
                outputBuffer.Offset,
                (dataToEncrypt.Count / inputBlockSize) * outputBlockSize);
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
            RSA rsa = encryptingCertificate.GetRSAPrivateKey();

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

            MemoryStream ostrm = new MemoryStream(
                decryptedBuffer,
                outputBuffer.Offset,
                outputBuffer.Count);

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

            ostrm.Dispose();

            // return buffers.
            return new ArraySegment<byte>(decryptedBuffer, outputBuffer.Offset, (dataToDecrypt.Count / inputBlockSize) * outputBlockSize);
        }
        #endregion
    }
}
