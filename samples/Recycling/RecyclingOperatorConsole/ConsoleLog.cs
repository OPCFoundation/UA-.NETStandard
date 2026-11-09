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
