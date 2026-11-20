/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Decides, for a <c>GetEndpoints</c> request handled by the local Server, which member of the
    /// <c>RedundantServerSet</c> a Client should be directed to.
    /// </summary>
    public interface IServerDirectionPolicy
    {
        /// <summary>
        /// Returns the ServerUri of the peer the Client should be directed to, or <c>null</c> when the local Server
        /// should serve the request itself. The decision is: eligibility by health <c>ServiceLevel</c> tier first
        /// (a peer in a strictly higher tier wins), then least load among the top tier (with the local Server
        /// included), with random tie-breaking among equally-loaded members and fail-to-self on a stale/unknown view.
        /// </summary>
        /// <param name="localServerUri">The local ServerUri.</param>
        /// <param name="localServiceLevel">The local health <c>ServiceLevel</c>.</param>
        /// <param name="localLoadWeight">The local load weight.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<string?> SelectTargetServerUriAsync(
            string localServerUri,
            byte localServiceLevel,
            byte localLoadWeight,
            CancellationToken cancellationToken = default);
    }
}
