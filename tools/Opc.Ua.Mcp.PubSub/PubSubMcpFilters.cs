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
    /// Publishes exact typed SDK contracts and independently hides host-disabled expansion tools.
    /// Legacy names and schemas are not rewritten.
    /// </summary>
    internal static class PubSubMcpFilters
    {
        /// <summary>
        /// Wraps only new domain tools without changing the SDK argument binder.
        /// </summary>
        internal static McpServerTool Wrap(McpServerTool tool)
        {
            return new InputTool(tool);
        }

        /// <summary>
        /// Hides gated operations even in Full. Cleanup and pure offline discovery remain available.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Catalogue(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                IMcpOperationPolicy? policy = request.Services?.GetService<IMcpOperationPolicy>();
                result.Tools = [.. result.Tools.Where(tool =>
                    Capability(tool.Name) is not { } capability || policy?.IsEnabled(capability) == true)];
                return result;
            };
        }

        /// <summary>
        /// Maps exact expansion families rather than treating a catalogue profile as authority.
        /// </summary>
        internal static McpCapability? Capability(string tool)
        {
            if (tool is "pubsub_capabilities" or "pubsub_application_validate" or "pubsub_bridge_validate" or
                "pubsub_application_list" or "pubsub_application_remove" or
                "pubsub_bridge_list" or "pubsub_bridge_remove")
            {
                return null;
            }
            if (tool.StartsWith("pubsub_application_", StringComparison.Ordinal))
            {
                return McpCapability.PubSubRuntime;
            }
            if (tool.StartsWith("pubsub_configuration_", StringComparison.Ordinal))
            {
                return McpCapability.PubSubConfiguration;
            }
            if (tool.StartsWith("pubsub_security_", StringComparison.Ordinal))
            {
                return McpCapability.PubSubSecurity;
            }
            return tool.StartsWith("pubsub_bridge_", StringComparison.Ordinal) ||
                tool.StartsWith("pubsub_adapter_", StringComparison.Ordinal) ? McpCapability.PubSubBridge : null;
        }

        /// <summary>
        /// Replaces only converter-obscured DTO schemas with the same generated types actually consumed by the binder.
        /// </summary>
        private static void Shape(Tool tool)
        {
            JsonObject schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
            schema["additionalProperties"] = false;
            JsonObject properties = schema["properties"]!.AsObject();
            if (tool.Name is "pubsub_bridge_create" or "pubsub_bridge_validate")
            {
                schema["$defs"] = new JsonObject
                {
                    ["application"] = Contract<PubSubApplicationSettingsInput>(),
                    ["connection"] = Contract<PubSubConnectionInput>(),
                    ["field"] = Contract<PubSubFieldInput>()
                };
                Set(properties, "input", Contract<PubSubBridgeSettingsInput>());
                foreach (string name in new[] { "source", "target" })
                {
                    Set(properties, name, Reference("application"));
                    Set(properties, name + "Connection", Reference("connection"));
                    Set(properties, name + "Fields", Array(Reference("field"), 1, 500));
                }
                Set(properties, "renameFields", Array(Contract<PubSubFieldRenameInput>(), 0, 500));
                foreach (JsonNode? definition in schema["$defs"]!.AsObject().Select(item => item.Value))
                {
                    definition!.AsObject().Remove("examples");
                }
            }
            else if (tool.Name is "pubsub_application_validate" or "pubsub_application_create" or
                "pubsub_configuration_add_connection" or "pubsub_configuration_add_writer_group" or
                "pubsub_configuration_add_reader_group" or "pubsub_configuration_add_writer" or
                "pubsub_configuration_add_reader")
            {
                Set(properties, "input", Contract<PubSubApplicationSettingsInput>());
                Set(properties, "connection", Contract<PubSubConnectionInput>());
                Set(properties, "fields", Array(Contract<PubSubFieldInput>(), 1, 500));
            }
            else
            {
                switch (tool.Name)
                {
                    case "pubsub_application_publish":
                        Set(properties, "fields", Array(Contract<PubSubFieldValueInput>(), 1, 500));
                        break;
                    case "pubsub_configuration_add_dataset":
                    case "pubsub_configuration_add_fields":
                    case "pubsub_adapter_bind_source":
                    case "pubsub_adapter_bind_sink":
                        Set(properties, "variables", Array(Contract<PubSubVariableInput>(), 1, 500));
                        break;
                    case "pubsub_configuration_remove_fields":
                        Set(properties, "fields", Array(Contract<PubSubFieldIndexInput>(), 1, 500));
                        break;
                    case "pubsub_security_connect_groups":
                    case "pubsub_security_disconnect_groups":
                        Set(properties, "nodes", Array(Contract<PubSubNodeInput>(), 1, 16));
                        break;
                    case "pubsub_security_push_keys":
                        Set(properties, "input", Contract<PubSubSecurityInput>());
                        break;
                    case "pubsub_adapter_bind_action":
                        Set(properties, "input", Contract<PubSubActionBindingInput>());
                        break;
                }
            }
            if (properties.ContainsKey("version"))
            {
                Set(properties, "version", Contract<PubSubVersionInput>());
            }
            if (properties["maximum"] is JsonObject maximum)
            {
                maximum["minimum"] = 1;
                maximum["maximum"] = 500;
            }
            using var document = JsonDocument.Parse(schema.ToJsonString());
            tool.InputSchema = document.RootElement.Clone();
        }

        /// <summary>
        /// Retains the actual parameter description while restoring a converter's structural contract.
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
        /// Reuses a concrete definition without changing the shape accepted by the binder.
        /// </summary>
        private static JsonObject Reference(string name)
        {
            return new JsonObject { ["type"] = "object", ["$ref"] = "#/$defs/" + name };
        }

        /// <summary>
        /// Uses source-generated metadata for every nested object and explicitly bounded ArrayOf converter.
        /// </summary>
        /// <typeparam name="T">The concrete input contract.</typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        private static JsonNode Contract<T>()
        {
            JsonNode schema = (McpPubSubExpansionJsonContext.Default.GetTypeInfo(typeof(T)) ??
                throw new InvalidOperationException("Missing PubSub input metadata.")).GetJsonSchemaAsNode();
            if (schema is not JsonObject contract || contract["properties"] is not JsonObject properties)
            {
                return schema;
            }
            contract["additionalProperties"] = false;
            if (typeof(T) == typeof(PubSubApplicationSettingsInput))
            {
                Set(properties, "publisherId", Contract<PubSubPublisherIdInput>());
                properties["publisherId"]!.AsObject().Remove("examples");
                Set(properties, "security", Nullable(Contract<PubSubSecurityInput>()));
                properties["security"]!.AsObject().Remove("examples");
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["name"] = "publisher",
                    ["mode"] = "Publisher",
                    ["publisherId"] = new JsonObject { ["type"] = "UInt16", ["value"] = 1 }
                });
            }
            else if (typeof(T) == typeof(PubSubConnectionInput))
            {
                Set(properties, "auxiliaryTopics", Array(new JsonObject
                {
                    ["type"] = "string",
                    ["examples"] = new JsonArray((JsonNode?)JsonValue.Create("line1/status"))
                }, 0, 16));
                properties["auxiliaryTopics"]!.AsObject().Remove("examples");
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["transport"] = "Udp",
                    ["endpoint"] = "opc.udp://127.0.0.1:4849",
                    ["networkInterface"] = "eth0"
                });
            }
            else if (typeof(T) == typeof(PubSubBridgeSettingsInput))
            {
                foreach (string name in new[] { "selectFields", "excludeFields", "promoteFields" })
                {
                    Set(properties, name, Array(new JsonObject
                    {
                        ["type"] = "string",
                        ["examples"] = new JsonArray((JsonNode?)JsonValue.Create("Count"))
                    }, 0, 500));
                    properties[name]!.AsObject().Remove("examples");
                }
                Set(properties, "publisherId", Nullable(Contract<PubSubPublisherIdInput>()));
                properties["publisherId"]!.AsObject().Remove("examples");
            }
            else if (typeof(T) == typeof(PubSubActionBindingInput))
            {
                Set(properties, "outputFieldNames", Array(new JsonObject
                {
                    ["type"] = "string",
                    ["examples"] = new JsonArray((JsonNode?)JsonValue.Create("Result"))
                }, 0, 500));
                properties["outputFieldNames"]!["description"] ??=
                    "Output field names in method argument order; not executable mappings.";
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["objectId"] = "ns=2;s=Device",
                    ["methodId"] = "ns=2;s=Device.Reset",
                    ["actionTargetId"] = 1
                });
            }
            else if (typeof(T) == typeof(PubSubFieldInput))
            {
                Set(properties, "type", new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray([.. Enum.GetValues<BuiltInType>()
                        .Where(type => type is >= BuiltInType.Boolean and <= BuiltInType.ByteString)
                        .Select(type => (JsonNode?)JsonValue.Create(type.ToString()))])
                });
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["name"] = "Count",
                    ["type"] = "Int32",
                    ["initialValue"] = 7
                });
            }
            else if (typeof(T) == typeof(PubSubPublisherIdInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject { ["type"] = "UInt16", ["value"] = 1 });
            }
            else if (typeof(T) == typeof(PubSubFieldValueInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject { ["name"] = "Count", ["value"] = 7 });
            }
            else if (typeof(T) == typeof(PubSubVariableInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["name"] = "Count",
                    ["nodeId"] = "ns=2;s=Count",
                    ["attributeId"] = 13
                });
            }
            else if (typeof(T) == typeof(PubSubFieldIndexInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject { ["index"] = 0 });
            }
            else if (typeof(T) == typeof(PubSubNodeInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject { ["nodeId"] = "ns=2;s=SecurityGroup1" });
            }
            else if (typeof(T) == typeof(PubSubVersionInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["majorVersion"] = 1,
                    ["minorVersion"] = 0
                });
            }
            else if (typeof(T) == typeof(PubSubFieldRenameInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["source"] = "Count",
                    ["target"] = "Counter"
                });
            }
            else if (typeof(T) == typeof(PubSubSecurityInput))
            {
                contract["examples"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["groupId"] = "Line1",
                    ["policyUri"] = "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes256-CTR",
                    ["keyReference"] = "issued:0123456789abcdef0123456789abcdef",
                    ["resource"] = "urn:example:sks:line1",
                    ["tokenId"] = 1,
                    ["expiresAt"] = "2030-01-01T00:00:00Z"
                });
            }
            return contract;
        }

        /// <summary>
        /// Preserves the complete object shape and null semantics without an extra union-schema layer.
        /// </summary>
        private static JsonObject Nullable(JsonNode schema)
        {
            JsonObject nullable = schema.AsObject();
            nullable["type"] = new JsonArray("object", "null");
            return nullable;
        }

        /// <summary>
        /// Describes a bounded native JSON array accepted by the actual input converter.
        /// </summary>
        private static JsonObject Array(JsonNode item, int minimum, int maximum)
        {
            var schema = new JsonObject
            {
                ["type"] = "array",
                ["items"] = item,
                ["minItems"] = minimum,
                ["maxItems"] = maximum
            };
            if (item["examples"]?[0] is JsonNode example)
            {
                schema["examples"] = new JsonArray((JsonNode)new JsonArray(example.DeepClone()));
                item.AsObject().Remove("examples");
            }
            return schema;
        }

        /// <summary>The exact borrowed-session selector used by remote configuration, security and adapters.</summary>
        internal const string SessionNameDescription =
            "SignAndEncrypt session alias; omit for a sole session. Requires exact host grants.";

        /// <summary>The caller-bound application lease selector, distinct from the display name.</summary>
        internal const string ApplicationHandleDescription =
            "Caller-owned handle from pubsub_application_create/list; not the application name.";

        /// <summary>The caller-bound bridge lease selector, distinct from the route name.</summary>
        internal const string BridgeHandleDescription =
            "Caller-owned handle from pubsub_bridge_create/list; not the route name.";

        /// <summary>The ordered field schema shared by local and remote PubSub plans.</summary>
        internal const string FieldsDescription =
            "Fields, e.g. [{name:'Count',type:'Int32'}]; valueRank -1=scalar,1=array; initialValue matches type/rank.";

        /// <summary>The remote SecurityGroup node selector, not the group's string identifier.</summary>
        internal const string SecurityGroupNodeIdDescription =
            "Exact SecurityGroup object NodeId on the selected SKS server, e.g. ns=2;s=SecurityGroup1; " +
            "not the SecurityGroupId string.";

        /// <summary>The remote SKS push-target node selector, not its endpoint or application URI.</summary>
        internal const string PushTargetNodeIdDescription =
            "Exact existing PubSubKeyPushTarget NodeId on the selected SKS server, e.g. ns=2;s=PushTarget1; " +
            "not its endpoint URL or application URI.";

        /// <summary>
        /// Rejects unknown top-level safety/authority claims rather than silently ignoring them.
        /// </summary>
        private sealed class InputTool : DelegatingMcpServerTool
        {
            /// <summary>
            /// Records the exact shaped root parameter set.
            /// </summary>
            internal InputTool(McpServerTool inner)
                : base(inner)
            {
                Shape(inner.ProtocolTool);
                m_names = inner.ProtocolTool.InputSchema.GetProperty("properties")
                    .EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            }

            /// <inheritdoc/>
            public override ValueTask<CallToolResult> InvokeAsync(
                RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
            {
                if (request.Params?.Arguments?.Keys.Any(name => !m_names.Contains(name)) == true)
                {
                    return ValueTask.FromResult(McpCompanionTools.Result(new JsonObject
                    {
                        ["error"] = true,
                        ["statusCodeValue"] = StatusCodes.BadInvalidArgument.Code,
                        ["message"] = "An unknown PubSub argument was supplied."
                    }));
                }
                return base.InvokeAsync(request, cancellationToken);
            }

            private readonly HashSet<string> m_names;
        }
    }
}
