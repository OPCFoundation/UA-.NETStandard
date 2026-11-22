// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Resolves a redundant Server application URI to a connectable Endpoint.
    /// </summary>
    /// <remarks>
    /// See OPC 10000-4 §6.6.2.4.5.1 for non-transparent <c>RedundantServerSet</c> peer resolution via
    /// <c>FindServers</c>.
    /// </remarks>
    public interface IRedundantServerEndpointResolver
    {
        /// <summary>
        /// Resolves a redundant server URI to a configured endpoint.
        /// </summary>
        /// <param name="serverUri">The redundant server application URI.</param>
        /// <param name="currentEndpoint">The currently connected endpoint.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resolved endpoint, or <see langword="null"/> if none is available.</returns>
        ValueTask<ConfiguredEndpoint?> ResolveAsync(
            string serverUri,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct = default);
    }
}
