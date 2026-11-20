/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Observes how acknowledgements end. Every acknowledgement a session tracks is reported
    /// exactly once, so an application can release the state it keeps per notification.
    /// </summary>
    /// <remarks>
    /// The observer is called without holding SDK locks and must be thread-safe. Exceptions
    /// are logged and do not affect publishing. Internal confirmation state is retired
    /// before this callback. Delivery is buffered, so implementations should return promptly;
    /// a slow observer can grow its notification backlog but cannot retain internal
    /// confirmation state. Disposal waits at most five seconds for queued callbacks.
    /// </remarks>
    public interface ISubscriptionAcknowledgementObserver
    {
        /// <summary>
        /// Reports acknowledgements that ended.
        /// </summary>
        /// <param name="outcomes">The terminal state of each acknowledgement.</param>
        /// <param name="ct">The cancellation token.</param>
        ValueTask OnCompletedAsync(
            ArrayOf<AcknowledgementOutcome> outcomes,
            CancellationToken ct = default);
    }
}
