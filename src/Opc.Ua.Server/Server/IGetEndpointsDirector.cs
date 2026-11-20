// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional seam consulted by <c>GetEndpoints</c> to direct a Client to a different member of a
    /// <c>RedundantServerSet</c> (an extension beyond OPC 10000-4 §6.6). It is opt-in and gated: normal discovery is
    /// unaffected, and the standard client-driven <c>RedundantServerArray</c> / <c>ServiceLevel</c> selection remains
    /// the authoritative Failover mechanism.
    /// </summary>
    public interface IGetEndpointsDirector
    {
        /// <summary>
        /// Decides whether a <c>GetEndpoints</c> request should be answered with a peer Server's endpoints instead of
        /// the local Server's.
        /// </summary>
        /// <param name="endpointUrl">The URL the Client used to reach the discovery endpoint.</param>
        /// <param name="localEndpoints">The endpoints the local Server would otherwise return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <c>Redirect = true</c> and the peer's <c>Endpoints</c> when the request should be directed to a peer;
        /// otherwise <c>Redirect = false</c> and the local Server serves its own endpoints.
        /// </returns>
        ValueTask<(bool Redirect, ArrayOf<EndpointDescription> Endpoints)> TryGetDirectedEndpointsAsync(
            string? endpointUrl,
            ArrayOf<EndpointDescription> localEndpoints,
            CancellationToken cancellationToken = default);
    }
}
