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
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.CertificateManager;

/// <summary>
/// Source-generated logging for <see cref="CertificateManagerPlugin"/>.
/// Event ids are offset from <see cref="UaLensEventIds.CertificateManagerBase"/>.
/// </summary>
internal static partial class CertificateManagerLog
{
    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 0, Level = LogLevel.Error,
        Message = "Certificate Manager tab {Title} LoadStores failed.")]
    public static partial void CertLoadStoresFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 1, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} Enumerate({Store}) failed.")]
    public static partial void CertEnumerateFailed(
        this ILogger logger, Exception exception, string title, string store);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 2, Level = LogLevel.Information,
        Message = "Added certificate {Thumbprint} ({Subject}) to {Store}.")]
    public static partial void CertAdded(this ILogger logger, string thumbprint, string subject, string store);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 3, Level = LogLevel.Warning,
        Message = "Add to {Store} failed.")]
    public static partial void CertAddFailed(this ILogger logger, Exception exception, string store);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 4, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} AddStore failed.")]
    public static partial void CertAddStoreFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 5, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} OpenTrustDialog failed.")]
    public static partial void CertOpenTrustDialogFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 6, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} ViewDetails failed.")]
    public static partial void CertViewDetailsFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 7, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} Move({Target}) failed.")]
    public static partial void CertMoveFailed(
        this ILogger logger, Exception exception, string title, CertStoreRole target);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 8, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} Delete failed.")]
    public static partial void CertDeleteFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 9, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} Export failed.")]
    public static partial void CertExportFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.CertificateManagerBase + 10, Level = LogLevel.Warning,
        Message = "Certificate Manager tab {Title} Import failed.")]
    public static partial void CertImportFailed(this ILogger logger, Exception exception, string title);
}
