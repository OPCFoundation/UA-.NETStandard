/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers OPC 40001 Machinery tools in an embedding MCP host.
    /// </summary>
    public static class OpcUaMcpMachineryExtensions
    {
        /// <summary>
        /// Registers Machinery tools; each invocation resolves its named session anew.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpMachinery(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddTransient<MachineryTools>();
            return services;
        }

        /// <summary>
        /// Adds Machinery and connection tools for the Machinery or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaMachineryTools(
            this IMcpServerBuilder builder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.WithOpcUaMachineryTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds Machinery tools to a composed catalog, with idempotent connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaMachineryTools(
            this IMcpServerBuilder builder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (toolProfiles.Contains(McpToolProfile.Machinery) || toolProfiles.Contains(McpToolProfile.Full))
            {
                builder.WithOpcUaConnectionTools().WithTools<MachineryTools>();
            }
            return builder;
        }
    }
}
