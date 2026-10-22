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
        /// When <see langword="true"/>, a <see cref="ManagedSession"/> reads <c>Server.ServiceLevel</c> and
        /// <c>Server.ServerStatus.State</c> of every other member of a non-transparent redundant Server set
        /// over a short session with a copy of the identity of the session in use, when a selection or
        /// failover decision needs them: after the initial connect, for a failover, and during ServiceLevel
        /// monitoring once the Server in use is no longer Healthy. A member whose resolved endpoint does not
        /// keep the security mode and policy of the session in use is not probed. The values replace those
        /// reported by the Server in use, so the initial connect, the
        /// failover target selection and the failover decision compare the members' current
        /// <c>ServiceLevel</c> (OPC 10000-4 §6.6.2.4.5). After the initial connect the session switches to
        /// the member with the highest <c>ServiceLevel</c> when it is higher than the <c>ServiceLevel</c> of
        /// the Server it connected to. A member that cannot be probed keeps the values reported for it and
        /// carries the failure in <see cref="RedundantServer.ResolutionError"/>. Each probe is bounded by
        /// <see cref="PeerDiscoveryTimeout"/>. Default <see langword="false"/>.
        /// </summary>
        public bool ProbeMemberServiceLevel { get; init; }

        /// <summary>
        /// When set, a <see cref="ManagedSession"/> reads <c>Server.ServiceLevel</c> of the Server in use at
        /// this interval while it is connected to a non-transparent redundant Server set. When the
        /// <c>ServiceLevel</c> leaves the Healthy subrange (OPC 10000-4 §6.6.2.4.2) the redundancy snapshot is
        /// refreshed, including the member probe when <see cref="ProbeMemberServiceLevel"/> is set, and a
        /// failover decision that warrants it starts <see cref="ManagedSession.FailoverAsync"/> to the target
        /// the redundancy handler selects. Default <see langword="null"/>: the session fails over only when
        /// the reconnect policy gives up or the application calls <see cref="ManagedSession.FailoverAsync"/>.
        /// </summary>
        public TimeSpan? ServiceLevelMonitoringInterval { get; init; }

        /// <summary>
        /// Validates that both bounds and the monitoring interval are usable.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when a bound is neither positive nor <see cref="Timeout.InfiniteTimeSpan"/>, or when
        /// <see cref="ServiceLevelMonitoringInterval"/> is set and not positive.
        /// </exception>
        public void Validate()
        {
            ValidateTimeout(RefreshTimeout, nameof(RefreshTimeout));
            ValidateTimeout(PeerDiscoveryTimeout, nameof(PeerDiscoveryTimeout));
            if (ServiceLevelMonitoringInterval is TimeSpan interval && interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ServiceLevelMonitoringInterval),
                    interval,
                    "The ServiceLevel monitoring interval must be positive.");
            }
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
