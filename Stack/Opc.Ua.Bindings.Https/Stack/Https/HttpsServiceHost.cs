/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;


namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> with
    /// <see cref="ITransportListener"/> interface.
    /// </summary>
    public abstract class HttpsServiceHost : ITransportListenerFactory
    {
        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        public abstract string UriScheme { get; }

        /// <summary>
        /// The method creates a new instance of a <see cref="HttpsTransportListener"/>.
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
            string hostName = hostName = "/Https";

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
                if (!baseAddresses[ii].StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal))
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

                if (uri.Scheme == Utils.UriSchemeHttps)
                {
                    // Can only support one policy with HTTPS
                    // So pick the first policy with security mode sign and encrypt
                    ServerSecurityPolicy bestPolicy = null;
                    foreach (ServerSecurityPolicy policy in securityPolicies)
                    {
                        if (policy.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                        {
                            continue;
                        }

                        bestPolicy = policy;
                        break;
                    }

                    // Pick the first policy from the list if no policies with sign and encrypt defined
                    if (bestPolicy == null)
                    {
                        bestPolicy = securityPolicies[0];
                    }

                    EndpointDescription description = new EndpointDescription();

                    description.EndpointUrl = uri.ToString();
                    description.Server = serverDescription;

                    if (instanceCertificate != null)
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

                    description.SecurityMode = bestPolicy.SecurityMode;
                    description.SecurityPolicyUri = bestPolicy.SecurityPolicyUri;
                    description.SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(bestPolicy.SecurityMode, bestPolicy.SecurityPolicyUri);
                    description.UserIdentityTokens = serverBase.GetUserTokenPolicies(configuration, description);
                    description.TransportProfileUri = Profiles.HttpsBinaryTransport;

                    ITransportListener listener = Create();
                    if (listener != null)
                    {
                        endpoints.Add(description);
                        serverBase.CreateServiceHostEndpoint(uri.Uri, endpoints, endpointConfiguration, listener,
                            configuration.CertificateValidator.GetChannelValidator());
                    }
                    else
                    {
                        Utils.LogError("Failed to create endpoint {0} because the transport profile is unsupported.", uri);
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
