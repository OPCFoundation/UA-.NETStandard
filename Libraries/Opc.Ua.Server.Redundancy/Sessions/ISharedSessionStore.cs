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
    /// Shares session context across server replicas keyed by the session
    /// <c>AuthenticationToken</c>, enabling fast active/passive reconnect:
    /// after a failover a client re-activates against the promoted replica
    /// using only its token, without a full re-handshake.
    /// </summary>
    /// <remarks>
    /// Analogous to <see cref="ISubscriptionStore"/> for subscriptions. The
    /// default implementation
    /// (<see cref="SharedKeyValueSessionStore"/>) persists entries in the
    /// same shared key/value backend as the distributed address space.
    /// </remarks>
    public interface ISharedSessionStore
    {
        /// <summary>
        /// Stores or replaces a session entry (keyed by its
        /// <see cref="SharedSessionEntry.AuthenticationToken"/>).
        /// </summary>
        /// <param name="entry">The session entry.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask PutAsync(SharedSessionEntry entry, CancellationToken ct = default);

        /// <summary>
        /// Looks up a session entry by authentication token.
        /// </summary>
        /// <param name="authenticationToken">The session token.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The entry, or <c>null</c> when absent.</returns>
        ValueTask<SharedSessionEntry?> TryGetAsync(NodeId authenticationToken, CancellationToken ct = default);

        /// <summary>
        /// Removes a session entry (e.g. on close).
        /// </summary>
        /// <param name="authenticationToken">The session token.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> when an entry was removed.</returns>
        ValueTask<bool> RemoveAsync(NodeId authenticationToken, CancellationToken ct = default);
    }
}
