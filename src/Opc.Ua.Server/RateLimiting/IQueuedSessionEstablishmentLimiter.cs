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
    /// Optionally implemented by an <see cref="IServerRateLimiterProvider"/> whose
    /// session-establishment limiter can queue operations until a permit is free.
    /// </summary>
    /// <remarks>
    /// When the provider implements this interface the server waits for a permit
    /// here instead of calling
    /// <see cref="IServerRateLimiterProvider.TryAcquireSessionEstablishment"/>, which
    /// never queues.
    /// </remarks>
    public interface IQueuedSessionEstablishmentLimiter
    {
        /// <summary>
        /// Acquires a permit for a single session establishment operation (a
        /// <c>CreateSession</c> or <c>ActivateSession</c> call), waiting in the queue
        /// when all permits are in use and the queue has room.
        /// </summary>
        /// <param name="cancellationToken">Stops waiting for a permit.</param>
        /// <returns>
        /// Whether the operation may proceed; when it may, a lease that MUST be
        /// disposed when the operation completes (or <c>null</c> when session rate
        /// limiting is disabled); when rejected, an optional retry-after hint.
        /// </returns>
        ValueTask<(bool Acquired, IDisposable? Lease, TimeSpan? RetryAfter)>
            AcquireSessionEstablishmentAsync(CancellationToken cancellationToken = default);
    }
}
