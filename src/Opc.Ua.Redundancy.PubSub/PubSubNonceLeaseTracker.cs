// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Observes lease confirmations used by the activation coordinator without doing I/O on the publish path.
    /// </summary>
    internal sealed class PubSubNonceLeaseTracker : IPubSubLeaseStore, IPubSubActivationFenceProvider
    {
        /// <summary>
        /// Tracks the leases returned to one coordinator.
        /// </summary>
        public PubSubNonceLeaseTracker(IPubSubLeaseStore store, TimeProvider timeProvider)
        {
            m_store = store;
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public bool TryGetActiveFence(string componentId, out long fencingToken)
        {
            lock (m_gate)
            {
                if (m_confirmed.TryGetValue(componentId, out Confirmation confirmed) &&
                    !confirmed.CancellationToken.IsCancellationRequested &&
                    confirmed.Lease.ExpiresAt > m_timeProvider.GetUtcNow() &&
                    m_timeProvider.GetElapsedTime(confirmed.RequestedAt) < confirmed.Duration)
                {
                    fencingToken = confirmed.Lease.FencingToken;
                    return fencingToken > 0;
                }
            }
            fencingToken = 0;
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubLease?> TryAcquireAsync(
            string leaseKey,
            string ownerId,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            long requestedAt = m_timeProvider.GetTimestamp();
            PubSubLease? result = await m_store.TryAcquireAsync(leaseKey, ownerId, duration, cancellationToken)
                .ConfigureAwait(false);
            Track(result, requestedAt, duration, cancellationToken);
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubLease?> TryRenewAsync(
            PubSubLease lease,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            long requestedAt = m_timeProvider.GetTimestamp();
            PubSubLease? result = null;
            try
            {
                result = await m_store.TryRenewAsync(lease, duration, cancellationToken).ConfigureAwait(false);
                Track(result, requestedAt, duration, cancellationToken);
                return result;
            }
            finally
            {
                if (result is null)
                {
                    Clear(lease);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask ReleaseAsync(PubSubLease lease, CancellationToken cancellationToken = default)
        {
            Clear(lease);
            return m_store.ReleaseAsync(lease, cancellationToken);
        }

        private void Track(
            PubSubLease? result, long requestedAt, TimeSpan duration, CancellationToken cancellationToken)
        {
            if (result is not PubSubLease lease || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            lock (m_gate)
            {
                m_confirmed[lease.LeaseKey] = new Confirmation(lease, requestedAt, duration, cancellationToken);
            }
        }

        private void Clear(PubSubLease lease)
        {
            lock (m_gate)
            {
                if (m_confirmed.TryGetValue(lease.LeaseKey, out Confirmation existing) &&
                    existing.Lease == lease)
                {
                    m_confirmed.Remove(lease.LeaseKey);
                }
            }
        }

        private readonly record struct Confirmation(
            PubSubLease Lease,
            long RequestedAt,
            TimeSpan Duration,
            CancellationToken CancellationToken);

        private readonly IPubSubLeaseStore m_store;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, Confirmation> m_confirmed = new(StringComparer.Ordinal);
    }
}
