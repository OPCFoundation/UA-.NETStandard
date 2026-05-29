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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that support annotations on
    /// historizing variables (Part 11 §5.2.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// In OPC UA, annotations live on the <c>Annotations</c> property of
    /// a historizing variable, addressed via HistoryRead/HistoryUpdate on
    /// the property's NodeId. The framework translates property NodeId →
    /// parent variable NodeId before calling this interface, so provider
    /// implementations only ever see the variable NodeId, not the
    /// Annotations property NodeId.
    /// </para>
    /// <para>
    /// <strong>Update semantics</strong> follow the same patterns as
    /// <see cref="IHistorianDataProvider"/>: per-value best-effort with
    /// <see cref="StatusCodes.BadEntryExists"/> / <see cref="StatusCodes.BadNoEntryExists"/>
    /// signalling. The annotation's <see cref="Annotation.AnnotationTime"/>
    /// is the storage key.
    /// </para>
    /// </remarks>
    public interface IHistorianAnnotationProvider
    {
        /// <summary>
        /// Reads one page of annotations associated with a historizing variable.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised annotation read request.</param>
        /// <param name="resumeToken">Page resume token; empty on first page.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<HistorianPage<Annotation>> ReadAnnotationsAsync(
            HistorianOperationContext context,
            HistorianAnnotationReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);

        /// <summary>
        /// Inserts new annotations.
        /// </summary>
        ValueTask<IList<StatusCode>> InsertAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<Annotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Replaces existing annotations matching their
        /// <see cref="Annotation.AnnotationTime"/>.
        /// </summary>
        ValueTask<IList<StatusCode>> ReplaceAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<Annotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Upserts annotations.
        /// </summary>
        ValueTask<IList<StatusCode>> UpdateAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<Annotation> annotations,
            CancellationToken ct);

        /// <summary>
        /// Deletes annotations at the specified annotation timestamps.
        /// </summary>
        ValueTask<IList<StatusCode>> DeleteAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DateTimeUtc> annotationTimes,
            CancellationToken ct);
    }
}
