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

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Builder for property fields.
    /// </summary>
    public class ComplexTypeFieldBuilder : IComplexTypeFieldBuilder
    {
        /// <summary>
        /// The field builder for a complex type.
        /// </summary>
        /// <param name="structureBuilder">The type builder to use.</param>
        /// <param name="structureType">The structure type.</param>
        public ComplexTypeFieldBuilder(TypeBuilder structureBuilder, StructureType structureType)
        {
            m_structureBuilder = structureBuilder;
            m_structureType = structureType;
        }

        /// <inheritdoc/>
        public void AddTypeIdAttribute(
            ExpandedNodeId complexTypeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId)
        {
            // m_structureBuilder is non-null until CreateType has been called.
            TypeBuilder builder = m_structureBuilder!;
            builder.StructureTypeIdAttribute(
                complexTypeId,
                binaryEncodingId,
                xmlEncodingId);
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Complex types are dynamically built via Reflection.Emit.")]
        public void AddField(StructureField field, IType? fieldType, int order, bool allowSubTypes)
        {
            // Caller is required to supply a non-null fieldType when adding a field
            // for a structure; the interface allows null only because some callers may
            // skip unresolved fields before reaching this point.
            Type typeOfField = fieldType!.Type;
            bool isEnum = fieldType is IEnumeratedType || typeOfField.IsEnum;
            if (field.ValueRank == ValueRanks.OneDimension)
            {
                typeOfField = typeOfField.MakeArrayType();
            }
            else if (field.ValueRank >= ValueRanks.TwoDimensions)
            {
                typeOfField = typeOfField.MakeArrayType(field.ValueRank);
            }
            // m_structureBuilder is non-null until CreateType has been called; AddField is only
            // valid before CreateType, so dereference is safe here.
            TypeBuilder structureBuilder = m_structureBuilder!;
            string fieldName = field.Name!;
            FieldBuilder fieldBuilder = structureBuilder.DefineField(
                "_" + fieldName,
                typeOfField,
                FieldAttributes.Private);
            PropertyBuilder propertyBuilder = structureBuilder.DefineProperty(
                fieldName,
                PropertyAttributes.None,
                typeOfField,
                null);
            const System.Reflection.MethodAttributes methodAttributes =
                System.Reflection.MethodAttributes.Public |
                System.Reflection.MethodAttributes.HideBySig |
                System.Reflection.MethodAttributes.Virtual;

            MethodBuilder setBuilder = structureBuilder.DefineMethod(
                "set_" + fieldName,
                methodAttributes,
                null,
                [typeOfField]);
            ILGenerator setIl = setBuilder.GetILGenerator();
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Stfld, fieldBuilder);
            if (m_structureType is StructureType.Union or StructureType.UnionWithSubtypedValues)
            {
                // set the union selector to the new field index
                // The internal field is defined directly on UnionComplexType in this assembly,
                // so reflection lookup is guaranteed to succeed at runtime.
                FieldInfo unionField = typeof(UnionComplexType).GetField(
                    "m_switchField",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                setIl.Emit(OpCodes.Ldarg_0);
                setIl.Emit(OpCodes.Ldc_I4, order);
                setIl.Emit(OpCodes.Stfld, unionField);
            }
            setIl.Emit(OpCodes.Ret);

            MethodBuilder getBuilder = structureBuilder.DefineMethod(
                "get_" + fieldName,
                methodAttributes,
                typeOfField,
                Type.EmptyTypes);
            ILGenerator getIl = getBuilder.GetILGenerator();
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, fieldBuilder);
            getIl.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getBuilder);
            propertyBuilder.SetSetMethod(setBuilder);
            propertyBuilder.DataMemberAttribute(fieldName, false, order);
            propertyBuilder.StructureFieldAttribute(field, allowSubTypes, isEnum);
        }

        /// <inheritdoc/>
        public IEncodeableType CreateType()
        {
            // The structure builder produces a complex type that always implements
            // IEncodeableType because it derives from BaseComplexType / OptionalFieldsComplexType
            // / UnionComplexType which are all IEncodeable.
            TypeBuilder structureBuilder = m_structureBuilder!;
            Type complexType = structureBuilder.CreateType();
            m_structureBuilder = null;
            return (ReflectionBasedType.From(complexType) as IEncodeableType)!;
        }

        /// <inheritdoc/>
        public IEncodeableType GetStructureType()
        {
            return new Stub(m_structureBuilder!);
        }

        /// <summary>
        /// Stub for not fully built encodeable types
        /// </summary>
        private record class Stub(Type Type) : IEncodeableType
        {
            /// <inheritdoc/>
            public XmlQualifiedName XmlName
                => throw new NotImplementedException();

            /// <inheritdoc/>
            public IEncodeable CreateInstance()
            {
                throw new NotImplementedException();
            }
        }

        private TypeBuilder? m_structureBuilder;
        private readonly StructureType m_structureType;
    }
}
