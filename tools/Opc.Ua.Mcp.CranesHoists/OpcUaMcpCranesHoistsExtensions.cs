// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Embeds the bounded crane catalog using the host's shared OPC UA session manager.
    /// </summary>
    public static class OpcUaMcpCranesHoistsExtensions
    {
        /// <summary>
        /// Registers the typed-client accessor without creating another OPC UA application or session manager.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpCranesHoists(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<CranesHoistsClientOptions>();
            services.TryAddSingleton<CranesHoistsClientAccessor>();
            return services;
        }

        /// <summary>
        /// Selects crane tools for CranesHoists or Full; other profiles contribute no tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaCranesHoistsTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return toolProfile is McpToolProfile.CranesHoists or McpToolProfile.Full
                ? Register(mcpServerBuilder) : mcpServerBuilder;
        }

        /// <summary>
        /// Selects crane tools for a composed catalog without duplicating shared connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaCranesHoistsTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return toolProfiles.Contains(McpToolProfile.CranesHoists) || toolProfiles.Contains(McpToolProfile.Full)
                ? Register(mcpServerBuilder) : mcpServerBuilder;
        }

        /// <summary>
        /// Registers tools and schema enrichment once, sharing the core connection-tool deduplication.
        /// </summary>
        private static IMcpServerBuilder Register(IMcpServerBuilder builder)
        {
            if (!builder.Services.Any(static service => service.ServiceType == typeof(Registration)))
            {
                builder.Services.AddSingleton(new Registration());
                builder.WithTools<CranesHoistsTools>()
                    .WithRequestFilters(filters => filters.AddListToolsFilter(CranesHoistsMcpFilters.AddSchemas));
            }
            return builder.WithOpcUaConnectionTools();
        }

        /// <summary>
        /// Marks the catalog as registered on this builder only.
        /// </summary>
        private sealed class Registration
        {
        }
    }
}
