// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// A single client subscription on an external OPC UA server that holds
    /// many dynamically managed monitored items at a fixed publishing interval.
    /// Each monitored item delivers the latest <see cref="DataValue"/> of one
    /// node attribute via the <see cref="DataChanged"/> event. Disposing the
    /// subscription removes it from the server.
    /// </summary>
    public interface IDataChangeSubscription : IAsyncDisposable
    {
        /// <summary>
        /// Raised on every data change reported by the server for any monitored
        /// item in this subscription. Handlers receive the originating client
        /// handle, node identifier, and the latest value.
        /// </summary>
        event EventHandler<DataChangeEventArgs>? DataChanged;

        /// <summary>
        /// Adds a monitored item to this subscription for the supplied node and
        /// attribute. The item is queued for creation on the server and becomes
        /// active after the next <see cref="ApplyChangesAsync"/> (or the next
        /// engine apply cycle). The publisher should prime the initial value by
        /// issuing a Read through <see cref="IServerSession.ReadAsync"/>.
        /// </summary>
        /// <param name="nodeId">
        /// The node to monitor.
        /// </param>
        /// <param name="attributeId">
        /// The attribute to monitor, for example <see cref="Attributes.Value"/>.
        /// </param>
        /// <param name="samplingIntervalMs">
        /// The requested sampling interval in milliseconds. Use <c>-1</c> to
        /// defer to the subscription publishing interval.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The client handle assigned to the new monitored item; it identifies
        /// the item in <see cref="DataChanged"/> notifications.
        /// </returns>
        ValueTask<uint> AddMonitoredItemAsync(
            NodeId nodeId,
            uint attributeId,
            double samplingIntervalMs,
            CancellationToken ct = default);

        /// <summary>
        /// Flushes monitored items added since the last call to the server and
        /// completes once they have been created (or settled with an error).
        /// The underlying managed subscription engine also applies changes
        /// automatically; this method lets a publisher await item creation
        /// deterministically before priming initial values.
        /// </summary>
        /// <param name="ct">
        /// A token used to cancel the wait.
        /// </param>
        ValueTask ApplyChangesAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Optional subscription capability for monitoring a server-side index-range projection.
    /// Providers without this capability remain usable for whole-value subscriptions.
    /// </summary>
    public interface IRangedDataChangeSubscription : IDataChangeSubscription
    {
        /// <summary>
        /// Adds a monitored item whose notifications contain only the requested range.
        /// </summary>
        /// <param name="nodeId">The node to monitor.</param>
        /// <param name="attributeId">The attribute to monitor.</param>
        /// <param name="indexRange">The OPC UA numeric range, or null for the complete value.</param>
        /// <param name="samplingIntervalMs">The requested sampling interval in milliseconds.</param>
        /// <param name="ct">A token used to cancel the operation.</param>
        /// <returns>The client handle identifying this exact projection.</returns>
        ValueTask<uint> AddMonitoredItemAsync(
            NodeId nodeId,
            uint attributeId,
            string? indexRange,
            double samplingIntervalMs,
            CancellationToken ct = default);
    }
}
