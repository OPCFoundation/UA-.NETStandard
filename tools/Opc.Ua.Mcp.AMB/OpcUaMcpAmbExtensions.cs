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
    /// Registers the Asset Management Basics MCP runtime and tool catalogue.
    /// </summary>
    public static class OpcUaMcpAmbExtensions
    {
        /// <summary>
        /// Registers the session-scoped AMB client accessor without replacing a host registration.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpAmb(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<AmbClientAccessor>();
            return services;
        }

        /// <summary>
        /// Adds AMB tools for the AMB or Full profile, including deduplicated connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaAmbTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return mcpServerBuilder.WithOpcUaAmbTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds AMB tools when selected in a composed profile set.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaAmbTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            if (!toolProfiles.Contains(McpToolProfile.Amb) && !toolProfiles.Contains(McpToolProfile.Full))
            {
                return mcpServerBuilder;
            }
            return mcpServerBuilder
                .WithOpcUaConnectionTools()
                .WithTools<AmbReadTools>()
                .WithTools<AmbCommandTools>();
        }
    }
}
