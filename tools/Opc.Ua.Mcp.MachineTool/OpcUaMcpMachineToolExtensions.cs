/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Embeds MachineTool and wireless peripherals as one catalog using existing host-owned UA connections.
    /// </summary>
    public static class OpcUaMcpMachineToolExtensions
    {
        /// <summary>
        /// Registers typed access without opening sessions or registering another OPC UA application.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpMachineTool(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<MachineToolClientAccessor>();
            return services;
        }

        /// <summary>
        /// Selects both MachineTool and WMTP workflows only for the MachineTool or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaMachineToolTools(
            this IMcpServerBuilder builder, McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return toolProfile is McpToolProfile.MachineTool or McpToolProfile.Full ? Register(builder) : builder;
        }

        /// <summary>
        /// Adds the combined catalog to a composed profile set, deduplicating common connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaMachineToolTools(
            this IMcpServerBuilder builder, McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return toolProfiles.Contains(McpToolProfile.MachineTool) || toolProfiles.Contains(McpToolProfile.Full)
                ? Register(builder) : builder;
        }

        /// <summary>
        /// Registers each typed catalog and its narrow schema filter once for the owning builder.
        /// </summary>
        private static IMcpServerBuilder Register(IMcpServerBuilder builder)
        {
            if (!builder.Services.Any(static service => service.ServiceType == typeof(Registration)))
            {
                builder.Services.AddSingleton(new Registration());
                builder.WithTools<MachineToolTools>();
                builder.WithTools<WmtpMcpTools>();
                builder.WithTools<MachineToolServiceTools>();
                builder.WithRequestFilters(filters =>
                {
                    filters.AddListToolsFilter(MachineToolMcpFilters.AddSchemas);
                    filters.AddListToolsFilter(WmtpMcpFilters.AddSchemas);
                    filters.AddListToolsFilter(MachineToolServiceFilters.AddSchemas);
                });
            }
            return builder.WithOpcUaConnectionTools();
        }

        /// <summary>
        /// Identifies registration on this service collection without global catalog state.
        /// </summary>
        private sealed class Registration
        {
        }
    }
}
