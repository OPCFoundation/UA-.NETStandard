// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for discovering Robot Intent controllers and capabilities.
    /// </summary>
    [McpServerToolType]
    public sealed class RoboticsDiscoveryTools
    {
        /// <summary>
        /// Lists Robot Intent controllers below Server/RobotIntent/Controllers.
        /// </summary>
        [McpServerTool(Name = "robotics_list_controllers")]
        [Description("Lists Robot Intent controllers visible in the active OPC UA session. This performs discovery " +
            "only; it does not request command authority and therefore cannot be refused for ControlNotOwned.")]
        public static async Task<RobotIntentNodeLookupEntry[]> ListControllersAsync(
            RoboticsIntentManager manager,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            ArrayOf<RobotIntentNodeLookupEntry> controllers = await manager
                .CreateClient(sessionName)
                .DiscoverControllersAsync(ct)
                .ConfigureAwait(false);
            var result = new RobotIntentNodeLookupEntry[controllers.Count];
            for (int i = 0; i < controllers.Count; i++)
            {
                result[i] = controllers[i];
            }
            return result;
        }

        /// <summary>
        /// Reads one controller's declared capabilities and lookup tables.
        /// </summary>
        [McpServerTool(Name = "robotics_read_controller")]
        [Description("Reads a Robot Intent controller's declared capabilities: SupportedIntents, SupportedFacets, " +
            "frames, tools, locations, axes, outputs, and programs. This tool reports what the server declares and " +
            "does not infer missing capabilities or command authority.")]
        public static async Task<RobotIntentControllerInfo> ReadControllerAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.ReadAsync(ct).ConfigureAwait(false);
        }
    }
}
