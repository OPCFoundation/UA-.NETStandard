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
using System.Threading;

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
    /// Successor and restoration copies share their exact source and dependency lease until
    /// the last copy is disposed. Portable native cursors can be durably mirrored, but captured
    /// generation owners and their provider tokens remain process-local.
    /// </para>
    /// </remarks>
    internal sealed class HistorianContinuationState : IHistoryContinuationPoint
    {
        /// <summary>
        /// Creates independent continuation ownership for an initial history page.
        /// </summary>
        public HistorianContinuationState()
        {
            m_ownership = new SharedOwnership();
        }

        /// <summary>
        /// Creates a successor or restoration copy that shares the exact ownership of an existing state.
        /// </summary>
        /// <param name="ownership">The shared ownership whose reference count is incremented for the new copy.</param>
        private HistorianContinuationState(SharedOwnership ownership)
        {
            m_ownership = ownership.AddReference();
        }

        /// <summary>
        /// Identifier of the session continuation point.
        /// </summary>
        public required Guid Id { get; set; }

        /// <summary>
        /// Historian provider that serves the continued read.
        /// </summary>
        public required IHistorianProvider Provider { get; init; }

        /// <summary>
        /// Kind of history read being continued.
        /// </summary>
        public required HistorianReadKind Kind { get; init; }

        /// <summary>
        /// Node identifier bound to this continuation.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the node identifier the client originally addressed, which can differ from <see cref="NodeId"/> when
        /// the read was redirected to an annotation or other related node.
        /// </summary>
        public NodeId OriginNodeId { get; init; }

        /// <summary>
        /// Gets the in-memory node that served the initial page, if any.
        /// </summary>
        public NodeState? SourceNode { get; init; }

        /// <summary>
        /// Gets the process-local ownership record shared by all copies of this continuation.
        /// </summary>
        internal ContinuationPoint Ownership => m_ownership.Point;

        /// <summary>
        /// Gets or sets whether the continuation is currently saved in a session and must not be disposed when the
        /// active request scope ends.
        /// </summary>
        internal bool Saved { get; set; }

        /// <summary>
        /// Gets whether the shared ownership was invalidated, for example because its source was retired.
        /// </summary>
        internal bool IsInvalidated => Volatile.Read(ref m_ownership.Invalidated) != 0;

        /// <summary>
        /// Provider cursor used to request the next page.
        /// </summary>
        public required HistorianResumeToken ResumeToken { get; set; }

        /// <summary>
        /// Original raw history read request, when continuing a raw read.
        /// </summary>
        public HistorianRawReadRequest? RawRequest { get; init; }

        /// <summary>
        /// Original modified history read request, when continuing a modified read.
        /// </summary>
        public HistorianModifiedReadRequest? ModifiedRequest { get; init; }

        /// <summary>
        /// Original aggregate read request, when continuing a processed read.
        /// </summary>
        public HistorianProcessedReadRequest? ProcessedRequest { get; init; }

        /// <summary>
        /// Original requested-time history read request, when continuing an at-time read.
        /// </summary>
        public HistorianAtTimeReadRequest? AtTimeRequest { get; init; }

        /// <summary>
        /// Original annotation read request, when continuing an annotation read.
        /// </summary>
        public HistorianAnnotationReadRequest? AnnotationRequest { get; init; }

        /// <summary>
        /// Original event history read request, when continuing an event read.
        /// </summary>
        public HistorianEventReadRequest? EventRequest { get; init; }

        /// <summary>
        /// Timestamp selection retained from the original read.
        /// </summary>
        public TimestampsToReturn TimestampsToReturn { get; init; }

        /// <summary>
        /// Array index range retained from the original read.
        /// </summary>
        public NumericRange IndexRange { get; init; }

        /// <summary>
        /// Data encoding requested for values returned by the continued read.
        /// </summary>
        public QualifiedName DataEncoding { get; init; } = QualifiedName.Null;

        /// <summary>
        /// Whether the annotation continuation still requires node-identifier normalization.
        /// </summary>
        public bool UsesLegacyAnnotationNodeId { get; init; }

        /// <summary>
        /// Buffered output values for paginated processed reads using
        /// the framework streaming fallback. The first call computes
        /// every aggregate value, returns the first page, and stores
        /// the remainder here for subsequent calls to drain. Null for
        /// every other read kind. Successor and restoration states share
        /// this immutable payload and advance only their own offset.
        /// </summary>
        public HistorianBufferedProcessedPayload? BufferedProcessedOutputs { get; set; }

        /// <summary>
        /// Cursor into <see cref="BufferedProcessedOutputs"/>.
        /// </summary>
        public int BufferedProcessedOffset { get; set; }

        /// <summary>
        /// Creates a continuation with a new identifier and the next provider cursor or buffered offset.
        /// </summary>
        public HistorianContinuationState CreateSuccessor(
            HistorianResumeToken resumeToken,
            int? bufferedProcessedOffset = null)
        {
            return Clone(
                Guid.NewGuid(),
                resumeToken,
                bufferedProcessedOffset ?? BufferedProcessedOffset,
                NodeId,
                UsesLegacyAnnotationNodeId);
        }

        /// <summary>
        /// Copies the current request and cursor while retaining the identifier for restoration.
        /// </summary>
        public HistorianContinuationState CreateRestorationCopy()
        {
            return Clone(
                Id,
                ResumeToken,
                BufferedProcessedOffset,
                NodeId,
                UsesLegacyAnnotationNodeId);
        }

        /// <summary>
        /// Copies a legacy annotation continuation with the resolved annotation property node identifier.
        /// </summary>
        public HistorianContinuationState CreateNormalizedAnnotationState(
            NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException(
                    "The annotation property NodeId must not be null.",
                    nameof(nodeId));
            }
            if (Kind != HistorianReadKind.Annotations ||
                !UsesLegacyAnnotationNodeId)
            {
                throw new InvalidOperationException(
                    "Only legacy annotation continuation state can be normalized.");
            }
            return Clone(
                Id,
                ResumeToken,
                BufferedProcessedOffset,
                nodeId,
                usesLegacyAnnotationNodeId: false);
        }

        /// <summary>
        /// Releases this continuation's reference to buffered processed values.
        /// </summary>
        public void Dispose()
        {
            DisposeCore(null);
        }

        /// <summary>
        /// Disposes this copy unless the continuation is owned by a session other than the attempted owner.
        /// </summary>
        /// <param name="attemptedOwner">The session continuation points that attempts to dispose the state.</param>
        internal void DisposeUnlessOwnedByAnotherSession(ISessionContinuationPoints attemptedOwner)
        {
            DisposeCore(attemptedOwner);
        }

        /// <summary>
        /// Marks the shared ownership as invalidated for all copies of this continuation.
        /// </summary>
        internal void Invalidate()
        {
            Volatile.Write(ref m_ownership.Invalidated, 1);
        }

        /// <summary>
        /// Records the session that owns this continuation and the callback that releases the session slot.
        /// </summary>
        /// <param name="owner">The session continuation points that now own the state.</param>
        /// <param name="releaseOwner">The callback invoked once when the state is disposed.</param>
        internal void SetOwnerRelease(ISessionContinuationPoints owner, Action releaseOwner)
        {
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (releaseOwner is null)
            {
                throw new ArgumentNullException(nameof(releaseOwner));
            }
            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(HistorianContinuationState));
                }
                if (m_ownerRelease is not null)
                {
                    throw new InvalidOperationException("The history continuation already has a session owner.");
                }
                m_sessionOwner = owner;
                m_ownerRelease = releaseOwner;
            }
        }

        /// <summary>
        /// Verifies that the continuation is not owned by a session other than the specified owner.
        /// </summary>
        /// <param name="owner">The session continuation points expected to own the state.</param>
        internal void ValidateSessionOwner(ISessionContinuationPoints owner)
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(HistorianContinuationState));
                }
                if (m_sessionOwner is not null && !ReferenceEquals(owner, m_sessionOwner))
                {
                    throw new InvalidOperationException("The history continuation already has another session owner.");
                }
            }
        }

        /// <summary>
        /// Disposes the state once, releasing buffered values, the shared ownership reference and the session slot
        /// unless another session owns the continuation.
        /// </summary>
        /// <param name="attemptedOwner">The disposing session, or null to dispose unconditionally.</param>
        private void DisposeCore(ISessionContinuationPoints? attemptedOwner)
        {
            Action? releaseOwner;
            lock (m_lock)
            {
                if (m_disposed ||
                    (attemptedOwner is not null && m_sessionOwner is not null &&
                        !ReferenceEquals(attemptedOwner, m_sessionOwner)))
                {
                    return;
                }
                m_disposed = true;
                releaseOwner = m_ownerRelease;
                m_ownerRelease = null;
                m_sessionOwner = null;
                Saved = false;
                BufferedProcessedOutputs = null;
            }
            try
            {
                m_ownership.Dispose();
            }
            finally
            {
                releaseOwner?.Invoke();
            }
        }

        /// <summary>
        /// Disposes a continuation state at the end of a request scope unless it was saved in the session.
        /// </summary>
        /// <param name="state">The state used by the request, or null when the request has no continuation.</param>
        internal sealed class Use(HistorianContinuationState? state) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                if (state is { Saved: false })
                {
                    state.Dispose();
                }
            }
        }

        private HistorianContinuationState Clone(
            Guid id,
            HistorianResumeToken resumeToken,
            int bufferedProcessedOffset,
            NodeId nodeId,
            bool usesLegacyAnnotationNodeId)
        {
            HistorianProcessedReadRequest? processedRequest = ProcessedRequest is null
                ? null
                : ProcessedRequest with
                {
                    Configuration = CoreUtils.Clone(ProcessedRequest.Configuration) ??
                        throw new InvalidOperationException("The processed request configuration could not be cloned.")
                };
            HistorianEventReadRequest? eventRequest = EventRequest is null
                ? null
                : EventRequest with
                {
                    Filter = CoreUtils.Clone(EventRequest.Filter) ??
                        throw new InvalidOperationException("The event request filter could not be cloned.")
                };
            return new HistorianContinuationState(m_ownership)
            {
                Id = id,
                Provider = Provider,
                Kind = Kind,
                NodeId = nodeId,
                OriginNodeId = OriginNodeId,
                SourceNode = SourceNode,
                ResumeToken = resumeToken,
                RawRequest = RawRequest is null ? null : RawRequest with { },
                ModifiedRequest = ModifiedRequest is null
                    ? null
                    : ModifiedRequest with { },
                ProcessedRequest = processedRequest,
                AtTimeRequest = AtTimeRequest is null
                    ? null
                    : AtTimeRequest with { },
                AnnotationRequest = AnnotationRequest is null
                    ? null
                    : AnnotationRequest with { },
                EventRequest = eventRequest,
                TimestampsToReturn = TimestampsToReturn,
                IndexRange = IndexRange,
                DataEncoding = DataEncoding,
                UsesLegacyAnnotationNodeId =
                    usesLegacyAnnotationNodeId,
                BufferedProcessedOutputs = BufferedProcessedOutputs,
                BufferedProcessedOffset = bufferedProcessedOffset
            };
        }

        /// <summary>
        /// Reference-counted holder of the continuation point and invalidation flag shared by all copies of a history
        /// continuation state.
        /// </summary>
        private sealed class SharedOwnership : IDisposable
        {
            /// <summary>
            /// Gets the continuation point that carries the retained manager, dependency owners and routing snapshot.
            /// </summary>
            public ContinuationPoint Point { get; } = new();

            /// <summary>
            /// Non-zero once the shared ownership was invalidated.
            /// </summary>
            public int Invalidated;

            /// <summary>
            /// Adds a reference for another copy of the continuation state.
            /// </summary>
            /// <returns>The same shared ownership instance.</returns>
            public SharedOwnership AddReference()
            {
                while (true)
                {
                    int count = Volatile.Read(ref m_references);
                    if (count == 0)
                    {
                        throw new ObjectDisposedException(nameof(HistorianContinuationState));
                    }
                    if (Interlocked.CompareExchange(ref m_references, count + 1, count) == count)
                    {
                        return this;
                    }
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Decrement(ref m_references) == 0)
                {
                    Point.Dispose();
                }
            }

            private int m_references = 1;
        }

        private readonly SharedOwnership m_ownership;
        private readonly Lock m_lock = new();
        private Action? m_ownerRelease;
        private ISessionContinuationPoints? m_sessionOwner;
        private bool m_disposed;
    }

    /// <summary>
    /// Immutable buffered output shared by processed continuation states.
    /// </summary>
    internal sealed class HistorianBufferedProcessedPayload
    {
        /// <summary>
        /// Initializes the shared payload with the buffered aggregate values.
        /// </summary>
        public HistorianBufferedProcessedPayload(
            ArrayOf<DataValue> values)
        {
            Values = values;
        }

        /// <summary>
        /// Number of buffered aggregate values.
        /// </summary>
        public int Count => Values.Count;

        /// <summary>
        /// Gets the buffered aggregate value at the specified index.
        /// </summary>
        public DataValue this[int index] => Values[index];

        private ArrayOf<DataValue> Values { get; }
    }

    /// <summary>
    /// Kind of paginated read in flight.
    /// </summary>
    internal enum HistorianReadKind
    {
        /// <summary>
        /// Reads recorded historical values.
        /// </summary>
        Raw,

        /// <summary>
        /// Reads historical values with their modification information.
        /// </summary>
        Modified,

        /// <summary>
        /// Reads aggregate values calculated over processing intervals.
        /// </summary>
        Processed,

        /// <summary>
        /// Reads historical values at requested timestamps.
        /// </summary>
        AtTime,

        /// <summary>
        /// Reads annotations associated with historical values.
        /// </summary>
        Annotations,

        /// <summary>
        /// Reads historical events.
        /// </summary>
        Events
    }
}
