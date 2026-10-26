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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Shared typed-model helpers for remote PubSub configuration and SKS. Owns no sessions or server processes.
    /// </summary>
    internal static class McpPubSubRemote
    {
        /// <summary>
        /// Requires exact caller/session authority and an encrypted existing channel.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static OpcUaSessionManager.SessionInfo Session(
            OpcUaSessionManager sessions, string? name, McpCapability capability)
        {
            OpcUaSessionManager.SessionInfo info = sessions.GetSessionInfoOrThrow(name);
            _ = sessions.OperationPolicy.RequireSession(capability, info.Name);
            if (info.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient,
                    "Remote PubSub administration and key operations require SignAndEncrypt.");
            }
            return info;
        }

        /// <summary>
        /// Resolves a local namespace-qualified node without accepting cross-server references.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        internal static NodeId Node(ISession session, string text)
        {
            PubSubInputValidation.Text(text);
            var expanded = ExpandedNodeId.Parse(text);
            if (expanded.ServerIndex != 0)
            {
                throw new ArgumentException("Remote-server expanded node identities are not accepted.");
            }
            var node = ExpandedNodeId.ToNodeId(expanded, session.NamespaceUris);
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    "The node namespace is not present in the selected session.");
            }
            return node;
        }

        /// <summary>
        /// Requires a concrete remote model operation in addition to the caller's named-session grant.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void RequireTarget(
            OpcUaSessionManager.SessionInfo session, NodeId node, string operation, McpPubSubOptions options)
        {
            if (!options.ModelRules.ToList().Any(rule => rule.SessionName == session.Name &&
                rule.Operations.Contains(operation) &&
                Node(session.Session, rule.NodeId) == node))
            {
                throw McpPubSubTransportProvider.Refused();
            }
        }

        /// <summary>
        /// Verifies the actual remote object type, not a caller-supplied method label.
        /// </summary>
        internal static async ValueTask<NodeId> TargetAsync(
            ISession session, string text, ExpandedNodeId type, CancellationToken ct)
        {
            NodeId node = Node(session, text);
            _ = await McpModelAccess.RequireTypeAsync(session, node, type, ct).ConfigureAwait(false);
            return node;
        }

        /// <summary>
        /// Resolves the existing standard configuration file using its generated parent proxy.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<PubSubConfigurationTypeClient> ConfigurationAsync(
            ISession session, CancellationToken ct)
        {
            _ = await McpModelAccess.RequireTypeAsync(
                session, ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType, ct).ConfigureAwait(false);
            return await new PublishSubscribeTypeClient(
                session, ObjectIds.PublishSubscribe, session.MessageContext.Telemetry)
                .GetPubSubConfigurationAsync(session.MessageContext.Telemetry, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The selected server does not expose the standard PubSubConfiguration object.");
        }

        /// <summary>
        /// Reads bounded fields/properties and preserves optional absence, quality and timestamps.
        /// </summary>
        internal static async ValueTask<JsonObject> PropertiesAsync(
            ISession session, NodeId node, ArrayOf<string> properties, CancellationToken ct)
        {
            var result = new JsonObject
            {
                ["nodeId"] = node.ToString(),
                ["expandedNodeId"] = NodeId.ToExpandedNodeId(node, session.NamespaceUris).ToString()
            };
            foreach (string name in properties.ToList())
            {
                (NodeId child, DataValue value) = await McpModelAccess.PropertyAsync(
                    session, node, new QualifiedName(name), ct).ConfigureAwait(false);
                result[name] = child.IsNull ? new JsonObject { ["present"] = false } :
                    new JsonObject
                    {
                        ["present"] = true,
                        ["value"] = McpCompanionJson.DataValue(value, session.MessageContext)
                    };
            }
            return result;
        }

        /// <summary>
        /// Marks a completed generated call without inventing an unavailable Good service subcode.
        /// </summary>
        internal static JsonObject Completed()
        {
            return new JsonObject
            {
                ["methodCompleted"] = true,
                ["atomicity"] = "only as provided by the remote standard method; no client-side rollback",
                ["retryAdvice"] = "Do not repeat a completed mutation to obtain missing optional details."
            };
        }

        /// <summary>
        /// Adds real per-item outcomes and marks partial server results explicitly.
        /// </summary>
        internal static void Statuses(JsonObject result, ArrayOf<StatusCode> statuses)
        {
            var values = new JsonArray();
            foreach (StatusCode status in statuses)
            {
                values.Add((JsonNode)new JsonObject { ["code"] = status.Code, ["name"] = status.ToString() });
            }
            result["results"] = values;
            bool hasBad = statuses.FindIndex(StatusCode.IsBad) >= 0;
            bool hasGood = statuses.FindIndex(StatusCode.IsGood) >= 0;
            result["partial"] = hasBad && hasGood;
            result["error"] = hasBad;
        }

        /// <summary>
        /// Converts exact variable selections to the standard generated method parameter type.
        /// </summary>
        internal static ArrayOf<PublishedVariableDataType> Variables(
            ISession session, PubSubVariablesInput input, McpPubSubOptions options)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentOutOfRangeException.ThrowIfLessThan(input.Variables.Count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Variables.Count, options.MaxFields);
            var names = new HashSet<string>(StringComparer.Ordinal);
            return new ArrayOf<PublishedVariableDataType>(input.Variables.ToList().Select(variable =>
            {
                ArgumentNullException.ThrowIfNull(variable);
                PubSubInputValidation.Text(variable.Name, 128);
                if (!names.Add(variable.Name) || variable.AttributeId is < 1 or > Attributes.AccessLevelEx)
                {
                    throw new ArgumentException(
                        "Published variables require unique field names and a valid attribute.");
                }
                return new PublishedVariableDataType
                {
                    PublishedVariable = Node(session, variable.NodeId),
                    AttributeId = variable.AttributeId
                };
            }).ToArray());
        }

        /// <summary>
        /// Decodes only the existing stack configuration representation under a caller-enforced byte ceiling.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static PubSubConfigurationDataType Decode(
            McpBoundedMemoryStream stream, ITelemetryContext telemetry)
        {
            stream.Position = 0;
            try
            {
                return UaPubSubConfigurationHelper.LoadConfiguration(stream, telemetry);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "The artifact is not a supported standard PubSub XML configuration.");
            }
        }

        /// <summary>
        /// Decodes the standard binary-file envelope and maps its namespace table into the selected Session.
        /// </summary>
        internal static PubSubConfiguration2DataType DecodeBinary(byte[] bytes, ISession session)
        {
            try
            {
                using var probe = new BinaryDecoder(bytes, session.MessageContext);
                ExtensionObject encoded = probe.ReadExtensionObject(null);
                if (probe.Position != bytes.Length ||
                    !encoded.TryGetValue(out UABinaryFileDataType? file) || file is null)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "The server file must contain one UABinaryFileDataType ExtensionObject.");
                }
                var namespaces = new NamespaceTable();
                foreach (string uri in file.Namespaces)
                {
                    if (string.IsNullOrEmpty(uri) || namespaces.GetIndex(uri) >= 0 ||
                        session.NamespaceUris.GetIndex(uri) < 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                            "The server file contains a duplicate or unknown namespace URI.");
                    }
                    namespaces.Append(uri);
                }
                using var decoder = new BinaryDecoder(bytes, session.MessageContext);
                decoder.SetMappingTables(namespaces, null);
                encoded = decoder.ReadExtensionObject(null);
                if (!encoded.TryGetValue(out file) || file is null ||
                    !file.Body.TryGetValue(out ExtensionObject body) ||
                    !body.TryGetValue(out PubSubConfiguration2DataType? configuration) || configuration is null)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "The binary file Body must be PubSubConfiguration2DataType.");
                }
                return configuration;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "The server did not return a supported standard PubSub binary configuration.");
            }
        }

        /// <summary>
        /// Encodes a sanitized configuration as the standard Part 5 binary-file ExtensionObject.
        /// </summary>
        internal static void EncodeBinary(
            PubSubConfigurationDataType configuration, ISession session, Stream destination)
        {
            PubSubConfiguration2DataType body = configuration is PubSubConfiguration2DataType extended
                ? (PubSubConfiguration2DataType)extended.Clone()
                : new PubSubConfiguration2DataType
                {
                    Enabled = configuration.Enabled,
                    Connections = configuration.Connections,
                    PublishedDataSets = configuration.PublishedDataSets
                };
            var file = new UABinaryFileDataType
            {
                Namespaces = [.. session.NamespaceUris.ToArray().Skip(1)],
                Body = Variant.From(new ExtensionObject(body))
            };
            using var encoder = new BinaryEncoder(destination, session.MessageContext, leaveOpen: true);
            encoder.WriteExtensionObject(null, new ExtensionObject(file));
        }
    }
}
