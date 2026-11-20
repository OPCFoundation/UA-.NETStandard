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
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class StandardServer
    {
        /// <summary>
        /// The discovery services of the server: FindServers and GetEndpoints, and the
        /// translation of the endpoint descriptions for the endpoint URL a client used.
        /// </summary>
        /// <remarks>
        /// Nested in <see cref="StandardServer"/> because it builds on the protected base
        /// address and endpoint translation members of <see cref="ServerBase"/>.
        /// </remarks>
        private sealed class DiscoveryServices
        {
            /// <summary>
            /// Creates the discovery services of a server.
            /// </summary>
            /// <param name="server">The server whose endpoints are discovered.</param>
            public DiscoveryServices(StandardServer server)
            {
                m_server = server;
            }

            /// <summary>
            /// Invokes the FindServers service.
            /// </summary>
            public async ValueTask<FindServersResponse> FindServersAsync(
                RequestHeader? requestHeader,
                string? endpointUrl,
                ArrayOf<string> localeIds,
                ArrayOf<string> serverUris,
                RequestLifetime requestLifetime)
            {
                StandardServer server = m_server;
                List<ApplicationDescription> servers = [];

                server.ValidateRequest(requestHeader);

                await server.m_semaphoreSlim.WaitAsync(requestLifetime.CancellationToken).ConfigureAwait(false);
                try
                {
                    // parse the url provided by the client.
                    IList<BaseAddress> baseAddresses = server.BaseAddresses;

                    Uri? parsedEndpointUrl = Utils.ParseUri(endpointUrl);

                    if (parsedEndpointUrl != null)
                    {
                        baseAddresses = server.FilterByEndpointUrl(parsedEndpointUrl, baseAddresses);
                    }

                    // check if nothing to do.
                    if (baseAddresses.Count == 0)
                    {
                        return new FindServersResponse
                        {
                            ResponseHeader = server.CreateResponse(requestHeader, StatusCodes.Good),
                            Servers = servers
                        };
                    }

                    // build list of unique servers.
                    var uniqueServers = new Dictionary<string, ApplicationDescription>();

                    foreach (EndpointDescription description in server.GetEndpoints())
                    {
                        ApplicationDescription candidate = description.Server;

                        // skip servers that have been processed.
                        if (uniqueServers.ContainsKey(candidate.ApplicationUri!))
                        {
                            continue;
                        }

                        // check client is filtering by server uri.
                        if (serverUris.Count > 0 &&
                            !serverUris.Contains(f => f == candidate.ApplicationUri))
                        {
                            continue;
                        }

                        // localize the application name if requested.
                        LocalizedText applicationName = candidate.ApplicationName;

                        if (localeIds.Count > 0)
                        {
                            applicationName = server.ServerInternal.ResourceManager
                                .Translate(localeIds, applicationName);
                        }

                        // get the application description.
                        ApplicationDescription application = server.TranslateApplicationDescription(
                            parsedEndpointUrl!,
                            candidate,
                            baseAddresses,
                            applicationName);

                        uniqueServers.Add(candidate.ApplicationUri!, application);

                        // add to list of servers to return.
                        servers.Add(application);
                    }

                    AddRedundantServerSetDescriptions(serverUris, uniqueServers, servers);
                }
                finally
                {
                    server.m_semaphoreSlim.Release();
                }

                return new FindServersResponse
                {
                    ResponseHeader = server.CreateResponse(requestHeader, StatusCodes.Good),
                    Servers = servers
                };
            }

            /// <summary>
            /// Invokes the GetEndpoints service.
            /// </summary>
            public async ValueTask<GetEndpointsResponse> GetEndpointsAsync(
                RequestHeader? requestHeader,
                string? endpointUrl,
                ArrayOf<string> localeIds,
                ArrayOf<string> profileUris,
                RequestLifetime requestLifetime)
            {
                StandardServer server = m_server;
                ArrayOf<EndpointDescription> endpoints = default;

                server.ValidateRequest(requestHeader);

                await server.m_semaphoreSlim.WaitAsync(requestLifetime.CancellationToken).ConfigureAwait(false);
                try
                {
                    // filter by profile.
                    IList<BaseAddress> baseAddresses = server.FilterByProfile(profileUris, server.BaseAddresses);

                    // get the descriptions.
                    endpoints = server.GetEndpointDescriptions(endpointUrl, baseAddresses, localeIds);
                }
                finally
                {
                    server.m_semaphoreSlim.Release();
                }

                // consult the optional load-direction seam; when it directs the
                // Client to a peer it replaces the local endpoints (extension
                // beyond OPC 10000-4 §6.6; opt-in and gated).
                IGetEndpointsDirector? director = server.GetEndpointsDirector;
                if (director != null)
                {
                    (bool redirect, ArrayOf<EndpointDescription> directed) = await director
                        .TryGetDirectedEndpointsAsync(endpointUrl, endpoints, requestLifetime.CancellationToken)
                        .ConfigureAwait(false);
                    if (redirect)
                    {
                        endpoints = directed;
                    }
                }

                return new GetEndpointsResponse
                {
                    ResponseHeader = server.CreateResponse(requestHeader, StatusCodes.Good),
                    Endpoints = endpoints
                };
            }

            /// <summary>
            /// Returns the endpoints that match the base address and endpoint url.
            /// </summary>
            public ArrayOf<EndpointDescription> GetEndpointDescriptions(
                string? endpointUrl,
                IList<BaseAddress> baseAddresses,
                ArrayOf<string> localeIds)
            {
                StandardServer server = m_server;
                ArrayOf<EndpointDescription> endpoints = default;

                // parse the url provided by the client.
                Uri? parsedEndpointUrl = Utils.ParseUri(endpointUrl);

                if (parsedEndpointUrl != null)
                {
                    baseAddresses = server.FilterByEndpointUrl(parsedEndpointUrl, baseAddresses);
                }

                // check if nothing to do.
                if (baseAddresses.Count != 0)
                {
                    // localize the application name if requested.
                    LocalizedText applicationName = server.ServerDescription!.ApplicationName;

                    if (localeIds.Count > 0)
                    {
                        applicationName = server.ServerInternal.ResourceManager
                            .Translate(localeIds, applicationName);
                    }

                    // translate the application description.
                    ApplicationDescription application = server.TranslateApplicationDescription(
                        parsedEndpointUrl!,
                        server.ServerDescription,
                        baseAddresses,
                        applicationName);

                    // translate the endpoint descriptions.
                    endpoints = server.TranslateEndpointDescriptions(
                        parsedEndpointUrl!,
                        baseAddresses,
                        server.Endpoints,
                        application);
                }

                return endpoints;
            }

            private void AddRedundantServerSetDescriptions(
                ArrayOf<string> serverUris,
                Dictionary<string, ApplicationDescription> uniqueServers,
                List<ApplicationDescription> servers)
            {
                IRedundantServerSetProvider? provider = m_server.RedundantServerSetProvider;
                if (provider == null)
                {
                    return;
                }

                foreach (ApplicationDescription peer in provider.GetRedundantServerSet())
                {
                    string? applicationUri = peer.ApplicationUri;
                    if (string.IsNullOrEmpty(applicationUri) ||
                        uniqueServers.ContainsKey(applicationUri) ||
                        (serverUris.Count > 0 && !serverUris.Contains(uri => uri == applicationUri)))
                    {
                        continue;
                    }

                    uniqueServers.Add(applicationUri, peer);
                    servers.Add(peer);
                }
            }

            private readonly StandardServer m_server;
        }
    }
}
