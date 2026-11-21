// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers domain-neutral xRegistry MCP tools without creating another session.
    /// </summary>
    public static class OpcUaMcpXRegistryExtensions
    {
        /// <summary>
        /// Registers the tool's injectable dependencies without replacing host registrations.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpXRegistry(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddTransient<XRegistryTools>();
            return services;
        }

        /// <summary>
        /// Adds the xRegistry catalogue when selected.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaXRegistryTools(
            this IMcpServerBuilder builder, McpToolProfile profile = McpToolProfile.Full)
        {
            return builder.WithOpcUaXRegistryTools(new McpToolProfileSet(profile));
        }

        /// <summary>
        /// Adds the composed xRegistry catalogue and deduplicated connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaXRegistryTools(
            this IMcpServerBuilder builder, McpToolProfileSet profiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return profiles.Contains(McpToolProfile.XRegistry) || profiles.Contains(McpToolProfile.Full)
                ? builder.WithOpcUaConnectionTools().WithTools<XRegistryTools>()
                : builder;
        }
    }
}
