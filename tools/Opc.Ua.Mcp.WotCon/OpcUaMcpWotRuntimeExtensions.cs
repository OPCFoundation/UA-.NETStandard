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
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers opt-in live WoT composition independently from the non-executing document catalogue.
    /// </summary>
    public static class OpcUaMcpWotRuntimeExtensions
    {
        /// <summary>
        /// Registers bounded preparation and lazy existing executors; no protocol channel is opened.
        /// Caller authority, network rules and named-runtime leases remain in MCP Core.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpWotRuntime(
            this IServiceCollection services, WotRuntimeOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            options ??= new WotRuntimeOptions();
            options.Validate();
            services.TryAddSingleton(options);
            services.AddOpcUaMcpWotCon();
            services.AddOpcUaMcpSecretReferenceDefaults();
            services.TryAddSingleton<WotBindingPreparation>();
            services.TryAddSingleton<IWotRuntimeExecutorFactory>(provider => new WotRuntimeExecutorFactory(
                provider.GetRequiredService<WotRuntimeOptions>(),
                provider.GetRequiredService<OpcUaSessionManager>().Telemetry));
            services.TryAddSingleton(provider => new WotRuntimeTools(
                provider.GetRequiredService<WotBindingPreparation>(),
                provider.GetRequiredService<OpcUaSessionManager>(),
                provider.GetRequiredService<McpRuntimeRegistry>(),
                provider.GetRequiredService<IMcpOperationPolicy>(),
                provider.GetRequiredService<McpNetworkPolicy>(),
                provider.GetRequiredService<IWotRuntimeExecutorFactory>(),
                provider.GetRequiredService<WotRuntimeOptions>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<OpcUaSessionManager>().Telemetry,
                provider.GetRequiredService<McpSecretReferences>()));
            return services;
        }

        /// <summary>
        /// Selects the live runtime catalogue without granting host or caller execution permission.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaWotRuntimeTools(
            this IMcpServerBuilder builder, McpToolProfile profile = McpToolProfile.Full)
        {
            return builder.WithOpcUaWotRuntimeTools(new McpToolProfileSet(profile));
        }

        /// <summary>
        /// Composes ten live tools and deduplicated connection tools exactly once.
        /// Full is a catalogue selection, not a capability grant.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaWotRuntimeTools(
            this IMcpServerBuilder builder, McpToolProfileSet profiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if ((!profiles.Contains(McpToolProfile.WotRuntime) && !profiles.Contains(McpToolProfile.Full)) ||
                builder.Services.Any(descriptor => descriptor.ServiceType == typeof(CatalogueMarker)))
            {
                return builder;
            }
            builder.Services.AddSingleton<CatalogueMarker>();
            builder.WithOpcUaConnectionTools();
            builder.WithRequestFilters(filters => filters.AddListToolsFilter(WotRuntimeMcpFilters.Catalogue));
            int first = builder.Services.Count;
            builder.WithTools<WotRuntimeTools>(WotRuntimeMcpFilters.SerializerOptions);
            for (int index = first; index < builder.Services.Count; index++)
            {
                ServiceDescriptor descriptor = builder.Services[index];
                if (descriptor.ServiceType == typeof(McpServerTool) &&
                    descriptor.ImplementationFactory is { } factory)
                {
                    builder.Services[index] = ServiceDescriptor.Describe(typeof(McpServerTool),
                        provider => WotRuntimeMcpFilters.Wrap((McpServerTool)factory(provider)), descriptor.Lifetime);
                }
            }
            return builder;
        }

        /// <summary>
        /// Marks a single composed live catalogue.
        /// </summary>
        private sealed class CatalogueMarker;
    }
}
