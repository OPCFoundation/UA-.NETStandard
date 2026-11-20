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
    /// Registers the read-only RSL and GPOS MCP companion tools.
    /// </summary>
    public static class OpcUaMcpPositioningExtensions
    {
        /// <summary>
        /// Registers the per-call client accessor without replacing host registrations.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpPositioning(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<PositioningClientAccessor>();
            return services;
        }

        /// <summary>
        /// Contributes tools only to the Positioning or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaPositioningTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            if (!Enum.IsDefined(toolProfile))
            {
                throw new ArgumentOutOfRangeException(nameof(toolProfile), toolProfile, "Unknown MCP tool profile.");
            }
            if (toolProfile is not (McpToolProfile.Positioning or McpToolProfile.Full))
            {
                return mcpServerBuilder;
            }
            if (toolProfile == McpToolProfile.Positioning)
            {
                mcpServerBuilder.WithOpcUaConnectionTools();
            }
            return RegisterTools(mcpServerBuilder);
        }

        /// <summary>
        /// Contributes Positioning and idempotent connection tools to a composed profile set.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaPositioningTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return toolProfiles.Contains(McpToolProfile.Positioning) || toolProfiles.Contains(McpToolProfile.Full)
                ? RegisterTools(mcpServerBuilder.WithOpcUaConnectionTools())
                : mcpServerBuilder;
        }

        /// <summary>
        /// Registers the finite positioning catalog.
        /// </summary>
        private static IMcpServerBuilder RegisterTools(IMcpServerBuilder builder)
        {
            return builder
                .WithTools<RelativePositioningTools>()
                .WithTools<GlobalPositioningTools>();
        }
    }
}
