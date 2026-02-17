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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

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
        public abstract ITransportListener Create(ITelemetryContext telemetry);

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
            CertificateTypesProvider instanceCertificateTypesProvider)
        {
            // generate a unique host name.
            string hostName = hostName = "/Https";

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
            ILogger logger = serverBase.MessageContext.Telemetry.CreateLogger<HttpsServiceHost>();

            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                if (!Utils.IsUriHttpsScheme(baseAddresses[ii]))
                {
                    continue;
                }

                if (!baseAddresses[ii].StartsWith(UriScheme, StringComparison.Ordinal))
                {
                    continue;
                }

                var uri = new UriBuilder(baseAddresses[ii]);

                if (uri.Path[^1] != '/')
                {
                    uri.Path += "/";
                }

                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    uri.Host = computerName;
                }

                uris.Add(uri.Uri);

                ServerSecurityPolicy bestPolicy = null;
                bool httpsMutualTls = configuration.ServerConfiguration.HttpsMutualTls;
                if (!httpsMutualTls)
                {
                    // Only use security None without mutual TLS authentication!
                    // When the mutual TLS authentication is not used, anonymous access is disabled
                    // Then the only protection against unauthorized access is user authorization
                    bestPolicy = new ServerSecurityPolicy
                    {
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    };
                }
                else
                {
                    // Only support one secure policy with HTTPS and mutual authentication
                    // So pick the first policy with security mode sign and encrypt
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
                    bestPolicy ??= securityPolicies[0];
                }

                var description = new EndpointDescription
                {
                    EndpointUrl = uri.ToString(),
                    Server = serverDescription
                };

                if (instanceCertificateTypesProvider != null)
                {
                    X509Certificate2 instanceCertificate = instanceCertificateTypesProvider
                        .GetInstanceCertificate(
                            bestPolicy.SecurityPolicyUri);
                    description.ServerCertificate =
                        instanceCertificate.RawData.ToByteString();

                    // check if complete chain should be sent.
                    if (instanceCertificateTypesProvider.SendCertificateChain)
                    {
                        description.ServerCertificate =
                            instanceCertificateTypesProvider.LoadCertificateChainRaw(
                                instanceCertificate).ToByteString();
                    }
                }

                description.SecurityMode = bestPolicy.SecurityMode;
                description.SecurityPolicyUri = bestPolicy.SecurityPolicyUri;
                description.SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(
                    bestPolicy.SecurityMode,
                    bestPolicy.SecurityPolicyUri,
                    logger);
                description.UserIdentityTokens = serverBase.GetUserTokenPolicies(
                    configuration,
                    description);
                description.TransportProfileUri = Profiles.HttpsBinaryTransport;

                // if no mutual TLS authentication is used, anonymous user tokens are not allowed
                if (!httpsMutualTls)
                {
                    description.UserIdentityTokens =
                    [
                        .. description.UserIdentityTokens
                            .Where(token => token.TokenType != UserTokenType.Anonymous)
                    ];
                }

                ITransportListener listener = Create(serverBase.MessageContext.Telemetry);
                if (listener != null)
                {
                    endpoints.Add(description);
                    serverBase.CreateServiceHostEndpoint(
                        uri.Uri,
                        endpoints,
                        endpointConfiguration,
                        listener,
                        configuration.CertificateValidator.GetChannelValidator());
                }
                else
                {
                    logger.LogError("Failed to create endpoint {Uri} because the transport profile is unsupported.", uri);
                }
            }

            // create the host.
            hosts[hostName] = serverBase.CreateServiceHost(serverBase, [.. uris]);
            return endpoints;
        }
    }
}
