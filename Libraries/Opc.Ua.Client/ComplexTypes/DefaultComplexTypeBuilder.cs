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

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Default complex type builder
    /// </summary>
    internal sealed class DefaultComplexTypeBuilder : IComplexTypeBuilder
    {
        /// <summary>
        /// Create type builder
        /// </summary>
        public DefaultComplexTypeBuilder(
            DefaultComplexTypeFactory defaultComplexTypeFactory,
            string targetNamespace,
            int targetNamespaceIndex)
        {
            TargetNamespace = targetNamespace;
            TargetNamespaceIndex = targetNamespaceIndex;
            m_defaultComplexTypeFactory = defaultComplexTypeFactory;
        }

        /// <inheritdoc/>
        public string TargetNamespace { get; }

        /// <inheritdoc/>
        public int TargetNamespaceIndex { get; }

        /// <inheritdoc/>
        public IEnumeratedType AddEnumType(
            QualifiedName typeName,
            EnumDefinition enumDefinition)
        {
            var xmlName = new XmlQualifiedName(typeName.Name, TargetNamespace);
            var type = new Encoders.Enumeration(xmlName, enumDefinition);
            OnTypeCreated(type);
            return type;
        }

        /// <inheritdoc/>
        public IComplexTypeFieldBuilder AddStructuredType(
            QualifiedName name,
            StructureDefinition structureDefinition)
        {
            return new DefaultComplexTypeFieldBuilder(this, name, structureDefinition);
        }

        /// <inheritdoc/>
        public IEncodeableType AddOptionSetType(
            QualifiedName typeName,
            ExpandedNodeId typeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId,
            EnumDefinition enumDefinition)
        {
            var xmlName = new XmlQualifiedName(typeName.Name, TargetNamespace);
            var type = new Encoders.OptionSet(
                xmlName,
                typeId,
                binaryEncodingId,
                xmlEncodingId,
                enumDefinition);
            OnTypeCreated(type);
            return type;
        }

        /// <summary>
        /// Type created
        /// </summary>
        internal void OnTypeCreated(IType type)
        {
            m_defaultComplexTypeFactory.OnTypeCreated(type);
        }

        private readonly DefaultComplexTypeFactory m_defaultComplexTypeFactory;
    }
}
