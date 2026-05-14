/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Connection;

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
    private ApplicationConfiguration? m_config;

    public DiscoveryService(ITelemetryContext telemetry)
    {
        m_telemetry = telemetry;
        m_log = telemetry.CreateLogger("Discovery");
    }

    public async Task<ArrayOf<EndpointDescription>> DiscoverAsync(
        string discoveryUrl, CancellationToken ct = default)
    {
        m_config ??= await AppConfig.BuildAsync(m_telemetry).ConfigureAwait(false);

        Uri uri = new(discoveryUrl);
        var endpointConfiguration = EndpointConfiguration.Create(m_config);
        endpointConfiguration.OperationTimeout = 10_000;

        m_log.LogInformation("Discovering endpoints at {Url}", discoveryUrl);
        using DiscoveryClient client = await DiscoveryClient.CreateAsync(
            m_config, uri, endpointConfiguration, ct: ct).ConfigureAwait(false);
        return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
    }
}
