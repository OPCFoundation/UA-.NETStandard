// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that can persist samples for many
    /// nodes in a single round-trip more efficiently than N back-to-back
    /// <see cref="IHistorianDataProvider.InsertAsync"/> calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework's auto-capture pipeline (see
    /// <c>HistorianCaptureSink</c>) prefers this when the resolved
    /// provider implements it; otherwise it falls back to per-node
    /// <see cref="IHistorianDataProvider.InsertAsync"/>.
    /// </para>
    /// <para>
    /// Implementations should amortise per-call overhead (lock
    /// acquisition, transaction setup, remote round-trips) across the
    /// whole batch. The bundled <c>InMemoryHistorianProvider</c>
    /// implementation acquires its lock once per <c>InsertBatchAsync</c>
    /// rather than once per node.
    /// </para>
    /// <para>
    /// Per-value status follows the same semantics as
    /// <see cref="IHistorianDataProvider.InsertAsync"/> — best-effort,
    /// one <see cref="StatusCode"/> per input value per node.
    /// </para>
    /// </remarks>
    public interface IHistorianBulkInsertProvider
    {
        /// <summary>
        /// Bulk-inserts values for one or more nodes.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="batch">
        /// Map of historizing variable <see cref="NodeId"/> to the values
        /// to insert for that node, in ascending source-timestamp order.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// One outcome per input batch entry, in the same order.
        /// </returns>
        ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
            HistorianOperationContext context,
            ArrayOf<HistorianDataBatch> batch,
            CancellationToken ct);
    }

    /// <summary>
    /// Values captured for one historizing node in a bulk insert.
    /// </summary>
    /// <param name="NodeId">The historizing variable.</param>
    /// <param name="Values">Values to insert in source-timestamp order.</param>
    public readonly record struct HistorianDataBatch(
        NodeId NodeId,
        ArrayOf<DataValue> Values);
}
