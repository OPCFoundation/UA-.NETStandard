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
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Extensions to build attributes for the complex type builder.
    /// </summary>
    public static class AttributeExtensions
    {
        #region Extensions
        /// <summary>
        /// Get the return type of an item in a collection.
        /// </summary>
        public static Type GetItemType(this Type collectionType)
        {
            return collectionType.GetMethod("get_Item").ReturnType;
        }

        /// <summary>
        /// Build the DataContract attribute for a complex type.
        /// </summary>
        public static void DataContractAttribute(this TypeBuilder builder, string Namespace)
        {
            var attribute = DataContractAttributeBuilder(Namespace);
            builder.SetCustomAttribute(attribute);
        }

        /// <summary>
        /// Build the DataContract attribute for an enumeration type.
        /// </summary>
        public static void DataContractAttribute(this EnumBuilder builder, string Namespace)
        {
            var attribute = DataContractAttributeBuilder(Namespace);
            builder.SetCustomAttribute(attribute);
        }

        /// <summary>
        /// Build the DataMember attribute for a complex type.
        /// </summary>
        public static void DataMemberAttribute(this PropertyBuilder typeBuilder, string name, bool isRequired, int order)
        {
            var attribute = DataMemberAttributeBuilder(name, isRequired, order);
            typeBuilder.SetCustomAttribute(attribute);
        }

        /// <summary>
        /// Build the StructureDefiniton attribute for a complex type.
        /// </summary>
        public static void StructureDefinitonAttribute(
            this TypeBuilder typeBuilder,
            StructureDefinition structureDefinition)
        {
            var attributeType = typeof(StructureDefinitionAttribute);
            var baseDataType = StructureDefinitionAttribute.FromBaseType(structureDefinition.BaseDataType);
            ConstructorInfo ctorInfo = attributeType.GetConstructor(Type.EmptyTypes);
            CustomAttributeBuilder builder = new CustomAttributeBuilder(
                ctorInfo,
                new object[0],  // constructor arguments
                new[]           // properties to assign
                {
                    attributeType.GetProperty("DefaultEncodingId"),
                    attributeType.GetProperty("BaseDataType"),
                    attributeType.GetProperty("StructureType")
                },
                new object[]    // values to assign
                {
                    structureDefinition.DefaultEncodingId?.ToString(),
                    baseDataType,
                    structureDefinition.StructureType
                });
            typeBuilder.SetCustomAttribute(builder);
        }

        /// <summary>
        /// Build the StructureTypeId attribute for a complex type.
        /// </summary>
        public static void StructureTypeIdAttribute(
            this TypeBuilder typeBuilder,
            ExpandedNodeId complexTypeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId
            )
        {
            var attributeType = typeof(StructureTypeIdAttribute);
            ConstructorInfo ctorInfo = attributeType.GetConstructor(Type.EmptyTypes);
            CustomAttributeBuilder builder = new CustomAttributeBuilder(
                ctorInfo,
                new object[0],  // constructor arguments
                new[]           // properties to assign
                {
                    attributeType.GetProperty("ComplexTypeId"),
                    attributeType.GetProperty("BinaryEncodingId"),
                    attributeType.GetProperty("XmlEncodingId")
                },
                new object[]    // values to assign
                {
                    complexTypeId?.ToString(),
                    binaryEncodingId?.ToString(),
                    xmlEncodingId?.ToString()
                });
            typeBuilder.SetCustomAttribute(builder);
        }

        /// <summary>
        /// Build the StructureField attribute for a complex type.
        /// </summary>
        public static void StructureFieldAttribute(
            this PropertyBuilder typeBuilder,
            StructureField structureField)
        {
            var attributeType = typeof(StructureFieldAttribute);
            ConstructorInfo ctorInfo = attributeType.GetConstructor(Type.EmptyTypes);
            CustomAttributeBuilder builder = new CustomAttributeBuilder(
                ctorInfo,
                new object[0],  // constructor arguments
                new[]           // properties to assign
                {
                    attributeType.GetProperty("ValueRank"),
                    attributeType.GetProperty("MaxStringLength"),
                    attributeType.GetProperty("IsOptional")
                },
                new object[]    // values to assign
                {
                    structureField.ValueRank,
                    structureField.MaxStringLength,
                    structureField.IsOptional
                });
            typeBuilder.SetCustomAttribute(builder);
        }

        /// <summary>
        /// Build the EnumMember attribute for an enumeration type.
        /// </summary>
        public static void EnumMemberAttribute(this FieldBuilder typeBuilder, string Name, int Value)
        {
            var attributeType = typeof(EnumMemberAttribute);
            Type[] ctorParams = new Type[] { typeof(string) };
            ConstructorInfo ctorInfo = attributeType.GetConstructor(Type.EmptyTypes);
            CustomAttributeBuilder builder = new CustomAttributeBuilder(
                ctorInfo,
                new object[0],  // constructor arguments
                new[]           // properties to assign
                {
                    attributeType.GetProperty("Value")
                },
                new object[]    // values to assign
                {
                    Name+"_"+Value.ToString()
                });
            typeBuilder.SetCustomAttribute(builder);
        }
        #endregion

        #region Private Static Members
        /// <summary>
        /// Build the DataMember attribute.
        /// </summary>
        private static CustomAttributeBuilder DataMemberAttributeBuilder(string name, bool isRequired, int order)
        {
            var attributeType = typeof(DataMemberAttribute);
            ConstructorInfo ctorInfo = attributeType.GetConstructor(Type.EmptyTypes);
            CustomAttributeBuilder builder = new CustomAttributeBuilder(
                ctorInfo,
                new object[0],  // constructor arguments
                new[]           // properties to assign
                {
                    attributeType.GetProperty("Name"),
                    attributeType.GetProperty("IsRequired"),
                    attributeType.GetProperty("Order")
                },
                new object[]    // values to assign
                {
                    name,
                    isRequired,
                    order
                });
            return builder;
        }

        /// <summary>
        /// Build the DataContract attribute.
        /// </summary>
        private static CustomAttributeBuilder DataContractAttributeBuilder(string Namespace)
        {
            var attributeType = typeof(DataContractAttribute);
            ConstructorInfo ctorInfo = attributeType.GetConstructor(Type.EmptyTypes);
            CustomAttributeBuilder builder = new CustomAttributeBuilder(
                ctorInfo,
                new object[0],  // constructor arguments
                new[]           // properties to assign
                {
                    attributeType.GetProperty("Namespace")
                },
                new object[]    // values to assign
                {
                    Namespace
                });
            return builder;
        }
        #endregion

    }
}//namespace
