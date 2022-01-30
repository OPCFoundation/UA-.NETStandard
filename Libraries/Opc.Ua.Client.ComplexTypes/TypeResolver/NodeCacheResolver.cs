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


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Implements the complex type resolver for a Session using NodeCache.
    /// </summary>
    public class NodeCacheResolver : IComplexTypeResolver
    {
        #region Constructors
        /// <summary>
        /// Initializes the type resolver with a session
        /// to load the custom type information.
        /// </summary>
        public NodeCacheResolver(Session session)
        {
            Initialize(session);
        }

        private void Initialize(Session session)
        {
            m_session = session;
        }
        #endregion Constructors

        #region IComplexTypeResolver
        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_session.NamespaceUris;

        /// <inheritdoc/>
        public IEncodeableFactory Factory => m_session.Factory;

        /// <inheritdoc/>
        public Task<Dictionary<NodeId, DataDictionary>> LoadDataTypeSystem(NodeId dataTypeSystem = null)
        {
            return m_session.LoadDataTypeSystem(dataTypeSystem);
        }

        /// <inheritdoc/>
        public IList<NodeId> BrowseForEncodings(
            ExpandedNodeId nodeId,
            string[] supportedEncodings,
            out ExpandedNodeId binaryEncodingId,
            out ExpandedNodeId xmlEncodingId)
        {
            var references = m_session.NodeCache.FindReferences(
                 nodeId,
                 ReferenceTypeIds.HasEncoding,
                 false,
                 false
                 );
            binaryEncodingId = references.FirstOrDefault(r => r.BrowseName.Name == BrowseNames.DefaultBinary)?.NodeId;
            binaryEncodingId = NormalizeExpandedNodeId(binaryEncodingId);
            xmlEncodingId = references.FirstOrDefault(r => r.BrowseName.Name == BrowseNames.DefaultXml)?.NodeId;
            xmlEncodingId = NormalizeExpandedNodeId(xmlEncodingId);
            return references.Where(r => supportedEncodings.Contains(r.BrowseName.Name))
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris)).ToList();
        }

        /// <inheritdoc/>
        public INode BrowseForSingleProperty(ExpandedNodeId nodeId)
        {
            var references = m_session.NodeCache.FindReferences(
                nodeId,
                ReferenceTypeIds.HasProperty,
                false,
                false
                );
            return references.FirstOrDefault();
        }

        /// <inheritdoc/>
        public bool BrowseTypeIdsForDictionaryComponent(
            NodeId nodeId,
            out ExpandedNodeId typeId,
            out ExpandedNodeId encodingId,
            out DataTypeNode dataTypeNode)
        {
            typeId = ExpandedNodeId.Null;
            encodingId = ExpandedNodeId.Null;
            dataTypeNode = null;

            var references = m_session.NodeCache.FindReferences(
                nodeId,
                ReferenceTypeIds.HasDescription,
                true,
                false
                );

            if (references.Count == 1)
            {
                encodingId = references[0].NodeId;
                references = m_session.NodeCache.FindReferences(
                    encodingId,
                    ReferenceTypeIds.HasEncoding,
                    true,
                    false
                    );
                encodingId = NormalizeExpandedNodeId(encodingId);

                if (references.Count == 1)
                {
                    typeId = references[0].NodeId;
                    dataTypeNode = m_session.NodeCache.Find(typeId) as DataTypeNode;
                    typeId = NormalizeExpandedNodeId(typeId);
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public IList<INode> LoadDataTypes(
            ExpandedNodeId dataType,
            bool nestedSubTypes = false,
            bool addRootNode = false,
            bool filterUATypes = true)
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                dataType
            };

            if (addRootNode)
            {
                var rootNode = m_session.NodeCache.Find(dataType);
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
                    var response = m_session.NodeCache.FindReferences(
                        node,
                        ReferenceTypeIds.HasSubtype,
                        false,
                        false);

                    if (nestedSubTypes)
                    {
                        nextNodesToBrowse.AddRange(response.Select(r => r.NodeId).ToList());
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
        public INode Find(ExpandedNodeId nodeId)
        {
            return m_session.NodeCache.Find(nodeId);
        }

        /// <inheritdoc/>
        public DataValue ReadValue(ExpandedNodeId nodeId)
        {
            return m_session.ReadValue(ExpandedNodeId.ToNodeId(nodeId,NamespaceUris));
        }

        /// <inheritdoc/>
        public NodeId FindSuperType(NodeId typeId)
        {
            return m_session.NodeCache.FindSuperType(typeId);
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
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, m_session.NamespaceUris);
            return NodeId.ToExpandedNodeId(nodeId, m_session.NamespaceUris);
        }
        #endregion Private Methods

        #region Private Fields
        private Session m_session;
        #endregion Private Fields
    }//namespace
}