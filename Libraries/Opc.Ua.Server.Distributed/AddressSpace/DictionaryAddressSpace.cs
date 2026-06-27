/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a simple dictionary-backed <see cref="ILocalAddressSpace"/>. Suitable
    /// for hosting a flat set of top-level nodes and for unit/integration
    /// testing the synchronizer; a node manager provides its own adapter
    /// over <c>PredefinedNodes</c> in production.
    /// </summary>
    public sealed class DictionaryAddressSpace : ILocalAddressSpace
    {
        /// <summary>
        /// Creates an empty address space bound to a system context.
        /// </summary>
        /// <param name="context">The system context.</param>
        public DictionaryAddressSpace(ISystemContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public ISystemContext Context { get; }

        /// <inheritdoc/>
        public IEnumerable<NodeState> Nodes
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_nodes.Values];
                }
            }
        }

        /// <inheritdoc/>
        public event Action<NodeState>? NodeAdded;

        /// <inheritdoc/>
        public event Action<NodeId>? NodeRemoved;

        /// <inheritdoc/>
        public bool TryGetNode(NodeId nodeId, [NotNullWhen(true)] out NodeState? node)
        {
            lock (m_lock)
            {
                return m_nodes.TryGetValue(nodeId, out node);
            }
        }

        /// <inheritdoc/>
        public ValueTask AddOrUpdateNodeAsync(NodeState node, CancellationToken cancellationToken = default)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            lock (m_lock)
            {
                m_nodes[node.NodeId] = node;
            }
            NodeAdded?.Invoke(node);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveNodeAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            bool removed;
            lock (m_lock)
            {
                removed = m_nodes.TryRemove(nodeId, out _);
            }
            if (removed)
            {
                NodeRemoved?.Invoke(nodeId);
            }
            return new ValueTask<bool>(removed);
        }

        private readonly Lock m_lock = new();
        private readonly NodeIdDictionary<NodeState> m_nodes = [];
    }
}
