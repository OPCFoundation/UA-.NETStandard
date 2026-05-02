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

using System.Collections.Generic;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Provides read-only access to the application's own certificates.
    /// </summary>
    public interface ICertificateRegistry
    {
        /// <summary>
        /// Gets the list of all application certificate entries.
        /// </summary>
        IReadOnlyList<CertificateEntry> ApplicationCertificates { get; }

        /// <summary>
        /// Returns the application certificate entry that matches the
        /// specified OPC UA certificate type <see cref="NodeId"/>.
        /// </summary>
        /// <param name="certificateType">
        /// The OPC UA certificate type node identifier.
        /// </param>
        /// <returns>
        /// The matching <see cref="CertificateEntry"/>, or <see langword="null"/>
        /// if no certificate of that type is registered.
        /// </returns>
        CertificateEntry? GetApplicationCertificate(NodeId certificateType);

        /// <summary>
        /// Returns the instance certificate entry that is appropriate for the
        /// specified security policy URI.
        /// </summary>
        /// <param name="securityPolicyUri">
        /// The OPC UA security policy URI (e.g.
        /// <c>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</c>).
        /// </param>
        /// <returns>
        /// The matching <see cref="CertificateEntry"/>, or <see langword="null"/>
        /// if no suitable certificate is found.
        /// </returns>
        CertificateEntry? GetInstanceCertificate(string securityPolicyUri);

        /// <summary>
        /// Returns the DER-encoded certificate chain blob for the instance
        /// certificate matching the specified security policy URI.
        /// </summary>
        /// <param name="securityPolicyUri">
        /// The OPC UA security policy URI.
        /// </param>
        /// <returns>The encoded chain blob.</returns>
        byte[] GetEncodedChainBlob(string securityPolicyUri);
    }
}
