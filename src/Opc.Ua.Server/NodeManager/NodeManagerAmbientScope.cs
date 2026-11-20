/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Revokes an operation's ambient value in every captured context when that operation ends.
    /// </summary>
    /// <typeparam name="T">The operation's guard value.</typeparam>
    internal sealed class NodeManagerAmbientScope<T> : IDisposable
        where T : struct
    {
        /// <summary>
        /// Installs a guard while retaining the enclosing scope for nested operations.
        /// </summary>
        /// <param name="current">The operation's ambient slot.</param>
        /// <param name="value">The value available while this scope is active.</param>
        internal NodeManagerAmbientScope(AsyncLocal<NodeManagerAmbientScope<T>?> current, T value)
        {
            m_current = current;
            m_previous = current.Value;
            m_value = value;
            current.Value = this;
        }

        /// <summary>
        /// Gets the nearest active guard, skipping ended scopes in captured contexts.
        /// </summary>
        /// <param name="current">The operation's ambient slot.</param>
        /// <returns>The active guard value, or its default when every enclosing scope has ended.</returns>
        internal static T GetValue(AsyncLocal<NodeManagerAmbientScope<T>?> current)
        {
            for (NodeManagerAmbientScope<T>? scope = current.Value; scope is not null; scope = scope.m_previous)
            {
                if (Volatile.Read(ref scope.m_active) != 0)
                {
                    return scope.m_value;
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Volatile.Write(ref m_active, 0);
            if (ReferenceEquals(m_current.Value, this))
            {
                m_current.Value = m_previous;
            }
        }

        private readonly AsyncLocal<NodeManagerAmbientScope<T>?> m_current;
        private readonly NodeManagerAmbientScope<T>? m_previous;
        private readonly T m_value;
        private int m_active = 1;
    }
}
