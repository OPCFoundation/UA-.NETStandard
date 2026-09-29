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
