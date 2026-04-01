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

using System.Xml;
using System.Collections.Generic;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Complex type field builder
    /// </summary>
    internal sealed class DefaultComplexTypeFieldBuilder : IComplexTypeFieldBuilder
    {
        /// <summary>
        /// Create field builder
        /// </summary>
        /// <param name="defaultComplexTypeBuilder"></param>
        /// <param name="name"></param>
        /// <param name="structureDefinition"></param>
        public DefaultComplexTypeFieldBuilder(
            DefaultComplexTypeBuilder defaultComplexTypeBuilder,
            QualifiedName name,
            StructureDefinition structureDefinition)
        {
            m_name = name;
            m_defaultComplexTypeBuilder = defaultComplexTypeBuilder;
            m_structureDefinition = structureDefinition;
        }

        /// <inheritdoc/>
        public void AddField(
            StructureField field,
            IType? fieldType,
            int order,
            bool allowSubTypes)
        {
            m_fieldTypes[field.Name] = fieldType switch
            {
                IEnumeratedType => BuiltInType.Enumeration,
                IBuiltInType builtIn => builtIn.BuiltInType,
                _ => BuiltInType.ExtensionObject
            };
        }

        /// <inheritdoc/>
        public void AddTypeIdAttribute(
            ExpandedNodeId complexTypeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId)
        {
            m_typeId = complexTypeId;
            m_binaryEncodingId = binaryEncodingId;
            m_xmlEncodingId = xmlEncodingId;
        }

        /// <inheritdoc/>
        public IEncodeableType CreateType()
        {
            var xmlName = new XmlQualifiedName(
                m_name.Name,
                m_defaultComplexTypeBuilder.TargetNamespace);
            switch (m_structureDefinition.StructureType)
            {
                case StructureType.Structure:
                case StructureType.StructureWithSubtypedValues:
                    m_structureToBuild = new Structure(
                        xmlName,
                        m_typeId,
                        m_binaryEncodingId,
                        m_xmlEncodingId,
                        m_structureDefinition,
                        m_fieldTypes);
                    break;
                case StructureType.StructureWithOptionalFields:
                    m_structureToBuild = new StructureWithOptionalFields(
                        xmlName,
                        m_typeId,
                        m_binaryEncodingId,
                        m_xmlEncodingId,
                        m_structureDefinition,
                        m_fieldTypes);
                    break;
                case StructureType.Union:
                case StructureType.UnionWithSubtypedValues:
                    m_structureToBuild = new Union(
                        xmlName,
                        m_typeId,
                        m_binaryEncodingId,
                        m_xmlEncodingId,
                        m_structureDefinition,
                        m_fieldTypes,
                        0);  // TODO: switch field?
                    break;
            }
            return m_structureToBuild!;
        }

        /// <inheritdoc/>
        public IEncodeableType GetStructureType()
        {
            return CreateType();
        }

        private readonly Dictionary<string, BuiltInType> m_fieldTypes = [];
        private Structure? m_structureToBuild;
        private ExpandedNodeId m_typeId;
        private ExpandedNodeId m_binaryEncodingId;
        private ExpandedNodeId m_xmlEncodingId;
        private readonly DefaultComplexTypeBuilder m_defaultComplexTypeBuilder;
        private readonly QualifiedName m_name;
        private readonly StructureDefinition m_structureDefinition;
    }
}
