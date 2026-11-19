/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
            : this(server, generation, s_current.Value, ambient: true)
        {
        }

        /// <summary>
        /// Creates explicit readiness admission without changing the execution context.
        /// </summary>
        internal NodeManagerReadinessScope(
            IServerInternal server,
            NodeManagerGeneration generation,
            NodeManagerReadinessScope? parent,
            bool ambient)
        {
            m_server = server;
            m_generation = generation;
            m_parent = parent;
            if (parent is not null)
            {
                Interlocked.Increment(ref parent.m_liveChildren);
            }
            m_ambient = ambient;
            if (ambient)
            {
                m_previous = s_current.Value;
                s_current.Value = this;
            }
        }

        /// <summary>
        /// Gets the initializer at a context-free entry point.
        /// </summary>
        internal static NodeManagerReadinessScope? Current => s_current.Value;

        /// <summary>
        /// Determines whether the current initializer may use this pending generation.
        /// </summary>
        /// <param name="generation">The generation selected by explicit request routing.</param>
        /// <returns>Whether an active initializer owns this generation.</returns>
        internal static bool Allows(NodeManagerGeneration generation)
        {
            return Current?.Includes(generation) == true;
        }

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
            return Current?.Includes(server) == true;
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

            NodeManagerReadinessScope? parent = m_parent;
            // A live child cannot traverse a revoked ancestor to reach an older initializer.
            if (Volatile.Read(ref m_liveChildren) != 0 ||
                (m_ambient && !ReferenceEquals(s_current.Value, this)))
            {
                Interlocked.Exchange(ref m_parent, null);
            }
            if (m_ambient && ReferenceEquals(s_current.Value, this))
            {
                s_current.Value = m_previous;
            }
            Interlocked.Exchange(ref m_server, null);
            Interlocked.Exchange(ref m_generation, null);
            Interlocked.Exchange(ref m_previous, null);
            if (parent is not null)
            {
                Interlocked.Decrement(ref parent.m_liveChildren);
            }
        }

        /// <summary>The initializer scope in this execution context, never a routing view.</summary>
        private static readonly AsyncLocal<NodeManagerReadinessScope?> s_current = new();

        private readonly bool m_ambient;
        private NodeManagerReadinessScope? m_previous;

        /// <summary>The exact server owning this initializer.</summary>
        private IServerInternal? m_server;

        /// <summary>The pending generation accessible to this initializer.</summary>
        private NodeManagerGeneration? m_generation;

        /// <summary>The enclosing initializer, if a callback publishes a dependent generation.</summary>
        private NodeManagerReadinessScope? m_parent;

        /// <summary>Counts initializer scopes linked to this scope that have not yet been revoked.</summary>
        private int m_liveChildren;

        /// <summary>Whether captured execution contexts may still use this initializer capability.</summary>
        private int m_active = 1;
    }
}
