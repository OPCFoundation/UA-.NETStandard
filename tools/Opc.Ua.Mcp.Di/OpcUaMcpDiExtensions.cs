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
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers the Device Integration MCP runtime and tool catalogue.
    /// </summary>
    public static class OpcUaMcpDiExtensions
    {
        /// <summary>
        /// Registers DI tools without replacing the host's session manager or transfer policy.
        /// Register the shared runtime with AddOpcUaMcpCore first.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpDi(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<McpFileTransfers>();
            services.TryAddSingleton<DiReadTools>();
            services.TryAddSingleton<DiCommandTools>();
            services.TryAddSingleton<DiSoftwareUpdateTools>();
            return services;
        }

        /// <summary>
        /// Adds DI tools for the DI or Full profile, together with shared connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaDiTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return mcpServerBuilder.WithOpcUaDiTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds DI tools when selected in a composed profile set.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaDiTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            if (!toolProfiles.Contains(McpToolProfile.Di) && !toolProfiles.Contains(McpToolProfile.Full))
            {
                return mcpServerBuilder;
            }
            return mcpServerBuilder
                .WithOpcUaConnectionTools()
                .WithRequestFilters(filters => filters.AddListToolsFilter(DiMcpFilters.AddInstallSchemas))
                .WithTools<DiReadTools>()
                .WithTools<DiCommandTools>()
                .WithTools<DiSoftwareUpdateTools>();
        }
    }
}
