// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Supplies the server's admission-control limiters: the transport connection
    /// rate limiter and the session-establishment limiter.
    /// </summary>
    /// <remarks>
    /// The default implementation builds deterministic limiters from
    /// <see cref="ServerRateLimitOptions"/> using
    /// <c>System.Threading.RateLimiting</c>. Register a custom implementation in
    /// dependency injection to plug in a different algorithm; a server constructed
    /// directly (without DI) uses the default provider as a fallback.
    /// Explicit custom providers remain an additional ceiling and may reject otherwise
    /// protected ingress; the server never bypasses or silently replaces their policy.
    /// </remarks>
    public interface IServerRateLimiterProvider : IDisposable
    {
        /// <summary>
        /// Gets the listener socket backlog to apply, or 0 to use the transport default.
        /// </summary>
        int ListenBacklog { get; }

        /// <summary>
        /// Gets the connection admission rate limiter to attach to the transport
        /// listener, or <c>null</c> to disable connection rate limiting.
        /// </summary>
        IConnectionRateLimiter? ConnectionRateLimiter { get; }

        /// <summary>
        /// Attempts to acquire a permit for a single session establishment
        /// operation (a <c>CreateSession</c> or <c>ActivateSession</c> call).
        /// </summary>
        /// <param name="lease">
        /// When the call returns <c>true</c>, a lease that MUST be disposed when the
        /// operation completes (releasing the permit); may be <c>null</c> when
        /// session rate limiting is disabled. When the call returns <c>false</c>,
        /// always <c>null</c>.
        /// </param>
        /// <param name="retryAfter">
        /// When rejected, an optional hint for how long the caller should wait
        /// before retrying; otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if the operation may proceed; <c>false</c> if it should be
        /// rejected because the server is too busy.
        /// </returns>
        bool TryAcquireSessionEstablishment(out IDisposable? lease, out TimeSpan? retryAfter);
    }
}
