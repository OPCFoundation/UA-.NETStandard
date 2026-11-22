// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Serializable envelope for a mirrored continuation point.
    /// </summary>
    /// <remarks>
    /// The built-in Session creates history envelopes with only the continuation identifier, session identifier and
    /// kind; Browse envelopes also include the original request parameters and index. The envelope does not carry
    /// <see cref="ContinuationPoint.Data"/> or reconstruct a cursor on a backup. If lookup completes with only this
    /// envelope, a resume through the built-in Session store returns
    /// <see cref="StatusCodes.BadContinuationPointInvalid"/>. The client must issue a new Browse or HistoryRead to
    /// continue. Portable historian continuations use a separate <see cref="IHistoryContinuationPointStore"/>.
    /// </remarks>
    public sealed class ContinuationPointEnvelope
    {
        /// <summary>
        /// The continuation point id.
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// The session id that owned the continuation point on the active replica.
        /// </summary>
        public NodeId OwnerSessionId { get; init; }

        /// <summary>
        /// The continuation point family.
        /// </summary>
        public ContinuationPointKind Kind { get; init; }

        /// <summary>
        /// The original browse target, when available.
        /// </summary>
        public NodeId BrowseNodeId { get; init; }

        /// <summary>
        /// The browse view, when available.
        /// </summary>
        public ViewDescription? View { get; init; }

        /// <summary>
        /// The requested maximum references per Browse result.
        /// </summary>
        public uint MaxResultsToReturn { get; init; }

        /// <summary>
        /// The requested browse direction.
        /// </summary>
        public BrowseDirection BrowseDirection { get; init; }

        /// <summary>
        /// The requested reference type filter.
        /// </summary>
        public NodeId ReferenceTypeId { get; init; }

        /// <summary>
        /// Whether reference subtypes were included.
        /// </summary>
        public bool IncludeSubtypes { get; init; }

        /// <summary>
        /// The requested node-class mask.
        /// </summary>
        public uint NodeClassMask { get; init; }

        /// <summary>
        /// The requested browse result mask.
        /// </summary>
        public BrowseResultMask ResultMask { get; init; }

        /// <summary>
        /// The index at which the original replica paused.
        /// </summary>
        public int Index { get; init; }
    }
}
