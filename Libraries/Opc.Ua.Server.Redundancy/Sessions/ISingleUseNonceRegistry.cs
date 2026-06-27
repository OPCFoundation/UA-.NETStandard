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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Records server nonces as single-use across the whole replica set so a
    /// captured <c>ActivateSession</c> signature cannot be replayed on another
    /// replica during a mirrored fast-reconnect. A nonce may be consumed
    /// <em>exactly once</em> by exactly one replica; every later attempt (on any
    /// replica) is rejected.
    /// </summary>
    /// <remarks>
    /// OPC UA Part 4 §5.7.3.1 requires the <c>serverNonce</c> to be single-use.
    /// In a shared / mirrored session deployment that guarantee must hold across
    /// replicas, otherwise a Sign-mode <c>ActivateSession</c> captured against
    /// one replica replays against a standby. This
    /// registry provides the cross-replica single-use check.
    /// </remarks>
    public interface ISingleUseNonceRegistry
    {
        /// <summary>
        /// Atomically marks <paramref name="nonce"/> as consumed.
        /// </summary>
        /// <param name="nonce">The server nonce being consumed.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>true</c> when this call was the first to consume the nonce (the
        /// caller may proceed); <c>false</c> when the nonce was already consumed
        /// (the caller must reject the request as a replay).
        /// </returns>
        ValueTask<bool> TryConsumeAsync(ByteString nonce, CancellationToken ct = default);
    }
}
