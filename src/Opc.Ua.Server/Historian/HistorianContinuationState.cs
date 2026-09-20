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

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Continuation-point state retained by the dispatcher between
    /// HistoryRead pages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dispatcher retains one instance per outstanding paginated
    /// read in the session's continuation-point cache
    /// (<see cref="ISessionContinuationPoints.SaveHistory"/>).
    /// On the next page request the dispatcher restores the state
    /// (which checks it out without releasing its generation owners), calls the same
    /// provider with the saved <see cref="ResumeToken"/>, and either
    /// retires the continuation (final page → <see cref="Dispose"/>) or
    /// re-saves it with the new resume token under a fresh <see cref="Id"/>.
    /// This makes every issued continuation point single-use.
    /// </para>
    /// <para>
    /// State held here must stay small — it is stored verbatim in the
    /// session for as long as the client keeps the continuation point
    /// active. The session disposes available entries on eviction or close. Checked-out
    /// entries remain owned until their request completes, fails, or is cancelled.
    /// Disposal releases the exact source and dependency owners through the existing
    /// continuation ownership mechanism. Mirrored envelopes do not reconstruct this
    /// process-local state or its opaque provider token.
    /// </para>
    /// </remarks>
    internal sealed class HistorianContinuationState : IHistoryContinuationPoint
    {
        public required Guid Id { get; set; }

        public required IHistorianProvider Provider { get; init; }

        public required HistorianReadKind Kind { get; init; }

        public required NodeId NodeId { get; init; }

        public NodeId OriginNodeId { get; init; }

        public NodeState? SourceNode { get; init; }

        internal ContinuationPoint Ownership { get; } = new();

        internal bool Saved { get; set; }

        public required HistorianResumeToken ResumeToken { get; set; }

        public HistorianRawReadRequest? RawRequest { get; init; }

        public HistorianModifiedReadRequest? ModifiedRequest { get; init; }

        public HistorianProcessedReadRequest? ProcessedRequest { get; init; }

        public HistorianAtTimeReadRequest? AtTimeRequest { get; init; }

        public HistorianAnnotationReadRequest? AnnotationRequest { get; init; }

        public HistorianEventReadRequest? EventRequest { get; init; }

        public TimestampsToReturn TimestampsToReturn { get; init; }

        public NumericRange IndexRange { get; init; }

        public QualifiedName DataEncoding { get; init; } = QualifiedName.Null;

        /// <summary>
        /// Buffered output values for paginated processed reads using
        /// the framework streaming fallback. The first call computes
        /// every aggregate value, returns the first page, and stores
        /// the remainder here for subsequent calls to drain. Null for
        /// every other read kind.
        /// </summary>
        public List<DataValue>? BufferedProcessedOutputs { get; set; }

        /// <summary>
        /// Cursor into <see cref="BufferedProcessedOutputs"/>.
        /// </summary>
        public int BufferedProcessedOffset { get; set; }

        public void Dispose()
        {
            try
            {
                BufferedProcessedOutputs = null;
            }
            finally
            {
                Ownership.Dispose();
            }
        }

        internal sealed class Use(HistorianContinuationState? state) : IDisposable
        {
            public void Dispose()
            {
                if (state is { Saved: false })
                {
                    state.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Kind of paginated read in flight.
    /// </summary>
    internal enum HistorianReadKind
    {
        Raw,
        Modified,
        Processed,
        AtTime,
        Annotations,
        Events
    }
}
