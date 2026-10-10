/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
