/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Source-generated log messages that retain the identity of the removed
    /// "OPC-UA-Server" <c>EventSource</c> provider (legacy numeric ids, event names,
    /// levels, and message templates). See docs/DeveloperGuide.md, "Narrow exception:
    /// retained EventSource-compatibility ids". Do not add new messages here; this
    /// class exists only to preserve the three events that previously shipped through
    /// the retired provider.
    /// </summary>
    internal static partial class ServerCompatibilityLog
    {
        /// <summary>
        /// The <see cref="ILogger"/> category name used by the retired "OPC-UA-Server"
        /// <c>EventSource</c> provider.
        /// </summary>
        internal const string CategoryName = "OPC-UA-Server";

        /// <summary>
        /// Creates the compatibility logger for the retired "OPC-UA-Server"
        /// <c>EventSource</c> provider.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use.</param>
        internal static ILogger CreateCompatibilityLogger(this ITelemetryContext? telemetry)
        {
            return telemetry.CreateLogger(CategoryName);
        }

        /// <summary>
        /// A server call message. Legacy id 1 (<c>SendResponse</c>) was never
        /// implemented by the provider and is intentionally left unused.
        /// </summary>
        [LoggerMessage(EventId = ServerCompatibilityEventIds.ServerCall, EventName = "ServerCall",
            Level = LogLevel.Information, Message = "Server Call={RequestType}, Id={RequestId}")]
        public static partial void CompatibilityServerCall(
            this ILogger logger,
            string requestType,
            uint requestId);

        /// <summary>
        /// The state of the session.
        /// </summary>
        [LoggerMessage(EventId = ServerCompatibilityEventIds.SessionState, EventName = "SessionState",
            Level = LogLevel.Information,
            Message = "Session {Context}, Id={SessionId}, Name={SessionName}, ChannelId={SecureChannelId}, " +
                "User={Identity}")]
        public static partial void CompatibilitySessionState(
            this ILogger logger,
            string context,
            string sessionId,
            string sessionName,
            string secureChannelId,
            string identity);

        /// <summary>
        /// The state of a monitored item's readiness to publish. Emissions are
        /// currently disabled; the contract is retained for compatibility.
        /// </summary>
        [LoggerMessage(EventId = ServerCompatibilityEventIds.MonitoredItemReady, EventName = "MonitoredItemReady",
            Level = LogLevel.Trace, Message = "IsReadyToPublish[{Id}] {State}")]
        public static partial void CompatibilityMonitoredItemReady(this ILogger logger, uint id, string state);
    }
}
