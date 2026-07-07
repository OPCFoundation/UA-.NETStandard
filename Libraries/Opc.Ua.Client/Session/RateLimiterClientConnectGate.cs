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
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A client connect gate backed by <see cref="RateLimiter"/>.
    /// </summary>
    public sealed class RateLimiterClientConnectGate : IClientConnectGate, IDisposable
    {
        private readonly RateLimiter m_rateLimiter;
        private readonly bool m_ownsRateLimiter;

        /// <summary>
        /// Initializes a new instance of the <see cref="RateLimiterClientConnectGate"/> class.
        /// </summary>
        /// <param name="maxConcurrency">The maximum number of concurrent initial connects.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is less than one.</exception>
        public RateLimiterClientConnectGate(int maxConcurrency)
            : this(CreateLimiter(maxConcurrency), ownsRateLimiter: true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RateLimiterClientConnectGate"/> class.
        /// </summary>
        /// <param name="rateLimiter">The shared rate limiter to use.</param>
        /// <exception cref="ArgumentNullException"><paramref name="rateLimiter"/> is <c>null</c>.</exception>
        public RateLimiterClientConnectGate(RateLimiter rateLimiter)
            : this(rateLimiter, ownsRateLimiter: false)
        {
        }

        private RateLimiterClientConnectGate(
            RateLimiter rateLimiter,
            bool ownsRateLimiter)
        {
            m_rateLimiter = rateLimiter
                ?? throw new ArgumentNullException(nameof(rateLimiter));
            m_ownsRateLimiter = ownsRateLimiter;
        }

        /// <inheritdoc/>
        public async ValueTask<IDisposable> AcquireAsync(CancellationToken ct = default)
        {
            RateLimitLease lease = await m_rateLimiter
                .AcquireAsync(1, ct)
                .ConfigureAwait(false);

            if (lease.IsAcquired)
            {
                return lease;
            }

            lease.Dispose();
            throw new ServiceResultException(
                StatusCodes.BadTooManyOperations,
                "The client connect rate limiter did not grant a permit.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_ownsRateLimiter)
            {
                m_rateLimiter.Dispose();
            }
        }

        private static ConcurrencyLimiter CreateLimiter(int maxConcurrency)
        {
            if (maxConcurrency < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            }

            return new ConcurrencyLimiter(
                new ConcurrencyLimiterOptions
                {
                    PermitLimit = maxConcurrency,
                    QueueLimit = int.MaxValue,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        }
    }
}
