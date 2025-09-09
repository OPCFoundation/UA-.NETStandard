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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security;

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

        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>A list of server urls.</returns>
        public static ValueTask<IList<string>> DiscoverServersAsync(
            ApplicationConfiguration configuration,
            CancellationToken ct = default)
        {
            return DiscoverServersAsync(configuration, DefaultDiscoverTimeout, ct);
        }

        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="discoverTimeout">Operation timeout in milliseconds.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>A list of server urls.</returns>
        public static async ValueTask<IList<string>> DiscoverServersAsync(
            ApplicationConfiguration configuration,
            int discoverTimeout,
            CancellationToken ct = default)
        {
            var serverUrls = new List<string>();

            // set a short timeout because this is happening in the drop down event.
            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            endpointConfiguration.OperationTimeout = discoverTimeout;

            // Connect to the local discovery server and find the available servers.
            using (var client = DiscoveryClient.Create(
                    new Uri(Utils.Format(Utils.DiscoveryUrls[0], "localhost")),
                    endpointConfiguration))
            {
                ApplicationDescriptionCollection servers =
                    await client.FindServersAsync(null, ct).ConfigureAwait(false);

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
                        if (discoveryUrl.EndsWith(
                            ConfiguredEndpoint.DiscoverySuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            discoveryUrl =
                                discoveryUrl[..^ConfiguredEndpoint.DiscoverySuffix.Length];
                        }

                        // ensure duplicates do not get added.
                        if (!serverUrls.Exists(serverUrl =>
                                serverUrl.Equals(discoveryUrl, StringComparison.OrdinalIgnoreCase)))
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
        public static ValueTask<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity,
            CancellationToken ct = default)
        {
            return SelectEndpointAsync(
                application,
                connection,
                useSecurity,
                DefaultDiscoverTimeout,
                ct);
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        public static async ValueTask<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity,
            int discoverTimeout,
            CancellationToken ct = default)
        {
            var endpointConfiguration = EndpointConfiguration.Create(application);
            endpointConfiguration.OperationTimeout = discoverTimeout > 0
                ? discoverTimeout
                : DefaultDiscoverTimeout;

            using var client = DiscoveryClient.Create(
                application,
                connection,
                endpointConfiguration);
            var url = new Uri(client.Endpoint.EndpointUrl);
            EndpointDescriptionCollection endpoints =
                await client.GetEndpointsAsync(null, ct).ConfigureAwait(false);
            return SelectEndpoint(application, url, endpoints, useSecurity);
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="application">The application configuration.</param>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="useSecurity">if set to <c>true</c> select an endpoint
        /// that uses security.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>The best available endpoint.</returns>
        public static ValueTask<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity,
            CancellationToken ct = default)
        {
            return SelectEndpointAsync(
                application,
                discoveryUrl,
                useSecurity,
                DefaultDiscoverTimeout,
                ct);
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="application">The application configuration.</param>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="useSecurity">if set to <c>true</c> select an endpoint that uses security.</param>
        /// <param name="discoverTimeout">The timeout for the discover operation.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>The best available endpoint.</returns>
        public static async ValueTask<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity,
            int discoverTimeout,
            CancellationToken ct = default)
        {
            Uri uri = GetDiscoveryUrl(discoveryUrl);
            var endpointConfiguration = EndpointConfiguration.Create(application);
            endpointConfiguration.OperationTimeout = discoverTimeout;

            using var client = DiscoveryClient.Create(application, uri, endpointConfiguration);
            // Connect to the server's discovery endpoint and find the available configuration.
            var url = new Uri(client.Endpoint.EndpointUrl);
            EndpointDescriptionCollection endpoints =
                await client.GetEndpointsAsync(null, ct).ConfigureAwait(false);
            EndpointDescription selectedEndpoint = SelectEndpoint(
                application,
                url,
                endpoints,
                useSecurity);

            Uri endpointUrl = Utils.ParseUri(selectedEndpoint.EndpointUrl);
            if (endpointUrl != null && endpointUrl.Scheme == uri.Scheme)
            {
                var builder = new UriBuilder(endpointUrl)
                {
                    Host = uri.DnsSafeHost,
                    Port = uri.Port
                };
                selectedEndpoint.EndpointUrl = builder.ToString();
            }

            return selectedEndpoint;
        }

        /// <summary>
        /// Select the best supported endpoint from an
        /// EndpointDescriptionCollection, with or without security.
        /// </summary>
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration configuration,
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
                if (endpoint.EndpointUrl.StartsWith(url.Scheme, StringComparison.Ordinal))
                {
                    // check if security was requested.
                    if (useSecurity)
                    {
                        if (endpoint.SecurityMode == MessageSecurityMode.None)
                        {
                            continue;
                        }

                        if (configuration != null)
                        {
                            // skip unsupported security policies
                            if (!configuration.SecurityConfiguration.SupportedSecurityPolicies
                                    .Contains(
                                        endpoint.SecurityPolicyUri))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // skip unsupported security policies, for backward compatibility only
                            // may contain policies for which no certificate is available
                            if (SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri) == null)
                            {
                                continue;
                            }
                        }
                    }
                    else if (endpoint.SecurityMode != MessageSecurityMode.None)
                    {
                        continue;
                    }

                    // pick the first available endpoint by default.
                    selectedEndpoint ??= endpoint;

                    //Select endpoint if it has a higher calculated security level, than the previously selected one
                    if (SecuredApplication.CalculateSecurityLevel(
                        endpoint.SecurityMode,
                        endpoint.SecurityPolicyUri) >
                        SecuredApplication.CalculateSecurityLevel(
                            selectedEndpoint.SecurityMode,
                            selectedEndpoint.SecurityPolicyUri))
                    {
                        selectedEndpoint = endpoint;
                    }
                }
            }

            // pick the first available endpoint by default.
            if (selectedEndpoint == null && endpoints.Count > 0)
            {
                selectedEndpoint = endpoints.FirstOrDefault(e =>
                    e.EndpointUrl?.StartsWith(url.Scheme, StringComparison.Ordinal) == true);
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
            if (Utils.IsUriHttpRelatedScheme(discoveryUrl) &&
                !discoveryUrl.EndsWith(
                    ConfiguredEndpoint.DiscoverySuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                discoveryUrl += ConfiguredEndpoint.DiscoverySuffix;
            }

            // parse the selected URL.
            return new Uri(discoveryUrl);
        }
    }
}
