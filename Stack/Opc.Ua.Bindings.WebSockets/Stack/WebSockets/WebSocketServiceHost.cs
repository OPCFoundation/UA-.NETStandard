/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;


namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a new <see cref="WebSocketTransportListener"/> with
    /// <see cref="ITransportListener"/> interface.
    /// </summary>
    public abstract class WebSocketServiceHost : ITransportListenerFactory
    {
        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        public abstract string UriScheme { get; }

        /// <summary>
        /// The method creates a new instance of a <see cref="WebSocketTransportListener"/>.
        /// </summary>
        /// <returns>The transport listener.</returns>
        public abstract ITransportListener Create();

        /// <inheritdoc/>
        /// <summary>
        /// Create a new service host for UA HTTPS.
        /// </summary>

        public List<EndpointDescription> CreateServiceHost(
            ServerBase serverBase,
            IDictionary<string, ServiceHost> hosts,
            ApplicationConfiguration configuration,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            List<ServerSecurityPolicy> securityPolicies,
            X509Certificate2 instanceCertificate,
            X509Certificate2Collection instanceCertificateChain
            )
        {
            // generate a unique host name.
            string hostName = hostName = "/Tcp";

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
                if (!baseAddresses[ii].StartsWith(Utils.UriSchemeOpcWss, StringComparison.Ordinal))
                {
                    continue;
                }

                UriBuilder uri = new UriBuilder(baseAddresses[ii]);

                if (uri.Path[uri.Path.Length - 1] != '/')
                {
                    uri.Path += "/";
                }

                if (String.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    uri.Host = computerName;
                }

                uris.Add(uri.Uri);

                if (uri.Scheme == Utils.UriSchemeOpcWss)
                {
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
                        
                        description.TransportProfileUri = Profiles.WssJsonTransport;

                        bool requireEncryption = ServerBase.RequireEncryption(description);

                        if (requireEncryption)
                        {
                            description.ServerCertificate = instanceCertificate.RawData;

                            // check if complete chain should be sent.
                            if (configuration.SecurityConfiguration.SendCertificateChain && instanceCertificateChain != null && instanceCertificateChain.Count > 0)
                            {
                                List<byte> serverCertificateChain = new List<byte>();

                                for (int i = 0; i < instanceCertificateChain.Count; i++)
                                {
                                    serverCertificateChain.AddRange(instanceCertificateChain[i].RawData);
                                }

                                description.ServerCertificate = serverCertificateChain.ToArray();
                            }
                        }

                        endpoints.Add(description);
                    }

                    ITransportListener listener = Create();
                    if (listener != null)
                    {
                        serverBase.CreateServiceHostEndpoint(uri.Uri, endpoints, endpointConfiguration, listener,
                            configuration.CertificateValidator.GetChannelValidator());
                    }
                    else
                    {
                        Utils.Trace(Utils.TraceMasks.Error, "Failed to create endpoint {0} because the transport profile is unsupported.", uri);
                    }
                }

                // create the host.
                ServiceHost serviceHost = serverBase.CreateServiceHost(serverBase, uris.ToArray());

                hosts[hostName] = serviceHost;
            }

            return endpoints;
        }
    }
}
