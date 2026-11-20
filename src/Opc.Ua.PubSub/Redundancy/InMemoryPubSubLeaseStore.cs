/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Single-process <see cref="IPubSubLeaseStore"/> backed by an in-memory
    /// dictionary. Intended for tests and single-instance deployments; a
    /// distributed store is required for genuine multi-instance failover.
    /// </summary>
    public sealed class InMemoryPubSubLeaseStore : IPubSubLeaseStore
    {
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, PubSubLease> m_leases = new(StringComparer.Ordinal);
        private long m_fencingToken;

        /// <summary>
        /// Initializes a new <see cref="InMemoryPubSubLeaseStore"/>.
        /// </summary>
        /// <param name="timeProvider">
        /// Clock used to evaluate lease expiry. Defaults to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public InMemoryPubSubLeaseStore(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubLease?> TryAcquireAsync(
            string leaseKey,
            string ownerId,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(leaseKey))
            {
                throw new ArgumentException("leaseKey is required.", nameof(leaseKey));
            }
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("ownerId is required.", nameof(ownerId));
            }
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset now = m_timeProvider.GetUtcNow();
            lock (m_gate)
            {
                if (m_leases.TryGetValue(leaseKey, out PubSubLease existing) &&
                    existing.ExpiresAt > now &&
                    !string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal))
                {
                    return new ValueTask<PubSubLease?>((PubSubLease?)null);
                }

                long token = string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal) &&
                    existing.ExpiresAt > now
                        ? existing.FencingToken
                        : ++m_fencingToken;
                var lease = new PubSubLease(leaseKey, ownerId, token, now + duration);
                m_leases[leaseKey] = lease;
                return new ValueTask<PubSubLease?>(lease);
            }
        }

        /// <inheritdoc/>
        public ValueTask<PubSubLease?> TryRenewAsync(
            PubSubLease lease,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            lock (m_gate)
            {
                if (!m_leases.TryGetValue(lease.LeaseKey, out PubSubLease current) ||
                    current.FencingToken != lease.FencingToken ||
                    !string.Equals(current.OwnerId, lease.OwnerId, StringComparison.Ordinal) ||
                    current.ExpiresAt <= now)
                {
                    return new ValueTask<PubSubLease?>((PubSubLease?)null);
                }
                var renewed = new PubSubLease(
                    lease.LeaseKey, lease.OwnerId, lease.FencingToken, now + duration);
                m_leases[lease.LeaseKey] = renewed;
                return new ValueTask<PubSubLease?>(renewed);
            }
        }

        /// <inheritdoc/>
        public ValueTask ReleaseAsync(
            PubSubLease lease,
            CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                if (m_leases.TryGetValue(lease.LeaseKey, out PubSubLease current) &&
                    current.FencingToken == lease.FencingToken &&
                    string.Equals(current.OwnerId, lease.OwnerId, StringComparison.Ordinal))
                {
                    m_leases.Remove(lease.LeaseKey);
                }
            }
            return default;
        }
    }
}
