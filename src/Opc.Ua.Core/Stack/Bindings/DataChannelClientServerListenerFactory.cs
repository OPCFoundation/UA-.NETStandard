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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Registers one explicit virtual native Service endpoint with ordinary ServerBase hosting.
    /// The independently configured native transport owns its sockets and connection/CONNECT endpoint.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelClientServerListenerFactory : ITransportListenerFactory, ITransportListenerProfile
    {
        /// <summary>
        /// Supplies the listener the application uses directly or connects to its shared resource router.
        /// </summary>
        public DataChannelClientServerListenerFactory(DataChannelClientServerListener listener)
        {
            Listener = listener ?? throw new ArgumentNullException(nameof(listener));
        }

        /// <summary>
        /// Gets the listener whose ownership transfers to the Server when opened.
        /// </summary>
        public DataChannelClientServerListener Listener { get; }

        /// <inheritdoc/>
        public string UriScheme => Listener.UriScheme;

        /// <inheritdoc/>
        public string TransportProfileUri => Profiles.UaTcpTransport;

        /// <inheritdoc/>
        public ITransportListener Create(ITelemetryContext telemetry)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (Interlocked.Exchange(ref m_created, 1) != 0)
            {
                throw new InvalidOperationException("Configure one virtual Service endpoint per native lane listener.");
            }
            return Listener;
        }

        /// <inheritdoc/>
        public async ValueTask<List<EndpointDescription>> CreateServiceHostAsync(
            ServerBase serverBase,
            IDictionary<string, ServiceHost> hosts,
            ApplicationConfiguration configuration,
            ArrayOf<string> baseAddresses,
            ApplicationDescription serverDescription,
            ArrayOf<ServerSecurityPolicy> securityPolicies,
            ICertificateRegistry serverCertificates,
            ICertificateValidatorEx clientCertificateValidator,
            CancellationToken ct = default)
        {
            Uri? address = null;
            foreach (string candidate in baseAddresses)
            {
                if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) && uri.Scheme == UriScheme)
                {
                    if (address != null)
                    {
                        throw new ArgumentException("Only one virtual Service endpoint is permitted per lane listener.",
                            nameof(baseAddresses));
                    }
                    address = uri;
                }
            }
            var endpoints = new List<EndpointDescription>();
            if (address == null)
            {
                return endpoints;
            }
            foreach (ServerSecurityPolicy policy in securityPolicies)
            {
                if (policy.SecurityMode is not (MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt) ||
                    policy.SecurityPolicyUri == SecurityPolicies.None)
                {
                    continue;
                }
                var description = new EndpointDescription
                {
                    EndpointUrl = address.AbsoluteUri,
                    Server = serverDescription,
                    TransportProfileUri = Profiles.UaTcpTransport,
                    SecurityMode = policy.SecurityMode,
                    SecurityPolicyUri = policy.SecurityPolicyUri
                };
                description.UserIdentityTokens = serverBase.GetUserTokenPolicies(configuration, description);
                ServerBase.SetServerCertificateInEndpointDescription(
                    description, serverCertificates, checkRequireEncryption: true, serverBase.SecurityPolicyRegistry);
                endpoints.Add(description);
            }
            if (endpoints.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }
            await serverBase.CreateServiceHostEndpointAsync(
                address, endpoints, EndpointConfiguration.Create(configuration),
                Create(serverBase.MessageContext.Telemetry), clientCertificateValidator, ct).ConfigureAwait(false);
            hosts.Add("/NativeClientServer/" + UriScheme, serverBase.CreateServiceHost(serverBase, [address])!);
            return endpoints;
        }

        private int m_created;
    }
}
