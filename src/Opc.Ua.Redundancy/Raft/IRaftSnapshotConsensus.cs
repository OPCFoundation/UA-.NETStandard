// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        ValueTask CompactAsync(ulong appliedIndex, ByteString snapshot, CancellationToken ct = default);
    }
}
