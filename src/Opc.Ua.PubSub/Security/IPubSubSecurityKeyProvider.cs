// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// Part 14 §8.3 Security Key Service</see>. An SKS pull
    /// implementation and a local in-memory provider are provided.
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
