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
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Fixed client-workflow contracts and status-preserving projections.
    /// </summary>
    internal static class WorkflowJson
    {
        /// <summary>
        /// Validates a caller-selected page against the host ceiling before any server work.
        /// </summary>
        internal static void Page(ClientWorkflowOptions options, int maxResults, int offset = 0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, options.MaxItems);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, 1_000_000);
        }

        /// <summary>
        /// Validates a bounded text field without incorporating sensitive input in errors.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void Text(string value, int maximum = 2048, bool empty = false)
        {
            ArgumentNullException.ThrowIfNull(value);
            if ((!empty && string.IsNullOrWhiteSpace(value)) ||
                value.Length > maximum ||
                value.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("A text field is empty, invalid, or exceeds the host length limit.");
            }
        }

        /// <summary>
        /// Resolves local or URI-qualified identifiers without inventing namespaces.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static NodeId Node(ISession session, string text)
        {
            Text(text);
            var expanded = ExpandedNodeId.Parse(text);
            NodeId node = McpModelAccess.ModelNode(session, expanded);
            if (node.IsNull || session.NamespaceUris.GetString(node.NamespaceIndex) is null)
            {
                throw new ArgumentException("A non-null local node identifier is required.", nameof(text));
            }
            return node;
        }

        /// <summary>
        /// Keeps both the session-local NodeId and its namespace-URI identity.
        /// </summary>
        internal static JsonObject Identity(ISession session, NodeId node)
        {
            string? namespaceUri = session.NamespaceUris.GetString(node.NamespaceIndex);
            return new JsonObject
            {
                ["nodeId"] = node.ToString(),
                ["namespaceUri"] = namespaceUri,
                ["expandedNodeId"] = node.IsNull ? NodeId.Null.ToString() : namespaceUri is null ? null :
                    NodeId.ToExpandedNodeId(node, session.NamespaceUris).ToString()
            };
        }

        /// <summary>
        /// Projects one real browse reference, including its exact namespace and type.
        /// </summary>
        internal static JsonObject Reference(ISession session, ReferenceDescription reference)
        {
            NodeId node = McpModelAccess.ModelNode(session, reference.NodeId);
            JsonObject result = Identity(session, node);
            result["browseName"] = reference.BrowseName.Name;
            result["browseNamespaceUri"] = session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex);
            result["typeDefinition"] = reference.TypeDefinition.IsNull ? null :
                NodeId.ToExpandedNodeId(McpModelAccess.ModelNode(session, reference.TypeDefinition),
                    session.NamespaceUris).ToString();
            result["referenceTypeId"] = reference.ReferenceTypeId.ToString();
            result["referenceTypeExpandedNodeId"] = reference.ReferenceTypeId.IsNull ? null :
                NodeId.ToExpandedNodeId(reference.ReferenceTypeId, session.NamespaceUris).ToString();
            result["displayName"] = EncodeVariant(
                Variant.From(reference.DisplayName), session.MessageContext);
            return result;
        }

        /// <summary>
        /// Preserves a method's actual status and input-argument statuses without echoing arguments.
        /// </summary>
        internal static JsonObject Method(
            ISession session, NodeId target, CallMethodResult result, bool includeOutputs = true)
        {
            JsonObject value = Identity(session, target);
            value["error"] = StatusCode.IsBad(result.StatusCode);
            value["statusCode"] = result.StatusCode.ToString();
            value["statusCodeValue"] = result.StatusCode.Code;
            value["inputArgumentResults"] = Statuses(result.InputArgumentResults);
            if (includeOutputs)
            {
                var output = new JsonArray();
                foreach (Variant argument in result.OutputArguments)
                {
                    output.Add(EncodeVariant(argument, session.MessageContext));
                }
                value["outputArguments"] = output;
            }
            return value;
        }

        /// <summary>
        /// Encodes a value through the stack into a bounded buffer before constructing an MCP JSON tree.
        /// </summary>
        internal static JsonNode? EncodeVariant(in Variant value, IServiceMessageContext context)
        {
            using var buffer = new McpBoundedMemoryStream(kMaximumValueJsonBytes);
            using (var encoder = new JsonEncoder(buffer, context, JsonEncoderOptions.Verbose))
            {
                encoder.WriteVariant("value", value);
                _ = encoder.Close();
            }
            return ReadEncodedValue(buffer);
        }

        /// <summary>
        /// Bounds encoded historical data before allocating its projected JSON value.
        /// </summary>
        internal static JsonNode? EncodeDataValue(in DataValue value, IServiceMessageContext context)
        {
            using var buffer = new McpBoundedMemoryStream(kMaximumValueJsonBytes);
            using (var encoder = new JsonEncoder(buffer, context, JsonEncoderOptions.Verbose))
            {
                encoder.WriteDataValue("value", value);
                _ = encoder.Close();
            }
            return ReadEncodedValue(buffer);
        }

        /// <summary>
        /// Bounds encoding of one standard structure before building its JSON tree.
        /// </summary>
        /// <typeparam name="T">The generated standard encodeable type.</typeparam>
        internal static JsonNode? Encode<T>(T value, IServiceMessageContext context)
            where T : IEncodeable
        {
            using var buffer = new McpBoundedMemoryStream(kMaximumValueJsonBytes);
            using (var encoder = new JsonEncoder(buffer, context, JsonEncoderOptions.Verbose))
            {
                encoder.WriteEncodeable("value", value, default);
                _ = encoder.Close();
            }
            return ReadEncodedValue(buffer);
        }

        /// <summary>
        /// Projects fixed model properties through bounded encoding, preserving actual quality and optional absence.
        /// </summary>
        internal static async ValueTask<JsonObject> PropertiesAsync(
            ISession session, NodeId parent, ArrayOf<QualifiedName> names, CancellationToken ct)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(names.Count, 64);
            var properties = new JsonObject();
            bool error = false;
            for (int index = 0; index < names.Count; index++)
            {
                QualifiedName name = names[index];
                (NodeId node, DataValue value) = await McpModelAccess.PropertyAsync(session, parent, name, ct)
                    .ConfigureAwait(false);
                properties[name.Name!] = new JsonObject
                {
                    ["nodeId"] = node.IsNull ? null : node.ToString(),
                    ["expandedNodeId"] = node.IsNull ? null :
                        NodeId.ToExpandedNodeId(node, session.NamespaceUris).ToString(),
                    ["supported"] = !node.IsNull,
                    ["dataValue"] = node.IsNull ? null : EncodeDataValue(value, session.MessageContext)
                };
                error |= !node.IsNull && StatusCode.IsBad(value.StatusCode);
            }
            JsonObject result = Identity(session, parent);
            result["error"] = error;
            result["properties"] = properties;
            return result;
        }

        /// <summary>
        /// Projects operation statuses without implying atomicity.
        /// </summary>
        internal static JsonArray Statuses(ArrayOf<StatusCode> results)
        {
            var statuses = new JsonArray();
            foreach (StatusCode status in results)
            {
                statuses.Add((JsonNode)new JsonObject
                {
                    ["statusCode"] = status.ToString(),
                    ["statusCodeValue"] = status.Code
                });
            }
            return statuses;
        }

        /// <summary>
        /// Returns the exact ordered statuses for a non-atomic historical or attribute update.
        /// </summary>
        internal static JsonObject Update(ISession session, NodeId target, ArrayOf<StatusCode> results)
        {
            JsonObject response = Identity(session, target);
            bool error = false;
            foreach (StatusCode result in results)
            {
                error |= StatusCode.IsBad(result);
            }
            response["error"] = error;
            response["atomic"] = false;
            response["results"] = Statuses(results);
            return response;
        }

        /// <summary>
        /// Requires host authorization for the actual selected session, then encrypted transport.
        /// </summary>
        internal static async ValueTask<IClientWorkflowSessionScope> IdentityScopeAsync(
            OpcUaSessionManager manager, IClientWorkflowSessionFactory scopes,
            string? sessionName, CancellationToken ct)
        {
            OpcUaSessionManager.SessionInfo info = manager.GetSessionInfoOrThrow(sessionName);
            _ = manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, info.Name);
            IClientWorkflowSessionScope scope = await scopes.CaptureAsync(info, ct).ConfigureAwait(false);
            try
            {
                McpModelAccess.RequireSecureMutation(scope.Access.Session);
                return scope;
            }
            catch
            {
                await scope.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Parses only ISO timestamps with an explicit UTC designator or numeric offset.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static DateTime Utc(string text)
        {
            Text(text, 64);
            if (!DateTimeOffset.TryParseExact(text, s_timestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset instant))
            {
                throw new ArgumentException("Use an ISO 8601 timestamp with an explicit Z or numeric UTC offset.");
            }
            return instant.UtcDateTime;
        }

        /// <summary>
        /// Reads exact attributes without converting bad quality or unsupported attributes into defaults.
        /// </summary>
        internal static async ValueTask<JsonObject> AttributesAsync(
            ISession session, NodeId node, ArrayOf<uint> attributes, CancellationToken ct)
        {
            var requests = new ReadValueId[attributes.Count];
            for (int index = 0; index < requests.Length; index++)
            {
                requests[index] = new ReadValueId { NodeId = node, AttributeId = attributes[index] };
            }
            ArrayOf<ReadValueId> requestList = requests.ToArrayOf();
            ReadResponse read = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both, requestList, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(read.Results, requestList);
            ClientBase.ValidateDiagnosticInfos(read.DiagnosticInfos, requestList);
            var values = new JsonObject();
            for (int index = 0; index < requests.Length; index++)
            {
                values[AttributeName(attributes[index])] =
                    EncodeDataValue(read.Results[index], session.MessageContext);
            }
            JsonObject result = Identity(session, node);
            result["attributes"] = values;
            return result;
        }

        /// <summary>
        /// Resolves a fixed namespace-zero optional child, refusing absence before a mutation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<NodeId> ChildAsync(
            ISession session, NodeId parent, string browseName, CancellationToken ct)
        {
            NodeId child = await McpModelAccess.ChildAsync(
                session, parent, new QualifiedName(browseName), ct).ConfigureAwait(false);
            if (child.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The selected object does not expose the requested optional member.");
            }
            return child;
        }

        /// <summary>
        /// Labels the closed attribute set used by these workflows, retaining unknown numeric identities.
        /// </summary>
        private static string AttributeName(uint attribute)
        {
            return attribute switch
            {
                Attributes.RolePermissions => nameof(Attributes.RolePermissions),
                Attributes.UserRolePermissions => nameof(Attributes.UserRolePermissions),
                Attributes.AccessRestrictions => nameof(Attributes.AccessRestrictions),
                Attributes.WriteMask => nameof(Attributes.WriteMask),
                Attributes.UserWriteMask => nameof(Attributes.UserWriteMask),
                Attributes.Historizing => nameof(Attributes.Historizing),
                Attributes.AccessLevel => nameof(Attributes.AccessLevel),
                Attributes.UserAccessLevel => nameof(Attributes.UserAccessLevel),
                Attributes.Executable => nameof(Attributes.Executable),
                Attributes.UserExecutable => nameof(Attributes.UserExecutable),
                _ => attribute.ToString(CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Reads only a validated encoded value and releases the envelope without a second serialization.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static JsonNode? ReadEncodedValue(McpBoundedMemoryStream buffer)
        {
            buffer.Position = 0;
            JsonObject document = JsonNode.Parse(buffer)?.AsObject() ??
                throw new InvalidOperationException("The UA JSON encoder did not write an object.");
            return document["value"]?.DeepClone();
        }

        private const long kMaximumValueJsonBytes = 64 * 1024;

        private static readonly string[] s_timestampFormats =
        [
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
        ];
    }
}
