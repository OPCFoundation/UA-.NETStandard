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

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Per-writer-group reception window enforcing replay protection
    /// over the (<c>TokenId</c>, <c>SequenceNumber</c>, <c>Nonce</c>)
    /// triple. Implementations track the last accepted sequence
    /// number per token plus a sliding bitmap of recently seen
    /// sequence numbers, and reject duplicate / out-of-window /
    /// nonce-reuse frames.
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
}
