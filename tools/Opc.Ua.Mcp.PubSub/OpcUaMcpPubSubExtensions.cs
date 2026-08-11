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
    /// Registers the OPC UA PubSub MCP tools and the runtime they need on a
    /// host that is building its own MCP server.
    /// </summary>
    /// <remarks>
    /// These tools cover the PubSub runtime - starting and stopping publishers
    /// and subscribers - together with PubSub actions and discovery. Packet
    /// capture of PubSub traffic lives in
    /// <c>Opc.Ua.Mcp.PubSub.Diagnostics</c> so that a host wanting the runtime
    /// does not also take a dependency on the capture stack.
    /// </remarks>
    public static class OpcUaMcpPubSubExtensions
    {
        /// <summary>
        /// Registers the PubSub runtime manager the PubSub tools resolve.
        /// </summary>
        /// <param name="services">The service collection to add to.</param>
        /// <returns>The service collection, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>.
        /// </exception>
        public static IServiceCollection AddOpcUaMcpPubSub(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<PubSubRuntimeManager>();
            return services;
        }

        /// <summary>
        /// Registers the PubSub tools when <paramref name="toolProfile"/>
        /// selects them.
        /// </summary>
        /// <remarks>
        /// A profile that does not name PubSub contributes nothing rather than
        /// failing, so a host can pass one profile to every OPC UA tool package
        /// it references.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <param name="toolProfile">The profile selecting the tool set.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="toolProfile"/> is not a defined profile.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaPubSubTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            switch (toolProfile)
            {
                case McpToolProfile.PubSub:
                case McpToolProfile.Full:
                    mcpServerBuilder
                        .WithTools<PubSubActionTools>()
                        .WithTools<PubSubDiscoveryTools>()
                        .WithTools<PubSubRuntimeTools>();
                    break;
                case McpToolProfile.Core:
                case McpToolProfile.Services:
                case McpToolProfile.Administration:
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
    }
}
