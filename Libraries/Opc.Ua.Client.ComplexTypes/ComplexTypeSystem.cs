/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using static Opc.Ua.Utils;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Manages the custom types of a server for a client session.
    /// Loads the custom types into the type factory
    /// of a client session, to allow for decoding and encoding
    /// of custom enumeration types and structured types.
    /// </summary>
    /// <remarks>
    /// Support for V1.03 dictionaries and all V1.04 data type definitions
    /// with the following known restrictions:
    /// - Support only for V1.03 structured types which can be mapped to the V1.04
    /// structured type definition. Unsupported V1.03 types are ignored.
    /// - V1.04 OptionSet does not create the enumeration flags.
    /// </remarks>
    public class ComplexTypeSystem
    {
        // an internal limit to prevent the retry
        // datatype loader mechanism to loop forever
        private const int MaxLoopCount = 100;

        #region Constructors
        /// <summary>
        /// Initializes the type system with a session to load the custom types.
        /// </summary>
        public ComplexTypeSystem(ISession session)
        {
            Initialize(new NodeCacheResolver(session), new ComplexTypeBuilderFactory());
        }

        /// <summary>
        /// Initializes the type system with a complex type resolver to load the custom types.
        /// </summary>
        public ComplexTypeSystem(IComplexTypeResolver complexTypeResolver)
        {
            Initialize(complexTypeResolver, new ComplexTypeBuilderFactory());
        }

        /// <summary>
        /// Initializes the type system with a session to load the custom types
        /// and a customized type builder factory
        /// </summary>
        public ComplexTypeSystem(
            ISession session,
            IComplexTypeFactory complexTypeBuilderFactory)
        {
            Initialize(new NodeCacheResolver(session), complexTypeBuilderFactory);
        }

        /// <summary>
        /// Initializes the type system with a complex type resolver to load the custom types.
        /// </summary>
        public ComplexTypeSystem(
            IComplexTypeResolver complexTypeResolver,
            IComplexTypeFactory complexTypeBuilderFactory)
        {
            Initialize(complexTypeResolver, complexTypeBuilderFactory);
        }

        private void Initialize(
            IComplexTypeResolver complexTypeResolver,
            IComplexTypeFactory complexTypeBuilderFactory)
        {
            m_complexTypeResolver = complexTypeResolver;
            m_complexTypeBuilderFactory = complexTypeBuilderFactory;
        }
        #endregion Constructors

        #region Public Members
        /// <summary>
        /// Load a single custom type with subtypes.
        /// </summary>
        /// <remarks>
        /// Uses inverse references on the server to find the super type(s).
        /// If the new structure contains a type dependency to a yet
        /// unknown type, it loads also the dependent type(s).
        /// For servers without DataTypeDefinition support, all
        /// custom types are loaded.
        /// </remarks>
        public async Task<Type> LoadType(ExpandedNodeId nodeId, bool subTypes = false, bool throwOnError = false)
        {
            try
            {
                // add fast path, if no subTypes are requested
                if (!subTypes)
                {
                    var systemType = GetSystemType(nodeId);
                    if (systemType != null)
                    {
                        return systemType;
                    }
                }

                // cache the server type system
                m_complexTypeResolver.LoadDataTypes(DataTypeIds.BaseDataType, true);
                var subTypeNodes = m_complexTypeResolver.LoadDataTypes(nodeId, subTypes, true);
                var subTypeNodesWithoutKnownTypes = RemoveKnownTypes(subTypeNodes);

                if (subTypeNodesWithoutKnownTypes.Count > 0)
                {
                    IList<INode> serverEnumTypes = new List<INode>();
                    IList<INode> serverStructTypes = new List<INode>();
                    foreach (var node in subTypeNodesWithoutKnownTypes)
                    {
                        AddEnumerationOrStructureType(node, serverEnumTypes, serverStructTypes);
                    }

                    // load server types
                    if (DisableDataTypeDefinition || !LoadBaseDataTypes(serverEnumTypes, serverStructTypes))
                    {
                        if (!DisableDataTypeDictionary)
                        {
                            await LoadDictionaryDataTypes(serverEnumTypes, serverStructTypes, false).ConfigureAwait(false);
                        }
                    }
                }
                return GetSystemType(nodeId);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Failed to load the custom type {0}.", nodeId);
                if (throwOnError)
                {
                    throw;
                }
                return null;
            }
        }

        /// <summary>
        /// Load all custom types of a namespace.
        /// </summary>
        /// <remarks>
        /// If a new type in the namespace contains a type dependency to an
        /// unknown type in another namespace, it loads also the dependent type(s).
        /// For servers without DataTypeDefinition support all
        /// custom types are loaded.
        /// </remarks>
        public async Task<bool> LoadNamespace(string nameSpace, bool throwOnError = false)
        {
            try
            {
                int index = m_complexTypeResolver.NamespaceUris.GetIndex(nameSpace);
                if (index < 0)
                {
                    throw new ServiceResultException($"Bad argument {nameSpace}. Namespace not found.");
                }
                ushort nameSpaceIndex = (ushort)index;
                m_complexTypeResolver.LoadDataTypes(DataTypeIds.BaseDataType, true);
                var serverEnumTypes = m_complexTypeResolver.LoadDataTypes(DataTypeIds.Enumeration);
                var serverStructTypes = m_complexTypeResolver.LoadDataTypes(DataTypeIds.Structure, true);
                // filter for namespace
                serverEnumTypes = serverEnumTypes.Where(rd => rd.NodeId.NamespaceIndex == nameSpaceIndex).ToList();
                serverStructTypes = serverStructTypes.Where(rd => rd.NodeId.NamespaceIndex == nameSpaceIndex).ToList();
                // load types
                if (DisableDataTypeDefinition || !LoadBaseDataTypes(serverEnumTypes, serverStructTypes))
                {
                    if (DisableDataTypeDictionary)
                    {
                        return false;
                    }
                    return await LoadDictionaryDataTypes(serverEnumTypes, serverStructTypes, false).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Failed to load the custom type namespace {0}.", nameSpace);
                if (throwOnError)
                {
                    throw;
                }
                return false;
            }
        }

        /// <summary>
        /// Load all custom types from a server into the session system type factory.
        /// </summary>
        /// <remarks>
        /// The loader follows the following strategy:
        /// - Load all DataType nodes of the Enumeration subtypes.
        /// - Load all DataType nodes of the Structure subtypes.
        /// - Create all enumerated custom types using the DataTypeDefinion attribute, if available.
        /// - Create all remaining enumerated custom types using the EnumValues or EnumStrings property, if available.
        /// - Create all structured types using the DataTypeDefinion attribute, if available.
        /// if there are type definitions remaining
        /// - Load the binary schema dictionaries with type definitions.
        /// - Create all remaining enumerated custom types using the dictionaries.
        /// - Convert all structured types in the dictionaries to the DataTypeDefinion attribute, if possible.
        /// - Create all structured types from the dictionaries using the converted DataTypeDefinion attribute..
        /// </remarks>
        /// <returns>true if all DataTypes were loaded.</returns>
        public async Task<bool> Load(bool onlyEnumTypes = false, bool throwOnError = false)
        {
            try
            {
                // load server types in cache
                m_complexTypeResolver.LoadDataTypes(DataTypeIds.BaseDataType, true);
                IList<INode> serverEnumTypes = m_complexTypeResolver.LoadDataTypes(DataTypeIds.Enumeration);
                IList<INode> serverStructTypes = onlyEnumTypes ? new List<INode>() : m_complexTypeResolver.LoadDataTypes(DataTypeIds.Structure, true);
                if (DisableDataTypeDefinition || !LoadBaseDataTypes(serverEnumTypes, serverStructTypes))
                {
                    if (DisableDataTypeDictionary)
                    {
                        return false;
                    }
                    return await LoadDictionaryDataTypes(serverEnumTypes, serverStructTypes, true).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Failed to load the custom types.");
                if (throwOnError)
                {
                    throw;
                }
                return false;
            }
        }

        /// <summary>
        /// Get the types defined in this type system.
        /// </summary>
        public Type[] GetDefinedTypes()
        {
            return m_complexTypeBuilderFactory.GetTypes();
        }

        /// <summary>
        /// Returns data types node ids for everything that was defined.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ExpandedNodeId> GetDefinedDataTypeIds()
        {
            return m_dataTypeDefinitionCache.Keys.Select(nodeId => NodeId.ToExpandedNodeId(nodeId, m_complexTypeResolver.NamespaceUris));
        }

        /// <summary>
        /// Get the data type definition and dependent definitions for a data type node id.
        /// Recursive through the cache to find all dependent types for strutures fields
        /// contained in the cache.
        /// </summary>
        public NodeIdDictionary<DataTypeDefinition> GetDataTypeDefinitionsForDataType(ExpandedNodeId dataTypeId)
        {
            var dataTypeDefinitions = new NodeIdDictionary<DataTypeDefinition>();

            var dataTypeNodeId = ExpandedNodeId.ToNodeId(dataTypeId, m_complexTypeResolver.NamespaceUris);
            if (!NodeId.IsNull(dataTypeNodeId))
            {
                CollectAllDataTypeDefinitions(dataTypeNodeId, dataTypeDefinitions);
            }

            return dataTypeDefinitions;

            void CollectAllDataTypeDefinitions(NodeId nodeId, NodeIdDictionary<DataTypeDefinition> collect)
            {
                if (NodeId.IsNull(nodeId))
                {
                    return;
                }

                if (m_dataTypeDefinitionCache.TryGetValue(nodeId, out var dataTypeDefinition))
                {
                    collect[nodeId] = dataTypeDefinition;

                    if (dataTypeDefinition is StructureDefinition structureDefinition)
                    {
                        foreach (StructureField field in structureDefinition.Fields)
                        {
                            if (!IsRecursiveDataType(nodeId, field.DataType) &&
                                !collect.ContainsKey(nodeId))
                            {
                                CollectAllDataTypeDefinitions(field.DataType, collect);
                            }
                        }
                    }
                }
            }
        }

        #endregion Public Members

        #region Internal Properties
        /// <summary>
        /// Disable the use of DataTypeDefinition to create the complex type definition.
        /// </summary>
        internal bool DisableDataTypeDefinition { get; set; } = false;

        /// <summary>
        /// Disable the use of DataType Dictinaries to create the complex type definition.
        /// </summary>
        internal bool DisableDataTypeDictionary { get; set; } = false;
        #endregion Internal Properties

        #region Private Members
        /// <summary>
        /// Load listed custom types from dictionaries
        /// into the sessions system type factory.
        /// </summary>
        /// <remarks>
        /// Loads all custom types at this time to avoid
        /// complexity when resolving type dependencies.
        /// </remarks>
        private async Task<bool> LoadDictionaryDataTypes(
            IList<INode> serverEnumTypes,
            IList<INode> serverStructTypes,
            bool fullTypeList
            )
        {
            // build a type dictionary with all known new types
            var allEnumTypes = fullTypeList ? serverEnumTypes : m_complexTypeResolver.LoadDataTypes(DataTypeIds.Enumeration);
            var typeDictionary = new Dictionary<XmlQualifiedName, NodeId>();

            // strip known types from list
            serverEnumTypes = RemoveKnownTypes(allEnumTypes);

            // load the binary schema dictionaries from the server
            var typeSystem = await m_complexTypeResolver.LoadDataTypeSystem().ConfigureAwait(false);

            // sort dictionaries with import dependencies to the end of the list
            var sortedTypeSystem = typeSystem.OrderBy(t => t.Value.TypeDictionary?.Import?.Length).ToList();

            bool allTypesLoaded = true;

            // create custom types for all dictionaries
            foreach (var dictionaryId in sortedTypeSystem)
            {
                try
                {
                    var dictionary = dictionaryId.Value;
                    if (dictionary.TypeDictionary == null ||
                        dictionary.TypeDictionary.Items == null)
                    {
                        continue;
                    }
                    var targetDictionaryNamespace = dictionary.TypeDictionary.TargetNamespace;
                    var targetNamespaceIndex = m_complexTypeResolver.NamespaceUris.GetIndex(targetDictionaryNamespace);
                    var structureList = new List<Schema.Binary.TypeDescription>();
                    var enumList = new List<Schema.Binary.TypeDescription>();

                    // split into enumeration and structure types and sort
                    // types with dependencies to the end of the list.
                    SplitAndSortDictionary(dictionary, structureList, enumList);

                    // create assembly for all types in the same module
                    var complexTypeBuilder = m_complexTypeBuilderFactory.Create(
                        targetDictionaryNamespace,
                        targetNamespaceIndex,
                        dictionary.Name);

                    // Add all unknown enumeration types in dictionary
                    AddEnumTypes(complexTypeBuilder, typeDictionary, enumList, allEnumTypes, serverEnumTypes);

                    // handle structures
                    int loopCounter = 0;
                    int lastStructureCount = 0;
                    while (structureList.Count > 0 &&
                        structureList.Count != lastStructureCount &&
                        loopCounter < MaxLoopCount)
                    {
                        loopCounter++;
                        lastStructureCount = structureList.Count;
                        var retryStructureList = new List<Schema.Binary.TypeDescription>();
                        // build structured types
                        foreach (var item in structureList)
                        {
                            if (item is Schema.Binary.StructuredType structuredObject)
                            {
                                NodeId nodeId = dictionary.DataTypes.FirstOrDefault(d => d.Value.Name == item.Name).Key;
                                if (nodeId == null)
                                {
                                    Utils.LogError(TraceMasks.Error, "Skip the type definition of {0} because the data type node was not found.", item.Name);
                                    continue;
                                }

                                // find the data type node and the binary encoding id
                                ExpandedNodeId typeId;
                                ExpandedNodeId binaryEncodingId;
                                DataTypeNode dataTypeNode;
                                bool newTypeDescription = m_complexTypeResolver.BrowseTypeIdsForDictionaryComponent(
                                    nodeId,
                                    out typeId,
                                    out binaryEncodingId,
                                    out dataTypeNode);

                                if (!newTypeDescription)
                                {
                                    Utils.LogError(TraceMasks.Error, "Skip the type definition of {0} because the data type node was not found.", item.Name);
                                    continue;
                                }

                                if (GetSystemType(typeId) != null)
                                {
                                    var qName = structuredObject.QName ?? new XmlQualifiedName(structuredObject.Name, targetDictionaryNamespace);
                                    typeDictionary[qName] = ExpandedNodeId.ToNodeId(typeId, m_complexTypeResolver.NamespaceUris);
                                    Utils.LogInfo("Skip the type definition of {0} because the type already exists.", item.Name);
                                    continue;
                                }

                                // Use DataTypeDefinition attribute, if available (>=V1.04)
                                StructureDefinition structureDefinition = null;
                                if (!DisableDataTypeDefinition)
                                {
                                    structureDefinition = GetStructureDefinition(dataTypeNode);
                                }
                                if (structureDefinition == null)
                                {
                                    try
                                    {
                                        // convert the binary schema description to a StructureDefinition
                                        structureDefinition = structuredObject.ToStructureDefinition(
                                            binaryEncodingId,
                                            typeDictionary,
                                            m_complexTypeResolver.NamespaceUris,
                                            dataTypeNode.NodeId);
                                    }
                                    catch (DataTypeNotSupportedException)
                                    {
                                        Utils.LogError("Skipped the type definition of {0} because it is not supported.", item.Name);
                                        continue;
                                    }
                                    catch (ServiceResultException sre)
                                    {
                                        Utils.LogError(sre, "Skip the type definition of {0}.", item.Name);
                                        continue;
                                    }
                                }

                                ExpandedNodeIdCollection missingTypeIds = null;
                                Type complexType = null;
                                if (structureDefinition != null)
                                {
                                    var encodingIds = m_complexTypeResolver.BrowseForEncodings(typeId, m_supportedEncodings,
                                        out binaryEncodingId, out ExpandedNodeId xmlEncodingId);
                                    try
                                    {
                                        // build the actual .NET structured type in assembly
                                        complexType = AddStructuredType(
                                            complexTypeBuilder,
                                            structureDefinition,
                                            dataTypeNode.BrowseName,
                                            typeId,
                                            binaryEncodingId,
                                            xmlEncodingId,
                                            out missingTypeIds
                                            );
                                    }
                                    catch (DataTypeNotSupportedException typeNotSupportedException)
                                    {
                                        Utils.LogInfo(typeNotSupportedException,
                                            "Skipped the type definition of {0} because it is not supported.", item.Name);
                                        continue;
                                    }

                                    // Add new type to factory
                                    if (complexType != null)
                                    {
                                        // match namespace and add new type to type factory
                                        foreach (var encodingId in encodingIds)
                                        {
                                            AddEncodeableType(encodingId, complexType);
                                        }
                                        AddEncodeableType(typeId, complexType);
                                        var qName = structuredObject.QName ?? new XmlQualifiedName(structuredObject.Name, targetDictionaryNamespace);
                                        typeDictionary[qName] = ExpandedNodeId.ToNodeId(typeId, m_complexTypeResolver.NamespaceUris);
                                    }
                                }

                                if (complexType == null)
                                {
                                    retryStructureList.Add(item);
                                    Utils.LogTrace("Skipped the type definition of {0}, missing {1}. Retry in next round.", item.Name, missingTypeIds?.ToString() ?? string.Empty);
                                }
                            }
                        }
                        structureList = retryStructureList;
                    }
                    allTypesLoaded = allTypesLoaded && structureList.Count == 0;
                }
                catch (ServiceResultException sre)
                {
                    Utils.LogError(sre,
                        "Unexpected error processing {0}.", dictionaryId.Value.Name);
                }
            }
            return allTypesLoaded;
        }

        /// <summary>
        /// Load all custom types with DataTypeDefinition into the type factory.
        /// </summary>
        /// <returns>true if all types were loaded, false otherwise</returns>
        private bool LoadBaseDataTypes(
            IList<INode> serverEnumTypes,
            IList<INode> serverStructTypes
            )
        {
            bool repeatDataTypeLoad = false;
            IList<INode> enumTypesToDoList = new List<INode>();
            IList<INode> structTypesToDoList = new List<INode>();

            do
            {
                // strip known types
                serverEnumTypes = RemoveKnownTypes(serverEnumTypes);
                serverStructTypes = RemoveKnownTypes(serverStructTypes);

                repeatDataTypeLoad = false;
                try
                {
                    enumTypesToDoList = LoadBaseEnumDataTypes(serverEnumTypes);
                    structTypesToDoList = LoadBaseStructureDataTypes(serverStructTypes);
                }
                catch (DataTypeNotFoundException dtnfex)
                {
                    Utils.LogWarning(dtnfex.Message);
                    foreach (var nodeId in dtnfex.NodeIds)
                    {
                        // add missing types to list
                        var dataTypeNode = m_complexTypeResolver.Find(nodeId);
                        if (dataTypeNode != null)
                        {
                            AddEnumerationOrStructureType(dataTypeNode, serverEnumTypes, serverStructTypes);
                            repeatDataTypeLoad = true;
                        }
                        else
                        {
                            Utils.LogWarning("Datatype {0} was not found.", nodeId);
                        }
                    }
                }
            } while (repeatDataTypeLoad);

            // all types loaded
            return enumTypesToDoList.Count == 0 && structTypesToDoList.Count == 0;
        }

        /// <summary>
        /// Load all custom types with DataTypeDefinition into the type factory.
        /// </summary>
        /// <returns>true if all types were loaded, false otherwise</returns>
        private IList<INode> LoadBaseEnumDataTypes(
            IList<INode> serverEnumTypes
            )
        {
            // strip known types
            serverEnumTypes = RemoveKnownTypes(serverEnumTypes);

            // add new enum Types for all namespaces
            var enumTypesToDoList = new List<INode>();
            int namespaceCount = m_complexTypeResolver.NamespaceUris.Count;

            // create enumeration types for all namespaces
            for (uint i = 0; i < namespaceCount; i++)
            {
                IComplexTypeBuilder complexTypeBuilder = null;
                var enumTypes = serverEnumTypes.Where(node => node.NodeId.NamespaceIndex == i).ToList();
                if (enumTypes.Count != 0)
                {
                    if (complexTypeBuilder == null)
                    {
                        string targetNamespace = m_complexTypeResolver.NamespaceUris.GetString(i);
                        complexTypeBuilder = m_complexTypeBuilderFactory.Create(
                            targetNamespace,
                            (int)i);
                    }
                    foreach (var enumType in enumTypes)
                    {
                        var newType = AddEnumType(complexTypeBuilder, enumType as DataTypeNode);
                        if (newType != null)
                        {
                            // match namespace and add to type factory
                            AddEncodeableType(enumType.NodeId, newType);
                        }
                        else
                        {
                            enumTypesToDoList.Add(enumType);
                        }
                    }
                }
            }

            // all types loaded, return remaining
            return enumTypesToDoList;
        }

        /// <summary>
        /// Load all structure custom types with DataTypeDefinition into the type factory.
        /// </summary>
        /// <returns>true if all types were loaded, false otherwise</returns>
        private IList<INode> LoadBaseStructureDataTypes(
            IList<INode> serverStructTypes
            )
        {
            // strip known types
            serverStructTypes = RemoveKnownTypes(serverStructTypes);

            // add new enum Types for all namespaces
            int namespaceCount = m_complexTypeResolver.NamespaceUris.Count;

            bool retryAddStructType;
            var structTypesToDoList = new List<INode>();
            var structTypesWorkList = serverStructTypes;

            // allow the loader to cache the encodings
            IList<ExpandedNodeId> nodeIds = serverStructTypes.Select(n => n.NodeId).ToList();
            m_complexTypeResolver.BrowseForEncodings(nodeIds, m_supportedEncodings);

            // create structured types for all namespaces
            int loopCounter = 0;
            do
            {
                loopCounter++;
                retryAddStructType = false;
                for (uint i = 0; i < namespaceCount; i++)
                {
                    IComplexTypeBuilder complexTypeBuilder = null;
                    var structTypes = structTypesWorkList.Where(node => node.NodeId.NamespaceIndex == i).ToList();
                    if (structTypes.Count != 0)
                    {
                        if (complexTypeBuilder == null)
                        {
                            string targetNamespace = m_complexTypeResolver.NamespaceUris.GetString(i);
                            complexTypeBuilder = m_complexTypeBuilderFactory.Create(
                                targetNamespace,
                                (int)i);
                        }
                        foreach (INode structType in structTypes)
                        {
                            Type newType = null;
                            if (!(structType is DataTypeNode dataTypeNode) ||
                                dataTypeNode.IsAbstract)
                            {
                                continue;
                            }

                            var structureDefinition = GetStructureDefinition(dataTypeNode);
                            if (structureDefinition != null)
                            {
                                var encodingIds = m_complexTypeResolver.BrowseForEncodings(structType.NodeId, m_supportedEncodings,
                                    out ExpandedNodeId binaryEncodingId, out ExpandedNodeId xmlEncodingId);
                                try
                                {
                                    ExpandedNodeId typeId = NormalizeExpandedNodeId(structType.NodeId);
                                    newType = AddStructuredType(
                                        complexTypeBuilder,
                                        structureDefinition,
                                        dataTypeNode.BrowseName,
                                        typeId,
                                        binaryEncodingId,
                                        xmlEncodingId,
                                        out ExpandedNodeIdCollection missingTypeIds
                                        );

                                    if (missingTypeIds?.Count > 0)
                                    {
                                        var missingTypeIdsFromWorkList = new ExpandedNodeIdCollection();
                                        foreach (var missingTypeId in missingTypeIds)
                                        {
                                            var typeMatch = structTypesWorkList.FirstOrDefault(n => n.NodeId == missingTypeId);
                                            if (typeMatch == null)
                                            {
                                                missingTypeIdsFromWorkList.Add(missingTypeId);
                                            }
                                        }
                                        foreach (var id in missingTypeIdsFromWorkList)
                                        {
                                            if (!structTypesToDoList.Where(n => n.NodeId == id).Any())
                                            {
                                                structTypesToDoList.Add(m_complexTypeResolver.Find(id));
                                            }
                                            retryAddStructType = true;
                                        }
                                    }
                                }
                                catch (DataTypeNotSupportedException)
                                {
                                    Utils.LogError("Skipped the type definition of {0} because it is not supported.", dataTypeNode.BrowseName.Name);
                                }
                                catch
                                {
                                    // creating the new type failed, likely a missing dependency, retry later
                                    retryAddStructType = true;
                                }

                                if (newType != null)
                                {
                                    foreach (var encodingId in encodingIds)
                                    {
                                        AddEncodeableType(encodingId, newType);
                                    }
                                    AddEncodeableType(structType.NodeId, newType);
                                }
                            }

                            if (newType == null)
                            {
                                structTypesToDoList.Add(structType);
                                if (structureDefinition != null)
                                {
                                    retryAddStructType = true;
                                }
                            }
                        }
                    }
                }
                // due to type dependencies, retry missing types until there is no more progress
                if (retryAddStructType &&
                    structTypesWorkList.Count != structTypesToDoList.Count &&
                    loopCounter < MaxLoopCount)
                {
                    structTypesWorkList = structTypesToDoList;
                    structTypesToDoList = new List<INode>();
                }
                else
                {
                    break;
                }

            } while (retryAddStructType);

            // all types loaded
            return structTypesToDoList;
        }

        /// <summary>
        /// Return the structure definition from a DataTypeDefinition
        /// </summary>
        private StructureDefinition GetStructureDefinition(DataTypeNode dataTypeNode)
        {
            if (dataTypeNode.DataTypeDefinition?.Body is StructureDefinition structureDefinition)
            {
                // Validate the DataTypeDefinition structure,
                // but not if the type is supported
                if (structureDefinition.Fields == null ||
                    structureDefinition.BaseDataType.IsNullNodeId ||
                    structureDefinition.BinaryEncodingId.IsNull)
                {
                    return null;
                }
                // Validate the structure according to Part3, Table 36
                foreach (var field in structureDefinition.Fields)
                {
                    // validate if the DataTypeDefinition is correctly
                    // filled out, some servers don't do it yet...
                    if (field.BinaryEncodingId.IsNull ||
                        field.DataType.IsNullNodeId ||
                        field.TypeId.IsNull ||
                        field.Name == null)
                    {
                        return null;
                    }
                    if (!(field.ValueRank == ValueRanks.Scalar ||
                        field.ValueRank >= ValueRanks.OneDimension))
                    {
                        return null;
                    }
                }
                return structureDefinition;
            }
            return null;
        }

        /// <summary>
        /// Helper to ensure the expanded nodeId contains a valid namespaceUri.
        /// </summary>
        /// <param name="expandedNodeId">The expanded nodeId.</param>
        /// <returns>The normalized expanded nodeId.</returns>
        private ExpandedNodeId NormalizeExpandedNodeId(ExpandedNodeId expandedNodeId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, m_complexTypeResolver.NamespaceUris);
            return NodeId.ToExpandedNodeId(nodeId, m_complexTypeResolver.NamespaceUris);
        }

        /// <summary>
        /// Add data type to enumeration or structure base type list depending on supertype.
        /// </summary>
        private void AddEnumerationOrStructureType(INode dataTypeNode, IList<INode> serverEnumTypes, IList<INode> serverStructTypes)
        {
            NodeId superType = ExpandedNodeId.ToNodeId(dataTypeNode.NodeId, m_complexTypeResolver.NamespaceUris);
            while (true)
            {
                superType = m_complexTypeResolver.FindSuperType(superType);
                if (superType.IsNullNodeId)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, $"SuperType for {dataTypeNode.NodeId} not found.");
                }
                if (superType == DataTypeIds.Enumeration)
                {
                    serverEnumTypes.Insert(0, dataTypeNode);
                    break;
                }
                else if (superType == DataTypeIds.Structure)
                {
                    serverStructTypes.Insert(0, dataTypeNode);
                    break;
                }
                else if (TypeInfo.GetBuiltInType(superType) != BuiltInType.Null)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Remove all known types in the type factory from a list of DataType nodes.
        /// </summary>
        private IList<INode> RemoveKnownTypes(IList<INode> nodeList)
        {
            return nodeList.Where(
                node => GetSystemType(node.NodeId) == null).Distinct().ToList();
        }

        /// <summary>
        /// Get the factory system type for an expanded node id.
        /// </summary>
        private Type GetSystemType(ExpandedNodeId nodeId)
        {
            if (!nodeId.IsAbsolute)
            {
                nodeId = NormalizeExpandedNodeId(nodeId);
            }
            return m_complexTypeResolver.Factory.GetSystemType(nodeId);
        }

        /// <summary>
        /// Add an enum type defined in a binary schema dictionary.
        /// </summary>
        private void AddEnumTypes(
            IComplexTypeBuilder complexTypeBuilder,
            Dictionary<XmlQualifiedName, NodeId> typeDictionary,
            IList<Schema.Binary.TypeDescription> enumList,
            IList<INode> allEnumerationTypes,
            IList<INode> enumerationTypes)
        {
            foreach (var item in enumList)
            {
                Type newType = null;
                DataTypeNode enumDescription = null;
                DataTypeNode enumType = enumerationTypes.FirstOrDefault(node =>
                    node.BrowseName.Name == item.Name &&
                    (node.BrowseName.NamespaceIndex == complexTypeBuilder.TargetNamespaceIndex ||
                    complexTypeBuilder.TargetNamespaceIndex == -1))
                    as DataTypeNode;
                if (enumType != null)
                {
                    enumDescription = enumType;

                    // try dictionary enum definition
                    if (item is Schema.Binary.EnumeratedType enumeratedObject)
                    {
                        // 1. use Dictionary entry
                        var enumDefinition = enumeratedObject.ToEnumDefinition();

                        // Add EnumDefinition to cache
                        m_dataTypeDefinitionCache[enumType.NodeId] = enumDefinition;

                        newType = complexTypeBuilder.AddEnumType(enumeratedObject.Name, enumDefinition);
                    }
                    if (newType == null)
                    {
                        // 2. use node cache
                        var dataTypeNode = m_complexTypeResolver.Find(enumType.NodeId) as DataTypeNode;
                        newType = AddEnumType(complexTypeBuilder, dataTypeNode);
                    }
                }
                else
                {
                    enumDescription = allEnumerationTypes.FirstOrDefault(node =>
                        node.BrowseName.Name == item.Name &&
                        (node.BrowseName.NamespaceIndex == complexTypeBuilder.TargetNamespaceIndex ||
                        complexTypeBuilder.TargetNamespaceIndex == -1))
                        as DataTypeNode;
                }
                if (enumDescription != null)
                {
                    var qName = new XmlQualifiedName(item.Name, complexTypeBuilder.TargetNamespace);
                    typeDictionary[qName] = enumDescription.NodeId;
                }
                if (newType != null)
                {
                    // match namespace and add to type factory
                    AddEncodeableType(enumType.NodeId, newType);
                }
            }
        }

        /// <summary>
        /// Helper to add new type with absolute ExpandedNodeId.
        /// </summary>
        private void AddEncodeableType(ExpandedNodeId nodeId, Type type)
        {
            if (NodeId.IsNull(nodeId) || type == null)
            {
                return;
            }
            var internalNodeId = NormalizeExpandedNodeId(nodeId);
            Utils.LogDebug("Adding Type {0} as: {1}", type.FullName, internalNodeId);
            m_complexTypeResolver.Factory.AddEncodeableType(internalNodeId, type);
        }

        /// <summary>
        /// Add an enum type defined in a DataType node.
        /// </summary>
        private Type AddEnumType(
            IComplexTypeBuilder complexTypeBuilder,
            DataTypeNode enumTypeNode
            )
        {
            Type newType = null;
            if (enumTypeNode != null)
            {
                QualifiedName name = enumTypeNode.BrowseName;

                // 1. use DataTypeDefinition
                if (DisableDataTypeDefinition ||
                    !(enumTypeNode.DataTypeDefinition?.Body is EnumDefinition enumDefinition))
                {
                    // browse for EnumFields or EnumStrings property
                    object enumTypeArray = m_complexTypeResolver.GetEnumTypeArray(enumTypeNode.NodeId);
                    if (enumTypeArray is ExtensionObject[] extensionObject)
                    {
                        // 2. use EnumValues
                        enumDefinition = extensionObject.ToEnumDefinition();
                    }
                    else if (enumTypeArray is LocalizedText[] localizedText)
                    {
                        // 3. use EnumStrings
                        enumDefinition = localizedText.ToEnumDefinition();
                    }
                    else
                    {
                        // 4. Give up
                        enumDefinition = null;
                    }
                }

                if (enumDefinition != null)
                {
                    // Add EnumDefinition to cache
                    m_dataTypeDefinitionCache[enumTypeNode.NodeId] = enumDefinition;

                    newType = complexTypeBuilder.AddEnumType(name, enumDefinition);
                }
            }
            return newType;
        }

        /// <summary>
        /// Add structured type to assembly with StructureDefinition.
        /// </summary>
        private Type AddStructuredType(
            IComplexTypeBuilder complexTypeBuilder,
            StructureDefinition structureDefinition,
            QualifiedName typeName,
            ExpandedNodeId complexTypeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId,
            out ExpandedNodeIdCollection missingTypes
            )
        {
            // init missing type list
            missingTypes = null;

            var localDataTypeId = ExpandedNodeId.ToNodeId(complexTypeId, m_complexTypeResolver.NamespaceUris);
            bool allowSubTypes = IsAllowSubTypes(structureDefinition);

            // check all types
            var typeList = new List<Type>();
            foreach (StructureField field in structureDefinition.Fields)
            {
                Type newType = GetFieldType(field, allowSubTypes);
                if (newType == null &&
                    !IsRecursiveDataType(localDataTypeId, field.DataType))
                {
                    if (missingTypes == null)
                    {
                        missingTypes = new ExpandedNodeIdCollection() { field.DataType };
                    }
                    else if (!missingTypes.Contains(field.DataType))
                    {
                        missingTypes.Add(field.DataType);
                    }
                }
                else
                {
                    typeList.Add(newType);
                }
            }

            if (missingTypes != null)
            {
                return null;
            }

            // Add StructureDefinition to cache
            m_dataTypeDefinitionCache[localDataTypeId] = structureDefinition;

            var fieldBuilder = complexTypeBuilder.AddStructuredType(
                typeName,
                structureDefinition
                );

            fieldBuilder.AddTypeIdAttribute(complexTypeId, binaryEncodingId, xmlEncodingId);

            int order = 1;
            var typeListEnumerator = typeList.GetEnumerator();
            foreach (StructureField field in structureDefinition.Fields)
            {
                typeListEnumerator.MoveNext();

                // check for recursive data type:
                //    field has the same data type as the parent structure
                var nodeId = ExpandedNodeId.ToNodeId(complexTypeId, m_complexTypeResolver.NamespaceUris);
                var isRecursiveDataType = IsRecursiveDataType(nodeId, field.DataType);
                if (isRecursiveDataType)
                {
                    fieldBuilder.AddField(field, fieldBuilder.GetStructureType(field.ValueRank), order);
                }
                else
                {
                    fieldBuilder.AddField(field, typeListEnumerator.Current, order);
                }
                order++;
            }

            return fieldBuilder.CreateType();
        }

        bool IsAllowSubTypes(StructureDefinition structureDefinition)
        {
            switch (structureDefinition.StructureType)
            {
                case StructureType.UnionWithSubtypedValues:
                case StructureType.StructureWithSubtypedValues:
                    return true;
            }
            return false;
        }

        private bool IsAbstractType(NodeId fieldDataType)
        {
            var dataTypeNode = m_complexTypeResolver.Find(fieldDataType) as DataTypeNode;
            return dataTypeNode?.IsAbstract == true;
        }

        private bool IsRecursiveDataType(NodeId structureDataType, NodeId fieldDataType)
            => fieldDataType.Equals(structureDataType);

        /// <summary>
        /// Determine the type of a field in a StructureField definition.
        /// </summary>
        private Type GetFieldType(StructureField field, bool allowSubTypes)
        {
            if (field.ValueRank != ValueRanks.Scalar &&
                field.ValueRank < ValueRanks.OneDimension)
            {
                throw new DataTypeNotSupportedException(field.DataType, $"The ValueRank {field.ValueRank} is not supported.");
            }

            Type fieldType = field.DataType.NamespaceIndex == 0 ?
                TypeInfo.GetSystemType(field.DataType, m_complexTypeResolver.Factory) :
                GetSystemType(field.DataType);

            if (fieldType == null)
            {
                var superType = GetBuiltInSuperType(field.DataType, allowSubTypes, field.IsOptional);
                if (superType?.IsNullNodeId == false)
                {
                    field.DataType = superType;
                    return GetFieldType(field, allowSubTypes);
                }
                return null;
            }

            if (field.ValueRank == ValueRanks.OneDimension)
            {
                fieldType = fieldType.MakeArrayType();
            }
            else if (field.ValueRank >= ValueRanks.TwoDimensions)
            {
                fieldType = fieldType.MakeArrayType(field.ValueRank);
            }

            return fieldType;
        }

        /// <summary>
        /// Find superType for a datatype.
        /// </summary>
        private NodeId GetBuiltInSuperType(NodeId dataType, bool allowSubTypes, bool isOptional)
        {
            NodeId superType = dataType;
            while (true)
            {
                superType = m_complexTypeResolver.FindSuperType(superType);
                if (superType?.IsNullNodeId != false)
                {
                    return null;
                }
                if (superType.NamespaceIndex == 0)
                {
                    if (superType == DataTypeIds.Enumeration &&
                        dataType.NamespaceIndex == 0)
                    {
                        // enumerations of namespace 0 in a structure
                        // which are not in the type system are encoded as UInt32
                        return new NodeId((uint)BuiltInType.UInt32);
                    }
                    if (superType == DataTypeIds.Enumeration)
                    {
                        return null;
                    }
                    else if (superType == DataTypeIds.Structure)
                    {
                        // throw on invalid combinations of allowSubTypes, isOptional and abstract types
                        // in such case the encoding as ExtensionObject is undetermined and not specified
                        if ((dataType != DataTypeIds.Structure) &&
                            ((allowSubTypes && !isOptional) || !allowSubTypes) &&
                            IsAbstractType(dataType))
                        {
                            throw new DataTypeNotSupportedException("Invalid definition of a abstract subtype of a structure.");
                        }

                        if (allowSubTypes && isOptional)
                        {
                            return superType;
                        }
                        return null;
                    }
                    break;
                }
            }
            return superType;
        }

        /// <summary>
        /// Split the dictionary types into a list of structures and enumerations.
        /// Sort the structures by dependencies, with structures with dependent
        /// types at the end of the list, so they can be added to the factory in order.
        /// </summary>
        private void SplitAndSortDictionary(
            DataDictionary dictionary,
            List<Schema.Binary.TypeDescription> structureList,
            List<Schema.Binary.TypeDescription> enumList
            )
        {
            foreach (var item in dictionary.TypeDictionary.Items)
            {
                if (item is Schema.Binary.StructuredType structuredObject)
                {
                    var dependentFields = structuredObject.Field.Where(f => f.TypeName.Namespace == dictionary.TypeDictionary.TargetNamespace);
                    if (!dependentFields.Any())
                    {
                        structureList.Insert(0, structuredObject);
                    }
                    else
                    {
                        structureList.Add(structuredObject);
                    }
                }
                else if (item is Schema.Binary.EnumeratedType)
                {
                    enumList.Add(item);
                }
                else if (item is Schema.Binary.OpaqueType)
                {
                    // no need to handle Opaque types
                }
                else
                {
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "Unexpected Type in binary schema: {0}.", item.GetType().Name);
                }
            }
        }
        #endregion Private Members

        #region Private Fields
        private IComplexTypeResolver m_complexTypeResolver;
        private IComplexTypeFactory m_complexTypeBuilderFactory;
        private NodeIdDictionary<DataTypeDefinition> m_dataTypeDefinitionCache = new NodeIdDictionary<DataTypeDefinition>();
        private static readonly string[] m_supportedEncodings = new string[] { BrowseNames.DefaultBinary, BrowseNames.DefaultXml, BrowseNames.DefaultJson };
        #endregion Private Fields
    }
}//namespace
