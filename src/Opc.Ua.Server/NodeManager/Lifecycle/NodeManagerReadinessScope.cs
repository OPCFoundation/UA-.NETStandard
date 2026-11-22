// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Grants an initializer access to its pending generation without selecting request routes.
    /// </summary>
    internal sealed class NodeManagerReadinessScope : IDisposable
    {
        /// <summary>
        /// Creates explicit readiness admission without changing the execution context.
        /// </summary>
        internal NodeManagerReadinessScope(
            IServerInternal server,
            NodeManagerGeneration generation,
            NodeManagerReadinessScope? parent = null)
        {
            m_server = server;
            m_generation = generation;
            m_parent = parent;
        }

        /// <summary>
        /// Gets whether this initializer has not yet revoked its context.
        /// </summary>
        internal bool IsActive => Volatile.Read(ref m_active) != 0;

        /// <summary>
        /// Checks pending-generation access on an explicitly selected initializer chain.
        /// </summary>
        /// <param name="generation">The generation selected by explicit request routing.</param>
        /// <returns>Whether an active initializer in the chain owns this generation.</returns>
        internal bool Includes(NodeManagerGeneration generation)
        {
            for (NodeManagerReadinessScope? scope = this; scope is not null;
                scope = Volatile.Read(ref scope.m_parent))
            {
                NodeManagerGeneration? owner = Volatile.Read(ref scope.m_generation);
                if (!scope.IsActive)
                {
                    return false;
                }
                if (ReferenceEquals(owner, generation))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks startup admission for the exact server.
        /// </summary>
        /// <param name="server">The server receiving the request.</param>
        /// <returns>Whether an active initializer belongs to this server.</returns>
        internal bool Includes(IServerInternal server)
        {
            for (NodeManagerReadinessScope? scope = this; scope is not null;
                scope = Volatile.Read(ref scope.m_parent))
            {
                IServerInternal? owner = Volatile.Read(ref scope.m_server);
                if (!scope.IsActive)
                {
                    return false;
                }
                if (ReferenceEquals(owner, server))
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_active, 0) == 0)
            {
                return;
            }

            Interlocked.Exchange(ref m_parent, null);
            Interlocked.Exchange(ref m_server, null);
            Interlocked.Exchange(ref m_generation, null);
        }

        /// <summary>The exact server owning this initializer.</summary>
        private IServerInternal? m_server;

        /// <summary>The pending generation accessible to this initializer.</summary>
        private NodeManagerGeneration? m_generation;

        /// <summary>The enclosing initializer, if a callback publishes a dependent generation.</summary>
        private NodeManagerReadinessScope? m_parent;

        /// <summary>Whether holders of this scope may still use this initializer capability.</summary>
        private int m_active = 1;
    }
}
