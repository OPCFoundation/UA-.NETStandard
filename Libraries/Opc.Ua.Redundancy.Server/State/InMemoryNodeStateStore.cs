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
    public sealed class InMemoryNodeStateStore : INodeStateStore
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

        /// <inheritdoc/>
        public ValueTask UpsertNodeAsync(IStoredNode node, CancellationToken ct = default)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            return m_store.SetAsync(NodePrefix + node.NodeId, m_protector.Protect(node.Payload), ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            return m_store.DeleteAsync(NodePrefix + nodeId, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<IStoredNode?> TryGetNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(NodePrefix + nodeId, ct)
                .ConfigureAwait(false);
            if (found && m_protector.TryUnprotect(value, out ByteString payload))
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
                    m_protector.TryUnprotect(entry.Value, out ByteString payload))
                {
                    yield return new StoredNode(id, payload);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask WriteValueAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
        {
            // Encode synchronously (in-parameters are not allowed in async
            // methods) and return the backend write task directly.
            ByteString bytes = m_protector.Protect(EncodeValue(in value));
            return m_store.SetAsync(ValuePrefix + nodeId, bytes, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<(bool Found, DataValue Value)> TryReadValueAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(ValuePrefix + nodeId, ct)
                .ConfigureAwait(false);
            if (found && m_protector.TryUnprotect(value, out ByteString payload))
            {
                return (true, DecodeValue(payload));
            }
            return (false, DataValue.Null);
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
                if (!m_protector.TryUnprotect(change.Value, out ByteString payload))
                {
                    return null;
                }
                return new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Upsert,
                    NodeId = id,
                    Node = new StoredNode(id, payload)
                };
            }

            if (change.Key.StartsWith(ValuePrefix, StringComparison.Ordinal))
            {
                if (change.Kind != KeyValueChangeKind.Set ||
                    !TryParseNodeId(change.Key, ValuePrefix, out NodeId id))
                {
                    return null;
                }
                if (!m_protector.TryUnprotect(change.Value, out ByteString payload))
                {
                    return null;
                }
                return new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Value,
                    NodeId = id,
                    Value = DecodeValue(payload)
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

        private const string NodePrefix = "n/";
        private const string ValuePrefix = "v/";
        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly TimeSpan m_pollInterval;
    }
}
