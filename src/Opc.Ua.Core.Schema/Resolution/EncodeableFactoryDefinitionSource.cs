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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Resolves data type definitions from an <see cref="IEncodeableFactory"/>.
    /// Generated and other registered encodeable/enumerated types that
    /// implement <see cref="IDataTypeDefinitionSource"/> expose their
    /// definition, so any type known to the factory can produce a schema
    /// without being registered manually.
    /// </summary>
    [Experimental("UA_NETStandard_1")]
    public sealed class EncodeableFactoryDefinitionSource : IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="EncodeableFactoryDefinitionSource"/> class.
        /// </summary>
        /// <param name="factory">The encodeable factory to resolve types from.</param>
        /// <param name="namespaceUris">The namespace table used to materialize the
        /// definitions.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public EncodeableFactoryDefinitionSource(
            IEncodeableFactory factory,
            NamespaceTable namespaceUris)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            m_namespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
        }

        /// <inheritdoc/>
        public bool TryResolve(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            if (m_factory.TryGetEncodeableType(typeId, out IEncodeableType? encodeableType) &&
                encodeableType is IDataTypeDefinitionSource encodeableSource)
            {
                DataTypeDefinition encodeable = encodeableSource.GetDataTypeDefinition(m_namespaceUris);
                description = Describe(typeId, encodeableType.XmlName, encodeable);
                return true;
            }

            if (m_factory.TryGetEnumeratedType(typeId, out IEnumeratedType? enumeratedType) &&
                enumeratedType is IDataTypeDefinitionSource enumeratedSource)
            {
                DataTypeDefinition enumerated = enumeratedSource.GetDataTypeDefinition(m_namespaceUris);
                description = Describe(typeId, enumeratedType.XmlName, enumerated);
                return true;
            }

            description = null;
            return false;
        }

        /// <inheritdoc/>
        public bool TryResolve(
            NodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            return TryResolve(new ExpandedNodeId(typeId), out description);
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return [];
            }

            var result = new List<UaTypeDescription>();

            // The factory maps a type by its data type id and by each of its
            // encoding ids, so KnownTypeIds yields the same type several times.
            // De-duplicate by the type's (unique) qualified name so each data
            // type is reported once.
            var seen = new HashSet<XmlQualifiedName>();
            foreach (ExpandedNodeId typeId in m_factory.KnownTypeIds)
            {
                if (TryResolve(typeId, out UaTypeDescription? description) &&
                    string.Equals(description.NamespaceUri, namespaceUri, StringComparison.Ordinal) &&
                    seen.Add(new XmlQualifiedName(description.Name, description.NamespaceUri)))
                {
                    result.Add(description);
                }
            }
            return result;
        }

        private static UaTypeDescription Describe(
            ExpandedNodeId typeId,
            XmlQualifiedName xmlName,
            DataTypeDefinition definition)
        {
            string? namespaceUri = xmlName != null && !string.IsNullOrEmpty(xmlName.Namespace)
                ? xmlName.Namespace
                : typeId.NamespaceUri;
            var browseName = new QualifiedName(xmlName?.Name);
            return new UaTypeDescription(typeId, browseName, definition, namespaceUri);
        }

        private readonly IEncodeableFactory m_factory;
        private readonly NamespaceTable m_namespaceUris;
    }
}
