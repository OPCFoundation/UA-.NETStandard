/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Extensions to convert binary schema type definitions to DataTypeDefinitions.
    /// </summary>
    public static class DataTypeDefinitionExtension
    {
        #region Public Extensions
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
        public static StructureDefinition ToStructureDefinition(
            this Schema.Binary.StructuredType structuredType,
            ExpandedNodeId defaultEncodingId,
            Dictionary<XmlQualifiedName, NodeId> typeDictionary,
            NamespaceTable namespaceTable)
        {
            var structureDefinition = new StructureDefinition() {
                BaseDataType = null,
                DefaultEncodingId = ExpandedNodeId.ToNodeId(defaultEncodingId, namespaceTable),
                Fields = new StructureFieldCollection(),
                StructureType = StructureType.Structure
            };

            bool isSupportedType = true;
            bool hasBitField = false;
            bool isUnionType = false;

            foreach (var field in structuredType.Field)
            {
                // check for yet unsupported properties
                if (field.IsLengthInBytes ||
                    field.Terminator != null)
                {
                    isSupportedType = false;
                }

                if (field.SwitchValue != 0)
                {
                    isUnionType = true;
                }

                if (field.TypeName.Namespace == Namespaces.OpcBinarySchema ||
                    field.TypeName.Namespace == Namespaces.OpcUa)
                {
                    if (field.TypeName.Name == "Bit")
                    {
                        hasBitField = true;
                        continue;
                    }
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
                    "The structure definition combines a Union and a bit filed, both of which are not supported in a single structure.");
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
            Int32 dataTypeFieldPosition = 0;
            var switchFieldBits = new Dictionary<string, byte>();
            // convert fields
            foreach (var field in structuredType.Field)
            {
                // consume optional bits
                if (field.TypeName.IsXmlBitType())
                {
                    var count = structureDefinition.Fields.Count;
                    if (count == 0 &&
                        switchFieldBitPosition < 32)
                    {
                        structureDefinition.StructureType = StructureType.StructureWithOptionalFields;
                        byte fieldLength = (byte)((field.Length == 0) ? 1u : field.Length);
                        switchFieldBits[field.Name] = switchFieldBitPosition;
                        switchFieldBitPosition += fieldLength;
                    }
                    else
                    {
                        throw new DataTypeNotSupportedException(
                            "Options for bit selectors must be 32 bit in size, use the Int32 datatype and must be the first element in the structure.");
                    }
                    continue;
                }

                if (switchFieldBitPosition != 0 &&
                    switchFieldBitPosition != 32)
                {
                    throw new DataTypeNotSupportedException(
                        "Bitwise option selectors must have 32 bits.");
                }

                var dataTypeField = new StructureField() {
                    Name = field.Name,
                    Description = null,
                    DataType = field.TypeName.ToNodeId(typeDictionary),
                    IsOptional = false,
                    MaxStringLength = 0,
                    ArrayDimensions = null,
                    ValueRank = -1
                };

                if (field.LengthField != null)
                {
                    // handle array length
                    var lastField = structureDefinition.Fields.Last();
                    if (lastField.Name != field.LengthField)
                    {
                        throw new DataTypeNotSupportedException(
                            "The length field must precede the type field of an array.");
                    }
                    lastField.Name = field.Name;
                    lastField.DataType = field.TypeName.ToNodeId(typeDictionary);
                    lastField.ValueRank = 1;
                }
                else
                {
                    if (isUnionType)
                    {
                        // ignore the switchfield
                        if (field.SwitchField == null)
                        {
                            if (structureDefinition.Fields.Count != 0)
                            {
                                throw new DataTypeNotSupportedException(
                                    "The switch field of a union must be the first field in the complex type.");
                            }
                            continue;
                        }
                        if (structureDefinition.Fields.Count != dataTypeFieldPosition)
                        {
                            throw new DataTypeNotSupportedException(
                                "The count of the switch field of the union member is not matching the field position.");
                        }
                        dataTypeFieldPosition++;
                    }
                    else
                    {
                        if (field.SwitchField != null)
                        {
                            dataTypeField.IsOptional = true;
                            byte value;
                            if (!switchFieldBits.TryGetValue(field.SwitchField, out value))
                            {
                                throw new DataTypeNotSupportedException(
                                    $"The switch field for {field.SwitchField} does not exist.");
                            }
                        }
                    }
                    structureDefinition.Fields.Add(dataTypeField);
                }
            }

            return structureDefinition;
        }

        /// <summary>
        /// Test for special Bit type used in the binary schema structure definition.
        /// </summary>
        private static bool IsXmlBitType(this XmlQualifiedName typeName)
        {
            if (typeName.Namespace == Namespaces.OpcBinarySchema ||
                typeName.Namespace == Namespaces.OpcUa)
            {
                if (typeName.Name == "Bit")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Look up the node id for a qualified name of a type
        /// in a binary schema type definition.
        /// </summary>
        private static NodeId ToNodeId(
            this XmlQualifiedName typeName,
            Dictionary<XmlQualifiedName, NodeId> typeCollection)
        {
            if (typeName.Namespace == Namespaces.OpcBinarySchema ||
                typeName.Namespace == Namespaces.OpcUa)
            {
                switch (typeName.Name)
                {
                    case "CharArray": return DataTypeIds.String;
                    case "Variant": return DataTypeIds.BaseDataType;
                    case "ExtensionObject": return DataTypeIds.Structure;
                }
                var internalField = typeof(DataTypeIds).GetField(typeName.Name);
                if (internalField == null)
                {
                    throw new DataTypeNotFoundException(
                        $"The type {typeName.Name} was not found in the internal type factory.");
                }
                return (NodeId)internalField.GetValue(typeName.Name);
            }
            else
            {
                if (!typeCollection.TryGetValue(typeName, out NodeId referenceId))
                {
                    throw new DataTypeNotFoundException(
                        typeName.Name,
                        $"The type {typeName.Name} in namespace {typeName.Namespace} was not found.");
                }
                return referenceId;
            }
        }
        #endregion
    }
}//namespace
