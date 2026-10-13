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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers the OPC UA PubSub MCP tools and the runtime they need on a
    /// host that is building its own MCP server.
    /// </summary>
    /// <remarks>
    /// These tools cover the PubSub runtime - starting and stopping publishers
    /// and subscribers - together with PubSub actions and discovery. Packet
    /// capture of PubSub traffic lives in
    /// <c>Opc.Ua.Mcp.PubSub.Diagnostics</c> so that a host wanting the runtime
    /// does not also take a dependency on the capture stack.
    /// </remarks>
    public static class OpcUaMcpPubSubExtensions
    {
        /// <summary>
        /// Registers the PubSub runtime manager the PubSub tools resolve.
        /// </summary>
        /// <param name="services">The service collection to add to.</param>
        /// <returns>The service collection, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>.
        /// </exception>
        public static IServiceCollection AddOpcUaMcpPubSub(this IServiceCollection services)
        {
            return services.AddOpcUaMcpPubSub(new McpPubSubOptions());
        }

        /// <summary>
        /// Registers bounded named applications, remote configuration, declarative adapters/bridges and SKS.
        /// Host capability and target grants remain separate, default-deny decisions.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpPubSub(
            this IServiceCollection services, McpPubSubOptions options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            services.TryAddSingleton(options);
            services.AddOpcUaMcpSecretReferenceDefaults();
            services.TryAddSingleton<PubSubRuntimeManager>();
            services.TryAddSingleton<McpPubSubKeyReferences>();
            services.TryAddSingleton<IMcpPubSubTransportProvider>(provider => new McpPubSubTransportProvider(
                provider.GetRequiredService<IMcpOperationPolicy>(),
                provider.GetRequiredService<McpNetworkPolicy>(),
                provider.GetRequiredService<McpPubSubOptions>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetService<IOptions<DtlsTransportOptions>>(),
                provider.GetService<DtlsProfileRegistry>(),
                provider.GetService<IDtlsContextFactory>(),
                provider.GetService<IEthernetFrameChannelFactory>(),
                provider.GetService<ApplicationConfiguration>()));
            services.TryAddSingleton<McpPubSubRuntimeManager>();
            services.TryAddTransient<PubSubApplicationTools>();
            services.TryAddTransient<PubSubConfigurationTools>();
            services.TryAddTransient<PubSubSecurityTools>();
            services.TryAddTransient<PubSubAdapterTools>();
            services.TryAddTransient<PubSubBridgeTools>();
            return services;
        }

        /// <summary>
        /// Registers the PubSub tools when <paramref name="toolProfile"/>
        /// selects them.
        /// </summary>
        /// <remarks>
        /// A profile that does not name PubSub contributes nothing rather than
        /// failing, so a host can pass one profile to every OPC UA tool package
        /// it references.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <param name="toolProfile">The profile selecting the tool set.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="toolProfile"/> is not a defined profile.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaPubSubTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            if (!Enum.IsDefined(toolProfile))
            {
                throw new ArgumentOutOfRangeException(nameof(toolProfile), toolProfile, "Unknown MCP tool profile.");
            }
            return mcpServerBuilder.WithOpcUaPubSubTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Registers the PubSub tools when the composed
        /// <paramref name="toolProfiles"/> includes
        /// <see cref="McpToolProfile.PubSub"/>.
        /// </summary>
        /// <remarks>
        /// This overload is the composition entry point a host uses when it
        /// wants the PubSub runtime tools alongside other bounded profiles.
        /// Legacy and named-runtime profiles do not add connection tools.
        /// Configuration, bridge and security profiles include deduplicated connection tools for borrowed sessions.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <param name="toolProfiles">The composed set of profiles.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaPubSubTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            bool full = toolProfiles.Contains(McpToolProfile.Full);
            bool legacy = full || toolProfiles.Contains(McpToolProfile.PubSub);
            bool runtime = full || toolProfiles.Contains(McpToolProfile.PubSubRuntime);
            bool configuration = full || toolProfiles.Contains(McpToolProfile.PubSubConfiguration);
            bool bridge = full || toolProfiles.Contains(McpToolProfile.PubSubBridge);
            bool security = full || toolProfiles.Contains(McpToolProfile.PubSubSecurity);
            if (!legacy && !runtime && !configuration && !bridge && !security)
            {
                return mcpServerBuilder;
            }
            mcpServerBuilder.WithOpcUaMcpCallerIdentity();
            if (configuration || bridge || security)
            {
                mcpServerBuilder.WithOpcUaConnectionTools();
            }
            if (legacy && Mark<LegacyMarker>(mcpServerBuilder.Services))
            {
                mcpServerBuilder.WithTools<PubSubActionTools>()
                    .WithTools<PubSubDiscoveryTools>().WithTools<PubSubRuntimeTools>();
            }
            int first = mcpServerBuilder.Services.Count;
            if (Mark<FilterMarker>(mcpServerBuilder.Services))
            {
                mcpServerBuilder.WithRequestFilters(filters =>
                    filters.AddListToolsFilter(PubSubMcpFilters.Catalogue));
            }
            if (runtime && Mark<RuntimeMarker>(mcpServerBuilder.Services))
            {
                mcpServerBuilder.WithTools<PubSubApplicationTools>(McpPubSubToolJson.Options);
            }
            if (configuration && Mark<ConfigurationMarker>(mcpServerBuilder.Services))
            {
                mcpServerBuilder.WithTools<PubSubConfigurationTools>(McpPubSubToolJson.Options);
            }
            if (bridge && Mark<BridgeMarker>(mcpServerBuilder.Services))
            {
                mcpServerBuilder.WithTools<PubSubBridgeTools>(McpPubSubToolJson.Options)
                    .WithTools<PubSubAdapterTools>(McpPubSubToolJson.Options);
            }
            if (security && Mark<SecurityMarker>(mcpServerBuilder.Services))
            {
                mcpServerBuilder.WithTools<PubSubSecurityTools>(McpPubSubToolJson.Options);
            }
            for (int index = first; index < mcpServerBuilder.Services.Count; index++)
            {
                ServiceDescriptor descriptor = mcpServerBuilder.Services[index];
                if (descriptor.ServiceType == typeof(McpServerTool) &&
                    descriptor.ImplementationFactory is { } factory)
                {
                    mcpServerBuilder.Services[index] = ServiceDescriptor.Describe(typeof(McpServerTool),
                        services => PubSubMcpFilters.Wrap((McpServerTool)factory(services)), descriptor.Lifetime);
                }
            }
            return mcpServerBuilder;
        }

        /// <summary>
        /// Deduplicates registration across mixed single/composed calls.
        /// </summary>
        /// <typeparam name="T">The private registration marker.</typeparam>
        private static bool Mark<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            IServiceCollection services) where T : class
        {
            if (services.Any(descriptor => descriptor.ServiceType == typeof(T)))
            {
                return false;
            }
            services.AddSingleton<T>();
            return true;
        }

        /// <summary>
        /// Marks legacy tools.
        /// </summary>
        private sealed class LegacyMarker;

        /// <summary>
        /// Marks runtime tools.
        /// </summary>
        private sealed class RuntimeMarker;

        /// <summary>
        /// Marks configuration tools.
        /// </summary>
        private sealed class ConfigurationMarker;

        /// <summary>
        /// Marks bridge tools.
        /// </summary>
        private sealed class BridgeMarker;

        /// <summary>
        /// Marks security tools.
        /// </summary>
        private sealed class SecurityMarker;

        /// <summary>
        /// Marks one catalogue filter.
        /// </summary>
        private sealed class FilterMarker;
    }
}
