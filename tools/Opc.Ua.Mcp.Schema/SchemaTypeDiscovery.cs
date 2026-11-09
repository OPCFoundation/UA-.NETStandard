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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Schema;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Bounded, operation-local discovery without a retained node-cache proxy or unlimited namespace expansion.
    /// </summary>
    internal static class SchemaTypeDiscovery
    {
        /// <summary>
        /// Enumerates only a finite registered namespace, activating definitions after the scan/type checks.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static IEnumerable<UaTypeDescription> Registered(
            IServiceMessageContext context, IDataTypeDefinitionResolver? registered,
            string namespaceUri, McpSchemaOptions options, CancellationToken ct)
        {
            var seen = new HashSet<NodeId>();
            if (registered is not null)
            {
                IReadOnlyCollection<UaTypeDescription> descriptions = registered.GetNamespaceTypes(namespaceUri);
                if (descriptions.Count > options.MaxTypes)
                {
                    throw SchemaDefinitionSnapshot.Limit("The registered namespace exceeds the host type limit.");
                }
                foreach (UaTypeDescription description in descriptions)
                {
                    ct.ThrowIfCancellationRequested();
                    NodeId id = LocalId(description.TypeId, context.NamespaceUris);
                    if (string.Equals(context.NamespaceUris.GetString(id.NamespaceIndex), namespaceUri,
                        StringComparison.Ordinal) &&
                        seen.Add(id))
                    {
                        yield return Normalize(id, description, context);
                    }
                }
            }
            var source = new EncodeableFactoryDefinitionSource(context.Factory, context.NamespaceUris);
            var activators = new HashSet<IEncodeableType>();
            int scanned = 0;
            foreach (ExpandedNodeId candidate in context.Factory.KnownTypeIds)
            {
                ct.ThrowIfCancellationRequested();
                if (++scanned > 4096)
                {
                    throw SchemaDefinitionSnapshot.Limit("The registered identifier scan exceeds the host limit.");
                }
                string? uri = string.IsNullOrEmpty(candidate.NamespaceUri)
                    ? context.NamespaceUris.GetString(candidate.InnerNodeId.NamespaceIndex)
                    : candidate.NamespaceUri;
                if (!string.Equals(uri, namespaceUri, StringComparison.Ordinal))
                {
                    continue;
                }
                ExpandedNodeId typeId = candidate;
                if (context.Factory.TryGetEncodeableType(candidate, out IEncodeableType? encodeable))
                {
                    if (!activators.Add(encodeable))
                    {
                        continue;
                    }
                    typeId = encodeable.CreateInstance().TypeId;
                }
                NodeId id = LocalId(typeId, context.NamespaceUris);
                if (!string.Equals(context.NamespaceUris.GetString(id.NamespaceIndex), namespaceUri,
                    StringComparison.Ordinal))
                {
                    continue;
                }
                if (!seen.Add(id))
                {
                    continue;
                }
                if (seen.Count > options.MaxTypes)
                {
                    throw SchemaDefinitionSnapshot.Limit("The registered namespace exceeds the host type limit.");
                }
                if (source.TryResolve(typeId, out UaTypeDescription? description))
                {
                    yield return Normalize(id, description, context);
                }
            }
        }

        /// <summary>
        /// Streams HasSubtype references and releases continuation points through the shared client helper.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async IAsyncEnumerable<ReferenceDescription> RemoteAsync(
            ISession session, NodeId root, string? namespaceUri, McpSchemaOptions options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var visited = new HashSet<NodeId> { root };
            var pending = new Queue<(NodeId Id, int Depth)>();
            pending.Enqueue((root, 0));
            int scanned = 0;
            while (pending.Count > 0)
            {
                (NodeId id, int depth) = pending.Dequeue();
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    id, ReferenceTypeIds.HasSubtype, includeSubtypes: false,
                    nodeClassMask: (uint)NodeClass.DataType, pageSize: (uint)Math.Min(options.MaxTypes, 100), ct: ct)
                    .ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    if (++scanned > options.MaxTypes)
                    {
                        throw SchemaDefinitionSnapshot.Limit("The DataType browse exceeds the host type limit.");
                    }
                    NodeId child = McpModelAccess.ModelNode(session, reference.NodeId);
                    if (!visited.Add(child))
                    {
                        continue;
                    }
                    if (depth >= options.MaxDepth)
                    {
                        throw SchemaDefinitionSnapshot.Limit("The DataType hierarchy exceeds the host depth limit.");
                    }
                    pending.Enqueue((child, depth + 1));
                    if (string.IsNullOrEmpty(namespaceUri) ||
                        string.Equals(namespaceUri,
                            session.NamespaceUris.GetString(child.NamespaceIndex), StringComparison.Ordinal))
                    {
                        yield return reference;
                    }
                }
            }
        }

        /// <summary>
        /// Reads exact attributes, preserving their quality for inspection before interpreting any definition.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<ArrayOf<DataValue>> ReadAttributesAsync(
            ISession session, NodeId id, CancellationToken ct)
        {
            ArrayOf<ReadValueId> requests =
            [
                new ReadValueId { NodeId = id, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = id, AttributeId = Attributes.DataTypeDefinition },
                new ReadValueId { NodeId = id, AttributeId = Attributes.IsAbstract }
            ];
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both, requests, ct)
                .ConfigureAwait(false);
            if (StatusCode.IsNotGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            return response.Results;
        }

        /// <summary>
        /// Reads and validates a single type without guessing from its name or namespace.
        /// </summary>
        internal static async ValueTask<(UaTypeDescription Description, DataTypeNode Node)> ReadTypeAsync(
            ISession session, NodeId id, McpSchemaOptions options, CancellationToken ct)
        {
            ArrayOf<DataValue> attributes = await ReadAttributesAsync(session, id, ct).ConfigureAwait(false);
            return await DescribeAsync(session, id, attributes, options, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Interprets only good-quality definition attributes and resolves OptionSet representation explicitly.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<(UaTypeDescription Description, DataTypeNode Node)> DescribeAsync(
            ISession session, NodeId id, ArrayOf<DataValue> attributes,
            McpSchemaOptions options, CancellationToken ct)
        {
            foreach (DataValue value in attributes)
            {
                if (value.IsNotGood)
                {
                    throw new ServiceResultException(value.StatusCode, "A DataType attribute has non-good quality.");
                }
            }
            if (!attributes[0].WrappedValue.TryGetValue(out QualifiedName name) ||
                name.IsNull ||
                string.IsNullOrWhiteSpace(name.Name) ||
                !attributes[1].WrappedValue.TryGetValue(out ExtensionObject encoded) ||
                !encoded.TryGetValue(out DataTypeDefinition? definition) ||
                !attributes[2].WrappedValue.TryGetValue(out bool isAbstract))
            {
                throw SchemaDefinitionSnapshot.Unsupported("The selected node has no usable DataTypeDefinition.");
            }
            SchemaDefinitionSnapshot.ValidateDefinition(definition, options);
            bool structureOptionSet = definition is EnumDefinition { IsOptionSet: true } &&
                await IsStructureOptionSetAsync(session, id, options, ct).ConfigureAwait(false);
            return (new UaTypeDescription(id, name, definition, session.NamespaceUris.GetString(id.NamespaceIndex),
                structureOptionSet), new DataTypeNode
                {
                    NodeId = id,
                    BrowseName = name,
                    IsAbstract = isAbstract,
                    DataTypeDefinition = encoded
                });
        }

        /// <summary>
        /// Converts URI or index identifiers without appending namespaces or accepting cross-server references.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static NodeId LocalId(ExpandedNodeId id, NamespaceTable namespaces)
        {
            var local = ExpandedNodeId.ToNodeId(id, namespaces);
            if (id.ServerIndex != 0 || local.IsNull || string.IsNullOrEmpty(namespaces.GetString(local.NamespaceIndex)))
            {
                throw SchemaDefinitionSnapshot.Unsupported("A non-null local DataType identifier is required.");
            }
            return local;
        }

        /// <summary>
        /// Preserves the data-type namespace independently of a differently qualified BrowseName.
        /// </summary>
        private static UaTypeDescription Normalize(
            NodeId id, UaTypeDescription description, IServiceMessageContext context)
        {
            return new UaTypeDescription(id, description.BrowseName, description.Definition,
                context.NamespaceUris.GetString(id.NamespaceIndex), description.IsStructureOptionSet);
        }

        /// <summary>
        /// Classifies OptionSets through a bounded single-parent walk, not an unbounded cache expansion.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<bool> IsStructureOptionSetAsync(
            ISession session, NodeId id, McpSchemaOptions options, CancellationToken ct)
        {
            var visited = new HashSet<NodeId>();
            for (int depth = 0; depth <= options.MaxDepth; depth++)
            {
                if (id == DataTypeIds.OptionSet)
                {
                    return true;
                }
                if (TypeInfo.GetBuiltInType(id) != BuiltInType.Null)
                {
                    return false;
                }
                if (!visited.Add(id))
                {
                    throw SchemaDefinitionSnapshot.Unsupported("The OptionSet supertype hierarchy is cyclic.");
                }
                NodeId parent = default;
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    id, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse, includeSubtypes: false,
                    nodeClassMask: (uint)NodeClass.DataType, pageSize: 2, ct: ct).ConfigureAwait(false))
                {
                    if (!parent.IsNull)
                    {
                        throw SchemaDefinitionSnapshot.Unsupported("The DataType has multiple immediate supertypes.");
                    }
                    parent = McpModelAccess.ModelNode(session, reference.NodeId);
                }
                if (parent.IsNull)
                {
                    throw SchemaDefinitionSnapshot.Unsupported("The OptionSet supertype cannot be determined.");
                }
                id = parent;
            }
            throw SchemaDefinitionSnapshot.Limit("The OptionSet hierarchy exceeds the host depth limit.");
        }
    }
}
