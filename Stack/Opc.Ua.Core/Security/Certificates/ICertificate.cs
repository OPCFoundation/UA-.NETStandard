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

namespace Opc.Ua
{
    /// <summary>
    /// An interface to a certificate used by a UA application.
    /// </summary>
    public interface ICertificate
    {
        /// <summary>
        /// The subject name for the certificate.
        /// </summary>
        string Subject { get; }

        /// <summary>
        /// The SHA1 hash of the certificate represented as an uppercase hexadecimal.
        /// </summary>
        string Thumbprint { get; }

        /// <summary>
        /// Whether a private key is available.
        /// </summary>
        bool HasPrivateKey { get; }

        /// <summary>
        /// The DER encoded certficate data with all supporting certificates.
        /// </summary>
        byte[] RawData { get; }

        /// <summary>
        /// Returns the size of a block of unencrypted data.
        /// </summary>
        int GetPlainTextBlockSize(string algorithmUri);
        
        /// <summary>
        /// Returns the size of a block of encrypted data.
        /// </summary>
        int GetCipherTextBlockSize(string algorithmUri);
        
        /// <summary>
        /// Returns the length of the signature.
        /// </summary>
        int GetSignatureLength(string algorithmUri);
        
        /// <summary>
        /// Encrypts the data using the specified algorithm.
        /// </summary>
        /// <remarks>
        /// The input must be a multiple of the plaintext block size.
        /// </remarks>
        ArraySegment<byte> Encrypt(
            string             algorithmUri,
            ArraySegment<byte> dataToEncrypt);
        
        /// <summary>
        /// Encrypts the data using the specified algorithm.
        /// </summary>
        /// <remarks>
        /// The input must be a multiple of the cipher text block size.
        /// </remarks>
        ArraySegment<byte> Decrypt(
            string             algorithmUri,
            ArraySegment<byte> dataToDecrypt);
        
        /// <summary>
        /// Signs the data and returns the signature.
        /// </summary>
        byte[] Sign(
            string             algorithmUri,
            ArraySegment<byte> dataToSign);
        
        /// <summary>
        /// Verifies the signature for the data.
        /// </summary>
        bool Verify(
            string             algorithmUri,
            ArraySegment<byte> dataToVerify,
            ArraySegment<byte> signature);

        /// <summary>
        /// The primary certificate.
        /// </summary>
        X509Certificate2 Certificate { get; }

        /// <summary>
        /// Any supporting certificates.
        /// </summary>
        X509Certificate2Collection GetSupportingCertificates();
    }
}
