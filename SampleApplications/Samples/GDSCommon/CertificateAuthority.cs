/* ========================================================================
 * Copyright (c) 2005-2011 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Creates a manages certificates.
    /// </summary>
    public class CertificateAuthority
    {
        /// <summary>
        /// Replaces the certificate in a PFX file.
        /// </summary>
        /// <param name="newCertificate">The new certificate.</param>
        /// <param name="existingCertificate">The existing certificate with a private key.</param>
        /// <returns>The new certificate with a private key.</returns>
        public static X509Certificate2 Replace(
            X509Certificate2 newCertificate,
            X509Certificate2 existingCertificate)
        {
            //TODO
            return null;
        }

        /// <summary>
        /// Signs an existing certificate by the CA.
        /// </summary>
        /// <param name="requestPath">The path to the certificate signing request.</param>
        /// <param name="commonName">Name of the common.</param>
        /// <param name="applicationUri">The application uri. Replaces whatever is in the existing certificate.</param>
        /// <param name="domainNames">The domain names. Replaces whatever is in the existing certificate.</param>
        /// <param name="issuerKeyFilePath">The path to the CA private key.</param>
        /// <param name="issuerKeyFilePassword">The password for the CA private key.</param>
        /// <param name="startTime">The begining of the validity period for the certificate.</param>
        /// <param name="lifetimeInMonths">The lifetime in months.</param>
        /// <param name="hashSizeInBits">The hash size in bits.</param>
        /// <param name="outputStore">The location for the new certificate.</param>
        /// <returns>
        /// The path to the new certificate.
        /// </returns>
        /// <exception cref="System.IO.FileNotFoundException">Public key file not found
        /// or
        /// Issuer key file not found
        /// or
        /// Output store not found</exception>
        /// <exception cref="ServiceResultException">Input file was not processed properly.
        /// or
        /// Invalid response produced by the CertificateGenerator.</exception>
        public static string Sign(
            string requestPath,
            string commonName,
            string applicationUri,
            IList<string> domainNames,
            string issuerKeyFilePath,
            string issuerKeyFilePassword,
            DateTime startTime,
            ushort lifetimeInMonths,
            ushort hashSizeInBits,
            string outputStore)
        {
            //TODO
            return string.Empty;
        }

        /// <summary>
        /// Creates a certificate signing request.
        /// </summary>
        /// <param name="certificate">The certificate to go with the private key.</param>
        /// <param name="privateKey">The private key used to sign the request.</param>
        /// <param name="isPEMKey">TRUE if the private key is in PEM format; FALSE otherwise.</param>
        /// <param name="password">The password for the private key.</param>
        /// <param name="subjectName">Subject name for the new certificate.</param>
        /// <param name="applicationUri">The application uri. Replaces whatever is in the existing certificate.</param>
        /// <param name="domainNames">The domain names. Replaces whatever is in the existing certificate.</param>
        /// <param name="hashSizeInBits">The hash size in bits.</param>
        /// <returns>
        /// The certificate signing request.
        /// </returns>
        public static byte[] CreateRequest(
            X509Certificate2 certificate,
            byte[] privateKey,
            bool isPEMKey,
            string password,
            string subjectName,
            string applicationUri,
            IList<string> domainNames,
            ushort hashSizeInBits)
        {
            //TODO
            return null;
        }
    }
}
