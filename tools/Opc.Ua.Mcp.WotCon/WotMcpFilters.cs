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
    /// Advertises concrete bounded arrays and mutually exclusive document content sources.
    /// </summary>
    internal static class WotMcpFilters
    {
        /// <summary>
        /// Promotes collections and sources while grouping scalar options behind the strongly typed SDK binder.
        /// </summary>
        internal static McpServerTool WrapInput(McpServerTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            return IsInputTool(tool.ProtocolTool.Name) ? new GroupedInputTool(tool) : tool;
        }

        /// <summary>
        /// Replaces custom ArrayOf converter schemas with the same source-generated element contracts they consume.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddInputSchemas(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                foreach (Tool tool in result.Tools)
                {
                    if (!IsInputTool(tool.Name))
                    {
                        continue;
                    }
                    JsonObject schema = InputSchema(tool);
                    using JsonDocument updated = JsonDocument.Parse(schema.ToJsonString());
                    tool.InputSchema = updated.RootElement.Clone();
                }
                return result;
            };
        }

        /// <summary>
        /// Recognizes only the six tools whose C# parameters contain a typed input record.
        /// </summary>
        private static bool IsInputTool(string name)
        {
            return name is "wot_refresh" or "wot_inspect_document" or "wot_validate_document" or
                "wot_plan_bindings" or "wot_to_nodeset" or "wot_from_nodeset";
        }

        /// <summary>
        /// Fills in the concrete arrays omitted by custom converter metadata.
        /// </summary>
        private static JsonObject InputSchema(Tool tool)
        {
            JsonObject schema = JsonNode.Parse(tool.InputSchema.GetRawText()) as JsonObject ??
                throw new InvalidOperationException("The WoT tool schema is missing.");
            JsonObject root = schema["properties"] as JsonObject ??
                throw new InvalidOperationException("The WoT input schema has no properties.");
            JsonObject properties = root["input"]?["properties"] as JsonObject ?? root;
            if (tool.Name == "wot_refresh")
            {
                if (!properties.ContainsKey("selection"))
                {
                    throw new InvalidOperationException("The WoT refresh schema has no selection field.");
                }
                properties["selection"] = ArraySchema(
                    WotInputJsonContext.Default.WotResourceSelection.GetJsonSchemaAsNode(), 500);
                properties["selection"]!["description"] = InputDescription("selection");
                properties["selection"]!["examples"] = JsonNode.Parse(
                    """[[{"kind":"ThingDescription","groupId":"thingdescriptions","resourceId":"pump"}]]""");
            }
            else
            {
                if (!properties.ContainsKey("source"))
                {
                    throw new InvalidOperationException("The WoT document schema has no source field.");
                }
                properties["source"] = SourceSchema();
                JsonObject dependency = SourceSchema();
                dependency["required"] = JsonNode.Parse("""["reference"]""");
                properties["dependencies"] = ArraySchema(dependency, 255);
                properties["requiredConformance"] = ArraySchema(new JsonObject { ["type"] = "string" }, 64);
                properties["source"]!["description"] = InputDescription("source");
                properties["dependencies"]!["description"] = InputDescription("dependencies");
                properties["requiredConformance"]!["description"] = InputDescription("requiredConformance");
                properties["dependencies"]!["examples"] = JsonNode.Parse(
                    """[[{"reference":"schema.json","filePath":"schema.json"}]]""");
                properties["requiredConformance"]!["examples"] = JsonNode.Parse("[[]]");
            }
            return schema;
        }

        /// <summary>
        /// Builds an explicit content-source choice without permitting two competing sources.
        /// </summary>
        private static JsonObject SourceSchema()
        {
            JsonObject schema = WotInputJsonContext.Default.WotDocumentSource.GetJsonSchemaAsNode().AsObject();
            JsonObject properties = schema["properties"]!.AsObject();
            properties["text"] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = 65_536,
                ["description"] = "Inline UTF-8 text; at most 64 KiB after encoding."
            };
            properties["filePath"] = new JsonObject
            {
                ["type"] = "string",
                ["minLength"] = 1,
                ["description"] = "File beneath the configured TransferRoot; never an arbitrary URL."
            };
            schema["oneOf"] = JsonNode.Parse("""
                [
                  {"required":["text"],"not":{"required":["filePath"]}},
                  {"required":["filePath"],"not":{"required":["text"]}}
                ]
                """);
            return schema;
        }

        /// <summary>
        /// Creates a bounded JSON array schema.
        /// </summary>
        private static JsonObject ArraySchema(JsonNode item, int maximum)
        {
            return new JsonObject { ["type"] = "array", ["items"] = item, ["minItems"] = 0, ["maxItems"] = maximum };
        }

        /// <summary>
        /// Describes the promoted record fields without reflection over DTO attributes.
        /// </summary>
        private static string InputDescription(string name)
        {
            return name switch
            {
                "source" => "Primary document: exactly one inline text value or a file beneath TransferRoot.",
                "dependencies" => "Explicit local dependencies, each with a reference and one text or file source.",
                "requiredConformance" => "Required Binding claims, up to 64; requires options.strictAuthoring=true.",
                "selection" => "Explicit resource/version selectors, up to 500; " +
                    "empty only with options.allResources=true.",
                _ => throw new InvalidOperationException("The WoT input field has no wire description.")
            };
        }

        /// <summary>
        /// Keeps both nesting and top-level argument count small without changing the typed method's execution.
        /// </summary>
        private sealed class GroupedInputTool : DelegatingMcpServerTool
        {
            /// <summary>
            /// Creates a shallow wire schema, retaining names, annotations, output schemas, and execution metadata.
            /// </summary>
            public GroupedInputTool(McpServerTool inner)
                : base(inner)
            {
                JsonObject schema = InputSchema(inner.ProtocolTool);
                JsonObject root = schema["properties"]!.AsObject();
                JsonObject input = root["input"]?.AsObject() ??
                    throw new InvalidOperationException("The typed WoT input parameter is missing.");
                JsonObject fields = input["properties"]!.AsObject();
                m_inputNames = new HashSet<string>(fields.Select(field => field.Key), StringComparer.Ordinal);
                m_optionNames = new HashSet<string>(StringComparer.Ordinal);
                root.Remove("input");
                var options = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> field in fields)
                {
                    JsonObject property = field.Value?.DeepClone().AsObject() ??
                        throw new InvalidOperationException("The WoT input field has no schema.");
                    if (field.Key is "source" or "selection" or "dependencies" or "requiredConformance")
                    {
                        property["description"] = InputDescription(field.Key);
                        root.Add(field.Key, property);
                    }
                    else
                    {
                        options.Add(field.Key, property);
                        m_optionNames.Add(field.Key);
                    }
                }
                root.Add("options", new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = options,
                    ["required"] = new JsonArray(),
                    ["additionalProperties"] = false,
                    ["description"] = inner.ProtocolTool.Name == "wot_refresh"
                        ? "Refresh scope and execution settings; empty selection requires allResources=true."
                        : "Resolution and validation settings; remote fetching requires host and call permission.",
                    ["examples"] = JsonNode.Parse(inner.ProtocolTool.Name == "wot_refresh"
                        ? """[{"dryRun":true,"requestId":"preview"}]"""
                        : """[{"strictAuthoring":false}]""")
                });
                var required = new JsonArray();
                if (schema["required"] is JsonArray outerRequired)
                {
                    foreach (JsonNode? value in outerRequired)
                    {
                        if (value?.GetValue<string>() != "input")
                        {
                            required.Add(value?.DeepClone());
                        }
                    }
                }
                if (input["required"] is JsonArray innerRequired)
                {
                    foreach (JsonNode? value in innerRequired)
                    {
                        required.Add(value?.DeepClone());
                    }
                }
                schema["required"] = required;
                m_argumentNames = new HashSet<string>(root.Select(field => field.Key), StringComparer.Ordinal);
                using JsonDocument updated = JsonDocument.Parse(schema.ToJsonString());
                inner.ProtocolTool.InputSchema = updated.RootElement.Clone();
            }

            /// <inheritdoc/>
            public override async ValueTask<CallToolResult> InvokeAsync(
                RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(request);
                cancellationToken.ThrowIfCancellationRequested();
                CallToolRequestParams parameters = request.Params ??
                    throw new ArgumentException("The WoT tool request has no parameters.", nameof(request));
                IDictionary<string, JsonElement>? original = parameters.Arguments;
                var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                var input = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (original is not null)
                {
                    foreach (KeyValuePair<string, JsonElement> argument in original)
                    {
                        if (!m_argumentNames.Contains(argument.Key))
                        {
                            return McpCompanionTools.Result(new JsonObject
                            {
                                ["error"] = true,
                                ["errorType"] = nameof(ArgumentException),
                                ["message"] = $"Unknown argument '{argument.Key}'; " +
                                    "use the advertised fields and options."
                            });
                        }
                        if (argument.Key == "options")
                        {
                            if (argument.Value.ValueKind != JsonValueKind.Object)
                            {
                                return InputError("The options argument must be an object.");
                            }
                            foreach (JsonProperty option in argument.Value.EnumerateObject())
                            {
                                if (!m_optionNames.Contains(option.Name))
                                {
                                    return InputError($"Unknown WoT option '{option.Name}'.");
                                }
                                if (!input.TryAdd(option.Name, option.Value))
                                {
                                    return InputError($"Duplicate WoT option '{option.Name}'.");
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
                    arguments.Add("input", PackInput(input, cancellationToken));
                }
                catch (IOException exception)
                {
                    return McpCompanionTools.Result(new JsonObject
                    {
                        ["error"] = true,
                        ["errorType"] = nameof(IOException),
                        ["message"] = exception.Message
                    });
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
            /// Copies raw JSON without building a second dependency-object tree.
            /// Disposes the bounded buffer before dispatch so late cancellation cannot mask a completed mutation.
            /// </summary>
            private static JsonElement PackInput(Dictionary<string, JsonElement> fields, CancellationToken ct)
            {
                using var buffer = new WotDocumentBuffer(128L * 1024 * 1024, ct);
                JsonSerializer.Serialize(buffer, fields, WotInputJsonContext.Default.DictionaryStringJsonElement);
                using JsonDocument packed = JsonDocument.Parse(buffer.ToArray());
                return packed.RootElement.Clone();
            }

            /// <summary>
            /// Reports invalid option containers before the underlying tool can dispatch a command.
            /// </summary>
            private static CallToolResult InputError(string message)
            {
                return McpCompanionTools.Result(new JsonObject
                {
                    ["error"] = true,
                    ["errorType"] = nameof(ArgumentException),
                    ["message"] = message
                });
            }

            private readonly HashSet<string> m_inputNames;
            private readonly HashSet<string> m_optionNames;
            private readonly HashSet<string> m_argumentNames;
        }
    }
}
