// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Aggregate interface that unifies certificate registry, trust list
    /// management, validation, lifecycle, and store provider capabilities.
    /// </summary>
    public interface ICertificateManager :
        ICertificateRegistry,
        ICertificateTrustListManager,
        ICertificateValidatorEx,
        ICertificateLifecycle,
        ITrustListFileAccess
    {
        /// <summary>
        /// Centralised <see cref="ICertificateProvider"/> exposed by the
        /// manager. Consumers that hold a
        /// <see cref="CertificateIdentifier"/> rather than a live
        /// <see cref="Security.Certificates.Certificate"/>
        /// reference resolve the cert through this provider.
        /// </summary>
        ICertificateProvider CertificateProvider { get; }
    }
}
