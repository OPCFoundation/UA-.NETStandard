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
