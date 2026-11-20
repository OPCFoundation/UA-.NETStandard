// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Connection;

internal static partial class CertificateStoreServiceLog
{
    [LoggerMessage(EventId = UaLensEventIds.CertificateStoreService, Level = LogLevel.Information,
        Message = "Added certificate {Thumbprint} ({Subject}) to {Kind}.")]
    public static partial void CertificateAdded(ILogger logger, string thumbprint, string subject, CertStoreKind kind);

    [LoggerMessage(EventId = UaLensEventIds.CertificateStoreService + 1, Level = LogLevel.Error,
        Message = "Failed to add certificate to {Kind}.")]
    public static partial void CertificateAddFailed(ILogger logger, Exception exception, CertStoreKind kind);

    [LoggerMessage(EventId = UaLensEventIds.CertificateStoreService + 2, Level = LogLevel.Information,
        Message = "Trusted rejected certificate {Thumbprint} (delete from rejected: {Ok}).")]
    public static partial void RejectedCertificateTrusted(ILogger logger, string thumbprint, bool ok);

    [LoggerMessage(EventId = UaLensEventIds.CertificateStoreService + 3, Level = LogLevel.Information,
        Message = "DeleteExpired({Kind}): deleted {Count} of {Total} certificates.")]
    public static partial void ExpiredCertificatesDeleted(ILogger logger, CertStoreKind kind, int count, int total);
}
