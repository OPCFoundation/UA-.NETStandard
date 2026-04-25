// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Client.Nodes;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// The data type cache is an encodeable factory that plugs into the encoder
    /// and decoder system of the stack and contains all types in the server.
    /// The types can be loaded and reloaded from the server.
    /// The types are dynamically preloaded when they are needed. This happens
    /// whereever custom types are required in the API surface, e.g. reading
    /// writing and calling as well as creating monitored items. Alternatively
    /// the user can choose to load all types when the session is created or
    /// recreated with the server.
    /// </summary>
    internal class DataTypeDescriptionResolver : IEncodeableFactory,
        IDataTypeDescriptionManager, IDataTypeDescriptionResolver
    {
        /// <inheritdoc/>
        public IEnumerable<ExpandedNodeId> KnownTypeIds
            => _context.Factory.KnownTypeIds;

        /// <inheritdoc/>
        public IEncodeableFactoryBuilder Builder
            => _context.Factory.Builder;

        /// <summary>
        /// Initializes the type system with a session and optionally type
        /// factory to load the custom types.
        /// </summary>
        /// <param name="nodeCache"></param>
        /// <param name="context"></param>
        /// <param name="dataTypeSystems"></param>
        /// <param name="logger"></param>
        public DataTypeDescriptionResolver(INodeCache nodeCache,
            IServiceMessageContext context, IDataTypeSystemManager? dataTypeSystems,
            ILogger<DataTypeDescriptionResolver> logger)
        {
            _logger = logger;
            _context = context;
            _nodeCache = nodeCache;
            _dataTypeSystems = dataTypeSystems;
        }

        /// <summary>
        /// Clone constructor
        /// </summary>
        /// <param name="dataTypeSystem"></param>
        private DataTypeDescriptionResolver(DataTypeDescriptionResolver dataTypeSystem)
        {
            _logger = dataTypeSystem._logger;
            _context = dataTypeSystem._context;
            _nodeCache = dataTypeSystem._nodeCache;
            _dataTypeSystems = dataTypeSystem._dataTypeSystems;
        }

        /// <inheritdoc/>
        public bool TryGetEncodeableType(ExpandedNodeId typeId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEncodeableType? encodeableType)
        {
            return _context.Factory.TryGetEncodeableType(typeId, out encodeableType);
        }

        /// <inheritdoc/>
        public bool TryGetEnumeratedType(ExpandedNodeId typeId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumeratedType? enumeratedType)
        {
            return _context.Factory.TryGetEnumeratedType(typeId, out enumeratedType);
        }

        /// <inheritdoc/>
        public bool TryGetType(XmlQualifiedName xmlName,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IType? type)
        {
            return _context.Factory.TryGetType(xmlName, out type);
        }

        /// <summary>
        /// Returns the system type for the specified type or encoding id.
        /// </summary>
        public Type? GetSystemType(ExpandedNodeId typeOrEncodingId)
        {
            if (_structures.ContainsKey(typeOrEncodingId))
            {
                return typeof(StructureValue);
            }
            if (_enums.ContainsKey(typeOrEncodingId))
            {
                return typeof(EnumValue);
            }
            return _context.Factory.GetSystemType(typeOrEncodingId);
        }

        /// <inheritdoc/>
        public StructureDescription? GetStructureDescription(ExpandedNodeId typeOrEncodingId)
        {
            if (!_structures.TryGetValue(typeOrEncodingId, out var info))
            {
                // sync load the description from the server
                return GetDataTypeDescriptionAsync(typeOrEncodingId, default)
                    .AsTask().GetAwaiter().GetResult() as StructureDescription;
            }
            return info;
        }

        /// <inheritdoc/>
        public EnumDescription? GetEnumDescription(ExpandedNodeId typeOrEncodingId)
        {
            if (!_enums.TryGetValue(typeOrEncodingId, out var info))
            {
                // sync load the description from the server
                return GetDataTypeDescriptionAsync(typeOrEncodingId, default)
                    .AsTask().GetAwaiter().GetResult() as EnumDescription;
            }
            return info;
        }

        /// <inheritdoc/>
        public async ValueTask<IDictionary<ExpandedNodeId, DataTypeDefinition>> GetDefinitionsAsync(
            ExpandedNodeId dataTypeId, CancellationToken ct)
        {
            var dataTypeDefinitions = new Dictionary<ExpandedNodeId, DataTypeDefinition>();
            await PreloadDataTypeAsync(dataTypeId, true, ct).ConfigureAwait(false);
            CollectAllDataTypeDefinitions(dataTypeId, dataTypeDefinitions);
            return dataTypeDefinitions;

            void CollectAllDataTypeDefinitions(ExpandedNodeId typeId,
                Dictionary<ExpandedNodeId, DataTypeDefinition> collect)
            {
                Debug.Assert(!typeId.IsNull, "Unexpected null data type id");
                if (_structures.TryGetValue(typeId, out var dataTypeDefinition))
                {
                    var structureDefinition = dataTypeDefinition.StructureDefinition;
                    collect[typeId] = structureDefinition;

                    foreach (var field in structureDefinition.Fields)
                    {
                        if (!collect.ContainsKey(field.DataType))
                        {
                            CollectAllDataTypeDefinitions(field.DataType, collect);
                        }
                    }
                }
                else if (_enums.TryGetValue(typeId, out var enumDescription))
                {
                    collect[typeId] = enumDescription.EnumDefinition;
                }
            }
        }

        /// <summary>
        /// Get a data type definition or load it if not already loaded into the cache.
        /// The method returns null if the type cannot be resolved.
        /// </summary>
        /// <param name="typeOrEncodingId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<DataTypeDescription?> GetDataTypeDescriptionAsync(
            ExpandedNodeId typeOrEncodingId, CancellationToken ct)
        {
            var definition = GetDataTypeDescription(typeOrEncodingId);
            if (definition != null)
            {
                return ValueTask.FromResult<DataTypeDescription?>(definition);
            }
            return GetDataTypeDefinitionAsyncCore(typeOrEncodingId, ct);
            async ValueTask<DataTypeDescription?> GetDataTypeDefinitionAsyncCore(
                ExpandedNodeId dataTypeId, CancellationToken ct)
            {
                var dataTypeNode = await GetDataTypeAsync(dataTypeId, ct).ConfigureAwait(false);
                if (dataTypeNode != null &&
                    await AddDataTypeAsync(dataTypeNode, ct).ConfigureAwait(false))
                {
                    return GetDataTypeDescription(dataTypeId);
                }
                _logger.LogDebug("Failed to get definition for type {DataTypeId}", dataTypeId);
                return null;
            }
        }

        /// <summary>
        /// Load the data type definitions for the data type referenced by the provided
        /// node id. If the node is not a data type, try to resolve the data type the
        /// user intended to use.
        /// </summary>
        /// <param name="dataTypeId"></param>
        /// <param name="includeSubTypes"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask PreloadDataTypeAsync(ExpandedNodeId dataTypeId,
            bool includeSubTypes, CancellationToken ct)
        {
            var dataTypeNode = await GetDataTypeAsync(dataTypeId, ct).ConfigureAwait(false);
            if (dataTypeNode == null)
            {
                // Could not find data type node
                return;
            }
            if (includeSubTypes)
            {
                // Load all subtypes of this data type
                await foreach (var subType in GetUnknownSubTypesAsync(dataTypeNode.NodeId,
                    ct).ConfigureAwait(false))
                {
                    await PreloadAsync(subType, ct).ConfigureAwait(false);
                }
            }
            if (IsKnownType(dataTypeNode.NodeId))
            {
                // Type is already known to us
                return;
            }
            await PreloadAsync(dataTypeNode, ct).ConfigureAwait(false);

            async ValueTask PreloadAsync(DataTypeNode dataTypeNode, CancellationToken ct)
            {
                if (!await AddDataTypeAsync(dataTypeNode, ct).ConfigureAwait(false))
                {
                    _logger.LogDebug("Preloading type {DataTypeId} failed.",
                        dataTypeNode.NodeId);
                    return;
                }
                if (_structures.TryGetValue(dataTypeNode.NodeId, out var description))
                {
                    // Preload all field types if needed
                    var includeSubtypes = description.FieldsCanHaveSubtypedValues;
                    var fieldsArray = description.StructureDefinition.Fields.ToArray()!;
                    foreach (var field in fieldsArray)
                    {
                        if (!includeSubtypes && IsKnownType(field.DataType))
                        {
                            continue;
                        }
                        await PreloadDataTypeAsync(field.DataType, includeSubtypes,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Load all custom types from a server using the session node cache.
        /// The loader loads all data types first and then all definitions
        /// associated with them.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns>true if all DataTypes were loaded.</returns>
        public async ValueTask<bool> PreloadAllDataTypeAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            // Get all unknown types in the type hierarchy of the base data type
            var allTypesLoaded = true;
            await foreach (var type in GetUnknownSubTypesAsync(
                DataTypeIds.BaseDataType, ct).ConfigureAwait(false))
            {
                if (!await AddDataTypeAsync(type, ct).ConfigureAwait(false))
                {
                    _logger.LogDebug("Preloading type {DataTypeId} failed.",
                        type.NodeId);
                    allTypesLoaded = false;
                }
            }
            _logger.LogInformation("Preloading all types took {Duration}ms.",
                sw.ElapsedMilliseconds);
            return allTypesLoaded;
        }

        /// <summary>
        /// Get a data type definition if it is already loaded
        /// </summary>
        /// <param name="dataTypeId"></param>
        /// <returns></returns>
        public DataTypeDescription? GetDataTypeDescription(ExpandedNodeId dataTypeId)
        {
            if (_structures.TryGetValue(dataTypeId, out var structureDescription))
            {
                return structureDescription;
            }
            if (_enums.TryGetValue(dataTypeId, out var enumDescription))
            {
                return enumDescription;
            }
            return null;
        }

        /// <summary>
        /// Try to get the data type user wanted. If the data type id is not a data type
        /// node then if it is a variable or variable type, use the data type of it, if
        /// it is a encoding of a type, use the data type that references the encoding.
        /// Otherwise give up and return null.
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async ValueTask<DataTypeNode?> GetDataTypeAsync(ExpandedNodeId typeId,
            CancellationToken ct)
        {
            var nodeId = ExpandedNodeId.ToNodeId(typeId, _context.NamespaceUris);
            var node = await _nodeCache.GetNodeAsync(nodeId, ct).ConfigureAwait(false);
            if (node is not DataTypeNode)
            {
                // Load the type definition for the variable or variable type
                switch (node)
                {
                    case VariableNode v:
                        // Check if this is a variable of type Encoding
                        // Then we want the inverse HasEncoding reference to it
                        var references = await _nodeCache.GetReferencesAsync(
                            nodeId, ReferenceTypeIds.HasEncoding, true, false, ct)
                            .ConfigureAwait(false);
                        if (references.Count == 1) // It is a encoding of a type
                        {
                            typeId = references[0].NodeId;
                            break;
                        }
                        typeId = v.DataType;
                        break;
                    case VariableTypeNode v:
                        typeId = v.DataType;
                        break;
                    default:
                        // Nothing to do
                        return null;
                }
                nodeId = ExpandedNodeId.ToNodeId(typeId, _context.NamespaceUris);
                node = await _nodeCache.GetNodeAsync(nodeId, ct).ConfigureAwait(false);
            }
            return node as DataTypeNode;
        }

        /// <summary>
        /// Get the data type definition from the data type node. Validates the type
        /// information is correct and returns null if it is not.
        /// </summary>
        /// <param name="dataTypeNode"></param>
        /// <exception cref="ServiceResultException">In case the presented data is
        /// completely incorrect</exception>
        internal DataTypeDefinition? GetDataTypeDefinition(DataTypeNode dataTypeNode)
        {
            if (dataTypeNode.DataTypeDefinition.IsNull ||
                !dataTypeNode.DataTypeDefinition.TryGetEncodeable(out IEncodeable dtDefBody))
            {
                return null;
            }
            switch (dtDefBody)
            {
                case EnumDefinition enumDefinition:
                    if (enumDefinition.Fields.Count == 0)
                    {
                        return enumDefinition;
                    }
                    if (enumDefinition.Fields.ToArray()!.Select(f => f.Name)
                            .Distinct()
                            .Count() !=
                        enumDefinition.Fields.Count)
                    {
                        _logger.LogWarning("Using Enum definition of data type {Type} " +
                            "which contains duplicate field names.", dataTypeNode.NodeId);
                        // We accept this because it makes no difference to us
                    }
                    return enumDefinition;
                case StructureDefinition structureDefinition:
                    switch (structureDefinition.StructureType)
                    {
                        case StructureType.Union:
                        case StructureType.UnionWithSubtypedValues:
                            if (structureDefinition.BaseDataType.IsNull)
                            {
                                structureDefinition.BaseDataType = DataTypeIds.Union;
                            }
                            break;
                        case StructureType.StructureWithOptionalFields:
                        case StructureType.StructureWithSubtypedValues:
                        case StructureType.Structure:
                            if (structureDefinition.BaseDataType.IsNull)
                            {
                                structureDefinition.BaseDataType = DataTypeIds.Structure;
                            }
                            break;
                        default:
                            _logger.LogError("Structure definition of data type {Type} " +
                                "has unknown structure type {Type}.",
                                dataTypeNode.NodeId, structureDefinition.StructureType);
                            return null;
                    }
                    if (structureDefinition.Fields.Count == 0)
                    {
                        return structureDefinition;
                    }
                    for (var i = 0; i < structureDefinition.Fields.Count; i++)
                    {
                        var field = structureDefinition.Fields[i];
                        // https://reference.opcfoundation.org/Core/Part3/v105/docs/8.51
                        if (string.IsNullOrWhiteSpace(field.Name))
                        {
                            _logger.LogError("Field at index {Index} in structure " +
                                "definition of data type {Type} is missing a name.",
                                i, dataTypeNode.NodeId);
                            return null;
                        }
                        if (field.DataType.IsNull)
                        {
                            _logger.LogError("Field {Name} in structure definition of " +
                                "data type {Type} is missing a data type.",
                                field.Name, dataTypeNode.NodeId);
                            return null;
                        }
                        if (field.ValueRank is not
                            (ValueRanks.Scalar or >= ValueRanks.OneDimension))
                        {
                            _logger.LogError("Field {Name} in structure definition of " +
                                "data type {Type} has an invalid value rank {ValueRank}.",
                                field.Name, dataTypeNode.NodeId, field.ValueRank);
                            return null;
                        }
                    }
                    if (structureDefinition.Fields.ToArray()!.Select(f => f.Name)
                            .Distinct()
                            .Count() !=
                        structureDefinition.Fields.Count)
                    {
                        _logger.LogWarning("Using structure definition of data type {Type} " +
                            "which contains duplicate field names.", dataTypeNode.NodeId);
                        // We accept this because it makes no difference to us
                    }
                    return structureDefinition;
                default:
                    break;
            }
            throw ServiceResultException.Create(StatusCodes.BadDataEncodingInvalid,
                "Data type definition information provided by server is not valid");
        }

        /// <summary>
        /// Add type information
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="definition"></param>
        /// <param name="binaryEncodingId"></param>
        /// <param name="xmlEncodingId"></param>
        /// <param name="jsonEncodingId"></param>
        /// <param name="xmlName"></param>
        /// <param name="isAbstract"></param>
        /// <param name="xmlDefinition"></param>
        internal void Add(ExpandedNodeId typeId, DataTypeDefinition definition,
            ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId,
            ExpandedNodeId jsonEncodingId, XmlQualifiedName xmlName, bool isAbstract,
            DataTypeDefinition? xmlDefinition = null)
        {
            switch (definition)
            {
                case StructureDefinition structureDefinition:
                    var structureDescription = StructureDescription.Create(this, typeId,
                        structureDefinition, xmlName, binaryEncodingId, xmlEncodingId,
                        jsonEncodingId, isAbstract);
                    _structures[typeId] = structureDescription;
                    if (binaryEncodingId != ExpandedNodeId.Null)
                    {
                        _structures[binaryEncodingId] = structureDescription;
                    }
                    if (xmlEncodingId != ExpandedNodeId.Null)
                    {
                        if (xmlDefinition is StructureDefinition xml)
                        {
                            structureDescription = StructureDescription.Create(this,
                                typeId, xml, xmlName, binaryEncodingId, xmlEncodingId,
                                ExpandedNodeId.Null, isAbstract);
                        }
                        _structures[xmlEncodingId] = structureDescription;
                    }
                    break;
                case EnumDefinition enumDefinition:
                    var enumDescription = new EnumDescription(typeId, enumDefinition,
                        xmlName, binaryEncodingId, xmlEncodingId, jsonEncodingId,
                        isAbstract);
                    _enums[typeId] = enumDescription;
                    if (binaryEncodingId != ExpandedNodeId.Null)
                    {
                        _enums[binaryEncodingId] = enumDescription;
                    }
                    if (xmlEncodingId != ExpandedNodeId.Null)
                    {
                        if (xmlDefinition is EnumDefinition xml)
                        {
                            enumDescription = new EnumDescription(typeId, xml, xmlName,
                                binaryEncodingId, xmlEncodingId, jsonEncodingId, isAbstract);
                        }
                        _enums[xmlEncodingId] = enumDescription;
                    }
                    break;
            }
        }

        /// <summary>
        /// Add an structure type defined in a DataType node to the type cache.
        /// </summary>
        /// <param name="dataTypeNode"></param>
        /// <param name="ct"></param>
        private async ValueTask<bool> AddDataTypeAsync(DataTypeNode dataTypeNode,
            CancellationToken ct)
        {
            // Get encodings
            var source = ExpandedNodeId.ToNodeId(dataTypeNode.NodeId,
                _context.NamespaceUris);
            var references = await _nodeCache.GetReferencesAsync(source,
                ReferenceTypeIds.HasEncoding, false, false, ct).ConfigureAwait(false);
            var lookup = references.ToDictionary(r => r.BrowseName, r =>
                NormalizeExpandedNodeId(r.NodeId));
            var binaryEncodingId = lookup.TryGetValue((QualifiedName)BrowseNames.DefaultBinary,
                out var b) ? b : ExpandedNodeId.Null;
            var xmlEncodingId = lookup.TryGetValue((QualifiedName)BrowseNames.DefaultXml,
                out var x) ? x : ExpandedNodeId.Null;
            var jsonEncodingId = lookup.TryGetValue((QualifiedName)BrowseNames.DefaultJson,
                out var j) ? j : ExpandedNodeId.Null;

            XmlQualifiedName? name = null;
            var dataTypeId = NormalizeExpandedNodeId(dataTypeNode.NodeId);

            // 1. Use data type definition for all encodings
            var dataTypeDefinition = GetDataTypeDefinition(dataTypeNode);
            if (dataTypeDefinition == null && _dataTypeSystems != null)
            {
                // 2. Use legacy type system
                var def = await _dataTypeSystems.GetDataTypeDefinitionAsync(
                    (QualifiedName)BrowseNames.DefaultBinary, dataTypeNode.NodeId,
                    ct).ConfigureAwait(false);

                dataTypeDefinition = def?.Definition;
                name = def?.XmlName;

                // The xml encoding might be different than the binary encoding
                // This is a special case to handle the 1.03 type system where
                // the xml encoding is defined using xml schema which could be
                // different from the binary schema definition. Therefore we
                // register the xml schema specially.
                if (xmlEncodingId != ExpandedNodeId.Null || dataTypeDefinition == null)
                {
                    var xml = await _dataTypeSystems.GetDataTypeDefinitionAsync(
                        (QualifiedName)BrowseNames.DefaultXml, dataTypeNode.NodeId,
                        ct).ConfigureAwait(false);
                    if (xml?.Definition != null)
                    {
                        dataTypeDefinition ??= xml.Definition;
                        Add(dataTypeId, dataTypeDefinition, binaryEncodingId,
                            xmlEncodingId, jsonEncodingId, xml.XmlName,
                            dataTypeNode.IsAbstract, xml.Definition);
                        return true;
                    }
                }
            }
            if (dataTypeDefinition == null)
            {
                // 3. Give up
                return false;
            }
            if (name == null)
            {
                var typeName = dataTypeNode.BrowseName;
                name = new XmlQualifiedName(typeName.Name,
                    _context.NamespaceUris.GetString(typeName.NamespaceIndex));
            }
            Add(dataTypeId, dataTypeDefinition,
                binaryEncodingId, xmlEncodingId, jsonEncodingId, name, dataTypeNode.IsAbstract);
            return true;
        }

        /// <summary>
        /// Fetch all nodes and subtype nodes of a data type.
        /// </summary>
        /// <param name="dataType"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        private async IAsyncEnumerable<DataTypeNode> GetUnknownSubTypesAsync(
            ExpandedNodeId dataType, [EnumeratorCancellation] CancellationToken ct)
        {
            var nodesToBrowse = new List<NodeId>
            {
                ExpandedNodeId.ToNodeId(dataType, _context.NamespaceUris)
            };
            while (nodesToBrowse.Count > 0)
            {
                var response = await _nodeCache.GetReferencesAsync(nodesToBrowse,
                    [ReferenceTypeIds.HasSubtype], false, false, ct).ConfigureAwait(false);
                foreach (var node in response.OfType<DataTypeNode>()
                    .Where(n => !IsKnownType(n.NodeId)))
                {
                    yield return node;
                }
                nodesToBrowse = response
                    .OfType<DataTypeNode>()
                    .Select(r => ExpandedNodeId.ToNodeId(r.NodeId,
                        _context.NamespaceUris))
                    .ToList();
            }
        }

        /// <summary>
        /// Helper to ensure the expanded nodeId contains a valid namespaceUri.
        /// </summary>
        /// <param name="expandedNodeId">The expanded nodeId.</param>
        /// <returns>The normalized expanded nodeId.</returns>
        private ExpandedNodeId NormalizeExpandedNodeId(ExpandedNodeId expandedNodeId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, _context.NamespaceUris);
            return NodeId.ToExpandedNodeId(nodeId, _context.NamespaceUris);
        }

        /// <summary>
        /// Get the factory system type for an expanded node id.
        /// </summary>
        /// <param name="nodeId"></param>
        private bool IsKnownType(ExpandedNodeId nodeId)
        {
            if (!nodeId.IsAbsolute)
            {
                nodeId = NormalizeExpandedNodeId(nodeId);
            }
            return GetSystemType(nodeId) != null;
        }

        private readonly ILogger _logger;
        private readonly INodeCache _nodeCache;
        private readonly IServiceMessageContext _context;
        private readonly IDataTypeSystemManager? _dataTypeSystems;
        private readonly ConcurrentDictionary<ExpandedNodeId, StructureDescription> _structures = [];
        private readonly ConcurrentDictionary<ExpandedNodeId, EnumDescription> _enums = [];
    }
}
