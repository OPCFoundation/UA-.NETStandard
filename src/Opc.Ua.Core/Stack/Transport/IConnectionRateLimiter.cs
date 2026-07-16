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
using System.Net;

namespace Opc.Ua
{
    /// <summary>
    /// Admission control for inbound transport connections.
    /// </summary>
    /// <remarks>
    /// A transport listener consults the rate limiter for every inbound
    /// connection before a secure channel is created. This lets a server shed a
    /// connection storm with a deterministic, fast rejection (and an optional
    /// retry-after hint) instead of spending scarce handshake CPU on connections
    /// it cannot service. Implementations wrap a configurable
    /// <see cref="System.Threading.RateLimiting.RateLimiter"/> so the admission
    /// algorithm (token bucket, sliding window, concurrency, ...) is pluggable
    /// via dependency injection.
    /// </remarks>
    public interface IConnectionRateLimiter : IDisposable
    {
        /// <summary>
        /// Attempts to admit a new inbound connection from the given remote endpoint.
        /// </summary>
        /// <param name="remoteEndPoint">
        /// The remote endpoint of the inbound connection, or <c>null</c> when it is
        /// not available. Implementations may partition the limit per source.
        /// </param>
        /// <param name="retryAfter">
        /// When the connection is rejected, an optional hint for how long the peer
        /// should wait before retrying; otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if the connection is admitted; <c>false</c> if it should be
        /// rejected because the server is currently too busy.
        /// </returns>
        bool TryAdmitConnection(EndPoint? remoteEndPoint, out TimeSpan? retryAfter);
    }
}
