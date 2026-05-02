/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Extensions to convert binary schema type definitions to DataTypeDefinitions.
    /// </summary>
    public static class DataTypeDefinitionExtension
    {
        /// <summary>
        /// Convert a binary schema type definition to a
        /// StructureDefinition.
        /// </summary>
        /// <remarks>
        /// Support for:
        /// - Structures, structures with optional fields and unions.
        /// - Nested types and typed arrays with length field.
        /// The converter has the following known restrictions:
        /// - Support only for V1.03 structured types which can be mapped to the V1.04
        /// structured type definition.
        /// The following dictionary tags cause bail out for a structure:
        /// - use of a terminator of length in bytes
        /// - an array length field is not a direct predecessor of the array
        /// - The switch value of a union is not the first field.
        /// - The selector bits of optional fields are not stored in a 32 bit variable
        ///   and do not add up to 32 bits.
        /// </remarks>
        /// <exception cref="DataTypeNotSupportedException"></exception>
        public static StructureDefinition ToStructureDefinition(
            this Schema.Binary.StructuredType structuredType,
            ExpandedNodeId defaultEncodingId,
            Dictionary<XmlQualifiedName, NodeId> typeDictionary,
            NamespaceTable namespaceTable,
            NodeId dataTypeNodeId)
        {
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = default,
                DefaultEncodingId = ExpandedNodeId.ToNodeId(defaultEncodingId, namespaceTable),
                StructureType = StructureType.Structure
            };

            var structureFields = new List<StructureField>();
            bool isSupportedType = true;
            bool hasBitField = false;
            bool isUnionType = false;

            // Schema.Binary.StructuredType.Field is nullable on the imported XML schema type
            // but a structured type without any fields cannot describe a valid OPC UA
            // structure; the bang reflects that requirement.
            foreach (Schema.Binary.FieldType field in structuredType.Field!)
            {
                // check for yet unsupported properties
                if (field.IsLengthInBytes || field.Terminator != null)
                {
                    isSupportedType = false;
                }

                if (field.SwitchValue != 0)
                {
                    isUnionType = true;
                }

                // Field.TypeName is nullable on the imported schema; a binary field without
                // a type reference cannot be encoded so the bang treats it as required.
                if (field.TypeName!.Namespace is Namespaces.OpcBinarySchema or Namespaces.OpcUa &&
                    field.TypeName.Name == "Bit")
                {
                    hasBitField = true;
                    continue;
                }
                if (field.Length != 0)
                {
                    isSupportedType = false;
                }
            }

            // test forbidden combinations
            if (!isSupportedType)
            {
                throw new DataTypeNotSupportedException(
                    "The structure definition uses a Terminator or LengthInBytes, which are not supported.");
            }

            if (isUnionType && hasBitField)
            {
                throw new DataTypeNotSupportedException(
                    "The structure definition combines a Union and a bit field, both of which are not supported in a single structure.");
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
            int dataTypeFieldPosition = 0;
            var switchFieldBits = new Dictionary<string, byte>();
            // convert fields
            // structuredType.Field is required; see note on the first loop above.
            foreach (Schema.Binary.FieldType field in structuredType.Field!)
            {
                // consume optional bits
                // field.TypeName is required; see note on the first loop above.
                if (field.TypeName!.IsXmlBitType())
                {
                    int count = structureFields.Count;
                    if (count == 0 && switchFieldBitPosition < 32)
                    {
                        structureDefinition.StructureType
                            = StructureType.StructureWithOptionalFields;
                        byte fieldLength = (byte)(field.Length == 0 ? 1u : field.Length);
                        // field.Name is annotated nullable on the schema but every named
                        // bit selector carries a name in valid binary schemas.
                        switchFieldBits[field.Name!] = switchFieldBitPosition;
                        switchFieldBitPosition += fieldLength;
                    }
                    else
                    {
                        throw new DataTypeNotSupportedException(
                            "Options for bit selectors must be 32 bit in size, use the Int32 datatype and must be the first element in the structure.");
                    }
                    continue;
                }

                if (switchFieldBitPosition is not 0 and not 32)
                {
                    throw new DataTypeNotSupportedException(
                        "Bitwise option selectors must have 32 bits.");
                }
                NodeId fieldDataTypeNodeId;
                if (field.TypeName == structuredType.QName)
                {
                    // recursive type
                    fieldDataTypeNodeId = dataTypeNodeId;
                }
                else
                {
                    // field.TypeName is required for non-recursive references; see note above.
                    fieldDataTypeNodeId = field.TypeName!.ToNodeId(typeDictionary);
                }
                var dataTypeField = new StructureField
                {
                    // field.Name carries the structure field identifier; required.
                    Name = field.Name!,
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
                    StructureField lastField = structureFields[^1];
                    if (lastField.Name != field.LengthField)
                    {
                        throw new DataTypeNotSupportedException(
                            "The length field must precede the type field of an array.");
                    }
                    // field.Name carries the array element name; required.
                    lastField.Name = field.Name!;
                    lastField.DataType = fieldDataTypeNodeId;
                    lastField.ValueRank = 1;
                }
                else
                {
                    if (isUnionType)
                    {
                        // ignore the switchfield
                        if (field.SwitchField == null)
                        {
                            if (structureFields.Count != 0)
                            {
                                throw new DataTypeNotSupportedException(
                                    "The switch field of a union must be the first field in the complex type.");
                            }
                            continue;
                        }
                        if (structureFields.Count != dataTypeFieldPosition)
                        {
                            throw new DataTypeNotSupportedException(
                                "The count of the switch field of the union member is not matching the field position.");
                        }
                        dataTypeFieldPosition++;
                    }
                    else if (field.SwitchField != null)
                    {
                        dataTypeField.IsOptional = true;
                        if (!switchFieldBits.TryGetValue(field.SwitchField, out byte value))
                        {
                            throw new DataTypeNotSupportedException(
                                $"The switch field for {field.SwitchField} does not exist.");
                        }
                    }
                    structureFields.Add(dataTypeField);
                }
            }

            structureDefinition.Fields = structureFields;
            return structureDefinition;
        }

        /// <summary>
        /// Convert a binary schema enumerated type to an enum data type definition
        /// Available before OPC UA V1.04.
        /// </summary>
        public static EnumDefinition? ToEnumDefinition(
            this Schema.Binary.EnumeratedType enumeratedType,
            string enumTypeName)
        {
            var enumFields = new List<EnumField>();

            if (enumeratedType.EnumeratedValue != null)
            {
                foreach (Schema.Binary.EnumeratedValue enumValue in enumeratedType.EnumeratedValue)
                {
                    string? fieldName = enumValue.Name;
                    if (string.IsNullOrEmpty(fieldName))
                    {
                        if (string.IsNullOrEmpty(enumTypeName))
                        {
                            // Here we give up because the overall type is broken
                            return null;
                        }
                        fieldName = $"{enumTypeName}_{enumValue.Value}";
                    }

                    var enumTypeField = new EnumField
                    {
                        Name = fieldName,
                        Value = enumValue.Value,
                        // Documentation/Text/FirstOrDefault may yield null when no documentation
                        // exists; LocalizedText.From's signature does not accept null, but the
                        // implementation maps null to LocalizedText.Null at runtime.
                        Description = LocalizedText.From(enumValue.Documentation?.Text?.FirstOrDefault()!),
                        // enumValue.Name on the imported schema is nullable; passing null to From
                        // is handled at runtime as LocalizedText.Null (preserves prior behavior).
                        DisplayName = LocalizedText.From(enumValue.Name!)
                    };
                    enumFields.Add(enumTypeField);
                }
            }

            return new EnumDefinition
            {
                Fields = enumFields
            };
        }

        /// <summary>
        /// Convert a list of EnumValues to an enum data type definition
        /// Available before OPC UA V1.04.
        /// </summary>
        public static EnumDefinition? ToEnumDefinition(
            this ArrayOf<ExtensionObject> enumValueTypes,
            string enumTypeName)
        {
            var enumFields = new List<EnumField>();

            foreach (ExtensionObject extensionObject in enumValueTypes)
            {
                if (!extensionObject.TryGetEncodeable(out EnumValueType? enumValue))
                {
                    // All we can do here is skip this value. Since there is no
                    // fallback it is better to include all other type fields if
                    // they are in the extension object array.
                    continue;
                }

                // TryGetEncodeable<T> returning true implies enumValue is non-null but its
                // signature lacks [NotNullWhen(true)] so the compiler cannot infer it.
                string? name = enumValue!.DisplayName.Text;
                if (string.IsNullOrEmpty(name))
                {
                    if (string.IsNullOrEmpty(enumTypeName))
                    {
                        // Here we give up because the overall type is broken
                        return null;
                    }
                    name = $"{enumTypeName}_{enumValue.Value}";
                }

                var enumTypeField = new EnumField
                {
                    Name = name,
                    Value = enumValue.Value,
                    // name is non-null here (assigned from non-null DisplayName.Text or the
                    // composed fallback above); legacy BCL targets lack [NotNullWhen(false)]
                    // on string.IsNullOrEmpty so the compiler cannot infer it.
                    DisplayName = LocalizedText.From(name!)
                };
                enumFields.Add(enumTypeField);
            }

            return new EnumDefinition
            {
                Fields = enumFields
            };
        }

        /// <summary>
        /// Convert a list of EnumValues to an enum data type definition
        /// Available before OPC UA V1.04.
        /// </summary>
        public static EnumDefinition? ToEnumDefinition(
            this ArrayOf<LocalizedText> enumFieldNames,
            string enumTypeName)
        {
            var enumFields = new List<EnumField>();

            for (int ii = 0; ii < enumFieldNames.Count; ii++)
            {
                LocalizedText enumFieldName = enumFieldNames[ii];
                string? name = enumFieldName.Text;

                if (string.IsNullOrEmpty(name))
                {
                    if (string.IsNullOrEmpty(enumTypeName))
                    {
                        // Here we give up because the overall type is broken
                        return null;
                    }
                    name = $"{enumTypeName}_{ii}";
                }

                var enumTypeField = new EnumField
                {
                    Name = name,
                    Value = ii,
                    // name is non-null here; see comment in the ExtensionObject overload.
                    DisplayName = LocalizedText.From(name!)
                };

                enumFields.Add(enumTypeField);
            }

            return new EnumDefinition
            {
                Fields = enumFields
            };
        }

        /// <summary>
        /// Test for special Bit type used in the binary schema structure definition.
        /// </summary>
        private static bool IsXmlBitType(this XmlQualifiedName typeName)
        {
            return typeName.Namespace is Namespaces.OpcBinarySchema or Namespaces.OpcUa &&
                typeName.Name == "Bit";
        }

        /// <summary>
        /// Look up the node id for a qualified name of a type
        /// in a binary schema type definition.
        /// </summary>
        private static NodeId ToNodeId(
            this XmlQualifiedName typeName,
            Dictionary<XmlQualifiedName, NodeId> typeCollection)
        {
            if (typeName.Namespace is Namespaces.OpcBinarySchema or Namespaces.OpcUa)
            {
                switch (typeName.Name)
                {
                    case "CharArray":
                        return DataTypeIds.String;
                    case "Variant":
                        return DataTypeIds.BaseDataType;
                    case "ExtensionObject":
                        return DataTypeIds.Structure;
                    default:
                        if (!DataTypes.TryGetIdentifier(typeName.Name, out uint id))
                        {
                            return default;
                        }
                        return new NodeId(id);
                }
            }
            if (!typeCollection.TryGetValue(typeName, out NodeId referenceId))
            {
                // The type was not found in the namespace
                return NodeId.Null;
            }

            return referenceId;
        }
    }
}
