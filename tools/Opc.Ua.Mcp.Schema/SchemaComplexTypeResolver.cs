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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Schema;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Adapts a validated finite snapshot to the existing AOT-safe complex type loader without whole-server browsing.
    /// </summary>
    internal sealed class SchemaComplexTypeResolver : IComplexTypeResolver
    {
        /// <summary>
        /// Captures the current operation's factory and definitions; no session is retained.
        /// </summary>
        internal SchemaComplexTypeResolver(SchemaDefinitionSnapshot snapshot, IEncodeableFactory factory)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(factory);
            m_snapshot = snapshot;
            NamespaceUris = snapshot.Context.NamespaceUris;
            FactoryBuilder = factory.Builder;
            foreach (UaTypeDescription type in snapshot.Types)
            {
                if (type.Definition is EnumDefinition { IsOptionSet: true } && !type.IsStructureOptionSet)
                {
                    throw SchemaDefinitionSnapshot.Unsupported(
                        "The bounded loader cannot register an integer-backed OptionSet as a signed enumeration.");
                }
                NodeId id = SchemaTypeDiscovery.LocalId(type.TypeId, NamespaceUris);
                if (id.NamespaceIndex != 0)
                {
                    m_nodes.Add(id, snapshot.GetNode(id));
                }
            }
        }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris { get; }

        /// <inheritdoc/>
        public IEncodeableFactoryBuilder FactoryBuilder { get; }

        /// <inheritdoc/>
        public NodeIdDictionary<DataDictionary> DataTypeSystem { get; } = [];

        /// <summary>
        /// Preflights all supported encoding references before any factory mutation can take place.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask PrepareAsync(ISession session, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(session);
            foreach (KeyValuePair<NodeId, DataTypeNode> item in m_nodes)
            {
                ct.ThrowIfCancellationRequested();
                if (!m_snapshot.Registry.TryResolve(item.Key, out UaTypeDescription? description) ||
                    (description.Definition is not StructureDefinition && !description.IsStructureOptionSet))
                {
                    continue;
                }
                var encodings = new List<NodeId>();
                var names = new HashSet<QualifiedName>();
                ExpandedNodeId binary = default;
                ExpandedNodeId xml = default;
                int count = 0;
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    item.Key, ReferenceTypeIds.HasEncoding, includeSubtypes: false,
                    nodeClassMask: (uint)NodeClass.Object, pageSize: 4, ct: ct).ConfigureAwait(false))
                {
                    if (++count > 4)
                    {
                        throw SchemaDefinitionSnapshot.Limit("A DataType exposes more than four encoding references.");
                    }
                    if (reference.BrowseName.NamespaceIndex != 0 ||
                        reference.BrowseName.Name is not (BrowseNames.DefaultBinary or BrowseNames.DefaultXml))
                    {
                        continue;
                    }
                    if (!names.Add(reference.BrowseName))
                    {
                        throw SchemaDefinitionSnapshot.Unsupported("A DataType exposes duplicate default encodings.");
                    }
                    NodeId encoding = McpModelAccess.ModelNode(session, reference.NodeId);
                    encodings.Add(encoding);
                    if (reference.BrowseName.Name == BrowseNames.DefaultBinary)
                    {
                        binary = NodeId.ToExpandedNodeId(encoding, NamespaceUris);
                    }
                    else if (reference.BrowseName.Name == BrowseNames.DefaultXml)
                    {
                        xml = NodeId.ToExpandedNodeId(encoding, NamespaceUris);
                    }
                }
                if (binary.IsNull && !item.Value.IsAbstract)
                {
                    throw SchemaDefinitionSnapshot.Unsupported("A concrete structure has no Default Binary encoding.");
                }
                m_encodings.Add(item.Key, ([.. encodings], binary, xml));
            }
        }

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
            NodeId dataTypeSystem = default, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw SchemaDefinitionSnapshot.Unsupported("Unbounded legacy dictionary loading is disabled.");
        }

        /// <inheritdoc/>
        public Task<(ExpandedNodeId typeId, ExpandedNodeId encodingId, DataTypeNode? dataTypeNode)>
            BrowseTypeIdsForDictionaryComponentAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw SchemaDefinitionSnapshot.Unsupported(
                "Legacy dictionary components are outside the bounded snapshot.");
        }

        /// <inheritdoc/>
        public Task<ArrayOf<NodeId>> BrowseForEncodingsAsync(
            ArrayOf<ExpandedNodeId> nodeIds, string[] supportedEncodings, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var result = new List<NodeId>();
            foreach (ExpandedNodeId id in nodeIds)
            {
                if (m_encodings.TryGetValue(SchemaTypeDiscovery.LocalId(id, NamespaceUris),
                    out (ArrayOf<NodeId> Ids, ExpandedNodeId Binary, ExpandedNodeId Xml) encodings))
                {
                    result.AddRange(encodings.Ids);
                }
            }
            return Task.FromResult(result.ToArrayOf());
        }

        /// <inheritdoc/>
        public Task<(ArrayOf<NodeId> encodings, ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId)>
            BrowseForEncodingsAsync(
                ExpandedNodeId nodeId, string[] supportedEncodings, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(m_encodings.TryGetValue(
                SchemaTypeDiscovery.LocalId(nodeId, NamespaceUris),
                out (ArrayOf<NodeId> Ids, ExpandedNodeId Binary, ExpandedNodeId Xml) encodings)
                ? encodings
                : (ArrayOf<NodeId>.Empty, ExpandedNodeId.Null, ExpandedNodeId.Null));
        }

        /// <inheritdoc/>
        public Task<ArrayOf<INode>> LoadDataTypesAsync(
            ExpandedNodeId dataType, bool nestedSubTypes = false, bool addRootNode = false,
            bool filterUATypes = true, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            NodeId id = SchemaTypeDiscovery.LocalId(dataType, NamespaceUris);
            if (id == DataTypeIds.BaseDataType)
            {
                return Task.FromResult<ArrayOf<INode>>([.. m_nodes.Values]);
            }
            if (id == DataTypeIds.Enumeration || id == DataTypeIds.Structure)
            {
                return Task.FromResult<ArrayOf<INode>>([.. m_nodes.Values.Where(node =>
                    m_snapshot.Registry.TryResolve(node.NodeId, out UaTypeDescription? type) &&
                    ((id == DataTypeIds.Enumeration &&
                        type.Definition is EnumDefinition &&
                        !type.IsStructureOptionSet) ||
                        (id == DataTypeIds.Structure &&
                            (type.Definition is StructureDefinition || type.IsStructureOptionSet))))]);
            }
            return Task.FromResult<ArrayOf<INode>>(m_nodes.TryGetValue(id, out DataTypeNode? root) ? [root] : []);
        }

        /// <inheritdoc/>
        public Task<INode?> FindAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            NodeId id = SchemaTypeDiscovery.LocalId(nodeId, NamespaceUris);
            return Task.FromResult<INode?>(m_nodes.TryGetValue(id, out DataTypeNode? node) ? node : null);
        }

        /// <inheritdoc/>
        public Task<Variant> GetEnumTypeArrayAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw SchemaDefinitionSnapshot.Unsupported("Only validated EnumDefinition attributes can be loaded.");
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (typeId == DataTypeIds.OptionSet)
            {
                return Task.FromResult(DataTypeIds.Structure);
            }
            if (m_snapshot.Registry.TryResolve(typeId, out UaTypeDescription? type))
            {
                return Task.FromResult(type.Definition is StructureDefinition structure
                    ? structure.BaseDataType
                    : type.IsStructureOptionSet ? DataTypeIds.OptionSet : DataTypeIds.Enumeration);
            }
            return Task.FromResult<NodeId>(default);
        }

        /// <summary>The finite, validated description registry.</summary>
        private readonly SchemaDefinitionSnapshot m_snapshot;

        /// <summary>Remote DataType nodes captured before registration.</summary>
        private readonly Dictionary<NodeId, DataTypeNode> m_nodes = [];

        /// <summary>Preflighted default encodings; the loader performs no further remote reads.</summary>
        private readonly Dictionary<NodeId,
            (ArrayOf<NodeId> Ids, ExpandedNodeId Binary, ExpandedNodeId Xml)> m_encodings = [];
    }
}
