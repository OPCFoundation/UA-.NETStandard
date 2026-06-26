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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Shared <see cref="ILocalAddressSpace"/> adapter over a node manager's
    /// <c>PredefinedNodes</c> dictionary. The snapshot/lookup behaviour is
    /// identical for the synchronous and asynchronous node manager base
    /// classes; the add and remove operations are supplied as delegates so the
    /// adapter can drive either the synchronous or the asynchronous predefined
    /// node pipeline without duplicating the boilerplate.
    /// </summary>
    internal sealed class PredefinedNodesAddressSpace : ILocalAddressSpace
    {
        /// <summary>
        /// Creates the adapter.
        /// </summary>
        /// <param name="context">The system context used to (de)serialize nodes.</param>
        /// <param name="predefinedNodes">The node manager's predefined node dictionary.</param>
        /// <param name="addAsync">Adds or replaces a node in the address space.</param>
        /// <param name="removeAsync">Removes a node from the address space.</param>
        public PredefinedNodesAddressSpace(
            ISystemContext context,
            NodeIdDictionary<NodeState> predefinedNodes,
            Func<NodeState, CancellationToken, ValueTask> addAsync,
            Func<NodeId, CancellationToken, ValueTask<bool>> removeAsync)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_predefinedNodes = predefinedNodes ?? throw new ArgumentNullException(nameof(predefinedNodes));
            m_addAsync = addAsync ?? throw new ArgumentNullException(nameof(addAsync));
            m_removeAsync = removeAsync ?? throw new ArgumentNullException(nameof(removeAsync));
        }

        /// <inheritdoc/>
        public ISystemContext Context => m_context;

        /// <inheritdoc/>
        public IEnumerable<NodeState> Nodes
        {
            get
            {
                var nodes = new List<NodeState>();
                foreach (NodeState node in m_predefinedNodes.Values)
                {
                    // Skip instance children whose parent is already a
                    // top-level predefined node; they travel with the parent.
                    if (node is BaseInstanceState instance &&
                        instance.Parent != null &&
                        !instance.Parent.NodeId.IsNull &&
                        m_predefinedNodes.ContainsKey(instance.Parent.NodeId))
                    {
                        continue;
                    }

                    nodes.Add(node);
                }

                return nodes;
            }
        }

        /// <inheritdoc/>
        public event Action<NodeState>? NodeAdded;

        /// <inheritdoc/>
        public event Action<NodeId>? NodeRemoved;

        /// <inheritdoc/>
        public bool TryGetNode(NodeId nodeId, [NotNullWhen(true)] out NodeState? node)
        {
            return m_predefinedNodes.TryGetValue(nodeId, out node);
        }

        /// <inheritdoc/>
        public async ValueTask AddOrUpdateNodeAsync(NodeState node, CancellationToken cancellationToken = default)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            await m_addAsync(node, cancellationToken).ConfigureAwait(false);
            NodeAdded?.Invoke(node);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveNodeAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            bool removed = await m_removeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (removed)
            {
                NodeRemoved?.Invoke(nodeId);
            }

            return removed;
        }

        private readonly ISystemContext m_context;
        private readonly NodeIdDictionary<NodeState> m_predefinedNodes;
        private readonly Func<NodeState, CancellationToken, ValueTask> m_addAsync;
        private readonly Func<NodeId, CancellationToken, ValueTask<bool>> m_removeAsync;
    }
}
