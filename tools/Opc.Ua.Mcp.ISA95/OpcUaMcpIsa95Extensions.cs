// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers ISA-95 common-model and explicit Job Control tools in an MCP host.
    /// </summary>
    public static class OpcUaMcpIsa95Extensions
    {
        /// <summary>
        /// Registers ISA-95 tools without binding a session or endpoint in advance.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpIsa95(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddTransient<Isa95Tools>();
            return services;
        }

        /// <summary>
        /// Adds ISA-95 and connection tools for the Isa95 or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaIsa95Tools(
            this IMcpServerBuilder builder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.WithOpcUaIsa95Tools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds ISA-95 tools to a composed catalog, with idempotent connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaIsa95Tools(
            this IMcpServerBuilder builder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (toolProfiles.Contains(McpToolProfile.Isa95) || toolProfiles.Contains(McpToolProfile.Full))
            {
                builder.WithOpcUaConnectionTools()
                    .WithRequestFilters(filters => filters.AddListToolsFilter(Isa95McpFilters.AddInputSchemas))
                    .WithTools<Isa95Tools>();
            }
            return builder;
        }
    }
}
