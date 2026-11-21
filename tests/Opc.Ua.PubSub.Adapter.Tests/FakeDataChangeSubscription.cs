// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// In-memory <see cref="IDataChangeSubscription"/> test double that
    /// records monitored items added to it, hands out incrementing client
    /// handles, and lets a test raise <see cref="DataChanged"/> notifications.
    /// </summary>
    internal sealed class FakeDataChangeSubscription : IRangedDataChangeSubscription
    {
        private uint m_nextHandle = 1;

        public event EventHandler<DataChangeEventArgs>? DataChanged;

        public List<(NodeId NodeId, uint AttributeId, double SamplingMs)> MonitoredItems { get; }
            = [];

        /// <summary>
        /// The server-side range requested for each monitored item.
        /// </summary>
        public List<string?> IndexRanges { get; } = [];

        public int ApplyChangesCount { get; private set; }

        public bool Disposed { get; private set; }

        public ValueTask<uint> AddMonitoredItemAsync(
            NodeId nodeId,
            uint attributeId,
            double samplingIntervalMs,
            CancellationToken ct = default)
        {
            return AddMonitoredItemAsync(nodeId, attributeId, null, samplingIntervalMs, ct);
        }

        /// <inheritdoc/>
        public ValueTask<uint> AddMonitoredItemAsync(
            NodeId nodeId,
            uint attributeId,
            string? indexRange,
            double samplingIntervalMs,
            CancellationToken ct = default)
        {
            uint handle = m_nextHandle++;
            MonitoredItems.Add((nodeId, attributeId, samplingIntervalMs));
            IndexRanges.Add(indexRange);
            return new ValueTask<uint>(handle);
        }

        public ValueTask ApplyChangesAsync(CancellationToken ct = default)
        {
            ApplyChangesCount++;
            return default;
        }

        public void Raise(uint clientHandle, NodeId nodeId, DataValue value)
        {
            DataChanged?.Invoke(
                this, new DataChangeEventArgs(clientHandle, nodeId, value));
        }

        /// <summary>
        /// Captures an admitted notification so it can be delivered after handler removal or retirement.
        /// </summary>
        public Action CaptureNotification(uint clientHandle, NodeId nodeId, DataValue value)
        {
            EventHandler<DataChangeEventArgs>? handler = DataChanged;
            var notification = new DataChangeEventArgs(clientHandle, nodeId, value);
            return () => handler?.Invoke(this, notification);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
