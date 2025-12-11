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
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a new Tcp service host.
    /// </summary>
    /// <remarks>
    /// This class can be used by a transport which
    /// implements the <see cref="ITransportListenerFactory"/>
    /// </remarks>
    public abstract class TcpServiceHost : ITransportListenerFactory
    {
        /// <inheritdoc/>
        public abstract string UriScheme { get; }

        /// <inheritdoc/>
        public abstract ITransportListener Create(ITelemetryContext telemetry);

        /// <summary>
        /// Create a new service host for UA TCP.
        /// </summary>
        public List<EndpointDescription> CreateServiceHost(
            ServerBase serverBase,
            IDictionary<string, ServiceHost> hosts,
            ApplicationConfiguration configuration,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            List<ServerSecurityPolicy> securityPolicies,
            CertificateTypesProvider instanceCertificateTypesProvider)
        {
            // generate a unique host name.
            string hostName = "/Tcp";

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", hosts.Count);
            }

            // build list of uris.
            var uris = new List<Uri>();
            var endpoints = new EndpointDescriptionCollection();

            // create the endpoint configuration to use.
            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            string computerName = Utils.GetHostName();

            // create intermediate logger for just this call.
            // This is needed because the binding always requires a default
            // constructor construction. So the telemetry context is not available
            // until we are here.
            ILogger logger = serverBase.MessageContext.Telemetry.CreateLogger<TcpServiceHost>();

            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                // UA TCP and HTTPS endpoints support multiple policies.
                if (!baseAddresses[ii].StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    continue;
                }

                var uri = new UriBuilder(baseAddresses[ii]);

                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    uri.Host = computerName;
                }

                _ = instanceCertificateTypesProvider.SendCertificateChain;
                ITransportListener listener = Create(serverBase.MessageContext.Telemetry);
                if (listener != null)
                {
                    var listenerEndpoints = new EndpointDescriptionCollection();
                    uris.Add(uri.Uri);

                    foreach (ServerSecurityPolicy policy in securityPolicies)
                    {
                        // create the endpoint description.
                        var description = new EndpointDescription
                        {
                            EndpointUrl = uri.ToString(),
                            Server = serverDescription,
                            TransportProfileUri = Profiles.UaTcpTransport,
                            SecurityMode = policy.SecurityMode,
                            SecurityPolicyUri = policy.SecurityPolicyUri,
                            SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(
                                policy.SecurityMode,
                                policy.SecurityPolicyUri,
                                logger)
                        };
                        description.UserIdentityTokens = serverBase.GetUserTokenPolicies(
                            configuration,
                            description);

                        ServerBase.SetServerCertificateInEndpointDescription(
                            description,
                            instanceCertificateTypesProvider);

                        listenerEndpoints.Add(description);
                    }

                    serverBase.CreateServiceHostEndpoint(
                        uri.Uri,
                        listenerEndpoints,
                        endpointConfiguration,
                        listener,
                        configuration.CertificateValidator.GetChannelValidator());

                    endpoints.AddRange(listenerEndpoints);
                }
                else
                {
                    logger.LogError(
                        "Failed to create endpoint {Uri} because the transport profile is unsupported.",
                        Redaction.Redact.Create(uri));
                }
            }

            hosts[hostName] = serverBase.CreateServiceHost(serverBase, [.. uris]);
            return endpoints;
        }
    }
}
