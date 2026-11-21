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
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Defers local targets from every browser source to the operation's routing view.
    /// </summary>
    internal sealed class OverrideNodeBrowser : INodeBrowser, IAsyncDisposable
    {
        /// <summary>
        /// Reports whether the built-in browser wrapper can defer the supplied exact local targets.
        /// </summary>
        internal static bool Supports(ArrayOf<NodeId> targets)
        {
            if (targets.IsNull)
            {
                return false;
            }
            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index].IsNull)
                {
                    return false;
                }
            }
            return true;
        }

        private OverrideNodeBrowser(INodeBrowser browser, HashSet<NodeId> overrides)
        {
            m_browser = browser;
            m_overrides = overrides;
        }

        /// <summary>
        /// Wraps a populated browser without changing its creation or population hooks.
        /// </summary>
        internal static INodeBrowser Wrap(
            INodeBrowser browser,
            NodeManagerRoutingTable.RoutingSnapshot? snapshot)
        {
            return snapshot is null || snapshot.NodeOverrides.Count == 0
                ? browser
                : new OverrideNodeBrowser(browser, [.. snapshot.NodeOverrides.Keys]);
        }

        /// <inheritdoc/>
        public IReference? Next()
        {
            return Defer(m_browser.Next());
        }

        /// <inheritdoc/>
        public async ValueTask<IReference?> NextAsync(CancellationToken cancellationToken = default)
        {
            return Defer(await m_browser.NextAsync(cancellationToken).ConfigureAwait(false));
        }

        /// <inheritdoc/>
        public void Push(IReference reference)
        {
            try
            {
                m_browser.Push(ReferenceEquals(reference, m_lastVisible) ? m_lastOriginal! : reference);
            }
            finally
            {
                m_lastOriginal = null;
                m_lastVisible = null;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_lastOriginal = null;
            m_lastVisible = null;
            m_browser.Dispose();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            m_lastOriginal = null;
            m_lastVisible = null;
            if (m_browser is IAsyncDisposable asynchronous)
            {
                return asynchronous.DisposeAsync();
            }
            m_browser.Dispose();
            return default;
        }

        private IReference? Defer(IReference? reference)
        {
            m_lastOriginal = reference;
            m_lastVisible = reference;
            if (reference is NodeStateReference { Target: not null } local &&
                !local.TargetId.IsAbsolute &&
                m_overrides.Contains((NodeId)local.TargetId))
            {
                m_lastVisible = new NodeStateReference(
                    local.ReferenceTypeId, local.IsInverse, local.TargetId);
            }
            return m_lastVisible;
        }

        private readonly INodeBrowser m_browser;
        private readonly HashSet<NodeId> m_overrides;
        private IReference? m_lastOriginal;
        private IReference? m_lastVisible;
    }
}
