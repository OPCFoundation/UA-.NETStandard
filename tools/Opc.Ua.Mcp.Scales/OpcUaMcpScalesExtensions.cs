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
    /// Registers the OPC 40200 MCP companion tools.
    /// </summary>
    public static class OpcUaMcpScalesExtensions
    {
        /// <summary>
        /// Registers the per-call client accessor without replacing host registrations.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpScales(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<ScalesClientAccessor>();
            return services;
        }

        /// <summary>
        /// Contributes tools only to the Scales or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaScalesTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            if (!Enum.IsDefined(toolProfile))
            {
                throw new ArgumentOutOfRangeException(nameof(toolProfile), toolProfile, "Unknown MCP tool profile.");
            }
            if (toolProfile is not (McpToolProfile.Scales or McpToolProfile.Full))
            {
                return mcpServerBuilder;
            }
            if (toolProfile == McpToolProfile.Scales)
            {
                mcpServerBuilder.WithOpcUaConnectionTools();
            }
            return RegisterTools(mcpServerBuilder);
        }

        /// <summary>
        /// Contributes Scales tools and idempotent connection tools to a composed profile set.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaScalesTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return toolProfiles.Contains(McpToolProfile.Scales) || toolProfiles.Contains(McpToolProfile.Full)
                ? RegisterTools(mcpServerBuilder.WithOpcUaConnectionTools())
                : mcpServerBuilder;
        }

        /// <summary>
        /// Registers the finite scale read and command catalog.
        /// </summary>
        private static IMcpServerBuilder RegisterTools(IMcpServerBuilder builder)
        {
            return builder
                .WithRequestFilters(filters => filters.AddListToolsFilter(ScalesMcpFilters.AddRecipeSchema))
                .WithTools<ScalesReadTools>()
                .WithTools<ScalesCommandTools>();
        }
    }
}
