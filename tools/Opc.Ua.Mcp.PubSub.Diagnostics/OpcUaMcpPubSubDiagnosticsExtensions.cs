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
using Opc.Ua.PubSub.Pcap;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers the OPC UA PubSub protocol diagnostics MCP tools - PubSub
    /// packet capture and decode - on a host that is building its own MCP
    /// server.
    /// </summary>
    /// <remarks>
    /// The decode tool loads PubSub key material, so it stays behind the same
    /// opt-in gate the rest of the diagnostics tooling uses and is off unless a
    /// host asks for it.
    /// </remarks>
    public static class OpcUaMcpPubSubDiagnosticsExtensions
    {
        /// <summary>
        /// Registers the PubSub capture services the tools resolve.
        /// </summary>
        /// <remarks>
        /// A host also needs <c>AddOpcUaMcpDiagnostics</c> from
        /// <c>Opc.Ua.Mcp.Diagnostics</c>, which registers the capture session
        /// store and options these tools share with the UA-TCP capture tools.
        /// </remarks>
        /// <param name="services">The service collection to add to.</param>
        /// <returns>The service collection, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>.
        /// </exception>
        public static IServiceCollection AddOpcUaMcpPubSubDiagnostics(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddPubSubPcap();
            return services;
        }

        /// <summary>
        /// Registers the PubSub diagnostics tools when
        /// <paramref name="toolProfile"/> selects them.
        /// </summary>
        /// <remarks>
        /// A profile that names neither PubSub nor diagnostics contributes
        /// nothing rather than failing, so a host can pass one profile to every
        /// OPC UA tool package it references. The key-loading decode tool is
        /// registered only when <paramref name="diagnosticsToolsEnabled"/> is
        /// <c>true</c>.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <param name="toolProfile">The profile selecting the tool set.</param>
        /// <param name="diagnosticsToolsEnabled">
        /// Whether the key-loading decode tool is opted in.
        /// </param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="toolProfile"/> is not a defined profile.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaPubSubDiagnosticsTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full,
            bool diagnosticsToolsEnabled = false)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            switch (toolProfile)
            {
                case McpToolProfile.PubSub:
                case McpToolProfile.Full:
                    mcpServerBuilder.WithRequestFilters(filters =>
                    {
                        filters.AddCallToolFilter(PubSubPcapMcpFilters.SurfaceDiagnosticsErrors);
                    });
                    mcpServerBuilder.WithTools<PubSubCaptureTools>();

                    if (diagnosticsToolsEnabled)
                    {
                        mcpServerBuilder.WithTools<PubSubDecodeTools>();
                    }

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
