/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
        #region Constructors
        /// <summary>
        /// Initializes the type system with a session to load the custom types.
        /// </summary>
        public ComplexTypeSystem(Session session)
        {
            Initialize(session, new ComplexTypeBuilderFactory());
        }

        /// <summary>
        /// Initializes the type system with a session to load the custom types
        /// and a customized type builder factory
        /// </summary>
        public ComplexTypeSystem(
            Session session,
            IComplexTypeFactory complexTypeBuilderFactory)
        {
            Initialize(session, complexTypeBuilderFactory);
        }

        private void Initialize(
            Session session,
            IComplexTypeFactory complexTypeBuilderFactory)
        {
            m_session = session;
            m_complexTypeBuilderFactory = complexTypeBuilderFactory;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Load a single custom type with subtypes.
        /// </summary>
        /// <remarks>
        /// Type dependencies are not resolved. If the new structure contains an
        /// unknown type, the load fails. In such a case the caller should proceed
        /// with a generic load of the whole type system.
        /// </remarks>
        public async Task<Type> LoadType(ExpandedNodeId nodeId, bool subTypes = false, bool throwOnError = false)
        {
            try
            {
                var subTypeNodes = LoadDataTypes(nodeId, true, true);
                var subTypeNodesWithoutKnownTypes = RemoveKnownTypes(subTypeNodes);

                if (subTypeNodesWithoutKnownTypes.Count > 0)
                {
                    IList<INode> serverEnumTypes = new List<INode>();
                    IList<INode> serverStructTypes = serverEnumTypes;
                    var superType = m_session.NodeCache.FindSuperType(nodeId);
                    if (superType == DataTypeIds.Enumeration)
                    {
                        serverEnumTypes = subTypeNodesWithoutKnownTypes;
                    }
                    else
                    {
                        serverStructTypes = subTypeNodesWithoutKnownTypes;
                    }

                    // load server types
                    LoadBaseDataTypes(serverEnumTypes, serverStructTypes);
                    await LoadDictionaryDataTypes(serverEnumTypes, serverStructTypes);
                }
                return GetSystemType(nodeId);
            }
            catch (ServiceResultException sre)
            {
                Utils.Trace(sre, "Failed to load the custom type.");
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
        /// Type dependencies with other namespaces are not resolved. 
        /// If a new structure contains an unknown enum or structured 
        /// type from another namespace, loading such types fails. 
        /// To load such a type the caller must load the whole type system.
        /// </remarks>
        public async Task<bool> LoadNamespace(string nameSpace, bool throwOnError = false)
        {
            try
            {
                int index = m_session.NamespaceUris.GetIndex(nameSpace);
                if (index < 0)
                {
                    throw new ServiceResultException($"Bad argument {nameSpace}. Namespace not found.");
                }
                ushort nameSpaceIndex = (ushort)index;
                var serverEnumTypes = LoadDataTypes(DataTypeIds.Enumeration);
                var serverStructTypes = LoadDataTypes(DataTypeIds.Structure, true);
                // filter for namespace
                serverEnumTypes = serverEnumTypes.Where(rd => rd.NodeId.NamespaceIndex == nameSpaceIndex).ToList();
                serverStructTypes = serverStructTypes.Where(rd => rd.NodeId.NamespaceIndex == nameSpaceIndex).ToList();
                // load types
                LoadBaseDataTypes(serverEnumTypes, serverStructTypes);
                await LoadDictionaryDataTypes(serverEnumTypes, serverStructTypes);
                return true;
            }
            catch (ServiceResultException sre)
            {
                Utils.Trace(sre, $"Failed to load the custom type dictionary.");
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
        /// - Create all enumerated custom types using the dictionaries.
        /// - Convert all structured types in the dictionaries to the DataTypeDefinion attribute, if possible.
        /// - Create all structured types from the dictionaries using the DataTypeDefinion attribute..
        /// </remarks>
        public async Task<bool> Load(bool onlyEnumTypes = false, bool throwOnError = false)
        {
            try
            {
                // load server types
                IList<INode> serverEnumTypes = LoadDataTypes(DataTypeIds.Enumeration);
                IList<INode> serverStructTypes;
                if (onlyEnumTypes)
                {
                    serverStructTypes = new List<INode>();
                }
                else
                {
                    serverStructTypes = LoadDataTypes(DataTypeIds.Structure, true);
                }
                LoadBaseDataTypes(serverEnumTypes, serverStructTypes);
                await LoadDictionaryDataTypes(serverEnumTypes, serverStructTypes);
                return true;
            }
            catch (ServiceResultException sre)
            {
                Utils.Trace(sre, $"Failed to load the custom type dictionary.");
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
        #endregion

        #region Private Members
        /// <summary>
        /// Load all custom types from dictionaries into the sessions system type factory.
        /// </summary>
        private async Task LoadDictionaryDataTypes(
            IList<INode> serverEnumTypes,
            IList<INode> serverStructTypes
            )
        {
            // build a type dictionary with all new types
            var allTypes = new List<INode>();
            allTypes.AddRange(serverEnumTypes);
            allTypes.AddRange(serverStructTypes);

            // strip known types from the work list
            serverEnumTypes = RemoveKnownTypes(serverEnumTypes);

            // load the binary schema dictionaries from the server
            var typeSystem = await m_session.LoadDataTypeSystem();

            // sort dictionaries with import dependencies to the end of the list
            var sortedTypeSystem = typeSystem.OrderBy(t => t.Value.TypeDictionary.Import?.Count()).ToList();

            // create custom types for all dictionaries
            foreach (var dictionaryId in sortedTypeSystem)
            {
                try
                {
                    var dictionary = dictionaryId.Value;
                    var targetNamespace = dictionary.TypeDictionary.TargetNamespace;
                    var structureList = new List<Schema.Binary.TypeDescription>();
                    var enumList = new List<Opc.Ua.Schema.Binary.TypeDescription>();

                    // split into enumeration and structure types and sort
                    // types with dependencies to the end of the list.
                    SplitAndSortDictionary(dictionary, structureList, enumList);

                    // create assembly for all types in the same module
                    var complexTypeBuilder = m_complexTypeBuilderFactory.Create(
                        targetNamespace,
                        m_session.NamespaceUris.GetIndex(targetNamespace),
                        dictionary.Name);

                    // Add all unknown enumeration types 
                    AddEnumTypes(complexTypeBuilder, enumList, serverEnumTypes);

                    int lastStructureCount = 0;
                    while (structureList.Count > 0 &&
                        structureList.Count != lastStructureCount)
                    {
                        lastStructureCount = structureList.Count;
                        var retryStructureList = new List<Schema.Binary.TypeDescription>();
                        // build structured types
                        foreach (var item in structureList)
                        {
                            var structuredObject = item as Opc.Ua.Schema.Binary.StructuredType;
                            if (structuredObject != null)
                            {
                                var nodeId = dictionary.DataTypes.FirstOrDefault(d => d.Value.DisplayName == item.Name).Value;

                                // find the data type node and the binary encoding id
                                ExpandedNodeId typeId;
                                ExpandedNodeId binaryEncodingId;
                                DataTypeNode dataTypeNode;
                                bool newTypeDescription = BrowseTypeIdsForDictionaryComponent(
                                    ExpandedNodeId.ToNodeId(nodeId.NodeId, m_session.NamespaceUris),
                                    out typeId,
                                    out binaryEncodingId,
                                    out dataTypeNode);

                                if (!newTypeDescription)
                                {
                                    Utils.Trace(TraceMasks.Error, $"Skip the type definition of {item.Name} because the data type node was not found.");
                                    continue;
                                }

                                if (GetSystemType(typeId) != null)
                                {
                                    Utils.Trace(TraceMasks.Information, $"Skip the type definition of {item.Name} because the type already exists.");
                                    continue;
                                }

                                // Use DataTypeDefinition attribute, if available (>=V1.04)
                                StructureDefinition structureDefinition = dataTypeNode.DataTypeDefinition?.Body as StructureDefinition;
                                if (structureDefinition == null)
                                {
                                    try
                                    {
                                        // convert the binary schema description to a StructureDefinition
                                        structureDefinition = structuredObject.ToStructureDefinition(
                                            binaryEncodingId,
                                            allTypes,
                                            m_session.NamespaceUris);
                                    }
                                    catch (ServiceResultException sre)
                                    {
                                        Utils.Trace(sre, $"Skip the type definition of {item.Name}.");
                                        continue;
                                    }
                                }

                                Type complexType = null;
                                if (structureDefinition != null)
                                {
                                    // build the actual structured type in an assembly
                                    complexType = AddStructuredType(
                                        complexTypeBuilder,
                                        structureDefinition,
                                        dataTypeNode.BrowseName.Name,
                                        typeId,
                                        binaryEncodingId);

                                    // Add new type to factory
                                    if (complexType != null)
                                    {
                                        AddEncodeableType(binaryEncodingId, complexType);
                                        AddEncodeableType(typeId, complexType);
                                    }
                                }

                                if (complexType == null)
                                {
                                    retryStructureList.Add(item);
                                    Utils.Trace(TraceMasks.Error, $"Skipped the type definition of {item.Name}. Retry in next round.");
                                }
                            }
                        }
                        structureList = retryStructureList;
                    }
                }
                catch (ServiceResultException sre)
                {
                    Utils.Trace(sre,
                        $"Unexpected error processing {dictionaryId.Value.Name}.");
                }
            }
        }

        /// <summary>
        /// Load all custom types with DataTypeDefinition into the type factory.
        /// </summary>
        private bool LoadBaseDataTypes(
            IList<INode> serverEnumTypes,
            IList<INode> serverStructTypes
            )
        {
            // strip known types
            serverEnumTypes = RemoveKnownTypes(serverEnumTypes);
            serverStructTypes = RemoveKnownTypes(serverStructTypes);

            // add new enum Types for all namespaces
            var enumTypesToDoList = new List<INode>();
            int namespaceCount = m_session.NamespaceUris.Count;

            // create enumeration types for all namespaces
            for (uint i = 0; i < namespaceCount; i++)
            {
                IComplexTypeBuilder complexTypeBuilder = null;
                var enumTypes = serverEnumTypes.Where(node => node.NodeId.NamespaceIndex == i).ToList();
                if (enumTypes.Count != 0)
                {
                    if (complexTypeBuilder == null)
                    {
                        string targetNamespace = m_session.NamespaceUris.GetString(i);
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

            bool retryAddStructType;
            var structTypesToDoList = new List<INode>();
            var structTypesWorkList = serverStructTypes;

            // create structured types for all namespaces
            do
            {
                retryAddStructType = false;
                for (uint i = 0; i < namespaceCount; i++)
                {
                    IComplexTypeBuilder complexTypeBuilder = null;
                    var structTypes = structTypesWorkList.Where(node => node.NodeId.NamespaceIndex == i).ToList();
                    if (structTypes.Count != 0)
                    {
                        if (complexTypeBuilder == null)
                        {
                            string targetNamespace = m_session.NamespaceUris.GetString(i);
                            complexTypeBuilder = m_complexTypeBuilderFactory.Create(
                                targetNamespace,
                                (int)i);
                        }
                        foreach (INode structType in structTypes)
                        {
                            Type newType = null;
                            var dataTypeNode = structType as DataTypeNode;
                            if (dataTypeNode == null)
                            {
                                continue;
                            }
                            var structureDefinition = dataTypeNode.DataTypeDefinition?.Body as StructureDefinition;
                            if (structureDefinition != null)
                            {
                                try
                                {
                                    newType = AddStructuredType(
                                        complexTypeBuilder,
                                        structureDefinition,
                                        dataTypeNode.BrowseName.Name,
                                        structType.NodeId,
                                        structureDefinition.DefaultEncodingId
                                        );
                                }
                                catch
                                {
                                    // creating the new type failed, likely a missing dependency, retry later
                                    retryAddStructType = true;
                                }
                                if (newType != null)
                                {
                                    // match namespace and add new type to type factory
                                    AddEncodeableType(structureDefinition.DefaultEncodingId, newType);
                                    AddEncodeableType(structType.NodeId, newType);
                                }
                            }
                            if (newType == null)
                            {
                                structTypesToDoList.Add(structType);
                            }
                        }
                    }
                }
                // due to type dependencies, retry missing types until there is no more progress
                if (retryAddStructType &&
                    structTypesWorkList.Count != structTypesToDoList.Count)
                {
                    structTypesWorkList = structTypesToDoList;
                    structTypesToDoList = new List<INode>();
                }
            } while (retryAddStructType);

            return structTypesToDoList.Count == 0;
        }


        /// <summary>
        /// Helper to ensure the expanded nodeId contains a valid namespaceUri.
        /// </summary>
        /// <param name="expandedNodeId">The expanded nodeId.</param>
        /// <param name="namespaceTable">The session namespace table.</param>
        /// <returns>The normalized expanded nodeId.</returns>
        private ExpandedNodeId NormalizeExpandedNodeId(ExpandedNodeId expandedNodeId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, m_session.NamespaceUris);
            return NodeId.ToExpandedNodeId(nodeId, m_session.NamespaceUris);
        }

        /// <summary>
        /// Browse for the type and encoding id for a dictionary component.
        /// </summary>
        /// <remarks>
        /// To find the typeId and encodingId for a dictionary type definition:
        /// i) inverse browse the description to get the encodingid
        /// ii) from the description inverse browse for encoding 
        /// to get the subtype typeid 
        /// iii) load the DataType node 
        /// </remarks>
        /// <param name="nodeId"></param>
        /// <param name="typeId"></param>
        /// <param name="encodingId"></param>
        /// <returns></returns>
        private bool BrowseTypeIdsForDictionaryComponent(
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

        /// <summary>
        /// Browse for the property.
        /// </summary>
        /// <remarks>
        /// Browse for property (type description) of an enum datatype.
        /// </remarks>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        private INode BrowseForSingleProperty(
            ExpandedNodeId nodeId)
        {
            var references = m_session.NodeCache.FindReferences(
                nodeId,
                ReferenceTypeIds.HasProperty,
                false,
                false
                );
            return references.FirstOrDefault();
        }

        /// <summary>
        /// Load all subTypes and optionally nested subtypes of a type definition.
        /// Filter for all subtypes or only subtypesoutside the default namespace.
        /// </summary>
        private IList<INode> LoadDataTypes(
            ExpandedNodeId dataType,
            bool nestedSubTypes = false,
            bool addRootNode = false,
            bool filterUATypes = true)
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection();
            nodesToBrowse.Add(dataType);

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

        /// <summary>
        /// Remove all known types in the type factory from a list of DataType nodes.
        /// </summary>
        private IList<INode> RemoveKnownTypes(IList<INode> nodeList)
        {
            return nodeList.Where(
                node => GetSystemType(node.NodeId) == null).ToList();
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
            return m_session.Factory.GetSystemType(nodeId);
        }

        /// <summary>
        /// Add an enum type defined in a binary schema dictionary.
        /// </summary>
        private void AddEnumTypes(
            IComplexTypeBuilder complexTypeBuilder,
            IList<Opc.Ua.Schema.Binary.TypeDescription> enumList,
            IList<INode> enumerationTypes
            )
        {
            foreach (var item in enumList)
            {
                Type newType = null;
                DataTypeNode enumType = enumerationTypes.FirstOrDefault(node =>
                    node.BrowseName.Name == item.Name &&
                    (node.NodeId.NamespaceIndex == complexTypeBuilder.TargetNamespaceIndex ||
                    complexTypeBuilder.TargetNamespaceIndex == -1))
                    as DataTypeNode;
                if (enumType != null)
                {
                    // try dictionary enum definition
                    var enumeratedObject = item as Schema.Binary.EnumeratedType;
                    if (enumeratedObject != null)
                    {
                        // 1. use Dictionary entry
                        newType = complexTypeBuilder.AddEnumType(enumeratedObject);
                    }
                    if (newType == null)
                    {
                        var dataType = m_session.NodeCache.Find(enumType.NodeId) as DataTypeNode;
                        if (dataType != null)
                        {
                            if (dataType.DataTypeDefinition != null)
                            {
                                // 2. use DataTypeDefinition 
                                newType = complexTypeBuilder.AddEnumType(enumType.BrowseName.Name, dataType.DataTypeDefinition);
                            }
                            else
                            {
                                // browse for EnumFields or EnumStrings property
                                var property = BrowseForSingleProperty(enumType.NodeId);
                                var enumArray = m_session.ReadValue(
                                    ExpandedNodeId.ToNodeId(property.NodeId, m_session.NamespaceUris));
                                if (enumArray.Value is ExtensionObject[])
                                {
                                    // 3. use EnumValues
                                    newType = complexTypeBuilder.AddEnumType(enumType.BrowseName.Name, (ExtensionObject[])enumArray.Value);
                                }
                                else if (enumArray.Value is LocalizedText[])
                                {
                                    // 4. use EnumStrings
                                    newType = complexTypeBuilder.AddEnumType(enumType.BrowseName.Name, (LocalizedText[])enumArray.Value);
                                }
                            }
                        }
                    }
                    if (newType != null)
                    {
                        // match namespace and add to type factory
                        AddEncodeableType(enumType.NodeId, newType);
                    }
                }
            }
        }

        /// <summary>
        /// Helper to add new type with absolute ExpandedNodeId.
        /// </summary>
        private void AddEncodeableType(ExpandedNodeId nodeId, Type type)
        {
            var internalNodeId = NormalizeExpandedNodeId(nodeId);
            Utils.TraceDebug($"Adding Type {type.FullName} as: {internalNodeId.ToString()}");
            m_session.Factory.AddEncodeableType(internalNodeId, type);
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
                string name = enumTypeNode.BrowseName.Name;
                if (enumTypeNode.DataTypeDefinition != null)
                {
                    // 1. use DataTypeDefinition 
                    newType = complexTypeBuilder.AddEnumType(name, enumTypeNode.DataTypeDefinition);
                }
                else
                {
                    // browse for EnumFields or EnumStrings property
                    var property = BrowseForSingleProperty(enumTypeNode.NodeId);
                    if (property != null)
                    {
                        var enumArray = m_session.ReadValue(
                            ExpandedNodeId.ToNodeId(property.NodeId,
                            m_session.NamespaceUris));
                        if (enumArray.Value is ExtensionObject[])
                        {
                            // 2. use EnumValues
                            newType = complexTypeBuilder.AddEnumType(name, (ExtensionObject[])enumArray.Value);
                        }
                        else if (enumArray.Value is LocalizedText[])
                        {
                            // 3. use EnumStrings
                            newType = complexTypeBuilder.AddEnumType(name, (LocalizedText[])enumArray.Value);
                        }
                    }
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
            string typeName,
            ExpandedNodeId complexTypeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId = null
            )
        {
            // check all types
            var typeList = new List<Type>();
            foreach (StructureField field in structureDefinition.Fields)
            {
                var newType = GetFieldType(field);
                if (newType == null)
                {
                    // missing that type
                    return null;
                }
                typeList.Add(newType);
            }

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
                fieldBuilder.AddField(field, typeListEnumerator.Current, order);
                order++;
            }

            return fieldBuilder.CreateType();
        }

        /// <summary>
        /// Determine the type of a field in a StructureField definition.
        /// </summary>
        private Type GetFieldType(StructureField field)
        {
            Type fieldType = null;
            Type collectionType = null;
            if (field.DataType.NamespaceIndex == 0)
            {
                fieldType = Opc.Ua.TypeInfo.GetSystemType(field.DataType, m_session.Factory);
                if (field.ValueRank >= 0)
                {
                    if (fieldType == typeof(Byte[]))
                    {
                        collectionType = typeof(ByteStringCollection);
                    }
                    else if (fieldType == typeof(Single))
                    {
                        collectionType = typeof(FloatCollection);
                    }
                    else
                    {
                        var assemblyQualifiedName = typeof(StatusCode).Assembly;
                        String collectionClassName = "Opc.Ua." + fieldType.Name + "Collection, " + assemblyQualifiedName;
                        collectionType = Type.GetType(collectionClassName);
                    }
                }
            }
            else
            {
                fieldType = GetSystemType(field.DataType);
                if (fieldType == null)
                {
                    return null;
                }
                if (field.ValueRank >= 0)
                {
                    String collectionClassName = (fieldType.Namespace != null) ? fieldType.Namespace + "." : "";
                    collectionClassName += fieldType.Name + "Collection, " + fieldType.Assembly;
                    collectionType = Type.GetType(collectionClassName);
                }
            }

            if (field.ValueRank >= 0)
            {
                if (collectionType != null)
                {
                    fieldType = collectionType;
                }
                else
                {
                    fieldType = fieldType.MakeArrayType();
                }
            }

            return fieldType;
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
                var structuredObject = item as Opc.Ua.Schema.Binary.StructuredType;
                if (structuredObject != null)
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
                else if (item is Opc.Ua.Schema.Binary.EnumeratedType)
                {
                    enumList.Add(item);
                }
                else if (item is Opc.Ua.Schema.Binary.OpaqueType)
                {
                    // TODO
                }
                else
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError, $"Unexpected Type in binary schema: {item.GetType().Name}.");
                }
            }
        }
        #endregion

        #region Private Fields
        Session m_session;
        IComplexTypeFactory m_complexTypeBuilderFactory;
        const string m_opcComplexTypesPrefix = "Opc.Ua.ComplexTypes.";
        #endregion
    }

}//namespace
