// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: snapshot management of a store that is a replicated state machine over an
    /// <see cref="IRaftConsensus"/> log, such as <see cref="RaftSharedKeyValueStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An application bounds the consensus log with it: it creates a snapshot, persists
    /// <see cref="RaftKeyValueSnapshot.Data"/>, and on restart restores the snapshot before the store applies the
    /// log. <see cref="RaftKeyValueSnapshot.LastIncludedCommand"/> is the exact position in the committed command
    /// stream up to which the snapshot holds the state; commands after it are applied on top of it.
    /// </para>
    /// <para>
    /// The redundancy builders register the store's implementation as a singleton, so it can be injected wherever
    /// the <see cref="ISharedKeyValueStore"/> is. In eventual consistency mode the
    /// <see cref="HybridSharedKeyValueStore"/> delegates to its Raft store, so the snapshot holds the strong keyspaces.
    /// </para>
    /// </remarks>
    public interface IRaftSnapshotManager
    {
        /// <summary>
        /// Captures the state of the store. A barrier is committed through the consensus, and the state is captured
        /// when the store applies that barrier, so the snapshot holds exactly the commands committed up to and
        /// including the barrier.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The snapshot.</returns>
        ValueTask<RaftKeyValueSnapshot> CreateSnapshotAsync(CancellationToken ct = default);

        /// <summary>
        /// Replaces the state of the store with a snapshot before the store starts applying the log. Startup and
        /// restore are serialized: a store that has started, or is starting, rejects the restore.
        /// </summary>
        /// <param name="snapshot">The persisted <see cref="RaftKeyValueSnapshot.Data"/> of a snapshot.</param>
        /// <param name="replay">Which committed commands the consensus delivers after the restore.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="System.FormatException">The data is not a valid snapshot.</exception>
        /// <exception cref="System.InvalidOperationException">The store has started or is starting to apply the
        /// log.</exception>
        /// <exception cref="System.ObjectDisposedException">The store is disposed.</exception>
        ValueTask RestoreSnapshotAsync(
            ByteString snapshot,
            RaftSnapshotLogReplay replay = RaftSnapshotLogReplay.AfterSnapshot,
            CancellationToken ct = default);
    }
}
