// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional store for mirrored continuation-point envelopes.
    /// </summary>
    /// <remarks>
    /// The envelope records the continuation and session identifiers; Browse envelopes also include request
    /// parameters. It does not carry the node manager's <see cref="ContinuationPoint.Data"/>. The built-in Session
    /// store records mirrored identifiers on a backup but cannot reconstruct a Browse or history cursor from this
    /// envelope. If lookup completes with only this envelope, a resume returns
    /// <see cref="StatusCodes.BadContinuationPointInvalid"/>. The client must issue a new Browse or HistoryRead to
    /// continue, as permitted by OPC UA Part 4 §6.6.2.2. A store failure can prevent lookup. Portable historian
    /// continuations use a separate <see cref="IHistoryContinuationPointStore"/>.
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
