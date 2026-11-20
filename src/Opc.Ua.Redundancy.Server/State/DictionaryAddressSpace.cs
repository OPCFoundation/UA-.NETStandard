/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
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
        public ValueTask AddOrUpdateRangeAsync(
            IEnumerable<NodeState> nodes,
            CancellationToken cancellationToken = default)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            lock (m_lock)
            {
                foreach (NodeState node in nodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    m_nodes[node.NodeId] = node;
                }
            }
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
