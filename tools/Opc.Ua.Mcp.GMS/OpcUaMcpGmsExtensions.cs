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
using Opc.Ua.GMS.Client;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Embeds bounded GMS workflows without creating a second OPC UA application or connection manager.
    /// </summary>
    public static class OpcUaMcpGmsExtensions
    {
        /// <summary>
        /// Registers a typed-client accessor borrowing named sessions from the host's shared session manager.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpGms(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<GmsClientOptions>();
            services.TryAddSingleton<GmsClientAccessor>();
            return services;
        }

        /// <summary>
        /// Selects the native GMS catalog for Gms or Full; other profiles add nothing.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaGmsTools(
            this IMcpServerBuilder mcpServerBuilder, McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return toolProfile is McpToolProfile.Gms or McpToolProfile.Full
                ? Register(mcpServerBuilder) : mcpServerBuilder;
        }

        /// <summary>
        /// Selects GMS in a composed catalog while deduplicating shared connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaGmsTools(
            this IMcpServerBuilder mcpServerBuilder, McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);
            return toolProfiles.Contains(McpToolProfile.Gms) || toolProfiles.Contains(McpToolProfile.Full)
                ? Register(mcpServerBuilder) : mcpServerBuilder;
        }

        /// <summary>
        /// Registers the catalog and schema enrichment once per service collection.
        /// </summary>
        private static IMcpServerBuilder Register(IMcpServerBuilder builder)
        {
            if (!builder.Services.Any(static service => service.ServiceType == typeof(Registration)))
            {
                builder.Services.AddSingleton(new Registration());
                builder.WithTools<GmsTools>()
                    .WithRequestFilters(filters => filters.AddListToolsFilter(GmsMcpFilters.AddSchemas));
            }
            return builder.WithOpcUaConnectionTools();
        }

        /// <summary>
        /// Marks this package's catalog as registered without affecting other profiles.
        /// </summary>
        private sealed class Registration
        {
        }
    }
}
