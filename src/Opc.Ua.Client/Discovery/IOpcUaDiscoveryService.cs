// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Discovery
{
    /// <summary>
    /// Injectable OPC UA discovery operations.
    /// </summary>
    public interface IOpcUaDiscoveryService
    {
        /// <summary>
        /// Calls FindServers at <paramref name="discoveryUrl"/>.
        /// </summary>
        ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
            string discoveryUrl,
            ArrayOf<string> serverUris = default,
            CancellationToken ct = default);

        /// <summary>
        /// Calls GetEndpoints at <paramref name="discoveryUrl"/>.
        /// </summary>
        ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string discoveryUrl,
            ArrayOf<string> profileUris = default,
            CancellationToken ct = default);
    }
}
