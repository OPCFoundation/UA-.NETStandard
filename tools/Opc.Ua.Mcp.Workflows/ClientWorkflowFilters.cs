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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Publishes closed, bounded workflow contracts and hides host-disabled identity administration.
    /// </summary>
    internal static class ClientWorkflowFilters
    {
        /// <summary>
        /// Wraps registration-time schemas while keeping the original SDK argument binder.
        /// </summary>
        internal static McpServerTool Wrap(McpServerTool tool)
        {
            return new InputTool(tool);
        }

        /// <summary>
        /// Hides privileged administration unless the host opted in; invocation rechecks exact caller/session grants.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Catalogue(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                IMcpOperationPolicy policy = request.Services!.GetRequiredService<IMcpOperationPolicy>();
                if (!policy.IsEnabled(McpCapability.IdentityAdministration))
                {
                    result.Tools = [.. result.Tools.Where(tool => !RequiresIdentityAdministration(tool.Name))];
                }
                return result;
            };
        }

        /// <summary>
        /// Defines the exact gated subset rather than broadening the grant to a profile or runtime category.
        /// </summary>
        internal static bool RequiresIdentityAdministration(string name)
        {
            return name is "roles_create" or "roles_remove" or "roles_add_identity" or "roles_remove_identity" or
                "roles_add_application" or "roles_remove_application" or "roles_add_endpoint" or
                "roles_remove_endpoint" or "roles_set_flags" or "roles_write_permissions" or
                "users_policy" or "users_list" or "users_add" or "users_modify" or
                "users_remove" or "users_change_password";
        }

        /// <summary>
        /// Materializes exact structured-array and scalar input schemas without reflection.
        /// </summary>
        private static void Shape(Tool tool)
        {
            JsonObject schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
            schema["additionalProperties"] = false;
            schema["required"] ??= new JsonArray();
            JsonObject properties = schema["properties"]!.AsObject();
            if (properties.ContainsKey("values"))
            {
                Replace(properties, "values", Array(Contract<WorkflowDataValueInput>(), 1, 500));
            }
            if (properties.ContainsKey("arrayElements"))
            {
                Replace(properties, "arrayElements", Array(Contract<WorkflowArrayElementInput>(), 0, 500));
            }
            if (properties.ContainsKey("select"))
            {
                Replace(properties, "select", Array(Contract<WorkflowEventFieldInput>(), 1, 32));
            }
            if (properties.ContainsKey("paths"))
            {
                Replace(properties, "paths", Array(Contract<WorkflowEventPathInput>(), 0, 256));
            }
            if (properties.ContainsKey("events"))
            {
                Replace(properties, "events", Array(Contract<WorkflowHistoryEventInput>(), 1, 500));
            }
            if (properties.ContainsKey("annotations"))
            {
                Replace(properties, "annotations", Array(Contract<WorkflowAnnotationInput>(), 1, 500));
            }
            if (properties.ContainsKey("permissions"))
            {
                Replace(properties, "permissions", Array(Contract<WorkflowRolePermissionInput>(), 0, 500));
            }
            if (properties.ContainsKey("timestamps"))
            {
                Replace(properties, "timestamps", Array(
                    new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 64 }, 1, 500));
            }
            if (properties.ContainsKey("eventIds"))
            {
                Replace(properties, "eventIds", Array(
                    new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 87384 }, 1, 500));
            }
            if (properties.ContainsKey("optionType"))
            {
                Replace(properties, "optionType", ValueType());
                Replace(properties, "optionValue", Scalar(allowArray: true));
            }
            Bound(properties, "maxResults", 1, 500);
            Bound(properties, "offset", 0, 1_000_000);
            Bound(properties, "durationMilliseconds", 1, 30_000);
            Bound(properties, "maxDepth", 0, 6);
            using var document = JsonDocument.Parse(schema.ToJsonString());
            tool.InputSchema = document.RootElement.Clone();
        }

        /// <summary>
        /// Gets a concrete source-generated contract, overriding only custom ArrayOf converter shapes.
        /// </summary>
        /// <typeparam name="T">A source-generated input type.</typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        private static JsonNode Contract<T>()
        {
            JsonNode schema = (WorkflowInputJsonContext.Default.GetTypeInfo(typeof(T)) ??
                throw new InvalidOperationException("Missing workflow input metadata.")).GetJsonSchemaAsNode();
            Normalize(schema);
            return schema;
        }

        /// <summary>
        /// Shapes only the known input types and their bounded arrays.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static void Normalize(JsonNode node)
        {
            if (node is not JsonObject schema || schema["properties"] is not JsonObject properties)
            {
                return;
            }
            schema["additionalProperties"] = false;
            foreach (string name in properties.Select(property => property.Key).ToArray())
            {
                if (name == "value")
                {
                    properties[name] = Scalar(allowArray: false);
                }
                else if (name == "type")
                {
                    properties[name] = ValueType();
                }
                else if (name == "arrayLength")
                {
                    properties[name] = new JsonObject
                    {
                        ["type"] = new JsonArray("integer", "null"),
                        ["minimum"] = 0,
                        ["maximum"] = 500,
                        ["description"] = "0=empty; omit for scalar."
                    };
                }
                else if (name is "eventIndex" or "valueIndex" or "elementIndex" or "selectIndex" or "fieldIndex" or
                    "segmentIndex")
                {
                    properties[name] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0,
                        ["maximum"] = name == "segmentIndex" ? 7 : name is "fieldIndex" or "selectIndex" ? 31 : 499,
                        ["description"] = "Zero-based."
                    };
                }
                else if (name == "attributeId")
                {
                    properties[name] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["enum"] = new JsonArray(1, 13),
                        ["description"] = "1=NodeId; 13=Value."
                    };
                }
                else if (properties[name] is JsonObject field)
                {
                    field["description"] = name switch
                    {
                        "sourceTimestamp" or "serverTimestamp" or "annotationTime" =>
                            "ISO8601 with Z/offset.",
                        "namespaceUri" => "Exact URI.",
                        "browseName" or "name" => "Browse-name segment.",
                        "typeDefinitionId" => "Event type NodeId.",
                        "roleId" => "Role NodeId.",
                        "statusCode" => "UA code; 0=Good.",
                        "permissions" => "PermissionType mask.",
                        "message" => "Annotation.",
                        "userName" => "Attribution, not authority.",
                        _ => throw new InvalidOperationException("A workflow DTO field has no declared description.")
                    };
                }
            }
        }

        /// <summary>
        /// Declares the supported built-in value discriminator without accepting arbitrary type names.
        /// </summary>
        private static JsonObject ValueType()
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["description"] = "UA value type.",
                ["enum"] = new JsonArray(
                    "Null", "Boolean", "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64",
                    "Float", "Double", "String", "DateTime", "Guid", "ByteString", "NodeId",
                    "StatusCode", "LocalizedText")
            };
        }

        /// <summary>
        /// Advertises actual JSON scalars, optionally a flat scalar array for standalone generation options.
        /// </summary>
        private static JsonObject Scalar(bool allowArray)
        {
            var schema = new JsonObject
            {
                ["type"] = allowArray
                    ? new JsonArray("null", "boolean", "number", "string", "array")
                    : new JsonArray("null", "boolean", "number", "string"),
                ["description"] = "ByteString=base64; DateTime=ISO8601+offset."
            };
            if (allowArray)
            {
                schema["items"] = Scalar(allowArray: false);
                schema["maxItems"] = 500;
            }
            return schema;
        }

        /// <summary>
        /// Replaces only a real bound parameter contract and preserves its interface description.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static void Replace(JsonObject properties, string name, JsonObject replacement)
        {
            if (properties[name] is not JsonObject original)
            {
                throw new InvalidOperationException("A shaped workflow parameter is not present in the bound method.");
            }
            if (original["description"] is { } description)
            {
                replacement["description"] = description.DeepClone();
            }
            properties[name] = replacement;
        }

        /// <summary>
        /// Constructs one concrete bounded JSON array.
        /// </summary>
        private static JsonObject Array(JsonNode item, int minimum, int maximum)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = item,
                ["minItems"] = minimum,
                ["maxItems"] = maximum
            };
        }

        /// <summary>
        /// Sets a published scalar ceiling that invocation validates again against host options.
        /// </summary>
        private static void Bound(JsonObject properties, string name, int minimum, int maximum)
        {
            if (properties[name] is JsonObject schema)
            {
                schema["minimum"] = minimum;
                schema["maximum"] = maximum;
            }
        }

        /// <summary>
        /// Rejects unknown root arguments instead of silently ignoring a caller's intended safety control.
        /// </summary>
        private sealed class InputTool : DelegatingMcpServerTool
        {
            /// <summary>
            /// Snapshots the shaped contract's exact root arguments.
            /// </summary>
            internal InputTool(McpServerTool inner)
                : base(inner)
            {
                Shape(inner.ProtocolTool);
                m_names = new HashSet<string>(inner.ProtocolTool.InputSchema.GetProperty("properties")
                    .EnumerateObject().Select(property => property.Name), StringComparer.Ordinal);
            }

            /// <inheritdoc/>
            public override ValueTask<CallToolResult> InvokeAsync(
                RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(request);
                if (request.Params?.Arguments?.Keys.Any(name => !m_names.Contains(name)) == true)
                {
                    return ValueTask.FromResult(McpCompanionTools.Result(new JsonObject
                    {
                        ["error"] = true,
                        ["statusCode"] = "BadInvalidArgument",
                        ["statusCodeValue"] = StatusCodes.BadInvalidArgument.Code,
                        ["message"] = "An unknown client-workflow argument was supplied."
                    }));
                }
                return base.InvokeAsync(request, cancellationToken);
            }

            private readonly HashSet<string> m_names;
        }
    }
}
