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
            return new Enumeration(
                new XmlQualifiedName(typeName.Name, TargetNamespace),
                enumDefinition);
        }

        /// <inheritdoc/>
        public IComplexTypeFieldBuilder AddStructuredType(
            QualifiedName name,
            StructureDefinition structureDefinition)
        {
            return new DefaultComplexTypeFieldBuilder(this, name, structureDefinition);
        }
    }

    /// <summary>
    /// Enumeration definition adapter
    /// </summary>
    public sealed class Enumeration : IEnumeratedType
    {
        /// <summary>
        /// Create enumeration
        /// </summary>
        public Enumeration(XmlQualifiedName name, EnumDefinition definition)
        {
            m_definition = definition;
            m_name = name;
        }

        /// <inheritdoc/>
        public EnumValue Default => default;

        /// <inheritdoc/>
        public Type Type => GetType();

        /// <inheritdoc/>
        public XmlQualifiedName XmlName => m_name;

        /// <inheritdoc/>
        public bool TryGetSymbol(int value, out string? symbol)
        {
            symbol = default;
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetValue(string symbol, out int value)
        {
            value = default;
            return false;
        }

        private readonly EnumDefinition m_definition;
        private readonly XmlQualifiedName m_name;
    }
}
