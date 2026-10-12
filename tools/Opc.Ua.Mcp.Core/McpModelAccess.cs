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
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Shared model-aware reads and bounded discovery for companion tools.
    /// </summary>
    public static class McpModelAccess
    {
        /// <summary>
        /// Resolves a model identifier without adding an unsupported namespace to the session.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static NodeId ModelNode(ISessionClient session, ExpandedNodeId identifier)
        {
            ArgumentNullException.ThrowIfNull(session);
            var node = ExpandedNodeId.ToNodeId(identifier, session.MessageContext.NamespaceUris);
            if (identifier.ServerIndex != 0 || node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The selected session does not expose the requested model identifier.");
            }
            return node;
        }

        /// <summary>
        /// Resolves exactly one qualified local child, distinguishing optional absence from errors.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<NodeId> ChildAsync(
            ISessionClient session, NodeId parent, QualifiedName name, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = name
                            }
                        ]
                    }
                }
            ];
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, paths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, paths);
            BrowsePathResult result = response.Results[0];
            if (result.StatusCode == StatusCodes.BadNoMatch || result.StatusCode == StatusCodes.BadNotFound)
            {
                return NodeId.Null;
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.Targets.Count == 0)
            {
                return NodeId.Null;
            }
            if (result.Targets.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated, "The child path is ambiguous.");
            }
            BrowsePathTarget target = result.Targets[0];
            if (target.RemainingPathIndex != uint.MaxValue)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The child path is only partly resolved.");
            }
            return ModelNode(session, target.TargetId);
        }

        /// <summary>
        /// Creates a qualified name only for a namespace actually advertised by the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static QualifiedName Name(ISessionClient session, string namespaceUri, string name)
        {
            ArgumentNullException.ThrowIfNull(session);
            int index = session.MessageContext.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The model namespace is not available.");
            }
            return new QualifiedName(name, checked((ushort)index));
        }

        /// <summary>
        /// Reads a qualified property and preserves its quality and timestamps. A null NodeId means optional absence.
        /// </summary>
        public static async ValueTask<(NodeId NodeId, DataValue Value)> PropertyAsync(
            ISessionClient session, NodeId parent, QualifiedName name, CancellationToken ct = default)
        {
            NodeId node = await ChildAsync(session, parent, name, ct).ConfigureAwait(false);
            if (node.IsNull)
            {
                return (node, default);
            }
            return (node, await ReadDataValueAsync(session, node, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Reads one value without losing its bad quality, source timestamp or server timestamp.
        /// </summary>
        public static async ValueTask<DataValue> ReadDataValueAsync(
            ISessionClient session, NodeId node, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArrayOf<ReadValueId> requests = [new ReadValueId { NodeId = node, AttributeId = Attributes.Value }];
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both, requests, ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            return response.Results[0];
        }

        /// <summary>
        /// Projects a finite list of known properties without confusing bad quality with optional absence.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static async ValueTask<JsonObject> PropertiesAsync(
            ISessionClient session, NodeId parent, ArrayOf<QualifiedName> names, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (names.Count > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(names));
            }
            var properties = new JsonObject();
            bool error = false;
            for (int index = 0; index < names.Count; index++)
            {
                QualifiedName name = names[index];
                (NodeId node, DataValue value) = await PropertyAsync(session, parent, name, ct).ConfigureAwait(false);
                properties[name.Name!] = new JsonObject
                {
                    ["nodeId"] = node.IsNull ? null : node.ToString(),
                    ["supported"] = !node.IsNull,
                    ["dataValue"] = node.IsNull ? null : McpCompanionJson.DataValue(value, session.MessageContext)
                };
                error |= !node.IsNull && StatusCode.IsBad(value.StatusCode);
            }
            return new JsonObject { ["nodeId"] = parent.ToString(), ["error"] = error, ["properties"] = properties };
        }

        /// <summary>
        /// Reads an object's actual type definition, rejecting missing or ambiguous type information.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<NodeId> TypeDefinitionAsync(
            ISession session, NodeId node, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            NodeId type = default;
            await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                node, ReferenceTypeIds.HasTypeDefinition, nodeClassMask: (uint)NodeClass.ObjectType, ct: ct)
                .ConfigureAwait(false))
            {
                if (!type.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                        "The object has more than one type definition.");
                }
                type = ModelNode(session, reference.NodeId);
            }
            if (type.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                    "An object with a type definition is required.");
            }
            return type;
        }

        /// <summary>
        /// Requires the requested model type or a server-advertised subtype.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<NodeId> RequireTypeAsync(
            ISession session, NodeId node, ExpandedNodeId expectedType, CancellationToken ct = default)
        {
            NodeId expected = ModelNode(session, expectedType);
            NodeId actual = await TypeDefinitionAsync(session, node, ct).ConfigureAwait(false);
            if (actual != expected &&
                !await session.NodeCache.IsTypeOfAsync(actual, expected, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The node does not implement the requested model type.");
            }
            return actual;
        }

        /// <summary>
        /// Discovers matching object references below a bounded hierarchy, preserving vendor type identifiers.
        /// </summary>
        public static IAsyncEnumerable<ReferenceDescription> DiscoverAsync(
            ISession session, NodeId root, ExpandedNodeId expectedType,
            int maxDepth = 3, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDepth, 6);
            return DiscoverCoreAsync(session, root, ModelNode(session, expectedType), maxDepth, ct);
        }

        /// <summary>
        /// Projects an address-space reference without following remote-server references.
        /// </summary>
        public static JsonObject Reference(ReferenceDescription reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            return new JsonObject
            {
                ["nodeId"] = reference.NodeId.ToString(),
                ["browseName"] = reference.BrowseName.ToString(),
                ["displayName"] = reference.DisplayName.IsNull ? null : reference.DisplayName.Text,
                ["locale"] = reference.DisplayName.IsNull ? null : reference.DisplayName.Locale,
                ["nodeClass"] = reference.NodeClass.ToString(),
                ["typeDefinitionId"] = reference.TypeDefinition.ToString(),
                ["remote"] = reference.NodeId.ServerIndex != 0
            };
        }

        /// <summary>
        /// Adds usable session-local identifiers while retaining expanded identities for remote references.
        /// </summary>
        public static JsonObject Reference(ISessionClient session, ReferenceDescription reference)
        {
            ArgumentNullException.ThrowIfNull(session);
            JsonObject result = Reference(reference);
            result["expandedNodeId"] = reference.NodeId.ToString();
            if (reference.NodeId.ServerIndex == 0)
            {
                var local = ExpandedNodeId.ToNodeId(reference.NodeId, session.MessageContext.NamespaceUris);
                result["nodeId"] = local.IsNull ? null : local.ToString();
                result["local"] = !local.IsNull;
            }
            else
            {
                result["nodeId"] = null;
                result["local"] = false;
            }
            if (reference.TypeDefinition.ServerIndex == 0)
            {
                var type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, session.MessageContext.NamespaceUris);
                result["typeDefinitionId"] = type.IsNull ? null : type.ToString();
            }
            return result;
        }

        /// <summary>
        /// Preserves the method status, input argument results and typed outputs in matching MCP content.
        /// </summary>
        public static JsonObject MethodResult(CallMethodResult result, IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            var outputs = new JsonArray();
            foreach (Variant value in result.OutputArguments)
            {
                outputs.Add(McpCompanionJson.Variant(value, context));
            }
            var inputs = new JsonArray();
            foreach (StatusCode status in result.InputArgumentResults)
            {
                JsonNode value = JsonValue.Create(status.Code);
                inputs.Add(value);
            }
            return new JsonObject
            {
                ["error"] = StatusCode.IsBad(result.StatusCode),
                ["statusCode"] = result.StatusCode.ToString(),
                ["statusCodeValue"] = result.StatusCode.Code,
                ["inputArgumentResults"] = inputs,
                ["outputArguments"] = outputs
            };
        }

        /// <summary>
        /// Detects explicitly failed nested results, including a bounded page's selected items.
        /// </summary>
        public static bool HasError(JsonObject value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value["error"] is JsonValue flag && flag.TryGetValue(out bool error) && error)
            {
                return true;
            }
            if (value["items"] is JsonArray items)
            {
                foreach (JsonNode? item in items)
                {
                    if (item is JsonObject child && HasError(child))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Observes a generated event filter and preserves every selected field as a typed UA value.
        /// </summary>
        public static ValueTask<JsonObject> ObserveEventsAsync(
            ISession session,
            NodeId notifier,
            EventFilter filter,
            int durationMs = 1000,
            int maxItems = 100,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(filter);
            return McpCompanionTools.ObserveAsync(
                token => McpCompanionTools.GetStreaming(session).SubscribeEventsAsync(notifier, filter, ct: token),
                notification => Event(notification, filter, session.MessageContext), durationMs, maxItems, ct);
        }

        /// <summary>
        /// Requires encrypted registry mutations and an explicit opt-in to a zero concurrency token.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static void RequireSecureMutation(
            ISession session, uint? expectedCounter = null, bool unconditional = false)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (session.ConfiguredEndpoint.Description.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient,
                    "Registry and WoT mutations require SignAndEncrypt.");
            }
            if (expectedCounter == 0 && !unconditional)
            {
                throw new ArgumentException("A zero counter disables concurrency checks; explicitly allow it.");
            }
        }

        /// <summary>
        /// Uses the generated filter's qualified paths as stable field keys, not reflection over event records.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static JsonObject Event(
            EventNotification notification, EventFilter filter, IServiceMessageContext context)
        {
            if (notification.Fields.Count != filter.SelectClauses.Count)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "The notification does not match the selected event fields.");
            }
            var fields = new JsonObject();
            for (int index = 0; index < filter.SelectClauses.Count; index++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[index];
                string path = string.Join("/", clause.BrowsePath.ConvertAll(name => name.ToString()).ToArray()!);
                string key = string.IsNullOrEmpty(path) ? "@" + clause.AttributeId : path;
                fields[key] = McpCompanionJson.Variant(notification.Fields[index], context);
            }
            return new JsonObject { ["fields"] = fields, ["partitionServerId"] = notification.PartitionServerId };
        }

        /// <summary>
        /// Walks at most 1000 objects and never follows a remote reference.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async IAsyncEnumerable<ReferenceDescription> DiscoverCoreAsync(
            ISession session, NodeId root, NodeId expected, int maxDepth, [EnumeratorCancellation] CancellationToken ct)
        {
            var pending = new Queue<(NodeId Node, int Depth)>();
            var visited = new HashSet<NodeId> { root };
            pending.Enqueue((root, 0));
            int scanned = 0;
            while (pending.Count > 0)
            {
                (NodeId parent, int depth) = pending.Dequeue();
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    parent, ReferenceTypeIds.HierarchicalReferences, nodeClassMask: (uint)NodeClass.Object, ct: ct)
                    .ConfigureAwait(false))
                {
                    if (++scanned > 1000)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "Discovery exceeded 1000 references; select a narrower root.");
                    }
                    if (reference.NodeId.ServerIndex != 0)
                    {
                        continue;
                    }
                    NodeId node = ModelNode(session, reference.NodeId);
                    if (!visited.Add(node))
                    {
                        continue;
                    }
                    var type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, session.NamespaceUris);
                    if (!type.IsNull &&
                        (type == expected ||
                            await session.NodeCache.IsTypeOfAsync(type, expected, ct).ConfigureAwait(false)))
                    {
                        yield return reference;
                    }
                    else if (depth < maxDepth)
                    {
                        pending.Enqueue((node, depth + 1));
                    }
                }
            }
        }
    }
}
