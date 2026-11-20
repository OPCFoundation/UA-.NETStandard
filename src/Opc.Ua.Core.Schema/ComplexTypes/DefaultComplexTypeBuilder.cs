/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Xml;

namespace Opc.Ua
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
