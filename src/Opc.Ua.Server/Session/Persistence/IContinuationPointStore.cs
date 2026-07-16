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
