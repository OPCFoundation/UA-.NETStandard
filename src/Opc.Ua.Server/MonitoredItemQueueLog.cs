// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Shared source-generated log messages for monitored item queues.
    /// </summary>
    internal static partial class MonitoredItemQueueLog
    {
        [LoggerMessage(EventId = ServerEventIds.MonitoredItemQueue + 0, Level = LogLevel.Trace,
            Message = "DEQUEUE VALUE: Value={Value} CODE={Code}<{Code:X8}> OVERFLOW={Overflow}," +
                " MonitoredItemId={MonitoredItemId}")]
        public static partial void DequeueValue(
            this ILogger logger,
            Variant value,
            uint code,
            bool overflow,
            uint monitoredItemId);
    }
}
