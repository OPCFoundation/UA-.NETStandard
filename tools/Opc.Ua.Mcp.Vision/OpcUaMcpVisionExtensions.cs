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
    /// Registers the OPC UA Vision MCP tools and the runtime they need on a host.
    /// </summary>
    public static class OpcUaMcpVisionExtensions
    {
        /// <summary>
        /// Registers the Vision client accessor the Vision tools resolve.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpVision(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<VisionClientAccessor>();
            return services;
        }

        /// <summary>
        /// Registers the Vision tools when <paramref name="toolProfile"/> selects them.
        /// </summary>
        /// <remarks>
        /// The bounded <see cref="McpToolProfile.Vision"/> catalogue also carries the connection
        /// tools, because every Vision tool resolves a named OPC UA session and only those
        /// tools can open one. <see cref="McpToolProfile.Full"/> already carries them through the
        /// core package, so they are not added twice.
        /// </remarks>
        public static IMcpServerBuilder WithOpcUaVisionTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            switch (toolProfile)
            {
                case McpToolProfile.Vision:
                case McpToolProfile.Full:
                    mcpServerBuilder
                        .WithTools<VisionDiscoveryTools>()
                        .WithTools<VisionMonitoringTools>()
                        .WithTools<VisionSeeingTools>()
                        .WithTools<VisionInferenceTools>()
                        .WithTools<VisionFeedbackTools>()
                        .WithTools<VisionGeometryTools>();

                    if (toolProfile == McpToolProfile.Vision)
                    {
                        // Every Vision tool resolves a named OPC UA session and only the connection
                        // tools can open one, so the bounded vision catalogue has to carry them to
                        // be usable at all. Full already gets them from the core package, so adding
                        // them there would register the same tools twice.
                        mcpServerBuilder.WithTools<ConnectionTools>();
                    }

                    break;
                case McpToolProfile.Core:
                case McpToolProfile.Services:
                case McpToolProfile.Administration:
                case McpToolProfile.PubSub:
                case McpToolProfile.Diagnostics:
                case McpToolProfile.Robotics:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(toolProfile),
                        toolProfile,
                        "Unknown MCP tool profile.");
            }

            return mcpServerBuilder;
        }

        /// <summary>
        /// Registers the Vision tools when the composed <paramref name="toolProfiles"/>
        /// includes <see cref="McpToolProfile.Vision"/>.
        /// </summary>
        /// <remarks>
        /// This overload is the composition entry point a host uses when it
        /// wants the Vision tools alongside the tools of another bounded
        /// profile - <see cref="McpToolProfile.Robotics"/>, for a vision-guided
        /// pick-and-place agent, for example. It never registers
        /// <see cref="Tools.ConnectionTools"/> directly; the
        /// <c>McpToolProfileSet</c> overload of <c>WithOpcUaCoreTools</c> owns
        /// that registration and deduplicates it across every package that
        /// contributes to the same MCP server.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <param name="toolProfiles">The composed set of profiles.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaVisionTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            if (!toolProfiles.Contains(McpToolProfile.Vision) &&
                !toolProfiles.Contains(McpToolProfile.Full))
            {
                return mcpServerBuilder;
            }

            return mcpServerBuilder
                .WithTools<VisionDiscoveryTools>()
                .WithTools<VisionMonitoringTools>()
                .WithTools<VisionSeeingTools>()
                .WithTools<VisionInferenceTools>()
                .WithTools<VisionFeedbackTools>()
                .WithTools<VisionGeometryTools>();
        }
    }
}

