/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for configuring a <see cref="ReconnectPolicy"/>. Provided as
    /// a record so it can be used with the .NET options pattern
    /// (<see cref="Microsoft.Extensions.Options.IOptions{T}"/>) and bound
    /// from configuration sources.
    /// </summary>
    public sealed record ReconnectPolicyOptions
    {
        /// <summary>
        /// Initial delay between reconnect attempts.
        /// </summary>
        public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum delay between attempts (caps the backoff).
        /// </summary>
        public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum number of retries (0 = unlimited).
        /// </summary>
        public int MaxRetries { get; init; }

        /// <summary>
        /// Backoff strategy.
        /// </summary>
        public BackoffStrategy Strategy { get; init; } = BackoffStrategy.Exponential;

        /// <summary>
        /// Jitter factor (0.0 = no jitter, 0.1 = ±10%).
        /// </summary>
        public double JitterFactor { get; init; } = 0.1;

        /// <summary>
        /// Maximum total elapsed time for one reconnect cycle across
        /// outer ManagedSession retries and channel-manager retries.
        /// <see cref="TimeSpan.Zero"/> or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>
        /// means unlimited; other negative values are rejected.
        /// </summary>
        public TimeSpan MaxTotalReconnectTime { get; init; } = TimeSpan.FromMinutes(5);
    }
}
