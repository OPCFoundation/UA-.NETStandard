/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Backoff strategy for reconnection delays.
    /// </summary>
    public enum BackoffStrategy
    {
        /// <summary>
        /// Constant delay between attempts.
        /// </summary>
        Constant,

        /// <summary>
        /// Linearly increasing delay.
        /// </summary>
        Linear,

        /// <summary>
        /// Exponentially increasing delay.
        /// </summary>
        Exponential
    }

    /// <summary>
    /// Default reconnect policy with configurable backoff,
    /// jitter, and retry limits.
    /// </summary>
    public class ReconnectPolicy : IReconnectPolicy
    {
        /// <summary>
        /// Default initial delay (1 second).
        /// </summary>
        public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Default max delay (30 seconds).
        /// </summary>
        public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Initialize a reconnect policy with default options.
        /// </summary>
        public ReconnectPolicy()
            : this(new ReconnectPolicyOptions())
        {
        }

        /// <summary>
        /// Initialize a reconnect policy from an options snapshot.
        /// </summary>
        public ReconnectPolicy(ReconnectPolicyOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            InitialDelay = options.InitialDelay;
            MaxDelay = options.MaxDelay;
            MaxRetries = options.MaxRetries;
            Strategy = options.Strategy;
            JitterFactor = options.JitterFactor;
        }

        /// <summary>
        /// Initial delay between reconnect attempts.
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = DefaultInitialDelay;

        /// <summary>
        /// Maximum delay between attempts.
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = DefaultMaxDelay;

        /// <summary>
        /// Maximum number of retries (0 = unlimited).
        /// </summary>
        public int MaxRetries { get; set; }

        /// <summary>
        /// Backoff strategy.
        /// </summary>
        public BackoffStrategy Strategy { get; set; } = BackoffStrategy.Exponential;

        /// <summary>
        /// Jitter factor (0.0 = no jitter, 0.1 = ±10%).
        /// </summary>
        public double JitterFactor { get; set; } = 0.1;

        /// <inheritdoc/>
        public TimeSpan? GetNextDelay(int attempt, CancellationToken ct = default)
        {
            if (MaxRetries > 0 && attempt >= MaxRetries)
            {
                return null;
            }

            double delayMs = Strategy switch
            {
                BackoffStrategy.Constant => InitialDelay.TotalMilliseconds,
                BackoffStrategy.Linear => InitialDelay.TotalMilliseconds * (attempt + 1),
                BackoffStrategy.Exponential => InitialDelay.TotalMilliseconds * Math.Pow(2, attempt),
                _ => InitialDelay.TotalMilliseconds
            };

            delayMs = Math.Min(delayMs, MaxDelay.TotalMilliseconds);

            if (JitterFactor > 0)
            {
                int jitter = (int)(delayMs * JitterFactor);
                delayMs += UnsecureRandom.Shared.Next(-jitter, jitter + 1);
            }

            return TimeSpan.FromMilliseconds(Math.Max(delayMs, 0));
        }

        /// <inheritdoc/>
        public void Reset()
        {
            // Stateless — no internal state to reset.
        }
    }
}
