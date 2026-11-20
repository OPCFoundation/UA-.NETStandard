// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Resolves the published <c>EndpointDescription</c>s of a peer Server in the <c>RedundantServerSet</c> so the
    /// local Server can return them from a redirected <c>GetEndpoints</c> response.
    /// </summary>
    public interface IPeerEndpointDirectory
    {
        /// <summary>
        /// Returns the peer's published endpoints, or an empty set when none are available (unknown peer, missing,
        /// or a record that failed integrity verification).
        /// </summary>
        /// <param name="serverUri">The peer ServerUri / ApplicationUri.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string serverUri,
            CancellationToken cancellationToken = default);
    }
}
