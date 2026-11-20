// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Shared store used to elect the active instance of a redundant PubSub
    /// component through leadership leases.
    /// </summary>
    /// <remarks>
    /// Implements the externalized coordination state required for OPC UA
    /// Part 14 §9.1.6 HA deployments. A distributed implementation (for
    /// example backed by the shared runtime-state store, a database, or a
    /// broker primitive) lets multiple instances agree on a single active
    /// owner per lease key, with a monotonic fencing token on each ownership
    /// change. The default <see cref="InMemoryPubSubLeaseStore"/> is
    /// single-process and intended for tests and single-instance use.
    /// </remarks>
    public interface IPubSubLeaseStore
    {
        /// <summary>
        /// Attempts to acquire the lease for <paramref name="leaseKey"/> on
        /// behalf of <paramref name="ownerId"/>. Succeeds when the lease is
        /// unheld or expired, or already held by the same owner (renewal).
        /// </summary>
        /// <param name="leaseKey">Contended resource key.</param>
        /// <param name="ownerId">Requesting instance identifier.</param>
        /// <param name="duration">Requested lease duration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The acquired lease, or <see langword="null"/> when the lease is
        /// currently held by another live owner.
        /// </returns>
        ValueTask<PubSubLease?> TryAcquireAsync(
            string leaseKey,
            string ownerId,
            TimeSpan duration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to renew a previously acquired lease. Fails when the lease
        /// has been taken over by another owner (fencing token advanced).
        /// </summary>
        /// <param name="lease">The lease to renew.</param>
        /// <param name="duration">New lease duration from now.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The renewed lease, or <see langword="null"/> when renewal was
        /// rejected because ownership was lost.
        /// </returns>
        ValueTask<PubSubLease?> TryRenewAsync(
            PubSubLease lease,
            TimeSpan duration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a held lease so another instance can acquire it
        /// immediately. No-op when the lease was already lost.
        /// </summary>
        /// <param name="lease">The lease to release.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask ReleaseAsync(
            PubSubLease lease,
            CancellationToken cancellationToken = default);
    }
}
