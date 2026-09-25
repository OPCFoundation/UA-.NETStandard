/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
