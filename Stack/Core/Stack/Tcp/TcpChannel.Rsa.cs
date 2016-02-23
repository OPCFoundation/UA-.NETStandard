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
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Windows.Security.Cryptography.Core;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class TcpChannel
    {
        /// <summary>
        /// Return the plaintext block size for RSA OAEP encryption.
        /// </summary>
        protected static int Rsa_GetPlainTextBlockSize(X509Certificate2 encryptingCertificate, bool useOaep)
        {
            RSA rsa = encryptingCertificate.GetRSAPublicKey();

            if (rsa != null)
            {
                if (useOaep)
                {
                    return rsa.KeySize/8 - 42;
                }
                else
                {
                    return rsa.KeySize/8 - 11;
                }
            }

            return -1;
        }
        
        /// <summary>
        /// Return the ciphertext block size for RSA OAEP encryption.
        /// </summary>
        protected static int Rsa_GetCipherTextBlockSize(X509Certificate2 encryptingCertificate, bool useOaep)
        {
            RSA rsa = encryptingCertificate.GetRSAPublicKey();

            if (rsa != null)
            {
                return rsa.KeySize/8;
            }

            return -1;
        }

        /// <summary>
        /// Returns the length of a RSA PKCS#1 v1.5 signature of a SHA1 digest.
        /// </summary>
        private static int RsaPkcs15Sha1_GetSignatureLength(X509Certificate2 signingCertificate)
        {
            RSA rsa = signingCertificate.GetRSAPublicKey();

            if (rsa == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
            }

            return rsa.KeySize/8;
        }

        /// <summary>
        /// Creates an RSA PKCS#1 v1.5 signature of a SHA1 for the stream.
        /// </summary>
        private static byte[] RsaPkcs15Sha1_Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2   signingCertificate)
        {
            // extract the private key.
            RSA rsa = null;
            try
            {
                rsa = signingCertificate.GetRSAPrivateKey();
            }
            catch(Exception ex)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate: " + ex.Message);
            }

            if (rsa == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate.");
            }
                  
            // compute the hash of message.
            MemoryStream istrm = new MemoryStream(dataToSign.Array, dataToSign.Offset, dataToSign.Count, false);

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
        /// Verifies an RSA PKCS#1 v1.5 signature of a SHA1 for the stream.
        /// </summary>
        private static bool RsaPkcs15Sha1_Verify(
            ArraySegment<byte> dataToVerify,
            byte[]             signature,
            X509Certificate2   signingCertificate)
        {
            // extract the public key.
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
            if (!rsa.VerifyHash(digest, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
            {
                string messageType = new UTF8Encoding().GetString(dataToVerify.Array, dataToVerify.Offset, 4);
                int messageLength = BitConverter.ToInt32(dataToVerify.Array, dataToVerify.Offset+4);
                string expectedDigest = Utils.ToHexString(digest);
                string actualSignature = Utils.ToHexString(signature);

                Utils.Trace(
                    "Could not validate signature.\r\nCertificate={0}, MessageType={1}, Length={2}\r\nDigest={3}\r\nActualSignature={4}",
                    signingCertificate.Subject,
                    messageType,
                    messageLength,
                    expectedDigest,
                    actualSignature);

                return false;
            }

            return true;
        }

        /// <summary>
        /// Encrypts the message using RSA PKCS#1 v1.5 encryption.
        /// </summary>
        private ArraySegment<byte> Rsa_Encrypt(
            ArraySegment<byte> dataToEncrypt,
            ArraySegment<byte> headerToCopy,
            X509Certificate2   encryptingCertificate,
            bool               useOaep)
        {
            // get the encrypting key.
            RSA rsa = encryptingCertificate.GetRSAPublicKey();
            
            if (rsa == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No public key for certificate.");
            }

            int inputBlockSize  = Rsa_GetPlainTextBlockSize(encryptingCertificate, useOaep);
            int outputBlockSize = rsa.KeySize/8;

            // verify the input data is the correct block size.
            if (dataToEncrypt.Count % inputBlockSize != 0)
            {
                Utils.Trace("Message is not an integral multiple of the block size. Length = {0}, BlockSize = {1}.", dataToEncrypt.Count, inputBlockSize);
            }

            byte[] encryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Rsa_Encrypt");
            Array.Copy(headerToCopy.Array, headerToCopy.Offset, encryptedBuffer, 0, headerToCopy.Count);

            MemoryStream ostrm = new MemoryStream(
                encryptedBuffer, 
                headerToCopy.Count, 
                encryptedBuffer.Length - headerToCopy.Count);
            
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
            return new ArraySegment<byte>(encryptedBuffer, 0, (dataToEncrypt.Count/inputBlockSize)*outputBlockSize + headerToCopy.Count);   
        }

        /// <summary>
        /// Decrypts the message using RSA PKCS#1 v1.5 encryption.
        /// </summary>
        private ArraySegment<byte> Rsa_Decrypt(
            ArraySegment<byte> dataToDecrypt,
            ArraySegment<byte> headerToCopy,
            X509Certificate2   encryptingCertificate,
            bool               useOaep)
        {
            // get the encrypting key.
            RSA rsa = encryptingCertificate.GetRSAPrivateKey();
            
            if (rsa == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "No private key for certificate.");
            }

            int inputBlockSize  = rsa.KeySize/8;
            int outputBlockSize = Rsa_GetPlainTextBlockSize(encryptingCertificate, useOaep);
            
            // verify the input data is the correct block size.
            if (dataToDecrypt.Count % inputBlockSize != 0)
            {
                Utils.Trace("Message is not an integral multiple of the block size. Length = {0}, BlockSize = {1}.", dataToDecrypt.Count, inputBlockSize);
            }

            byte[] decryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Rsa_Decrypt");
            Array.Copy(headerToCopy.Array, headerToCopy.Offset, decryptedBuffer, 0, headerToCopy.Count);

            MemoryStream ostrm = new MemoryStream(
                decryptedBuffer, 
                headerToCopy.Count, 
                decryptedBuffer.Length - headerToCopy.Count);

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
            return new ArraySegment<byte>(decryptedBuffer, 0, (dataToDecrypt.Count/inputBlockSize)*outputBlockSize + headerToCopy.Count); 
        }        
    }
}
