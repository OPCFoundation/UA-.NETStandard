// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Combines workflow input contracts with the MCP SDK's protocol and scalar serialization contracts.
    /// </summary>
    internal static class WorkflowToolJson
    {
        /// <summary>
        /// Gets immutable tool options without modifying either source-generated context's original options.
        /// </summary>
        internal static JsonSerializerOptions Options { get; } = CreateOptions();

        /// <summary>
        /// Keeps workflow converters first while retaining the SDK's generated MCP return and primitive metadata.
        /// </summary>
        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions(WorkflowInputJsonContext.Default.Options)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    new ArrayResolver(), WorkflowInputJsonContext.Default,
                    McpJsonUtilities.DefaultOptions.TypeInfoResolver)
            };
            options.MakeReadOnly();
            return options;
        }

        /// <summary>
        /// Binds concrete root arrays without reflecting over ArrayOf memory properties.
        /// </summary>
        private sealed class ArrayResolver : IJsonTypeInfoResolver
        {
            /// <inheritdoc/>
            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            {
                if (type == typeof(ArrayOf<WorkflowDataValueInput>))
                {
                    return Array<WorkflowDataValueInput>(options);
                }
                if (type == typeof(ArrayOf<WorkflowArrayElementInput>))
                {
                    return Array<WorkflowArrayElementInput>(options);
                }
                if (type == typeof(ArrayOf<WorkflowEventFieldInput>))
                {
                    return Array<WorkflowEventFieldInput>(options);
                }
                if (type == typeof(ArrayOf<WorkflowEventPathInput>))
                {
                    return Array<WorkflowEventPathInput>(options);
                }
                if (type == typeof(ArrayOf<WorkflowHistoryEventInput>))
                {
                    return Array<WorkflowHistoryEventInput>(options);
                }
                if (type == typeof(ArrayOf<WorkflowAnnotationInput>))
                {
                    return Array<WorkflowAnnotationInput>(options);
                }
                if (type == typeof(ArrayOf<WorkflowRolePermissionInput>))
                {
                    return Array<WorkflowRolePermissionInput>(options);
                }
                return type == typeof(ArrayOf<string>)
                    ? JsonMetadataServices.CreateValueInfo<ArrayOf<string>>(options, new McpStringArrayJsonConverter())
                    : null;
            }

            /// <summary>
            /// Creates a converter-backed value contract for a known concrete request element.
            /// </summary>
            /// <typeparam name="T">A source-generated workflow element.</typeparam>
            private static JsonTypeInfo<ArrayOf<T>> Array<T>(JsonSerializerOptions options)
                where T : class
            {
                return JsonMetadataServices.CreateValueInfo<ArrayOf<T>>(options, new WorkflowInputArrayConverter<T>());
            }
        }
    }
}
