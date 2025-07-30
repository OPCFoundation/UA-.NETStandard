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
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
        public NodeCacheResolver(ISession session)
        {
            Initialize(session);
        }

        private void Initialize(ISession session)
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
        public NodeIdDictionary<DataDictionary> DataTypeSystem => m_dataTypeSystem;

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
            NodeId dataTypeSystem = null,
            CancellationToken ct = default)
        {
            if (dataTypeSystem == null)
            {
                dataTypeSystem = ObjectIds.OPCBinarySchema_TypeSystem;
            }
            else
            if (!Utils.IsEqual(dataTypeSystem, ObjectIds.OPCBinarySchema_TypeSystem) &&
                !Utils.IsEqual(dataTypeSystem, ObjectIds.XmlSchema_TypeSystem))
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, $"{nameof(dataTypeSystem)} does not refer to a valid data dictionary.");
            }

            // find the dictionary for the description.
            IList<INode> references = await m_session.NodeCache.FindReferencesAsync(dataTypeSystem, ReferenceTypeIds.HasComponent, false, false, ct).ConfigureAwait(false);

            if (references.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "Type system does not contain a valid data dictionary.");
            }

            // batch read all encodings and namespaces
            var referenceNodeIds = references.Select(r => r.NodeId).ToList();

            // find namespace properties
            var namespaceReferences = await m_session.NodeCache.FindReferencesAsync(referenceNodeIds, new NodeIdCollection { ReferenceTypeIds.HasProperty }, false, false, ct).ConfigureAwait(false);
            var namespaceNodes = namespaceReferences.Where(n => n.BrowseName == BrowseNames.NamespaceUri).ToList();
            var namespaceNodeIds = namespaceNodes.Select(n => ExpandedNodeId.ToNodeId(n.NodeId, m_session.NamespaceUris)).ToList();

            // read all schema definitions
            var referenceExpandedNodeIds = references
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, this.NamespaceUris))
                .Where(n => n.NamespaceIndex != 0).ToList();
            IDictionary<NodeId, byte[]> schemas = await DataDictionary.ReadDictionariesAsync(m_session, referenceExpandedNodeIds, ct).ConfigureAwait(false);

            // read namespace property values
            var namespaces = new Dictionary<NodeId, string>();
            m_session.ReadValues(namespaceNodeIds, Enumerable.Repeat(typeof(string), namespaceNodeIds.Count).ToList(), out var nameSpaceValues, out var errors);

            // build the namespace dictionary
            for (int ii = 0; ii < nameSpaceValues.Count; ii++)
            {
                if (StatusCode.IsNotBad(errors[ii].StatusCode))
                {
                    // servers may optimize space by not returning a dictionary
                    if (nameSpaceValues[ii] != null)
                    {
                        namespaces[((NodeId)referenceNodeIds[ii])] = (string)nameSpaceValues[ii];
                    }
                }
                else
                {
                    Utils.LogWarning("Failed to load namespace {0}: {1}", namespaceNodeIds[ii], errors[ii]);
                }
            }

            // build the namespace/schema import dictionary
            var imports = new Dictionary<string, byte[]>();
            foreach (var r in references)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris);
                if (schemas.TryGetValue(nodeId, out var schema) && namespaces.TryGetValue(nodeId, out var ns))
                {
                    imports[ns] = schema;
                }
            }

            // read all type dictionaries in the type system
            foreach (var r in references)
            {
                DataDictionary dictionaryToLoad = null;
                NodeId dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                if (dictionaryId.NamespaceIndex != 0 &&
                    !m_dataTypeSystem.TryGetValue(dictionaryId, out dictionaryToLoad))
                {
                    try
                    {
                        dictionaryToLoad = new DataDictionary(m_session);
                        if (schemas.TryGetValue(dictionaryId, out var schema))
                        {
                            dictionaryToLoad.Load(dictionaryId, dictionaryId.ToString(), schema, imports);
                        }
                        else
                        {
                            dictionaryToLoad.Load(dictionaryId, dictionaryId.ToString());
                        }
                        m_dataTypeSystem[dictionaryId] = dictionaryToLoad;
                    }
                    catch (Exception ex)
                    {
                        Utils.LogError("Dictionary load error for Dictionary {0} : {1}", r.NodeId, ex.Message);
                    }
                }
            }
            return m_dataTypeSystem;
        }

        /// <inheritdoc/>
        public async Task<IList<NodeId>> BrowseForEncodingsAsync(
            IList<ExpandedNodeId> nodeIds,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            // cache type encodings
            IList<INode> encodings = await m_session.NodeCache.FindReferencesAsync(
                 nodeIds,
                 new NodeIdCollection { ReferenceTypeIds.HasEncoding },
                 false,
                 false,
                 ct).ConfigureAwait(false);

            // cache dictionary descriptions
            nodeIds = encodings.Select(r => r.NodeId).ToList();
            IList<INode> descriptions = await m_session.NodeCache.FindReferencesAsync(
                nodeIds,
                new NodeIdCollection { ReferenceTypeIds.HasDescription },
                false,
                false,
                ct).ConfigureAwait(false);

            return encodings.Where(r => supportedEncodings.Contains(r.BrowseName.Name))
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris)).ToList();
        }

        /// <inheritdoc/>
        public async Task<(IList<NodeId> encodings, ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId)> BrowseForEncodingsAsync(
            ExpandedNodeId nodeId,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            IList<INode> references = await m_session.NodeCache.FindReferencesAsync(
                nodeId,
                ReferenceTypeIds.HasEncoding,
                false,
                false,
                ct).ConfigureAwait(false);

            ExpandedNodeId binaryEncodingId = references.FirstOrDefault(r => r.BrowseName.Name == BrowseNames.DefaultBinary)?.NodeId;
            binaryEncodingId = NormalizeExpandedNodeId(binaryEncodingId);
            ExpandedNodeId xmlEncodingId = references.FirstOrDefault(r => r.BrowseName.Name == BrowseNames.DefaultXml)?.NodeId;
            xmlEncodingId = NormalizeExpandedNodeId(xmlEncodingId);
            return (references
                .Where(r => supportedEncodings.Contains(r.BrowseName.Name))
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris))
                .ToList(), binaryEncodingId, xmlEncodingId);
        }

        /// <inheritdoc/>
        public async Task<(ExpandedNodeId typeId, ExpandedNodeId encodingId, DataTypeNode dataTypeNode)> BrowseTypeIdsForDictionaryComponentAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            ExpandedNodeId encodingId;
            DataTypeNode dataTypeNode;

            IList<INode> references = await m_session.NodeCache.FindReferencesAsync(
                nodeId,
                ReferenceTypeIds.HasDescription,
                true,
                false,
                ct).ConfigureAwait(false);

            if (references.Count == 1)
            {
                encodingId = references[0].NodeId;
                references = await m_session.NodeCache.FindReferencesAsync(
                    encodingId,
                    ReferenceTypeIds.HasEncoding,
                    true,
                    false,
                    ct).ConfigureAwait(false);
                encodingId = NormalizeExpandedNodeId(encodingId);

                if (references.Count == 1)
                {
                    ExpandedNodeId typeId = references[0].NodeId;
                    dataTypeNode = m_session.NodeCache.Find(typeId) as DataTypeNode;
                    typeId = NormalizeExpandedNodeId(typeId);
                    return (typeId, encodingId, dataTypeNode);
                }
            }
            return (null, null, null);
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

