/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Converts session history continuations to and from durable envelopes.
    /// </summary>
    internal interface IHistoryContinuationPointCodec
    {
        /// <summary>
        /// Encodes a session-owned history continuation when its state supports persistence.
        /// </summary>
        ValueTask<HistoryContinuationPointEnvelope?> EncodeAsync(
            NodeId ownerSessionId,
            IHistoryContinuationPoint continuationPoint,
            CancellationToken cancellationToken);

        /// <summary>
        /// Restores a history continuation from a supported durable envelope.
        /// </summary>
        ValueTask<IHistoryContinuationPoint?> DecodeAsync(
            HistoryContinuationPointEnvelope envelope,
            CancellationToken cancellationToken);
    }
}
