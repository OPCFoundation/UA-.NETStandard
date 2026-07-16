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
    /// Opt-in capability interface for historian providers that support
    /// raw data reads (Part 11 §5.2.4) and the Part 11 §6.8 update services
    /// for raw values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Read pagination</strong>. The framework calls
    /// <see cref="ReadRawAsync"/> with a <see cref="HistorianResumeToken"/>
    /// that is empty on the initial page; subsequent pages pass the token
    /// returned by the previous call until the provider returns
    /// <see cref="HistorianPage{T}.IsFinal"/> = <c>true</c>.
    /// </para>
    /// <para>
    /// <strong>Update semantics</strong> (Part 11 §6.8). Per-value
    /// best-effort semantics: the provider returns a status code for every
    /// input value, never throws for individual failures.
    /// <list type="bullet">
    ///   <item><c>Insert</c> fails with <see cref="StatusCodes.BadEntryExists"/> when a
    ///     value already exists at <c>SourceTimestamp</c>.</item>
    ///   <item><c>Replace</c> fails with <see cref="StatusCodes.BadNoEntryExists"/> when
    ///     no value exists at <c>SourceTimestamp</c>.</item>
    ///   <item><c>Update</c> performs upsert semantics — replaces if exists,
    ///     inserts otherwise.</item>
    ///   <item>Replaced values are retained in the modified-history log
    ///     when the provider also implements
    ///     <see cref="IHistorianModifiedProvider"/>.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IHistorianDataProvider
    {
        /// <summary>
        /// Reads one page of raw values from the archive.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised raw read request.</param>
        /// <param name="resumeToken">
        /// Empty on the first page; on subsequent pages it is the token
        /// returned by the previous call.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
            HistorianOperationContext context,
            HistorianRawReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);

        /// <summary>
        /// Inserts new values; fails per-value with
        /// <see cref="StatusCodes.BadEntryExists"/> when a value already
        /// exists at the value's <c>SourceTimestamp</c>.
        /// </summary>
        ValueTask<IList<StatusCode>> InsertAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Replaces existing values; fails per-value with
        /// <see cref="StatusCodes.BadNoEntryExists"/> when no value exists
        /// at the value's <c>SourceTimestamp</c>.
        /// </summary>
        ValueTask<IList<StatusCode>> ReplaceAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Upsert — insert when absent, replace otherwise.
        /// </summary>
        ValueTask<IList<StatusCode>> UpdateAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Deletes raw values whose <c>SourceTimestamp</c> falls in the
        /// half-open interval <c>[startTime, endTime)</c>.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="nodeId">The historizing variable.</param>
        /// <param name="startTime">Inclusive lower bound.</param>
        /// <param name="endTime">Exclusive upper bound.</param>
        /// <param name="isDeleteModified">
        /// When true, only modified-history entries (replaced/deleted
        /// versions) are deleted; the live raw value at each timestamp
        /// is preserved. When false, the live raw values in range are
        /// deleted (a modification entry is logged when the provider
        /// also implements <see cref="IHistorianModifiedProvider"/>).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<StatusCode> DeleteRawAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            bool isDeleteModified,
            CancellationToken ct);

        /// <summary>
        /// Deletes raw values at the specified source timestamps; fails
        /// per-value with <see cref="StatusCodes.BadNoEntryExists"/> when
        /// no value exists at the requested timestamp.
        /// </summary>
        ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DateTimeUtc> timestamps,
            CancellationToken ct);
    }
}
