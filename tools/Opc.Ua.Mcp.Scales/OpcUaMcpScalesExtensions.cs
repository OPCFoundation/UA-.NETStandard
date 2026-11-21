// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
