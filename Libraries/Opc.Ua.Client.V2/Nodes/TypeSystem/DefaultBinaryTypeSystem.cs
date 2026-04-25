// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua.Client.Nodes;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;

    /// <summary>
    /// Opc binary schema type system. This type system is used to parse
    /// OPC Binary schema files and create data type definitions from them.
    /// </summary>
    /// <remarks>
    /// Support for V1.03 dictionaries with the following known restrictions:
    /// - Structured types are mapped to the V1.04 structured type definition.
    /// - Enumerated types are mapped to the V1.04 enum definition.
    /// - V1.04 OptionSet are not supported.
    /// </remarks>
    internal sealed record class DefaultBinaryTypeSystem : DataTypeSystem
    {
        /// <inheritdoc/>
        public override NodeId TypeSystemId
            => (NodeId)Objects.OPCBinarySchema_TypeSystem;
        /// <inheritdoc/>
        public override QualifiedName TypeSystemName
            => (QualifiedName)BrowseNames.OPCBinarySchema_TypeSystem;
        /// <inheritdoc/>
        public override QualifiedName EncodingName
            => (QualifiedName)BrowseNames.DefaultBinary;

        /// <inheritdoc/>
        public DefaultBinaryTypeSystem(INodeCache nodeCache,
            IServiceMessageContext context, ILogger<DefaultBinaryTypeSystem> logger)
            : base(nodeCache, context, logger)
        {
        }

        /// <inheritdoc/>
        protected override DataTypeDictionary Load(NodeId dictionaryId,
            string targetNamespace, byte[] buffer, Dictionary<string, byte[]> imports)
        {
            using var istrm = new MemoryStream(buffer);
            var validator = new Schema.Binary.BinarySchemaValidator(imports);
            validator.Validate(istrm);
            return new DataTypeDictionary(dictionaryId,
                targetNamespace, TypeSystemId, TypeSystemName, validator.Dictionary, null);
        }

        /// <inheritdoc/>
        protected override void LoadDictionaryDataTypeDefinitions(
            Dictionary<XmlQualifiedName,
                (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)> typeDictionary,
            DataTypeDictionary dictionary, NamespaceTable namespaceUris)
        {
            foreach (var item in dictionary.TypeDictionary!.Items)
            {
                var qName = item.QName ??
                    new XmlQualifiedName(item.Name, dictionary.Namespace);
                if (!typeDictionary.TryGetValue(qName, out var entry))
                {
                    continue;
                }
                switch (item)
                {
                    case Schema.Binary.EnumeratedType enumeratedObject:
                        var enumDefinition = new DictionaryDataTypeDefinition(
                            ToEnumDefinition(enumeratedObject),
                            qName, entry.EncodingId);
                        Add(entry.EncodingId, entry.TypeId, enumDefinition);
                        break;
                    case Schema.Binary.StructuredType structuredObject:
                        var structureDefinition = new DictionaryDataTypeDefinition(
                            ToStructureDefinition(structuredObject, entry.EncodingId,
                            typeDictionary, namespaceUris, entry.TypeId),
                            qName, entry.EncodingId);
                        Add(entry.EncodingId, entry.TypeId, structureDefinition);
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// Convert a binary schema type definition to a
        /// StructureDefinition.
        /// </summary>
        /// <param name="structuredType"></param>
        /// <param name="defaultEncodingId"></param>
        /// <param name="typeDictionary"></param>
        /// <param name="namespaceTable"></param>
        /// <param name="dataTypeNodeId"></param>
        /// <remarks>
        /// Support for:
        /// - Structures, structures with optional fields and unions.
        /// - Nested types and typed arrays with length field.
        /// The converter has the following known restrictions:
        /// - Support only for V1.03 structured types which can be mapped to the V1.04
        ///   structured type definition.
        /// The following dictionary tags cause bail out for a structure:
        /// - use of a terminator of length in bytes
        /// - an array length field is not a direct predecessor of the array
        /// - The switch value of a union is not the first field.
        /// - The selector bits of optional fields are not stored in a 32 bit variable
        ///   and do not add up to 32 bits.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        internal static StructureDefinition ToStructureDefinition(
            Schema.Binary.StructuredType structuredType,
            ExpandedNodeId defaultEncodingId, Dictionary<XmlQualifiedName,
                (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)> typeDictionary,
            NamespaceTable namespaceTable, ExpandedNodeId dataTypeNodeId)
        {
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Null,
                DefaultEncodingId =
                    ExpandedNodeId.ToNodeId(defaultEncodingId, namespaceTable),
                Fields = [],
                StructureType = StructureType.Structure
            };

            var hasBitField = false;
            var isUnionType = false;
            foreach (var field in structuredType.Field)
            {
                // check for yet unsupported properties
                if (field.IsLengthInBytes || field.Terminator != null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                        "The structure definition uses a Terminator or " +
                        "LengthInBytes, which are not supported.");
                }

                if (field.SwitchValue != 0)
                {
                    isUnionType = true;
                }

                if (field.TypeName.Namespace is Namespaces.OpcBinarySchema or
                    Namespaces.OpcUa && field.TypeName.Name == "Bit")
                {
                    hasBitField = true;
                    continue;
                }
                if (field.Length != 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                        "A structure field has a length field which is not supported.");
                }
            }

            if (isUnionType && hasBitField)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                    "The structure definition combines a Union and a bit filed," +
                    " both of which are not supported in a single structure.");
            }

            if (isUnionType)
            {
                structureDefinition.StructureType = StructureType.Union;
            }
            if (hasBitField)
            {
                structureDefinition.StructureType = StructureType.StructureWithOptionalFields;
            }

            byte switchFieldBitPosition = 0;
            var switchFieldBits = new Dictionary<string, byte>();
            // convert fields
            for (var i = 0; i < structuredType.Field.Length; i++)
            {
                var field = structuredType.Field[i];
                // consume optional bits
                if (IsXmlBitType(field.TypeName))
                {
                    var count = structureDefinition.Fields.Count;
                    if (count == 0 && switchFieldBitPosition < 32)
                    {
                        structureDefinition.StructureType =
                            StructureType.StructureWithOptionalFields;
                        var fieldLength =
                            (byte)((field.Length == 0) ? 1u : field.Length);
                        switchFieldBits[field.Name] = switchFieldBitPosition;
                        switchFieldBitPosition += fieldLength;
                    }
                    else
                    {
                        throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                            "Options for bit selectors must be 32 bit in size, use " +
                            "the Int32 datatype and must be the first element in the structure.");
                    }
                    continue;
                }

                if (switchFieldBitPosition is not 0 and not 32)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                        "Bitwise option selectors must have 32 bits.");
                }
                var fieldDataTypeNodeId = ExpandedNodeId.ToNodeId(
                    field.TypeName == structuredType.QName ?
                    dataTypeNodeId : ToNodeId(field.TypeName), namespaceTable);
                var dataTypeField = new StructureField
                {
                    Name = field.Name,
                    Description = default,
                    DataType = fieldDataTypeNodeId,
                    IsOptional = false,
                    MaxStringLength = 0,
                    ArrayDimensions = default,
                    ValueRank = -1
                };
                if (field.LengthField != null)
                {
                    // handle array length
                    var lastField = structureDefinition.Fields.Count == 0
                        ? null : structureDefinition.Fields[^1];
                    if (lastField == null || lastField.Name != field.LengthField)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                            "The length field must precede the type field of an array.");
                    }
                    lastField.Name = field.Name;
                    lastField.DataType = fieldDataTypeNodeId;
                    lastField.ValueRank = 1;
                    structureDefinition.Fields += dataTypeField;
                    continue;
                }
                if (isUnionType)
                {
                    // ignore the switchfield
                    if (field.SwitchField == null)
                    {
                        if (i != 0)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                                "The switch field of a union must be the first field in the complex type.");
                        }
                        continue;
                    }
                    structureDefinition.Fields += dataTypeField;
                    if (structureDefinition.Fields.Count != i)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                            "The count of the switch field of the union member is not matching the field position.");
                    }
                    continue;
                }
                if (field.SwitchField != null)
                {
                    dataTypeField.IsOptional = true;
                    if (!switchFieldBits.TryGetValue(field.SwitchField, out _))
                    {
                        throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid,
                            $"The switch field for {field.SwitchField} does not exist.");
                    }
                }
                structureDefinition.Fields += dataTypeField;
            }
            return structureDefinition;

            ExpandedNodeId ToNodeId(XmlQualifiedName typeName)
            {
                if (typeName.Namespace is Namespaces.OpcBinarySchema or
                    Namespaces.OpcUa)
                {
                    switch (typeName.Name)
                    {
                        case "CharArray": return DataTypeIds.String;
                        case "Variant": return DataTypeIds.BaseDataType;
                        case "ExtensionObject": return DataTypeIds.Structure;
                    }
                }
                if (!typeDictionary.TryGetValue(typeName, out var referenceId))
                {
                    // The type was not found in the namespace
                    return NodeId.Null;
                }
                return referenceId.TypeId;
            }
        }

        /// <summary>
        /// Convert a binary schema enumerated type to an enum data type definition
        /// Available before OPC UA V1.04.
        /// </summary>
        /// <param name="enumeratedType"></param>
        internal static EnumDefinition ToEnumDefinition(
            Schema.Binary.EnumeratedType enumeratedType)
        {
            var enumDefinition = new EnumDefinition();
            foreach (var enumValue in enumeratedType.EnumeratedValue)
            {
                var enumTypeField = new EnumField
                {
                    Name = enumValue.Name,
                    Value = enumValue.Value,
                    Description = (LocalizedText)(enumValue.Documentation?.Text?.FirstOrDefault() ?? string.Empty),
                    DisplayName = (LocalizedText)enumValue.Name
                };
                enumDefinition.Fields += enumTypeField;
            }
            return enumDefinition;
        }

        /// <summary>
        /// Test for bit flag
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        private static bool IsXmlBitType(XmlQualifiedName typeName)
        {
            if (typeName.Namespace is Namespaces.OpcBinarySchema or
                Namespaces.OpcUa && typeName.Name == "Bit")
            {
                return true;
            }
            return false;
        }
    }
}
