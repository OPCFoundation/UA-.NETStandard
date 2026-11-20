/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Event payload raised by an <see cref="IPubSubSecurityKeyProvider"/>
    /// when the active token rotates. Carries the new and prior
    /// <c>TokenId</c> so consumers can update their security headers
    /// and reset replay windows atomically.
    /// </summary>
    /// <remarks>
    /// Implements the rotation notification defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>.
    /// </remarks>
    public sealed class PubSubKeyRotatedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubKeyRotatedEventArgs"/>.
        /// </summary>
        /// <param name="newTokenId">TokenId of the new key.</param>
        /// <param name="previousTokenId">
        /// TokenId of the prior key, or <see langword="null"/> for the
        /// very first key issued for the group.
        /// </param>
        /// <param name="effectiveAt">When the new key becomes active.</param>
        public PubSubKeyRotatedEventArgs(
            uint newTokenId,
            uint? previousTokenId,
            DateTimeUtc effectiveAt)
        {
            NewTokenId = newTokenId;
            PreviousTokenId = previousTokenId;
            EffectiveAt = effectiveAt;
        }

        /// <summary>
        /// TokenId of the new key.
        /// </summary>
        public uint NewTokenId { get; }

        /// <summary>
        /// TokenId of the prior key, or <see langword="null"/> on
        /// first issuance.
        /// </summary>
        public uint? PreviousTokenId { get; }

        /// <summary>
        /// Effective time of the rotation.
        /// </summary>
        public DateTimeUtc EffectiveAt { get; }
    }
}
