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
    /// An <see cref="IReconnectPolicy"/> that adapts the backoff delay to the
    /// server's response, so a client reacts to "server busy" signals instead of
    /// retrying blindly and amplifying a connect storm.
    /// </summary>
    /// <remarks>
    /// This is a separate interface (rather than a new member on
    /// <see cref="IReconnectPolicy"/>) to preserve binary compatibility for
    /// existing custom policies and to avoid default-interface-methods, which are
    /// not supported on the older target frameworks. The connection state machine
    /// uses the adaptive overload when the policy implements this interface and
    /// falls back to <see cref="IReconnectPolicy.GetNextDelay(int, CancellationToken)"/>
    /// otherwise.
    /// </remarks>
    public interface IAdaptiveReconnectPolicy : IReconnectPolicy
    {
        /// <summary>
        /// Gets the delay before the next reconnection attempt, taking the last
        /// attempt's result and any server-provided retry-after hint into account.
        /// </summary>
        /// <param name="attempt">Zero-based attempt number.</param>
        /// <param name="lastStatus">
        /// The status code of the previous attempt (<c>Good</c> when there is no
        /// prior result). A "server busy" code causes a more aggressive backoff.
        /// </param>
        /// <param name="serverRetryAfter">
        /// An optional server-provided hint for how long to wait before retrying,
        /// honored as a lower bound; otherwise <c>null</c>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Delay before next attempt, or <c>null</c> to stop retrying.</returns>
        TimeSpan? GetNextDelay(
            int attempt,
            StatusCode lastStatus,
            TimeSpan? serverRetryAfter,
            CancellationToken ct = default);
    }
}
