// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Source-generated logging methods for the network model node manager.
    /// </summary>
    internal static partial class NetworkModelNodeManagerLog
    {
        /// <summary>
        /// Records a provider read failure without implying that unavailable values are healthy.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager,
            Level = LogLevel.Error,
            Message = "The network model provider read failed; no healthy defaults were published.")]
        internal static partial void ProviderReadFailed(ILogger logger, Exception exception);

        /// <summary>
        /// Records a provider-watch failure before the manager retries the watch.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager + 1,
            Level = LogLevel.Error,
            Message = "The network model provider watch failed; published values were marked bad " +
                "and the watch will be retried.")]
        internal static partial void ProviderWatchFailed(ILogger logger, Exception exception);

        /// <summary>
        /// Records a newer provider revision rejected because its acquisition timestamp regressed.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager + 2,
            Level = LogLevel.Warning,
            Message = "Rejected network model revision {Revision} acquired at {Timestamp:o}; it is older " +
                "than applied revision {AppliedRevision} acquired at {AppliedTimestamp:o}.")]
        internal static partial void StaleSnapshotRejected(
            ILogger logger,
            long revision,
            DateTime timestamp,
            long appliedRevision,
            DateTime appliedTimestamp);

        /// <summary>
        /// Records observation ending before the server canceled it.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager + 3,
            Level = LogLevel.Warning,
            Message = "The network model provider watch ended before cancellation; retained values " +
                "were marked bad and the watch will be retried.")]
        internal static partial void ProviderWatchCompleted(ILogger logger);
    }
}
