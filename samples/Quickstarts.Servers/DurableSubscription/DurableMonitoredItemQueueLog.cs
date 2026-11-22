// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;

namespace Quickstarts.Servers
{
    internal static partial class DurableMonitoredItemQueueLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.DurableMonitoredItemQueue + 0, Level = LogLevel.Debug,
            Message = "Storing batch for monitored item {MonitoredItemId}")]
        public static partial void StoringBatch(this ILogger logger, uint monitoredItemId);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.DurableMonitoredItemQueue + 1, Level = LogLevel.Debug,
            Message = "Dequeue was requeusted but queue was not restored for monitoreditem {MonitoredItemId} " +
                "try to restore for 10 ms.")]
        public static partial void DequeueRequestedBeforeRestore(this ILogger logger, uint monitoredItemId);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.DurableMonitoredItemQueue + 2, Level = LogLevel.Debug,
            Message = "Dequeue failed for monitoreditem {MonitoredItemId} as queue could not be " +
                "restored in time.")]
        public static partial void DequeueFailedBeforeRestore(this ILogger logger, uint monitoredItemId);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.DurableMonitoredItemQueue + 3, Level = LogLevel.Warning,
            Message = "Could not load a spilled event batch of monitored item {MonitoredItemId} to store it.")]
        public static partial void SpilledBatchNotLoadedForStore(this ILogger logger, uint monitoredItemId);
    }
}
