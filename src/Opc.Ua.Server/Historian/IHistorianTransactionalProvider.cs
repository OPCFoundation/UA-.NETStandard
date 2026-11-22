// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that can commit a batch of
    /// history updates atomically (all-or-nothing). The default
    /// <see cref="IHistorianDataProvider"/> contract is per-value
    /// best-effort; providers that offer stronger guarantees implement
    /// this interface for callers that explicitly request atomic updates.
    /// The HistoryUpdate service uses per-value methods regardless of this capability.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method signatures mirror <see cref="IHistorianDataProvider"/>'s
    /// per-value update methods but document an atomic semantics: if any
    /// value cannot be applied, all values are rolled back and the
    /// returned status list contains the per-value failure code; the
    /// archive is left in its pre-call state.
    /// </para>
    /// <para>
    /// Implementations should still return a status for every input
    /// value (so callers can identify which value caused the rollback)
    /// but every successful value gets <see cref="StatusCodes.Good"/>
    /// or <see cref="StatusCodes.GoodEntryInserted"/> /
    /// <see cref="StatusCodes.GoodEntryReplaced"/> only when the entire
    /// batch committed.
    /// </para>
    /// </remarks>
    public interface IHistorianTransactionalProvider
    {
        /// <summary>
        /// Inserts a batch of values atomically.
        /// </summary>
        /// <param name="context">The operation context and default modification metadata.</param>
        /// <param name="nodeId">The historizing variable.</param>
        /// <param name="values">Values to insert atomically, in request order.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// One status per value and a rollback flag; success statuses require the entire batch to commit.
        /// </returns>
        ValueTask<HistorianUpdateOutcome<DataValue>> InsertAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Replaces a batch of values atomically.
        /// </summary>
        /// <param name="context">The operation context and default modification metadata.</param>
        /// <param name="nodeId">The historizing variable.</param>
        /// <param name="values">Replacement values, in request order.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// Per-value statuses, replaced prior values on success, and whether the batch was rolled back.
        /// </returns>
        ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Upserts a batch of values atomically.
        /// </summary>
        /// <param name="context">The operation context and default modification metadata.</param>
        /// <param name="nodeId">The historizing variable.</param>
        /// <param name="values">Values to insert or replace atomically, in request order.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// Per-value statuses, replaced prior values on success, and whether the batch was rolled back.
        /// </returns>
        ValueTask<HistorianUpdateOutcome<DataValue>> UpdateAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct);
    }
}
