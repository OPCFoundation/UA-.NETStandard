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

#nullable enable

using System;
using System.Collections.Generic;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Stateless factory for creating and parsing X.509 certificates.
    /// </summary>
    public interface ICertificateFactory
    {
        /// <summary>
        /// Creates a <see cref="Certificate"/> from DER-encoded raw data.
        /// </summary>
        /// <param name="encodedData">The DER-encoded certificate bytes.</param>
        /// <returns>A new <see cref="Certificate"/> instance.</returns>
        Certificate CreateFromRawData(ReadOnlyMemory<byte> encodedData);

        /// <summary>
        /// Parses a chain blob (e.g. a PKCS #7 or concatenated DER blob)
        /// into a <see cref="CertificateCollection"/>.
        /// </summary>
        /// <param name="chainBlob">The encoded chain blob.</param>
        /// <returns>The parsed certificate collection.</returns>
        CertificateCollection ParseChainBlob(ReadOnlyMemory<byte> chainBlob);

        /// <summary>
        /// Creates a new certificate builder for the specified subject name.
        /// </summary>
        /// <param name="subjectName">The X.500 distinguished name.</param>
        /// <returns>A builder that can be used to configure and create the certificate.</returns>
        ICertificateBuilder CreateCertificate(string subjectName);

        /// <summary>
        /// Creates a new OPC UA application certificate builder with the
        /// standard extensions pre-configured.
        /// </summary>
        /// <param name="applicationUri">The OPC UA application URI.</param>
        /// <param name="applicationName">The human-readable application name.</param>
        /// <param name="subjectName">The X.500 distinguished name.</param>
        /// <param name="domainNames">
        /// Optional list of DNS names and/or IP addresses to include
        /// in the Subject Alternative Name extension.
        /// </param>
        /// <returns>A builder that can be used to configure and create the certificate.</returns>
        ICertificateBuilder CreateApplicationCertificate(
            string applicationUri,
            string applicationName,
            string subjectName,
            IReadOnlyList<string>? domainNames = null);

        /// <summary>
        /// Creates a PKCS #10 certificate signing request for the given certificate.
        /// </summary>
        /// <param name="certificate">
        /// The certificate whose key pair is used to generate the signing request.
        /// </param>
        /// <param name="domainNames">
        /// Optional list of DNS names and/or IP addresses to request.
        /// </param>
        /// <returns>The DER-encoded signing request.</returns>
        byte[] CreateSigningRequest(
            Certificate certificate,
            IReadOnlyList<string>? domainNames = null);

        /// <summary>
        /// Combines a certificate with a PEM-encoded private key.
        /// </summary>
        /// <param name="certificate">The public certificate.</param>
        /// <param name="pemDataBlob">The PEM-encoded private key data.</param>
        /// <param name="password">
        /// An optional password used to decrypt the private key.
        /// </param>
        /// <returns>A new certificate that contains the private key.</returns>
        Certificate CreateWithPEMPrivateKey(
            Certificate certificate,
            byte[] pemDataBlob,
            ReadOnlySpan<char> password = default);

        /// <summary>
        /// Combines a certificate with the private key from another certificate.
        /// </summary>
        /// <param name="certificate">The public certificate.</param>
        /// <param name="certificateWithPrivateKey">
        /// A certificate that contains the matching private key.
        /// </param>
        /// <returns>A new certificate that contains the private key.</returns>
        Certificate CreateWithPrivateKey(
            Certificate certificate,
            Certificate certificateWithPrivateKey);
    }
}
