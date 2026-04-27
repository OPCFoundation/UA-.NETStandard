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

namespace Opc.Ua.Client
{
    using System;

    /// <summary>
    /// Security configuration
    /// </summary>
    public sealed record class SecurityOptions
    {
        /// <summary>
        /// PkiRootPath
        /// </summary>
        public string PkiRootPath { get; init; } = Environment.CurrentDirectory;

        /// <summary>
        /// The subject name to use to find the certificate or when
        /// creating a new application certificate.
        /// </summary>
        public string? ApplicationCertificateSubjectName { get; init; }

        /// <summary>
        /// Password to secure the key of the application certificate
        /// in the private key infrastructure.
        /// </summary>
        public string? ApplicationCertificatePassword { get; init; }

        /// <summary>
        /// Update the application configuration from the certificate
        /// found in the own folder.  This is useful when the application
        /// was configured externally without updating the application
        /// configuration.
        /// </summary>
        public bool UpdateApplicationFromExistingCert { get; init; }

        /// <summary>
        /// Automatically add application certificate to the trusted store
        /// </summary>
        public bool AddAppCertToTrustedStore { get; init; }

        /// <summary>
        /// Host name override to use when accessing the client host
        /// name is not possible or yields wrong results.
        /// </summary>
        public string? HostName { get; init; }

        /// <summary>
        /// Whether to auto accept untrusted certificates
        /// </summary>
        public bool AutoAcceptUntrustedCertificates { get; init; }

        /// <summary>
        /// Minimum key size
        /// </summary>
        public ushort MinimumCertificateKeySize { get; init; } = 2048;

        /// <summary>
        /// Whether to reject unsecure signatures
        /// </summary>
        public bool RejectSha1SignedCertificates { get; init; } = true;

        /// <summary>
        /// Reject chain validation with CA certs with unknown revocation
        /// status, e.g.when the CRL is not available or the OCSP provider
        /// is offline.
        /// </summary>
        public bool RejectUnknownRevocationStatus { get; init; } = true;
    }
}