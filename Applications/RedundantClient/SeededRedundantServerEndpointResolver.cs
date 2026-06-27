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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace RedundantClient
{
    /// <summary>
    /// Resolves redundant server application URIs against explicit sample seed discovery URLs.
    /// </summary>
    public sealed class SeededRedundantServerEndpointResolver : IRedundantServerEndpointResolver
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SeededRedundantServerEndpointResolver"/> class.
        /// </summary>
        public SeededRedundantServerEndpointResolver(
            IEnumerable<string> discoveryUrls,
            ITelemetryContext telemetry)
        {
            if (discoveryUrls == null)
            {
                throw new ArgumentNullException(nameof(discoveryUrls));
            }

            m_discoveryUrls = new List<string>(discoveryUrls);
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_fallback = new DefaultRedundantServerEndpointResolver(telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask<ConfiguredEndpoint?> ResolveAsync(
            string serverUri,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(serverUri))
            {
                throw new ArgumentException("Server URI must not be empty.", nameof(serverUri));
            }

            if (currentEndpoint == null)
            {
                throw new ArgumentNullException(nameof(currentEndpoint));
            }

            for (int ii = 0; ii < m_discoveryUrls.Count; ii++)
            {
                ConfiguredEndpoint? endpoint = await ResolveWithDiscoveryUrlAsync(
                    serverUri,
                    currentEndpoint,
                    m_discoveryUrls[ii],
                    ct).ConfigureAwait(false);
                if (endpoint != null)
                {
                    return endpoint;
                }
            }

            return await m_fallback.ResolveAsync(serverUri, currentEndpoint, ct).ConfigureAwait(false);
        }

        private async ValueTask<ConfiguredEndpoint?> ResolveWithDiscoveryUrlAsync(
            string serverUri,
            ConfiguredEndpoint currentEndpoint,
            string discoveryUrl,
            CancellationToken ct)
        {
            Uri discoveryUri = CoreClientUtils.GetDiscoveryUrl(discoveryUrl);
            EndpointConfiguration configuration = currentEndpoint.Configuration
                ?? EndpointConfiguration.Create();

            using DiscoveryClient discoveryClient = await DiscoveryClient.CreateAsync(
                discoveryUri,
                configuration,
                m_telemetry,
                ct: ct).ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await discoveryClient
                .FindServersAsync(new ArrayOf<string>(new[] { serverUri }), ct)
                .ConfigureAwait(false);
            ApplicationDescription? server = FindServer(servers, serverUri);
            if (server == null)
            {
                return null;
            }

            for (int ii = 0; ii < server.DiscoveryUrls.Count; ii++)
            {
                EndpointDescription? endpointDescription = await SelectEndpointAsync(
                    currentEndpoint,
                    server.DiscoveryUrls[ii],
                    ct).ConfigureAwait(false);
                if (endpointDescription != null)
                {
                    endpointDescription.Server = server;
                    return new ConfiguredEndpoint(
                        currentEndpoint.Collection,
                        endpointDescription,
                        currentEndpoint.Configuration);
                }
            }

            return null;
        }

        private async ValueTask<EndpointDescription?> SelectEndpointAsync(
            ConfiguredEndpoint currentEndpoint,
            string discoveryUrl,
            CancellationToken ct)
        {
            Uri discoveryUri = CoreClientUtils.GetDiscoveryUrl(discoveryUrl);
            EndpointConfiguration configuration = currentEndpoint.Configuration
                ?? EndpointConfiguration.Create();

            using DiscoveryClient discoveryClient = await DiscoveryClient.CreateAsync(
                discoveryUri,
                configuration,
                m_telemetry,
                ct: ct).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await discoveryClient
                .GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);

            for (int ii = 0; ii < endpoints.Count; ii++)
            {
                EndpointDescription endpoint = endpoints[ii];
                if (IsSameSecurity(endpoint, currentEndpoint.Description) &&
                    IsSameScheme(endpoint, currentEndpoint.Description))
                {
                    return endpoint;
                }
            }

            bool useSecurity = currentEndpoint.Description.SecurityMode != MessageSecurityMode.None;
            return CoreClientUtils.SelectEndpoint(
                null!,
                discoveryUri,
                endpoints,
                useSecurity,
                m_telemetry);
        }

        private static ApplicationDescription? FindServer(
            ArrayOf<ApplicationDescription> servers,
            string serverUri)
        {
            for (int ii = 0; ii < servers.Count; ii++)
            {
                if (string.Equals(servers[ii].ApplicationUri, serverUri, StringComparison.Ordinal))
                {
                    return servers[ii];
                }
            }

            return null;
        }

        private static bool IsSameScheme(
            EndpointDescription endpoint,
            EndpointDescription currentEndpoint)
        {
            Uri? endpointUrl = Utils.ParseUri(endpoint.EndpointUrl);
            Uri? currentUrl = Utils.ParseUri(currentEndpoint.EndpointUrl);
            return endpointUrl != null &&
                currentUrl != null &&
                string.Equals(endpointUrl.Scheme, currentUrl.Scheme, StringComparison.Ordinal);
        }

        private static bool IsSameSecurity(
            EndpointDescription endpoint,
            EndpointDescription currentEndpoint)
        {
            return endpoint.SecurityMode == currentEndpoint.SecurityMode &&
                string.Equals(
                    endpoint.SecurityPolicyUri,
                    currentEndpoint.SecurityPolicyUri,
                    StringComparison.Ordinal);
        }

        private readonly List<string> m_discoveryUrls;
        private readonly ITelemetryContext m_telemetry;
        private readonly DefaultRedundantServerEndpointResolver m_fallback;
    }
}
