// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Resolves data type definitions from an <see cref="IEncodeableFactory"/>.
    /// Generated type activators and other registered encodeable/enumerated types that
    /// implement <see cref="IDataTypeDefinitionSource"/> expose their
    /// definition, so any type known to the factory can produce a schema
    /// without being registered manually.
    /// </summary>
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
            if (TryResolveExact(typeId, out description))
            {
                return true;
            }

            if (typeId.ServerIndex == 0)
            {
                if (string.IsNullOrEmpty(typeId.NamespaceUri))
                {
                    ushort namespaceIndex = typeId.InnerNodeId.NamespaceIndex;
                    if (namespaceIndex > 0)
                    {
                        string? namespaceUri = m_namespaceUris.GetString(namespaceIndex);
                        if (!string.IsNullOrEmpty(namespaceUri))
                        {
                            return TryResolveExact(
                                new ExpandedNodeId(typeId.InnerNodeId, namespaceUri),
                                out description);
                        }
                    }
                }
                else
                {
                    int namespaceIndex = m_namespaceUris.GetIndex(typeId.NamespaceUri);
                    if (namespaceIndex >= 0 && namespaceIndex <= ushort.MaxValue)
                    {
                        return TryResolveExact(
                            new ExpandedNodeId(
                                CreateLocalNodeId(typeId.InnerNodeId, (ushort)namespaceIndex)),
                            out description);
                    }
                }
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

        private bool TryResolveExact(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            if (m_factory.TryGetEncodeableType(typeId, out IEncodeableType? encodeableType) &&
                encodeableType is IDataTypeDefinitionSource encodeableSource)
            {
                DataTypeDefinition encodeable = encodeableSource.GetDataTypeDefinition(m_namespaceUris);

                // an encodeable OptionSet is a subtype of the OptionSet structure {Value, ValidBits}.
                description = Describe(
                    typeId,
                    encodeableType.XmlName,
                    encodeable,
                    isStructureOptionSet: encodeable is EnumDefinition { IsOptionSet: true });
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

        private static NodeId CreateLocalNodeId(NodeId identifier, ushort namespaceIndex)
        {
            // WithNamespaceIndex preserves null NodeIds, but zero/empty identifiers are valid outside namespace zero.
            if (identifier.TryGetValue(out uint numeric))
            {
                return new NodeId(numeric, namespaceIndex);
            }
            if (identifier.TryGetValue(out string text))
            {
                return new NodeId(text, namespaceIndex);
            }
            if (identifier.TryGetValue(out Guid guid))
            {
                return new NodeId(guid, namespaceIndex);
            }
            if (identifier.TryGetValue(out ByteString opaque))
            {
                return new NodeId(opaque, namespaceIndex);
            }
            throw ServiceResultException.Unexpected($"Unexpected IdType value {identifier.IdType}.");
        }

        private static UaTypeDescription Describe(
            ExpandedNodeId typeId,
            XmlQualifiedName xmlName,
            DataTypeDefinition definition,
            bool isStructureOptionSet = false)
        {
            string? namespaceUri = xmlName != null && !string.IsNullOrEmpty(xmlName.Namespace)
                ? xmlName.Namespace
                : typeId.NamespaceUri;
            var browseName = new QualifiedName(xmlName?.Name);
            return new UaTypeDescription(typeId, browseName, definition, namespaceUri, isStructureOptionSet);
        }

        private readonly IEncodeableFactory m_factory;
        private readonly NamespaceTable m_namespaceUris;
    }
}
