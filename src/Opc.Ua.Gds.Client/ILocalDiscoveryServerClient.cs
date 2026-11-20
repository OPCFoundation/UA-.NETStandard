// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Public surface of <see cref="LocalDiscoveryServerClient"/>. Exposes the
    /// asynchronous discovery operations a client uses to find servers and
    /// query their endpoints from a Local Discovery Server.
    /// </summary>
    public interface ILocalDiscoveryServerClient : IAsyncDisposable
    {
        /// <summary>The application configuration in use.</summary>
        ApplicationConfiguration ApplicationConfiguration { get; }

        /// <summary>The message context derived from the configuration.</summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>Diagnostics flags requested for every discovery call.</summary>
        DiagnosticsMasks DiagnosticsMasks { get; }

        /// <summary>Locales preferred by the client.</summary>
        ArrayOf<string> PreferredLocales { get; set; }

        /// <summary>Default operation timeout for discovery calls.</summary>
        int DefaultOperationTimeout { get; set; }

        /// <summary>Finds servers known to the LDS.</summary>
        /// <remarks>Invokes the <c>FindServers</c> Discovery service (OPC 10000-4 §5.4.2).</remarks>
        ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(CancellationToken ct = default);

        /// <summary>Finds servers known to the LDS at the supplied endpoint URL.</summary>
        /// <remarks>Invokes the <c>FindServers</c> Discovery service (OPC 10000-4 §5.4.2).</remarks>
        ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
            string? endpointUrl,
            string? endpointTransportProfileUri,
            CancellationToken ct = default);

        /// <summary>Returns the endpoints exposed by the supplied server URL.</summary>
        /// <remarks>Invokes the <c>GetEndpoints</c> Discovery service (OPC 10000-4 §5.4.4).</remarks>
        ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string endpointUrl,
            CancellationToken ct = default);

        /// <summary>Returns the endpoints exposed by the supplied server URL.</summary>
        /// <remarks>Invokes the <c>GetEndpoints</c> Discovery service (OPC 10000-4 §5.4.4).</remarks>
        ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string endpointUrl,
            string? endpointTransportProfileUri,
            CancellationToken ct = default);

        /// <summary>Finds servers exposed via mDNS on the network.</summary>
        /// <remarks>Invokes the <c>FindServersOnNetwork</c> Discovery service (OPC 10000-4 §5.4.3).</remarks>
        ValueTask<(ArrayOf<ServerOnNetwork>, DateTimeUtc lastCounterResetTime)> FindServersOnNetworkAsync(
                uint startingRecordId,
                uint maxRecordsToReturn,
                CancellationToken ct = default);

        /// <summary>Finds servers exposed via mDNS on the network at the supplied endpoint URL.</summary>
        /// <remarks>Invokes the <c>FindServersOnNetwork</c> Discovery service (OPC 10000-4 §5.4.3).</remarks>
        ValueTask<(ArrayOf<ServerOnNetwork>, DateTimeUtc lastCounterResetTime)> FindServersOnNetworkAsync(
                string? endpointUrl,
                string? endpointTransportProfileUri,
                uint startingRecordId,
                uint maxRecordsToReturn,
                ArrayOf<string> serverCapabilityFilters,
                CancellationToken ct = default);
    }
}