#if DEBUG
            var stopwatch = new Stopwatch();
            stopwatch.Start();
#endif

            if (addRootNode)
            {
                INode rootNode = await m_session.NodeCache.FindAsync(dataType, ct).ConfigureAwait(false);
                if (!(rootNode is DataTypeNode))
                {
                    throw new ServiceResultException("Root Node is not a DataType node.");
                }
                result.Add(rootNode);
            }

            while (nodesToBrowse.Count > 0)
            {
                IList<INode> response = await m_session.NodeCache.FindReferencesAsync(
                    nodesToBrowse,
                    new NodeIdCollection { ReferenceTypeIds.HasSubtype },
                    false,
                    false,
                    ct).ConfigureAwait(false);

                var nextNodesToBrowse = new ExpandedNodeIdCollection();
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
                nodesToBrowse = nextNodesToBrowse;
            }

#if DEBUG
            stopwatch.Stop();
            Utils.LogInfo("LoadDataTypes returns {0} nodes in {1}ms", result.Count, stopwatch.ElapsedMilliseconds);
#endif

            return result;
        }

        /// <inheritdoc/>
        public Task<INode> FindAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            return m_session.NodeCache.FindAsync(nodeId, ct);
        }

        /// <inheritdoc/>
        public async Task<object> GetEnumTypeArrayAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            // find the property reference for the enum type
            IList<INode> references = await m_session.NodeCache.FindReferencesAsync(
                nodeId,
                ReferenceTypeIds.HasProperty,
                false,
                false,
                ct).ConfigureAwait(false);
            INode property = references.FirstOrDefault();
            if (property != null)
            {
                // read the enum type array
                DataValue value = await m_session.ReadValueAsync(ExpandedNodeId.ToNodeId(property.NodeId, NamespaceUris), ct).ConfigureAwait(false);
                return value?.Value;
            }
            return null;
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
        {
            return m_session.NodeCache.FindSuperTypeAsync(typeId, ct);
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
        private readonly NodeIdDictionary<DataDictionary> m_dataTypeSystem = new NodeIdDictionary<DataDictionary>();
        private ISession m_session;
        #endregion Private Fields
    }//namespace
}
