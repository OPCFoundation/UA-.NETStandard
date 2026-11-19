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
            if (parent is not null)
            {
                Interlocked.Increment(ref parent.m_liveChildren);
            }
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
            if (Volatile.Read(ref m_liveChildren) != 0)
            {
                Interlocked.Exchange(ref m_parent, null);
            }
            Interlocked.Exchange(ref m_server, null);
            Interlocked.Exchange(ref m_generation, null);
            if (parent is not null)
            {
                Interlocked.Decrement(ref parent.m_liveChildren);
            }
        }

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
