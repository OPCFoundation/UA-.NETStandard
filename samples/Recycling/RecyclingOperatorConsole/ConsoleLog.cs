/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.Logging;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Source-generated workstation diagnostics; credentials and request tokens are never included.
/// </summary>
internal static partial class ConsoleLog
{
    /// <summary>
    /// Reports a coordinator connection failure.
    /// </summary>
    [LoggerMessage(EventId = RecyclingOperatorConsoleEventIds.Console + 0, Level = LogLevel.Warning,
        Message = "Encrypted recycling coordinator connection failed; check application trust and user authorization.")]
    internal static partial void ConnectionFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Reports an independently unavailable native endpoint without exposing credentials.
    /// </summary>
    [LoggerMessage(EventId = RecyclingOperatorConsoleEventIds.Console + 1, Level = LogLevel.Warning,
        Message = "Selected native recycling endpoint is unavailable or unauthorized.")]
    internal static partial void NativeFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Records explicit command failures without logging the request payload or credentials.
    /// </summary>
    [LoggerMessage(EventId = RecyclingOperatorConsoleEventIds.Console + 2, Level = LogLevel.Warning,
        Message = "Recycling coordinator command failed with status {Status}.")]
    internal static partial void CommandFailed(this ILogger logger, string status, Exception exception);

    /// <summary>
    /// Records bounded history failures without material or credential payloads.
    /// </summary>
    [LoggerMessage(EventId = RecyclingOperatorConsoleEventIds.Console + 3, Level = LogLevel.Warning,
        Message = "Recycling coordinator history read failed with status {Status}.")]
    internal static partial void HistoryFailed(this ILogger logger, string status, Exception exception);

    /// <summary>
    /// Records an unavailable optional native member without logging material or identity payloads.
    /// </summary>
    [LoggerMessage(EventId = RecyclingOperatorConsoleEventIds.Console + 4, Level = LogLevel.Debug,
        Message = "Optional native recycling companion reading is unavailable with status {Status}.")]
    internal static partial void NativeOptionalUnavailable(this ILogger logger, StatusCode status);
}
