// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua.Client.Nodes;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// Data type system base class. All known data type systems are derived from
    /// this class. This includes OPCBinary and XMLSchema. Since data type systems
    /// are deprecated in 1.04, only these two are likely ever implemented. See
    /// <see href="https://reference.opcfoundation.org/Core/Part5/v104/docs/D"/> and
    /// <see href="https://reference.opcfoundation.org/Core/Part5/v104/docs/E"/> for
    /// more information.
    /// </summary>
    internal abstract record class DataTypeSystem
    {
        /// <summary>
        /// The data type system identifier
        /// </summary>
        public abstract NodeId TypeSystemId { get; }

        /// <summary>
        /// The name of the data type system
        /// </summary>
        public abstract QualifiedName TypeSystemName { get; }

        /// <summary>
        /// Encoding name
        /// </summary>
        public abstract QualifiedName EncodingName { get; }

        /// <summary>
        /// Get number of type definitions contained in the system.
        /// </summary>
        public int TypeCount => _typeDefinitions.Count;

        /// <summary>
        /// Create data type system
        /// </summary>
        /// <param name="nodeCache"></param>
        /// <param name="context"></param>
        /// <param name="logger"></param>
        protected DataTypeSystem(INodeCache nodeCache,
            IServiceMessageContext context, ILogger logger)
        {
            _logger = logger;
            _nodeCache = nodeCache;
            _context = context;
        }

        /// <summary>
        /// Get data type definitions
        /// </summary>
        /// <param name="dataTypeId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<DictionaryDataTypeDefinition?> GetDataTypeDefinitionAsync(
            ExpandedNodeId dataTypeId, CancellationToken ct)
        {
            if (_typeDefinitions.TryGetValue(dataTypeId, out var definition))
            {
                return ValueTask.FromResult<DictionaryDataTypeDefinition?>(definition);
            }
            return GetDefinitionFromPropertiesAsync(dataTypeId, ct);
        }

        /// <summary>
        /// Load an entire data type system from the server. A data type system
        /// contains all dictionaries relative to the data type. This is the simplest
        /// and fastest way to get access to the dictionaries and resolving all imports
        /// correctly
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask LoadAsync(CancellationToken ct)
        {
            // get all dictionaries in the data type system.
            var dictionaryReferences = await _nodeCache.GetReferencesAsync(TypeSystemId,
                ReferenceTypeIds.HasComponent, false, false, ct).ConfigureAwait(false);
            if (dictionaryReferences.Count == 0)
            {
                _logger.LogError("Loading type system {NodeId} returned 0 dictionaries.",
                    TypeSystemId);
                return;
            }

            // Read all dictionaries
            var dictionaryIds = dictionaryReferences
                .ConvertAll(r => ExpandedNodeId.ToNodeId(r.NodeId, _context.NamespaceUris))
                .ToList();

            var values = await _nodeCache.GetValuesAsync(dictionaryIds,
                ct).ConfigureAwait(false);
            Debug.Assert(dictionaryIds.Count == values.Count);
            var schemas = new Dictionary<NodeId, (string Ns, byte[] Buffer)>();
            for (var index = 0; index < dictionaryIds.Count; index++)
            {
                if (StatusCode.IsBad(values[index].StatusCode))
                {
                    throw new ServiceResultException(values[index].StatusCode);
                }
                if (!values[index].WrappedValue.TryGet(out ByteString bufferBs))
                {
                    throw new ServiceResultException(values[index].StatusCode);
                }

                var buffer = bufferBs.ToArray();
                var zeroTerminator = Array.IndexOf<byte>(buffer, 0);
                if (zeroTerminator >= 0)
                {
                    Array.Resize(ref buffer, zeroTerminator);
                }

                // Read namespace property of the dictionary
                var references = await _nodeCache.GetReferencesAsync(dictionaryIds[index],
                    ReferenceTypeIds.HasProperty, false, false, ct).ConfigureAwait(false);
                var namespaceNodeId = references.ToArray()!
                    .FirstOrDefault(r => r.BrowseName == BrowseNames.NamespaceUri)?.NodeId;
                if (namespaceNodeId == null)
                {
                    continue;
                }
                // read namespace property values
                var nameSpaceValue = await _nodeCache.GetValueAsync(ExpandedNodeId.ToNodeId(
                    namespaceNodeId.Value, _context.NamespaceUris), ct).ConfigureAwait(false);
                string ns = null!;
                if (StatusCode.IsBad(nameSpaceValue.StatusCode) ||
                    !nameSpaceValue.WrappedValue.TryGet(out ns!))
                {
                    _logger.LogWarning("Failed to load namespace {Ns}: {Error}",
                        namespaceNodeId, nameSpaceValue.StatusCode);
                    continue;
                }
                if (!schemas.TryAdd(dictionaryIds[index], (ns, buffer)))
                {
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "Trying to add duplicate dictionary.");
                }
            }

            // build the namespace/schema import dictionary
            var imports = schemas.Values.ToDictionary(v => v.Ns, v => v.Buffer);
            var typeDictionary = new Dictionary<XmlQualifiedName,
                (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)>();
            var dictionaries = new Dictionary<ExpandedNodeId, DataTypeDictionary>();
            foreach (var (dictionaryId, (ns, buffer)) in schemas)
            {
                try
                {
                    dictionaries[dictionaryId] = Load(dictionaryId, ns, buffer, imports);
                    await LoadTypesAsync(dictionaryId, ns, typeDictionary,
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Dictionary load error for Dictionary {NodeId} : {Message}",
                        dictionaryId, ex.Message);
                }
            }
            foreach (var (dictionaryId, dictionary) in dictionaries)
            {
                LoadDictionaryDataTypeDefinitions(typeDictionary, dictionary,
                    _context.NamespaceUris);
            }
        }

        /// <summary>
        /// Load data type definitions
        /// </summary>
        /// <param name="typeDictionary"></param>
        /// <param name="dictionary"></param>
        /// <param name="namespaceUris"></param>
        protected abstract void LoadDictionaryDataTypeDefinitions(Dictionary<XmlQualifiedName,
            (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)> typeDictionary,
            DataTypeDictionary dictionary, NamespaceTable namespaceUris);

        /// <summary>
        /// Load and validate the dictionary schema
        /// </summary>
        /// <param name="dictionaryId"></param>
        /// <param name="targetNamespace"></param>
        /// <param name="buffer"></param>
        /// <param name="imports"></param>
        /// <returns></returns>
        protected abstract DataTypeDictionary Load(NodeId dictionaryId, string targetNamespace,
            byte[] buffer, Dictionary<string, byte[]> imports);

        /// <summary>
        /// Add definitions
        /// </summary>
        /// <param name="encodingId"></param>
        /// <param name="typeId"></param>
        /// <param name="definition"></param>
        protected void Add(ExpandedNodeId encodingId, ExpandedNodeId typeId,
            DictionaryDataTypeDefinition definition)
        {
            _typeDefinitions[typeId] = definition;
            _typeDefinitions[encodingId] = definition;
        }

        /// <summary>
        /// Get enum definition from the types properties
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask<DictionaryDataTypeDefinition?> GetDefinitionFromPropertiesAsync(
            ExpandedNodeId nodeId, CancellationToken ct = default)
        {
            // find the property reference for the enum type
            var references = await _nodeCache.GetReferencesAsync(
                ExpandedNodeId.ToNodeId(nodeId, _context.NamespaceUris),
                ReferenceTypeIds.HasProperty, false, false, ct).ConfigureAwait(false);

            // Filter down to the supported properties
            var propertiesToRead = references.ToArray()!
                .Where(n => n.BrowseName == BrowseNames.EnumValues ||
                            n.BrowseName == BrowseNames.EnumStrings)
                .Select(n => ExpandedNodeId.ToNodeId(n.NodeId, _context.NamespaceUris))
                .ToList();
            if (references.Count == 0)
            {
                // Give up
                return null;
            }
            // read the properties
            var values = await _nodeCache.GetValuesAsync(propertiesToRead,
                ct).ConfigureAwait(false);
            EnumDefinition? enumDefinition = null;
            foreach (var value in values)
            {
                if (value == null)
                {
                    continue;
                }

                if (value.WrappedValue.TryGet(out ArrayOf<ExtensionObject> enumValueTypes))
                {
                    // use EnumValues
                    var enumValues = new EnumDefinition();
                    foreach (var extensionObject in enumValueTypes)
                    {
                        if (!extensionObject.TryGetEncodeable(out EnumValueType enumValue))
                        {
                            continue;
                        }
                        var name = enumValue.DisplayName.Text;
                        var enumTypeField = new EnumField
                        {
                            Name = name,
                            Value = enumValue.Value,
                            DisplayName = (LocalizedText)name
                        };
                        enumValues.Fields += enumTypeField;
                    }
                    if (enumValues.Fields.Count > 0)
                    {
                        // Prefer enum values, otherwise use enum strings
                        return new DictionaryDataTypeDefinition(enumValues,
                            new XmlQualifiedName(nodeId.IdentifierAsString,
                            _context.NamespaceUris.GetString(nodeId.NamespaceIndex)),
                            ExpandedNodeId.Null);
                    }
                }
                else if (value.WrappedValue.TryGet(out ArrayOf<LocalizedText> enumFieldNames))
                {
                    // Degrade to EnumStrings
                    enumDefinition ??= new EnumDefinition();
                    for (var i = 0; i < enumFieldNames.Count; i++)
                    {
                        var enumFieldName = enumFieldNames[i];
                        var name = enumFieldName.Text;

                        var enumTypeField = new EnumField
                        {
                            Name = name,
                            Value = i,
                            DisplayName = (LocalizedText)name
                        };
                        enumDefinition.Fields += enumTypeField;
                    }
                }
            }
            if (enumDefinition != null)
            {
                return new DictionaryDataTypeDefinition(enumDefinition, new XmlQualifiedName(
                    nodeId.IdentifierAsString, _context.NamespaceUris.GetString(
                        nodeId.NamespaceIndex)), ExpandedNodeId.Null);
            }
            return null;
        }

        /// <summary>
        /// Get all the data types that are referenced by the dictionary and load them
        /// into the type system lookup table. The logic follows the references from
        /// the dictionary to the referenced data types and records the encoding ids.
        /// </summary>
        /// <param name="dictionaryId"></param>
        /// <param name="targetNamespace"></param>
        /// <param name="results"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask LoadTypesAsync(NodeId dictionaryId, string targetNamespace,
            Dictionary<XmlQualifiedName,
                (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)> results,
            CancellationToken ct)
        {
            var descriptions = await _nodeCache.GetReferencesAsync(dictionaryId,
                ReferenceTypeIds.HasComponent, false, false, ct).ConfigureAwait(false);
            if (descriptions.Count == 0)
            {
                // Nothing to do
                return;
            }
            var nodeIds = descriptions
                .ConvertAll(node => ExpandedNodeId.ToNodeId(node.NodeId, _context.NamespaceUris))
                .ToList();
            //
            // DataTypeDictionaries are complex Variables which expose their Descriptions
            // as Variables using HasComponent References. A DataTypeDescription provides
            // the information necessary to find the formal description of a DataType
            // within the dictionary. The Value of a description depends on the DataTypeSystem
            // of the DataTypeDictionary. When using OPC Binary dictionaries the Value
            // shall be the name of the TypeDescription. When using XML Schema dictionaries
            // the Value shall be an Xpath expression (see XPATH) which points to an XML
            // element in the schema document.
            //
            var descriptionInfos = await _nodeCache.GetValuesAsync(nodeIds,
                ct).ConfigureAwait(false);
            var encodings = await _nodeCache.GetReferencesAsync(nodeIds,
                [ReferenceTypeIds.HasDescription], true, false, ct).ConfigureAwait(false);
            var encodingNodeIds = encodings.ToArray()!
                .Where(node => Utils.IsEqual(node.BrowseName, EncodingName)) // Filter only the encodings
                .Select(node => ExpandedNodeId.ToNodeId(node.NodeId, _context.NamespaceUris))
                .ToList();
            if (encodingNodeIds.Count != nodeIds.Count)
            {
                throw new ServiceResultException(StatusCodes.BadDataEncodingInvalid,
                    "Failed to resolve descriptions from encodings.");
            }
            Debug.Assert(descriptionInfos.Count == encodingNodeIds.Count);
            var dataTypeNodes = await _nodeCache.GetReferencesAsync(encodingNodeIds,
                [ReferenceTypeIds.HasEncoding], true, false, ct).ConfigureAwait(false);
            if (dataTypeNodes.Count != nodeIds.Count)
            {
                throw new ServiceResultException(StatusCodes.BadDataEncodingInvalid,
                    "Failed to resolve data types from encodings.");
            }
            for (var i = 0; i < dataTypeNodes.Count; i++)
            {
                var key = descriptionInfos[i];
                if (!StatusCode.IsGood(key.StatusCode))
                {
                    _logger.LogInformation("Bad data type description {NodeId} : {Status}",
                        nodeIds[i], key.StatusCode);
                    continue;
                }
                if (!key.WrappedValue.TryGet(out string typeName))
                {
                    _logger.LogInformation("Bad data type description {NodeId} : {Status}",
                        nodeIds[i], key.StatusCode);
                    continue;
                }
                var xmlName = new XmlQualifiedName(typeName, targetNamespace);
                results[xmlName] = (dataTypeNodes[i].NodeId, encodings[i].NodeId);
            }
        }

        /// <summary>
        /// A dictionary is a holder to represent the loaded dictionary
        /// </summary>
        /// <param name="DictionaryId"></param>
        /// <param name="Namespace"></param>
        /// <param name="TypeSystemId"></param>
        /// <param name="TypeSystemName"></param>
        /// <param name="TypeDictionary"></param>
        /// <param name="Schema"></param>
        internal sealed record class DataTypeDictionary(NodeId DictionaryId,
            string Namespace, NodeId TypeSystemId, QualifiedName TypeSystemName,
            Schema.Binary.TypeDictionary? TypeDictionary, System.Xml.Schema.XmlSchema? Schema);

        private readonly ConcurrentDictionary<ExpandedNodeId,
            DictionaryDataTypeDefinition> _typeDefinitions = [];
        private readonly IServiceMessageContext _context;
        private readonly INodeCache _nodeCache;
        private readonly ILogger _logger;
    }
}
