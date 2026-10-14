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
    /// Publishes actual bounded input contracts and hides host-disabled catalogues.
    /// Invocation checks remain inside the tools, including when called directly.
    /// </summary>
    internal static class GdsMcpFilters
    {
        /// <summary>
        /// Publishes the concrete contract at registration and keeps promoted structured arrays genuinely bindable.
        /// </summary>
        internal static McpServerTool Wrap(McpServerTool tool)
        {
            return Capability(tool.ProtocolTool.Name) is null ? tool : new InputTool(tool);
        }

        /// <summary>
        /// Applies feature enablement and concrete array schemas without changing argument binding.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Catalogue(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                IMcpOperationPolicy policy = request.Services!.GetRequiredService<IMcpOperationPolicy>();
                var visible = new List<Tool>();
                foreach (Tool tool in result.Tools)
                {
                    McpCapability? capability = Capability(tool.Name);
                    if (capability is null)
                    {
                        visible.Add(tool);
                        continue;
                    }
                    if (!policy.IsEnabled(capability.Value))
                    {
                        continue;
                    }
                    visible.Add(tool);
                }
                result.Tools = visible;
                return result;
            };
        }

        /// <summary>
        /// Materializes closed DTO schemas, promoting only the structured name array to avoid excess nesting.
        /// </summary>
        private static void Shape(Tool tool)
        {
            JsonObject schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
            schema["additionalProperties"] = false;
            JsonObject properties = schema["properties"]!.AsObject();
            JsonNode? contract = InputContract(tool.Name);
            if (contract is not null)
            {
                if (properties["input"] is JsonObject inputSchema &&
                    inputSchema["description"] is JsonNode description)
                {
                    contract["description"] = description.DeepClone();
                }
                Normalize(contract, tool.Name);
                if (tool.Name is "gds_register_application" or "gds_update_application")
                {
                    JsonObject input = contract["properties"]!.AsObject();
                    properties["applicationNames"] = input["applicationNames"]!.DeepClone();
                    input.Remove("applicationNames");
                    JsonArray required = schema["required"]?.AsArray() ?? [];
                    JsonNode requiredNames = JsonValue.Create("applicationNames");
                    required.Add(requiredNames);
                    schema["required"] = required;
                }
                properties["input"] = contract;
            }
            if (properties["maxResults"] is JsonObject maximum)
            {
                maximum["minimum"] = 1;
                maximum["maximum"] = 500;
            }
            if (properties["offset"] is JsonObject offset)
            {
                offset["minimum"] = 0;
                offset["maximum"] = 1_000_000;
            }
            using var document = JsonDocument.Parse(schema.ToJsonString());
            tool.InputSchema = document.RootElement.Clone();
        }

        /// <summary>
        /// Maps only the module-owned prefixes, leaving all unrelated tools untouched.
        /// </summary>
        private static McpCapability? Capability(string name)
        {
            if (name.StartsWith("gds_", StringComparison.Ordinal))
            {
                return McpCapability.DirectoryAdministration;
            }
            if (name.StartsWith("certificate_", StringComparison.Ordinal))
            {
                return McpCapability.CertificateAdministration;
            }
            if (name.StartsWith("credential_", StringComparison.Ordinal))
            {
                return McpCapability.CredentialIssuance;
            }
            return name.StartsWith("onboarding_", StringComparison.Ordinal) ? McpCapability.Onboarding : null;
        }

        /// <summary>
        /// Uses source-generated metadata for every structured input.
        /// </summary>
        private static JsonNode? InputContract(string name)
        {
            return name switch
            {
                "gds_query_applications" or "gds_query_servers" =>
                    GdsInputJsonContext.Default.GdsQueryInput.GetJsonSchemaAsNode(),
                "gds_register_application" or "gds_update_application" =>
                    GdsInputJsonContext.Default.GdsApplicationInput.GetJsonSchemaAsNode(),
                "certificate_start_keypair_request" =>
                    GdsInputJsonContext.Default.GdsKeyPairInput.GetJsonSchemaAsNode(),
                "certificate_create_csr" => GdsInputJsonContext.Default.GdsCsrInput.GetJsonSchemaAsNode(),
                "certificate_create_self_signed" =>
                    GdsInputJsonContext.Default.GdsSelfSignedInput.GetJsonSchemaAsNode(),
                "certificate_stage_update" =>
                    GdsInputJsonContext.Default.GdsPushCertificateInput.GetJsonSchemaAsNode(),
                "credential_start_key_request" =>
                    GdsInputJsonContext.Default.GdsKeyCredentialInput.GetJsonSchemaAsNode(),
                "credential_finish_token_request" =>
                    GdsInputJsonContext.Default.GdsTokenFinishInput.GetJsonSchemaAsNode(),
                "onboarding_register_tickets" or "onboarding_remove_tickets" =>
                    GdsInputJsonContext.Default.GdsTicketInput.GetJsonSchemaAsNode(),
                _ => null
            };
        }

        /// <summary>
        /// Describes custom arrays as concrete JSON arrays and closes every structured DTO.
        /// </summary>
        private static void Normalize(JsonNode node, string toolName)
        {
            if (node is not JsonObject schema || schema["properties"] is not JsonObject properties)
            {
                return;
            }
            schema["additionalProperties"] = false;
            foreach (KeyValuePair<string, JsonNode?> property in properties.ToArray())
            {
                JsonNode? description = property.Value is JsonObject original
                    ? original["description"]?.DeepClone() : null;
                if (property.Key is "serverCapabilities" or "discoveryUrls" or "domainNames" or
                    "dnsNames" or "ipAddresses" or "issuerCertificatePaths" or "requestedRoles" or "ticketReferences")
                {
                    properties[property.Key] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] = description ??
                            JsonValue.Create(property.Key switch
                            {
                                "serverCapabilities" => GdsToolDescriptions.ServerCapabilities,
                                "discoveryUrls" => GdsToolDescriptions.DiscoveryUrls,
                                "domainNames" => GdsToolDescriptions.DomainNames,
                                "dnsNames" => GdsToolDescriptions.DnsNames,
                                "ipAddresses" => GdsToolDescriptions.IpAddresses,
                                "issuerCertificatePaths" => GdsToolDescriptions.IssuerCertificatePaths,
                                "requestedRoles" => toolName == "credential_start_key_request"
                                    ? GdsToolDescriptions.KeyCredentialRoles
                                    : GdsToolDescriptions.AuthorizationRoles,
                                _ => GdsToolDescriptions.TicketReferences
                            }),
                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 2048 },
                        ["minItems"] = property.Key == "ticketReferences" ? 1 : 0,
                        ["maxItems"] = property.Key == "issuerCertificatePaths" ? 32 : 100
                    };
                }
                else if (property.Key == "applicationNames")
                {
                    properties[property.Key] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] = description ?? JsonValue.Create(GdsToolDescriptions.ApplicationNames),
                        ["items"] = GdsInputJsonContext.Default.GdsApplicationName.GetJsonSchemaAsNode(),
                        ["minItems"] = 1,
                        ["maxItems"] = 100
                    };
                }
                else if (property.Value is not null)
                {
                    Normalize(property.Value, toolName);
                }
            }
        }

        /// <summary>
        /// Validates root argument names and binds the advertised structured application-name array.
        /// </summary>
        private sealed class InputTool : DelegatingMcpServerTool
        {
            /// <summary>
            /// Shapes only this module's tool and snapshots its closed root argument names.
            /// </summary>
            internal InputTool(McpServerTool inner)
                : base(inner)
            {
                Shape(inner.ProtocolTool);
                m_promoteNames = inner.ProtocolTool.Name is "gds_register_application" or "gds_update_application";
                m_arguments = new HashSet<string>(inner.ProtocolTool.InputSchema.GetProperty("properties")
                    .EnumerateObject().Select(property => property.Name), StringComparer.Ordinal);
            }

            /// <inheritdoc/>
            public override async ValueTask<CallToolResult> InvokeAsync(
                RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(request);
                CallToolRequestParams parameters = request.Params ??
                    throw new ArgumentException("The GDS tool request has no arguments.", nameof(request));
                IDictionary<string, JsonElement>? original = parameters.Arguments;
                if (original is not null && original.Keys.Any(name => !m_arguments.Contains(name)))
                {
                    return McpCompanionTools.Result(GdsJson.Error(
                        StatusCodes.BadInvalidArgument, "An unknown GDS tool argument was supplied."));
                }
                if (!m_promoteNames)
                {
                    return await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }
                if (original is null ||
                    !original.TryGetValue("applicationNames", out JsonElement names) ||
                    names.ValueKind != JsonValueKind.Array ||
                    !original.TryGetValue("input", out JsonElement input) ||
                    input.ValueKind != JsonValueKind.Object ||
                    input.EnumerateObject().Any(property =>
                        string.Equals(property.Name, "applicationNames", StringComparison.OrdinalIgnoreCase)))
                {
                    return McpCompanionTools.Result(GdsJson.Error(StatusCodes.BadInvalidArgument,
                        "Supply applicationNames as a root JSON array and registration fields as input."));
                }
                JsonObject packed = JsonNode.Parse(input.GetRawText())!.AsObject();
                packed["applicationNames"] = JsonNode.Parse(names.GetRawText());
                using var document = JsonDocument.Parse(packed.ToJsonString());
                var arguments = new Dictionary<string, JsonElement>(original, StringComparer.Ordinal)
                {
                    ["input"] = document.RootElement.Clone()
                };
                arguments.Remove("applicationNames");
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

            private readonly bool m_promoteNames;
            private readonly HashSet<string> m_arguments;
        }
    }
}
