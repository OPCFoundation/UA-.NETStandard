/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Preserves the source timestamp when reading and updating annotations.
    /// </summary>
    /// <remarks>
    /// An annotation is identified by its value's source timestamp and its payload's annotation time.
    /// Distinct source timestamps may share an annotation time. Updates must preserve the size and order
    /// of the input status list, including <see cref="StatusCodes.BadInvalidArgument"/> for invalid placeholders.
    /// </remarks>
    public interface IHistorianTimestampedAnnotationProvider
    {
        /// <summary>
        /// Reads annotations while preserving their value source timestamps.
        /// </summary>
        /// <param name="context">The historian operation context.</param>
        /// <param name="request">The source-time window, direction, client quota, and server page limit.</param>
        /// <param name="resumeToken">Empty initially; otherwise the token returned by the previous page.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// A page ordered by source timestamp and then annotation time, reversed for backward reads.
        /// A nonempty token carries the exclusive composite position and any remaining open-ended quota.
        /// </returns>
        ValueTask<HistorianPage<HistorianAnnotation>> ReadAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            HistorianAnnotationReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);

        /// <summary>
        /// Inserts annotations whose composite identities are not already present.
        /// </summary>
        /// <param name="context">The historian operation context.</param>
        /// <param name="nodeId">The node whose annotation history is updated.</param>
        /// <param name="annotations">The timestamped annotations to insert.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// One status per input annotation, in request order; duplicate identities return BadEntryExists.
        /// </returns>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> InsertAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Replaces annotations identified by both timestamps without moving them to another value.
        /// </summary>
        /// <param name="context">The historian operation context.</param>
        /// <param name="nodeId">The node whose annotation history is updated.</param>
        /// <param name="annotations">The timestamped annotations to replace.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// One status per input and the replaced prior annotations.
        /// Absent identities return BadNoEntryExists.
        /// </returns>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> ReplaceAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Inserts or replaces annotations according to their composite identities.
        /// </summary>
        /// <param name="context">The historian operation context.</param>
        /// <param name="nodeId">The node whose annotation history is updated.</param>
        /// <param name="annotations">The timestamped annotations to update.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// One status per input annotation, in request order, and prior annotations for successful replacements.
        /// </returns>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> UpdateAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Deletes annotations identified by both timestamps; message and user name do not select the target.
        /// </summary>
        /// <param name="context">The historian operation context.</param>
        /// <param name="nodeId">The node whose annotation history is updated.</param>
        /// <param name="annotations">The timestamped annotations to delete.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// One status per input annotation and the deleted annotations; absent identities return BadNoEntryExists.
        /// </returns>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> DeleteAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);
    }
}
