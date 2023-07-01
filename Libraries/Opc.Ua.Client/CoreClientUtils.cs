/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Defines numerous re-useable utility functions for clients.
    /// </summary>
    public static partial class CoreClientUtils
    {
        /// <summary>
        /// The default discover operation timeout.
        /// </summary>
        public static readonly int DefaultDiscoverTimeout = 15000;

        #region Discovery
        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns>A list of server urls.</returns>
        public static IList<string> DiscoverServers(ApplicationConfiguration configuration)
        {
            return DiscoverServers(configuration, DefaultDiscoverTimeout);
        }

        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="discoverTimeout">Operation timeout in milliseconds.</param>
        /// <returns>A list of server urls.</returns>
        public static IList<string> DiscoverServers(
            ApplicationConfiguration configuration,
            int discoverTimeout
            )
        {
            List<string> serverUrls = new List<string>();

            // set a short timeout because this is happening in the drop down event.
            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            endpointConfiguration.OperationTimeout = discoverTimeout;

            // Connect to the local discovery server and find the available servers.
            using (DiscoveryClient client = DiscoveryClient.Create(new Uri(String.Format(Utils.DiscoveryUrls[0], "localhost")), endpointConfiguration))
            {
                ApplicationDescriptionCollection servers = client.FindServers(null);

                // populate the drop down list with the discovery URLs for the available servers.
                for (int ii = 0; ii < servers.Count; ii++)
                {
                    if (servers[ii].ApplicationType == ApplicationType.DiscoveryServer)
                    {
                        continue;
                    }

                    for (int jj = 0; jj < servers[ii].DiscoveryUrls.Count; jj++)
                    {
                        string discoveryUrl = servers[ii].DiscoveryUrls[jj];

                        // Many servers will use the '/discovery' suffix for the discovery endpoint.
                        // The URL without this prefix should be the base URL for the server. 
                        if (discoveryUrl.EndsWith("/discovery"))
                        {
                            discoveryUrl = discoveryUrl.Substring(0, discoveryUrl.Length - "/discovery".Length);
                        }

                        // ensure duplicates do not get added.
                        if (!serverUrls.Contains(discoveryUrl))
                        {
                            serverUrls.Add(discoveryUrl);
                        }
                    }
                }
            }

            return serverUrls;
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="useSecurity">if set to <c>true</c> select an endpoint that uses security.</param>
        /// <returns>The best available endpoint.</returns>
        public static EndpointDescription SelectEndpoint(string discoveryUrl, bool useSecurity)
        {
            return SelectEndpoint(discoveryUrl, useSecurity, DefaultDiscoverTimeout);
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="useSecurity">if set to <c>true</c> select an endpoint that uses security.</param>
        /// <param name="discoverTimeout">Operation timeout in milliseconds.</param>
        /// <returns>The best available endpoint.</returns>
        public static EndpointDescription SelectEndpoint(
            string discoveryUrl,
            bool useSecurity,
            int discoverTimeout
            )
        {
            var url = GetDiscoveryUrl(discoveryUrl);
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = discoverTimeout;

            // Connect to the server's discovery endpoint and find the available configuration.
            using (var client = DiscoveryClient.Create(url, endpointConfiguration))
            {
                var endpoints = client.GetEndpoints(null);
                return SelectEndpoint(url, endpoints, useSecurity);
            }
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity
            )
        {
            return SelectEndpoint(application, connection, useSecurity, DefaultDiscoverTimeout);
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity,
            int discoverTimeout
            )
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = discoverTimeout > 0 ? discoverTimeout : DefaultDiscoverTimeout;

            using (DiscoveryClient client = DiscoveryClient.Create(application, connection, endpointConfiguration))
            {
                var url = new Uri(client.Endpoint.EndpointUrl);
                var endpoints = client.GetEndpoints(null);
                return SelectEndpoint(url, endpoints, useSecurity);
            }
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="application">The application configuration.</param>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="useSecurity">if set to <c>true</c> select an endpoint that uses security.</param>
        /// <returns>The best available endpoint.</returns>
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity)
        {
            return SelectEndpoint(application, discoveryUrl, useSecurity, DefaultDiscoverTimeout);
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="application">The application configuration.</param>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="useSecurity">if set to <c>true</c> select an endpoint that uses security.</param>
        /// <param name="discoverTimeout">The timeout for the discover operation.</param>
        /// <returns>The best available endpoint.</returns>
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity,
            int discoverTimeout
            )
        {
            var uri = GetDiscoveryUrl(discoveryUrl);
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = discoverTimeout;

            using (var client = DiscoveryClient.Create(application, uri, endpointConfiguration))
            {
                // Connect to the server's discovery endpoint and find the available configuration.
                Uri url = new Uri(client.Endpoint.EndpointUrl);
                var endpoints = client.GetEndpoints(null);
                var selectedEndpoint = SelectEndpoint(url, endpoints, useSecurity);

                Uri endpointUrl = Utils.ParseUri(selectedEndpoint.EndpointUrl);
                if (endpointUrl != null && endpointUrl.Scheme == uri.Scheme)
                {
                    UriBuilder builder = new UriBuilder(endpointUrl);
                    builder.Host = uri.DnsSafeHost;
                    builder.Port = uri.Port;
                    selectedEndpoint.EndpointUrl = builder.ToString();
                }

                return selectedEndpoint;
            }
        }

        /// <summary>
        /// Select the best supported endpoint from an
        /// EndpointDescriptionCollection, with or without security.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="endpoints"></param>
        /// <param name="useSecurity"></param>
        public static EndpointDescription SelectEndpoint(
            Uri url,
            EndpointDescriptionCollection endpoints,
            bool useSecurity)
        {
            EndpointDescription selectedEndpoint = null;

            // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
            for (int ii = 0; ii < endpoints.Count; ii++)
            {
                EndpointDescription endpoint = endpoints[ii];

                // check for a match on the URL scheme.
                if (endpoint.EndpointUrl.StartsWith(url.Scheme))
                {
                    // check if security was requested.
                    if (useSecurity)
                    {
                        if (endpoint.SecurityMode == MessageSecurityMode.None)
                        {
                            continue;
                        }

                        // skip unsupported security policies
                        if (SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri) == null)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (endpoint.SecurityMode != MessageSecurityMode.None)
                        {
                            continue;
                        }
                    }

                    // pick the first available endpoint by default.
                    if (selectedEndpoint == null)
                    {
                        selectedEndpoint = endpoint;
                    }

                    // The security level is a relative measure assigned by the server to the 
                    // endpoints that it returns. Clients should always pick the highest level
                    // unless they have a reason not too.
                    // Some servers however, mess this up a bit. So prefer a higher SecurityMode
                    // over the SecurityLevel.
                    if (endpoint.SecurityMode > selectedEndpoint.SecurityMode
                        || (endpoint.SecurityMode == selectedEndpoint.SecurityMode
                            && endpoint.SecurityLevel > selectedEndpoint.SecurityLevel))
                    {
                        selectedEndpoint = endpoint;
                    }
                }
            }

            // pick the first available endpoint by default.
            if (selectedEndpoint == null && endpoints.Count > 0)
            {
                selectedEndpoint = endpoints.FirstOrDefault(e => e.EndpointUrl?.StartsWith(url.Scheme) == true);
            }

            // return the selected endpoint.
            return selectedEndpoint;
        }

        /// <summary>
        /// Convert the discoveryUrl to a Uri and modify endpoint as per connection scheme if required.
        /// </summary>
        public static Uri GetDiscoveryUrl(string discoveryUrl)
        {
            // needs to add the '/discovery' back onto non-UA TCP URLs.
            if (discoveryUrl.StartsWith(Utils.UriSchemeHttp, StringComparison.Ordinal))
            {
                if (!discoveryUrl.EndsWith("/discovery", StringComparison.OrdinalIgnoreCase))
                {
                    discoveryUrl += "/discovery";
                }
            }

            // parse the selected URL.
            return new Uri(discoveryUrl);
        }
        #endregion
    }
}
