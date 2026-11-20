/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Opc.Ua.Schema;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A mutable <see cref="IDataTypeDefinitionResolver"/> whose backing
    /// resolver is filled once the server address space and encodeable factory
    /// are available (during startup). This lets the server expose its runtime
    /// encodeable factory as the schema data type definition resolver through
    /// dependency injection, where the factory instance is not known until the
    /// server has started.
    /// </summary>
    internal sealed class ServerDataTypeDefinitionResolver : IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Sets the resolver that backs this holder.
        /// </summary>
        /// <param name="resolver">The backing resolver.</param>
        public void SetResolver(IDataTypeDefinitionResolver resolver)
        {
            m_resolver = resolver;
        }

        /// <inheritdoc/>
        public bool TryResolve(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            IDataTypeDefinitionResolver? resolver = m_resolver;
            if (resolver != null)
            {
                return resolver.TryResolve(typeId, out description);
            }
            description = null;
            return false;
        }

        /// <inheritdoc/>
        public bool TryResolve(
            NodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            IDataTypeDefinitionResolver? resolver = m_resolver;
            if (resolver != null)
            {
                return resolver.TryResolve(typeId, out description);
            }
            description = null;
            return false;
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
        {
            return m_resolver?.GetNamespaceTypes(namespaceUri) ?? [];
        }

        private volatile IDataTypeDefinitionResolver? m_resolver;
    }
}
