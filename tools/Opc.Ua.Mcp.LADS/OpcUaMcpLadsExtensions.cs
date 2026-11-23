// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers OPC 30500-1 LADS tools in an embedding MCP host.
    /// </summary>
    public static class OpcUaMcpLadsExtensions
    {
        /// <summary>
        /// Registers per-call LADS tools without retaining clients or sessions.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpLads(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddTransient<LadsClientAccessor>();
            services.TryAddTransient<LadsTools>();
            return services;
        }

        /// <summary>
        /// Adds LADS and connection tools for the Lads or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaLadsTools(
            this IMcpServerBuilder builder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.WithOpcUaLadsTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds LADS tools to a composed catalog, with idempotent connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaLadsTools(
            this IMcpServerBuilder builder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (toolProfiles.Contains(McpToolProfile.Lads) || toolProfiles.Contains(McpToolProfile.Full))
            {
                builder.WithOpcUaConnectionTools();
                if (!builder.Services.Any(static service =>
                    service.ServiceType == typeof(LadsRegistrationMarker)))
                {
                    builder.Services.AddSingleton(new LadsRegistrationMarker());
                    builder.WithTools<LadsTools>();
                }
            }
            return builder;
        }

        private sealed class LadsRegistrationMarker
        {
        }
    }
}
