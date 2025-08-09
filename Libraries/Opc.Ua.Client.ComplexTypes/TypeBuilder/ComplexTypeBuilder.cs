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
using System.Reflection;
using System.Reflection.Emit;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Build an assembly with custom enum types and
    /// complex types based on the BaseComplexType class
    /// using System.Reflection.Emit.
    /// </summary>
    public class ComplexTypeBuilder : IComplexTypeBuilder
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ComplexTypeBuilder(
            AssemblyModule moduleFactory,
            string targetNamespace,
            int targetNamespaceIndex,
            string moduleName = null)
        {
            TargetNamespace = targetNamespace;
            TargetNamespaceIndex = targetNamespaceIndex;
            m_moduleName = FindModuleName(moduleName, targetNamespace);
            m_moduleBuilder = moduleFactory.GetModuleBuilder();
        }

        /// <summary>
        /// The target namespace of the type builder.
        /// </summary>
        public string TargetNamespace { get; }

        /// <summary>
        /// The target namespace index of the type builder.
        /// </summary>
        public int TargetNamespaceIndex { get; }

        /// <summary>
        /// Create an enum type from an EnumDefinition in an ExtensionObject.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        public Type AddEnumType(QualifiedName typeName, EnumDefinition enumDefinition)
        {
            if (enumDefinition == null)
            {
                throw new ArgumentNullException(nameof(enumDefinition));
            }
            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(typeName));
            }

            if (string.IsNullOrEmpty(typeName.Name))
            {
                // The type name should not be null or empty then the type definition
                // or xml is broken. For the latter we want to go into the fallback path
                // And try to read the data type definition's enum definition.
                return null;
            }

            EnumBuilder enumBuilder = m_moduleBuilder.DefineEnum(
                GetFullQualifiedTypeName(typeName),
                TypeAttributes.Public,
                typeof(int));
            enumBuilder.DataContractAttribute(TargetNamespace);
            if (enumDefinition.Fields != null)
            {
                var fieldNames = new HashSet<string>();
                foreach (EnumField enumValue in enumDefinition.Fields)
                {
                    // Create a field from the type name and ensure it is not a duplicate
                    string fieldName = enumValue.Name;
                    if (string.IsNullOrEmpty(fieldName))
                    {
                        // This is to be super safe, but we should never get here.
                        fieldName = $"{typeName.Name}_{enumValue.Value}";
                    }
                    if (fieldNames.Add(fieldName))
                    {
                        FieldBuilder newEnum = enumBuilder.DefineLiteral(fieldName, (int)enumValue.Value);
                        newEnum.EnumMemberAttribute(fieldName, (int)enumValue.Value);
                    }
                }
            }
            return enumBuilder.CreateTypeInfo();
        }

        /// <summary>
        /// Create a complex type from a StructureDefinition.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        public IComplexTypeFieldBuilder AddStructuredType(
            QualifiedName name,
            StructureDefinition structureDefinition)
        {
            if (structureDefinition == null)
            {
                throw new ArgumentNullException(nameof(structureDefinition));
            }

            Type baseType = structureDefinition.StructureType switch {
                StructureType.StructureWithOptionalFields => typeof(OptionalFieldsComplexType),
                StructureType.UnionWithSubtypedValues or StructureType.Union => typeof(UnionComplexType),
                StructureType.StructureWithSubtypedValues or StructureType.Structure => typeof(BaseComplexType),
                _ => throw new DataTypeNotSupportedException("Unsupported structure type"),
            };
            TypeBuilder structureBuilder = m_moduleBuilder.DefineType(
                GetFullQualifiedTypeName(name),
                TypeAttributes.Public | TypeAttributes.Class,
                baseType);
            structureBuilder.DataContractAttribute(TargetNamespace);
            structureBuilder.StructureDefinitionAttribute(structureDefinition);
            return new ComplexTypeFieldBuilder(structureBuilder, structureDefinition.StructureType);
        }

        /// <summary>
        /// Create a unique namespace module name for the type.
        /// </summary>
        private static string FindModuleName(string moduleName, string targetNamespace)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                // remove space chars in malformed namespace url
                string tempNamespace = targetNamespace.Replace(" ", "", StringComparison.Ordinal);
                var uri = new Uri(tempNamespace, UriKind.RelativeOrAbsolute);
                string tempName = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString();

                tempName = tempName.Replace("/", "", StringComparison.Ordinal);
                string[] splitName = tempName.Split(':');
                moduleName = splitName[^1];
            }
            return moduleName;
        }

        /// <summary>
        /// Creates a unique full qualified type name for the assembly.
        /// </summary>
        /// <param name="browseName">The browse name of the type.</param>
        private string GetFullQualifiedTypeName(QualifiedName browseName)
        {
            string result = "Opc.Ua.ComplexTypes." + m_moduleName + ".";
            if (browseName.NamespaceIndex > 1)
            {
                result += browseName.NamespaceIndex + ".";
            }
            return result + browseName.Name;
        }

        private readonly ModuleBuilder m_moduleBuilder;
        private readonly string m_moduleName;
    }
}//namespace
