/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
        /// Enters a revocable readiness callback scope for one server and generation.
        /// </summary>
        /// <param name="server">The exact server being initialized.</param>
        /// <param name="generation">The generation whose initializer is running.</param>
        internal NodeManagerReadinessScope(IServerInternal server, NodeManagerGeneration generation)
        {
            m_server = server;
            m_generation = generation;
            m_parent = s_current.Value;
            s_current.Value = this;
        }

        /// <summary>
        /// Determines whether the current initializer may use this pending generation.
        /// </summary>
        /// <param name="generation">The generation selected by explicit request routing.</param>
        /// <returns>Whether an active initializer owns this generation.</returns>
        internal static bool Allows(NodeManagerGeneration generation)
        {
            for (NodeManagerReadinessScope? scope = s_current.Value; scope is not null; scope = scope.m_parent)
            {
                if (Volatile.Read(ref scope.m_active) != 0 &&
                    ReferenceEquals(Volatile.Read(ref scope.m_generation), generation) &&
                    Volatile.Read(ref scope.m_active) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether the current initializer may invoke services during this server's startup.
        /// </summary>
        /// <param name="server">The exact server being initialized.</param>
        /// <returns>Whether an active initializer belongs to this server.</returns>
        internal static bool Allows(IServerInternal server)
        {
            for (NodeManagerReadinessScope? scope = s_current.Value; scope is not null; scope = scope.m_parent)
            {
                if (Volatile.Read(ref scope.m_active) != 0 &&
                    ReferenceEquals(Volatile.Read(ref scope.m_server), server) &&
                    Volatile.Read(ref scope.m_active) != 0)
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

            NodeManagerReadinessScope? parent = Interlocked.Exchange(ref m_parent, null);
            if (ReferenceEquals(s_current.Value, this))
            {
                s_current.Value = parent;
            }
            Interlocked.Exchange(ref m_server, null);
            Interlocked.Exchange(ref m_generation, null);
        }

        /// <summary>The initializer scope in this execution context, never a routing view.</summary>
        private static readonly AsyncLocal<NodeManagerReadinessScope?> s_current = new();

        /// <summary>The exact server owning this initializer.</summary>
        private IServerInternal? m_server;

        /// <summary>The pending generation accessible to this initializer.</summary>
        private NodeManagerGeneration? m_generation;

        /// <summary>The enclosing initializer, if a callback publishes a dependent generation.</summary>
        private NodeManagerReadinessScope? m_parent;

        /// <summary>Whether captured execution contexts may still use this initializer capability.</summary>
        private int m_active = 1;
    }
}
