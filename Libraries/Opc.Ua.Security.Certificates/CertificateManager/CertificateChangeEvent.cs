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

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Describes the kind of certificate change that occurred.
    /// </summary>
    public enum CertificateChangeKind
    {
        /// <summary>An application certificate was updated.</summary>
        ApplicationCertificateUpdated,

        /// <summary>A trust list was updated.</summary>
        TrustListUpdated,

        /// <summary>A certificate revocation list was updated.</summary>
        CrlUpdated,

        /// <summary>A certificate was rejected during validation.</summary>
        CertificateRejected,

        /// <summary>A certificate is approaching its expiry date.</summary>
        CertificateExpiring
    }

    /// <summary>
    /// Represents a certificate change event raised by the certificate
    /// manager when certificates, trust lists, or CRLs are modified.
    /// </summary>
    /// <param name="Kind">The kind of change that occurred.</param>
    /// <param name="TrustList">
    /// The trust list affected by the change.
    /// </param>
    /// <param name="CertificateType">
    /// The OPC UA certificate type <see cref="NodeId"/>, or
    /// <c>null</c> if not applicable.
    /// </param>
    /// <param name="OldCertificate">
    /// The previous certificate, or <c>null</c> if not applicable.
    /// </param>
    /// <param name="NewCertificate">
    /// The new certificate, or <c>null</c> if not applicable.
    /// </param>
    /// <param name="IssuerChain">
    /// The issuer chain for the new certificate, or <c>null</c> if
    /// not applicable.
    /// </param>
    public sealed record CertificateChangeEvent(
        CertificateChangeKind Kind,
        TrustListIdentifier TrustList,
        NodeId? CertificateType,
        Certificate? OldCertificate,
        Certificate? NewCertificate,
        CertificateCollection? IssuerChain);
}
