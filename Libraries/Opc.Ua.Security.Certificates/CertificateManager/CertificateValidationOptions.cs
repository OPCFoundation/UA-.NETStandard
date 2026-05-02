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
    /// Per-call validation overrides that allow callers to adjust
    /// certificate validation behaviour without changing the global
    /// configuration.
    /// </summary>
    public sealed class CertificateValidationOptions
    {
        /// <summary>
        /// Gets or sets a value that overrides the global setting for
        /// rejecting certificates signed with SHA-1.
        /// <c>null</c> means the global setting is used.
        /// </summary>
        public bool? RejectSHA1SignedCertificates { get; set; }

        /// <summary>
        /// Gets or sets a value that overrides the global setting for
        /// rejecting certificates when the revocation status is unknown.
        /// <c>null</c> means the global setting is used.
        /// </summary>
        public bool? RejectUnknownRevocationStatus { get; set; }

        /// <summary>
        /// Gets or sets the minimum acceptable certificate key size
        /// in bits. <c>null</c> means the global setting is used.
        /// </summary>
        public ushort? MinimumCertificateKeySize { get; set; }

        /// <summary>
        /// Gets or sets a value that overrides the global setting for
        /// automatically accepting untrusted certificates.
        /// <c>null</c> means the global setting is used.
        /// </summary>
        public bool? AutoAcceptUntrustedCertificates { get; set; }
    }
}
