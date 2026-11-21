// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Extension methods that populate a <see cref="DataTypeDefinitionRegistry"/>
    /// from OPC UA address-space nodes.
    /// </summary>
    public static class DataTypeDefinitionRegistryExtensions
    {
        /// <summary>
        /// Registers the data type definition carried by an address-space
        /// <see cref="DataTypeNode"/> (for example a node obtained by browsing a
        /// server or from the client node cache) so a schema can be generated
        /// for it.
        /// </summary>
        /// <param name="registry">The registry to add the data type to.</param>
        /// <param name="node">The data type node.</param>
        /// <param name="namespaceUris">The namespace table used to resolve the
        /// node namespace uri. May be <c>null</c>.</param>
        /// <returns>
        /// <c>true</c> when the node carried a usable structure or enum
        /// definition and was added; otherwise <c>false</c>.
        /// </returns>
        /// <remarks>
        /// Supply <paramref name="namespaceUris"/> when the registered type
        /// must also resolve from a namespace-URI-form
        /// <see cref="ExpandedNodeId"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public static bool TryAddDataType(
            this DataTypeDefinitionRegistry registry,
            DataTypeNode node,
            NamespaceTable? namespaceUris = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.NodeId.IsNull ||
                node.DataTypeDefinition.IsNull ||
                !node.DataTypeDefinition.TryGetValue(out DataTypeDefinition? definition))
            {
                return false;
            }

            string namespaceUri = namespaceUris?.GetString(node.NodeId.NamespaceIndex) ?? string.Empty;
            registry.Add(new UaTypeDescription(
                new ExpandedNodeId(node.NodeId),
                node.BrowseName,
                definition,
                namespaceUri));
            return true;
        }
    }
}
