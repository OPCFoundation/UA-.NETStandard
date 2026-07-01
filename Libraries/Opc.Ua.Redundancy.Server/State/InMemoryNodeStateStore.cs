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
using Opc.Ua.Redundancy;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: default <see cref="INodeStateStore"/> layered on an
    /// <see cref="ISharedKeyValueStore"/>. Node payloads and encoded values
    /// are stored under distinct key prefixes so topology and value changes
    /// can be routed independently on the change-feed.
    /// </summary>
    public sealed class InMemoryNodeStateStore : INodeStateStore, INodeStateSnapshotStore, IDisposable
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
        public ulong CurrentSequence => unchecked((ulong)Interlocked.Read(ref m_sequence));

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
            while (current < observed)
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
            ulong sequence = NextSequence();
            await m_store
                .SetAsync(
                    NodePrefix + node.NodeId,
                    m_protector.Protect(WithSequence(sequence, node.Payload)),
                    ct)
                .ConfigureAwait(false);
            await AppendDeltaAsync(sequence, NodeStateChangeKind.Upsert, node.NodeId, node.Payload, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ulong sequence = NextSequence();
            bool removed = await m_store.DeleteAsync(NodePrefix + nodeId, ct).ConfigureAwait(false);
            await AppendDeltaAsync(sequence, NodeStateChangeKind.Delete, nodeId, ByteString.Empty, ct)
                .ConfigureAwait(false);
            return removed;
        }

        /// <inheritdoc/>
        public async ValueTask<IStoredNode?> TryGetNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(NodePrefix + nodeId, ct)
                .ConfigureAwait(false);
            if (found && TryReadRecord(value, out _, out ByteString payload))
            {
                return new StoredNode(nodeId, payload);
            }
            return null;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<IStoredNode> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(NodePrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryParseNodeId(entry.Key, NodePrefix, out NodeId id) &&
                    TryReadRecord(entry.Value, out _, out ByteString payload))
                {
                    yield return new StoredNode(id, payload);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask WriteValueAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
        {
            // Encode synchronously (in-parameters are not allowed in async
            // methods) and hand off to the async record + delta-log writer.
            ulong sequence = NextSequence();
            ByteString payload = EncodeValue(in value);
            return WriteValueRecordAsync(nodeId, sequence, payload, ct);
        }

        private async ValueTask WriteValueRecordAsync(
            NodeId nodeId,
            ulong sequence,
            ByteString payload,
            CancellationToken ct)
        {
            await m_store
                .SetAsync(ValuePrefix + nodeId, m_protector.Protect(WithSequence(sequence, payload)), ct)
                .ConfigureAwait(false);
            await AppendDeltaAsync(sequence, NodeStateChangeKind.Value, nodeId, payload, ct)
                .ConfigureAwait(false);
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
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(ValuePrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryParseNodeId(entry.Key, ValuePrefix, out NodeId id) &&
                    TryReadRecord(entry.Value, out _, out ByteString payload))
                {
                    yield return (id, DecodeValue(payload));
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask WriteSnapshotAsync(CancellationToken ct = default)
        {
            await m_snapshotLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ulong sequence = CurrentSequence;
                var generation = Guid.NewGuid();
                int chunkIndex = 0;
                BinaryEncoder? encoder = null;

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
                    if (TryParseNodeId(entry.Key, NodePrefix, out NodeId id) &&
                        TryReadRecord(entry.Value, out ulong seq, out ByteString payload))
                    {
                        Append((byte)NodeStateChangeKind.Upsert, seq, id, payload);
                        if (encoder!.Position >= MaxChunkBytes)
                        {
                            await FlushAsync().ConfigureAwait(false);
                        }
                    }
                }

                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(ValuePrefix, ct)
                    .ConfigureAwait(false))
                {
                    if (TryParseNodeId(entry.Key, ValuePrefix, out NodeId id) &&
                        TryReadRecord(entry.Value, out ulong seq, out ByteString payload))
                    {
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

                // Chunks are written before the manifest, so a reader never sees a
                // partial snapshot (single writer, so a plain set is sufficient).
                await m_store
                    .SetAsync(
                        ManifestKey,
                        m_protector.Protect(EncodeManifest(generation, chunkIndex, sequence, predecessor)),
                        ct)
                    .ConfigureAwait(false);

                await TrimDeltaLogAsync(sequence, ct).ConfigureAwait(false);

                if (generationToCollect is Guid collect && collect != Guid.Empty)
                {
                    await DeleteGenerationAsync(collect, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_snapshotLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<NodeStateSnapshot?> TryReadSnapshotAsync(CancellationToken ct = default)
        {
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
                NodeStateChange? change = DecodeDelta(seq, frame);
                if (change != null)
                {
                    ObserveSequence(seq);
                    yield return change;
                }
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<NodeStateChange> SubscribeChangesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
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

            await foreach (NodeStateChange change in PollChangesAsync(ct).ConfigureAwait(false))
            {
                yield return change;
            }
        }

        private async IAsyncEnumerable<NodeStateChange> PollChangesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            var nodes = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            var values = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            bool baseline = false;

            while (!ct.IsCancellationRequested)
            {
                Dictionary<string, ByteString> currentNodes =
                    await SnapshotPrefixAsync(NodePrefix, ct).ConfigureAwait(false);
                Dictionary<string, ByteString> currentValues =
                    await SnapshotPrefixAsync(ValuePrefix, ct).ConfigureAwait(false);

                if (baseline)
                {
                    // Only changes after the first (baseline) scan are emitted,
                    // matching the change-feed "observe changes after the call"
                    // contract; the synchronizer hydrates the initial snapshot
                    // separately via EnumerateAsync.
                    foreach (NodeStateChange change in DiffSet(nodes, currentNodes))
                    {
                        yield return change;
                    }
                    foreach (NodeStateChange change in DiffDeletes(nodes, currentNodes))
                    {
                        yield return change;
                    }
                    foreach (NodeStateChange change in DiffSet(values, currentValues))
                    {
                        yield return change;
                    }
                }

                nodes = currentNodes;
                values = currentValues;
                baseline = true;
                await Task.Delay(m_pollInterval, ct).ConfigureAwait(false);
            }
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
                    return new NodeStateChange { Kind = NodeStateChangeKind.Delete, NodeId = id };
                }
                if (!TryReadRecord(change.Value, out ulong sequence, out ByteString payload))
                {
                    return null;
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
                if (!TryReadRecord(change.Value, out ulong sequence, out ByteString payload))
                {
                    return null;
                }
                return new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Value,
                    NodeId = id,
                    Value = DecodeValue(payload),
                    Sequence = sequence
                };
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

        private NodeStateChange? DecodeDelta(ulong sequence, ByteString frame)
        {
            if (!m_protector.TryUnprotect(frame, out ByteString plaintext) || plaintext.IsNull)
            {
                return null;
            }
            using var decoder = new BinaryDecoder(plaintext.ToArray(), m_context);
            var kind = (NodeStateChangeKind)decoder.ReadByte(null);
            NodeId nodeId = decoder.ReadNodeId(null);
            ByteString payload = decoder.ReadByteString(null);
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
                    Value = DecodeValue(payload),
                    Sequence = sequence
                },
                NodeStateChangeKind.Delete => new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Delete,
                    NodeId = nodeId,
                    Sequence = sequence
                },
                _ => null
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
                    continue;
                }

                byte[] buffer = plaintext.ToArray();
                using var decoder = new BinaryDecoder(buffer, m_context);
                while (decoder.Position < buffer.Length)
                {
                    var kind = (NodeStateChangeKind)decoder.ReadByte(null);
                    ulong entrySequence = decoder.ReadUInt64(null);
                    NodeId nodeId = decoder.ReadNodeId(null);
                    ByteString payload = decoder.ReadByteString(null);
                    NodeStateChange? change = kind switch
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
                            Value = DecodeValue(payload),
                            Sequence = entrySequence
                        },
                        _ => null
                    };
                    if (change != null)
                    {
                        yield return change;
                    }
                }
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
                var generation = new Guid(decoder.ReadByteString(null).ToArray());
                int chunkCount = decoder.ReadInt32(null);
                ulong sequence = decoder.ReadUInt64(null);
                var previous = new Guid(decoder.ReadByteString(null).ToArray());
                manifest = new SnapshotManifest(generation, chunkCount, sequence, previous);
                return true;
            }
            catch (ServiceResultException)
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
            return slash >= 0 &&
                ulong.TryParse(
                    key.Substring(slash + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out sequence);
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
                nodeId = NodeId.Parse(key.Substring(prefix.Length));
                return !nodeId.IsNull;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        private ulong NextSequence()
        {
            return unchecked((ulong)Interlocked.Increment(ref m_sequence));
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
            sequence = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(0, SequencePrefixLength));
            payload = new ByteString(span.Slice(SequencePrefixLength).ToArray());
            return true;
        }

        private const int SequencePrefixLength = 8;
        private const int MaxChunkBytes = 1024 * 1024;
        private const string NodePrefix = "n/";
        private const string ValuePrefix = "v/";
        private const string DeltaPrefix = "dlog/";
        private const string SnapshotPrefix = "snap/";
        private const string ManifestKey = "snapmeta/manifest";
        private long m_sequence;
        private readonly SemaphoreSlim m_snapshotLock = new(1, 1);
        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly TimeSpan m_pollInterval;

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
