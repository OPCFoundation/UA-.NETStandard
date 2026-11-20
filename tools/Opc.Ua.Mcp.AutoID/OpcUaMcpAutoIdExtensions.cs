/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers the OPC 30010 AutoID tools in an embedding MCP host.
    /// </summary>
    public static class OpcUaMcpAutoIdExtensions
    {
        /// <summary>
        /// Registers per-call AutoID tools without retaining clients or sessions.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpAutoId(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddTransient<AutoIdTools>();
            return services;
        }

        /// <summary>
        /// Adds AutoID and connection tools for the AutoId or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaAutoIdTools(
            this IMcpServerBuilder builder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.WithOpcUaAutoIdTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds AutoID tools to a composed catalog, with idempotent connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaAutoIdTools(
            this IMcpServerBuilder builder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (toolProfiles.Contains(McpToolProfile.AutoId) || toolProfiles.Contains(McpToolProfile.Full))
            {
                builder.WithOpcUaConnectionTools().WithTools<AutoIdTools>();
            }
            return builder;
        }
    }
}
