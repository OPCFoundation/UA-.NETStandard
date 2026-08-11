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
        public static async Task<ArrayOf<RobotIntentNodeLookupEntry>> ListControllersAsync(
            RoboticsIntentManager manager,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.CreateClient(sessionName).DiscoverControllersAsync(ct).ConfigureAwait(false);
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
            [Description("Controller NodeId, for example ns=2;s=RobotIntent/Controllers/Controller1.")] string controllerId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.OpenController(controllerId, sessionName).ReadAsync(ct).ConfigureAwait(false);
        }
    }
}
