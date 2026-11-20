/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional support for a bounded final-value observation through the normal subscription Publish service.
    /// </summary>
    /// <remarks>
    /// Implementations must identify the actual final notification, not merely a queue being drained.
    /// They must clean up registrations on every exit and release obligations when monitoring or the owning
    /// session ends. This capability is deliberately not required of existing custom monitored items.
    /// </remarks>
    public interface IMonitoredItemValuePublication
    {
        /// <summary>
        /// Queues a final sample and waits for its inclusion in an actual Publish response.
        /// </summary>
        /// <param name="session">The session whose permissions and identity were used to read the sample.</param>
        /// <param name="value">The final, per-subscriber sample, including its range and data encoding.</param>
        /// <param name="timeout">A positive, finite publication deadline.</param>
        /// <param name="cancellationToken">Cancels the observation and releases its registration.</param>
        /// <remarks>
        /// Final sampling bypasses data-change suppression, but not permissions, monitoring mode, publishing
        /// mode, queue limits, or Publish message limits. Concurrent samples must not overwrite the final sample.
        /// Aggregation or an unsupported publication pipeline must fail explicitly. A competing finalization
        /// must not supersede a pending one. Completion is not a transport-send or acknowledgement guarantee.
        /// </remarks>
        ValueTask WaitForValuePublicationAsync(
            ISession session,
            in DataValue value,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);
    }
}
