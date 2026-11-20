/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Serializable envelope for a mirrored continuation point.
    /// </summary>
    /// <remarks>
    /// The envelope mirrors the continuation-point id, owning session id, and re-issuable request parameters.
    /// Generic server mirroring does not reconstruct node-manager-owned opaque <see cref="ContinuationPoint.Data"/>
    /// on a backup replica. After failover, a mirrored-but-unreconstructable continuation point is recognized and
    /// fails gracefully with <see cref="StatusCodes.BadContinuationPointInvalid"/> so the client re-issues Browse or
    /// HistoryRead, as permitted by OPC UA Part 4 §6.6.2.2. Node managers that can serialize their own continuation
    /// data may use this seam to add full-fidelity restoration.
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
