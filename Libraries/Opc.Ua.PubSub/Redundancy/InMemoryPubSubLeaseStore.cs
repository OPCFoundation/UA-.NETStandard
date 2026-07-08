/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
        private readonly System.Threading.Lock m_gate = new();
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
