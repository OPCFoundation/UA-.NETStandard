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
        /// Companion bindings (e.g. the REST binding in
        /// <c>OPCFoundation.NetStandard.Opc.Ua.Bindings.Rest</c>) register
        /// <see cref="IHttpsListenerStartupContributor"/> instances on
        /// this collection to mount additional middleware (typically
        /// routing + MVC controllers) inside the Kestrel host that this
        /// factory's listeners build. Contributors are propagated to
        /// every listener the factory creates via
        /// <see cref="ApplyContributorsTo(HttpsTransportListener)"/>.
        /// </summary>
        public IList<IHttpsListenerStartupContributor> StartupContributors { get; }
            = new List<IHttpsListenerStartupContributor>();

        /// <summary>
        /// Snapshots <see cref="StartupContributors"/> onto the supplied
        /// <paramref name="listener"/>. Called by every concrete subclass'
        /// <c>Create(ITelemetryContext)</c> implementation before the
        /// listener is returned to the caller.
        /// </summary>
        /// <param name="listener">The freshly-created listener.</param>
        /// <returns>The supplied <paramref name="listener"/> for fluent
        /// composition.</returns>
        protected HttpsTransportListener ApplyContributorsTo(HttpsTransportListener listener)
        {
            if (StartupContributors.Count > 0)
            {
                listener.StartupContributors = [.. StartupContributors];
            }
            return listener;
        }

        /// <summary>
        /// The OPC UA <c>TransportProfileUri</c> reported on the
        /// <see cref="EndpointDescription.TransportProfileUri"/> emitted for
        /// this factory's base addresses. Defaults to the HTTPS binary
        /// profile; the WSS overrides return <c>UaWssTransport</c>.
        /// </summary>
        protected virtual string TransportProfileUri => Profiles.HttpsBinaryTransport;

        /// <summary>
        /// Optional companion JSON <c>TransportProfileUri</c> emitted as an
        /// additional <see cref="MessageSecurityMode.None"/> endpoint
        /// description for each base address. <c>null</c> means the
        /// factory does not advertise a JSON variant.
        /// </summary>
        protected virtual string? JsonTransportProfileUri => null;

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
            ArrayOf<string> baseAddresses,
            ApplicationDescription serverDescription,
            ArrayOf<ServerSecurityPolicy> securityPolicies,
            ICertificateRegistry serverCertificates,
            ICertificateValidatorEx clientCertificateValidator)
        {
            // generate a unique host name.
            string hostName = hostName = "/Https";

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", hosts.Count);
            }

            // build list of uris.
            var uris = new List<Uri>();
            var endpoints = new List<EndpointDescription>();

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
                // The factory's UriScheme already encodes whether this is an
                // HTTPS or WSS endpoint (https / opc.https / wss / opc.wss).
                // Each factory only handles base addresses with a matching
                // scheme prefix so duplicate descriptions are not emitted.
                if (!baseAddresses[ii].StartsWith(UriScheme + "://", StringComparison.Ordinal))
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

                ServerSecurityPolicy? bestPolicy = null;
                bool httpsMutualTls = configuration.ServerConfiguration!.HttpsMutualTls;
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

                if (serverCertificates != null)
                {
                    Certificate? instanceCertificate = serverCertificates
                        .GetInstanceCertificate(
                            bestPolicy.SecurityPolicyUri)?.Certificate;
                    description.ServerCertificate =
                        instanceCertificate!.RawData.ToByteString();

                    // check if complete chain should be sent.
                    if (serverCertificates.SendCertificateChain)
                    {
                        description.ServerCertificate =
                            serverCertificates.LoadCertificateChainRaw(
                                instanceCertificate!).ToByteString();
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
                description.TransportProfileUri = TransportProfileUri;

                // if no mutual TLS authentication is used, anonymous user tokens are not allowed
                if (!httpsMutualTls)
                {
                    description.UserIdentityTokens = description.UserIdentityTokens
                        .Filter(token => token.TokenType != UserTokenType.Anonymous);
                }

                ITransportListener listener = Create(serverBase.MessageContext.Telemetry);
                if (listener != null)
                {
                    endpoints.Add(description);

                    // NOTE: Earlier iterations of this refactor emitted an
                    // additional Security-Mode-None description per base
                    // address with TransportProfileUri = the JSON variant.
                    // That registered a callable SecurityMode=None endpoint
                    // through serverBase.CreateServiceHostEndpoint, which
                    // effectively backdoored SecurityMode=None onto the
                    // companion binary endpoint and broke
                    // ClientTest.GetEndpointsOnDiscoveryChannelAsync(False)
                    // (the test expects BadSecurityPolicyRejected when the
                    // server's only configured security policy is non-None
                    // and the discovery client uses None). The JSON
                    // sub-protocol is still fully reachable on the wire
                    // because the dispatcher in Startup.Configure picks it
                    // up via the Content-Type / Sec-WebSocket-Protocol
                    // headers; explicit discovery emission for the JSON
                    // profile is tracked as a follow-up (it needs to
                    // separate discovery-only emission from callable
                    // endpoint registration in ServerBase).

                    serverBase.CreateServiceHostEndpoint(
                        uri.Uri,
                        endpoints,
                        endpointConfiguration,
                        listener,
                        clientCertificateValidator);
                }
                else
                {
                    logger.LogError("Failed to create endpoint {Uri} because the transport profile is unsupported.", uri);
                }
            }

            // create the host.
            hosts[hostName] = serverBase!.CreateServiceHost(serverBase!, [.. uris])!;
            return endpoints;
        }
    }
}
