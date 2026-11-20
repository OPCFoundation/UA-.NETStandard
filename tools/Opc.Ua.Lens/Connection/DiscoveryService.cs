/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Connection
{
    /// <summary>
    /// Wraps <see cref="DiscoveryClient.GetEndpointsAsync"/> so the EndpointPicker
    /// dialog can populate its TreeView. The returned endpoints are exactly what
    /// the server advertises — no client-side filtering, no security-level
    /// preference applied.
    /// </summary>
    internal sealed class DiscoveryService
    {
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_log;

        public DiscoveryService(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_log = telemetry.CreateLogger("Discovery");
        }

        public async Task<ArrayOf<EndpointDescription>> DiscoverAsync(
            string discoveryUrl, CancellationToken ct = default)
        {
            ApplicationConfiguration configuration = await AppConfig.BuildAsync(m_telemetry, ct).ConfigureAwait(false);
            try
            {
                Uri uri = new(discoveryUrl);
                var endpointConfiguration = EndpointConfiguration.Create(configuration);
                endpointConfiguration.OperationTimeout = 10_000;

                m_log.DiscoveringEndpoints(discoveryUrl);
                using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                    configuration, uri, endpointConfiguration, ct: ct).ConfigureAwait(false);
                return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
            }
            finally
            {
                if (configuration.CertificateManager is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                }
                else if (configuration.CertificateManager is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }

    internal static partial class DiscoveryServiceLog
    {
        [LoggerMessage(EventId = UaLensEventIds.DiscoveryService + 0, Level = LogLevel.Information,
            Message = "Discovering endpoints at {Url}")]
        public static partial void DiscoveringEndpoints(this ILogger logger, string url);
    }
}
