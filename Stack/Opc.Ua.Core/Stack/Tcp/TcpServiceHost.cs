/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

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
        public abstract ITransportListener Create();

        /// <inheritdoc/>
        /// <summary>
        /// Create a new service host for UA TCP.
        /// </summary>
        public List<EndpointDescription> CreateServiceHost(
            ServerBase serverBase,
            IDictionary<string, Task> hosts,
            ApplicationConfiguration configuration,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            List<ServerSecurityPolicy> securityPolicies,
            X509Certificate2 instanceCertificate,
            X509Certificate2Collection instanceCertificateChain)
        {
            // generate a unique host name.
            string hostName = String.Empty;

            if (hosts.ContainsKey(hostName))
            {
                hostName = "/Tcp";
            }

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", hosts.Count);
            }

            // build list of uris.
            List<Uri> uris = new List<Uri>();
            EndpointDescriptionCollection endpoints = new EndpointDescriptionCollection();

            // create the endpoint configuration to use.
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            string computerName = Utils.GetHostName();

            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                // UA TCP and HTTPS endpoints support multiple policies.
                if (!baseAddresses[ii].StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    continue;
                }

                UriBuilder uri = new UriBuilder(baseAddresses[ii]);

                if (String.Compare(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    uri.Host = computerName;
                }

                ITransportListener listener = this.Create();
                if (listener != null)
                {
                    EndpointDescriptionCollection listenerEndpoints = new EndpointDescriptionCollection();
                    uris.Add(uri.Uri);

                    foreach (ServerSecurityPolicy policy in securityPolicies)
                    {
                        // create the endpoint description.
                        EndpointDescription description = new EndpointDescription();

                        description.EndpointUrl = uri.ToString();
                        description.Server = serverDescription;

                        description.SecurityMode = policy.SecurityMode;
                        description.SecurityPolicyUri = policy.SecurityPolicyUri;
                        description.SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(policy.SecurityMode, policy.SecurityPolicyUri);
                        description.UserIdentityTokens = serverBase.GetUserTokenPolicies(configuration, description);
                        description.TransportProfileUri = Profiles.UaTcpTransport;

                        bool requireEncryption = ServerBase.RequireEncryption(description);

                        if (requireEncryption)
                        {
                            description.ServerCertificate = instanceCertificate.RawData;

                            // check if complete chain should be sent.
                            if (configuration.SecurityConfiguration.SendCertificateChain &&
                                instanceCertificateChain != null &&
                                instanceCertificateChain.Count > 0)
                            {
                                List<byte> serverCertificateChain = new List<byte>();

                                for (int i = 0; i < instanceCertificateChain.Count; i++)
                                {
                                    serverCertificateChain.AddRange(instanceCertificateChain[i].RawData);
                                }

                                description.ServerCertificate = serverCertificateChain.ToArray();
                            }
                        }

                        listenerEndpoints.Add(description);
                    }

                    serverBase.CreateServiceHostEndpoint(uri.Uri, listenerEndpoints, endpointConfiguration, listener,
                        configuration.CertificateValidator.GetChannelValidator()
                        );

                    endpoints.AddRange(listenerEndpoints);
                }
                else
                {
                    Utils.Trace(Utils.TraceMasks.Error, "Failed to create endpoint {0} because the transport profile is unsupported.", uri);
                }
            }

            return endpoints;
        }
    }
}
