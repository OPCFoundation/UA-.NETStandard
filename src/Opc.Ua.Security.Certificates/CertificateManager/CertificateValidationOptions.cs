// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

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
        /// Gets or sets trust material which replaces all configured trust and issuer stores for this call.
        /// Scoped validation neither consumes nor populates the connection-validation cache and does not
        /// inherit the manager's automatic acceptance or error-acceptance callback.
        /// </summary>
        public CertificateValidationScope? TrustScope { get; set; }

        /// <summary>
        /// Gets or sets whether the leaf certificate is used to sign data rather
        /// than authenticate an OPC UA application. Signing requires digitalSignature
        /// key usage; trust, issuer usage, validity and revocation checks still apply.
        /// </summary>
        public bool ValidateForSigning { get; set; }

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
        /// Gets or sets whether the chain engine may download issuer
        /// certificates (via the AIA extension) during chain construction.
        /// The default (<c>null</c> or <c>false</c>) does not allow downloads,
        /// so validation performs no outbound network access.
        /// </summary>
        /// <remarks>
        /// This option only takes effect on .NET 5.0 and later, where it maps
        /// to <c>X509ChainPolicy.DisableCertificateDownloads</c>. On .NET
        /// Framework and .NET Standard 2.x that X509ChainPolicy property does
        /// not exist, so the setting has no effect and any AIA retrieval is
        /// bounded only by <see cref="UrlRetrievalTimeout"/>.
        /// </remarks>
        public bool? AllowCertificateDownload { get; set; }

        /// <summary>
        /// Gets or sets the URL retrieval timeout used during chain
        /// construction. <c>null</c> means the default behavior is used.
        /// </summary>
        public TimeSpan? UrlRetrievalTimeout { get; set; }

        /// <summary>
        /// Gets or sets a value that overrides the global setting for
        /// automatically accepting untrusted certificates.
        /// <c>null</c> means the global setting is used.
        /// </summary>
        public bool? AutoAcceptUntrustedCertificates { get; set; }

        /// <summary>
        /// Gets or sets whether a failed validation is recorded in the rejected
        /// store. Defaults to true. Trust revalidation of an existing connection
        /// can disable recording without changing the validation result.
        /// </summary>
        public bool RecordRejectedCertificates { get; set; } = true;

        /// <summary>
        /// Gets or sets an optional per-error accept callback. If set,
        /// the callback is invoked for each suppressible certificate
        /// validation error encountered during validation, with the
        /// failing certificate and the corresponding
        /// <see cref="ServiceResult"/>. Returning <see langword="true"/>
        /// accepts the specific error and allows validation to continue.
        /// </summary>
        /// <remarks>
        /// The callback is invoked only for suppressible errors;
        /// non-suppressible errors always cause validation to fail.
        /// (Replaces the per-error <c>e.Accept</c> mutation pattern from
        /// the legacy validator's <c>CertificateValidation</c> event,
        /// which has been removed.)
        /// </remarks>
        public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }
    }
}
