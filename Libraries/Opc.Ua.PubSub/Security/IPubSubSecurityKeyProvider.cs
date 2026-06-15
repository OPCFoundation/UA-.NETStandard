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

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Source of <see cref="PubSubSecurityKey"/> material for one
    /// <see cref="SecurityGroupDataType"/>. Implementations cover
    /// the SKS pull profile, local key store, push target and unit
    /// test fakes.
    /// </summary>
    /// <remarks>
    /// Implements the key-acquisition contract used by Publisher
    /// and Subscriber as described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>. Phase 8 will ship
    /// the SKS pull implementation and the local in-memory provider;
    /// Phase 1 only commits the contract.
    /// </remarks>
    public interface IPubSubSecurityKeyProvider
    {
        /// <summary>
        /// Identifier of the SecurityGroup this provider services.
        /// </summary>
        string SecurityGroupId { get; }

        /// <summary>
        /// Raised whenever the active token rotates.
        /// </summary>
        event EventHandler<PubSubKeyRotatedEventArgs>? KeyRotated;

        /// <summary>
        /// Returns the currently active token. Throws when no
        /// token is available (caller drives the
        /// <see cref="StateMachine.PubSubStateTransitionReason.Fatal"/>
        /// transition on the security subsystem).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PubSubSecurityKey> GetCurrentKeyAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to retrieve a specific token by its
        /// <see cref="PubSubSecurityKey.TokenId"/>. Returns
        /// <see langword="null"/> when the token has been rotated out
        /// or was never observed by this provider.
        /// </summary>
        /// <param name="tokenId">TokenId to look up.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PubSubSecurityKey?> TryGetKeyAsync(
            uint tokenId,
            CancellationToken cancellationToken = default);
    }
}
