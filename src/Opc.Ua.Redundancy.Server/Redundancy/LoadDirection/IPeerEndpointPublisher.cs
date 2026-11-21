// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Publishes the local Server's own <c>EndpointDescription</c>s to the shared store so peers can return them when
    /// they direct a Client to this Server.
    /// </summary>
    public interface IPeerEndpointPublisher
    {
        /// <summary>
        /// Publishes the local Server's endpoints (integrity-protected) under its ServerUri.
        /// </summary>
        /// <param name="endpoints">The endpoints to publish.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask PublishAsync(
            ArrayOf<EndpointDescription> endpoints,
            CancellationToken cancellationToken = default);
    }
}
