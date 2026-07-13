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
    /// </summary>
    public sealed class DataTypeDefinitionRegistry : IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Adds or replaces a data type description in the registry.
        /// </summary>
        /// <param name="description">The description to add.</param>
        /// <returns>The registry to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="description"/> is <c>null</c>.</exception>
        public DataTypeDefinitionRegistry Add(UaTypeDescription description)
        {
            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            NodeId key = description.TypeId.InnerNodeId;
            if (m_byNodeId.TryGetValue(key, out UaTypeDescription? existing) &&
                m_byNamespace.TryGetValue(existing.NamespaceUri, out List<UaTypeDescription>? existingList))
            {
                // Keep the namespace list consistent with the node-id map when a
                // type is re-registered (replace rather than leave a stale copy).
                existingList.Remove(existing);
            }
            m_byNodeId[key] = description;

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

        private readonly Dictionary<NodeId, UaTypeDescription> m_byNodeId = [];

        private readonly Dictionary<string, List<UaTypeDescription>> m_byNamespace =
            new(StringComparer.Ordinal);
    }
}
