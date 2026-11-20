// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.Alarms;

internal static partial class AlarmsLog
{
    [LoggerMessage(EventId = UaLensEventIds.AlarmPluginBase, Level = LogLevel.Information,
        Message = "Alarm observation started with a document-owned V2 subscription.")]
    public static partial void AlarmObservationStarted(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.AlarmPluginBase + 1, Level = LogLevel.Information,
        Message = "ConditionRefresh requested. Completeness requires refresh start/end markers.")]
    public static partial void AlarmRefreshRequested(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.AlarmPluginBase + 2, Level = LogLevel.Warning,
        Message = "ConditionRefresh failed; retained state remains incomplete.")]
    public static partial void AlarmRefreshFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.AlarmPluginBase + 3, Level = LogLevel.Information,
        Message = "Explicit alarm operation {Operation} was accepted; waiting for the server event.")]
    public static partial void AlarmCommandAccepted(this ILogger logger, AlarmOperationKind operation);

    [LoggerMessage(EventId = UaLensEventIds.AlarmPluginBase + 4, Level = LogLevel.Warning,
        Message = "Explicit alarm operation {Operation} was rejected.")]
    public static partial void AlarmCommandRejected(
        this ILogger logger, Exception exception, AlarmOperationKind operation);

    [LoggerMessage(EventId = UaLensEventIds.AlarmPluginBase + 5, Level = LogLevel.Warning,
        Message = "Alarm subscription cleanup failed.")]
    public static partial void AlarmCleanupFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.AlarmPluginBase + 6, Level = LogLevel.Warning,
        Message = "Alarm document action {Action} failed.")]
    public static partial void AlarmActionFailed(this ILogger logger, Exception exception, string action);
}
