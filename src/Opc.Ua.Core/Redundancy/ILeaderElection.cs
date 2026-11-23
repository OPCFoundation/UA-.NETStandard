// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Determines whether this replica is the writer (leader) in an
    /// active/passive or single-writer active/active deployment. Supply the
    /// <see cref="IsLeader"/> predicate to an address-space synchronizer so
    /// only the leader writes to the shared store ("shared read, master
    /// write"), and to an <c>IServiceLevelProvider</c> so the leader advertises
    /// the highest OPC UA <c>ServiceLevel</c>.
    /// </summary>
    public interface ILeaderElection : IAsyncDisposable
    {
        /// <summary>
        /// <c>true</c> when this replica currently holds leadership.
        /// </summary>
        bool IsLeader { get; }

        /// <summary>
        /// Raised when leadership is gained (<c>true</c>) or lost
        /// (<c>false</c>).
        /// </summary>
        event Action<bool>? LeadershipChanged;

        /// <summary>
        /// Attempts to acquire or renew leadership once and returns the
        /// resulting leadership state. Safe to call repeatedly.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default);

        /// <summary>
        /// Starts the background acquire/renew loop (if any).
        /// </summary>
        void Start();
    }
}
