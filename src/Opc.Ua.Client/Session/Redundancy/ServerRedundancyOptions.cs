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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Bounds the best-effort server-redundancy refresh so an unavailable
    /// server cannot delay connect, reconnect, or failover.
    /// </summary>
    /// <remarks>
    /// Both bounds trade responsiveness against tolerance of a slow server.
    /// Raise them for a high-latency link, where the defaults can expire
    /// before a healthy server answers; lower them when a connect must not
    /// stall. <see cref="Timeout.InfiniteTimeSpan"/> waits indefinitely and
    /// is only safe when the caller already bounds the operation.
    /// </remarks>
    public sealed record ServerRedundancyOptions
    {
        /// <summary>
        /// Default bound applied to each redundancy operation.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Bounds reading the active server's redundancy metadata at connect,
        /// reconnect, and failover. A refresh that exceeds it retains the
        /// previous snapshot rather than discarding it.
        /// </summary>
        public TimeSpan RefreshTimeout { get; init; } = DefaultTimeout;

        /// <summary>
        /// Bounds resolving each peer's endpoint. Peers resolve in parallel, so
        /// one unreachable backup does not consume another's budget, and an
        /// unresolved peer keeps its URI for a later attempt.
        /// </summary>
        public TimeSpan PeerDiscoveryTimeout { get; init; } = DefaultTimeout;

        /// <summary>
        /// Validates that both bounds are usable.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when a bound is neither positive nor <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public void Validate()
        {
            ValidateTimeout(RefreshTimeout, nameof(RefreshTimeout));
            ValidateTimeout(PeerDiscoveryTimeout, nameof(PeerDiscoveryTimeout));
        }

        private static void ValidateTimeout(TimeSpan value, string name)
        {
            if (value != Timeout.InfiniteTimeSpan && value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "A redundancy timeout must be positive or Timeout.InfiniteTimeSpan.");
            }
        }
    }
}
