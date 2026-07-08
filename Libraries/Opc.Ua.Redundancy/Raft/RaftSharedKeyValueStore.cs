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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

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
    /// Reads (<see cref="TryGetAsync"/>/<see cref="ScanAsync"/>) are served from the materialized map. The change-feed
    /// (<see cref="WatchAsync"/>) is derived from the same apply stream, so watchers observe changes in commit order.
    /// </para>
    /// </remarks>
    public sealed class RaftSharedKeyValueStore : ISharedKeyValueStore, IAsyncDisposable
    {
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
            m_ownsConsensus = ownsConsensus;
            m_commitTimeout = commitTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : commitTimeout;
        }

        /// <inheritdoc/>
        public async ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            await EnsureStartedAsync(ct).ConfigureAwait(false);
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
        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            keyPrefix ??= string.Empty;
            await EnsureStartedAsync(ct).ConfigureAwait(false);

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
            lock (m_lock)
            {
                m_watchers.Add(watcher);
            }

            try
            {
                await foreach (KeyValueChange change in watcher.Channel.Reader
                    .ReadAllAsync(ct)
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
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_cts.Cancel();
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

            foreach (KeyValuePair<long, TaskCompletionSource<bool>> pending in m_pending)
            {
                pending.Value.TrySetCanceled();
            }
            m_pending.Clear();

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
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            await EnsureStartedAsync(ct).ConfigureAwait(false);

            long requestId = Interlocked.Increment(ref m_requestId);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_pending[requestId] = tcs;

            // Bound the wait for commit: either the caller's token or the commit
            // timeout fails the pending proposal, so a no-leader / leadership-
            // change / lost-quorum window never blocks the caller indefinitely.
            using var commitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (m_commitTimeout != Timeout.InfiniteTimeSpan)
            {
                commitCts.CancelAfter(m_commitTimeout);
            }
            using CancellationTokenRegistration reg = commitCts.Token.Register(() =>
            {
                if (m_pending.TryRemove(requestId, out TaskCompletionSource<bool>? pending))
                {
                    if (ct.IsCancellationRequested)
                    {
                        pending.TrySetCanceled(ct);
                    }
                    else
                    {
                        pending.TrySetException(new TimeoutException(
                            "The Raft proposal did not commit within the commit timeout (no leader, leadership " +
                            "change, or lost quorum). Retry the operation."));
                    }
                }
            });

            byte[] command = Encode(op, m_originator, requestId, key, expected, value);
            try
            {
                await m_consensus.ProposeAsync(command, ct).ConfigureAwait(false);
            }
            catch
            {
                m_pending.TryRemove(requestId, out _);
                throw;
            }

            return await tcs.Task.ConfigureAwait(false);
        }

        private async ValueTask EnsureStartedAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_started) == 1)
            {
                return;
            }

            await m_startGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_started == 0)
                {
                    await m_consensus.StartAsync(ct).ConfigureAwait(false);
                    m_applyLoop = Task.Run(() => ApplyLoopAsync(m_cts.Token), m_cts.Token);
                    Volatile.Write(ref m_started, 1);
                }
            }
            finally
            {
                m_startGate.Release();
            }
        }

        private async Task ApplyLoopAsync(CancellationToken ct)
        {
            try
            {
                await foreach (ReadOnlyMemory<byte> command in m_consensus.Committed
                    .ReadAllAsync(ct)
                    .ConfigureAwait(false))
                {
                    try
                    {
                        Apply(command.Span);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning(
                            "Skipping malformed RaftSharedKeyValueStore command. The applier will continue. {0}",
                            ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                // The applier is the sole completer of pending proposals; if it
                // ever dies unexpectedly, fail every pending proposal so callers
                // do not hang.
                FailAllPending(ex);
            }
        }

        private void FailAllPending(Exception error)
        {
            foreach (KeyValuePair<long, TaskCompletionSource<bool>> pending in m_pending)
            {
                pending.Value.TrySetException(error);
            }
            m_pending.Clear();
        }

        private void Apply(ReadOnlySpan<byte> command)
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
            if (op != OpDelete)
            {
                value = ReadValue(command, ref offset);
            }

            bool result = false;
            KeyValueChange? change = null;
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
                            m_state[key] = value;
                            change = new KeyValueChange { Kind = KeyValueChangeKind.Set, Key = key, Value = value };
                            result = true;
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported Raft key/value operation '{op}'.");
                }

                if (change != null)
                {
                    PublishLocked(change);
                }
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
            byte[]? valueBytes = op != OpDelete && !value.IsNull ? value.ToArray() : null;

            int length = 1 + 16 + 8 + 4 + keyBytes.Length;
            length++;
            if (op == OpCas)
            {
                length += 4 + (expectedBytes?.Length ?? 0);
            }
            if (op != OpDelete)
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
            if (op != OpDelete)
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

        private sealed class Watcher
        {
            public Watcher(string prefix)
            {
                Prefix = prefix;
            }

            public string Prefix { get; }

            public Channel<KeyValueChange> Channel { get; } =
                System.Threading.Channels.Channel.CreateUnbounded<KeyValueChange>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        }

        private const byte OpSet = 0;
        private const byte OpDelete = 1;
        private const byte OpCas = 2;
        private const byte s_commandEncodingVersion = 1;
        private const int s_minCommandLength = 30;

        private readonly IRaftConsensus m_consensus;
        private readonly bool m_ownsConsensus;
        private readonly TimeSpan m_commitTimeout;
        private readonly Guid m_originator = Guid.NewGuid();
        private readonly Dictionary<string, ByteString> m_state = new(StringComparer.Ordinal);
        private readonly List<Watcher> m_watchers = [];
        private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> m_pending = new();
        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_startGate = new(1, 1);
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_applyLoop;
        private long m_requestId;
        private int m_started;
        private int m_disposed;
    }
}
