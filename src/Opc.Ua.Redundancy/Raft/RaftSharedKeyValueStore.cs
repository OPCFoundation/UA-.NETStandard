/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
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
    /// back to the caller (by request id) to return the result.
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
        ISharedKeyValueStore,
        ISharedKeyValueStoreConsistency,
        IRaftSnapshotManager,
        IAsyncDisposable
    {
        /// <inheritdoc/>
        public bool IsLinearizable(string key)
        {
            return key != null;
        }

        /// <inheritdoc/>
        public bool IsProcessLocal(string key)
        {
            return m_processLocal;
        }

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
            m_processLocal = consensus is InProcessRaftConsensus;
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
            var capture = new TaskCompletionSource<RaftKeyValueSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _ = await ProposeAsync(OpBarrier, string.Empty, default, default, capture, ct).ConfigureAwait(false);
            return await capture.Task.ConfigureAwait(false);
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
        private byte[] EncodeSnapshotLocked(long lastIncludedCommand)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, s_strictUtf8, leaveOpen: true))
            {
                writer.Write(s_snapshotEncodingVersion);
                writer.Write(lastIncludedCommand);
                writer.Write(m_state.Count);
                foreach (KeyValuePair<string, ByteString> entry in m_state)
                {
                    byte[] key = s_strictUtf8.GetBytes(entry.Key);
                    writer.Write(key.Length);
                    writer.Write(key);
                    byte[] value = entry.Value.IsNull ? [] : entry.Value.ToArray() ?? [];
                    writer.Write(entry.Value.IsNull ? kSnapshotNullValue : kSnapshotValue);
                    writer.Write(value.Length);
                    writer.Write(value);
                }
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes a snapshot strictly: only what <see cref="EncodeSnapshotLocked"/> can produce is accepted.
        /// </summary>
        /// <exception cref="FormatException">The data is not a valid snapshot.</exception>
        private static (Dictionary<string, ByteString> State, long LastIncludedCommand) DecodeSnapshot(
            ReadOnlySpan<byte> snapshot)
        {
            var state = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            long lastIncludedCommand;
            try
            {
                using var reader = new BinaryReader(new MemoryStream(snapshot.ToArray()), s_strictUtf8);
                if (reader.ReadByte() != s_snapshotEncodingVersion)
                {
                    throw new FormatException("The snapshot has an unknown encoding version.");
                }
                lastIncludedCommand = reader.ReadInt64();
                if (lastIncludedCommand < 1)
                {
                    throw new FormatException("The snapshot holds an invalid log position.");
                }
                int count = reader.ReadInt32();
                if (count < 0)
                {
                    throw new FormatException("The snapshot holds a negative entry count.");
                }
                for (int ii = 0; ii < count; ii++)
                {
                    string key = s_strictUtf8.GetString(ReadBlock(reader));
                    byte kind = reader.ReadByte();
                    if (kind is not (kSnapshotNullValue or kSnapshotValue))
                    {
                        throw new FormatException("The snapshot holds an invalid value marker.");
                    }
                    byte[] value = ReadBlock(reader);
                    if (kind == kSnapshotNullValue && value.Length != 0)
                    {
                        throw new FormatException("The snapshot holds a payload for a null value.");
                    }
                    if (state.ContainsKey(key))
                    {
                        throw new FormatException("The snapshot holds a key twice.");
                    }
                    state.Add(key, kind == kSnapshotNullValue ? default : ByteString.From(value));
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                {
                    throw new FormatException("The snapshot has trailing data.");
                }
            }
            catch (EndOfStreamException e)
            {
                throw new FormatException("The snapshot is truncated.", e);
            }
            catch (DecoderFallbackException e)
            {
                throw new FormatException("The snapshot holds a key that is not valid UTF-8.", e);
            }
            return (state, lastIncludedCommand);
        }

        private static byte[] ReadBlock(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new FormatException("The snapshot holds an invalid length.");
            }
            return reader.ReadBytes(length);
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
                foreach (KeyValuePair<long, TaskCompletionSource<bool>> pending
                    in m_pending)
                {
                    pending.Value.TrySetCanceled(m_cts.Token);
                }
                m_pending.Clear();
                foreach (KeyValuePair<long, TaskCompletionSource<RaftKeyValueSnapshot>> capture
                    in m_snapshotCaptures)
                {
                    capture.Value.TrySetCanceled(m_cts.Token);
                }
                m_snapshotCaptures.Clear();
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

        private ValueTask<bool> ProposeAsync(
            byte op,
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct)
        {
            return ProposeAsync(op, key, expected, value, null, ct);
        }

        private async ValueTask<bool> ProposeAsync(
            byte op,
            string key,
            ByteString expected,
            ByteString value,
            TaskCompletionSource<RaftKeyValueSnapshot>? snapshotCapture,
            CancellationToken ct)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            await EnsureStartedAsync(ct).ConfigureAwait(false);

            long requestId;
            TaskCompletionSource<bool> tcs;
            CancellationToken disposeToken;
            lock (m_proposalLock)
            {
                ThrowIfDisposed();
                ThrowIfApplierStopped();
                requestId = Interlocked.Increment(ref m_requestId);
                tcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                m_pending[requestId] = tcs;
                if (snapshotCapture != null)
                {
                    m_snapshotCaptures[requestId] = snapshotCapture;
                }
                disposeToken = m_cts.Token;
            }

            // Bound the wait for commit: either the caller's token or the commit
            // timeout fails the pending proposal, so a no-leader / leadership-
            // change / lost-quorum window never blocks the caller indefinitely.
            using var commitCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct, disposeToken);
            if (m_commitTimeout != Timeout.InfiniteTimeSpan)
            {
                commitCts.CancelAfter(m_commitTimeout);
            }
            // A snapshot capture is only discarded together with the proposal
            // it belongs to: whoever claims the pending proposal (the applier,
            // this callback, or a failed propose) also settles the capture, so
            // a proposal that succeeds always finds its capture completed.
            using CancellationTokenRegistration reg = commitCts.Token.Register(() =>
            {
                if (m_pending.TryRemove(requestId, out TaskCompletionSource<bool>? pending))
                {
                    m_snapshotCaptures.TryRemove(requestId, out _);
                    if (ct.IsCancellationRequested)
                    {
                        pending.TrySetCanceled(ct);
                    }
                    else if (disposeToken.IsCancellationRequested)
                    {
                        pending.TrySetCanceled(disposeToken);
                    }
                    else
                    {
                        pending.TrySetException(
                            CreateCommitTimeoutException());
                    }
                }
            });

            byte[] command = Encode(op, m_originator, requestId, key, expected, value);
            try
            {
                await m_consensus
                    .ProposeAsync(command, commitCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested &&
                    !disposeToken.IsCancellationRequested &&
                    commitCts.IsCancellationRequested)
            {
                if (m_pending.TryRemove(requestId, out _))
                {
                    m_snapshotCaptures.TryRemove(requestId, out _);
                }
                throw CreateCommitTimeoutException();
            }
            catch
            {
                if (m_pending.TryRemove(requestId, out _))
                {
                    m_snapshotCaptures.TryRemove(requestId, out _);
                }
                throw;
            }

            return await tcs.Task.ConfigureAwait(false);
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
        /// Refuses a proposal or a watch once the applier stopped: nothing would complete it. Called under
        /// <c>m_proposalLock</c>, which also orders it with the applier's fault, so a proposal or watch registered
        /// before the fault is ended by the applier and one after it is refused here.
        /// </summary>
        private void ThrowIfApplierStopped()
        {
            if (m_applierFault != null)
            {
                throw new InvalidOperationException(
                    "The Raft key/value store stopped applying committed commands.",
                    m_applierFault);
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
                    return;
                }

                await foreach (ReadOnlyMemory<byte> command in m_consensus.Committed
                    .ReadAllAsync(ct)
                    .ConfigureAwait(false))
                {
                    ApplyCommittedCommand(command.Span);
                }
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
                    m_applierFault = ex;
                    lock (m_lock)
                    {
                        foreach (Watcher watcher in m_watchers)
                        {
                            watcher.Channel.Writer.TryComplete(ex);
                        }
                        m_watchers.Clear();
                    }
                }
                FailAllPending(ex);
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
                    if (m_appliedCommands < m_skipThroughCommand)
                    {
                        // The state holds commands the log has not delivered again yet (a restored
                        // snapshot replayed from the first command), so it is not the state at this index.
                        return false;
                    }
                    snapshot = EncodeEngineSnapshotLocked();
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
        /// Encodes the state for the consensus engine: a format marker followed by the snapshot encoding of
        /// <see cref="CreateSnapshotAsync"/> at the position of the last applied command. The caller holds
        /// <c>m_lock</c>.
        /// </summary>
        private byte[] EncodeEngineSnapshotLocked()
        {
            byte[] state = EncodeSnapshotLocked(m_appliedCommands);
            byte[] snapshot = new byte[state.Length + 1];
            snapshot[0] = kEngineSnapshotFormat;
            state.CopyTo(snapshot, 1);
            return snapshot;
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
            if (snapshot.IsEmpty || snapshot[0] != kEngineSnapshotFormat)
            {
                throw new FormatException("The Raft engine delivered a snapshot in an unsupported format.");
            }
            (Dictionary<string, ByteString> state, long lastIncludedCommand) = DecodeSnapshot(snapshot.Slice(1));
            lock (m_lock)
            {
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
            // Every delivered command takes a position, including one
            // that is skipped or malformed, so the positions are the
            // same on every replica.
            long position;
            lock (m_lock)
            {
                position = ++m_appliedCommands;
                if (position <= m_skipThroughCommand)
                {
                    // Already held by the restored snapshot: the state is the one at the
                    // restored snapshot's last command, so only that command's index is published.
                    if (position == m_skipThroughCommand)
                    {
                        PublishAppliedIndex(logIndex);
                    }
                    return;
                }
            }
            try
            {
                Apply(command, position, logIndex);
            }
            catch (Exception ex)
            {
                PublishAppliedIndex(logIndex);
                Trace.TraceWarning(
                    "Skipping malformed RaftSharedKeyValueStore command. The applier will continue. {0}",
                    ex);
            }
        }

        private void PublishAppliedIndex(ulong? logIndex)
        {
            if (logIndex.HasValue)
            {
                Volatile.Write(ref m_appliedIndex, logIndex.Value);
            }
        }

        private void FailAllPending(Exception error)
        {
            foreach (KeyValuePair<long, TaskCompletionSource<bool>> pending in m_pending)
            {
                pending.Value.TrySetException(error);
            }
            m_pending.Clear();
            foreach (KeyValuePair<long, TaskCompletionSource<RaftKeyValueSnapshot>> capture in m_snapshotCaptures)
            {
                capture.Value.TrySetException(error);
            }
            m_snapshotCaptures.Clear();
        }

        private void Apply(ReadOnlySpan<byte> command, long position, ulong? logIndex)
        {
            if (command.Length < s_minCommandLength)
            {
                throw new InvalidOperationException("Committed Raft key/value command is truncated.");
            }

            byte version = command[0];
            if (version != s_commandEncodingVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported Raft key/value command encoding version {version}.");
            }

            byte op = command[1];
            var originator = new Guid(command.Slice(2, 16).ToArray());
            long requestId = BinaryPrimitives.ReadInt64LittleEndian(command.Slice(18, 8));
            int offset = 26;

            int keyLength = BinaryPrimitives.ReadInt32LittleEndian(command.Slice(offset, 4));
            offset += 4;
            string key = Encoding.UTF8.GetString(command.Slice(offset, keyLength).ToArray());
            offset += keyLength;

            ByteString expected = default;
            if (op == OpCas)
            {
                expected = ReadValue(command, ref offset);
            }

            ByteString value = default;
            if (op is OpSet or OpCas)
            {
                value = ReadValue(command, ref offset);
            }

            bool result = false;
            KeyValueChange? change = null;
            TaskCompletionSource<RaftKeyValueSnapshot>? snapshotCapture = null;
            RaftKeyValueSnapshot? snapshot = null;
            EncoderFallbackException? snapshotError = null;
            lock (m_lock)
            {
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
                        if (originator == m_originator &&
                            m_snapshotCaptures.TryRemove(requestId, out snapshotCapture))
                        {
                            try
                            {
                                snapshot = new RaftKeyValueSnapshot(
                                    position,
                                    ByteString.From(EncodeSnapshotLocked(position)));
                            }
                            catch (EncoderFallbackException ex)
                            {
                                snapshotError = ex;
                            }
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported Raft key/value operation '{op}'.");
                }

                PublishAppliedIndex(logIndex);
                if (change != null)
                {
                    PublishLocked(change);
                }
            }

            if (snapshotError != null)
            {
                snapshotCapture?.TrySetException(snapshotError);
            }
            else if (snapshot != null)
            {
                snapshotCapture?.TrySetResult(snapshot);
            }
            if (originator == m_originator && m_pending.TryRemove(requestId, out TaskCompletionSource<bool>? tcs))
            {
                tcs.TrySetResult(result);
            }
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

        private static byte[] Encode(
            byte op,
            Guid originator,
            long requestId,
            string key,
            ByteString expected,
            ByteString value)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[]? expectedBytes = op == OpCas && !expected.IsNull ? expected.ToArray() : null;
            byte[]? valueBytes = op is OpSet or OpCas && !value.IsNull ? value.ToArray() : null;

            int length = 1 + 16 + 8 + 4 + keyBytes.Length;
            length++;
            if (op == OpCas)
            {
                length += 4 + (expectedBytes?.Length ?? 0);
            }
            if (op is OpSet or OpCas)
            {
                length += 4 + (valueBytes?.Length ?? 0);
            }

            byte[] buffer = new byte[length];
            int offset = 0;
            buffer[offset++] = s_commandEncodingVersion;
            buffer[offset++] = op;
            originator.ToByteArray().CopyTo(buffer, offset);
            offset += 16;
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset, 8), requestId);
            offset += 8;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), keyBytes.Length);
            offset += 4;
            keyBytes.CopyTo(buffer, offset);
            offset += keyBytes.Length;

            if (op == OpCas)
            {
                WriteValue(buffer, ref offset, expected.IsNull, expectedBytes);
            }
            if (op is OpSet or OpCas)
            {
                WriteValue(buffer, ref offset, value.IsNull, valueBytes);
            }

            return buffer;
        }

        private static void WriteValue(byte[] buffer, ref int offset, bool isNull, byte[]? bytes)
        {
            int len = isNull ? -1 : bytes?.Length ?? 0;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), len);
            offset += 4;
            if (len > 0 && bytes != null)
            {
                bytes.CopyTo(buffer, offset);
                offset += bytes.Length;
            }
        }

        private static ByteString ReadValue(ReadOnlySpan<byte> command, ref int offset)
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(command.Slice(offset, 4));
            offset += 4;
            if (len < 0)
            {
                return default;
            }
            if (len == 0)
            {
                return new ByteString(Array.Empty<byte>());
            }
            byte[] bytes = command.Slice(offset, len).ToArray();
            offset += len;
            return new ByteString(bytes);
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

        private const byte OpSet = 0;
        private const byte OpDelete = 1;
        private const byte OpCas = 2;
        private const byte OpBarrier = 3;
        private const byte s_commandEncodingVersion = 1;
        private const byte s_snapshotEncodingVersion = 1;
        private const byte kSnapshotNullValue = 0;
        private const byte kSnapshotValue = 1;
        private const byte kEngineSnapshotFormat = 2;
        private static readonly UTF8Encoding s_strictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        private const int s_minCommandLength = 30;

        private readonly IRaftConsensus m_consensus;
        private readonly bool m_processLocal;
        private readonly bool m_ownsConsensus;
        private readonly TimeSpan m_commitTimeout;
        private readonly int m_compactionInterval;
        private readonly Guid m_originator = Guid.NewGuid();
        private readonly Dictionary<string, ByteString> m_state = new(StringComparer.Ordinal);
        private readonly List<Watcher> m_watchers = [];
        private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> m_pending = new();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<RaftKeyValueSnapshot>> m_snapshotCaptures =
            new();
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
