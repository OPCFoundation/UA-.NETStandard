/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Preserves the source timestamp when reading and updating annotations.
    /// </summary>
    public interface IHistorianTimestampedAnnotationProvider
    {
        /// <summary>
        /// Reads annotations while preserving their value source timestamps.
        /// </summary>
        ValueTask<HistorianPage<HistorianAnnotation>> ReadAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            HistorianAnnotationReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);

        /// <summary>
        /// Inserts timestamped annotations.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> InsertAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Replaces timestamped annotations.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> ReplaceAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Updates timestamped annotations.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> UpdateAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Deletes timestamped annotations.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<HistorianAnnotation>> DeleteAnnotationsWithTimestampsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianAnnotation> annotations,
            CancellationToken ct);
    }
}
