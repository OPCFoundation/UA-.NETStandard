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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a consensus replica whose engine bounds its log with snapshots of the
    /// state machine. It delivers each committed command with its log index, delivers a snapshot that replaces the
    /// state of the state machine (when the replica installs the leader's snapshot, and first after a restart), and
    /// records snapshots and discards the log behind them.
    /// </summary>
    public interface IRaftSnapshotConsensus : IRaftConsensus
    {
        /// <summary>
        /// The apply stream, in log order: committed commands and snapshots, each with its log index. It reads the
        /// same queue as <see cref="IRaftConsensus.Committed"/>, so a state machine reads one of them.
        /// </summary>
        ChannelReader<RaftCommittedItem> CommittedItems { get; }

        /// <summary>
        /// Records <paramref name="snapshot"/> as the state of the state machine at <paramref name="appliedIndex"/>
        /// and discards the log up to that index. The engine sends the snapshot to a replica whose log ends before
        /// the discarded entries, and delivers it on <see cref="CommittedItems"/> after a restart.
        /// </summary>
        /// <param name="appliedIndex">
        /// The <see cref="RaftCommittedItem.Index"/> of the last item the state machine applied; at least 1 and later
        /// than the latest snapshot the engine holds.
        /// </param>
        /// <param name="snapshot">The encoded state of the state machine at <paramref name="appliedIndex"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask CompactAsync(ulong appliedIndex, ReadOnlyMemory<byte> snapshot, CancellationToken ct = default);
    }
}
