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
    /// Registers the read-only OPC 40223 Pumps tools in an embedding MCP host.
    /// </summary>
    public static class OpcUaMcpPumpsExtensions
    {
        /// <summary>
        /// Registers per-call Pumps tools without retaining clients or sessions.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpPumps(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddTransient<PumpsTools>();
            return services;
        }

        /// <summary>
        /// Adds Pumps and connection tools for the Pumps or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaPumpsTools(
            this IMcpServerBuilder builder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.WithOpcUaPumpsTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds Pumps tools to a composed catalog, with idempotent connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaPumpsTools(
            this IMcpServerBuilder builder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (toolProfiles.Contains(McpToolProfile.Pumps) || toolProfiles.Contains(McpToolProfile.Full))
            {
                builder.WithOpcUaConnectionTools().WithTools<PumpsTools>();
            }
            return builder;
        }
    }
}
