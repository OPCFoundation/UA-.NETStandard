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
