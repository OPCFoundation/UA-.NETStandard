/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Legacy token-scoped reception window enforcing replay protection
    /// over the (<c>TokenId</c>, <c>SequenceNumber</c>, <c>Nonce</c>)
    /// triple. The built-in receive path adds authenticated PublisherId
    /// and WriterGroupId scoping without changing this public contract.
    /// </summary>
    /// <remarks>
    /// Implements the receiver-side replay protection requirement
    /// from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.2.3">
    /// Part 14 §7.2.2.3 Security NetworkMessage processing</see>.
    /// </remarks>
    public interface ISecurityTokenWindow
    {
        /// <summary>
        /// Attempts to accept an inbound NetworkMessage. Returns
        /// <see langword="false"/> if the (token, sequence) pair is
        /// a duplicate, falls below the sliding window's lower
        /// edge, or the nonce has already been used inside the
        /// current key's lifetime.
        /// </summary>
        /// <remarks>
        /// Implementations may conservatively reject a fresh nonce when using
        /// bounded probabilistic nonce tracking.
        /// </remarks>
        /// <param name="tokenId">SecurityHeader TokenId.</param>
        /// <param name="sequenceNumber">SecurityHeader SequenceNumber.</param>
        /// <param name="nonce">SecurityHeader Nonce bytes.</param>
        /// <returns>
        /// <see langword="true"/> when the message passes replay
        /// checks and should be processed.
        /// </returns>
        bool TryAccept(uint tokenId, ulong sequenceNumber, ReadOnlySpan<byte> nonce);

        /// <summary>
        /// Clears all accepted-sequence and nonce-seen state.
        /// Called when the writer-group restarts or the key rotates
        /// in a way that resets sequence numbering.
        /// </summary>
        void Reset();
    }

    internal interface IScopedSecurityTokenWindow
    {
        bool TryAccept(
            PublisherId publisherId,
            ushort writerGroupId,
            uint tokenId,
            ulong sequenceNumber,
            ReadOnlySpan<byte> nonce);
    }
}
