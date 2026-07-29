/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Implements the <see cref="IComplexTypeResolver"/> over a running
    /// server's in-memory address space. It surfaces the DataType nodes and
    /// their <c>DataTypeDefinition</c> attributes so the shared
    /// <see cref="ComplexTypeSystem"/> can build dynamic stand-in encodeables
    /// for DataTypes that were loaded from a NodeSet at runtime (and are not
    /// backed by a compiled .NET type). The OPC Binary / XML dictionary type
    /// system is intentionally not supported; DataType information is taken
    /// exclusively from the DataTypeDefinition attribute.
    /// </summary>
    internal sealed class AddressSpaceComplexTypeResolver : IComplexTypeResolver
    {
        /// <summary>
        /// Initializes the resolver with the server whose address space is
        /// inspected.
        /// </summary>
        /// <param name="server">The server to resolve types from.</param>
        /// <param name="additionalNodeManager">
        /// A prepared NodeManager to inspect before it is published.
        /// </param>
        public AddressSpaceComplexTypeResolver(
            IServerInternal server,
            IAsyncNodeManager? additionalNodeManager = null)
        {
            m_server = server;
            m_systemContext = server.DefaultSystemContext;
            m_additionalNodeManager = additionalNodeManager;
            FactoryBuilder = server.Factory.Builder;
        }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_server.NamespaceUris;

        /// <inheritdoc/>
        public IEncodeableFactoryBuilder FactoryBuilder { get; }

        /// <inheritdoc/>
        public NodeIdDictionary<DataDictionary> DataTypeSystem { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
            NodeId dataTypeSystem = default,
            CancellationToken ct = default)
        {
            // The server resolver builds types from the DataTypeDefinition
            // attribute only; the OPC Binary/XML dictionary type system is not
            // exposed.
            return Task.FromResult<IReadOnlyDictionary<NodeId, DataDictionary>>(
                new Dictionary<NodeId, DataDictionary>());
        }

        /// <inheritdoc/>
        public Task<(
            ExpandedNodeId typeId,
            ExpandedNodeId encodingId,
            DataTypeNode? dataTypeNode
        )> BrowseTypeIdsForDictionaryComponentAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            // Only used by the dictionary type system which is not supported.
            return Task.FromResult<(ExpandedNodeId, ExpandedNodeId, DataTypeNode?)>(
                (default, default, null));
        }

        /// <inheritdoc/>
        public async Task<ArrayOf<NodeId>> BrowseForEncodingsAsync(
            ArrayOf<ExpandedNodeId> nodeIds,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            var encodings = new List<NodeId>();
            for (int i = 0; i < nodeIds.Count; i++)
            {
                (ArrayOf<NodeId> nodeEncodings, _, _) = await BrowseForEncodingsAsync(
                    nodeIds[i],
                    supportedEncodings,
                    ct)
                    .ConfigureAwait(false);
                for (int j = 0; j < nodeEncodings.Count; j++)
                {
                    encodings.Add(nodeEncodings[j]);
                }
            }
            return [.. encodings];
        }

        /// <inheritdoc/>
        public async Task<(
            ArrayOf<NodeId> encodings,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId
        )> BrowseForEncodingsAsync(
            ExpandedNodeId nodeId,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            var encodings = new List<NodeId>();
            ExpandedNodeId binaryEncodingId = ExpandedNodeId.Null;
            ExpandedNodeId xmlEncodingId = ExpandedNodeId.Null;

            NodeState? dataType = await FindNodeStateAsync(nodeId, ct).ConfigureAwait(false);
            if (dataType != null)
            {
                var references = new List<IReference>();
                dataType.GetReferences(
                    m_systemContext,
                    references,
                    ReferenceTypeIds.HasEncoding,
                    false);

                foreach (IReference reference in references)
                {
                    var encodingNodeId = ExpandedNodeId.ToNodeId(
                        reference.TargetId,
                        NamespaceUris);
                    if (encodingNodeId.IsNull)
                    {
                        continue;
                    }

                    NodeState? encodingNode = await FindNodeStateAsync(
                        encodingNodeId,
                        ct)
                        .ConfigureAwait(false);
                    string? browseName = encodingNode?.BrowseName.Name;
                    if (browseName == null)
                    {
                        continue;
                    }

                    var expandedEncodingId = NodeId.ToExpandedNodeId(
                        encodingNodeId,
                        NamespaceUris);
                    if (browseName == BrowseNames.DefaultBinary)
                    {
                        binaryEncodingId = expandedEncodingId;
                    }
                    else if (browseName == BrowseNames.DefaultXml)
                    {
                        xmlEncodingId = expandedEncodingId;
                    }
                    if (Array.IndexOf(supportedEncodings, browseName) >= 0)
                    {
                        encodings.Add(encodingNodeId);
                    }
                }
            }

            return ([.. encodings], binaryEncodingId, xmlEncodingId);
        }

        /// <inheritdoc/>
        public async Task<ArrayOf<INode>> LoadDataTypesAsync(
            ExpandedNodeId dataType,
            bool nestedSubTypes = false,
            bool addRootNode = false,
            bool filterUATypes = true,
            CancellationToken ct = default)
        {
            var result = new List<INode>();
            if (addRootNode)
            {
                INode? rootNode = await FindAsync(dataType, ct).ConfigureAwait(false);
                if (rootNode is not DataTypeNode dataTypeNode)
                {
                    // FindAsync resolves a node only when it is a DataType, so a
                    // missing node and a non-DataType node both surface here.
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown,
                        $"Root node '{dataType}' could not be resolved to a DataType node.");
                }
                result.Add(dataTypeNode);
            }

            var nodesToBrowse = new List<ExpandedNodeId> { dataType };
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new List<ExpandedNodeId>();
                foreach (ExpandedNodeId nodeToBrowse in nodesToBrowse)
                {
                    ArrayOf<NodeId> subTypes = m_server.TypeTree.FindSubTypes(nodeToBrowse);
                    for (int i = 0; i < subTypes.Count; i++)
                    {
                        NodeId subType = subTypes[i];
                        var subTypeId = NodeId.ToExpandedNodeId(subType, NamespaceUris);
                        if (nestedSubTypes)
                        {
                            nextNodesToBrowse.Add(subTypeId);
                        }
                        if (filterUATypes && subType.NamespaceIndex == 0)
                        {
                            // filter out the default namespace
                            continue;
                        }
                        INode? node = await FindAsync(subTypeId, ct).ConfigureAwait(false);
                        if (node != null)
                        {
                            result.Add(node);
                        }
                    }
                }
                nodesToBrowse = nextNodesToBrowse;
            }

            return [.. result];
        }

        /// <inheritdoc/>
        public async Task<INode?> FindAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            NodeState? state = await FindNodeStateAsync(nodeId, ct).ConfigureAwait(false);
            if (state is DataTypeState dataType)
            {
                return new DataTypeNode
                {
                    NodeId = dataType.NodeId,
                    BrowseName = dataType.BrowseName,
                    DisplayName = dataType.DisplayName,
                    Description = dataType.Description,
                    IsAbstract = dataType.IsAbstract,
                    DataTypeDefinition = dataType.DataTypeDefinition
                };
            }
            return null;
        }

        /// <inheritdoc/>
        public async Task<Variant> GetEnumTypeArrayAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            NodeState? dataType = await FindNodeStateAsync(nodeId, ct).ConfigureAwait(false);
            if (dataType == null)
            {
                return default;
            }

            var references = new List<IReference>();
            dataType.GetReferences(m_systemContext, references, ReferenceTypeIds.HasProperty, false);

            // A DataType can expose several HasProperty children; collect the
            // variable nodes and then select by BrowseName in an explicit
            // priority order (richer EnumValues, then EnumStrings, then
            // OptionSetValues) instead of returning the first HasProperty
            // target, which could be an unrelated property.
            var properties = new List<BaseVariableState>();
            foreach (IReference reference in references)
            {
                var propertyId = ExpandedNodeId.ToNodeId(reference.TargetId, NamespaceUris);
                if (propertyId.IsNull)
                {
                    continue;
                }
                NodeState? property = await FindNodeStateAsync(propertyId, ct)
                    .ConfigureAwait(false);
                if (property is BaseVariableState variable && !variable.WrappedValue.IsNull)
                {
                    properties.Add(variable);
                }
            }

            foreach (string wellKnownName in s_enumDefinitionPropertyNames)
            {
                foreach (BaseVariableState variable in properties)
                {
                    if (variable.BrowseName.Name == wellKnownName)
                    {
                        return variable.WrappedValue;
                    }
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
        {
            // The type tree is a synchronous in-memory lookup.
            return Task.FromResult(m_server.TypeTree.FindSuperType(typeId));
        }

        /// <summary>
        /// Resolves the node state for an expanded node id in the address space.
        /// </summary>
        private async Task<NodeState?> FindNodeStateAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct)
        {
            var localId = ExpandedNodeId.ToNodeId(nodeId, NamespaceUris);
            if (localId.IsNull)
            {
                return null;
            }
            return await FindNodeStateAsync(localId, ct).ConfigureAwait(false);
        }

        private async ValueTask<NodeState?> FindNodeStateAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            if (m_additionalNodeManager is not null &&
                await m_additionalNodeManager
                    .GetManagerHandleAsync(nodeId, ct)
                    .ConfigureAwait(false) is NodeHandle handle)
            {
                return handle.Node;
            }

            return await m_server.NodeManager
                .FindNodeInAddressSpaceAsync(nodeId, ct)
                .ConfigureAwait(false);
        }

        private const string EnumValuesBrowseName = "EnumValues";
        private const string EnumStringsBrowseName = "EnumStrings";
        private const string OptionSetValuesBrowseName = "OptionSetValues";

        private static readonly string[] s_enumDefinitionPropertyNames =
        [
            EnumValuesBrowseName,
            EnumStringsBrowseName,
            OptionSetValuesBrowseName
        ];

        private readonly IAsyncNodeManager? m_additionalNodeManager;

        private readonly IServerInternal m_server;
        private readonly ISystemContext m_systemContext;
    }
}
