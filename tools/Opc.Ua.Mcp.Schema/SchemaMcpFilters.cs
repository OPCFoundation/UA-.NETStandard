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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Keeps schema-tool wire contracts shallow, typed and closed without reflection-based input packing.
    /// </summary>
    internal static class SchemaMcpFilters
    {
        /// <summary>
        /// Wraps the six approved tools, leaving connection and unrelated catalogues untouched.
        /// </summary>
        internal static McpServerTool Wrap(McpServerTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            return tool.ProtocolTool.Name is "schema_list_types" or "schema_inspect_type" or "schema_load_types" or
                "schema_generate" or "schema_generate_pubsub" or "schema_generate_openapi"
                ? new SchemaInputTool(tool)
                : tool;
        }

        /// <summary>
        /// Builds the concrete source-generated DTO schema, including explicitly bounded custom arrays.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static JsonObject Contract(string name)
        {
            JsonNode node = name switch
            {
                "schema_list_types" => SchemaInputJsonContext.Default.SchemaListInput.GetJsonSchemaAsNode(),
                "schema_inspect_type" => SchemaInputJsonContext.Default.SchemaInspectInput.GetJsonSchemaAsNode(),
                "schema_load_types" => SchemaInputJsonContext.Default.SchemaLoadInput.GetJsonSchemaAsNode(),
                "schema_generate" => SchemaInputJsonContext.Default.SchemaGenerateInput.GetJsonSchemaAsNode(),
                "schema_generate_pubsub" => SchemaInputJsonContext.Default.SchemaPubSubInput.GetJsonSchemaAsNode(),
                "schema_generate_openapi" => SchemaInputJsonContext.Default.SchemaOpenApiInput.GetJsonSchemaAsNode(),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            JsonObject schema = node.AsObject();
            JsonObject properties = schema["properties"]!.AsObject();
            if (name == "schema_load_types")
            {
                properties["typeIds"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                    ["minItems"] = 1,
                    ["maxItems"] = 512,
                    ["uniqueItems"] = true
                };
                schema["required"] = new JsonArray("typeIds");
            }
            if (name == "schema_generate_pubsub")
            {
                properties["fields"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = SchemaInputJsonContext.Default.SchemaPubSubField.GetJsonSchemaAsNode(),
                    ["minItems"] = 0,
                    ["maxItems"] = 1024
                };
            }
            if (name == "schema_list_types")
            {
                properties["offset"]!["minimum"] = 0;
                properties["offset"]!["maximum"] = 512;
                properties["maxResults"]!["minimum"] = 1;
                properties["maxResults"]!["maximum"] = 500;
            }
            foreach (KeyValuePair<string, JsonNode?> property in properties)
            {
                string? description = property.Key switch
                {
                    "typeId" => TypeIdDescription,
                    "typeIds" => TypeIdsDescription,
                    "fields" => FieldsDescription,
                    "metadataNodeId" => MetadataNodeIdDescription,
                    _ => null
                };
                if (description is not null)
                {
                    property.Value!["description"] = description;
                }
            }
            return schema;
        }

        /// <summary>
        /// Supplies concrete option examples without reflection-based scalar-array serialization.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static JsonArray OptionsExamples(string toolName)
        {
            var example = new JsonObject();
            switch (toolName)
            {
                case "schema_list_types":
                    example["source"] = "Registered";
                    example["namespaceUri"] = Namespaces.OpcUa;
                    example["maxResults"] = 25;
                    break;
                case "schema_inspect_type":
                    example["source"] = "Registered";
                    break;
                case "schema_generate":
                    example["source"] = "Registered";
                    example["format"] = "JsonCompact";
                    example["scope"] = "Type";
                    break;
                case "schema_generate_pubsub":
                    example["source"] = "Registered";
                    example["kind"] = "Payload";
                    example["fieldContentMask"] = (uint)DataSetFieldContentMask.RawData;
                    example["messageContentMask"] = 0;
                    example["networkContentMask"] = 0;
                    break;
                case "schema_generate_openapi":
                    example["serviceSet"] = "Sessionless";
                    example["includeSchemas"] = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(toolName));
            }
            return new JsonArray(example);
        }

        /// <summary>The explicit local or namespace-URI form of a DataType identifier.</summary>
        internal const string TypeIdDescription =
            "DataType NodeId, e.g. i=296 or nsu=urn:plant;s=Record. Its namespace must exist in the selected context.";

        /// <summary>The typed, finite client-load root selection.</summary>
        internal const string TypeIdsDescription =
            "Unique DataType NodeId strings, 1..512, e.g. [\"ns=2;s=Record\"]. " +
            "Loads only the bounded client-side closure.";

        /// <summary>The concrete inline PubSub payload field array.</summary>
        internal const string FieldsDescription =
            "Inline fields, up to 1024; e.g. [{\"name\":\"Temperature\",\"dataTypeId\":\"i=11\",\"valueRank\":-1}]. " +
            "Do not combine with metadataNodeId.";

        /// <summary>The alternative remote metadata Variable selection.</summary>
        internal const string MetadataNodeIdDescription =
            "DataSetMetaData Variable NodeId, e.g. ns=2;s=Publisher.Metadata. " +
            "Requires options.source=Session and no inline fields.";

        /// <summary>The optional currently connected session selector.</summary>
        internal const string SessionNameDescription =
            "Connected session name, e.g. plant. Omit to use the only connected session. " +
            "For tools with a source option, specify this only with options.source=Session.";

        /// <summary>The no-overwrite root-confined artifact destination.</summary>
        internal const string OutputPathDescription =
            "New file path beneath TransferRoot, e.g. schemas\\argument.json. " +
            "Its parent directory must exist; existing files are never overwritten.";

        /// <summary>
        /// Promotes actual arrays and identifiers, grouping only scalar settings as options.
        /// </summary>
        private sealed class SchemaInputTool : DelegatingMcpServerTool
        {
            /// <summary>
            /// Preserves the SDK's metadata and invocation while publishing the concrete closed contract.
            /// </summary>
            internal SchemaInputTool(McpServerTool inner)
                : base(inner)
            {
                JsonObject schema = JsonNode.Parse(inner.ProtocolTool.InputSchema.GetRawText())!.AsObject();
                JsonObject root = schema["properties"]!.AsObject();
                root.Remove("input");
                JsonObject contract = Contract(inner.ProtocolTool.Name);
                JsonObject fields = contract["properties"]!.AsObject();
                m_inputNames = new HashSet<string>(fields.Select(field => field.Key), StringComparer.Ordinal);
                m_optionNames = new HashSet<string>(StringComparer.Ordinal);
                var options = new JsonObject();
                var required = new JsonArray();
                var optionRequired = new JsonArray();
                if (schema["required"] is JsonArray originalRequired)
                {
                    foreach (JsonNode? value in originalRequired)
                    {
                        if (value!.GetValue<string>() != "input")
                        {
                            required.Add(value.DeepClone());
                        }
                    }
                }
                foreach (KeyValuePair<string, JsonNode?> field in fields)
                {
                    if (field.Key is "typeId" or "typeIds" or "fields" or "metadataNodeId")
                    {
                        root.Add(field.Key, field.Value!.DeepClone());
                    }
                    else
                    {
                        options.Add(field.Key, field.Value!.DeepClone());
                        m_optionNames.Add(field.Key);
                    }
                }
                if (contract["required"] is JsonArray inputRequired)
                {
                    foreach (JsonNode? value in inputRequired)
                    {
                        if (m_optionNames.Contains(value!.GetValue<string>()))
                        {
                            optionRequired.Add(value.DeepClone());
                        }
                        else
                        {
                            required.Add(value.DeepClone());
                        }
                    }
                }
                if (options.Count > 0)
                {
                    root["options"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = options,
                        ["required"] = optionRequired,
                        ["additionalProperties"] = false,
                        ["description"] = "Schema settings as an object; select sources, formats or explicit masks. " +
                            "Examples below show valid combinations. Host resource ceilings cannot be raised.",
                        ["examples"] = OptionsExamples(inner.ProtocolTool.Name)
                    };
                    if (optionRequired.Count > 0)
                    {
                        JsonNode optionName = JsonValue.Create("options");
                        required.Add(optionName);
                    }
                }
                schema["required"] = required;
                schema["additionalProperties"] = false;
                m_argumentNames = new HashSet<string>(root.Select(field => field.Key), StringComparer.Ordinal);
                using var updated = JsonDocument.Parse(schema.ToJsonString());
                inner.ProtocolTool.InputSchema = updated.RootElement.Clone();
            }

            /// <inheritdoc/>
            public override async ValueTask<CallToolResult> InvokeAsync(
                RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(request);
                cancellationToken.ThrowIfCancellationRequested();
                CallToolRequestParams parameters = request.Params ??
                    throw new ArgumentException("The schema tool request has no parameters.", nameof(request));
                IDictionary<string, JsonElement>? original = parameters.Arguments;
                var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                var input = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (original is not null)
                {
                    foreach (KeyValuePair<string, JsonElement> argument in original)
                    {
                        if (!m_argumentNames.Contains(argument.Key))
                        {
                            return Error($"Unknown schema argument '{argument.Key}'.");
                        }
                        if (argument.Key == "options")
                        {
                            if (argument.Value.ValueKind != JsonValueKind.Object)
                            {
                                return Error("options must be an object.");
                            }
                            foreach (JsonProperty option in argument.Value.EnumerateObject())
                            {
                                if (!m_optionNames.Contains(option.Name) || !input.TryAdd(option.Name, option.Value))
                                {
                                    return Error($"Unknown or duplicate schema option '{option.Name}'.");
                                }
                            }
                        }
                        else if (m_inputNames.Contains(argument.Key))
                        {
                            input.Add(argument.Key, argument.Value);
                        }
                        else
                        {
                            arguments.Add(argument.Key, argument.Value);
                        }
                    }
                }
                try
                {
                    using var buffer = new McpBoundedMemoryStream(16 * 1024 * 1024, cancellationToken);
                    JsonSerializer.Serialize(buffer, input, SchemaInputJsonContext.Default.DictionaryStringJsonElement);
                    using var packed = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
                    arguments["input"] = packed.RootElement.Clone();
                }
                catch (IOException exception)
                {
                    return Error(exception.Message);
                }
                parameters.Arguments = arguments;
                try
                {
                    return await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    parameters.Arguments = original;
                }
            }

            /// <summary>
            /// Reports malformed wire inputs before typed binding or any remote/file operation.
            /// </summary>
            private static CallToolResult Error(string message)
            {
                return McpCompanionTools.Result(new JsonObject
                {
                    ["error"] = true,
                    ["errorType"] = nameof(ArgumentException),
                    ["message"] = message
                });
            }

            /// <summary>Concrete DTO members reconstructed for the SDK binder.</summary>
            private readonly HashSet<string> m_inputNames;

            /// <summary>The closed set of scalar options.</summary>
            private readonly HashSet<string> m_optionNames;

            /// <summary>The complete advertised top-level argument set.</summary>
            private readonly HashSet<string> m_argumentNames;
        }
    }
}
