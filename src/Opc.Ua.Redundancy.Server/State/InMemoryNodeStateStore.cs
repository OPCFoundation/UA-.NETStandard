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
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: default <see cref="INodeStateStore"/> layered on an
    /// <see cref="ISharedKeyValueStore"/>. Node payloads and encoded values
    /// are stored under distinct key prefixes so topology and value changes
    /// can be routed independently on the change-feed.
    /// </summary>
    public sealed class InMemoryNodeStateStore :
        INodeStateStore,
        INodeStateSnapshotStore,
        INodeStatePartitionStore,
        INodeStateSubscriptionPreparer,
        INodeStateStoreStateProbe,
        ISequencedNodeStateStore,
        INodeStateStoreReadConsistency,
        IDisposable
    {
        /// <summary>
        /// Creates a node state store over the supplied key/value backend.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="context">
        /// The message context used to binary-encode and decode
        /// <see cref="DataValue"/> payloads.
        /// </param>
        /// <param name="protector">
        /// Optional record protector applied to every stored payload
        /// (authenticated encryption); defaults to a no-op pass-through.
        /// Configure an <see cref="AesCbcHmacRecordProtector"/> in production
        /// so the shared store can be treated as untrusted.
        /// </param>
        /// <param name="pollInterval">
        /// The scan-poll interval used by <see cref="SubscribeChangesAsync"/> when the backing store has no
        /// change-feed (for example a CRDT gossip store). Defaults to 2 seconds.
        /// </param>
        public InMemoryNodeStateStore(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector? protector = null,
            TimeSpan pollInterval = default)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = protector ?? NullRecordProtector.Instance;
            m_pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : pollInterval;
        }

        /// <summary>
        /// The highest write sequence this store has assigned or observed. A
        /// promoted writer continues from this high-water mark so sequences
        /// never move backward across a failover.
        /// </summary>
        /// <remarks>
        /// This allocation high-water mark includes unfinished publications. Snapshot
        /// publication validates shared completion state instead of using it as a trim horizon.
        /// </remarks>
        public ulong CurrentSequence => unchecked((ulong)Interlocked.Read(ref m_sequence));

        /// <inheritdoc/>
        bool INodeStateStoreReadConsistency.HasAuthoritativeReads =>
            IsLinearizableKey(NodePrefix) &&
            IsLinearizableKey(ValuePrefix) &&
            IsLinearizableKey(DeltaPrefix) &&
            IsLinearizableKey(PartitionPrefix);

        /// <inheritdoc/>
        bool INodeStateStoreReadConsistency.SupportsSnapshots =>
            ((INodeStateStoreReadConsistency)this).HasAuthoritativeReads &&
            IsLinearizableKey(SnapshotPrefix) &&
            IsLinearizableKey(ManifestKey);

        /// <inheritdoc/>
        async ValueTask<bool> INodeStatePartitionStore.IsPartitionInitializedAsync(
            string partitionId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(partitionId))
            {
                throw new ArgumentException("A partition identifier is required.", nameof(partitionId));
            }

            (bool found, ByteString stored) = await m_store
                .TryGetAsync(PartitionPrefix + Uri.EscapeDataString(partitionId), ct)
                .ConfigureAwait(false);
            if (!found)
            {
                return false;
            }
            if (!m_protector.TryUnprotect(stored, out ByteString marker) ||
                marker.Length != 1 ||
                marker.Span[0] != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"The distributed address-space partition marker '{partitionId}' is invalid.");
            }
            return true;
        }

        /// <inheritdoc/>
        ValueTask INodeStatePartitionStore.MarkPartitionInitializedAsync(
            string partitionId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(partitionId))
            {
                throw new ArgumentException("A partition identifier is required.", nameof(partitionId));
            }

            ct.ThrowIfCancellationRequested();
            ValidateCoordinator(m_store);
            return m_store.SetAsync(
                PartitionPrefix + Uri.EscapeDataString(partitionId),
                m_protector.Protect(new ByteString(new byte[] { 1 })),
                ct);
        }

        void INodeStateSubscriptionPreparer.PrepareSubscription()
        {
            Interlocked.Increment(ref m_emitInitialStateSubscriptions);
        }

        async ValueTask<bool> INodeStateStoreStateProbe.HasStoredStateAsync(
            Func<NodeId, bool> ownsNode,
            CancellationToken ct)
        {
            if (ownsNode == null)
            {
                throw new ArgumentNullException(nameof(ownsNode));
            }
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(NodePrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryParseNodeId(entry.Key, NodePrefix, out NodeId nodeId) &&
                    ownsNode(nodeId))
                {
                    if (!TryReadRecord(entry.Value, out _, out _))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            $"The distributed node record for {nodeId} is invalid.");
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Raises the sequence high-water mark to at least <paramref name="sequence"/>.
        /// Called while hydrating (snapshot / delta log) so a later promotion
        /// keeps assigning strictly increasing sequences.
        /// </summary>
        /// <param name="sequence">The observed sequence.</param>
        public void ObserveSequence(ulong sequence)
        {
            long observed = unchecked((long)sequence);
            long current = Interlocked.Read(ref m_sequence);
            while (unchecked((ulong)current) < sequence)
            {
                long prior = Interlocked.CompareExchange(ref m_sequence, observed, current);
                if (prior == current)
                {
                    return;
                }
                current = prior;
            }
        }

        /// <summary>
        /// Releases the snapshot-serialization semaphore. The backing key/value
        /// store is shared and not owned by this instance.
        /// </summary>
        public void Dispose()
        {
            m_snapshotLock.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask UpsertNodeAsync(IStoredNode node, CancellationToken ct = default)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (node.NodeId.IsNull || node.Payload.IsNull || node.Payload.IsEmpty)
            {
                throw new ArgumentException(
                    "A stored node requires an identifier and a nonempty payload.",
                    nameof(node));
            }
            ulong sequence = await NextSequenceAsync(ct).ConfigureAwait(false);
            await WritePrimaryRecordAsync(NodePrefix + node.NodeId, sequence, node.Payload, ct)
                .ConfigureAwait(false);
            await AppendDeltaAsync(sequence, NodeStateChangeKind.Upsert, node.NodeId, node.Payload, ct)
                .ConfigureAwait(false);
            await CompleteSequenceAsync(sequence, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A node identifier is required.", nameof(nodeId));
            }
            ulong sequence = await NextSequenceAsync(ct).ConfigureAwait(false);
            bool removed = await WritePrimaryRecordAsync(NodePrefix + nodeId, sequence, ByteString.Empty, ct)
                .ConfigureAwait(false);
            await AppendDeltaAsync(sequence, NodeStateChangeKind.Delete, nodeId, ByteString.Empty, ct)
                .ConfigureAwait(false);
            await CompleteSequenceAsync(sequence, ct).ConfigureAwait(false);
            return removed;
        }

        /// <inheritdoc/>
        public async ValueTask<IStoredNode?> TryGetNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(NodePrefix + nodeId, ct)
                .ConfigureAwait(false);
            if (found &&
                TryReadRecord(value, out _, out ByteString payload) &&
                !payload.IsEmpty)
            {
                return new StoredNode(nodeId, payload);
            }
            return null;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<IStoredNode> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach ((IStoredNode node, _) in
                ((ISequencedNodeStateStore)this)
                    .EnumerateNodesWithSequenceAsync(ct)
                    .ConfigureAwait(false))
            {
                yield return node;
            }
        }

        async IAsyncEnumerable<(IStoredNode Node, ulong Sequence)>
            ISequencedNodeStateStore.EnumerateNodesWithSequenceAsync(
                [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach ((IStoredNode node, ulong sequence) in EnumerateRetainedNodesAsync(ct).ConfigureAwait(false))
            {
                if (!node.Payload.IsEmpty)
                {
                    yield return (node, sequence);
                }
            }
        }

        /// <summary>
        /// Reads live topology and retained tombstones so identity allocation can reserve both before authoring.
        /// </summary>
        internal async IAsyncEnumerable<(IStoredNode Node, ulong Sequence)> EnumerateRetainedNodesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(NodePrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryParseNodeId(entry.Key, NodePrefix, out NodeId id))
                {
                    (ulong sequence, ByteString payload) = ReadRecord(entry.Key, entry.Value);
                    yield return (new StoredNode(id, payload), sequence);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask WriteValueAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A node identifier is required.", nameof(nodeId));
            }
            // Encode synchronously (in-parameters are not allowed in async
            // methods) and hand off to the async record + delta-log writer.
            ByteString payload = EncodeValue(in value);
            return WriteValueRecordAsync(nodeId, payload, ct);
        }

        private async ValueTask WriteValueRecordAsync(
            NodeId nodeId,
            ByteString payload,
            CancellationToken ct)
        {
            ulong sequence = await NextSequenceAsync(ct).ConfigureAwait(false);
            await WritePrimaryRecordAsync(ValuePrefix + nodeId, sequence, payload, ct)
                .ConfigureAwait(false);
            await AppendDeltaAsync(sequence, NodeStateChangeKind.Value, nodeId, payload, ct)
                .ConfigureAwait(false);
            await CompleteSequenceAsync(sequence, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<(bool Found, DataValue Value)> TryReadValueAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(ValuePrefix + nodeId, ct)
                .ConfigureAwait(false);
            if (found && TryReadRecord(value, out _, out ByteString payload))
            {
                return (true, DecodeValue(payload));
            }
            return (false, DataValue.Null);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<(NodeId NodeId, DataValue Value)> EnumerateValuesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach ((NodeId nodeId, DataValue value, _) in
                ((ISequencedNodeStateStore)this)
                    .EnumerateValuesWithSequenceAsync(ct)
                    .ConfigureAwait(false))
            {
                yield return (nodeId, value);
            }
        }

        async IAsyncEnumerable<(NodeId NodeId, DataValue Value, ulong Sequence)>
            ISequencedNodeStateStore.EnumerateValuesWithSequenceAsync(
                [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(ValuePrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryParseNodeId(entry.Key, ValuePrefix, out NodeId id))
                {
                    (ulong sequence, ByteString payload) = ReadRecord(entry.Key, entry.Value);
                    yield return (id, DecodeValueStrict(payload), sequence);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask WriteSnapshotAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ValidateCoordinator(m_store);
            if (!((INodeStateStoreReadConsistency)this).SupportsSnapshots)
            {
                throw new InvalidOperationException(
                    "Distributed address-space snapshots require linearizable node, value, delta, and snapshot " +
                    "keyspaces. An eventually consistent bulk scan cannot prove completeness; its delta log " +
                    "must be retained.");
            }

            var generation = Guid.NewGuid();
            BinaryEncoder? encoder = null;
            bool published = false;
            bool publicationUncertain = false;
            await m_snapshotLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                (ByteString sequenceRecord, ulong sequence, List<ulong> pending) =
                    await ReadSequenceAsync(ct).ConfigureAwait(false);
                if (pending.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"Distributed address-space publication {pending[0]} is unfinished. " +
                        "A snapshot cannot advance over an unfinished or failed publication.");
                }

                int chunkIndex = 0;

                async ValueTask FlushAsync()
                {
                    if (encoder == null)
                    {
                        return;
                    }
                    byte[]? plaintext = encoder.CloseAndReturnBuffer();
                    encoder.Dispose();
                    encoder = null;
                    if (plaintext == null || plaintext.Length == 0)
                    {
                        return;
                    }
                    if (chunkIndex >= MaxSnapshotChunks)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "The distributed address-space snapshot has too many chunks.");
                    }
                    await m_store
                        .SetAsync(
                            SnapshotChunkKey(generation, chunkIndex),
                            m_protector.Protect(new ByteString(plaintext)),
                            ct)
                        .ConfigureAwait(false);
                    chunkIndex++;
                }

                void Append(byte kind, ulong entrySequence, NodeId nodeId, ByteString payload)
                {
                    if (entrySequence > sequence)
                    {
                        throw new InvalidOperationException(
                            "The distributed address space changed while the snapshot was being built.");
                    }
                    encoder ??= new BinaryEncoder(m_context);
                    encoder.WriteByte(null, kind);
                    encoder.WriteUInt64(null, entrySequence);
                    encoder.WriteNodeId(null, nodeId);
                    encoder.WriteByteString(null, payload);
                }

                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(NodePrefix, ct)
                    .ConfigureAwait(false))
                {
                    if (TryParseNodeId(entry.Key, NodePrefix, out NodeId id))
                    {
                        (ulong seq, ByteString payload) = ReadRecord(entry.Key, entry.Value);
                        if (!payload.IsEmpty)
                        {
                            Append((byte)NodeStateChangeKind.Upsert, seq, id, payload);
                            if (encoder!.Position >= MaxChunkBytes)
                            {
                                await FlushAsync().ConfigureAwait(false);
                            }
                        }
                    }
                }

                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(ValuePrefix, ct)
                    .ConfigureAwait(false))
                {
                    if (TryParseNodeId(entry.Key, ValuePrefix, out NodeId id))
                    {
                        (ulong seq, ByteString payload) = ReadRecord(entry.Key, entry.Value);
                        _ = DecodeValueStrict(payload);
                        Append((byte)NodeStateChangeKind.Value, seq, id, payload);
                        if (encoder!.Position >= MaxChunkBytes)
                        {
                            await FlushAsync().ConfigureAwait(false);
                        }
                    }
                }

                await FlushAsync().ConfigureAwait(false);

                // Read the manifest being replaced so its predecessor generation
                // can be garbage-collected once the new manifest is published.
                Guid predecessor = Guid.Empty;
                Guid? generationToCollect = null;
                (bool foundManifest, ByteString existingManifest) = await m_store
                    .TryGetAsync(ManifestKey, ct)
                    .ConfigureAwait(false);
                if (foundManifest && TryDecodeManifest(existingManifest, out SnapshotManifest previous))
                {
                    predecessor = previous.Generation;
                    generationToCollect = previous.PreviousGeneration;
                }

                // The shared reservation record changes before any primary write.
                // Equality proves the linearizable scans ran without a concurrent
                // publication; writes starting after this check have a higher horizon.
                (ByteString currentSequenceRecord, _, _) =
                    await ReadSequenceAsync(ct).ConfigureAwait(false);
                if (!sequenceRecord.Equals(currentSequenceRecord))
                {
                    throw new InvalidOperationException(
                        "The distributed address space changed while the snapshot was being built.");
                }

                ByteString manifest = m_protector.Protect(
                    EncodeManifest(generation, chunkIndex, sequence, predecessor));
                ct.ThrowIfCancellationRequested();
                publicationUncertain = true;
                bool replaced = await m_store
                    .CompareAndSwapAsync(
                        ManifestKey,
                        foundManifest ? existingManifest : default,
                        manifest,
                        ct)
                    .ConfigureAwait(false);
                publicationUncertain = false;
                if (!replaced)
                {
                    throw new InvalidOperationException(
                        "Another distributed address-space snapshot was published concurrently.");
                }
                published = true;

                await TrimDeltaLogAsync(sequence, ct).ConfigureAwait(false);

                if (generationToCollect is Guid collect && collect != Guid.Empty)
                {
                    await DeleteGenerationAsync(collect, ct).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                // A failed transport/cancellation can leave an indeterminate CAS
                // that already published the manifest. Never delete its generation.
                if (!published && !publicationUncertain)
                {
                    try
                    {
                        await DeleteGenerationAsync(generation, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException(
                            "Snapshot publication failed, and its unpublished chunks could not be removed.",
                            exception,
                            cleanupException);
                    }
                }
                throw;
            }
            finally
            {
                encoder?.Dispose();
                m_snapshotLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<NodeStateSnapshot?> TryReadSnapshotAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!((INodeStateStoreReadConsistency)this).SupportsSnapshots)
            {
                return null;
            }
            (bool found, ByteString manifest) = await m_store
                .TryGetAsync(ManifestKey, ct)
                .ConfigureAwait(false);
            if (!found || !TryDecodeManifest(manifest, out SnapshotManifest decoded))
            {
                return null;
            }
            return new NodeStateSnapshot(decoded.Sequence, ReadSnapshotEntriesAsync(decoded, ct));
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<NodeStateChange> ReadDeltaLogAsync(
            ulong fromSequenceExclusive,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var pending = new List<(ulong Sequence, ByteString Frame)>();
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(DeltaPrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryParseSequence(entry.Key, out ulong seq) && seq > fromSequenceExclusive)
                {
                    pending.Add((seq, entry.Value));
                }
            }
            pending.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
            foreach ((ulong seq, ByteString frame) in pending)
            {
                ct.ThrowIfCancellationRequested();
                NodeStateChange change = DecodeDelta(seq, frame);
                ObserveSequence(seq);
                yield return change;
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<NodeStateChange> SubscribeChangesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            bool emitInitialState = ConsumeInitialStateRequest();
            IAsyncEnumerable<KeyValueChange>? feed;
            try
            {
                feed = m_store.WatchAsync(string.Empty, ct);
            }
            catch (NotSupportedException)
            {
                // The backing store has no change-feed (for example a CRDT
                // gossip store, or the bulk side of a hybrid store). Fall back
                // to periodic scan-polling so a standby replica still tracks
                // topology/value changes instead of silently stopping.
                feed = null;
            }

            if (feed != null)
            {
                await foreach (KeyValueChange change in feed.ConfigureAwait(false))
                {
                    NodeStateChange? mapped = Map(change);
                    if (mapped != null)
                    {
                        yield return mapped;
                    }
                }
                yield break;
            }

            await foreach (NodeStateChange change in PollChangesAsync(
                emitInitialState,
                ct).ConfigureAwait(false))
            {
                yield return change;
            }
        }

        /// <summary>
        /// Rejects writer coordination that is not declared linearizable, or that
        /// is process-local while the address-space payload is replicated.
        /// </summary>
        /// <param name="store">The backing key/value store.</param>
        /// <param name="key">The sequence or configured lease key to validate.</param>
        /// <exception cref="InvalidOperationException">
        /// The key lacks linearizable coordination shared by the payload's writer replicas.
        /// </exception>
        internal static void ValidateCoordinator(ISharedKeyValueStore store, string key = SequenceKey)
        {
            if (store is not ISharedKeyValueStoreConsistency consistency ||
                !consistency.IsLinearizable(key))
            {
                throw new InvalidOperationException(
                    $"Distributed address-space writers require a declared linearizable coordinator for '{key}'. " +
                    "Compose CRDT payload storage with a shared Raft store through HybridSharedKeyValueStore; " +
                    "a bare CRDT store does not support writer coordination.");
            }
            if (consistency.IsProcessLocal(key) &&
                (!consistency.IsProcessLocal(NodePrefix) ||
                    !consistency.IsProcessLocal(ValuePrefix) ||
                    !consistency.IsProcessLocal(DeltaPrefix)))
            {
                throw new InvalidOperationException(
                    $"The coordinator for '{key}' is process-local, but the address-space payload is replicated. " +
                    "All writer replicas must share the same linearizable coordinator. " +
                    "Process-local stores are supported only for single-process address spaces.");
            }
        }

        private bool IsLinearizableKey(string key)
        {
            return m_store is ISharedKeyValueStoreConsistency consistency &&
                consistency.IsLinearizable(key);
        }

        private async IAsyncEnumerable<NodeStateChange> PollChangesAsync(
            bool emitInitialState,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var nodes = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            var values = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            var deltas = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            var sequences = new Dictionary<(NodeId NodeId, bool Value), ulong>();
            bool baseline = emitInitialState;

            while (!ct.IsCancellationRequested)
            {
                Dictionary<string, ByteString> currentNodes =
                    await SnapshotPrefixAsync(NodePrefix, ct).ConfigureAwait(false);
                Dictionary<string, ByteString> currentValues =
                    await SnapshotPrefixAsync(ValuePrefix, ct).ConfigureAwait(false);
                Dictionary<string, ByteString> currentDeltas =
                    await SnapshotPrefixAsync(DeltaPrefix, ct).ConfigureAwait(false);

                var changes = new List<NodeStateChange>();
                changes.AddRange(DiffSet(nodes, currentNodes));
                changes.AddRange(DiffDeletes(nodes, currentNodes));
                changes.AddRange(DiffSet(values, currentValues));
                foreach (KeyValuePair<string, ByteString> entry in currentDeltas)
                {
                    if ((!deltas.TryGetValue(entry.Key, out ByteString previous) || !previous.Equals(entry.Value)) &&
                        TryParseSequence(entry.Key, out ulong sequence))
                    {
                        changes.Add(DecodeDelta(sequence, entry.Value));
                    }
                }
                changes.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
                foreach (NodeStateChange change in changes)
                {
                    (NodeId NodeId, bool) key = (change.NodeId, change.Kind == NodeStateChangeKind.Value);
                    if (change.Sequence != 0 &&
                        sequences.TryGetValue(key, out ulong previous) &&
                        previous >= change.Sequence)
                    {
                        continue;
                    }
                    sequences[key] = change.Sequence;
                    if (baseline)
                    {
                        yield return change;
                    }
                }

                nodes = currentNodes;
                values = currentValues;
                deltas = currentDeltas;
                baseline = true;
                await Task.Delay(m_pollInterval, ct).ConfigureAwait(false);
            }
        }

        private bool ConsumeInitialStateRequest()
        {
            int count = Volatile.Read(ref m_emitInitialStateSubscriptions);
            while (count > 0)
            {
                int previous = Interlocked.CompareExchange(
                    ref m_emitInitialStateSubscriptions,
                    count - 1,
                    count);
                if (previous == count)
                {
                    return true;
                }
                count = previous;
            }
            return false;
        }

        private IEnumerable<NodeStateChange> DiffSet(
            Dictionary<string, ByteString> previous,
            Dictionary<string, ByteString> current)
        {
            foreach (KeyValuePair<string, ByteString> entry in current)
            {
                if (!previous.TryGetValue(entry.Key, out ByteString prior) || !prior.Equals(entry.Value))
                {
                    NodeStateChange? mapped = Map(new KeyValueChange
                    {
                        Kind = KeyValueChangeKind.Set,
                        Key = entry.Key,
                        Value = entry.Value
                    });
                    if (mapped != null)
                    {
                        yield return mapped;
                    }
                }
            }
        }

        private IEnumerable<NodeStateChange> DiffDeletes(
            Dictionary<string, ByteString> previous,
            Dictionary<string, ByteString> current)
        {
            foreach (string key in previous.Keys)
            {
                if (!current.ContainsKey(key))
                {
                    NodeStateChange? mapped = Map(new KeyValueChange
                    {
                        Kind = KeyValueChangeKind.Delete,
                        Key = key
                    });
                    if (mapped != null)
                    {
                        yield return mapped;
                    }
                }
            }
        }

        private async Task<Dictionary<string, ByteString>> SnapshotPrefixAsync(string prefix, CancellationToken ct)
        {
            var snapshot = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(prefix, ct)
                .ConfigureAwait(false))
            {
                // Validate the baseline too: an unchanged corrupt row must not
                // disappear merely because it was present before subscription.
                _ = Map(new KeyValueChange
                {
                    Kind = KeyValueChangeKind.Set,
                    Key = entry.Key,
                    Value = entry.Value
                });
                snapshot[entry.Key] = entry.Value;
            }
            return snapshot;
        }

        private NodeStateChange? Map(KeyValueChange change)
        {
            if (change.Key.StartsWith(NodePrefix, StringComparison.Ordinal))
            {
                if (!TryParseNodeId(change.Key, NodePrefix, out NodeId id))
                {
                    return null;
                }
                if (change.Kind == KeyValueChangeKind.Delete)
                {
                    return null;
                }
                (ulong sequence, ByteString payload) = ReadRecord(change.Key, change.Value);
                if (payload.IsEmpty)
                {
                    return new NodeStateChange
                    {
                        Kind = NodeStateChangeKind.Delete,
                        NodeId = id,
                        Sequence = sequence
                    };
                }
                return new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Upsert,
                    NodeId = id,
                    Node = new StoredNode(id, payload),
                    Sequence = sequence
                };
            }

            if (change.Key.StartsWith(ValuePrefix, StringComparison.Ordinal))
            {
                if (change.Kind != KeyValueChangeKind.Set ||
                    !TryParseNodeId(change.Key, ValuePrefix, out NodeId id))
                {
                    return null;
                }
                (ulong sequence, ByteString payload) = ReadRecord(change.Key, change.Value);
                return new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Value,
                    NodeId = id,
                    Value = DecodeValueStrict(payload),
                    Sequence = sequence
                };
            }

            if (change.Kind == KeyValueChangeKind.Set &&
                change.Key.StartsWith(DeltaPrefix, StringComparison.Ordinal) &&
                TryParseSequence(change.Key, out ulong deltaSequence))
            {
                _ = DecodeDelta(deltaSequence, change.Value);
            }
            return null;
        }

        private ByteString EncodeValue(in DataValue value)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteDataValue(null, in value);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : new ByteString(buffer);
        }

        private DataValue DecodeValue(ByteString bytes)
        {
            if (bytes.IsNull || bytes.IsEmpty)
            {
                return DataValue.Null;
            }
            using var decoder = new BinaryDecoder(bytes.ToArray(), m_context);
            return decoder.ReadDataValue(null);
        }

        private DataValue DecodeValueStrict(ByteString bytes)
        {
            if (bytes.IsNull || bytes.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "The distributed address-space value payload is empty.");
            }
            using var decoder = new BinaryDecoder(bytes.ToArray(), m_context);
            DataValue value = decoder.ReadDataValue(null);
            if (decoder.Position != bytes.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "The distributed address-space value payload contains trailing data.");
            }
            return value;
        }

        private ValueTask AppendDeltaAsync(
            ulong sequence,
            NodeStateChangeKind kind,
            NodeId nodeId,
            ByteString payload,
            CancellationToken ct)
        {
            return m_store.SetAsync(
                DeltaPrefix + FormatSequence(sequence),
                m_protector.Protect(EncodeDelta(kind, nodeId, payload)),
                ct);
        }

        private ByteString EncodeDelta(NodeStateChangeKind kind, NodeId nodeId, ByteString payload)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteByte(null, (byte)kind);
            encoder.WriteNodeId(null, nodeId);
            encoder.WriteByteString(null, payload);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : new ByteString(buffer);
        }

        private NodeStateChange DecodeDelta(ulong sequence, ByteString frame)
        {
            if (!m_protector.TryUnprotect(frame, out ByteString plaintext) || plaintext.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"The distributed address-space delta {sequence} failed record authentication.");
            }
            using var decoder = new BinaryDecoder(plaintext.ToArray(), m_context);
            var kind = (NodeStateChangeKind)decoder.ReadByte(null);
            NodeId nodeId = decoder.ReadNodeId(null);
            ByteString payload = decoder.ReadByteString(null);
            if (nodeId.IsNull ||
                decoder.Position != plaintext.Length ||
                (kind == NodeStateChangeKind.Upsert && (payload.IsNull || payload.IsEmpty)) ||
                (kind == NodeStateChangeKind.Delete && !payload.IsEmpty))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"The distributed address-space delta {sequence} is invalid.");
            }
            return kind switch
            {
                NodeStateChangeKind.Upsert => new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Upsert,
                    NodeId = nodeId,
                    Node = new StoredNode(nodeId, payload),
                    Sequence = sequence
                },
                NodeStateChangeKind.Value => new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Value,
                    NodeId = nodeId,
                    Value = DecodeValueStrict(payload),
                    Sequence = sequence
                },
                NodeStateChangeKind.Delete => new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Delete,
                    NodeId = nodeId,
                    Sequence = sequence
                },
                _ => throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"The distributed address-space delta {sequence} has unsupported kind {(byte)kind}.")
            };
        }

        private async IAsyncEnumerable<NodeStateChange> ReadSnapshotEntriesAsync(
            SnapshotManifest manifest,
            [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < manifest.ChunkCount; i++)
            {
                (bool found, ByteString chunk) = await m_store
                    .TryGetAsync(SnapshotChunkKey(manifest.Generation, i), ct)
                    .ConfigureAwait(false);
                if (!found || !m_protector.TryUnprotect(chunk, out ByteString plaintext) || plaintext.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        $"Distributed address-space snapshot chunk {i} is missing or invalid.");
                }

                foreach (NodeStateChange change in DecodeSnapshotChunk(i, plaintext))
                {
                    yield return change;
                }
            }
        }

        private List<NodeStateChange> DecodeSnapshotChunk(
            int chunkIndex,
            ByteString plaintext)
        {
            try
            {
                byte[] buffer = plaintext.ToArray();
                using var decoder = new BinaryDecoder(buffer, m_context);
                var changes = new List<NodeStateChange>();
                while (decoder.Position < buffer.Length)
                {
                    var kind = (NodeStateChangeKind)decoder.ReadByte(null);
                    ulong entrySequence = decoder.ReadUInt64(null);
                    NodeId nodeId = decoder.ReadNodeId(null);
                    ByteString payload = decoder.ReadByteString(null);
                    if (nodeId.IsNull ||
                        (kind == NodeStateChangeKind.Upsert && (payload.IsNull || payload.IsEmpty)))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            "The distributed address-space snapshot entry is invalid.");
                    }
                    NodeStateChange change = kind switch
                    {
                        NodeStateChangeKind.Upsert => new NodeStateChange
                        {
                            Kind = NodeStateChangeKind.Upsert,
                            NodeId = nodeId,
                            Node = new StoredNode(nodeId, payload),
                            Sequence = entrySequence
                        },
                        NodeStateChangeKind.Value => new NodeStateChange
                        {
                            Kind = NodeStateChangeKind.Value,
                            NodeId = nodeId,
                            Value = DecodeValueStrict(payload),
                            Sequence = entrySequence
                        },
                        _ => throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            $"Unsupported snapshot entry kind {(byte)kind}.")
                    };
                    changes.Add(change);
                }
                return changes;
            }
            catch (ServiceResultException exception)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"Distributed address-space snapshot chunk {chunkIndex} is invalid.",
                    exception);
            }
        }

        private async ValueTask TrimDeltaLogAsync(ulong throughSequenceInclusive, CancellationToken ct)
        {
            var stale = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(DeltaPrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryParseSequence(entry.Key, out ulong seq) && seq <= throughSequenceInclusive)
                {
                    stale.Add(entry.Key);
                }
            }
            foreach (string key in stale)
            {
                await m_store.DeleteAsync(key, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask DeleteGenerationAsync(Guid generation, CancellationToken ct)
        {
            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(SnapshotChunkPrefix(generation), ct)
                .ConfigureAwait(false))
            {
                keys.Add(entry.Key);
            }
            foreach (string key in keys)
            {
                await m_store.DeleteAsync(key, ct).ConfigureAwait(false);
            }
        }

        private ByteString EncodeManifest(Guid generation, int chunkCount, ulong sequence, Guid previousGeneration)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteByteString(null, new ByteString(generation.ToByteArray()));
            encoder.WriteInt32(null, chunkCount);
            encoder.WriteUInt64(null, sequence);
            encoder.WriteByteString(null, new ByteString(previousGeneration.ToByteArray()));
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : new ByteString(buffer);
        }

        private bool TryDecodeManifest(ByteString stored, out SnapshotManifest manifest)
        {
            manifest = default;
            if (!m_protector.TryUnprotect(stored, out ByteString plaintext) || plaintext.IsNull)
            {
                return false;
            }
            try
            {
                using var decoder = new BinaryDecoder(plaintext.ToArray(), m_context);
                ByteString generationBytes = decoder.ReadByteString(null);
                int chunkCount = decoder.ReadInt32(null);
                ulong sequence = decoder.ReadUInt64(null);
                ByteString previousBytes = decoder.ReadByteString(null);
                if (generationBytes.Length != 16 ||
                    previousBytes.Length != 16 ||
                    chunkCount < 0 ||
                    chunkCount > MaxSnapshotChunks ||
                    decoder.Position != plaintext.Length)
                {
                    return false;
                }
                var generation = new Guid(generationBytes.ToArray());
                var previous = new Guid(previousBytes.ToArray());
                manifest = new SnapshotManifest(generation, chunkCount, sequence, previous);
                return true;
            }
            catch (Exception exception) when (
                exception is ServiceResultException or ArgumentException)
            {
                return false;
            }
        }

        private static string SnapshotChunkPrefix(Guid generation)
        {
            return SnapshotPrefix + generation.ToString("N") + "/";
        }

        private static string SnapshotChunkKey(Guid generation, int chunkIndex)
        {
            return SnapshotChunkPrefix(generation) + chunkIndex.ToString("D8", CultureInfo.InvariantCulture);
        }

        private static string FormatSequence(ulong sequence)
        {
            return sequence.ToString("D20", CultureInfo.InvariantCulture);
        }

        private static bool TryParseSequence(string key, out ulong sequence)
        {
            sequence = 0;
            int slash = key.LastIndexOf('/');
            if (slash < 0 || slash == key.Length - 1)
            {
                return false;
            }
            ulong value = 0;
            for (int i = slash + 1; i < key.Length; i++)
            {
                char c = key[i];
                if (c is < '0' or > '9')
                {
                    return false;
                }
                ulong digit = (ulong)(c - '0');
                if (value > (ulong.MaxValue - digit) / 10)
                {
                    return false;
                }
                value = (value * 10) + digit;
            }
            sequence = value;
            return true;
        }

        private static bool TryParseNodeId(string key, string prefix, out NodeId nodeId)
        {
            nodeId = NodeId.Null;
            if (key.Length <= prefix.Length)
            {
                return false;
            }
            try
            {
                nodeId = NodeId.Parse(key[prefix.Length..]);
                return !nodeId.IsNull;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        private async ValueTask<ulong> NextSequenceAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateCoordinator(m_store);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                (ByteString stored, ulong sharedSequence, List<ulong> pending) =
                    await ReadSequenceAsync(ct).ConfigureAwait(false);

                ulong current = Math.Max(CurrentSequence, sharedSequence);
                if (current == ulong.MaxValue)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadOutOfRange,
                        "The distributed address-space sequence is exhausted.");
                }
                ulong next = current + 1;
                pending.Add(next);
                ByteString replacement = EncodeSequenceRecord(next, pending);
                bool updated = await m_store
                    .CompareAndSwapAsync(
                        SequenceKey,
                        stored,
                        replacement,
                        ct)
                    .ConfigureAwait(false);
                if (updated)
                {
                    ObserveSequence(next);
                    return next;
                }
            }
        }

        private async ValueTask<(ByteString Record, ulong Sequence, List<ulong> Pending)> ReadSequenceAsync(
            CancellationToken ct)
        {
            (bool found, ByteString stored) = await m_store.TryGetAsync(SequenceKey, ct).ConfigureAwait(false);
            if (!found)
            {
                return (default, 0, []);
            }
            if (!m_protector.TryUnprotect(stored, out ByteString payload) ||
                payload.Length < sizeof(ulong) ||
                payload.Length % sizeof(ulong) != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "The distributed address-space sequence record is invalid.");
            }

            ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(payload.Span);
            var pending = new List<ulong>();
            ulong previous = 0;
            for (int offset = sizeof(ulong); offset < payload.Length; offset += sizeof(ulong))
            {
                ulong reservation = BinaryPrimitives.ReadUInt64BigEndian(payload.Span[offset..]);
                if (reservation <= previous || reservation > sequence)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "The distributed address-space publication reservations are invalid.");
                }
                pending.Add(reservation);
                previous = reservation;
            }
            ObserveSequence(sequence);
            return (stored, sequence, pending);
        }

        private ByteString EncodeSequenceRecord(ulong sequence, List<ulong> pending)
        {
            byte[] buffer = new byte[checked((pending.Count + 1) * sizeof(ulong))];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, sequence);
            for (int i = 0; i < pending.Count; i++)
            {
                BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan((i + 1) * sizeof(ulong)), pending[i]);
            }
            return m_protector.Protect(new ByteString(buffer));
        }

        private async ValueTask CompleteSequenceAsync(ulong sequence, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                (ByteString stored, ulong sharedSequence, List<ulong> pending) =
                    await ReadSequenceAsync(ct).ConfigureAwait(false);
                if (!pending.Remove(sequence))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        $"Distributed address-space publication {sequence} lost its reservation.");
                }
                if (await m_store.CompareAndSwapAsync(
                    SequenceKey,
                    stored,
                    EncodeSequenceRecord(sharedSequence, pending),
                    ct).ConfigureAwait(false))
                {
                    return;
                }
            }
        }

        private async ValueTask<bool> WritePrimaryRecordAsync(
            string key,
            ulong sequence,
            ByteString payload,
            CancellationToken ct)
        {
            ByteString replacement = m_protector.Protect(WithSequence(sequence, payload));
            bool linearizable = IsLinearizableKey(key);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                (bool found, ByteString stored) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
                bool replacedLiveRecord = false;
                if (found)
                {
                    (ulong previousSequence, ByteString previousPayload) = ReadRecord(key, stored);
                    if (previousSequence >= sequence)
                    {
                        return false;
                    }
                    replacedLiveRecord = !previousPayload.IsEmpty;
                }

                if (!linearizable)
                {
                    // This is deliberately not a CAS emulation. Concurrent CRDT
                    // rows may converge out of sequence; retained deltas carry the
                    // winning state and these scans never establish absence.
                    await m_store.SetAsync(key, replacement, ct).ConfigureAwait(false);
                    return replacedLiveRecord;
                }
                if (await m_store.CompareAndSwapAsync(
                    key,
                    found ? stored : default,
                    replacement,
                    ct).ConfigureAwait(false))
                {
                    return replacedLiveRecord;
                }
            }
        }

        private (ulong Sequence, ByteString Payload) ReadRecord(string key, ByteString stored)
        {
            if (!TryReadRecord(stored, out ulong sequence, out ByteString payload))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"The distributed address-space record '{key}' is invalid.");
            }
            return (sequence, payload);
        }

        private bool TryReadRecord(ByteString stored, out ulong sequence, out ByteString payload)
        {
            sequence = 0;
            payload = ByteString.Empty;
            return m_protector.TryUnprotect(stored, out ByteString wrapped) &&
                TrySplitSequence(wrapped, out sequence, out payload);
        }

        private static ByteString WithSequence(ulong sequence, ByteString payload)
        {
            int length = payload.IsNull ? 0 : payload.Length;
            byte[] buffer = new byte[SequencePrefixLength + length];
            BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(0, SequencePrefixLength), sequence);
            if (length > 0)
            {
                payload.Span.CopyTo(buffer.AsSpan(SequencePrefixLength));
            }
            return new ByteString(buffer);
        }

        private static bool TrySplitSequence(ByteString wrapped, out ulong sequence, out ByteString payload)
        {
            sequence = 0;
            payload = ByteString.Empty;
            if (wrapped.IsNull || wrapped.Length < SequencePrefixLength)
            {
                return false;
            }
            ReadOnlySpan<byte> span = wrapped.Span;
            sequence = BinaryPrimitives.ReadUInt64BigEndian(span[..SequencePrefixLength]);
            payload = new ByteString(span[SequencePrefixLength..].ToArray());
            return true;
        }

        /// <summary>
        /// The linearizable high-water mark followed by the ordered sequences of
        /// unfinished publications, all protected in one atomic coordination record.
        /// </summary>
        internal const string SequenceKey = "election/addressspace-sequence";
        private const int SequencePrefixLength = 8;
        private const int MaxChunkBytes = 1024 * 1024;
        private const int MaxSnapshotChunks = 65536;
        private const string NodePrefix = "n/";
        private const string ValuePrefix = "v/";
        private const string DeltaPrefix = "dlog/";
        private const string SnapshotPrefix = "snap/";
        private const string ManifestKey = "snapmeta/manifest";
        private const string PartitionPrefix = "partition/";
        private long m_sequence;
        private readonly SemaphoreSlim m_snapshotLock = new(1, 1);
        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly TimeSpan m_pollInterval;
        private int m_emitInitialStateSubscriptions;

        /// <summary>
        /// The published-snapshot pointer: which generation of chunks is live,
        /// how many chunks it has, the sequence it includes up to, and the
        /// predecessor generation retained for readers still draining it.
        /// </summary>
        private readonly record struct SnapshotManifest(
            Guid Generation,
            int ChunkCount,
            ulong Sequence,
            Guid PreviousGeneration);
    }
}
