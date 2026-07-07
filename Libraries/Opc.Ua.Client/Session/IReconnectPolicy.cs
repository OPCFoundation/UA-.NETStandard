/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
    /// Defines a reconnection policy for ManagedSession.
    /// Controls backoff timing, retry limits, and jitter for
    /// automatic reconnection attempts.
    /// </summary>
    public interface IReconnectPolicy
    {
        /// <summary>
        /// Get the delay before the next reconnection attempt.
        /// </summary>
        /// <param name="attempt">Zero-based attempt number.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Delay before next attempt, or null to stop retrying.</returns>
        TimeSpan? GetNextDelay(int attempt, CancellationToken ct = default);

        /// <summary>
        /// Try to get the delay before the next reconnection attempt, adapting the
        /// backoff to the server's response so a client reacts to "server busy"
        /// signals instead of retrying blindly and amplifying a connect storm.
        /// </summary>
        /// <remarks>
        /// Returns <c>false</c> when the policy has no adaptive behavior, in which
        /// case the caller falls back to
        /// <see cref="GetNextDelay(int, CancellationToken)"/>.
        /// </remarks>
        /// <param name="attempt">Zero-based attempt number.</param>
        /// <param name="lastStatus">
        /// The status code of the previous attempt (<c>Good</c> when there is no
        /// prior result). A "server busy" code causes a more aggressive backoff.
        /// </param>
        /// <param name="serverRetryAfter">
        /// An optional server-provided hint for how long to wait before retrying,
        /// honored as a lower bound; otherwise <c>null</c>.
        /// </param>
        /// <param name="delay">
        /// When this method returns <c>true</c>, the delay before the next attempt,
        /// or <c>null</c> to stop retrying.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>true</c> when an adaptive delay was produced in <paramref name="delay"/>;
        /// <c>false</c> when the policy has no adaptive behavior.
        /// </returns>
        bool TryGetNextDelay(
            int attempt,
            StatusCode lastStatus,
            TimeSpan? serverRetryAfter,
            out TimeSpan? delay,
            CancellationToken ct = default);

        /// <summary>
        /// Reset the policy state (e.g., after successful reconnection).
        /// </summary>
        void Reset();
    }
}
