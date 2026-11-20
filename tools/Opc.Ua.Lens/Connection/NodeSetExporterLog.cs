/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Connection;

internal static partial class NodeSetExporterLog
{
    [LoggerMessage(EventId = UaLensEventIds.NodeSetExporter, Level = LogLevel.Information,
        Message = "Browsed {Count} non-base nodes in {ElapsedMs}ms; writing NodeSet2 to {Path}")]
    public static partial void NodesBrowsed(ILogger logger, int count, long elapsedMs, string path);

    [LoggerMessage(EventId = UaLensEventIds.NodeSetExporter + 1, Level = LogLevel.Information,
        Message = "Exported {Count} nodes to {Path} in {ElapsedMs}ms")]
    public static partial void NodesExported(ILogger logger, int count, string path, long elapsedMs);
}
