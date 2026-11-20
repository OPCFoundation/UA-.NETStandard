/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tool for reading OPC UA client configuration in bounded profiles.
    /// </summary>
    [McpServerToolType]
    public sealed class ConfigurationReadTools
    {
        /// <summary>
        /// Gets the current OPC UA client configuration.
        /// </summary>
        [McpServerTool(Name = "GetConfiguration")]
        [Description(
            "Get current transport quotas, certificate security settings, and client defaults. " +
            "Use before changing configuration. Returns a JSON object with transportQuotas, security, " +
            "and clientConfiguration sections, or {error, message}.")]
        public static Task<string> GetConfigurationAsync(
            OpcUaSessionManager sessionManager,
            CancellationToken ct = default)
        {
            return ConfigurationTools.GetConfigurationAsync(sessionManager, ct);
        }
    }
}
