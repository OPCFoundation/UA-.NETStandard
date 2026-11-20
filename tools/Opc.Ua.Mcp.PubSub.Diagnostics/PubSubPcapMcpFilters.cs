/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Pcap.Capture;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Normalizes PubSub packet-diagnostics errors at the MCP boundary.
    /// </summary>
    internal static class PubSubPcapMcpFilters
    {
        /// <summary>
        /// Returns actionable packet-diagnostics errors instead of the MCP
        /// SDK's generic invocation failure.
        /// </summary>
        public static McpRequestHandler<CallToolRequestParams, CallToolResult> SurfaceDiagnosticsErrors(
            McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return async (request, ct) =>
            {
                try
                {
                    return await next(request, ct).ConfigureAwait(false);
                }
                catch (PcapDiagnosticsException exception)
                {
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = exception.Message }]
                    };
                }
            };
        }
    }
}
