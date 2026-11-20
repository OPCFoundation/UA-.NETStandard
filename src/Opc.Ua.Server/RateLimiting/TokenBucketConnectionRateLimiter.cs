/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Net;
using System.Threading.RateLimiting;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A token-bucket <see cref="IConnectionRateLimiter"/> that bounds the rate of
    /// admitted inbound connections. Backed by a
    /// <see cref="TokenBucketRateLimiter"/> so a burst up to the bucket capacity is
    /// admitted immediately and the sustained rate is the replenishment rate.
    /// </summary>
    public sealed class TokenBucketConnectionRateLimiter : IConnectionRateLimiter
    {
        private readonly TokenBucketRateLimiter m_limiter;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="TokenBucketConnectionRateLimiter"/> class.
        /// </summary>
        /// <param name="connectionsPerSecond">
        /// The sustained connection admission rate (tokens replenished per second).
        /// </param>
        /// <param name="burst">
        /// The burst capacity (token-bucket size).
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A limit is not positive.
        /// </exception>
        public TokenBucketConnectionRateLimiter(int connectionsPerSecond, int burst)
        {
            if (connectionsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionsPerSecond));
            }
            if (burst <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(burst));
            }

            m_limiter = new TokenBucketRateLimiter(
                new TokenBucketRateLimiterOptions
                {
                    TokenLimit = burst,
                    TokensPerPeriod = connectionsPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
        }

        /// <inheritdoc/>
        public bool TryAdmitConnection(EndPoint? remoteEndPoint, out TimeSpan? retryAfter)
        {
            // A single token is consumed per admitted connection. Disposing an
            // acquired token-bucket lease does not return the token, so the acquire
            // + dispose is the correct rate-metering operation.
            using RateLimitLease lease = m_limiter.AttemptAcquire(1);
            if (lease.IsAcquired)
            {
                retryAfter = null;
                return true;
            }

            retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan value)
                ? value
                : null;
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_limiter.Dispose();
        }
    }
}
