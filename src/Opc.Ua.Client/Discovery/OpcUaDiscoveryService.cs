// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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

        private async Task<DiscoveryClient> CreateClientAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            ApplicationConfiguration? configurationFromProvider = null;
            if (m_options.ConfigurationProvider != null)
            {
                // Use what the provider returns: discarding it and falling back
                // to m_options.Configuration makes every provider-only setup
                // fail with "Configuration is required".
                configurationFromProvider = await m_options.ConfigurationProvider
                    .GetAsync(ct)
                    .ConfigureAwait(false);
            }
            ApplicationConfiguration configuration =
                configurationFromProvider ?? m_options.Configuration ??
                throw new InvalidOperationException("OpcUaClientOptions.Configuration is required.");
            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            return await DiscoveryClient.CreateAsync(
                new Uri(discoveryUrl),
                endpointConfiguration,
                configuration,
                ct: ct).ConfigureAwait(false);
        }

        private readonly OpcUaClientOptions m_options;
    }
}
