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

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an item of <see cref="IRaftSnapshotConsensus.CommittedItems"/>, a committed
    /// command or a snapshot, with its log index.
    /// </summary>
    public sealed class RaftCommittedItem
    {
        /// <summary>
        /// Creates an item.
        /// </summary>
        /// <param name="isSnapshot">Whether the item is a snapshot rather than a command.</param>
        /// <param name="index">The log index of the item.</param>
        /// <param name="data">The command, or the encoded state of a snapshot.</param>
        public RaftCommittedItem(bool isSnapshot, ulong index, ReadOnlyMemory<byte> data)
        {
            IsSnapshot = isSnapshot;
            Index = index;
            Data = data;
        }

        /// <summary>
        /// <c>true</c> for a snapshot: the state machine replaces its state with <see cref="Data"/>, which holds
        /// every command up to <see cref="Index"/>.
        /// </summary>
        public bool IsSnapshot { get; }

        /// <summary>
        /// The log index of the item; after applying it, the state of the state machine corresponds to this index.
        /// </summary>
        public ulong Index { get; }

        /// <summary>
        /// The command, or the encoded state of a snapshot.
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }
    }
}
