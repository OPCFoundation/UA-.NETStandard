/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that support event-history
    /// reads and updates (Part 11 §5.3). The dispatcher projects select
    /// clauses and evaluates the stored request's <c>WhereClause</c>.
    /// Providers may also evaluate that filter to reduce fetched records.
    /// </summary>
    public interface IHistorianEventProvider
    {
        /// <summary>
        /// Reads one page of historical events for the supplied notifier.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised event read request.</param>
        /// <param name="resumeToken">Resume token from the previous page; empty on first call.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A page of event records in the requested direction, with a token when more records remain.
        /// </returns>
        ValueTask<HistorianPage<HistorianEventRecord>> ReadEventsAsync(
            HistorianOperationContext context,
            HistorianEventReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);

        /// <summary>
        /// Inserts events into the archive.
        /// </summary>
        /// <param name="context">The operation context used for the update.</param>
        /// <param name="nodeId">The historizing notifier.</param>
        /// <param name="events">Event records to insert, in request order.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>One insertion status per event, in request order.</returns>
        ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> InsertEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct);

        /// <summary>
        /// Replaces existing events identified by their
        /// <see cref="HistorianEventRecord.EventId"/>.
        /// </summary>
        /// <param name="context">The operation context used for the update.</param>
        /// <param name="nodeId">The historizing notifier.</param>
        /// <param name="events">Replacement event records, in request order.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>One status per event and the successfully replaced prior records.</returns>
        ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> ReplaceEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct);

        /// <summary>
        /// Upserts events (insert if absent, replace otherwise).
        /// </summary>
        /// <param name="context">The operation context used for the update.</param>
        /// <param name="nodeId">The historizing notifier.</param>
        /// <param name="events">Event records to insert or replace, in request order.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>One status per event and prior records for successful replacements.</returns>
        ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> UpdateEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct);

        /// <summary>
        /// Deletes events by <see cref="HistorianEventRecord.EventId"/>.
        /// </summary>
        /// <param name="context">The operation context used for the deletion.</param>
        /// <param name="nodeId">The historizing notifier.</param>
        /// <param name="eventIds">Identifiers of the events to delete, in request order.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>One status per requested identifier and the successfully deleted records.</returns>
        ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> DeleteEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<ByteString> eventIds,
            CancellationToken ct);
    }
}
