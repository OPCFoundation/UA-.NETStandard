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
                if (Volatile.Read(ref scope.m_active) != 0 && ReferenceEquals(scope.m_generation, generation))
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
                if (Volatile.Read(ref scope.m_active) != 0 && ReferenceEquals(scope.m_server, server))
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Volatile.Write(ref m_active, 0);
            s_current.Value = m_parent;
        }

        /// <summary>The initializer scope in this execution context, never a routing view.</summary>
        private static readonly AsyncLocal<NodeManagerReadinessScope?> s_current = new();

        /// <summary>The exact server owning this initializer.</summary>
        private readonly IServerInternal m_server;

        /// <summary>The pending generation accessible to this initializer.</summary>
        private readonly NodeManagerGeneration m_generation;

        /// <summary>The enclosing initializer, if a callback publishes a dependent generation.</summary>
        private readonly NodeManagerReadinessScope? m_parent;

        /// <summary>Whether captured execution contexts may still use this initializer capability.</summary>
        private int m_active = 1;
    }
}
