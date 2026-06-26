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
using System.Threading.Channels;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Distributed.Crdt
{
    /// <summary>
    /// Active/active (multi-writer) <see cref="IAddressSpaceSynchronizer"/>.
    /// Models the node manager's address space as a last-writer-wins CRDT map
    /// (keyed by node id, with separate topology and value entries) and
    /// gossips full state through an <see cref="ITransport"/>. Every replica is
    /// a writer: local changes mutate the local CRDT replica and broadcast,
    /// while received state is merged and the resulting differences are applied
    /// to the local graph. There is no leader.
    /// </summary>
    public sealed class CrdtAddressSpaceSynchronizer : IAddressSpaceSynchronizer
    {
        /// <summary>
        /// Creates a CRDT address-space synchronizer.
        /// </summary>
        /// <param name="addressSpace">The local node graph adapter.</param>
        /// <param name="messageContext">The message context used to encode values.</param>
        /// <param name="replicaId">This replica's stable CRDT identity.</param>
        /// <param name="transport">The gossip transport (owned by this synchronizer).</param>
        /// <param name="timeProvider">The time source for the logical clock.</param>
        /// <param name="readerOptions">Decoding limits for received state.</param>
        /// <param name="logger">Optional logger.</param>
        public CrdtAddressSpaceSynchronizer(
            ILocalAddressSpace addressSpace,
            IServiceMessageContext messageContext,
            ReplicaId replicaId,
            ITransport transport,
            TimeProvider timeProvider,
            CrdtReaderOptions readerOptions,
            ILogger? logger = null)
        {
            m_addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
            m_messageContext = messageContext ?? throw new ArgumentNullException(nameof(messageContext));
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_readerOptions = readerOptions ?? throw new ArgumentNullException(nameof(readerOptions));
            m_logger = logger;
            m_clock = new HybridLogicalClock(replicaId, timeProvider);
            m_onNodeAdded = OnLocalNodeAdded;
            m_onNodeRemoved = OnLocalNodeRemoved;
            m_onChanged = OnLocalNodeChanged;
            m_inbound = Channel.CreateUnbounded<byte[]>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            m_transport.FrameReceived += OnFrameReceived;
        }

        /// <inheritdoc/>
        public bool IsWriter => true;

        /// <summary>
        /// Raised (for tests) after each received frame has been merged and
        /// applied to the local graph.
        /// </summary>
        internal event Action? InboundApplied;

        /// <inheritdoc/>
        public async ValueTask SeedOrHydrateAsync(CancellationToken ct = default)
        {
            await m_transport.StartAsync(ct).ConfigureAwait(false);

            byte[] snapshot;
            lock (m_lock)
            {
                foreach (NodeState node in m_addressSpace.Nodes)
                {
                    CaptureUpsertLocked(node);
                    if (node is BaseVariableState variable)
                    {
                        CaptureValueLocked(variable);
                    }
                }
                snapshot = SerializeLocked();
            }

            await m_transport.SendAsync(snapshot, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Start()
        {
            lock (m_lock)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;

                m_addressSpace.NodeAdded += m_onNodeAdded;
                m_addressSpace.NodeRemoved += m_onNodeRemoved;
                foreach (NodeState node in m_addressSpace.Nodes)
                {
                    AttachStateChanged(node);
                }
            }

            m_inboundTask = Task.Run(() => DrainInboundAsync(m_cts.Token));
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
            m_addressSpace.NodeAdded -= m_onNodeAdded;
            m_addressSpace.NodeRemoved -= m_onNodeRemoved;
            DetachAll();

            m_cts.Cancel();
            m_inbound.Writer.TryComplete();
            if (m_inboundTask != null)
            {
                try
                {
                    await m_inboundTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }

            await m_transport.DisposeAsync().ConfigureAwait(false);
            m_cts.Dispose();
        }

        private void OnFrameReceived(ReadOnlyMemory<byte> frame)
        {
            // Copy: the transport may reuse the buffer after the callback.
            m_inbound.Writer.TryWrite(frame.ToArray());
        }

        private async Task DrainInboundAsync(CancellationToken ct)
        {
            try
            {
                await foreach (byte[] frame in m_inbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        await ApplyInboundFrameAsync(frame, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_logger?.LogError(ex, "CRDT address-space inbound apply failed.");
                    }

                    InboundApplied?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private async ValueTask ApplyInboundFrameAsync(byte[] frame, CancellationToken ct)
        {
            List<Diff> diffs;
            byte[] mergedSnapshot;
            lock (m_lock)
            {
                LWWMap<string, ByteString> remote = LWWMap<string, ByteString>.ReadFrom(
                    frame, CrdtValues.String, ByteStringCrdtSerializer.Instance, m_readerOptions);
                m_map.Merge(remote);
                diffs = ComputeDiffsLocked();
                mergedSnapshot = SerializeLocked();
            }

            // Apply outside the lock; the apply path awaits the node manager and
            // suppresses re-capture of its own mutations via m_applyingInbound.
            bool previous = m_applyingInbound.Value;
            m_applyingInbound.Value = true;
            try
            {
                foreach (Diff diff in diffs)
                {
                    await ApplyDiffAsync(diff, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_applyingInbound.Value = previous;
            }

            // Anti-entropy: when a merge changed our state, re-broadcast the
            // merged result so it propagates transitively across the cluster.
            // This terminates once every replica's merge is a no-op (LWW is
            // idempotent), and complements the transport's own gossip.
            if (diffs.Count > 0)
            {
                Broadcast(mergedSnapshot);
            }
        }

        private async ValueTask ApplyDiffAsync(Diff diff, CancellationToken ct)
        {
            if (!TryParseKey(diff.Key, out bool isValue, out NodeId nodeId))
            {
                return;
            }

            if (diff.Removed)
            {
                if (!isValue)
                {
                    await m_addressSpace.RemoveNodeAsync(nodeId, ct).ConfigureAwait(false);
                }
                return;
            }

            if (isValue)
            {
                if (m_addressSpace.TryGetNode(nodeId, out NodeState? node) && node is BaseVariableState variable)
                {
                    DataValue value = DecodeValue(diff.Value);
                    variable.Value = value.WrappedValue;
                    variable.StatusCode = value.StatusCode;
                    variable.Timestamp = value.SourceTimestamp;
                    variable.ClearChangeMasks(m_addressSpace.Context, false);
                }
                return;
            }

            NodeState reconstructed = NodeStateSerializer.Deserialize(m_addressSpace.Context, diff.Value);

            // The topology payload also carries the variable's value, but values
            // are versioned independently via the value (v|) entries. Preserve
            // the locally-known value so a topology merge never regresses a value
            // that a concurrent value entry already advanced.
            if (reconstructed is BaseVariableState reconstructedVariable &&
                m_addressSpace.TryGetNode(nodeId, out NodeState? existing) &&
                existing is BaseVariableState existingVariable)
            {
                reconstructedVariable.Value = existingVariable.Value;
                reconstructedVariable.StatusCode = existingVariable.StatusCode;
                reconstructedVariable.Timestamp = existingVariable.Timestamp;
            }

            await m_addressSpace.AddOrUpdateNodeAsync(reconstructed, ct).ConfigureAwait(false);
            AttachStateChanged(reconstructed);
        }

        private void OnLocalNodeAdded(NodeState node)
        {
            if (m_applyingInbound.Value)
            {
                return;
            }

            AttachStateChanged(node);
            byte[] snapshot;
            lock (m_lock)
            {
                CaptureUpsertLocked(node);
                snapshot = SerializeLocked();
            }
            Broadcast(snapshot);
        }

        private void OnLocalNodeRemoved(NodeId nodeId)
        {
            if (m_applyingInbound.Value)
            {
                return;
            }

            byte[] snapshot;
            lock (m_lock)
            {
                m_map.Remove(TopologyKey(nodeId), m_clock);
                m_map.Remove(ValueKey(nodeId), m_clock);
                m_lastApplied.Remove(TopologyKey(nodeId));
                m_lastApplied.Remove(ValueKey(nodeId));
                snapshot = SerializeLocked();
            }
            Broadcast(snapshot);
        }

        private void OnLocalNodeChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            if (m_applyingInbound.Value || (changes & NodeStateChangeMasks.Deleted) != 0)
            {
                return;
            }

            byte[] snapshot;
            lock (m_lock)
            {
                if (node is BaseVariableState variable && (changes & NodeStateChangeMasks.Value) != 0)
                {
                    CaptureValueLocked(variable);
                }

                if ((changes & (NodeStateChangeMasks.NonValue |
                    NodeStateChangeMasks.Children |
                    NodeStateChangeMasks.References)) != 0)
                {
                    CaptureUpsertLocked(node);
                }
                snapshot = SerializeLocked();
            }
            Broadcast(snapshot);
        }

        private void CaptureUpsertLocked(NodeState node)
        {
            string key = TopologyKey(node.NodeId);
            ByteString payload = NodeStateSerializer.Serialize(m_addressSpace.Context, node);
            m_map.Set(key, payload, m_clock.Now());
            UpdateLastAppliedLocked(key);
        }

        private void CaptureValueLocked(BaseVariableState variable)
        {
            string key = ValueKey(variable.NodeId);
            ByteString encoded = EncodeValue(
                new DataValue(variable.Value, variable.StatusCode, variable.Timestamp));
            m_map.Set(key, encoded, m_clock.Now());
            UpdateLastAppliedLocked(key);
        }

        private void UpdateLastAppliedLocked(string key)
        {
            if (m_map.TryGetValue(key, out ByteString stored))
            {
                m_lastApplied[key] = stored.IsNull ? Array.Empty<byte>() : stored.ToArray();
            }
            else
            {
                m_lastApplied.Remove(key);
            }
        }

        private byte[] SerializeLocked()
        {
            return m_map.ToByteArray(CrdtValues.String, ByteStringCrdtSerializer.Instance);
        }

        private List<Diff> ComputeDiffsLocked()
        {
            var diffs = new List<Diff>();
            var live = new HashSet<string>();

            foreach (string key in m_map.Keys)
            {
                if (!m_map.TryGetValue(key, out ByteString current))
                {
                    continue;
                }
                live.Add(key);
                byte[] currentBytes = current.ToArray();
                if (!m_lastApplied.TryGetValue(key, out byte[]? previous) ||
                    !previous.AsSpan().SequenceEqual(currentBytes))
                {
                    diffs.Add(new Diff(key, current, removed: false));
                    m_lastApplied[key] = currentBytes;
                }
            }

            foreach (string key in new List<string>(m_lastApplied.Keys))
            {
                if (!live.Contains(key))
                {
                    diffs.Add(new Diff(key, default, removed: true));
                    m_lastApplied.Remove(key);
                }
            }

            return diffs;
        }

        private void Broadcast(byte[] snapshot)
        {
            // Fire-and-forget; the gossip transport re-disseminates the latest
            // frame, so a dropped send still converges.
            _ = SendQuietlyAsync(snapshot);
        }

        private async Task SendQuietlyAsync(byte[] snapshot)
        {
            try
            {
                await m_transport.SendAsync(snapshot, m_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                m_logger?.LogError(ex, "CRDT address-space broadcast failed.");
            }
        }

        private ByteString EncodeValue(DataValue value)
        {
            using var encoder = new BinaryEncoder(m_messageContext);
            encoder.WriteDataValue(null, in value);
            return new ByteString(encoder.CloseAndReturnBuffer());
        }

        private DataValue DecodeValue(ByteString bytes)
        {
            if (bytes.IsNull)
            {
                return DataValue.Null;
            }
            using var decoder = new BinaryDecoder(bytes.ToArray(), m_messageContext);
            return decoder.ReadDataValue(null);
        }

        private void AttachStateChanged(NodeState node)
        {
            lock (m_lock)
            {
                if (m_attached.Add(node))
                {
                    node.StateChanged += m_onChanged;
                }
            }
        }

        private void DetachAll()
        {
            lock (m_lock)
            {
                foreach (NodeState node in m_attached)
                {
                    node.StateChanged -= m_onChanged;
                }
                m_attached.Clear();
            }
        }

        private static string TopologyKey(NodeId nodeId)
        {
            return TopologyPrefix + nodeId;
        }

        private static string ValueKey(NodeId nodeId)
        {
            return ValuePrefix + nodeId;
        }

        private static bool TryParseKey(string key, out bool isValue, out NodeId nodeId)
        {
            isValue = key.StartsWith(ValuePrefix, StringComparison.Ordinal);
            bool isTopology = key.StartsWith(TopologyPrefix, StringComparison.Ordinal);
            if (!isValue && !isTopology)
            {
                nodeId = default;
                return false;
            }

            try
            {
                nodeId = NodeId.Parse(key.Substring(2));
                return true;
            }
            catch (ServiceResultException)
            {
                nodeId = default;
                return false;
            }
        }

        private readonly struct Diff
        {
            public Diff(string key, ByteString value, bool removed)
            {
                Key = key;
                Value = value;
                Removed = removed;
            }

            public string Key { get; }

            public ByteString Value { get; }

            public bool Removed { get; }
        }

        private const string TopologyPrefix = "n|";
        private const string ValuePrefix = "v|";

        private readonly ILocalAddressSpace m_addressSpace;
        private readonly IServiceMessageContext m_messageContext;
        private readonly ITransport m_transport;
        private readonly TimeProvider m_timeProvider;
        private readonly CrdtReaderOptions m_readerOptions;
        private readonly ILogger? m_logger;
        private readonly HybridLogicalClock m_clock;
        private readonly Action<NodeState> m_onNodeAdded;
        private readonly Action<NodeId> m_onNodeRemoved;
        private readonly NodeStateChangedHandler m_onChanged;
        private readonly Channel<byte[]> m_inbound;
        private readonly CancellationTokenSource m_cts = new();
        private readonly Lock m_lock = new();
        private readonly LWWMap<string, ByteString> m_map = new();
        private readonly Dictionary<string, byte[]> m_lastApplied = [];
        private readonly HashSet<NodeState> m_attached = [];
        private readonly AsyncLocal<bool> m_applyingInbound = new();
        private Task? m_inboundTask;
        private bool m_started;
        private bool m_disposed;
    }
}
