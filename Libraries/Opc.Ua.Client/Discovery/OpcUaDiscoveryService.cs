/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client.Discovery
{
    /// <summary>
    /// Default injectable OPC UA discovery service.
    /// </summary>
    public sealed class OpcUaDiscoveryService : IOpcUaDiscoveryService
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public OpcUaDiscoveryService(
            OpcUaClientOptions options,
            ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            _ = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
            string discoveryUrl,
            ArrayOf<string> serverUris = default,
            CancellationToken ct = default)
        {
            if (discoveryUrl == null)
            {
                throw new ArgumentNullException(nameof(discoveryUrl));
            }

            using DiscoveryClient client = await CreateClientAsync(discoveryUrl, ct).ConfigureAwait(false);
            return await client.FindServersAsync(serverUris, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string discoveryUrl,
            ArrayOf<string> profileUris = default,
            CancellationToken ct = default)
        {
            if (discoveryUrl == null)
            {
                throw new ArgumentNullException(nameof(discoveryUrl));
            }

            using DiscoveryClient client = await CreateClientAsync(discoveryUrl, ct).ConfigureAwait(false);
            return await client.GetEndpointsAsync(profileUris, ct).ConfigureAwait(false);
        }

        private Task<DiscoveryClient> CreateClientAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            ApplicationConfiguration configuration = m_options.Configuration ??
                throw new InvalidOperationException("OpcUaClientOptions.Configuration is required.");
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            return DiscoveryClient.CreateAsync(
                new Uri(discoveryUrl),
                endpointConfiguration,
                configuration,
                ct: ct);
        }

        private readonly OpcUaClientOptions m_options;
    }
}
