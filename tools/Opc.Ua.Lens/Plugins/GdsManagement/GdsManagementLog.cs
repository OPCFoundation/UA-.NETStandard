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

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Source-generated logging for <see cref="GdsManagementPlugin"/>.
/// Event ids are offset from <see cref="UaLensEventIds.GdsManagementBase"/>.
/// </summary>
internal static partial class GdsManagementLog
{
    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 0, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: client dispose failed.")]
    public static partial void GdsMgmtClientDisposeFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 1, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: UseDifferentEndpoint failed.")]
    public static partial void GdsMgmtUseDifferentEndpointFailed(
        this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 2, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: UpdateSession failed; reconnecting.")]
    public static partial void GdsMgmtUpdateSessionFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 3, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: connected to {Endpoint}")]
    public static partial void GdsMgmtConnected(this ILogger logger, string title, string? endpoint);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 4, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: connect failed.")]
    public static partial void GdsMgmtConnectFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 5, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: piggy-backed on outer session at {Endpoint}.")]
    public static partial void GdsMgmtPiggybacked(this ILogger logger, string title, string? endpoint);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 6, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: piggyback to outer failed.")]
    public static partial void GdsMgmtPiggybackFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 7, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: refresh ok ({Count} apps).")]
    public static partial void GdsMgmtRefreshOk(this ILogger logger, string title, int count);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 8, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: refresh failed.")]
    public static partial void GdsMgmtRefreshFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 9, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: registered {Uri} → {Id}.")]
    public static partial void GdsMgmtRegistered(this ILogger logger, string title, string? uri, NodeId id);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 10, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: register failed.")]
    public static partial void GdsMgmtRegisterFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 11, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: unregistered {Id}.")]
    public static partial void GdsMgmtUnregistered(this ILogger logger, string title, NodeId id);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 12, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: unregister failed.")]
    public static partial void GdsMgmtUnregisterFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 13, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: issued {Label} for {App} (request {Req}). "
            + "No CurrentRegisteredApp context — delivery skipped.")]
    public static partial void GdsMgmtIssuedNoContext(
        this ILogger logger, string title, string label, string app, NodeId req);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 14, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: issued {Label} for {App} (request {Req}); delivery: {Detail}.")]
    public static partial void GdsMgmtIssued(
        this ILogger logger, string title, string label, string app, NodeId req, string detail);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 15, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: issue cert failed.")]
    public static partial void GdsMgmtIssueCertFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 16, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: view cert groups failed.")]
    public static partial void GdsMgmtViewCertGroupsFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 17, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: pulled trust list for {App}; {Detail}.")]
    public static partial void GdsMgmtPulledTrustList(this ILogger logger, string title, string app, string detail);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 18, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: pull trust list (local) failed.")]
    public static partial void GdsMgmtPullTrustListLocalFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 19, Level = LogLevel.Information,
        Message = "GdsManagement tab {Title}: pushed trust list for {App}; {Detail}.")]
    public static partial void GdsMgmtPushedTrustList(this ILogger logger, string title, string app, string detail);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 20, Level = LogLevel.Error,
        Message = "GdsManagement tab {Title}: pull trust list (push) failed.")]
    public static partial void GdsMgmtPullTrustListPushFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 21, Level = LogLevel.Debug,
        Message = "GdsManagement tab {Title}: FindApplication failed for {Uri}.")]
    public static partial void GdsMgmtFindApplicationFailed(
        this ILogger logger, Exception exception, string title, string uri);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 22, Level = LogLevel.Debug,
        Message = "GdsManagement tab {Title}: trust-list read failed for group {Group}.")]
    public static partial void GdsMgmtTrustListReadFailed(
        this ILogger logger, Exception exception, string title, NodeId group);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 23, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: write to cert store {Path} failed.")]
    public static partial void GdsMgmtWriteCertStoreFailed(
        this ILogger logger, Exception exception, string title, string path);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 24, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: write public-key file {Path} failed.")]
    public static partial void GdsMgmtWritePublicKeyFailed(
        this ILogger logger, Exception exception, string title, string path);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 25, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: write private-key file {Path} failed.")]
    public static partial void GdsMgmtWritePrivateKeyFailed(
        this ILogger logger, Exception exception, string title, string path);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 26, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: write to issuer store {Path} failed.")]
    public static partial void GdsMgmtWriteIssuerStoreFailed(
        this ILogger logger, Exception exception, string title, string path);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 27, Level = LogLevel.Debug,
        Message = "GdsManagement tab {Title}: skipping malformed issuer cert at index {Index}.")]
    public static partial void GdsMgmtSkipMalformedIssuerCert(
        this ILogger logger, Exception exception, string title, int index);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 28, Level = LogLevel.Debug,
        Message = "GdsManagement tab {Title}: skipping malformed {Bucket} cert at index {Index}.")]
    public static partial void GdsMgmtSkipMalformedCert(
        this ILogger logger, Exception exception, string title, string bucket, int index);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 29, Level = LogLevel.Debug,
        Message = "GdsManagement tab {Title}: skipping malformed {Bucket} CRL at index {Index}.")]
    public static partial void GdsMgmtSkipMalformedCrl(
        this ILogger logger, Exception exception, string title, string bucket, int index);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 30, Level = LogLevel.Debug,
        Message = "GdsManagement tab {Title}: push ApplyChanges tore down the channel as expected.")]
    public static partial void GdsMgmtPushApplyChangesToreDownChannel(
        this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsManagementBase + 31, Level = LogLevel.Warning,
        Message = "GdsManagement tab {Title}: dispose of ephemeral push client failed.")]
    public static partial void GdsMgmtEphemeralPushClientDisposeFailed(
        this ILogger logger, Exception exception, string title);
}
