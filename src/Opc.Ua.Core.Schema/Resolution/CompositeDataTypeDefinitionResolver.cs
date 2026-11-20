/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Aggregates multiple <see cref="IDataTypeDefinitionResolver"/> sources and
    /// resolves a type from the first source that knows it. Used to combine an
    /// explicit <see cref="DataTypeDefinitionRegistry"/> with, for example, an
    /// <see cref="EncodeableFactoryDefinitionSource"/>.
    /// </summary>
    public sealed class CompositeDataTypeDefinitionResolver : IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="CompositeDataTypeDefinitionResolver"/> class.
        /// </summary>
        /// <param name="resolvers">The resolver sources, tried in order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolvers"/> is <c>null</c>.</exception>
        public CompositeDataTypeDefinitionResolver(IEnumerable<IDataTypeDefinitionResolver> resolvers)
        {
            if (resolvers == null)
            {
                throw new ArgumentNullException(nameof(resolvers));
            }
            m_resolvers = [.. resolvers];
        }

        /// <inheritdoc/>
        public bool TryResolve(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            for (int i = 0; i < m_resolvers.Count; i++)
            {
                if (m_resolvers[i].TryResolve(typeId, out description))
                {
                    return true;
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
            for (int i = 0; i < m_resolvers.Count; i++)
            {
                if (m_resolvers[i].TryResolve(typeId, out description))
                {
                    return true;
                }
            }
            description = null;
            return false;
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
        {
            var result = new List<UaTypeDescription>();
            var seen = new HashSet<NodeId>();
            for (int i = 0; i < m_resolvers.Count; i++)
            {
                foreach (UaTypeDescription description in m_resolvers[i].GetNamespaceTypes(namespaceUri))
                {
                    if (seen.Add(description.TypeId.InnerNodeId))
                    {
                        result.Add(description);
                    }
                }
            }
            return result;
        }

        private readonly List<IDataTypeDefinitionResolver> m_resolvers;
    }
}
