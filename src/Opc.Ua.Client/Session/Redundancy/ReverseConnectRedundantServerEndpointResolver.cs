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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Resolves redundant ServerUris with <c>GetEndpoints</c> over the reverse
    /// connections the members open to the client.
    /// </summary>
    /// <remarks>
    /// With reverse connect (OPC 10000-6 §7.1.3) the client cannot dial a
    /// member for <c>FindServers</c> or <c>GetEndpoints</c>. This resolver waits
    /// for a connection of the member from the source, calls
    /// <c>GetEndpoints</c> over it and returns the endpoint of the member that
    /// matches the security and transport of the endpoint in use, configured
    /// for reverse connect with the member's ServerUri.
    /// </remarks>
    public sealed class ReverseConnectRedundantServerEndpointResolver : IRedundantServerEndpointResolver
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ReverseConnectRedundantServerEndpointResolver"/> class.
        /// </summary>
        /// <param name="connections">The source of the members' reverse connections.</param>
        /// <param name="telemetry">The telemetry context used when creating discovery clients.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connections"/> is null.</exception>
        public ReverseConnectRedundantServerEndpointResolver(
            IReverseConnectionAvailability connections,
            ITelemetryContext? telemetry = null)
        {
            m_connections = connections ?? throw new ArgumentNullException(nameof(connections));
            m_telemetry = telemetry ?? AmbientMessageContext.Telemetry!;
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentException"><paramref name="serverUri"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="currentEndpoint"/> is null.</exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotConnected"/> when the source reports no available connection of
        /// the member, or the error of the <c>GetEndpoints</c> call.
        /// </exception>
        public async ValueTask<ConfiguredEndpoint?> ResolveAsync(
            string serverUri,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(serverUri))
            {
                throw new ArgumentException("The server URI cannot be null or empty.", nameof(serverUri));
            }
            if (currentEndpoint is null)
            {
                throw new ArgumentNullException(nameof(currentEndpoint));
            }
            ct.ThrowIfCancellationRequested();

            Uri endpointUrl = SelectAvailableEndpointUrl(m_connections, serverUri, currentEndpoint)
                ?? throw NotConnected();
            ITransportWaitingConnection connection = await m_connections
                .WaitForConnectionAsync(endpointUrl, serverUri, ct)
                .ConfigureAwait(false);
            if (connection == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "The reverse connection source returned no connection.");
            }

            ArrayOf<EndpointDescription> endpoints;
            DiscoveryClient? client = null;
            try
            {
                client = await DiscoveryClient.CreateAsync(
                    connection,
                    currentEndpoint.Configuration ?? EndpointConfiguration.Create(),
                    m_telemetry,
                    ct: ct).ConfigureAwait(false);
                endpoints = await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
            }
            finally
            {
                if (client == null)
                {
                    (connection.Handle as IUaSCByteTransport)?.Close();
                }
                client?.Dispose();
            }

            EndpointDescription? match = null;
            for (int ii = 0; ii < endpoints.Count; ii++)
            {
                EndpointDescription candidate = endpoints[ii];
                if (string.Equals(candidate.Server?.ApplicationUri, serverUri, StringComparison.Ordinal) &&
                    candidate.SecurityMode == currentEndpoint.Description.SecurityMode &&
                    string.Equals(
                        candidate.SecurityPolicyUri,
                        currentEndpoint.Description.SecurityPolicyUri,
                        StringComparison.Ordinal) &&
                    HasScheme(candidate.EndpointUrl, currentEndpoint))
                {
                    match = candidate;
                    break;
                }
            }
            if (match == null)
            {
                return null;
            }

            return new ConfiguredEndpoint(currentEndpoint.Collection, match, currentEndpoint.Configuration)
            {
                UpdateBeforeConnect = false,
                ReverseConnect = new ReverseConnectEndpoint { Enabled = true, ServerUri = serverUri }
            };
        }

        /// <summary>
        /// Selects an endpoint url announced by an available connection of
        /// the member with the scheme of the endpoint in use.
        /// </summary>
        internal static Uri? SelectAvailableEndpointUrl(
            IReverseConnectionAvailability connections,
            string serverUri,
            ConfiguredEndpoint currentEndpoint)
        {
            IReadOnlyList<Uri> available = connections.GetAvailableEndpointUrls(serverUri);
            for (int ii = 0; ii < available.Count; ii++)
            {
                if (HasScheme(available[ii].OriginalString, currentEndpoint))
                {
                    return available[ii];
                }
            }
            return null;
        }

        /// <summary>
        /// The error of a member without an available reverse connection.
        /// </summary>
        internal static ServiceResultException NotConnected()
        {
            return new ServiceResultException(
                StatusCodes.BadNotConnected, "The Server holds no reverse connection to the client.");
        }

        private static bool HasScheme(string? endpointUrl, ConfiguredEndpoint currentEndpoint)
        {
            Uri? url = Utils.ParseUri(endpointUrl);
            Uri? current = Utils.ParseUri(currentEndpoint.Description.EndpointUrl);
            return url != null &&
                current != null &&
                string.Equals(url.Scheme, current.Scheme, StringComparison.Ordinal);
        }

        private readonly IReverseConnectionAvailability m_connections;
        private readonly ITelemetryContext m_telemetry;
    }
}
