// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Opc.Ua.Redundancy
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
    public sealed class ReplicatedSharedKeyValueStore :
        ISharedKeyValueStore,
        ISharedKeyValueStoreConsistency,
        IAsyncDisposable
    {
        /// <inheritdoc/>
        public bool IsLinearizable(string key)
        {
            return false;
        }

        /// <inheritdoc/>
        public bool IsProcessLocal(string key)
        {
            return false;
        }

        /// <summary>
        /// Creates a CRDT key/value store.
        /// </summary>
        /// <param name="replicaId">This replica's stable CRDT identity.</param>
        /// <param name="transport">The gossip transport (owned by this store).</param>
        /// <param name="timeProvider">The time source for the logical clock.</param>
        /// <param name="readerOptions">Decoding limits for received state.</param>
        public ReplicatedSharedKeyValueStore(
            ReplicaId replicaId,
            ITransport transport,
            TimeProvider timeProvider,
            CrdtReaderOptions readerOptions)
            : this(replicaId, transport, timeProvider, readerOptions, NullLogger.Instance)
        {
        }

        /// <summary>
        /// Creates a CRDT key/value store with a diagnostic logger.
        /// </summary>
        /// <param name="replicaId">This replica's stable CRDT identity.</param>
        /// <param name="transport">The gossip transport (owned by this store).</param>
        /// <param name="timeProvider">The time source for the logical clock.</param>
        /// <param name="readerOptions">Decoding limits for received state.</param>
        /// <param name="logger">The logger for rejected state.</param>
        public ReplicatedSharedKeyValueStore(
            ReplicaId replicaId,
            ITransport transport,
            TimeProvider timeProvider,
            CrdtReaderOptions readerOptions,
            ILogger logger)
        {
            m_transport = new FramingGossipTransport(
                transport ?? throw new ArgumentNullException(nameof(transport)));
            m_readerOptions = readerOptions ?? throw new ArgumentNullException(nameof(readerOptions));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_clock = new HybridLogicalClock(replicaId, m_timeProvider);
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
                ApplyLocalOperation(new LWWMapOperation<string, ByteString>(key, value, m_clock.Now()));
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
                "ReplicatedSharedKeyValueStore is eventually consistent and does not support compare-and-swap. " +
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
                ApplyLocalOperation(new LWWMapOperation<string, ByteString>(key, m_clock.Now()));
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
                "ReplicatedSharedKeyValueStore does not expose a change feed; it is intended for entry replication " +
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
            try
            {
                var remote = LWWMap<string, ByteString>.ReadFrom(
                    frame.Span, CrdtValues.String, ByteStringCrdtSerializer.Instance, m_readerOptions);
                lock (m_lock)
                {
                    if (!m_disposed)
                    {
                        ReplicatedCrdtClock.Observe(m_clock, m_timeProvider, remote);
                        m_map.Merge(remote);
                    }
                }
            }
            catch (FormatException)
            {
                m_logger.RejectedInvalidReplicatedState();
            }
            catch (DecoderFallbackException)
            {
                m_logger.RejectedInvalidReplicatedState();
            }
        }

        private void ApplyLocalOperation(in LWWMapOperation<string, ByteString> operation)
        {
            if (!m_map.Apply(operation))
            {
                throw new InvalidOperationException("The local replicated-store mutation did not win timestamp ordering.");
            }
        }

        private byte[] SerializeLocked()
        {
            return m_map.ToByteArray(CrdtValues.String, ByteStringCrdtSerializer.Instance);
        }

        private readonly FramingGossipTransport m_transport;
        private readonly CrdtReaderOptions m_readerOptions;
        private readonly HybridLogicalClock m_clock;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly Lock m_lock = new();
        private readonly LWWMap<string, ByteString> m_map = new();
        private readonly SemaphoreSlim m_startGate = new(1, 1);
        private bool m_started;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated messages for replicated key/value stores.
    /// </summary>
    internal static partial class ReplicatedSharedKeyValueStoreLog
    {
        [LoggerMessage(
            EventId = RedundancyEventIds.ReplicatedSharedKeyValueStore,
            Level = LogLevel.Warning,
            Message = "Dropped a replicated key/value observation with an invalid format or timestamp.")]
        public static partial void RejectedInvalidReplicatedState(this ILogger logger);
    }
}
