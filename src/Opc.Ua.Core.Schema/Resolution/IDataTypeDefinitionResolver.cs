// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Resolves the runtime structure definition for an OPC UA data type id.
    /// Implementations may aggregate multiple sources (registered generated
    /// types, dynamically built complex types, or a server address space).
    /// </summary>
    public interface IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Resolves the type description for the supplied data type id.
        /// </summary>
        /// <param name="typeId">The data type id to resolve, in namespace-index
        /// or namespace-URI form.</param>
        /// <param name="description">The resolved type description.</param>
        /// <returns><c>true</c> when the type was resolved.</returns>
        bool TryResolve(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description);

        /// <summary>
        /// Resolves the type description for the supplied data type id.
        /// </summary>
        /// <param name="typeId">The data type id to resolve.</param>
        /// <param name="description">The resolved type description.</param>
        /// <returns><c>true</c> when the type was resolved.</returns>
        bool TryResolve(
            NodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description);

        /// <summary>
        /// Returns all resolvable data types of a namespace.
        /// </summary>
        /// <param name="namespaceUri">The namespace uri.</param>
        /// <returns>The data types in the namespace.</returns>
        IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri);
    }
}
