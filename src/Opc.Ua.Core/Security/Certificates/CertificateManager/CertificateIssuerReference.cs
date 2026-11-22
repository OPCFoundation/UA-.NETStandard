// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Represents a single resolved entry in a certificate chain — a
    /// concrete <see cref="Certificate"/> plus the
    /// <see cref="CertificateValidationOptions"/> that govern its
    /// validation.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="ICertificateRegistry.GetIssuersAsync"/> and
    /// the certificate validator to carry chain elements without
    /// resorting to the conflated metadata-and-cert role of the legacy
    /// <see cref="CertificateIdentifier"/> wrapper. Returned references
    /// are caller-owned — callers must dispose <see cref="Certificate"/>
    /// when they are done with it.
    /// </remarks>
    /// <param name="Certificate">
    /// The certificate at this position in the chain. The caller owns
    /// this reference and is responsible for disposing it.
    /// </param>
    /// <param name="Options">
    /// Validation options associated with the source the certificate
    /// was loaded from (e.g. trusted-list-derived suppression flags).
    /// </param>
    public sealed record CertificateIssuerReference(
        Certificate Certificate,
        CertificateValidationOptions Options);
}
