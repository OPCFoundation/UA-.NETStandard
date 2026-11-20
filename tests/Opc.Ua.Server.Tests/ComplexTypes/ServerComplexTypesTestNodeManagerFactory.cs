// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// <see cref="INodeManagerFactory"/> for the
    /// <see cref="ServerComplexTypesTestNodeManager"/> so it can be plugged into
    /// a server through the standard node manager factory collection.
    /// </summary>
    internal sealed class ServerComplexTypesTestNodeManagerFactory : INodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [ServerComplexTypesTestNodeManager.NamespaceUri];

        /// <inheritdoc/>
        public INodeManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            // Ownership of the node manager is transferred to the server.
#pragma warning disable CA2000 // Ownership of the node manager is transferred to the server.
            return new ServerComplexTypesTestNodeManager(server, configuration);
#pragma warning restore CA2000
        }
    }
}
