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

#if NETCOREAPP3_0_OR_GREATER
#define USE_LRU_CACHE
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Implements the complex type resolver for a Session using a cache.
    /// </summary>
    public class NodeCacheResolver : IComplexTypeResolver
    {
        #region Constructors
#if USE_LRU_CACHE
        /// <summary>
        /// Initializes the type resolver with a session to load the custom type information.
        /// </summary>
        public NodeCacheResolver(ISession session)
            : this(new LruNodeCache(session))
        {
        }

        /// <summary>
        /// Initializes the type resolver with a session and lru cache to load the
        /// custom type information with the specified expiry.
        /// </summary>
        public NodeCacheResolver(ISession session, TimeSpan cacheExpiry)
            : this(new LruNodeCache(session, cacheExpiry))
        {
        }

        /// <summary>
        /// Initializes the type resolver with a lru node cache.
        /// </summary>
        public NodeCacheResolver(ISession session, TimeSpan cacheExpiry, int capacity)
            : this(new LruNodeCache(session, cacheExpiry, capacity))
        {
        }

        /// <summary>
        /// Initializes the type resolver with a session and lru cache to load the
        /// custom type information with the specified expiry and cache size.
        /// </summary>
        public NodeCacheResolver(ILruNodeCache lruNodeCache)
        {
            m_session = lruNodeCache.Session;
            m_lruNodeCache = lruNodeCache;
        }
#else
        /// <summary>
        /// Initializes the type resolver with a session to load the custom type information.
        /// </summary>
        public NodeCacheResolver(ISession session)
        {
            m_session = session;
        }
#endif

        #endregion Constructors

        #region IComplexTypeResolver
        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_session.NamespaceUris;

        /// <inheritdoc/>
        public IEncodeableFactory Factory => m_session.Factory;

        /// <inheritdoc/>
        public NodeIdDictionary<DataDictionary> DataTypeSystem { get; } = [];

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
            NodeId dataTypeSystem = null,
            CancellationToken ct = default)
        {
            if (dataTypeSystem == null)
            {
                dataTypeSystem = ObjectIds.OPCBinarySchema_TypeSystem;
            }
            else if (!Utils.IsEqual(dataTypeSystem, ObjectIds.OPCBinarySchema_TypeSystem)
                  && !Utils.IsEqual(dataTypeSystem, ObjectIds.XmlSchema_TypeSystem))
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid,
                    $"{nameof(dataTypeSystem)} does not refer to a valid data dictionary.");
            }

            // find the dictionary for the description.
            var references = await FindReferencesAsync(
                dataTypeSystem,
                ReferenceTypeIds.HasComponent,
                false,
                ct).ConfigureAwait(false);

            if (references.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid,
                    "Type system does not contain a valid data dictionary.");
            }

            // batch read all encodings and namespaces
            var referenceNodeIds = references.Select(r => r.NodeId).ToList();

            // find namespace properties
            var namespaceReferences = await FindReferencesAsync(
                referenceNodeIds,
                ReferenceTypeIds.HasProperty,
                false,
                ct).ConfigureAwait(false);

            var namespaceNodes = namespaceReferences
                .Where(n => n.BrowseName == BrowseNames.NamespaceUri)
                .ToList();

            // read all schema definitions
            var referenceExpandedNodeIds = references
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris))
                .Where(n => n.NamespaceIndex != 0).ToList();

            IDictionary<NodeId, byte[]> schemas = await ReadDictionariesAsync(
                referenceExpandedNodeIds,
                ct).ConfigureAwait(false);

            // read namespace property values
            var namespaces = new Dictionary<NodeId, string>();

            IReadOnlyList<DataValue> nameSpaceValues = await GetValuesAsync(
                namespaceNodes.Select(n => n.NodeId),
                ct).ConfigureAwait(false);

            // build the namespace dictionary
            for (int ii = 0; ii < nameSpaceValues.Count; ii++)
            {
                if (StatusCode.IsNotBad(nameSpaceValues[ii].StatusCode))
                {
                    var dataValue = nameSpaceValues[ii].Value;

                    // servers may optimize space by not returning a dictionary
                    if (dataValue != null)
                    {
                        if (dataValue is string ns)
                        {
                            namespaces[(NodeId)referenceNodeIds[ii]] = ns;
                            continue;
                        }
                        nameSpaceValues[ii].StatusCode = StatusCodes.BadEncodingError;
                    }
                }
                Utils.LogWarning("Failed to load namespace {0}: {1}", namespaceNodes[ii].NodeId,
                    nameSpaceValues[ii].StatusCode);
            }

            // build the namespace/schema import dictionary
            var imports = new Dictionary<string, byte[]>();
            foreach (INode r in references)
            {
                var nodeId = ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris);
                if (schemas.TryGetValue(nodeId, out byte[] schema) &&
                    namespaces.TryGetValue(nodeId, out string ns))
                {
                    imports[ns] = schema;
                }
            }

            // read all type dictionaries in the type system
            foreach (INode r in references)
            {
                DataDictionary dictionaryToLoad = null;
                var dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris);
                if (dictionaryId.NamespaceIndex != 0 &&
                    !DataTypeSystem.TryGetValue(dictionaryId, out dictionaryToLoad))
                {
                    try
                    {
                        dictionaryToLoad = new DataDictionary();
                        if (schemas.TryGetValue(dictionaryId, out byte[] schema))
                        {
                            await LoadDictionaryAsync(
                                dictionaryToLoad,
                                dictionaryId,
                                dictionaryId.ToString(),
                                schema,
                                imports,
                                ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await LoadDictionaryAsync(
                                dictionaryToLoad,
                                dictionaryId,
                                dictionaryId.ToString(),
                                ct: ct).ConfigureAwait(false);
                        }
                        DataTypeSystem[dictionaryId] = dictionaryToLoad;
                    }
                    catch (Exception ex)
                    {
                        Utils.LogError("Dictionary load error for Dictionary {0} : {1}",
                            r.NodeId, ex.Message);
                    }
                }
            }
            return DataTypeSystem;
        }

        /// <inheritdoc/>
        public async Task<IList<NodeId>> BrowseForEncodingsAsync(
            IList<ExpandedNodeId> nodeIds,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            // cache type encodings
            var encodings = await FindReferencesAsync(
                 nodeIds,
                 ReferenceTypeIds.HasEncoding,
                 false,
                 ct).ConfigureAwait(false);

            // cache dictionary descriptions
            nodeIds = [.. encodings.Select(r => r.NodeId)];
            var descriptions = await FindReferencesAsync(
                nodeIds,
                ReferenceTypeIds.HasDescription,
                false,
                ct).ConfigureAwait(false);

            return [.. encodings
                .Where(r => supportedEncodings.Contains(r.BrowseName.Name))
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris))];
        }

        /// <inheritdoc/>
        public async Task<(IList<NodeId> encodings, ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId)> BrowseForEncodingsAsync(
            ExpandedNodeId nodeId,
            string[] supportedEncodings,
            CancellationToken ct = default)
        {
            var references = await FindReferencesAsync(
                nodeId,
                ReferenceTypeIds.HasEncoding,
                false,
                ct).ConfigureAwait(false);

            ExpandedNodeId binaryEncodingId = references
                .FirstOrDefault(r => r.BrowseName.Name == BrowseNames.DefaultBinary)?.NodeId;
            binaryEncodingId = NormalizeExpandedNodeId(binaryEncodingId);
            ExpandedNodeId xmlEncodingId = references
                .FirstOrDefault(r => r.BrowseName.Name == BrowseNames.DefaultXml)?.NodeId;
            xmlEncodingId = NormalizeExpandedNodeId(xmlEncodingId);
            return (references
                .Where(r => supportedEncodings.Contains(r.BrowseName.Name))
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris))
                .ToList(), binaryEncodingId, xmlEncodingId);
        }

        /// <inheritdoc/>
        public async Task<(ExpandedNodeId typeId, ExpandedNodeId encodingId, DataTypeNode dataTypeNode)> BrowseTypeIdsForDictionaryComponentAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            ExpandedNodeId encodingId;
            DataTypeNode dataTypeNode;

            var references = await FindReferencesAsync(
                nodeId,
                ReferenceTypeIds.HasDescription,
                true,
                ct).ConfigureAwait(false);

            if (references.Count == 1)
            {
                encodingId = references[0].NodeId;
                references = await FindReferencesAsync(
                    encodingId,
                    ReferenceTypeIds.HasEncoding,
                    true,
                    ct).ConfigureAwait(false);
                encodingId = NormalizeExpandedNodeId(encodingId);

                if (references.Count == 1)
                {
                    ExpandedNodeId typeId = references[0].NodeId;
                    dataTypeNode = await GetNodeAsync(typeId, ct).ConfigureAwait(false) as DataTypeNode;
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
                INode rootNode = await GetNodeAsync(dataType, ct).ConfigureAwait(false);
                if (rootNode is not DataTypeNode)
                {
                    throw new ServiceResultException("Root Node is not a DataType node.");
                }
                result.Add(rootNode);
            }

            while (nodesToBrowse.Count > 0)
            {
                var response = await FindReferencesAsync(
                    nodesToBrowse,
                    ReferenceTypeIds.HasSubtype,
                    false,
                    ct).ConfigureAwait(false);

                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                if (nestedSubTypes)
                {
                    nextNodesToBrowse.AddRange([.. response.Select(r => r.NodeId)]);
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
            Utils.LogInfo("LoadDataTypes returns {0} nodes in {1}ms",
                result.Count, stopwatch.ElapsedMilliseconds);
#endif
            return result;
        }

        /// <inheritdoc/>
        public async Task<object> GetEnumTypeArrayAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            // find the property reference for the enum type
            var references = await FindReferencesAsync(
                nodeId,
                ReferenceTypeIds.HasProperty,
                false,
                ct).ConfigureAwait(false);
            INode property = references.FirstOrDefault();
            if (property != null)
            {
                // read the enum type array
                DataValue value = await GetValueAsync(property.NodeId, ct).ConfigureAwait(false);
                return value?.Value;
            }
            return null;
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
        {
            return GetSuperTypeAsync(typeId, ct)
#if USE_LRU_CACHE
                .AsTask()
#endif
                ;
        }

        /// <inheritdoc/>
        public Task<INode> FindAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            return GetNodeAsync(nodeId, ct)
#if USE_LRU_CACHE
                .AsTask()
#endif
                ;
        }
        #endregion IComplexTypeResolver

        #region Internal Methods
        /// <summary>
        /// Helper to load a DataDictionary by its NodeId.
        /// </summary>
        /// <param name="dictionaryId"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        internal async Task<DataDictionary> LoadDictionaryAsync(
            NodeId dictionaryId,
            string name)
        {
            var dictionaryToLoad = new DataDictionary();
            await LoadDictionaryAsync(dictionaryToLoad, dictionaryId, name);
            return dictionaryToLoad;
        }

        /// <summary>
        /// Reads the contents of a data dictionary directly from the server.
        /// </summary>
        internal async Task<byte[]> ReadDictionaryAsync(
            NodeId dictionaryId,
            CancellationToken ct = default)
        {
            var itemsToRead = new ReadValueIdCollection {
                new ReadValueId {
                    NodeId = dictionaryId,
                    AttributeId = Attributes.Value,
                    IndexRange = null,
                    DataEncoding = null
                }
            };
            // read value.
            try
            {
                ReadResponse readResult = await m_session.ReadAsync(null, 0,
                    TimestampsToReturn.Neither, itemsToRead, ct);

                ResponseHeader responseHeader = readResult.ResponseHeader;
                DataValueCollection values = readResult.Results;
                DiagnosticInfoCollection diagnosticInfos = readResult.DiagnosticInfos;

                ClientBase.ValidateResponse(values, itemsToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

                // check for error.
                if (StatusCode.IsBad(values[0].StatusCode))
                {
                    ServiceResult result = ClientBase.GetResult(
                        values[0].StatusCode, 0, diagnosticInfos, responseHeader);
                    throw new ServiceResultException(result);
                }

                // return as a byte array.
                return values[0].Value as byte[];
            }
            catch (ServiceResultException ex)
            {
                if (ex.StatusCode != StatusCodes.BadEncodingLimitsExceeded)
                {
                    throw;
                }
                // Fall back to reading the byte string in chunks.
                try
                {
                    return await m_session.ReadByteStringInChunksAsync(
                        dictionaryId, ct).ConfigureAwait(false);
                }
                catch
                {
                    ExceptionDispatchInfo.Capture(ex).Throw();
                    throw;
                }
            }
        }

        /// <summary>
        /// Reads the contents of multiple data dictionaries directly from the server.
        /// </summary>
        internal async Task<IDictionary<NodeId, byte[]>> ReadDictionariesAsync(
            IList<NodeId> dictionaryIds,
            CancellationToken ct)
        {
            var result = new Dictionary<NodeId, byte[]>();
            if (dictionaryIds.Count == 0)
            {
                return result;
            }

            var itemsToRead = new ReadValueIdCollection();
            foreach (NodeId nodeId in dictionaryIds)
            {
                // create item to read.
                var itemToRead = new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    IndexRange = null,
                    DataEncoding = null
                };
                itemsToRead.Add(itemToRead);
            }

            // read values.
            ReadResponse readResponse = await m_session.ReadAsync(null, 0,
                TimestampsToReturn.Neither, itemsToRead, ct).ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;
            ResponseHeader response = readResponse.ResponseHeader;
            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            int ii = 0;
            foreach (NodeId nodeId in dictionaryIds)
            {
                // check for error.
                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    if (values[ii].StatusCode != StatusCodes.BadEncodingLimitsExceeded)
                    {
                        ServiceResult sr = ClientBase.GetResult(
                            values[ii].StatusCode, 0, diagnosticInfos, response);
                        throw new ServiceResultException(sr);
                    }

                    // Fall back to reading the byte string in chunks.
                    result[nodeId] = await m_session.ReadByteStringInChunksAsync(
                        nodeId, ct).ConfigureAwait(false);
                }
                else
                {
                    // return as a byte array.
                    result[nodeId] = values[ii].Value as byte[];
                }
                ii++;
            }
            return result;
        }
        #endregion Internal Methods

        #region Private Methods
        /// <summary>
        /// Loads the dictionary identified by the node id.
        /// </summary>
        private async Task LoadDictionaryAsync(
            DataDictionary dictionaryToLoad,
            NodeId dictionaryId,
            string name,
            byte[] schema = null,
            IDictionary<string, byte[]> imports = null,
            CancellationToken ct = default)
        {
            if (dictionaryId == null)
            {
                throw new ArgumentNullException(nameof(dictionaryId));
            }

            await AddTypeSystemAsync(dictionaryToLoad, dictionaryId, ct).ConfigureAwait(false);

            if (schema == null || schema.Length == 0)
            {
                schema = await ReadDictionaryAsync(dictionaryId, ct).ConfigureAwait(false);
            }

            if (schema == null || schema.Length == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "Cannot parse empty data dictionary.");
            }

            // Interoperability: some server may return a null terminated dictionary string
            int zeroTerminator = Array.IndexOf<byte>(schema, 0);
            if (zeroTerminator >= 0)
            {
                // adjust length
                Array.Resize(ref schema, zeroTerminator);
            }

            dictionaryToLoad.Validate(schema, imports);

            await AddDataTypesAsync(dictionaryToLoad, dictionaryId, ct).ConfigureAwait(false);

            dictionaryToLoad.DictionaryId = dictionaryId;
            dictionaryToLoad.Name = name;
        }

        /// <summary>
        /// Retrieves the type system for the dictionary.
        /// </summary>
        private async Task AddTypeSystemAsync(
            DataDictionary dictionaryToLoad,
            NodeId dictionaryId,
            CancellationToken ct)
        {
            var references = await FindReferencesAsync(
                dictionaryId,
                ReferenceTypeIds.HasComponent,
                true,
                ct).ConfigureAwait(false);
            if (references.Count > 0)
            {
                dictionaryToLoad.TypeSystemId = ExpandedNodeId.ToNodeId(
                    references[0].NodeId, NamespaceUris);
                dictionaryToLoad.TypeSystemName = references[0].ToString();
            }
        }

        /// <summary>
        /// Retrieves the data types in the dictionary.
        /// </summary>
        /// <remarks>
        /// In order to allow for fast Linq matching of dictionary
        /// QNames with the data type nodes, the BrowseName of
        /// the DataType node is replaced with Value string.
        /// </remarks>
        private async Task AddDataTypesAsync(
            DataDictionary dictionaryToLoad,
            NodeId dictionaryId,
            CancellationToken ct)
        {
            var references = await FindReferencesAsync(
                dictionaryId,
                ReferenceTypeIds.HasComponent,
                false,
                ct).ConfigureAwait(false);

            // read the value to get the names that are used in the dictionary
            var values = await GetValuesAsync(references.Select(node => node.NodeId), ct);
            Debug.Assert(values.Count == references.Count);
            int ii = 0;
            foreach (INode reference in references)
            {
                DataValue value = values[ii++];
                var datatypeId = ExpandedNodeId.ToNodeId(reference.NodeId, NamespaceUris);
                if (datatypeId != null &&
                    StatusCode.IsGood(value.StatusCode) &&
                    value.Value is string dictName)
                {
                    dictionaryToLoad.DataTypes[datatypeId] =
                        new QualifiedName(dictName, datatypeId.NamespaceIndex);
                }
            }
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

#if USE_LRU_CACHE
        /// <summary>
        /// Get the super type of the specified type id.
        /// </summary>
        private ValueTask<NodeId> GetSuperTypeAsync(NodeId typeId, CancellationToken ct)
        {
            return m_lruNodeCache.GetSuperTypeAsync(typeId, ct);
        }

        /// <summary>
        /// Get the node identified by the expanded node id.
        /// </summary>
        private ValueTask<INode> GetNodeAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            return m_lruNodeCache.GetNodeAsync(nodeId, ct);
        }

        /// <summary>
        /// Reads the value of a node identified by the expanded node id.
        /// </summary>
        private async Task<DataValue> GetValueAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            return await m_lruNodeCache.GetValueAsync(nodeId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Get values for a collection of node Ids.
        /// </summary>
        /// <returns></returns>
        private ValueTask<IReadOnlyList<DataValue>> GetValuesAsync(
            IEnumerable<ExpandedNodeId> nodeIds,
            CancellationToken ct)
        {
            return m_lruNodeCache.GetValuesAsync(nodeIds.ToList(), ct);
        }

        /// <summary>
        /// Returns the references of the specified node that meet the criteria specified.
        /// </summary>
        private ValueTask<IReadOnlyList<INode>> FindReferencesAsync(
            ExpandedNodeId nodeIds,
            NodeId referenceTypeId,
            bool isInverse,
            CancellationToken ct)
        {
            return m_lruNodeCache.GetReferencesAsync(
                nodeIds,
                referenceTypeId,
                isInverse,
                false,
                ct);
        }

        /// <summary>
        /// Returns the references of the specified nodes that meet the criteria specified.
        /// </summary>
        private ValueTask<IReadOnlyList<INode>> FindReferencesAsync(
            IEnumerable<ExpandedNodeId> nodeIds,
            NodeId referenceTypeId,
            bool isInverse,
            CancellationToken ct)
        {
            return m_lruNodeCache.GetReferencesAsync(
                nodeIds.ToList(),
                [referenceTypeId],
                isInverse,
                false,
                ct);
        }
#else
        /// <summary>
        /// Get the super type of the specified type id.
        /// </summary>
        private Task<NodeId> GetSuperTypeAsync(NodeId typeId, CancellationToken ct)
        {
            return m_session.NodeCache.FindSuperTypeAsync(typeId, ct);
        }

        /// <summary>
        /// Get the node identified by the expanded node id.
        /// </summary>
        private Task<INode> GetNodeAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            return m_session.NodeCache.FindAsync(nodeId, ct);
        }

        /// <summary>
        /// Reads the value of a node identified by the expanded node id.
        /// </summary>
        private Task<DataValue> GetValueAsync(
            ExpandedNodeId expandedNodeId,
            CancellationToken ct)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, NamespaceUris);
            return m_session.ReadValueAsync(nodeId, ct);
        }

        /// <summary>
        /// Get values for a collection of node Ids.
        /// </summary>
        /// <returns></returns>
        private async Task<IReadOnlyList<DataValue>> GetValuesAsync(
            IEnumerable<ExpandedNodeId> expandedNodeIds,
            CancellationToken ct)
        {
            var nodeIds = expandedNodeIds
                .Select(n => ExpandedNodeId.ToNodeId(n, NamespaceUris))
                .ToList();
            (DataValueCollection values, IList<ServiceResult> errors) = await m_session.ReadValuesAsync(
                nodeIds,
                ct).ConfigureAwait(false);
            return [.. values
                .Zip(errors, (first, second) => StatusCode.IsNotBad(second.StatusCode) ?
                    first : new DataValue(second.StatusCode))];
        }

        /// <summary>
        /// Returns the references of the specified node that meet the criteria specified.
        /// </summary>
        private Task<IList<INode>> FindReferencesAsync(
            ExpandedNodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            CancellationToken ct)
        {
            return m_session.NodeCache.FindReferencesAsync(
                nodeId,
                referenceTypeId,
                isInverse,
                false,
                ct);
        }

        /// <summary>
        /// Returns the references of the specified nodes that meet the criteria specified.
        /// </summary>
        private Task<IList<INode>> FindReferencesAsync(
            IEnumerable<ExpandedNodeId> nodeIds,
            NodeId referenceTypeId,
            bool isInverse,
            CancellationToken ct)
        {
            return m_session.NodeCache.FindReferencesAsync(
                [.. nodeIds],
                [referenceTypeId],
                isInverse,
                false,
                ct);
        }
#endif
        #endregion Private Methods

        #region Private Fields
#if USE_LRU_CACHE
        private readonly ILruNodeCache m_lruNodeCache;
#endif
        private readonly ISession m_session;
        #endregion Private Fields
    }//namespace
}
