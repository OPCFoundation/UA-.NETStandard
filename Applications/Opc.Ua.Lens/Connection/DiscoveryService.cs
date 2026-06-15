/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
