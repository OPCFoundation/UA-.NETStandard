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

namespace Opc.Ua.Schema
{
    /// <summary>
    /// An in-memory registry of data type descriptions used as the default
    /// <see cref="IDataTypeDefinitionResolver"/>. Generated and dynamically
    /// built complex types register their <see cref="DataTypeDefinition"/> here
    /// so that schemas can be produced without reflection. The registry is
    /// intended to be populated during application start-up before it is read.
    /// Local namespace indexes in one registry must belong to the same namespace
    /// table context.
    /// </summary>
    public sealed class DataTypeDefinitionRegistry : IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Adds or replaces a data type description in the registry.
        /// </summary>
        /// <param name="description">The description to add.</param>
        /// <returns>The registry to allow chaining.</returns>
        /// <remarks>
        /// An index-form type id paired with its namespace URI supports lookup in both forms.
        /// Custom URI-only type ids do not imply a local namespace index; the BrowseName namespace
        /// is not used to infer one. The standard OPC UA namespace URI maps to index zero.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="description"/> is <c>null</c>.</exception>
        public DataTypeDefinitionRegistry Add(UaTypeDescription description)
        {
            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            bool hasNodeIdKey = TryGetNodeIdKey(description, out NodeId nodeIdKey);
            bool hasNamespaceUriKey = TryGetNamespaceUriKey(
                description,
                out (string NamespaceUri, IdType IdentifierKind, NodeId Identifier) namespaceUriKey);
            UaTypeDescription? existingByNodeId = null;
            if (hasNodeIdKey)
            {
                m_byNodeId.TryGetValue(nodeIdKey, out existingByNodeId);
            }

            UaTypeDescription? existingByNamespaceUri = null;
            if (hasNamespaceUriKey)
            {
                m_byNamespaceUri.TryGetValue(namespaceUriKey, out existingByNamespaceUri);
            }

            if (existingByNodeId != null)
            {
                Remove(existingByNodeId);
            }
            if (existingByNamespaceUri != null &&
                !ReferenceEquals(existingByNamespaceUri, existingByNodeId))
            {
                Remove(existingByNamespaceUri);
            }

            if (hasNodeIdKey)
            {
                m_byNodeId[nodeIdKey] = description;
            }

            if (hasNamespaceUriKey)
            {
                m_byNamespaceUri[namespaceUriKey] = description;
            }

            if (!m_byNamespace.TryGetValue(description.NamespaceUri, out List<UaTypeDescription>? list))
            {
                list = [];
                m_byNamespace[description.NamespaceUri] = list;
            }
            list.Add(description);
            return this;
        }

        /// <inheritdoc/>
        public bool TryResolve(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            if (!string.IsNullOrEmpty(typeId.NamespaceUri))
            {
                return m_byNamespaceUri.TryGetValue(
                    CreateNamespaceUriKey(typeId.NamespaceUri, typeId.InnerNodeId),
                    out description);
            }

            return TryResolve(typeId.InnerNodeId, out description);
        }

        /// <inheritdoc/>
        public bool TryResolve(
            NodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            return m_byNodeId.TryGetValue(typeId, out description);
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
        {
            if (namespaceUri != null &&
                m_byNamespace.TryGetValue(namespaceUri, out List<UaTypeDescription>? list))
            {
                // Return a snapshot so a later registration cannot invalidate an
                // in-progress namespace enumeration.
                return [.. list];
            }
            return [];
        }

        private void Remove(UaTypeDescription description)
        {
            if (TryGetNodeIdKey(description, out NodeId nodeIdKey) &&
                m_byNodeId.TryGetValue(nodeIdKey, out UaTypeDescription? nodeIdDescription) &&
                ReferenceEquals(nodeIdDescription, description))
            {
                m_byNodeId.Remove(nodeIdKey);
            }

            if (TryGetNamespaceUriKey(
                    description,
                    out (string NamespaceUri, IdType IdentifierKind, NodeId Identifier) namespaceUriKey) &&
                m_byNamespaceUri.TryGetValue(
                    namespaceUriKey,
                    out UaTypeDescription? namespaceUriDescription) &&
                ReferenceEquals(namespaceUriDescription, description))
            {
                m_byNamespaceUri.Remove(namespaceUriKey);
            }

            if (m_byNamespace.TryGetValue(
                    description.NamespaceUri,
                    out List<UaTypeDescription>? namespaceTypes))
            {
                namespaceTypes.Remove(description);
                if (namespaceTypes.Count == 0)
                {
                    m_byNamespace.Remove(description.NamespaceUri);
                }
            }
        }

        private static bool TryGetNodeIdKey(
            UaTypeDescription description,
            out NodeId nodeIdKey)
        {
            if (string.IsNullOrEmpty(description.TypeId.NamespaceUri))
            {
                nodeIdKey = description.TypeId.InnerNodeId;
                return true;
            }

            if (string.Equals(description.TypeId.NamespaceUri, Namespaces.OpcUa, StringComparison.Ordinal))
            {
                nodeIdKey = description.TypeId.InnerNodeId;
                return true;
            }

            nodeIdKey = NodeId.Null;
            return false;
        }

        private static bool TryGetNamespaceUriKey(
            UaTypeDescription description,
            out (string NamespaceUri, IdType IdentifierKind, NodeId Identifier) namespaceUriKey)
        {
            if (!string.IsNullOrEmpty(description.NamespaceUri))
            {
                namespaceUriKey = CreateNamespaceUriKey(
                    description.NamespaceUri,
                    description.TypeId.InnerNodeId);
                return true;
            }

            namespaceUriKey = default;
            return false;
        }

        private static (string NamespaceUri, IdType IdentifierKind, NodeId Identifier) CreateNamespaceUriKey(
            string namespaceUri,
            NodeId nodeId)
        {
            // NodeId equality treats zero/empty identifiers of different kinds as equal in namespace zero.
            return (namespaceUri, nodeId.IdType, nodeId.WithNamespaceIndex(0));
        }

        private readonly Dictionary<NodeId, UaTypeDescription> m_byNodeId = [];

        private readonly Dictionary<(string NamespaceUri, IdType IdentifierKind, NodeId Identifier), UaTypeDescription>
            m_byNamespaceUri = [];

        private readonly Dictionary<string, List<UaTypeDescription>> m_byNamespace =
            new(StringComparer.Ordinal);
    }
}
