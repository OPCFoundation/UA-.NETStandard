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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Partitions the existing connection rate and burst into protected and shared token buckets.
    /// </summary>
    /// <remarks>
    /// Bootstrap, reconnect and each provisioned trusted owner reserve one burst token and one
    /// token per second. All remaining tokens are work-conserving across classes. Protected
    /// tokens cannot be borrowed by other owners. Every bucket uses the same one-second
    /// replenishment boundary; admission and refill are atomic across the entire limiter.
    /// Pre-authentication guarantees require explicit trusted ingress classification.
    /// This meters admissions, not active connections: closing a connection never refunds its token.
    /// </remarks>
    public sealed class ResourceIsolationConnectionRateLimiter : IResourceIsolationConnectionRateLimiter
    {
        /// <summary>
        /// Creates rate isolation inside the supplied totals, without timers or queued admission.
        /// </summary>
        /// <param name="connectionsPerSecond">The existing positive server-wide rate.</param>
        /// <param name="burst">The existing positive server-wide burst.</param>
        /// <param name="resourceIsolationProvider">The borrowed Balanced or TrustedReservations provider.</param>
        /// <param name="timeProvider">The monotonic clock; defaults to the system clock.</param>
        /// <exception cref="ArgumentException">
        /// Either total cannot hold all protected buckets plus one shared token.
        /// </exception>
        public ResourceIsolationConnectionRateLimiter(
            int connectionsPerSecond,
            int burst,
            DefaultServerResourceIsolationProvider resourceIsolationProvider,
            TimeProvider? timeProvider = null)
        {
            if (connectionsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionsPerSecond));
            }
            if (burst <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(burst));
            }
            m_provider = resourceIsolationProvider ??
                throw new ArgumentNullException(nameof(resourceIsolationProvider));
            if (resourceIsolationProvider.Plan.Mode is not ServerResourceIsolationMode.Balanced and
                not ServerResourceIsolationMode.TrustedReservations)
            {
                throw new ArgumentException("A protected resource-isolation profile is required.",
                    nameof(resourceIsolationProvider));
            }
            long protectedCount = 2L + resourceIsolationProvider.Plan.TrustedOwners.Count;
            if (connectionsPerSecond <= protectedCount || burst <= protectedCount)
            {
                throw new ArgumentException(
                    "The configured connection rate and burst must each hold one bootstrap token, " +
                    "one reconnect token, one token per provisioned trusted owner and at least one shared token. " +
                    "Increase the explicit totals or select a profile without rate reservations.");
            }
            m_shared = new Bucket(connectionsPerSecond - (int)protectedCount, burst - (int)protectedCount);
            foreach (string key in resourceIsolationProvider.Plan.TrustedOwners.Keys)
            {
                m_trusted.Add(DefaultServerResourceIsolationProvider.GetTrustedOwnerKey(key), new Bucket(1, 1));
            }
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_started = m_timeProvider.GetTimestamp();
        }

        /// <summary>
        /// Attempts legacy, unclassified admission using only the shared bucket.
        /// Protected eligibility is never inferred from the endpoint by this limiter.
        /// </summary>
        public bool TryAdmitConnection(EndPoint? remoteEndPoint, out TimeSpan? retryAfter)
        {
            return TryAdmit(null, out retryAfter);
        }

        /// <inheritdoc/>
        public bool TryAdmitConnection(
            EndPoint? remoteEndPoint,
            ResourceIsolationOwner owner,
            out TimeSpan? retryAfter)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            return TryAdmit(owner, out retryAfter);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_disposed = true;
            }
        }

        private bool TryAdmit(ResourceIsolationOwner? owner, out TimeSpan? retryAfter)
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ResourceIsolationConnectionRateLimiter));
                }
                if (owner != null && !m_provider.IsIssuedOwner(owner))
                {
                    throw new ArgumentException("The owner was not issued by this limiter's isolation provider.",
                        nameof(owner));
                }
                long elapsedTicks = m_timeProvider.GetElapsedTime(m_started).Ticks;
                long period = elapsedTicks / TimeSpan.TicksPerSecond;
                if (period > m_lastPeriod)
                {
                    long periods = period - m_lastPeriod;
                    m_shared.Replenish(periods);
                    m_bootstrap.Replenish(periods);
                    m_reconnect.Replenish(periods);
                    foreach (Bucket bucket in m_trusted.Values)
                    {
                        bucket.Replenish(periods);
                    }
                    m_lastPeriod = period;
                }
                Bucket? reserved = null;
                if (owner != null)
                {
                    switch (owner.Class)
                    {
                        case ResourceIsolationClass.Bootstrap:
                            reserved = m_bootstrap;
                            break;
                        case ResourceIsolationClass.Reconnect:
                            reserved = m_reconnect;
                            break;
                        case ResourceIsolationClass.Trusted:
                            if (!m_trusted.TryGetValue(owner.Key, out reserved))
                            {
                                throw new InvalidOperationException(
                                    "The trusted owner has no provisioned rate bucket.");
                            }
                            break;
                    }
                }
                if (reserved?.TryTake() == true || m_shared.TryTake())
                {
                    retryAfter = null;
                    return true;
                }
                retryAfter = TimeSpan.FromTicks(TimeSpan.TicksPerSecond - elapsedTicks % TimeSpan.TicksPerSecond);
                return false;
            }
        }

        /// <summary>
        /// Tracks one finite token allocation; callers serialize consumption and replenishment.
        /// </summary>
        /// <param name="rate">Tokens added per one-second replenishment period.</param>
        /// <param name="capacity">Maximum retained tokens, including the initial burst.</param>
        private sealed class Bucket(int rate, int capacity)
        {
            /// <summary>
            /// Consumes one available token without borrowing from another bucket.
            /// </summary>
            public bool TryTake()
            {
                if (m_tokens == 0)
                {
                    return false;
                }
                m_tokens--;
                return true;
            }

            /// <summary>
            /// Adds elapsed periods' tokens without overflowing or exceeding the burst capacity.
            /// </summary>
            public void Replenish(long periods)
            {
                long missing = m_capacity - (long)m_tokens;
                if (periods >= (missing + m_rate - 1) / m_rate)
                {
                    m_tokens = m_capacity;
                }
                else
                {
                    m_tokens += (int)(periods * m_rate);
                }
            }

            /// <summary>
            /// Tokens restored per replenishment period.
            /// </summary>
            private readonly int m_rate = rate;

            /// <summary>
            /// Hard ceiling for accumulated tokens.
            /// </summary>
            private readonly int m_capacity = capacity;

            /// <summary>
            /// Unspent tokens, initialized to the full burst allowance.
            /// </summary>
            private int m_tokens = capacity;
        }

        private readonly Lock m_lock = new();
        private readonly Bucket m_shared;
        private readonly Bucket m_bootstrap = new(1, 1);
        private readonly Bucket m_reconnect = new(1, 1);
        private readonly Dictionary<string, Bucket> m_trusted = new(StringComparer.Ordinal);
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "The isolation provider is borrowed and outlives this rate limiter. " +
                "TODO: remove when the analyzer recognizes borrowed provider ownership.")]
        private readonly DefaultServerResourceIsolationProvider m_provider;
        private readonly TimeProvider m_timeProvider;
        private readonly long m_started;
        private long m_lastPeriod;
        private bool m_disposed;
    }
}
