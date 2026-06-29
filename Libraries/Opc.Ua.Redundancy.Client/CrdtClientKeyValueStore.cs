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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Redundancy.Client
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a CRDT-backed <see cref="ISharedKeyValueStore"/>: a last-writer-wins map
    /// gossiped between replicas so every replica converges on the same
    /// key/value state without a leader. Reads, writes, and deletes are local;
    /// the merged state propagates by gossip.
    /// </summary>
    /// <remarks>
    /// CRDTs are eventually consistent (AP) and cannot provide a linearizable
    /// compare-and-swap, so <see cref="CompareAndSwapAsync"/> is not supported.
    /// Use a strongly-consistent store for primitives that require exactly-once
    /// semantics (for example the single-use session nonce registry).
    /// </remarks>
    public sealed class CrdtClientKeyValueStore : ISharedKeyValueStore, IAsyncDisposable
    {
        /// <summary>
        /// Creates a CRDT key/value store.
        /// </summary>
        /// <param name="replicaId">This replica's stable CRDT identity.</param>
        /// <param name="transport">The gossip transport (owned by this store).</param>
        /// <param name="timeProvider">The time source for the logical clock.</param>
        /// <param name="readerOptions">Decoding limits for received state.</param>
        public CrdtClientKeyValueStore(
            ReplicaId replicaId,
            ITransport transport,
            TimeProvider timeProvider,
            CrdtReaderOptions readerOptions)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_readerOptions = readerOptions ?? throw new ArgumentNullException(nameof(readerOptions));
            m_clock = new HybridLogicalClock(
                replicaId,
                timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)));
            m_transport.FrameReceived += OnFrameReceived;
        }

        /// <inheritdoc/>
        public async ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);
            lock (m_lock)
            {
                return m_map.TryGetValue(key, out ByteString value) ? (true, value) : (false, default);
            }
        }

        /// <inheritdoc/>
        public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);
            byte[] snapshot;
            lock (m_lock)
            {
                m_map.Set(key, value, m_clock);
                snapshot = SerializeLocked();
            }
            await m_transport.SendAsync(snapshot, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);
            throw new NotSupportedException(
                "CrdtSharedKeyValueStore is eventually consistent and does not support compare-and-swap. " +
                "Use a strongly-consistent store for compare-and-swap primitives (e.g. the single-use nonce registry).");
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);
            byte[] snapshot;
            bool existed;
            lock (m_lock)
            {
                existed = m_map.ContainsKey(key);
                m_map.Remove(key, m_clock);
                snapshot = SerializeLocked();
            }
            await m_transport.SendAsync(snapshot, ct).ConfigureAwait(false);
            return existed;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);
            List<KeyValuePair<string, ByteString>> snapshot = [];
            lock (m_lock)
            {
                foreach (string key in m_map.Keys)
                {
                    if (key.StartsWith(keyPrefix, StringComparison.Ordinal) &&
                        m_map.TryGetValue(key, out ByteString value))
                    {
                        snapshot.Add(new KeyValuePair<string, ByteString>(key, value));
                    }
                }
            }

            foreach (KeyValuePair<string, ByteString> pair in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                yield return pair;
            }
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "CrdtSharedKeyValueStore does not expose a change feed; it is intended for entry replication " +
                "(for example mirrored session entries) where consumers read on demand.");
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }

            m_transport.FrameReceived -= OnFrameReceived;
            await m_transport.DisposeAsync().ConfigureAwait(false);
            m_startGate.Dispose();
        }

        private async ValueTask EnsureStartedAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_started))
            {
                return;
            }

            await m_startGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_started)
                {
                    await m_transport.StartAsync(ct).ConfigureAwait(false);
                    Volatile.Write(ref m_started, true);
                }
            }
            finally
            {
                m_startGate.Release();
            }
        }

        private void OnFrameReceived(ReadOnlyMemory<byte> frame)
        {
            byte[] bytes = frame.ToArray();
            lock (m_lock)
            {
                LWWMap<string, ByteString> remote = LWWMap<string, ByteString>.ReadFrom(
                    bytes, CrdtValues.String, ByteStringCrdtSerializer.Instance, m_readerOptions);
                m_map.Merge(remote);
            }
        }

        private byte[] SerializeLocked()
        {
            return m_map.ToByteArray(CrdtValues.String, ByteStringCrdtSerializer.Instance);
        }

        private readonly ITransport m_transport;
        private readonly CrdtReaderOptions m_readerOptions;
        private readonly HybridLogicalClock m_clock;
        private readonly Lock m_lock = new();
        private readonly LWWMap<string, ByteString> m_map = new();
        private readonly SemaphoreSlim m_startGate = new(1, 1);
        private bool m_started;
        private bool m_disposed;
    }
}
