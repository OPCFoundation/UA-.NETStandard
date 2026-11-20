// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace RedundantPubSub
{
    /// <summary>
    /// In-process <see cref="ILeaderElection"/> coordinator used by the single-container demo
    /// role to switch leadership between two co-hosted publishers deterministically.
    /// </summary>
    internal sealed class ManualLeaderElection
    {
        /// <summary>
        /// Creates a leader-election view bound to a specific owner id.
        /// </summary>
        /// <param name="ownerId">Identifier of the owner the returned election represents.</param>
        /// <returns>A per-owner <see cref="ILeaderElection"/>.</returns>
        public ILeaderElection ForOwner(string ownerId)
        {
            return new Node(this, ownerId);
        }

        /// <summary>
        /// Sets the current leader and notifies every registered owner of the change.
        /// </summary>
        /// <param name="ownerId">Identifier of the new leader.</param>
        public void SetLeader(string ownerId)
        {
            List<Node> nodes;
            lock (m_lock)
            {
                m_leader = ownerId;
                nodes = [.. m_nodes];
            }

            foreach (Node node in nodes)
            {
                node.Notify();
            }
        }

        private bool IsLeader(string ownerId)
        {
            lock (m_lock)
            {
                return string.Equals(m_leader, ownerId, StringComparison.Ordinal);
            }
        }

        private void Add(Node node)
        {
            lock (m_lock)
            {
                m_nodes.Add(node);
            }
        }

        private void Remove(Node node)
        {
            lock (m_lock)
            {
                m_nodes.Remove(node);
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<Node> m_nodes = [];
        private string? m_leader;

        /// <summary>
        /// Per-owner <see cref="ILeaderElection"/> view over the shared
        /// <see cref="ManualLeaderElection"/> coordinator.
        /// </summary>
        private sealed class Node : ILeaderElection
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Node"/> class for the specified owner.
            /// </summary>
            /// <param name="owner">The shared leader-election coordinator.</param>
            /// <param name="ownerId">The owner identifier represented by this node.</param>
            public Node(ManualLeaderElection owner, string ownerId)
            {
                m_owner = owner;
                m_ownerId = ownerId;
                m_owner.Add(this);
            }

            /// <inheritdoc/>
            public bool IsLeader => m_owner.IsLeader(m_ownerId);

            /// <inheritdoc/>
            public event Action<bool>? LeadershipChanged;

            /// <inheritdoc/>
            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(IsLeader);
            }

            /// <inheritdoc/>
            public void Start()
            {
                Notify();
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                m_owner.Remove(this);
                return default;
            }

            /// <summary>
            /// Raises a leadership notification with the current leader state.
            /// </summary>
            public void Notify()
            {
                LeadershipChanged?.Invoke(IsLeader);
            }

            private readonly ManualLeaderElection m_owner;
            private readonly string m_ownerId;
        }
    }
}
