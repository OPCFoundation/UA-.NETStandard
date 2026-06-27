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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: default <see cref="IAddressSpaceSynchronizer"/>. See the interface for
    /// the writer/reader role model.
    /// </summary>
    public sealed class AddressSpaceSynchronizer : IAddressSpaceSynchronizer
    {
        /// <summary>
        /// Creates a synchronizer between a local graph and a shared store.
        /// </summary>
        /// <param name="store">The shared node state store.</param>
        /// <param name="addressSpace">The local node graph.</param>
        /// <param name="isWriter">
        /// Predicate that reports whether this replica is the writer
        /// (leader). Defaults to always-writer (single instance).
        /// </param>
        /// <param name="logger">Optional logger for replication errors.</param>
        public AddressSpaceSynchronizer(
            INodeStateStore store,
            ILocalAddressSpace addressSpace,
            Func<bool>? isWriter = null,
            ILogger? logger = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
            m_isWriter = isWriter ?? (static () => true);
            m_logger = logger;
            m_onChanged = OnLocalNodeChanged;
            m_onNodeAdded = OnLocalNodeAdded;
            m_onNodeRemoved = OnLocalNodeRemoved;
        }

        /// <inheritdoc/>
        public bool IsWriter => m_isWriter();

        /// <summary>
        /// Raised (for tests) after each inbound change is applied.
        /// </summary>
        internal event Action<NodeStateChange>? InboundApplied;

        /// <inheritdoc/>
        public async ValueTask SeedOrHydrateAsync(CancellationToken ct = default)
        {
            bool any = false;
            await foreach (IStoredNode stored in m_store.EnumerateAsync(ct).ConfigureAwait(false))
            {
                any = true;
                await TryApplyUpsertAsync(stored.NodeId, stored.Payload, ct).ConfigureAwait(false);
            }

            if (any)
            {
                // Apply the latest value for every hydrated variable; value
                // keys can be newer than the node payload they were carved
                // from.
                foreach (NodeState node in m_addressSpace.Nodes)
                {
                    if (node is BaseVariableState)
                    {
                        (bool found, DataValue value) = await m_store
                            .TryReadValueAsync(node.NodeId, ct)
                            .ConfigureAwait(false);
                        if (found)
                        {
                            ApplyValue(node.NodeId, value);
                        }
                    }
                }
                return;
            }

            if (m_isWriter())
            {
                foreach (NodeState node in m_addressSpace.Nodes)
                {
                    await m_store
                        .UpsertNodeAsync(
                            new StoredNode(node.NodeId, NodeStateSerializer.Serialize(m_addressSpace.Context, node)),
                            ct)
                        .ConfigureAwait(false);
                    if (node is BaseVariableState variable)
                    {
                        await m_store
                            .WriteValueAsync(
                                node.NodeId,
                                new DataValue(variable.Value, variable.StatusCode, variable.Timestamp),
                                ct)
                            .ConfigureAwait(false);
                    }
                }
            }
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

                if (m_isWriter())
                {
                    m_outbound = Channel.CreateUnbounded<OutboundOp>(
                        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                    m_addressSpace.NodeAdded += m_onNodeAdded;
                    m_addressSpace.NodeRemoved += m_onNodeRemoved;
                    foreach (NodeState node in m_addressSpace.Nodes)
                    {
                        AttachStateChanged(node);
                    }
                    m_outboundTask = Task.Run(() => DrainOutboundAsync(m_cts.Token));
                }
                else
                {
                    // Register the change-feed watcher synchronously (the
                    // first MoveNextAsync runs the iterator prefix that adds
                    // the watcher) so no change published after Start()
                    // returns is missed, then consume it on a background task.
                    m_inboundEnumerator = m_store.SubscribeChangesAsync(m_cts.Token).GetAsyncEnumerator();
                    ValueTask<bool> firstMove = m_inboundEnumerator.MoveNextAsync();
                    m_inboundTask = Task.Run(() => ApplyInboundLoopAsync(firstMove));
                }
            }
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

            m_cts.Cancel();
            m_outbound?.Writer.TryComplete();

            await AwaitQuietlyAsync(m_outboundTask).ConfigureAwait(false);
            await AwaitQuietlyAsync(m_inboundTask).ConfigureAwait(false);

            // The inbound loop has finished; dispose the enumerator it owned.
            if (m_inboundEnumerator != null)
            {
                await m_inboundEnumerator.DisposeAsync().ConfigureAwait(false);
                m_inboundEnumerator = null;
            }

            m_addressSpace.NodeAdded -= m_onNodeAdded;
            m_addressSpace.NodeRemoved -= m_onNodeRemoved;
            DetachAll();
            m_cts.Dispose();
        }

        private void OnLocalNodeAdded(NodeState node)
        {
            AttachStateChanged(node);
            Enqueue(OutboundOp.ForUpsert(
                node.NodeId,
                NodeStateSerializer.Serialize(m_addressSpace.Context, node)));
        }

        private void OnLocalNodeRemoved(NodeId nodeId)
        {
            Enqueue(OutboundOp.ForDelete(nodeId));
        }

        private void OnLocalNodeChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            // Deletes are driven by ILocalAddressSpace.NodeRemoved.
            if ((changes & NodeStateChangeMasks.Deleted) != 0)
            {
                return;
            }

            if (node is BaseVariableState variable && (changes & NodeStateChangeMasks.Value) != 0)
            {
                Enqueue(OutboundOp.ForValue(
                    node.NodeId,
                    new DataValue(variable.Value, variable.StatusCode, variable.Timestamp)));
            }

            if ((changes & (NodeStateChangeMasks.NonValue |
                NodeStateChangeMasks.Children |
                NodeStateChangeMasks.References)) != 0)
            {
                Enqueue(OutboundOp.ForUpsert(
                    node.NodeId,
                    NodeStateSerializer.Serialize(m_addressSpace.Context, node)));
            }
        }

        private void Enqueue(OutboundOp op)
        {
            m_outbound?.Writer.TryWrite(op);
        }

        private async Task DrainOutboundAsync(CancellationToken ct)
        {
            try
            {
                await foreach (OutboundOp op in m_outbound!.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        switch (op.Kind)
                        {
                            case OutboundOpKind.Value:
                                await m_store.WriteValueAsync(op.NodeId, op.Value, ct).ConfigureAwait(false);
                                break;
                            case OutboundOpKind.Upsert:
                                await m_store
                                    .UpsertNodeAsync(new StoredNode(op.NodeId, op.Payload), ct)
                                    .ConfigureAwait(false);
                                break;
                            case OutboundOpKind.Delete:
                                await m_store.DeleteNodeAsync(op.NodeId, ct).ConfigureAwait(false);
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_logger?.LogError(ex, "Distributed address-space outbound write failed for {NodeId}.", op.NodeId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private async Task ApplyInboundLoopAsync(ValueTask<bool> firstMove)
        {
            try
            {
                bool hasValue = await firstMove.ConfigureAwait(false);
                while (hasValue)
                {
                    NodeStateChange change = m_inboundEnumerator!.Current;
                    try
                    {
                        await ApplyInboundAsync(change, m_cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger?.LogError(ex, "Distributed address-space inbound apply failed for {NodeId}.", change.NodeId);
                    }

                    InboundApplied?.Invoke(change);
                    hasValue = await m_inboundEnumerator.MoveNextAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private async ValueTask ApplyInboundAsync(NodeStateChange change, CancellationToken cancellationToken)
        {
            switch (change.Kind)
            {
                case NodeStateChangeKind.Upsert:
                    if (change.Node != null)
                    {
                        await TryApplyUpsertAsync(change.NodeId, change.Node.Payload, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    break;
                case NodeStateChangeKind.Delete:
                    await m_addressSpace.RemoveNodeAsync(change.NodeId, cancellationToken).ConfigureAwait(false);
                    break;
                case NodeStateChangeKind.Value:
                    ApplyValue(change.NodeId, change.Value);
                    break;
            }
        }

        private async ValueTask TryApplyUpsertAsync(NodeId nodeId, ByteString payload, CancellationToken cancellationToken)
        {
            NodeState node = NodeStateSerializer.Deserialize(m_addressSpace.Context, payload);
            await m_addressSpace.AddOrUpdateNodeAsync(node, cancellationToken).ConfigureAwait(false);
        }

        private void ApplyValue(NodeId nodeId, DataValue value)
        {
            if (m_addressSpace.TryGetNode(nodeId, out NodeState? node) && node is BaseVariableState variable)
            {
                variable.Value = value.WrappedValue;
                variable.StatusCode = value.StatusCode;
                variable.Timestamp = value.SourceTimestamp;
                variable.ClearChangeMasks(m_addressSpace.Context, false);
            }
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

        private static async Task AwaitQuietlyAsync(Task? task)
        {
            if (task == null)
            {
                return;
            }
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        private enum OutboundOpKind
        {
            Value,
            Upsert,
            Delete
        }

        private readonly struct OutboundOp
        {
            private OutboundOp(OutboundOpKind kind, NodeId nodeId, DataValue value, ByteString payload)
            {
                Kind = kind;
                NodeId = nodeId;
                Value = value;
                Payload = payload;
            }

            public OutboundOpKind Kind { get; }

            public NodeId NodeId { get; }

            public DataValue Value { get; }

            public ByteString Payload { get; }

            public static OutboundOp ForValue(NodeId nodeId, DataValue value)
            {
                return new OutboundOp(OutboundOpKind.Value, nodeId, value, default);
            }

            public static OutboundOp ForUpsert(NodeId nodeId, ByteString payload)
            {
                return new OutboundOp(OutboundOpKind.Upsert, nodeId, DataValue.Null, payload);
            }

            public static OutboundOp ForDelete(NodeId nodeId)
            {
                return new OutboundOp(OutboundOpKind.Delete, nodeId, DataValue.Null, default);
            }
        }

        private readonly INodeStateStore m_store;
        private readonly ILocalAddressSpace m_addressSpace;
        private readonly Func<bool> m_isWriter;
        private readonly ILogger? m_logger;
        private readonly NodeStateChangedHandler m_onChanged;
        private readonly Action<NodeState> m_onNodeAdded;
        private readonly Action<NodeId> m_onNodeRemoved;
        private readonly CancellationTokenSource m_cts = new();
        private readonly Lock m_lock = new();
        private readonly HashSet<NodeState> m_attached = [];
        private Channel<OutboundOp>? m_outbound;
        private IAsyncEnumerator<NodeStateChange>? m_inboundEnumerator;
        private Task? m_outboundTask;
        private Task? m_inboundTask;
        private bool m_started;
        private bool m_disposed;
    }
}
