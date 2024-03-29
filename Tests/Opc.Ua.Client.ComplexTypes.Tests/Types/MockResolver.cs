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
        #region Constructors
        /// <summary>
        /// The mock resolver emulates data type definitions
        /// which are stored in the server address space.
        /// </summary>
        public MockResolver()
        {
            Initialize();
        }

        private void Initialize()
        {
            m_dataTypeDictionary = new Dictionary<NodeId, DataDictionary>();
            m_dataTypeNodes = new Dictionary<NodeId, INode>();
            m_factory = new EncodeableFactory(EncodeableFactory.GlobalFactory);
            m_namespaceUris = new NamespaceTable();

            m_namespaceUris.Append("urn:This:is:my:test:encoder");
            m_namespaceUris.Append("urn:This:is:another:namespace");
            m_namespaceUris.Append(Namespaces.OpcUaEncoderTests);
            m_namespaceUris.Append(Namespaces.MockResolverUrl);

        }
        #endregion Constructors

        public Dictionary<NodeId, INode> DataTypeNodes => m_dataTypeNodes;

        #region IComplexTypeResolver
        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_namespaceUris;

        /// <inheritdoc/>
        public IEncodeableFactory Factory => m_factory;

        /// <inheritdoc/>
        public Task<Dictionary<NodeId, DataDictionary>> LoadDataTypeSystem(NodeId dataTypeSystem = null, CancellationToken ct = default)
        {
            return Task.FromResult(m_dataTypeDictionary);
        }

        /// <inheritdoc/>
        public Task<IList<NodeId>> BrowseForEncodingsAsync(IList<ExpandedNodeId> nodeIds, string[] supportedEncodings, CancellationToken ct = default)
        {
            return Task.FromResult((IList<NodeId>)new List<NodeId>());
        }

        /// <inheritdoc/>
        public Task<(IList<NodeId> encodings, ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId)> BrowseForEncodingsAsync(
            ExpandedNodeId nodeId,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            var binaryEncodingId = ExpandedNodeId.Null;
            var xmlEncodingId = ExpandedNodeId.Null;
            IList<NodeId> encodings = null;

            var node = m_dataTypeNodes[ExpandedNodeId.ToNodeId(nodeId, NamespaceUris)];
            if (node is DataTypeNode dataTypeNode)
            {
                var result = new List<NodeId>();
                var references = dataTypeNode.References.Where(r => r.ReferenceTypeId.Equals(ReferenceTypeIds.HasEncoding));
                foreach (var reference in references)
                {
                    var encodingNode = m_dataTypeNodes[ExpandedNodeId.ToNodeId(reference.TargetId, NamespaceUris)];
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
        public Task<(ExpandedNodeId typeId, ExpandedNodeId encodingId, DataTypeNode dataTypeNode)> BrowseTypeIdsForDictionaryComponentAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            return Task.FromResult<(ExpandedNodeId typeId, ExpandedNodeId encodingId, DataTypeNode dataTypeNode)>((null, null, null));
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
            var nodesToBrowse = new ExpandedNodeIdCollection {
                dataType
            };

            if (addRootNode)
            {
                var rootNode = await FindAsync(dataType, ct).ConfigureAwait(false);
                if (!(rootNode is DataTypeNode))
                {
                    throw new ServiceResultException("Root Node is not a DataType node.");
                }
                result.Add(rootNode);
            }

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {

                    var response = m_dataTypeNodes.Values.Where(n => {
                        if (n.NodeClass == NodeClass.DataType)
                        {
                            if (((DataTypeNode)n).DataTypeDefinition.Body is StructureDefinition structureDefinition)
                            {
                                if (Utils.IsEqual(structureDefinition.BaseDataType, node))
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    }).Cast<DataTypeNode>();
                    if (nestedSubTypes)
                    {
                        nextNodesToBrowse.AddRange(response.Select(r => NodeId.ToExpandedNodeId(r.NodeId, NamespaceUris)).ToList());
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
            return Task.FromResult(m_dataTypeNodes[ExpandedNodeId.ToNodeId(nodeId, NamespaceUris)]);
        }

        /// <inheritdoc/>
        public Task<object> GetEnumTypeArrayAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            return Task.FromResult<object>(null);
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
        {
            var node = m_dataTypeNodes[typeId];
            if (node is DataTypeNode dataTypeNode)
            {
                if (dataTypeNode.DataTypeDefinition.Body is EnumDefinition enumDefinition)
                {
                    return Task.FromResult(DataTypeIds.Enumeration);
                }
                else if (dataTypeNode.DataTypeDefinition.Body is StructureDefinition structureDefinition)
                {
                    return Task.FromResult(structureDefinition.BaseDataType);
                }
            }
            return Task.FromResult(DataTypeIds.BaseDataType);
        }
        #endregion IComplexTypeResolver

        #region Private Methods
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
        #endregion Private Methods

        #region Private Fields
        private Dictionary<NodeId, DataDictionary> m_dataTypeDictionary;
        private Dictionary<NodeId, INode> m_dataTypeNodes;
        private EncodeableFactory m_factory;
        private NamespaceTable m_namespaceUris;
        #endregion Private Fields
    }//namespace
}
