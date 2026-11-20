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
