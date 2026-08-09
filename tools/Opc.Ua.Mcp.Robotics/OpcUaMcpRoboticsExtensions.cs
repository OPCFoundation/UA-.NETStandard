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

using System;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers the OPC UA Robot Intent MCP tools and the runtime they need on a host.
    /// </summary>
    public static class OpcUaMcpRoboticsExtensions
    {
        /// <summary>
        /// Registers the Robot Intent controller manager the Robotics tools resolve.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpRobotics(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<RoboticsIntentManager>();
            return services;
        }

        /// <summary>
        /// Registers the Robot Intent tools when <paramref name="toolProfile"/> selects them.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaRoboticsTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            switch (toolProfile)
            {
                case McpToolProfile.Robotics:
                case McpToolProfile.Full:
                    mcpServerBuilder
                        .WithTools<RoboticsDiscoveryTools>()
                        .WithTools<RoboticsMonitoringTools>()
                        .WithTools<RoboticsControlTools>()
                        .WithTools<RoboticsMissionTools>();
                    break;
                case McpToolProfile.Core:
                case McpToolProfile.Services:
                case McpToolProfile.Administration:
                case McpToolProfile.PubSub:
                case McpToolProfile.Diagnostics:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(toolProfile),
                        toolProfile,
                        "Unknown MCP tool profile.");
            }

            return mcpServerBuilder;
        }
    }
}
