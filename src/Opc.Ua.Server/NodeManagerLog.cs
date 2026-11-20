/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Shared source-generated log messages for node manager implementations.
    /// </summary>
    internal static partial class NodeManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.NodeManager + 0, Level = LogLevel.Trace,
            Message = "READ: NodeId={NodeId} Value={Value} Range={Range}")]
        public static partial void Read(this ILogger logger, NodeId nodeId, Variant value, string? range);

        [LoggerMessage(EventId = ServerEventIds.NodeManager + 1, Level = LogLevel.Trace,
            Message = "WRITE: NodeId={NodeId} Value={Value} Range={Range}")]
        public static partial void Write(this ILogger logger, NodeId nodeId, Variant value, string? range);

        [LoggerMessage(EventId = ServerEventIds.NodeManager + 2, Level = LogLevel.Debug,
            Message = "Cleared historical access advertisement for NodeId={NodeId} because no historian provider is wired.")]
        public static partial void HistoryAdvertisementCleared(this ILogger logger, NodeId nodeId);

        [LoggerMessage(EventId = ServerEventIds.NodeManager + 3, Level = LogLevel.Debug,
            Message = "Completed the create lifecycle while registering NodeId={NodeId}, " +
                "BrowseName={BrowseName}. Call CreateAsPredefinedNode before registration " +
                "when configuration depends on OnAfterCreate.")]
        public static partial void PredefinedNodeLifecycleCompletedAtRegistration(
            this ILogger logger,
            NodeId nodeId,
            QualifiedName browseName);

        [LoggerMessage(EventId = ServerEventIds.NodeManager + 4, Level = LogLevel.Error,
            Message = "Retired NodeManager cleanup failed; a later lifecycle operation or shutdown will retry.")]
        public static partial void RetiredGenerationCleanupFailed(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = ServerEventIds.NodeManager + 5, Level = LogLevel.Warning,
            Message = "NodeManager lifecycle wait {Wait} has remained pending for {Elapsed}. Context: {Context}")]
        public static partial void LifecycleWaitExceeded(
            this ILogger logger,
            string wait,
            TimeSpan elapsed,
            string context);

        [LoggerMessage(EventId = ServerEventIds.NodeManager + 6, Level = LogLevel.Warning,
            Message = "NodeManager shutdown reconciliation failed after commit, but retirement completed.")]
        public static partial void ShutdownReconciliationReported(
            this ILogger logger,
            Exception exception);
    }
}
