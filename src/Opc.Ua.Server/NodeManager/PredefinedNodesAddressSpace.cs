// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_predefinedNodes = predefinedNodes ?? throw new ArgumentNullException(nameof(predefinedNodes));
            m_addAsync = addAsync ?? throw new ArgumentNullException(nameof(addAsync));
            m_removeAsync = removeAsync ?? throw new ArgumentNullException(nameof(removeAsync));
        }

        /// <inheritdoc/>
        public ISystemContext Context { get; }

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

            int depth = m_drivingOperation.Value;
            m_drivingOperation.Value = depth + 1;
            try
            {
                await m_addAsync(node, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_drivingOperation.Value = depth;
            }
            if (!m_predefinedNodes.TryGetValue(node.NodeId, out NodeState? activeNode))
            {
                throw new InvalidOperationException(
                    $"The add pipeline completed without registering node '{node.NodeId}'.");
            }
            NodeAdded?.Invoke(activeNode);
        }

        /// <inheritdoc/>
        public async ValueTask AddOrUpdateRangeAsync(
            IEnumerable<NodeState> nodes,
            CancellationToken cancellationToken = default)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            foreach (NodeState node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int depth = m_drivingOperation.Value;
                m_drivingOperation.Value = depth + 1;
                try
                {
                    await m_addAsync(node, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    m_drivingOperation.Value = depth;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveNodeAsync(NodeId nodeId, CancellationToken cancellationToken = default)
        {
            int depth = m_drivingOperation.Value;
            m_drivingOperation.Value = depth + 1;
            bool removed;
            try
            {
                removed = await m_removeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_drivingOperation.Value = depth;
            }
            if (removed)
            {
                NodeRemoved?.Invoke(nodeId);
            }

            return removed;
        }

        internal void NotifyAdded(NodeState node)
        {
            if (m_drivingOperation.Value == 0)
            {
                NodeAdded?.Invoke(node);
            }
        }

        internal void NotifyRemoved(NodeId nodeId)
        {
            if (m_drivingOperation.Value == 0)
            {
                NodeRemoved?.Invoke(nodeId);
            }
        }

        private readonly NodeIdDictionary<NodeState> m_predefinedNodes;
        private readonly Func<NodeState, CancellationToken, ValueTask> m_addAsync;
        private readonly Func<NodeId, CancellationToken, ValueTask<bool>> m_removeAsync;
        private readonly AsyncLocal<int> m_drivingOperation = new();
    }
}
