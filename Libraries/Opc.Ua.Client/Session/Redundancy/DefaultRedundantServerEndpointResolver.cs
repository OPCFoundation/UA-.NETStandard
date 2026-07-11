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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Resolves redundant ServerUris using <c>FindServers</c> and <c>GetEndpoints</c>.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 §6.6.2.4.5.1 requires each Server in a non-transparent <c>RedundantServerSet</c> to provide the
    /// <c>ApplicationDescription</c> for peers through <c>FindServers</c> so Clients can translate ServerUris into
    /// connectable Endpoints.
    /// </remarks>
    public sealed class DefaultRedundantServerEndpointResolver : IRedundantServerEndpointResolver
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultRedundantServerEndpointResolver"/> class.
        /// </summary>
        /// <param name="telemetry">The telemetry context used when creating discovery clients.</param>
        public DefaultRedundantServerEndpointResolver(ITelemetryContext? telemetry = null)
            : this(telemetry, DefaultRedundantServerDiscovery.Instance)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultRedundantServerEndpointResolver"/> class.
        /// </summary>
        /// <param name="telemetry">The telemetry context used when creating discovery clients.</param>
        /// <param name="discovery">The discovery service used to locate redundant server endpoints.</param>
        internal DefaultRedundantServerEndpointResolver(
            ITelemetryContext? telemetry,
            IRedundantServerDiscovery discovery)
        {
            m_telemetry = telemetry ?? AmbientMessageContext.Telemetry!;
            m_discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
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

            if (currentEndpoint is null)
            {
                throw new ArgumentNullException(nameof(currentEndpoint));
            }

            foreach (string discoveryUrl in GetDiscoveryUrls(currentEndpoint))
            {
                ConfiguredEndpoint? endpoint = await ResolveWithDiscoveryUrlAsync(
                    serverUri,
                    currentEndpoint,
                    discoveryUrl,
                    ct).ConfigureAwait(false);
                if (endpoint != null)
                {
                    return endpoint;
                }
            }

            return null;
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

            ArrayOf<ApplicationDescription> servers = await m_discovery
                .FindServersAsync(discoveryUri, configuration, serverUri, m_telemetry, ct)
                .ConfigureAwait(false);

            ApplicationDescription? server = null;
            for (int ii = 0; ii < servers.Count; ii++)
            {
                if (string.Equals(servers[ii].ApplicationUri, serverUri, StringComparison.Ordinal))
                {
                    server = servers[ii];
                    break;
                }
            }

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

            ArrayOf<EndpointDescription> endpoints = await m_discovery
                .GetEndpointsAsync(discoveryUri, configuration, m_telemetry, ct)
                .ConfigureAwait(false);

            EndpointDescription? exactMatch = null;
            for (int ii = 0; ii < endpoints.Count; ii++)
            {
                if (IsSameSecurity(endpoints[ii], currentEndpoint.Description) &&
                    IsSameScheme(endpoints[ii], currentEndpoint.Description))
                {
                    exactMatch = endpoints[ii];
                    break;
                }
            }

            return exactMatch;
        }

        private static IEnumerable<string> GetDiscoveryUrls(ConfiguredEndpoint currentEndpoint)
        {
            ArrayOf<string> discoveryUrls = currentEndpoint.Description.Server?.DiscoveryUrls ?? default;
            if (!discoveryUrls.IsEmpty)
            {
                return ToEnumerable(discoveryUrls);
            }

            string? endpointUrl = currentEndpoint.Description.EndpointUrl;
            return string.IsNullOrEmpty(endpointUrl) ? Array.Empty<string>() : [endpointUrl];
        }

        private static IEnumerable<string> ToEnumerable(ArrayOf<string> values)
        {
            for (int ii = 0; ii < values.Count; ii++)
            {
                yield return values[ii];
            }
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

        private readonly ITelemetryContext m_telemetry;
        private readonly IRedundantServerDiscovery m_discovery;
    }

    /// <summary>
    /// Provides discovery operations used to resolve redundant server endpoint descriptions.
    /// </summary>
    internal interface IRedundantServerDiscovery
    {
        /// <summary>
        /// Finds server application descriptions through the specified discovery endpoint.
        /// </summary>
        /// <param name="discoveryUri">The discovery endpoint URI to query.</param>
        /// <param name="configuration">The endpoint configuration used for the discovery request.</param>
        /// <param name="serverUri">The application URI of the server to locate.</param>
        /// <param name="telemetry">The telemetry context used while creating the discovery client.</param>
        /// <param name="ct">The cancellation token for the asynchronous operation.</param>
        /// <returns>The application descriptions returned by the discovery endpoint.</returns>
        ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
            Uri discoveryUri,
            EndpointConfiguration configuration,
            string serverUri,
            ITelemetryContext telemetry,
            CancellationToken ct);

        /// <summary>
        /// Gets endpoint descriptions from the specified discovery endpoint.
        /// </summary>
        /// <param name="discoveryUri">The discovery endpoint URI to query.</param>
        /// <param name="configuration">The endpoint configuration used for the discovery request.</param>
        /// <param name="telemetry">The telemetry context used while creating the discovery client.</param>
        /// <param name="ct">The cancellation token for the asynchronous operation.</param>
        /// <returns>The endpoint descriptions returned by the discovery endpoint.</returns>
        ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            Uri discoveryUri,
            EndpointConfiguration configuration,
            ITelemetryContext telemetry,
            CancellationToken ct);
    }

    /// <summary>
    /// Default <see cref="IRedundantServerDiscovery"/> implementation backed by <see cref="DiscoveryClient"/>.
    /// </summary>
    internal sealed class DefaultRedundantServerDiscovery : IRedundantServerDiscovery
    {
        /// <summary>
        /// Gets the shared default discovery implementation.
        /// </summary>
        public static DefaultRedundantServerDiscovery Instance { get; } = new();

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
            Uri discoveryUri,
            EndpointConfiguration configuration,
            string serverUri,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            using DiscoveryClient discoveryClient = await DiscoveryClient.CreateAsync(
                discoveryUri,
                configuration,
                telemetry,
                ct: ct).ConfigureAwait(false);

            return await discoveryClient
                .FindServersAsync(new ArrayOf<string>(new[] { serverUri }), ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            Uri discoveryUri,
            EndpointConfiguration configuration,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            using DiscoveryClient discoveryClient = await DiscoveryClient.CreateAsync(
                discoveryUri,
                configuration,
                telemetry,
                ct: ct).ConfigureAwait(false);

            return await discoveryClient.GetEndpointsAsync(default, ct).ConfigureAwait(false);
        }
    }
}
