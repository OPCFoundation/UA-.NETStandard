/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        /// The PKI root. When omitted, the running account's
        /// <c>LocalApplicationData/OPC Foundation/{ApplicationName}/pki</c>
        /// directory is used.
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
