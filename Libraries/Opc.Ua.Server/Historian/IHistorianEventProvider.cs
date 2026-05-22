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
    /// Opt-in capability for providers that support event-history
    /// reads and updates (Part 11 §5.3). Today the framework provides
    /// the dispatcher seams and select-clause projection; richer
    /// <c>WhereClause</c> evaluation is the provider's responsibility.
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
        ValueTask<HistorianPage<HistorianEventRecord>> ReadEventsAsync(
            HistorianOperationContext context,
            HistorianEventReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);

        /// <summary>
        /// Inserts events into the archive.
        /// </summary>
        ValueTask<IList<StatusCode>> InsertEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct);

        /// <summary>
        /// Replaces existing events identified by their
        /// <see cref="HistorianEventRecord.EventId"/>.
        /// </summary>
        ValueTask<IList<StatusCode>> ReplaceEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct);

        /// <summary>
        /// Upserts events (insert if absent, replace otherwise).
        /// </summary>
        ValueTask<IList<StatusCode>> UpdateEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct);

        /// <summary>
        /// Deletes events by <see cref="HistorianEventRecord.EventId"/>.
        /// </summary>
        ValueTask<IList<StatusCode>> DeleteEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<ByteString> eventIds,
            CancellationToken ct);
    }
}

