// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a strongly-consistent (CP), linearizable <see cref="ISharedKeyValueStore"/>
    /// implemented as a replicated state machine over an <see cref="IRaftConsensus"/> log. Unlike the
    /// eventually-consistent <see cref="ReplicatedSharedKeyValueStore"/>, this store provides a real
    /// <see cref="CompareAndSwapAsync"/> and a <see cref="WatchAsync"/> change-feed, which makes it the right backend
    /// for exactly-once primitives (the single-use session nonce registry) and master-write lease/election.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every mutation (<c>Set</c>/<c>Delete</c>/<c>CAS</c>) is encoded as an opaque command and
    /// <see cref="IRaftConsensus.ProposeAsync"/>d. A single applier drains <see cref="IRaftConsensus.Committed"/> in
    /// log order and mutates a materialized map deterministically — so a compare-and-swap is decided at apply time
    /// against the committed state, identically on every replica. The originating store correlates the applied command
    /// back to the caller only if the committed bytes match the proposal.
    /// </para>
    /// <para>
    /// Reads (<see cref="TryGetAsync"/>/<see cref="ScanAsync"/>) first commit and apply a no-op barrier, then snapshot
    /// the materialized map. The change-feed (<see cref="WatchAsync"/>) is derived from the same apply stream, so
    /// watchers observe changes in commit order.
    /// </para>
    /// <para>
    /// Over an <see cref="IRaftSnapshotConsensus"/> the store applies the engine's apply stream with log indices
    /// (<see cref="AppliedIndex"/>). A snapshot on that stream (a lagging replica installing the leader's snapshot, or
    /// the first item after a restart) replaces the materialized map, and watchers receive the changes between the
    /// former and the new map. With a compaction interval, the store snapshots its map every that many applied
    /// commands and lets the engine discard the log behind the snapshot, which bounds the Raft log.
    /// </para>
    /// </remarks>
    public sealed class RaftSharedKeyValueStore :
        IGuardedSharedKeyValueStore,
        ISharedKeyValueStoreConsistency,
        IRaftSnapshotManager,
        IAsyncDisposable
    {
        /// <inheritdoc/>
        public bool IsLinearizable(string key)
        {
            return key != null && Volatile.Read(ref m_applierFault) == null && Volatile.Read(ref m_disposed) == 0 &&
                m_consensus is not DefaultRaftConsensus { IsAvailable: false };
        }

        /// <inheritdoc/>
        public bool IsProcessLocal(string key)
        {
            return m_processLocal;
        }

        /// <summary>
        /// Gets whether this store has no externally accessible backend.
        /// </summary>
        internal bool UsesOnlyInProcessMemory =>
            m_consensus is InProcessRaftConsensus or DefaultRaftConsensus { UsesOnlyInProcessMemory: true };

        /// <summary>
        /// Creates a store backed by a private single-node in-process consensus replica (always the leader). Useful
        /// for single-process deployments and tests.
        /// </summary>
        // CA2000: ownership of the created replica transfers to this store
        // (ownsConsensus: true) and it is disposed in DisposeAsync.
#pragma warning disable CA2000
        public RaftSharedKeyValueStore()
            : this(new InProcessRaftConsensus(), ownsConsensus: true)
        {
        }
#pragma warning restore CA2000

        /// <summary>
        /// Creates a store backed by the supplied consensus replica.
        /// </summary>
        /// <param name="consensus">The consensus replica that replicates this store's commands.</param>
        /// <param name="ownsConsensus">
        /// When <c>true</c>, the consensus replica is disposed together with this store. Pass <c>false</c> when the
        /// replica is shared (for example with a <see cref="RaftLeaderElection"/>) and owned elsewhere.
        /// </param>
        /// <param name="commitTimeout">
        /// How long a proposal waits to commit before failing with a <see cref="TimeoutException"/>, so a caller is
        /// never blocked indefinitely when there is no leader, a leadership change discards the entry, or quorum is
        /// lost. Defaults to 30 seconds; pass <see cref="Timeout.InfiniteTimeSpan"/> to wait only on the caller's
        /// token.
        /// </param>
        public RaftSharedKeyValueStore(
            IRaftConsensus consensus,
            bool ownsConsensus = false,
            TimeSpan commitTimeout = default)
        {
            m_consensus = consensus ?? throw new ArgumentNullException(nameof(consensus));
            m_processLocal = consensus is not IRaftConsensusLocality { IsProcessLocal: false };
            m_ownsConsensus = ownsConsensus;
            m_commitTimeout = commitTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : commitTimeout;
        }

        /// <summary>
        /// Creates a store backed by the supplied consensus replica that bounds the Raft log: after every
        /// <paramref name="compactionInterval"/> applied commands it snapshots its state into the consensus, which
        /// discards the log behind the snapshot (<see cref="IRaftSnapshotConsensus.CompactAsync"/>).
        /// </summary>
        /// <param name="consensus">The consensus replica that replicates this store's commands.</param>
        /// <param name="ownsConsensus">
        /// When <c>true</c>, the consensus replica is disposed together with this store.
        /// </param>
        /// <param name="commitTimeout">
        /// How long a proposal waits to commit before failing with a <see cref="TimeoutException"/>;
        /// <see cref="TimeSpan.Zero"/> selects the default of 30 seconds.
        /// </param>
        /// <param name="compactionInterval">
        /// When positive, the number of applied commands after which the store snapshots its state into the consensus.
        /// Zero never compacts. A positive interval requires an <see cref="IRaftSnapshotConsensus"/>.
        /// </param>
        /// <param name="logger">Optional logger, for example of a compaction that failed and is retried.</param>
        /// <exception cref="ArgumentNullException"><paramref name="consensus"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="compactionInterval"/> is negative.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="compactionInterval"/> is positive and the consensus does not implement
        /// <see cref="IRaftSnapshotConsensus"/>.
        /// </exception>
        public RaftSharedKeyValueStore(
            IRaftConsensus consensus,
            bool ownsConsensus,
            TimeSpan commitTimeout,
            int compactionInterval,
            ILogger? logger = null)
            : this(consensus, ownsConsensus, commitTimeout)
        {
            if (compactionInterval < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(compactionInterval),
                    "The compaction interval is negative.");
            }
            if (compactionInterval > 0 && consensus is not IRaftSnapshotConsensus)
            {
                throw new ArgumentException(
                    "Compaction requires a consensus that implements IRaftSnapshotConsensus.",
                    nameof(compactionInterval));
            }
            m_compactionInterval = compactionInterval;
            m_logger = logger;
        }

        /// <summary>
        /// The log index of the last item of <see cref="IRaftSnapshotConsensus.CommittedItems"/> the store applied:
        /// the log position its state corresponds to. Zero until the store applied an item, and always zero over a
        /// consensus that does not implement <see cref="IRaftSnapshotConsensus"/>.
        /// </summary>
        public ulong AppliedIndex => Volatile.Read(ref m_appliedIndex);

        /// <inheritdoc/>
        public async ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            _ = await ProposeAsync(OpBarrier, string.Empty, default, default, ct).ConfigureAwait(false);
            lock (m_lock)
            {
                ThrowIfApplierStopped();
                return m_state.TryGetValue(key, out ByteString value) ? (true, value) : (false, default);
            }
        }

        /// <inheritdoc/>
        public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            await ProposeAsync(OpSet, key, default, value, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            return ProposeAsync(OpCas, key, expected, value, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> CompareAndSwapWhenAsync(
            string key,
            ByteString expected,
            ByteString value,
            string guardKey,
            ByteString guardExpected,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(guardKey) || key == guardKey || guardExpected.IsNull)
            {
                throw new ArgumentException("A separate guard with a non-null expected value is required.");
            }
            ProposalResult result = await ProposeCoreAsync(
                OpGuardedCas, key, expected, value, captureSnapshot: false, ct, guardKey, guardExpected)
                .ConfigureAwait(false);
            return result.Applied;
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return ProposeAsync(OpDelete, key, default, default, ct);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The barrier is proposed like any other command; the applier captures the state while it applies that
        /// barrier, under the same lock that orders every command, so no command committed after the barrier is in
        /// the snapshot.
        /// </remarks>
        public async ValueTask<RaftKeyValueSnapshot> CreateSnapshotAsync(CancellationToken ct = default)
        {
            ProposalResult result = await ProposeCoreAsync(
                OpBarrier, string.Empty, default, default, captureSnapshot: true, ct).ConfigureAwait(false);
            return result.Snapshot!;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The restore takes the same gate as the startup of the store, and the startup marks the store as started
        /// before it releases the gate, so a restore can never interleave with a startup that already lets the
        /// consensus apply commands.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="replay"/> is not a defined value.</exception>
        public async ValueTask RestoreSnapshotAsync(
            ByteString snapshot,
            RaftSnapshotLogReplay replay = RaftSnapshotLogReplay.AfterSnapshot,
            CancellationToken ct = default)
        {
            if (replay is not (RaftSnapshotLogReplay.AfterSnapshot or RaftSnapshotLogReplay.FromFirstCommand))
            {
                throw new ArgumentOutOfRangeException(nameof(replay));
            }

            (Dictionary<string, ByteString> state, long lastIncludedCommand) = DecodeSnapshot(snapshot.Span);

            CancellationToken disposeToken;
            lock (m_proposalLock)
            {
                ThrowIfDisposed();
                disposeToken = m_cts.Token;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, disposeToken);
            await m_startGate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (Volatile.Read(ref m_started) != 0)
                {
                    throw new InvalidOperationException(
                        "A snapshot can only be restored before the store applies the log.");
                }
                lock (m_lock)
                {
                    m_state.Clear();
                    foreach (KeyValuePair<string, ByteString> entry in state)
                    {
                        m_state[entry.Key] = entry.Value;
                    }
                    m_appliedCommands = replay == RaftSnapshotLogReplay.AfterSnapshot ? lastIncludedCommand : 0;
                    m_skipThroughCommand = replay == RaftSnapshotLogReplay.FromFirstCommand ? lastIncludedCommand : 0;
                }
            }
            finally
            {
                m_startGate.Release();
            }
        }

        /// <summary>
        /// Encodes the state and the position of the command being applied. The caller holds <c>m_lock</c>.
        /// </summary>
        private byte[] EncodeSnapshotLocked(long lastIncludedCommand, ulong? logIndex = null)
        {
            int engineHeaderLength = logIndex.HasValue ? kEngineHeaderLength : 0;
            long length = kSnapshotHeaderLength + engineHeaderLength;
            if (m_state.Count > kMaxSnapshotEntries)
            {
                throw new InvalidOperationException("The state exceeds the Raft key/value snapshot entry limit.");
            }
            foreach (KeyValuePair<string, ByteString> entry in m_state)
            {
                length += (long)kSnapshotEntryOverhead + s_strictUtf8.GetByteCount(entry.Key) + entry.Value.Length;
                if (length > MaxSnapshotBytes)
                {
                    throw new InvalidOperationException("The state exceeds the Raft key/value snapshot size limit.");
                }
            }

            var snapshot = new byte[(int)length];
            if (logIndex.HasValue)
            {
                snapshot[0] = kEngineSnapshotFormat;
                BinaryPrimitives.WriteUInt64LittleEndian(snapshot.AsSpan(1), logIndex.Value);
            }
            int offset = engineHeaderLength;
            snapshot[offset++] = s_snapshotEncodingVersion;
            BinaryPrimitives.WriteInt64LittleEndian(snapshot.AsSpan(offset), lastIncludedCommand);
            offset += sizeof(long);
            WriteLength(snapshot, ref offset, m_state.Count);
            foreach (KeyValuePair<string, ByteString> entry in m_state)
            {
                WriteKey(snapshot, ref offset, entry.Key, s_strictUtf8.GetByteCount(entry.Key));
                snapshot[offset++] = entry.Value.IsNull ? kSnapshotNullValue : kSnapshotValue;
                WriteLength(snapshot, ref offset, entry.Value.Length);
                entry.Value.Span.CopyTo(snapshot.AsSpan(offset));
                offset += entry.Value.Length;
            }
            return snapshot;
        }

        /// <summary>
        /// Validates the application envelope for a snapshot before the engine persists or recovers it.
        /// </summary>
        internal static bool IsValidEngineSnapshot(ReadOnlySpan<byte> snapshot, ulong logIndex)
        {
            try
            {
                _ = DecodeEngineSnapshot(snapshot, logIndex);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static (Dictionary<string, ByteString> State, long LastIncludedCommand) DecodeEngineSnapshot(
            ReadOnlySpan<byte> snapshot,
            ulong logIndex)
        {
            if (snapshot.IsEmpty || snapshot.Length > MaxSnapshotBytes || logIndex == 0)
            {
                throw new FormatException("The Raft engine snapshot has an invalid size or log index.");
            }
            switch (snapshot[0])
            {
                case kEngineSnapshotFormat when snapshot.Length >= kEngineHeaderLength &&
                    BinaryPrimitives.ReadUInt64LittleEndian(snapshot.Slice(1)) == logIndex:
                    return DecodeSnapshot(snapshot.Slice(kEngineHeaderLength), logIndex);
                case kLegacyEngineSnapshotFormat:
                    return DecodeSnapshot(snapshot.Slice(1), logIndex);
                case kEngineSnapshotFormat:
                    throw new FormatException("The Raft engine snapshot does not match its log index.");
                default:
                    throw new FormatException("The Raft engine delivered a snapshot in an unsupported format.");
            }
        }

        /// <summary>
        /// Decodes a snapshot strictly and bounds the state it could materialize before allocating it.
        /// </summary>
        /// <exception cref="FormatException">The data is not a valid snapshot.</exception>
        private static (Dictionary<string, ByteString> State, long LastIncludedCommand) DecodeSnapshot(
            ReadOnlySpan<byte> snapshot,
            ulong logIndex = ulong.MaxValue)
        {
            if (snapshot.Length < kSnapshotHeaderLength || snapshot.Length > MaxSnapshotBytes ||
                snapshot[0] != s_snapshotEncodingVersion)
            {
                throw new FormatException("The snapshot has an invalid size or encoding version.");
            }
            long lastIncludedCommand = BinaryPrimitives.ReadInt64LittleEndian(snapshot.Slice(1));
            if (lastIncludedCommand is < 1 or long.MaxValue || (ulong)lastIncludedCommand > logIndex)
            {
                throw new FormatException("The snapshot holds an invalid command position.");
            }
            int count = BinaryPrimitives.ReadInt32LittleEndian(snapshot.Slice(1 + sizeof(long)));
            if (count < 0 || count > kMaxSnapshotEntries || count > lastIncludedCommand ||
                count > (snapshot.Length - kSnapshotHeaderLength) / kSnapshotEntryOverhead)
            {
                throw new FormatException("The snapshot holds an invalid entry count.");
            }

            var state = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            ReadOnlySpan<byte> remaining = snapshot.Slice(kSnapshotHeaderLength);
            for (int ii = 0; ii < count; ii++)
            {
                ReadOnlySpan<byte> key = ReadBlock(ref remaining);
                if (remaining.IsEmpty || remaining[0] is not (kSnapshotNullValue or kSnapshotValue))
                {
                    throw new FormatException("The snapshot holds an invalid value marker.");
                }
                bool isNull = remaining[0] == kSnapshotNullValue;
                remaining = remaining.Slice(1);
                ReadOnlySpan<byte> value = ReadBlock(ref remaining);
                if ((isNull && !value.IsEmpty) || key.Length + value.Length > MaxCommandBytes - kSetCommandOverhead)
                {
                    throw new FormatException("The snapshot holds an invalid entry size.");
                }
                string decodedKey = DecodeKey(key);
                if (!state.TryAdd(decodedKey, isNull ? default : ByteString.From(value)))
                {
                    throw new FormatException("The snapshot holds a key twice.");
                }
            }
            if (!remaining.IsEmpty)
            {
                throw new FormatException("The snapshot has trailing data.");
            }
            return (state, lastIncludedCommand);
        }

        private static string DecodeKey(ReadOnlySpan<byte> key)
        {
            try
            {
                return s_strictUtf8.GetString(key.ToArray());
            }
            catch (DecoderFallbackException e)
            {
                throw new FormatException("The Raft key is not valid UTF-8.", e);
            }
        }

        private static ReadOnlySpan<byte> ReadBlock(ref ReadOnlySpan<byte> remaining)
        {
            if (remaining.Length < sizeof(int))
            {
                throw new FormatException("The Raft payload is truncated.");
            }
            int length = BinaryPrimitives.ReadInt32LittleEndian(remaining);
            remaining = remaining.Slice(sizeof(int));
            if (length < 0 || length > remaining.Length)
            {
                throw new FormatException("The Raft payload holds an invalid length.");
            }
            ReadOnlySpan<byte> block = remaining.Slice(0, length);
            remaining = remaining.Slice(length);
            return block;
        }
        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            keyPrefix ??= string.Empty;
            _ = await ProposeAsync(OpBarrier, string.Empty, default, default, ct).ConfigureAwait(false);

            List<KeyValuePair<string, ByteString>> snapshot;
            lock (m_lock)
            {
                ThrowIfApplierStopped();
                snapshot = new List<KeyValuePair<string, ByteString>>(m_state.Count);
                foreach (KeyValuePair<string, ByteString> entry in m_state)
                {
                    if (entry.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
                    {
                        snapshot.Add(entry);
                    }
                }
            }

            foreach (KeyValuePair<string, ByteString> entry in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                yield return entry;
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValueChange> WatchAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);

            var watcher = new Watcher(keyPrefix ?? string.Empty);
            CancellationToken disposeToken;
            lock (m_proposalLock)
            {
                ThrowIfDisposed();
                ThrowIfApplierStopped();
                disposeToken = m_cts.Token;
                lock (m_lock)
                {
                    m_watchers.Add(watcher);
                }
            }

            using var linkedCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct, disposeToken);
            try
            {
                await foreach (KeyValueChange change in watcher.Channel.Reader
                    .ReadAllAsync(linkedCts.Token)
                    .ConfigureAwait(false))
                {
                    yield return change;
                }
            }
            finally
            {
                lock (m_lock)
                {
                    m_watchers.Remove(watcher);
                }
                watcher.Channel.Writer.TryComplete();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_proposalLock)
            {
                if (m_disposed != 0)
                {
                    return;
                }
                m_disposed = 1;
                m_cts.Cancel();
                foreach (PendingProposal pending in m_pending.Values)
                {
                    pending.Completion.TrySetCanceled(m_cts.Token);
                }
                m_pending.Clear();
            }

            await m_startGate.WaitAsync().ConfigureAwait(false);
            m_startGate.Release();
            if (m_applyLoop != null)
            {
                try
                {
                    await m_applyLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }

            lock (m_lock)
            {
                foreach (Watcher watcher in m_watchers)
                {
                    watcher.Channel.Writer.TryComplete();
                }
                m_watchers.Clear();
            }

            if (m_ownsConsensus)
            {
                await m_consensus.DisposeAsync().ConfigureAwait(false);
            }

            m_cts.Dispose();
            m_startGate.Dispose();
        }

        private async ValueTask<bool> ProposeAsync(
            byte op,
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct)
        {
            ProposalResult result = await ProposeCoreAsync(op, key, expected, value, captureSnapshot: false, ct)
                .ConfigureAwait(false);
            return result.Applied;
        }

        private async ValueTask<ProposalResult> ProposeCoreAsync(
            byte op,
            string key,
            ByteString expected,
            ByteString value,
            bool captureSnapshot,
            CancellationToken ct,
            string? guardKey = null,
            ByteString guardExpected = default)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            ct.ThrowIfCancellationRequested();
            int length = GetCommandLength(
                op, key, expected, value, guardKey, guardExpected, out int keyLength, out int guardKeyLength);
            long requestId = Interlocked.Increment(ref m_requestId);
            if (requestId <= 0)
            {
                throw new InvalidOperationException("The Raft key/value store exhausted its request identifiers.");
            }
            byte[] command = Encode(
                op, m_originator, requestId, key, keyLength, expected, value, length,
                guardKey, guardKeyLength, guardExpected);
            var proposal = new PendingProposal(SHA256.HashData(command), captureSnapshot);

            await EnsureStartedAsync(ct).ConfigureAwait(false);
            CancellationToken disposeToken;
            lock (m_proposalLock)
            {
                ThrowIfDisposed();
                ThrowIfApplierStopped();
                m_pending[requestId] = proposal;
                disposeToken = m_cts.Token;
            }

            // Bound the wait for commit: either the caller's token or the commit
            // timeout fails the pending proposal, so a no-leader / leadership-
            // change / lost-quorum window never blocks the caller indefinitely.
            using var commitCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct, disposeToken);
            try
            {
                if (m_commitTimeout != Timeout.InfiniteTimeSpan)
                {
                    commitCts.CancelAfter(m_commitTimeout);
                }
                await m_consensus
                    .ProposeAsync(command, commitCts.Token)
                    .ConfigureAwait(false);
                return await proposal.Completion.Task.WaitAsync(commitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested &&
                    !disposeToken.IsCancellationRequested &&
                    commitCts.IsCancellationRequested)
            {
                throw CreateCommitTimeoutException();
            }
            finally
            {
                m_pending.TryRemove(requestId, out _);
            }
        }

        private async ValueTask EnsureStartedAsync(CancellationToken ct)
        {
            CancellationToken disposeToken;
            lock (m_proposalLock)
            {
                ThrowIfDisposed();
                disposeToken = m_cts.Token;
            }
            if (Volatile.Read(ref m_started) == 1)
            {
                return;
            }

            using var linkedCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct, disposeToken);
            await m_startGate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (m_started == 0)
                {
                    await m_consensus
                        .StartAsync(linkedCts.Token)
                        .ConfigureAwait(false);
                    m_applyLoop = Task.Run(() => ApplyLoopAsync(m_cts.Token), m_cts.Token);
                    Volatile.Write(ref m_started, 1);
                }
            }
            finally
            {
                m_startGate.Release();
            }
        }

        /// <summary>
        /// Refuses operations once the applier stopped. Registration under <c>m_proposalLock</c>
        /// orders proposals and watches with the fault transition.
        /// </summary>
        private void ThrowIfApplierStopped()
        {
            if (Volatile.Read(ref m_applierFault) is Exception error)
            {
                throw new InvalidOperationException(
                    "The Raft key/value store stopped applying committed commands.",
                    error);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(RaftSharedKeyValueStore));
            }
        }

        private static TimeoutException CreateCommitTimeoutException()
        {
            return new TimeoutException(
                "The Raft proposal did not commit within the commit timeout " +
                "(no leader, leadership change, or lost quorum). Retry the " +
                "operation.");
        }

        private async Task ApplyLoopAsync(CancellationToken ct)
        {
            try
            {
                if (m_consensus is IRaftSnapshotConsensus snapshotting)
                {
                    await ApplyItemsAsync(snapshotting, ct).ConfigureAwait(false);
                }
                else
                {
                    await foreach (ReadOnlyMemory<byte> command in m_consensus.Committed
                        .ReadAllAsync(ct)
                        .ConfigureAwait(false))
                    {
                        ApplyCommittedCommand(command.Span);
                    }
                }
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The Raft committed stream ended unexpectedly.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                // The applier is the sole completer of pending proposals and the
                // sole writer of the change feed; if it ever dies unexpectedly,
                // fail every pending proposal and end every watch with the error,
                // and refuse every later proposal and watch.
                lock (m_proposalLock)
                {
                    if (m_consensus is DefaultRaftConsensus consensus)
                    {
                        consensus.ReportStateMachineFault(ex);
                    }
                    Volatile.Write(ref m_applierFault, ex);
                    lock (m_lock)
                    {
                        foreach (Watcher watcher in m_watchers)
                        {
                            watcher.Channel.Writer.TryComplete(ex);
                        }
                        m_watchers.Clear();
                    }
                }
                foreach (PendingProposal pending in m_pending.Values)
                {
                    pending.Completion.TrySetException(ex);
                }
            }
        }

        /// <summary>
        /// Applies the apply stream of a consensus with log indices: commands, and snapshots that replace the state.
        /// With a compaction interval, snapshots the state into the consensus after every that many commands.
        /// </summary>
        private async Task ApplyItemsAsync(IRaftSnapshotConsensus consensus, CancellationToken ct)
        {
            ulong snapshotIndex = 0;
            int sinceSnapshot = 0;
            await foreach (RaftCommittedItem item in consensus.CommittedItems
                .ReadAllAsync(ct)
                .ConfigureAwait(false))
            {
                if (item.IsSnapshot)
                {
                    // A snapshot that cannot be decoded leaves the state unknown: it stops the applier.
                    InstallEngineSnapshot(item.Data.Span, item.Index);
                    snapshotIndex = item.Index;
                    sinceSnapshot = 0;
                }
                else
                {
                    ApplyCommittedCommand(item.Data.Span, item.Index);
                    sinceSnapshot++;
                }

                if (m_compactionInterval > 0 &&
                    sinceSnapshot >= m_compactionInterval &&
                    item.Index > snapshotIndex)
                {
                    sinceSnapshot = 0;
                    if (await TryCompactAsync(consensus, item.Index, ct).ConfigureAwait(false))
                    {
                        snapshotIndex = item.Index;
                    }
                }
            }
        }

        /// <summary>
        /// Snapshots the state into the consensus at <paramref name="index"/>, the log index of the item just applied.
        /// A failure leaves the log intact, only not yet bounded; the next interval retries.
        /// </summary>
        private async Task<bool> TryCompactAsync(IRaftSnapshotConsensus consensus, ulong index, CancellationToken ct)
        {
            byte[] snapshot;
            try
            {
                lock (m_lock)
                {
                    if (m_appliedCommands < m_skipThroughCommand || m_appliedCommands != m_lastEngineCommand)
                    {
                        // The state holds commands the log has not delivered again yet (a restored
                        // snapshot replayed from the first command), or includes an external baseline
                        // not represented in this engine's log.
                        return false;
                    }
                    snapshot = EncodeSnapshotLocked(m_appliedCommands, index);
                }
                await consensus.CompactAsync(index, new ByteString(snapshot), ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // A cancellation the store did not request is a failed compaction like any other.
                m_logger?.RaftLogCompactionFailed(ex, index);
                return false;
            }
        }

        /// <summary>
        /// Replaces the state with a snapshot the consensus delivered (a lagging replica installing the leader's
        /// snapshot, or the first item after a restart) and publishes the differences to the watchers. A pending
        /// operation whose command the snapshot covers is not applied on its own; it fails with the commit timeout,
        /// its outcome unknown.
        /// </summary>
        /// <remarks>
        /// The snapshot carries the position of its last command in the command stream, so the commands after it
        /// keep the positions they have on every other replica. The consensus delivers every command after the
        /// snapshot, so the snapshot also replaces a state restored with <see cref="RestoreSnapshotAsync"/>, and no
        /// command is skipped after it.
        /// </remarks>
        /// <exception cref="FormatException">The snapshot is not one this store wrote.</exception>
        private void InstallEngineSnapshot(ReadOnlySpan<byte> snapshot, ulong logIndex)
        {
            (Dictionary<string, ByteString> state, long lastIncludedCommand) = DecodeEngineSnapshot(snapshot, logIndex);
            lock (m_lock)
            {
                if (logIndex <= m_lastLogIndex || lastIncludedCommand < m_lastEngineCommand ||
                    (ulong)(lastIncludedCommand - m_lastEngineCommand) > logIndex - m_lastLogIndex)
                {
                    throw new FormatException("The Raft engine snapshot does not advance the applied log.");
                }
                var changes = new List<KeyValueChange>();
                foreach (KeyValuePair<string, ByteString> entry in m_state)
                {
                    if (!state.ContainsKey(entry.Key))
                    {
                        changes.Add(new KeyValueChange { Kind = KeyValueChangeKind.Delete, Key = entry.Key });
                    }
                }
                foreach (KeyValuePair<string, ByteString> entry in state)
                {
                    // ByteString equality treats a null and an empty value alike; the snapshot keeps them apart.
                    if (!m_state.TryGetValue(entry.Key, out ByteString current) ||
                        current.IsNull != entry.Value.IsNull ||
                        !current.Equals(entry.Value))
                    {
                        changes.Add(new KeyValueChange
                        {
                            Kind = KeyValueChangeKind.Set,
                            Key = entry.Key,
                            Value = entry.Value
                        });
                    }
                }
                m_state.Clear();
                foreach (KeyValuePair<string, ByteString> entry in state)
                {
                    m_state[entry.Key] = entry.Value;
                }
                m_appliedCommands = lastIncludedCommand;
                m_skipThroughCommand = 0;
                m_lastLogIndex = logIndex;
                m_lastEngineCommand = lastIncludedCommand;

                // A watcher that reads AppliedIndex on a change sees the index of the state it reports.
                Volatile.Write(ref m_appliedIndex, logIndex);
                foreach (KeyValueChange change in changes)
                {
                    PublishLocked(change);
                }
            }
        }

        /// <summary>
        /// Applies a committed command at the next position of the command stream. With the command's
        /// <paramref name="logIndex"/> (a consensus with log indices), publishes it as <see cref="AppliedIndex"/>
        /// once the state holds the command, before watchers and the proposer learn of it.
        /// </summary>
        private void ApplyCommittedCommand(ReadOnlySpan<byte> command, ulong? logIndex = null)
        {
            if (command.Length is < kCommandHeaderLength or > MaxCommandBytes || command[0] != s_commandEncodingVersion)
            {
                throw new FormatException("The committed Raft key/value command has an invalid size or version.");
            }
            byte op = command[1];
            var originator = new Guid(command.Slice(2, 16).ToArray());
            long requestId = BinaryPrimitives.ReadInt64LittleEndian(command.Slice(18));
            if (op is not (OpSet or OpDelete or OpCas or OpBarrier or OpGuardedCas) ||
                originator == Guid.Empty || requestId <= 0)
            {
                throw new FormatException("The committed Raft key/value command has an invalid operation or identity.");
            }
            ReadOnlySpan<byte> remaining = command.Slice(26);
            string key = DecodeKey(ReadBlock(ref remaining));
            ByteString expected = op is OpCas or OpGuardedCas ? ReadValue(ref remaining) : default;
            ByteString value = op is OpSet or OpCas or OpGuardedCas ? ReadValue(ref remaining) : default;
            string? guardKey = null;
            ByteString guardExpected = default;
            if (op == OpGuardedCas)
            {
                guardKey = DecodeKey(ReadBlock(ref remaining));
                guardExpected = ReadValue(ref remaining);
                if (guardKey.Length == 0 || guardKey == key || guardExpected.IsNull)
                {
                    throw new FormatException("The committed guarded Raft command has an invalid guard.");
                }
            }
            if (!remaining.IsEmpty || (op == OpBarrier && key.Length != 0))
            {
                throw new FormatException("The committed Raft key/value command has unexpected data.");
            }

            PendingProposal? pending = null;
            if (originator == m_originator &&
                m_pending.TryGetValue(requestId, out PendingProposal? candidate) &&
                !candidate.Completion.Task.IsCompleted &&
                candidate.Digest.AsSpan().SequenceEqual(HashCommittedCommand(command)))
            {
                pending = candidate;
            }
            bool result = false;
            KeyValueChange? change = null;
            lock (m_lock)
            {
                if (logIndex.HasValue && logIndex.Value <= m_lastLogIndex)
                {
                    throw new FormatException("The committed Raft key/value command is out of log order.");
                }
                long position = checked(m_appliedCommands + 1);
                if (position == long.MaxValue || (position <= m_skipThroughCommand && pending != null))
                {
                    throw new FormatException("The committed Raft key/value command has an invalid replay position.");
                }
                if (logIndex.HasValue)
                {
                    m_lastLogIndex = logIndex.Value;
                    m_lastEngineCommand++;
                }
                m_appliedCommands = position;
                if (position <= m_skipThroughCommand)
                {
                    // This command is already represented by the restored state.
                    if (position == m_skipThroughCommand)
                    {
                        PublishAppliedIndex(logIndex);
                    }
                    return;
                }

                switch (op)
                {
                    case OpSet:
                        m_state[key] = value;
                        change = new KeyValueChange { Kind = KeyValueChangeKind.Set, Key = key, Value = value };
                        result = true;
                        break;
                    case OpDelete:
                        result = m_state.Remove(key);
                        if (result)
                        {
                            change = new KeyValueChange { Kind = KeyValueChangeKind.Delete, Key = key };
                        }
                        break;
                    case OpCas:
                    case OpGuardedCas:
                        if (guardKey != null &&
                            (!m_state.TryGetValue(guardKey, out ByteString guard) || guard != guardExpected))
                        {
                            break;
                        }

                        bool present = m_state.TryGetValue(key, out ByteString current);
                        bool matches = expected.IsNull ? !present : present && current.Equals(expected);
                        if (matches)
                        {
                            if (value.IsNull)
                            {
                                if (present)
                                {
                                    m_state.Remove(key);
                                    change = new KeyValueChange
                                    {
                                        Kind = KeyValueChangeKind.Delete,
                                        Key = key
                                    };
                                }
                            }
                            else
                            {
                                m_state[key] = value;
                                change = new KeyValueChange
                                {
                                    Kind = KeyValueChangeKind.Set,
                                    Key = key,
                                    Value = value
                                };
                            }
                            result = true;
                        }
                        break;
                    case OpBarrier:
                        result = true;
                        break;
                }

                PublishAppliedIndex(logIndex);
                if (change != null)
                {
                    PublishLocked(change);
                }
                if (pending != null)
                {
                    try
                    {
                        RaftKeyValueSnapshot? snapshot = pending.CaptureSnapshot
                            ? new RaftKeyValueSnapshot(position, new ByteString(EncodeSnapshotLocked(position)))
                            : null;
                        pending.Completion.TrySetResult(new ProposalResult(result, snapshot));
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Only the requested capture failed; the barrier itself was applied.
                        pending.Completion.TrySetException(ex);
                    }
                }
            }
        }

        private void PublishAppliedIndex(ulong? logIndex)
        {
            if (logIndex.HasValue)
            {
                Volatile.Write(ref m_appliedIndex, logIndex.Value);
            }
        }

        private static byte[] HashCommittedCommand(ReadOnlySpan<byte> command)
        {
#if NET6_0_OR_GREATER
            return SHA256.HashData(command);
#else
            return SHA256.HashData(command.ToArray());
#endif
        }

        private void PublishLocked(KeyValueChange change)
        {
            for (int ii = 0; ii < m_watchers.Count; ii++)
            {
                Watcher watcher = m_watchers[ii];
                if (change.Key.StartsWith(watcher.Prefix, StringComparison.Ordinal))
                {
                    watcher.Channel.Writer.TryWrite(change);
                }
            }
        }

        private static int GetCommandLength(
            byte op,
            string key,
            ByteString expected,
            ByteString value,
            string? guardKey,
            ByteString guardExpected,
            out int keyLength,
            out int guardKeyLength)
        {
            if (key.Length > MaxCommandBytes - kCommandHeaderLength)
            {
                throw new ArgumentOutOfRangeException(nameof(key), "The Raft key exceeds the command size limit.");
            }
            keyLength = s_strictUtf8.GetByteCount(key);
            long length = kCommandHeaderLength + keyLength;
            if (op is OpCas or OpGuardedCas)
            {
                length += (long)sizeof(int) + expected.Length;
            }
            if (op is OpSet or OpCas or OpGuardedCas)
            {
                length += (long)sizeof(int) + value.Length;
            }
            guardKeyLength = 0;
            if (guardKey != null)
            {
                if (guardKey.Length > MaxCommandBytes - kCommandHeaderLength)
                {
                    throw new ArgumentOutOfRangeException(nameof(guardKey), "The guard key exceeds the command limit.");
                }
                guardKeyLength = s_strictUtf8.GetByteCount(guardKey);
                length += (2L * sizeof(int)) + guardKeyLength + guardExpected.Length;
            }
            if (length > MaxCommandBytes)
            {
                throw new ArgumentException("The encoded Raft key/value command exceeds its size limit.");
            }
            return (int)length;
        }

        private static byte[] Encode(
            byte op,
            Guid originator,
            long requestId,
            string key,
            int keyLength,
            ByteString expected,
            ByteString value,
            int length,
            string? guardKey,
            int guardKeyLength,
            ByteString guardExpected)
        {
            var buffer = new byte[length];
            int offset = 0;
            buffer[offset++] = s_commandEncodingVersion;
            buffer[offset++] = op;
            originator.ToByteArray().CopyTo(buffer, offset);
            offset += 16;
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset, 8), requestId);
            offset += 8;
            WriteKey(buffer, ref offset, key, keyLength);

            if (op is OpCas or OpGuardedCas)
            {
                WriteValue(buffer, ref offset, expected);
            }
            if (op is OpSet or OpCas or OpGuardedCas)
            {
                WriteValue(buffer, ref offset, value);
            }
            if (guardKey != null)
            {
                WriteKey(buffer, ref offset, guardKey, guardKeyLength);
                WriteValue(buffer, ref offset, guardExpected);
            }

            return buffer;
        }

        private static void WriteKey(byte[] buffer, ref int offset, string key, int length)
        {
            WriteLength(buffer, ref offset, length);
            offset += s_strictUtf8.GetBytes(key, 0, key.Length, buffer, offset);
        }

        private static void WriteLength(byte[] buffer, ref int offset, int length)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), length);
            offset += sizeof(int);
        }

        private static void WriteValue(byte[] buffer, ref int offset, ByteString value)
        {
            WriteLength(buffer, ref offset, value.IsNull ? -1 : value.Length);
            value.Span.CopyTo(buffer.AsSpan(offset));
            offset += value.Length;
        }

        private static ByteString ReadValue(ref ReadOnlySpan<byte> remaining)
        {
            if (remaining.Length >= sizeof(int) && BinaryPrimitives.ReadInt32LittleEndian(remaining) == -1)
            {
                remaining = remaining.Slice(sizeof(int));
                return default;
            }
            return ByteString.From(ReadBlock(ref remaining));
        }

        private readonly record struct ProposalResult(bool Applied, RaftKeyValueSnapshot? Snapshot);

        private sealed record PendingProposal(byte[] Digest, bool CaptureSnapshot)
        {
            public TaskCompletionSource<ProposalResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Tracks a key-prefix watcher together with the channel used to deliver matching key/value changes.
        /// </summary>
        private sealed class Watcher
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Watcher"/> class.
            /// </summary>
            /// <param name="prefix">The key prefix this watcher is interested in.</param>
            public Watcher(string prefix)
            {
                Prefix = prefix;
            }

            /// <summary>
            /// Gets the key prefix this watcher is interested in.
            /// </summary>
            public string Prefix { get; }

            /// <summary>
            /// Gets the channel used to deliver matching key/value changes to the watcher.
            /// </summary>
            public Channel<KeyValueChange> Channel { get; } =
                System.Threading.Channels.Channel.CreateUnbounded<KeyValueChange>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        }

        /// <summary>
        /// Maximum complete application command size, including the key and both CAS operands.
        /// </summary>
        internal const int MaxCommandBytes = 4 * 1024 * 1024;

        /// <summary>
        /// Maximum serialized snapshot size, including any engine header.
        /// </summary>
        internal const int MaxSnapshotBytes = 8 * 1024 * 1024;

        private const byte OpSet = 0;
        private const byte OpDelete = 1;
        private const byte OpCas = 2;
        private const byte OpBarrier = 3;
        private const byte OpGuardedCas = 4;
        private const byte s_commandEncodingVersion = 1;
        private const byte s_snapshotEncodingVersion = 1;
        private const byte kSnapshotNullValue = 0;
        private const byte kSnapshotValue = 1;
        private const byte kLegacyEngineSnapshotFormat = 2;
        private const byte kEngineSnapshotFormat = 3;
        private const int kEngineHeaderLength = 1 + sizeof(ulong);
        private const int kSnapshotHeaderLength = 1 + sizeof(long) + sizeof(int);
        private const int kSnapshotEntryOverhead = 2 * sizeof(int) + sizeof(byte);
        private const int kMaxSnapshotEntries = 65_536;
        private static readonly UTF8Encoding s_strictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        private const int kCommandHeaderLength = 2 + 16 + sizeof(long) + sizeof(int);
        private const int kSetCommandOverhead = kCommandHeaderLength + sizeof(int);

        private readonly IRaftConsensus m_consensus;
        private readonly bool m_processLocal;
        private readonly bool m_ownsConsensus;
        private readonly TimeSpan m_commitTimeout;
        private readonly int m_compactionInterval;
        private readonly Guid m_originator = Guid.NewGuid();
        private readonly Dictionary<string, ByteString> m_state = new(StringComparer.Ordinal);
        private readonly List<Watcher> m_watchers = [];
        private readonly ConcurrentDictionary<long, PendingProposal> m_pending = new();
        private readonly Lock m_lock = new();
        private readonly Lock m_proposalLock = new();
        private readonly SemaphoreSlim m_startGate = new(1, 1);
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_applyLoop;
        private long m_requestId;
        private int m_started;
        private Exception? m_applierFault;
        private readonly ILogger? m_logger;
        private int m_disposed;
        private long m_appliedCommands;
        private long m_skipThroughCommand;
        private long m_lastEngineCommand;
        private ulong m_lastLogIndex;
        private ulong m_appliedIndex;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="RaftSharedKeyValueStore"/>.
    /// </summary>
    internal static partial class RaftSharedKeyValueStoreLog
    {
        /// <summary>
        /// Logs a compaction of the Raft log that failed; the log stays intact and the next interval retries.
        /// </summary>
        [LoggerMessage(EventId = RedundancyEventIds.RaftSharedKeyValueStore + 0, Level = LogLevel.Warning,
            Message = "Could not compact the Raft log at index {Index}; the next compaction interval retries.")]
        public static partial void RaftLogCompactionFailed(this ILogger logger, Exception exception, ulong index);
    }
}
