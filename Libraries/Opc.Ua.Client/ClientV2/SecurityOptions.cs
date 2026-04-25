#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

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
#endif
