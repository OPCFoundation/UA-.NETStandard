/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Implements the mock complex type resolver for testing.
    /// </summary>
    public class MockResolver : IComplexTypeResolver
    {
        /// <summary>
        /// The mock resolver emulates data type definitions
        /// which are stored in the server address space.
        /// </summary>
        public MockResolver(ITelemetryContext telemetry)
        {
            DataTypeSystem = [];
            DataTypeNodes = [];
            m_factory = new EncodeableFactory(telemetry);
            NamespaceUris = new NamespaceTable();

            NamespaceUris.Append("urn:This:is:my:test:encoder");
            NamespaceUris.Append("urn:This:is:another:namespace");
            NamespaceUris.Append(Namespaces.OpcUaEncoderTests);
            NamespaceUris.Append(Namespaces.MockResolverUrl);
        }

        public NodeIdDictionary<INode> DataTypeNodes { get; }

        /// <inheritdoc/>
        public NodeIdDictionary<DataDictionary> DataTypeSystem { get; }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris { get; }

        /// <inheritdoc/>
        public IEncodeableFactory Factory => m_factory;

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
            NodeId dataTypeSystem = null,
            CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyDictionary<NodeId, DataDictionary>>(DataTypeSystem);
        }

        /// <inheritdoc/>
        public Task<IList<NodeId>> BrowseForEncodingsAsync(
            IList<ExpandedNodeId> nodeIds,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            return Task.FromResult((IList<NodeId>)[]);
        }

        /// <inheritdoc/>
        public Task<(
            IList<NodeId> encodings,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId
        )> BrowseForEncodingsAsync(
            ExpandedNodeId nodeId,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            ExpandedNodeId binaryEncodingId = ExpandedNodeId.Null;
            ExpandedNodeId xmlEncodingId = ExpandedNodeId.Null;
            IList<NodeId> encodings = null;

            INode node = DataTypeNodes[ExpandedNodeId.ToNodeId(nodeId, NamespaceUris)];
            if (node is DataTypeNode dataTypeNode)
            {
                var result = new List<NodeId>();
                foreach (
                    ReferenceNode reference in dataTypeNode.References.Where(r =>
                        r.ReferenceTypeId.Equals(ReferenceTypeIds.HasEncoding)))
                {
                    INode encodingNode = DataTypeNodes[
                        ExpandedNodeId.ToNodeId(reference.TargetId, NamespaceUris)];
                    if (encodingNode == null)
                    {
                        continue;
                    }
                    if (encodingNode.BrowseName.Name == BrowseNames.DefaultBinary)
                    {
                        binaryEncodingId = NormalizeExpandedNodeId(reference.TargetId);
                    }
                    else if (encodingNode.BrowseName.Name == BrowseNames.DefaultXml)
                    {
                        xmlEncodingId = NormalizeExpandedNodeId(reference.TargetId);
                    }
                    else if (encodingNode.BrowseName.Name != BrowseNames.DefaultJson)
                    {
                        continue;
                    }
                    result.Add(ExpandedNodeId.ToNodeId(reference.TargetId, NamespaceUris));
                }
                encodings = result;
            }
            return Task.FromResult((encodings, binaryEncodingId, xmlEncodingId));
        }

        /// <inheritdoc/>
        public Task<(
            ExpandedNodeId typeId,
            ExpandedNodeId encodingId,
            DataTypeNode dataTypeNode
        )> BrowseTypeIdsForDictionaryComponentAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            return Task.FromResult<(ExpandedNodeId typeId, ExpandedNodeId encodingId, DataTypeNode dataTypeNode)>(
                (null, null, null));
        }

        /// <inheritdoc/>
        public async Task<IList<INode>> LoadDataTypesAsync(
            ExpandedNodeId dataType,
            bool nestedSubTypes = false,
            bool addRootNode = false,
            bool filterUATypes = true,
            CancellationToken ct = default)
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection { dataType };

            if (addRootNode)
            {
                INode rootNode = await FindAsync(dataType, ct).ConfigureAwait(false);
                if (rootNode is not DataTypeNode)
                {
                    throw new ServiceResultException("Root Node is not a DataType node.");
                }
                result.Add(rootNode);
            }

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (ExpandedNodeId node in nodesToBrowse)
                {
                    IEnumerable<DataTypeNode> response = DataTypeNodes
                        .Values.Where(n =>
                            n.NodeClass == NodeClass.DataType &&
                            ((DataTypeNode)n).DataTypeDefinition
                                .Body is StructureDefinition structureDefinition &&
                            Utils.IsEqual(structureDefinition.BaseDataType, node))
                        .Cast<DataTypeNode>();
                    if (nestedSubTypes)
                    {
                        nextNodesToBrowse.AddRange(
                            response.Select(r => NodeId.ToExpandedNodeId(r.NodeId, NamespaceUris)));
                    }
                    if (filterUATypes)
                    {
                        // filter out default namespace
                        result.AddRange(response.Where(rd => rd.NodeId.NamespaceIndex != 0));
                    }
                    else
                    {
                        result.AddRange(response);
                    }
                }
                nodesToBrowse = nextNodesToBrowse;
            }
            return result;
        }

        /// <inheritdoc/>
        public Task<INode> FindAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            return Task.FromResult(DataTypeNodes[ExpandedNodeId.ToNodeId(nodeId, NamespaceUris)]);
        }

        /// <inheritdoc/>
        public Task<object> GetEnumTypeArrayAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            return Task.FromResult<object>(null);
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
        {
            INode node = DataTypeNodes[typeId];
            if (node is DataTypeNode dataTypeNode)
            {
                if (dataTypeNode.DataTypeDefinition.Body is EnumDefinition)
                {
                    return Task.FromResult(DataTypeIds.Enumeration);
                }
                else if (dataTypeNode.DataTypeDefinition
                    .Body is StructureDefinition structureDefinition)
                {
                    return Task.FromResult(structureDefinition.BaseDataType);
                }
            }
            return Task.FromResult(DataTypeIds.BaseDataType);
        }

        /// <summary>
        /// Helper to ensure the expanded nodeId contains a valid namespaceUri.
        /// </summary>
        /// <param name="expandedNodeId">The expanded nodeId.</param>
        /// <returns>The normalized expanded nodeId.</returns>
        private ExpandedNodeId NormalizeExpandedNodeId(ExpandedNodeId expandedNodeId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, NamespaceUris);
            return NodeId.ToExpandedNodeId(nodeId, NamespaceUris);
        }

        private readonly EncodeableFactory m_factory;
    }
}
