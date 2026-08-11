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
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers the OPC UA Part 4 MCP tools and the services they need on a
    /// host that is building its own MCP server.
    /// </summary>
    /// <remarks>
    /// These are the two extension points an embedding application uses:
    /// <see cref="AddOpcUaMcpCore(IServiceCollection, Action{OpcUaMcpOptions}?)"/>
    /// contributes the OPC UA client and session manager to the service
    /// collection, and
    /// <see cref="WithOpcUaCoreTools(IMcpServerBuilder, McpToolProfile)"/>
    /// contributes the tools to the MCP server. A host is free to add its own
    /// tool types alongside them:
    /// <code>
    /// builder.Services.AddOpcUaMcpCore();
    /// builder.Services.AddMcpServer()
    ///     .WithStdioServerTransport()
    ///     .WithOpcUaCoreTools(McpToolProfile.Services)
    ///     .WithTools&lt;MyApplicationTools&gt;();
    /// </code>
    /// </remarks>
    public static class OpcUaMcpCoreExtensions
    {
        private const string kApplicationName = "OPC UA MCP Server";
        private const string kApplicationUri = "urn:localhost:UA:McpServer";
        private const string kProductUri = "uri:opcfoundation.org:McpServer";

        /// <summary>
        /// Registers the OPC UA client application and the session manager the
        /// Part 4 tools resolve.
        /// </summary>
        /// <param name="services">The service collection to add to.</param>
        /// <param name="configure">
        /// Optional configuration of the <see cref="OpcUaMcpOptions"/> the
        /// tools read. When omitted the options are taken from the well-known
        /// environment variables.
        /// </param>
        /// <returns>The service collection, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>.
        /// </exception>
        public static IServiceCollection AddOpcUaMcpCore(
            this IServiceCollection services,
            Action<OpcUaMcpOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = kApplicationName;
                options.ApplicationUri = kApplicationUri;
                options.ProductUri = kProductUri;
            });
            services.AddSingleton<OpcUaSessionManager>();

            OpcUaMcpOptions options = OpcUaMcpOptions.FromEnvironment();
            configure?.Invoke(options);
            services.AddSingleton(options);
            return services;
        }

        /// <summary>
        /// Registers the OPC UA client application and the session manager,
        /// using an already-constructed options instance.
        /// </summary>
        /// <param name="services">The service collection to add to.</param>
        /// <param name="options">The options the tools read.</param>
        /// <returns>The service collection, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> or <paramref name="options"/> is <c>null</c>.
        /// </exception>
        public static IServiceCollection AddOpcUaMcpCore(
            this IServiceCollection services,
            OpcUaMcpOptions options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            services.AddOpcUa().AddClient(clientOptions =>
            {
                clientOptions.ApplicationName = kApplicationName;
                clientOptions.ApplicationUri = kApplicationUri;
                clientOptions.ProductUri = kProductUri;
            });
            services.AddSingleton<OpcUaSessionManager>();
            services.AddSingleton(options);
            return services;
        }

        /// <summary>
        /// Registers the request and schema filters that make tool errors
        /// actionable and tool schemas explicit.
        /// </summary>
        /// <remarks>
        /// A host that composes several OPC UA tool packages should call this
        /// once. The individual <c>With…Tools</c> methods do not call it
        /// themselves, because filters are server-wide rather than per tool set
        /// and registering them repeatedly would run them repeatedly.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaMcpFilters(
            this IMcpServerBuilder mcpServerBuilder)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            mcpServerBuilder.WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(McpRequestFilters.ValidateRequiredArguments);
                filters.AddListToolsFilter(McpSchemaFilters.AddExplicitRequiredArrays);
            });
            return mcpServerBuilder;
        }

        /// <summary>
        /// Registers the Part 4 tools selected by <paramref name="toolProfile"/>,
        /// together with the session resources.
        /// </summary>
        /// <remarks>
        /// A profile naming tools this package does not own - <see
        /// cref="McpToolProfile.PubSub"/> and <see
        /// cref="McpToolProfile.Diagnostics"/> - contributes nothing here rather
        /// than failing, so a host can pass one profile to every package it
        /// references and get exactly the tools those packages provide.
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
        public static IMcpServerBuilder WithOpcUaCoreTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            switch (toolProfile)
            {
                case McpToolProfile.Core:
                    AddCoreTools(mcpServerBuilder);
                    break;
                case McpToolProfile.Services:
                    AddServiceTools(mcpServerBuilder);
                    break;
                case McpToolProfile.Administration:
                    AddAdministrationTools(mcpServerBuilder);
                    break;
                case McpToolProfile.PubSub:
                case McpToolProfile.Diagnostics:
                case McpToolProfile.Robotics:
                case McpToolProfile.Vision:
                    break;
                case McpToolProfile.Full:
                    AddFullTools(mcpServerBuilder);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(toolProfile),
                        toolProfile,
                        "Unknown MCP tool profile.");
            }

            mcpServerBuilder.WithResources<SessionResources>();
            return mcpServerBuilder;
        }

        /// <summary>
        /// Registers the Part 4 tools that satisfy the composed set of
        /// profiles in <paramref name="toolProfiles"/>, together with the
        /// session resources.
        /// </summary>
        /// <remarks>
        /// This overload is the composition entry point a host uses when it
        /// wants tools from more than one bounded profile - a vision-guided
        /// pick-and-place agent that has to both see through the
        /// <c>Vision</c> tools and command a robot through the <c>Robotics</c>
        /// tools, for example. Every profile in the set that owns a Part 4
        /// tool set contributes it, deduplicated by tool type. Profiles that
        /// only rely on Part 4 tools indirectly - <see cref="McpToolProfile.Vision"/>,
        /// <see cref="McpToolProfile.Robotics"/> and <see cref="McpToolProfile.Diagnostics"/>
        /// - cause <see cref="Tools.ConnectionTools"/> to be registered exactly once
        /// through <see cref="WithOpcUaConnectionTools(IMcpServerBuilder)"/>, so
        /// composing Vision and Robotics on the same builder does not register
        /// the connection tools twice.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <param name="toolProfiles">The composed set of profiles.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaCoreTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            if (toolProfiles.Contains(McpToolProfile.Full))
            {
                AddFullTools(mcpServerBuilder);
            }
            else
            {
                var registered = new HashSet<Type>();
                if (toolProfiles.Contains(McpToolProfile.Services))
                {
                    AddServiceToolTypes(registered);
                }
                if (toolProfiles.Contains(McpToolProfile.Administration))
                {
                    AddAdministrationToolTypes(registered);
                }
                if (toolProfiles.Contains(McpToolProfile.Core))
                {
                    AddCoreToolTypes(registered);
                }
                // ConnectionTools is registered once through the shared helper
                // below, so remove it from the set of tools to register here
                // even when a Part 4 profile listed it as owned.
                registered.Remove(typeof(ConnectionTools));
                RegisterCoreToolTypes(mcpServerBuilder, registered);
            }

            if (NeedsConnectionTools(toolProfiles))
            {
                mcpServerBuilder.WithOpcUaConnectionTools();
            }

            mcpServerBuilder.WithResources<SessionResources>();
            return mcpServerBuilder;
        }

        /// <summary>
        /// Registers <see cref="Tools.ConnectionTools"/> at most once on the
        /// supplied builder, so several tool packages composed on the same MCP
        /// server share one set of connection tools instead of registering
        /// their own.
        /// </summary>
        /// <remarks>
        /// Every session-scoped OPC UA tool - Part 4 services, Vision,
        /// Robotics and Diagnostics - resolves a named session that only the
        /// connection tools can open, so a composed host has to expose them.
        /// This method uses a marker service on <see cref="IMcpServerBuilder.Services"/>
        /// to detect a prior registration on the same builder and skip a
        /// second one. Each package's <c>McpToolProfileSet</c> overload calls
        /// it, so composing Vision and Robotics yields one <c>Connect</c>
        /// entry rather than two.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaConnectionTools(
            this IMcpServerBuilder mcpServerBuilder)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            IServiceCollection services = mcpServerBuilder.Services;
            for (int i = 0; i < services.Count; i++)
            {
                if (services[i].ServiceType == typeof(ConnectionToolsMarker))
                {
                    return mcpServerBuilder;
                }
            }

            services.AddSingleton<ConnectionToolsMarker>();
            return mcpServerBuilder.WithTools<ConnectionTools>();
        }

        internal static bool NeedsConnectionTools(McpToolProfileSet toolProfiles)
        {
            return toolProfiles.Contains(McpToolProfile.Core)
                || toolProfiles.Contains(McpToolProfile.Services)
                || toolProfiles.Contains(McpToolProfile.Administration)
                || toolProfiles.Contains(McpToolProfile.Diagnostics)
                || toolProfiles.Contains(McpToolProfile.Robotics)
                || toolProfiles.Contains(McpToolProfile.Vision)
                || toolProfiles.Contains(McpToolProfile.Full);
        }

        private static void AddCoreToolTypes(HashSet<Type> registered)
        {
            registered.Add(typeof(ConfigurationReadTools));
            registered.Add(typeof(ConfigurationUpdateTools));
            registered.Add(typeof(ConnectionTools));
            registered.Add(typeof(ConvenienceTools));
        }

        private static void AddServiceToolTypes(HashSet<Type> registered)
        {
            registered.Add(typeof(AttributeServiceTools));
            registered.Add(typeof(ConfigurationReadTools));
            registered.Add(typeof(ConfigurationUpdateTools));
            registered.Add(typeof(ConnectionTools));
            registered.Add(typeof(ConvenienceTools));
            registered.Add(typeof(DiscoveryServiceTools));
            registered.Add(typeof(MethodServiceTools));
            registered.Add(typeof(MonitoredItemServiceTools));
            registered.Add(typeof(NodeManagementServiceTools));
            registered.Add(typeof(SubscriptionServiceTools));
            registered.Add(typeof(ViewServiceTools));
        }

        private static void AddAdministrationToolTypes(HashSet<Type> registered)
        {
            registered.Add(typeof(ConfigurationReadTools));
            registered.Add(typeof(ConfigurationUpdateTools));
            registered.Add(typeof(ConnectionTools));
            registered.Add(typeof(NodeSetExportTools));
            registered.Add(typeof(PkiTools));
        }

        private sealed class ConnectionToolsMarker
        {
        }

        /// <summary>
        /// Registers each tool class the composed profiles asked for, exactly
        /// once.
        /// </summary>
        /// <remarks>
        /// The set decides *what* to register and this decides *how*. It has to
        /// be a fixed list of generic calls rather than a loop over the set: the
        /// non-generic <c>WithTools(IEnumerable&lt;Type&gt;)</c> looks method
        /// metadata up dynamically, which is annotated
        /// <c>RequiresUnreferencedCode</c> and breaks the Native AOT guarantee
        /// this repository builds under.
        /// </remarks>
        private static void RegisterCoreToolTypes(
            IMcpServerBuilder mcpServerBuilder,
            HashSet<Type> registered)
        {
            if (registered.Contains(typeof(AttributeServiceTools)))
            {
                mcpServerBuilder.WithTools<AttributeServiceTools>();
            }
            if (registered.Contains(typeof(ConfigurationReadTools)))
            {
                mcpServerBuilder.WithTools<ConfigurationReadTools>();
            }
            if (registered.Contains(typeof(ConfigurationUpdateTools)))
            {
                mcpServerBuilder.WithTools<ConfigurationUpdateTools>();
            }
            if (registered.Contains(typeof(ConvenienceTools)))
            {
                mcpServerBuilder.WithTools<ConvenienceTools>();
            }
            if (registered.Contains(typeof(DiscoveryServiceTools)))
            {
                mcpServerBuilder.WithTools<DiscoveryServiceTools>();
            }
            if (registered.Contains(typeof(MethodServiceTools)))
            {
                mcpServerBuilder.WithTools<MethodServiceTools>();
            }
            if (registered.Contains(typeof(MonitoredItemServiceTools)))
            {
                mcpServerBuilder.WithTools<MonitoredItemServiceTools>();
            }
            if (registered.Contains(typeof(NodeManagementServiceTools)))
            {
                mcpServerBuilder.WithTools<NodeManagementServiceTools>();
            }
            if (registered.Contains(typeof(NodeSetExportTools)))
            {
                mcpServerBuilder.WithTools<NodeSetExportTools>();
            }
            if (registered.Contains(typeof(PkiTools)))
            {
                mcpServerBuilder.WithTools<PkiTools>();
            }
            if (registered.Contains(typeof(SubscriptionServiceTools)))
            {
                mcpServerBuilder.WithTools<SubscriptionServiceTools>();
            }
            if (registered.Contains(typeof(ViewServiceTools)))
            {
                mcpServerBuilder.WithTools<ViewServiceTools>();
            }
        }

        private static void AddCoreTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithTools<ConfigurationReadTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<ConvenienceTools>();
        }

        private static void AddServiceTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithTools<AttributeServiceTools>()
                .WithTools<ConfigurationReadTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<ConvenienceTools>()
                .WithTools<DiscoveryServiceTools>()
                .WithTools<MethodServiceTools>()
                .WithTools<MonitoredItemServiceTools>()
                .WithTools<NodeManagementServiceTools>()
                .WithTools<SubscriptionServiceTools>()
                .WithTools<ViewServiceTools>();
        }

        private static void AddAdministrationTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithTools<ConfigurationReadTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<NodeSetExportTools>()
                .WithTools<PkiTools>();
        }

        private static void AddFullTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithTools<AttributeServiceTools>()
                .WithTools<ConfigurationTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<ConvenienceTools>()
                .WithTools<DiscoveryServiceTools>()
                .WithTools<MethodServiceTools>()
                .WithTools<MonitoredItemServiceTools>()
                .WithTools<NodeManagementServiceTools>()
                .WithTools<NodeSetExportTools>()
                .WithTools<PkiTools>()
                .WithTools<SubscriptionServiceTools>()
                .WithTools<ViewServiceTools>();
        }
    }
}
