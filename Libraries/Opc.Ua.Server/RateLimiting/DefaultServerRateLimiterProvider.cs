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
using System.Threading.RateLimiting;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The default <see cref="IServerRateLimiterProvider"/>: builds deterministic
    /// admission limiters from <see cref="ServerRateLimitOptions"/> using
    /// <c>System.Threading.RateLimiting</c>.
    /// </summary>
    /// <remarks>
    /// Connection admission uses a token bucket (burst + sustained rate). Session
    /// establishment uses a concurrency limiter that bounds the number of
    /// in-flight <c>CreateSession</c> / <c>ActivateSession</c> operations so the
    /// CPU-bound handshake work cannot saturate every core under a storm.
    /// </remarks>
    public sealed class DefaultServerRateLimiterProvider : IServerRateLimiterProvider
    {
        private readonly ConcurrencyLimiter? m_sessionLimiter;
        private int m_disposed;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="DefaultServerRateLimiterProvider"/> class.
        /// </summary>
        /// <param name="options">The rate-limit options. When <c>null</c>, defaults are used.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public DefaultServerRateLimiterProvider(ServerRateLimitOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            ListenBacklog = options.ListenBacklog > 0
                ? options.ListenBacklog
                : ServerRateLimitOptions.DefaultListenBacklog;

            if (options.Enabled && options.ConnectionRateLimitEnabled)
            {
                ConnectionRateLimiter = new TokenBucketConnectionRateLimiter(
                    options.ConnectionsPerSecond,
                    options.ConnectionBurst);
            }

            if (options.Enabled && options.SessionRateLimitEnabled)
            {
                int permitLimit = options.MaxConcurrentSessionEstablishment > 0
                    ? options.MaxConcurrentSessionEstablishment
                    : ServerRateLimitOptions.DefaultMaxConcurrentSessionEstablishment;

                m_sessionLimiter = new ConcurrencyLimiter(
                    new ConcurrencyLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        QueueLimit = Math.Max(0, options.SessionEstablishmentQueueLimit),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            }
        }

        /// <inheritdoc/>
        public int ListenBacklog { get; }

        /// <inheritdoc/>
        public IConnectionRateLimiter? ConnectionRateLimiter { get; }

        /// <inheritdoc/>
        public bool TryAcquireSessionEstablishment(out IDisposable? lease, out TimeSpan? retryAfter)
        {
            retryAfter = null;

            if (m_sessionLimiter == null)
            {
                lease = null;
                return true;
            }

            RateLimitLease acquired = m_sessionLimiter.AttemptAcquire(1);
            if (acquired.IsAcquired)
            {
                lease = acquired;
                return true;
            }

            retryAfter = acquired.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan value)
                ? value
                : null;
            acquired.Dispose();
            lease = null;
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            ConnectionRateLimiter?.Dispose();
            m_sessionLimiter?.Dispose();
        }
    }
}
