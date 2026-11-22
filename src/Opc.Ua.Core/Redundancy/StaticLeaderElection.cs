// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// A fixed-role <see cref="ILeaderElection"/> for static deployments
    /// (single instance, or an externally-assigned leader). Use
    /// <see cref="SharedStoreLeaseElection"/> for dynamic election.
    /// </summary>
    public sealed class StaticLeaderElection : ILeaderElection
    {
        /// <summary>
        /// Creates a static election with a fixed leadership state.
        /// </summary>
        /// <param name="isLeader">Whether this replica is the leader.</param>
        public StaticLeaderElection(bool isLeader)
        {
            IsLeader = isLeader;
        }

        /// <inheritdoc/>
        public bool IsLeader { get; }

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
            LeadershipChanged?.Invoke(IsLeader);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
