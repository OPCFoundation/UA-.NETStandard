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
        /// The multiplier applied to the computed backoff delay when the previous
        /// attempt failed with a "server busy" signal, so the client backs off more
        /// aggressively instead of hammering an overloaded server.
        /// </summary>
        public const double ServerBusyBackoffMultiplier = 4.0;

        /// <summary>
        /// Default initial delay (1 second).
        /// </summary>
        public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Default max delay (30 seconds).
        /// </summary>
        public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Default maximum total reconnect time (5 minutes).
        /// </summary>
        public static readonly TimeSpan DefaultMaxTotalReconnectTime = TimeSpan.FromMinutes(5);

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
            MaxTotalReconnectTime = options.MaxTotalReconnectTime;
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

        /// <summary>
        /// Maximum total elapsed time for one reconnect cycle across
        /// outer ManagedSession retries and channel-manager retries.
        /// </summary>
        public TimeSpan MaxTotalReconnectTime { get; set; } = DefaultMaxTotalReconnectTime;

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
        public bool TryGetNextDelay(
            int attempt,
            StatusCode lastStatus,
            TimeSpan? serverRetryAfter,
            out TimeSpan? delay,
            CancellationToken ct = default)
        {
            TimeSpan? baseDelay = GetNextDelay(attempt, ct);
            if (baseDelay == null)
            {
                // Retry budget exhausted.
                delay = null;
                return true;
            }

            double delayMs = baseDelay.Value.TotalMilliseconds;

            // Back off more aggressively when the server signalled overload, so the
            // client ramps down instead of amplifying a connect storm.
            if (IsServerBusySignal(lastStatus))
            {
                delayMs = Math.Min(
                    delayMs * ServerBusyBackoffMultiplier,
                    MaxDelay.TotalMilliseconds);
            }

            // Honor a server-provided retry-after hint as a lower bound, clamped to
            // the maximum delay so a pathological hint cannot stall reconnection.
            if (serverRetryAfter > TimeSpan.Zero)
            {
                double hintMs = Math.Min(
                    serverRetryAfter.Value.TotalMilliseconds,
                    MaxDelay.TotalMilliseconds);
                delayMs = Math.Max(delayMs, hintMs);
            }

            delay = TimeSpan.FromMilliseconds(Math.Max(delayMs, 0));
            return true;
        }

        /// <summary>
        /// Indicates whether a status code signals that the server is overloaded
        /// (too busy, too many sessions/operations, or a transient timeout) and the
        /// client should back off rather than retry immediately.
        /// </summary>
        /// <param name="status">The status code to classify.</param>
        /// <returns><c>true</c> if the client should back off more aggressively.</returns>
        public static bool IsServerBusySignal(StatusCode status)
        {
            return status == StatusCodes.BadServerTooBusy ||
                status == StatusCodes.BadTcpServerTooBusy ||
                status == StatusCodes.BadTooManySessions ||
                status == StatusCodes.BadTooManyOperations ||
                status == StatusCodes.BadTooManyPublishRequests ||
                status == StatusCodes.BadRequestTimeout ||
                status == StatusCodes.BadTimeout;
        }

        /// <summary>
        /// Parses a server-provided retry-after hint from a fault's
        /// <c>AdditionalInfo</c>, if present (a <c>RetryAfterMs=N</c> token).
        /// </summary>
        /// <remarks>
        /// The token literal must stay in sync with the server
        /// (<c>StandardServer.RetryAfterHintPrefix</c>). The hint is best-effort:
        /// <c>AdditionalInfo</c> only reaches the client when it requests
        /// diagnostics, so a missing hint simply falls back to signal-based backoff.
        /// </remarks>
        /// <param name="additionalInfo">The fault's additional info, or <c>null</c>.</param>
        /// <returns>The retry-after duration, or <c>null</c> when absent/invalid.</returns>
        public static TimeSpan? ParseServerRetryAfter(string? additionalInfo)
        {
            if (string.IsNullOrEmpty(additionalInfo))
            {
                return null;
            }

            const string prefix = "RetryAfterMs=";
            int index = additionalInfo!.IndexOf(prefix, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            int start = index + prefix.Length;
            int end = start;
            long milliseconds = 0;
            while (end < additionalInfo.Length && char.IsDigit(additionalInfo[end]))
            {
                milliseconds = (milliseconds * 10) + (additionalInfo[end] - '0');
                end++;

                // Cap at one day to avoid overflow / a pathological hint.
                if (milliseconds >= 86_400_000)
                {
                    milliseconds = 86_400_000;
                    break;
                }
            }

            if (end > start && milliseconds > 0)
            {
                return TimeSpan.FromMilliseconds(milliseconds);
            }

            return null;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            // Stateless — no internal state to reset.
        }
    }
}
