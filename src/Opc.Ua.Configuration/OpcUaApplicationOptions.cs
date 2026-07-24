/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Common application identity and security options used by the
    /// dependency-injection client and server features.
    /// </summary>
    public sealed class OpcUaApplicationOptions
    {
        /// <summary>
        /// The application name.
        /// </summary>
        public string? ApplicationName { get; set; }

        /// <summary>
        /// The application URI. When omitted, validation generates a URI
        /// from the host name and <see cref="ApplicationName"/>.
        /// </summary>
        public string? ApplicationUri { get; set; }

        /// <summary>
        /// The product URI.
        /// </summary>
        public string? ProductUri { get; set; }

        /// <summary>
        /// The application certificate subject. When omitted, a subject
        /// is generated from <see cref="ApplicationName"/>.
        /// </summary>
        public string? SubjectName { get; set; }

        /// <summary>
        /// The PKI root. When omitted, a per-application directory below
        /// the temporary directory is used.
        /// </summary>
        public string? PkiRoot { get; set; }

        /// <summary>
        /// Whether unknown peer certificates are automatically accepted.
        /// </summary>
        public bool? AutoAcceptUntrustedCertificates { get; set; }

        /// <summary>
        /// Whether SHA-1-signed certificates are rejected.
        /// </summary>
        public bool? RejectSHA1SignedCertificates { get; set; }

        /// <summary>
        /// The minimum accepted RSA certificate key size.
        /// </summary>
        public ushort? MinimumCertificateKeySize { get; set; }

        /// <summary>
        /// Configures advanced application security options after the default
        /// certificates, stores, and first-class validation settings are applied.
        /// </summary>
        /// <remarks>
        /// This code-only callback is the final security customization before
        /// <c>CreateAsync</c>. It may override the first-class security
        /// properties on this options instance.
        /// </remarks>
        public Action<IApplicationConfigurationBuilderSecurityOptions>? ConfigureSecurity { get; set; }

        internal OpcUaApplicationOptions Clone()
        {
            return new OpcUaApplicationOptions
            {
                ApplicationName = ApplicationName,
                ApplicationUri = ApplicationUri,
                ProductUri = ProductUri,
                SubjectName = SubjectName,
                PkiRoot = PkiRoot,
                AutoAcceptUntrustedCertificates = AutoAcceptUntrustedCertificates,
                RejectSHA1SignedCertificates = RejectSHA1SignedCertificates,
                MinimumCertificateKeySize = MinimumCertificateKeySize,
                ConfigureSecurity = ConfigureSecurity
            };
        }
    }
}
