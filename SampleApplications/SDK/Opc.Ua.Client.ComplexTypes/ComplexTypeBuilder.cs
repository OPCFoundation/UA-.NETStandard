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
using System.Reflection;
using System.Reflection.Emit;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Build an assembly with custom enum types and 
    /// complex types based on the BaseComplexType class
    /// using System.Reflection.Emit.
    /// </summary>
    public class ComplexTypeBuilder
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ComplexTypeBuilder(
            string targetNamespace,
            int targetNamespaceIndex,
            string moduleName = null,
            string assemblyName = null)
        {
            m_targetNamespace = targetNamespace;
            m_targetNamespaceIndex = targetNamespaceIndex;
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName ?? Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.GetDynamicModule(moduleName ?? m_opcTypesModuleName);
            if (moduleBuilder == null)
            {
                moduleBuilder = assemblyBuilder.DefineDynamicModule(moduleName ?? m_opcTypesModuleName);
            }
            m_moduleBuilder = moduleBuilder;
        }
        #endregion

        #region Public Members
        public string TargetNamespace => m_targetNamespace;
        public int TargetNamespaceIndex => m_targetNamespaceIndex;

        /// <summary>
        /// Create an enum type from a binary schema definition.
        /// Available before OPC UA V1.04.
        /// </summary>
        public Type AddEnumType(Schema.Binary.EnumeratedType enumeratedType)
        {
            if (enumeratedType == null)
            {
                throw new ArgumentNullException(nameof(enumeratedType));
            }
            var enumBuilder = m_moduleBuilder.DefineEnum(enumeratedType.Name, TypeAttributes.Public, typeof(int));
            enumBuilder.DataContractAttribute(m_targetNamespace);
            foreach (var enumValue in enumeratedType.EnumeratedValue)
            {
                var newEnum = enumBuilder.DefineLiteral(enumValue.Name, enumValue.Value);
                newEnum.EnumMemberAttribute(enumValue.Name, enumValue.Value);
            }
            return enumBuilder.CreateTypeInfo();
        }

        /// <summary>
        /// Create an enum type from an EnumDefinition in an ExtensionObject.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        public Type AddEnumType(string typeName, ExtensionObject typeDefinition)
        {
            var enumDefinition = typeDefinition.Body as EnumDefinition;
            if (enumDefinition == null)
            {
                throw new ArgumentNullException(nameof(typeDefinition));
            }

            var enumBuilder = m_moduleBuilder.DefineEnum(typeName, TypeAttributes.Public, typeof(int));
            enumBuilder.DataContractAttribute(m_targetNamespace);
            foreach (var enumValue in enumDefinition.Fields)
            {
                var newEnum = enumBuilder.DefineLiteral(enumValue.Name, (int)enumValue.Value);
                newEnum.EnumMemberAttribute(enumValue.Name, (int)enumValue.Value);
            }
            return enumBuilder.CreateTypeInfo();
        }

        /// <summary>
        /// Create an enum type from an EnumValue property of a DataType node.
        /// Available before OPC UA V1.04.
        /// </summary>
        public Type AddEnumType(string typeName, ExtensionObject[] enumDefinition)
        {
            if (enumDefinition == null)
            {
                throw new ArgumentNullException(nameof(enumDefinition));
            }

            var enumBuilder = m_moduleBuilder.DefineEnum(typeName, TypeAttributes.Public, typeof(int));
            enumBuilder.DataContractAttribute(m_targetNamespace);
            foreach (var extensionObject in enumDefinition)
            {
                var enumValue = extensionObject.Body as EnumValueType;
                var name = enumValue.DisplayName.Text;
                var newEnum = enumBuilder.DefineLiteral(name, (int)enumValue.Value);
                newEnum.EnumMemberAttribute(name, (int)enumValue.Value);
            }
            return enumBuilder.CreateTypeInfo();
        }

        /// <summary>
        /// Create an enum type from the EnumString array of a DataType node.
        /// Available before OPC UA V1.04.
        /// </summary>
        public Type AddEnumType(string typeName, LocalizedText[] enumDefinition)
        {
            if (enumDefinition == null)
            {
                throw new ArgumentNullException(nameof(enumDefinition));
            }

            var enumBuilder = m_moduleBuilder.DefineEnum(typeName, TypeAttributes.Public, typeof(int));
            enumBuilder.DataContractAttribute(m_targetNamespace);
            int value = 1;
            foreach (var enumValue in enumDefinition)
            {
                var name = enumValue.Text;
                var newEnum = enumBuilder.DefineLiteral(name, value);
                newEnum.EnumMemberAttribute(name, value);
                value++;
            }
            return enumBuilder.CreateTypeInfo();
        }

        /// <summary>
        /// Create a complex type from a StructureDefinition.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        public ComplexTypeFieldBuilder AddStructuredType(
            string name,
            StructureDefinition structureDefinition)
        {
            if (structureDefinition == null)
            {
                throw new ArgumentNullException(nameof(structureDefinition));
            }
            var structureBuilder = m_moduleBuilder.DefineType(name,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Serializable,
                typeof(BaseComplexType));
            structureBuilder.DataContractAttribute(m_targetNamespace);
            structureBuilder.StructureDefinitonAttribute(structureDefinition);
            return new ComplexTypeFieldBuilder(structureBuilder);
        }
        #endregion

        #region Private Fields
        private const string m_opcTypesModuleName = "Opc.Ua.ComplexType.Assembly";
        private ModuleBuilder m_moduleBuilder;
        private string m_targetNamespace;
        private int m_targetNamespaceIndex;
        #endregion
    }

    /// <summary>
    /// Helper to build property fields.
    /// </summary>
    public class ComplexTypeFieldBuilder
    {
        #region Constructors
        public ComplexTypeFieldBuilder(TypeBuilder structureBuilder)
        {
            m_structureBuilder = structureBuilder;
        }
        #endregion

        #region Public Properties
#if NOT_USED
        public void AddField(string fieldName, Type fieldType, int order)
        {
            var field = new StructureField
            {
                Name = fieldName,
                ValueRank = -1,
                MaxStringLength = 0,
                IsOptional = false
            };
            AddField(field, fieldType, order);
        }
#endif
        public void AddField(StructureField field, Type fieldType, int order)
        {
            var fieldBuilder = m_structureBuilder.DefineField("_" + field.Name, fieldType, FieldAttributes.Private);
            var propertyBuilder = m_structureBuilder.DefineProperty(
                field.Name,
                PropertyAttributes.None,
                fieldType,
                null);
            var methodAttributes =
                System.Reflection.MethodAttributes.Public |
                System.Reflection.MethodAttributes.HideBySig |
                System.Reflection.MethodAttributes.Virtual;

            var setBuilder = m_structureBuilder.DefineMethod("set_" + field.Name, methodAttributes, null, new[] { fieldType });
            var setIl = setBuilder.GetILGenerator();
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Stfld, fieldBuilder);
            setIl.Emit(OpCodes.Ret);

            var getBuilder = m_structureBuilder.DefineMethod("get_" + field.Name, methodAttributes, fieldType, Type.EmptyTypes);
            var getIl = getBuilder.GetILGenerator();
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, fieldBuilder);
            getIl.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getBuilder);
            propertyBuilder.SetSetMethod(setBuilder);
            propertyBuilder.DataMemberAttribute(field.Name, false, order);
            propertyBuilder.StructureFieldAttribute(field);
        }

        public Type CreateType()
        {
            var complexType = m_structureBuilder.CreateType();
            m_structureBuilder = null;
            return complexType;
        }
        #endregion

        #region Private Fields
        private TypeBuilder m_structureBuilder;
        #endregion
    }
}//namespace
