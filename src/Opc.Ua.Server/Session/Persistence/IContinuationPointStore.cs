// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional mirror for best-effort continuation point envelopes.
    /// </summary>
    /// <remarks>
    /// Continuation points are mirrored as a best-effort envelope. Built-in node managers' opaque
    /// <see cref="ContinuationPoint.Data"/> is not reconstructed on a backup replica, so after failover a client
    /// re-issues Browse or HistoryRead when a mirrored continuation point returns
    /// <see cref="StatusCodes.BadContinuationPointInvalid"/>. This behavior is permitted by OPC UA Part 4 §6.6.2.2,
    /// which requires clients to be prepared for lost continuation points. Node managers may opt in to full
    /// continuation point data serialization by storing reconstructable data through this seam.
    /// </remarks>
    public interface IContinuationPointStore
    {
        /// <summary>
        /// Stores or updates a continuation point envelope.
        /// </summary>
        /// <param name="envelope">The envelope to store.</param>
        void StoreContinuationPoint(ContinuationPointEnvelope envelope);

        /// <summary>
        /// Removes a continuation point envelope.
        /// </summary>
        /// <param name="ownerSessionId">The owning session id.</param>
        /// <param name="kind">The continuation point kind.</param>
        /// <param name="id">The continuation point id.</param>
        void RemoveContinuationPoint(NodeId ownerSessionId, ContinuationPointKind kind, Guid id);

        /// <summary>
        /// Loads the mirrored continuation point envelopes for a restored session.
        /// </summary>
        /// <param name="ownerSessionId">
        /// The session id that owned the continuation points on the active replica.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The mirrored envelopes.</returns>
        ValueTask<ArrayOf<ContinuationPointEnvelope>> LoadContinuationPointsAsync(
            NodeId ownerSessionId,
            CancellationToken cancellationToken = default);
    }
}
