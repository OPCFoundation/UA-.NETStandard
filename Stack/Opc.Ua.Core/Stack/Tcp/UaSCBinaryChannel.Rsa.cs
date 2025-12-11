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
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// Creates an RSA PKCS#1 v1.5 or PSS signature of a hash algorithm for the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static byte[] Rsa_Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 signingCertificate,
            HashAlgorithmName algorithm,
            RSASignaturePadding padding)
        {
            // extract the private key.
            using RSA rsa =
                signingCertificate.GetRSAPrivateKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No private key for certificate.");

            // create the signature.
            var signature = rsa.SignData(
                dataToSign.Array,
                dataToSign.Offset,
                dataToSign.Count,
                algorithm,
                padding);

#if xDEBUG
            var data = new ReadOnlySpan<byte>(dataToSign.Array, dataToSign.Offset, dataToSign.Count).ToArray()
            Console.WriteLine($"dataToSign={TcpMessageType.KeyToString(data)}");
            Console.WriteLine($"algorithm={algorithm} padding={padding}");
            Console.WriteLine($"signingCertificate={signingCertificate.Thumbprint}");
            Console.WriteLine($"signature={TcpMessageType.KeyToString(signature)}");
#endif
            return signature;
        }

        /// <summary>
        /// Verifies an RSA PKCS#1 v1.5 or PSS signature of a hash algorithm for the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool Rsa_Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 signingCertificate,
            HashAlgorithmName algorithm,
            RSASignaturePadding padding)
        {
            // extract the public key.
            using RSA rsa =
                signingCertificate.GetRSAPublicKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");

#if xDEBUG
            var data = new ReadOnlySpan<byte>(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count).ToArray()
            Console.WriteLine($"dataToVerify={TcpMessageType.KeyToString(data)}");
            Console.WriteLine($"algorithm={algorithm} padding={padding}");
            Console.WriteLine($"signingCertificate={signingCertificate.Thumbprint}");
            Console.WriteLine($"signature={TcpMessageType.KeyToString(signature)}");
#endif
            // verify signature.
            if (!rsa.VerifyData(
                    dataToVerify.Array,
                    dataToVerify.Offset,
                    dataToVerify.Count,
                    signature,
                    algorithm,
                    padding))
            {
                string messageType = Encoding.UTF8
                    .GetString(dataToVerify.Array, dataToVerify.Offset, 4);
                int messageLength = BitConverter.ToInt32(
                    dataToVerify.Array,
                    dataToVerify.Offset + 4);
                string actualSignature = Utils.ToHexString(signature);
                m_logger.LogError("Could not validate signature.");
                m_logger.LogError("Certificate: {Certificate}", signingCertificate.AsLogSafeString());
                m_logger.LogError(
                    "MessageType ={MessageType}, Length ={Length}, ActualSignature={ActualSignature}",
                    messageType,
                    messageLength,
                    actualSignature);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Encrypts the message using RSA encryption.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ArraySegment<byte> Rsa_Encrypt(
            ArraySegment<byte> dataToEncrypt,
            ArraySegment<byte> headerToCopy,
            X509Certificate2 encryptingCertificate,
            RsaUtils.Padding padding)
        {
            // get the encrypting key.
            using RSA rsa =
                encryptingCertificate.GetRSAPublicKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No public key for certificate.");

            int inputBlockSize = RsaUtils.GetPlainTextBlockSize(rsa, padding);
            int outputBlockSize = RsaUtils.GetCipherTextBlockSize(rsa);

            // verify the input data is the correct block size.
            if (dataToEncrypt.Count % inputBlockSize != 0)
            {
                m_logger.LogWarning(
                    "Message is not an integral multiple of the block size. Length = {Length}, BlockSize = {BlockSize}.",
                    dataToEncrypt.Count,
                    inputBlockSize);
            }

            byte[] encryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Rsa_Encrypt");
            Array.Copy(
                headerToCopy.Array,
                headerToCopy.Offset,
                encryptedBuffer,
                0,
                headerToCopy.Count);
            RSAEncryptionPadding rsaPadding = RsaUtils.GetRSAEncryptionPadding(padding);

            using (
                var ostrm = new MemoryStream(
                    encryptedBuffer,
                    headerToCopy.Count,
                    encryptedBuffer.Length - headerToCopy.Count))
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
                0,
                (dataToEncrypt.Count / inputBlockSize * outputBlockSize) + headerToCopy.Count);
        }

        /// <summary>
        /// Decrypts the message using RSA encryption.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ArraySegment<byte> Rsa_Decrypt(
            ArraySegment<byte> dataToDecrypt,
            ArraySegment<byte> headerToCopy,
            X509Certificate2 encryptingCertificate,
            RsaUtils.Padding padding)
        {
            // get the encrypting key.
            using RSA rsa =
                encryptingCertificate.GetRSAPrivateKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No private key for certificate.");

            int inputBlockSize = RsaUtils.GetCipherTextBlockSize(rsa);
            int outputBlockSize = RsaUtils.GetPlainTextBlockSize(rsa, padding);

            // verify the input data is the correct block size.
            if (dataToDecrypt.Count % inputBlockSize != 0)
            {
                m_logger.LogWarning(
                    "Message is not an integral multiple of the block size. Length = {Length}, BlockSize = {BlockSize}.",
                    dataToDecrypt.Count,
                    inputBlockSize);
            }

            byte[] decryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Rsa_Decrypt");
            Array.Copy(
                headerToCopy.Array,
                headerToCopy.Offset,
                decryptedBuffer,
                0,
                headerToCopy.Count);
            RSAEncryptionPadding rsaPadding = RsaUtils.GetRSAEncryptionPadding(padding);

            using (
                var ostrm = new MemoryStream(
                    decryptedBuffer,
                    headerToCopy.Count,
                    decryptedBuffer.Length - headerToCopy.Count))
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
                0,
                (dataToDecrypt.Count / inputBlockSize * outputBlockSize) + headerToCopy.Count);
        }
    }
}
