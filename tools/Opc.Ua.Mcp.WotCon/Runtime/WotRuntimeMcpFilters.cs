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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Publishes shallow source-generated input shapes and hides live execution when the host has not opted in.
    /// </summary>
    internal static class WotRuntimeMcpFilters
    {
        /// <summary>
        /// Combines the closed live inputs with the SDK's generated protocol and scalar metadata.
        /// No reflection resolver is introduced for arbitrary caller types.
        /// </summary>
        internal static JsonSerializerOptions SerializerOptions { get; } =
            new(WotRuntimeInputJsonContext.Default.Options)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    WotRuntimeInputJsonContext.Default, McpJsonUtilities.DefaultOptions.TypeInfoResolver)
            };

        /// <summary>
        /// Materializes actual array schemas without repacking inputs as JSON strings.
        /// </summary>
        internal static McpServerTool Wrap(McpServerTool tool)
        {
            return tool.ProtocolTool.Name.StartsWith("wot_runtime_", StringComparison.Ordinal)
                ? new ContractTool(tool) : tool;
        }

        /// <summary>
        /// Applies catalogue-level enablement; every operation independently checks exact caller grants.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Catalogue(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                IMcpOperationPolicy policy = request.Services!.GetRequiredService<IMcpOperationPolicy>();
                result.Tools = [.. result.Tools.Where(tool =>
                    !tool.Name.StartsWith("wot_runtime_", StringComparison.Ordinal) ||
                    (policy.IsEnabled(McpCapability.WotRuntime) &&
                        (tool.Name != "wot_runtime_write_property" || policy.IsEnabled(McpCapability.WotWrite)) &&
                        (tool.Name != "wot_runtime_invoke_action" || policy.IsEnabled(McpCapability.WotInvoke))))];
                return result;
            };
        }

        /// <summary>
        /// Publishes the closed DTO schema for each non-scalar parameter.
        /// </summary>
        private static JsonObject Shape(McpServerTool tool)
        {
            JsonObject schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
            JsonObject properties = schema["properties"]!.AsObject();
            schema["additionalProperties"] = false;
            if (tool.ProtocolTool.Name == "wot_runtime_open")
            {
                Set(properties, "document", WotRuntimeInputJsonContext.Default.WotDocumentSource.GetJsonSchemaAsNode());
                properties["document"]!["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["reference"] = "device.td.json",
                    ["filePath"] = "device.td.json"
                });
                Set(properties, "selection",
                    WotRuntimeInputJsonContext.Default.WotRuntimeSelection.GetJsonSchemaAsNode());
                properties["selection"]!["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["sha256"] = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    ["affordance"] = "temperature",
                    ["kind"] = "Property",
                    ["formIndex"] = 0,
                    ["bindingId"] = "w3c.http",
                    ["bindingVersion"] = "1.1"
                });
                Set(properties, "options",
                    WotRuntimeInputJsonContext.Default.WotRuntimeOpenOptions.GetJsonSchemaAsNode());
                properties["options"]!["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["baseUri"] = "https://mcp.invalid/device.td.json"
                });
                properties["dependencies"] = Array(
                    WotRuntimeInputJsonContext.Default.WotDocumentSource.GetJsonSchemaAsNode(), 0, 255,
                    "Explicit local dependency sources, each with a logical reference and text or TransferRoot file.");
                properties["dependencies"]!["examples"] = new JsonArray((JsonNode)new JsonArray((JsonNode)new JsonObject
                {
                    ["reference"] = "schema.json",
                    ["filePath"] = "schema.json"
                }));
                properties["credentials"] = Array(
                    WotRuntimeInputJsonContext.Default.WotRuntimeCredentialInput.GetJsonSchemaAsNode(), 0, 1,
                    "Exactly the selected security definition's mapping; values are secret references, never secrets.");
                properties["credentials"]!["examples"] = new JsonArray((JsonNode)new JsonArray((JsonNode)new JsonObject
                {
                    ["schemeName"] = "bearer_sc",
                    ["secretReference"] = "alias:line1-token"
                }));
                properties["operations"] = Array(new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(
                        "readproperty", "writeproperty", "invokeaction", "observeproperty", "subscribeevent")
                }, 1, 5, "Distinct exact operations on the selected form; no other operations become available.");
                properties["operations"]!["uniqueItems"] = true;
                properties["operations"]!["examples"] = new JsonArray(
                    (JsonNode)new JsonArray((JsonNode?)JsonValue.Create("readproperty")));
                JsonObject selection = properties["selection"]!["properties"]!.AsObject();
                selection["formIndex"]!["minimum"] = 0;
                selection["formIndex"]!["maximum"] = 1023;
                selection["sha256"]!["pattern"] = "^[0-9A-Fa-f]{64}$";
                selection["kind"]!["enum"] = new JsonArray("Property", "Action", "Event");
                properties["name"]!["maxLength"] = 128;
            }
            if (properties.ContainsKey("inputs"))
            {
                properties["inputs"] = Array(
                    WotRuntimeInputJsonContext.Default.WotRuntimeValue.GetJsonSchemaAsNode(), 0, 128,
                    "Ordered typed UA values. HTTP/MQTT require at most one body argument.");
                properties["inputs"]!["examples"] = new JsonArray((JsonNode)new JsonArray((JsonNode)new JsonObject
                {
                    ["type"] = "Int32",
                    ["value"] = 7
                }));
            }
            if (properties.ContainsKey("value"))
            {
                Set(properties, "value", WotRuntimeInputJsonContext.Default.WotRuntimeValue.GetJsonSchemaAsNode());
                properties["value"]!["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["type"] = "Int32",
                    ["value"] = 7
                });
            }
            if (properties["durationMilliseconds"] is JsonObject duration)
            {
                duration["minimum"] = 1;
                duration["maximum"] = 60_000;
            }
            if (properties["maxNotifications"] is JsonObject maximum)
            {
                maximum["minimum"] = 1;
                maximum["maximum"] = 256;
            }
            CloseObjects(schema);
            return schema;
        }

        /// <summary>
        /// Retains the actual parameter description while restoring the source-generated structural contract.
        /// </summary>
        private static void Set(JsonObject properties, string name, JsonNode contract)
        {
            JsonNode? description = properties[name] is JsonObject previous
                ? previous["description"]?.DeepClone() : null;
            if (description is not null)
            {
                contract["description"] = description;
            }
            properties[name] = contract;
        }

        /// <summary>
        /// Creates an actual bounded array contract for a registered element type.
        /// </summary>
        private static JsonObject Array(JsonNode items, int minimum, int maximum, string description)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = items,
                ["minItems"] = minimum,
                ["maxItems"] = maximum,
                ["description"] = description
            };
        }

        /// <summary>
        /// Closes structured DTO objects while leaving the explicitly typed UA JSON value unconstrained in shape.
        /// </summary>
        private static void CloseObjects(JsonNode? node)
        {
            if (node is JsonObject value)
            {
                if (value["properties"] is JsonObject properties)
                {
                    value["additionalProperties"] = false;
                    foreach ((string _, JsonNode? property) in properties)
                    {
                        CloseObjects(property);
                    }
                }
                if (value["items"] is JsonNode items)
                {
                    CloseObjects(items);
                }
            }
        }

        /// <summary>
        /// Retains SDK invocation and generated bindings while refusing undeclared root arguments.
        /// </summary>
        private sealed class ContractTool : DelegatingMcpServerTool
        {
            /// <summary>
            /// Publishes the concrete schema at registration time for both listing and local inspection.
            /// </summary>
            internal ContractTool(McpServerTool inner)
                : base(inner)
            {
                JsonObject schema = Shape(inner);
                m_names = new HashSet<string>(
                    schema["properties"]!.AsObject().Select(value => value.Key), StringComparer.Ordinal);
                using var document = JsonDocument.Parse(schema.ToJsonString());
                inner.ProtocolTool.InputSchema = document.RootElement.Clone();
            }

            /// <inheritdoc/>
            public override async ValueTask<CallToolResult> InvokeAsync(
                RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(request);
                if (request.Params?.Arguments is { } arguments && arguments.Keys.Any(name => !m_names.Contains(name)))
                {
                    return McpCompanionTools.Result(new JsonObject
                    {
                        ["error"] = true,
                        ["message"] = "The live WoT request contains an undeclared argument."
                    });
                }
                try
                {
                    return await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is ArgumentException or JsonException or NotSupportedException)
                {
                    return McpCompanionTools.Result(new JsonObject
                    {
                        ["error"] = true,
                        ["message"] = "The live WoT input does not match its bounded typed schema."
                    });
                }
            }

            private readonly HashSet<string> m_names;
        }
    }
}
