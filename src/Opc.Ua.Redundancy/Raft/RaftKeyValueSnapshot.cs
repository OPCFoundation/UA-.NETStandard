// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a snapshot created by <see cref="IRaftSnapshotManager.CreateSnapshotAsync"/>.
    /// </summary>
    public sealed class RaftKeyValueSnapshot
    {
        /// <summary>
        /// Creates a snapshot.
        /// </summary>
        /// <param name="lastIncludedCommand">The position of the last committed command the snapshot holds.</param>
        /// <param name="data">The encoded snapshot.</param>
        public RaftKeyValueSnapshot(long lastIncludedCommand, ByteString data)
        {
            LastIncludedCommand = lastIncludedCommand;
            Data = data;
        }

        /// <summary>
        /// The 1-based position, in the committed command stream of the consensus, of the last command whose effect
        /// the snapshot holds: the barrier the snapshot was captured at. Every replica observes the same command
        /// stream, so the position identifies the same prefix of the log on every replica. It counts the commands
        /// delivered on <see cref="IRaftConsensus.Committed"/>, not the entries of the consensus engine's own log.
        /// </summary>
        public long LastIncludedCommand { get; }

        /// <summary>
        /// The encoded snapshot, which also carries <see cref="LastIncludedCommand"/>. Persist it and pass it to
        /// <see cref="IRaftSnapshotManager.RestoreSnapshotAsync"/>. The format is versioned and owned by the store.
        /// </summary>
        public ByteString Data { get; }
    }
}
