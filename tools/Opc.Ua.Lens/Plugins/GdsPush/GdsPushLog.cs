// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Source-generated logging for <see cref="GdsPushPlugin"/>.
/// Event ids are offset from <see cref="UaLensEventIds.GdsPushBase"/>.
/// </summary>
internal static partial class GdsPushLog
{
    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 0, Level = LogLevel.Debug,
        Message = "GdsPush tab {Title}: status timer dispose threw (suppressed).")]
    public static partial void GdsPushStatusTimerDisposeThrew(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 1, Level = LogLevel.Warning,
        Message = "GdsPush tab {Title}: client dispose failed.")]
    public static partial void GdsPushClientDisposeFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 2, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: UseDifferentEndpoint failed.")]
    public static partial void GdsPushUseDifferentEndpointFailed(
        this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 3, Level = LogLevel.Warning,
        Message = "GdsPush tab {Title}: UpdateSession failed; falling back to reconnect.")]
    public static partial void GdsPushUpdateSessionFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 4, Level = LogLevel.Information,
        Message = "GdsPush tab {Title}: connected to {Endpoint}")]
    public static partial void GdsPushConnected(this ILogger logger, string title, string? endpoint);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 5, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: connect failed.")]
    public static partial void GdsPushConnectFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 6, Level = LogLevel.Information,
        Message = "GdsPush tab {Title}: piggy-backed on outer session at {Endpoint}.")]
    public static partial void GdsPushPiggybacked(this ILogger logger, string title, string? endpoint);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 7, Level = LogLevel.Warning,
        Message = "GdsPush tab {Title}: piggyback to outer failed.")]
    public static partial void GdsPushPiggybackFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 8, Level = LogLevel.Information,
        Message = "GdsPush tab {Title}: refresh ok (masks={Masks}).")]
    public static partial void GdsPushRefreshOk(this ILogger logger, string title, TrustListMasks masks);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 9, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: refresh failed.")]
    public static partial void GdsPushRefreshFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 10, Level = LogLevel.Information,
        Message = "GdsPush tab {Title}: rejected list refresh ok ({Count}).")]
    public static partial void GdsPushRejectedRefreshOk(this ILogger logger, string title, int count);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 11, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: refresh rejected list failed.")]
    public static partial void GdsPushRejectedRefreshFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 12, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: add cert failed.")]
    public static partial void GdsPushAddCertFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 13, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: remove cert failed.")]
    public static partial void GdsPushRemoveCertFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 14, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: request new cert failed.")]
    public static partial void GdsPushRequestNewCertFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 15, Level = LogLevel.Information,
        Message = "GdsPush tab {Title}: ApplyChanges called.")]
    public static partial void GdsPushApplyChangesCalled(this ILogger logger, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 16, Level = LogLevel.Error,
        Message = "GdsPush tab {Title}: ApplyChanges failed.")]
    public static partial void GdsPushApplyChangesFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 17, Level = LogLevel.Warning,
        Message = "GdsPush tab {Title}: server info populate failed.")]
    public static partial void GdsPushServerInfoPopulateFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 18, Level = LogLevel.Warning,
        Message = "GdsPush tab {Title}: credentials prompt failed.")]
    public static partial void GdsPushCredentialsPromptFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 19, Level = LogLevel.Debug,
        Message = "GdsPush tab {Title}: server-status poll skipped.")]
    public static partial void GdsPushServerStatusPollSkipped(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.GdsPushBase + 20, Level = LogLevel.Debug,
        Message = "GdsPush tab {Title}: server-status read returned status {Status}.")]
    public static partial void GdsPushServerStatusBad(this ILogger logger, string title, StatusCode status);
}
