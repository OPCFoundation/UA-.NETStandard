// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Schema;
using Opc.Ua.Schema.Bsd;
using Opc.Ua.Schema.Json;
using Opc.Ua.Schema.Xsd;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Embeds schema workflows without registering dynamic code generation or establishing a UA connection.
    /// </summary>
    public static class OpcUaMcpSchemaExtensions
    {
        /// <summary>
        /// Registers host-owned limits, definition sources and SDK schema generators.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpSchema(
            this IServiceCollection services, McpSchemaOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            options ??= new McpSchemaOptions();
            options.Validate();
            services.TryAddSingleton(options);
            services.TryAddSingleton<DataTypeDefinitionRegistry>();
            services.TryAddSingleton<IDataTypeDefinitionResolver>(
                provider => provider.GetRequiredService<DataTypeDefinitionRegistry>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IUaSchemaGenerator, XsdSchemaGenerator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IUaSchemaGenerator, BsdSchemaGenerator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IUaSchemaGenerator, JsonSchemaGenerator>());
            services.TryAddSingleton(provider => new SchemaTools(
                provider.GetRequiredService<OpcUaSessionManager>(),
                provider.GetRequiredService<McpFileTransfers>(),
                provider.GetRequiredService<OpcUaMcpOptions>(),
                provider.GetRequiredService<McpSchemaOptions>(),
                provider.GetService<IServiceMessageContext>(),
                provider.GetRequiredService<IDataTypeDefinitionResolver>(),
                [.. provider.GetServices<IUaSchemaGenerator>()]));
            return services;
        }

        /// <summary>
        /// Adds the six schema tools and deduplicated connection tools for their explicit Session source.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaSchemaTools(
            this IMcpServerBuilder builder, McpToolProfile profile = McpToolProfile.Full)
        {
            return builder.WithOpcUaSchemaTools(new McpToolProfileSet(profile));
        }

        /// <summary>
        /// Composes the bounded schemas catalogue exactly once; local operations still need no session.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaSchemaTools(
            this IMcpServerBuilder builder, McpToolProfileSet profiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if ((!profiles.Contains(McpToolProfile.Schemas) && !profiles.Contains(McpToolProfile.Full)) ||
                builder.Services.Any(service => service.ServiceType == typeof(SchemaCatalogueMarker)))
            {
                return builder;
            }
            builder.Services.AddSingleton<SchemaCatalogueMarker>();
            builder.WithOpcUaConnectionTools();
            int first = builder.Services.Count;
            builder.WithTools<SchemaTools>(s_toolJsonOptions);
            for (int index = first; index < builder.Services.Count; index++)
            {
                ServiceDescriptor descriptor = builder.Services[index];
                if (descriptor.ServiceType == typeof(McpServerTool) &&
                    descriptor.ImplementationFactory is { } factory)
                {
                    builder.Services[index] = ServiceDescriptor.Describe(typeof(McpServerTool),
                        services => SchemaMcpFilters.Wrap((McpServerTool)factory(services)), descriptor.Lifetime);
                }
            }
            return builder;
        }

        /// <summary>
        /// Combines generated schema inputs with the SDK's protocol and scalar contracts without adding reflection.
        /// </summary>
        private static JsonSerializerOptions CreateToolJsonOptions()
        {
            var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    SchemaInputJsonContext.Default, McpJsonUtilities.DefaultOptions.TypeInfoResolver!)
            };
            options.MakeReadOnly();
            return options;
        }

        /// <summary>Immutable input and MCP protocol serialization contracts shared by the six tools.</summary>
        private static readonly JsonSerializerOptions s_toolJsonOptions = CreateToolJsonOptions();

        /// <summary>
        /// Prevents duplicate domain registrations when profiles or host modules are composed repeatedly.
        /// </summary>
        private sealed class SchemaCatalogueMarker;
    }
}
