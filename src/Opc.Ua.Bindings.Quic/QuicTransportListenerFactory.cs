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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates <see cref="QuicTransportListener"/> instances and the
    /// endpoints that advertise them.
    /// </summary>
    /// <remarks>
    /// A server that lists an <c>opc.quic</c> base address publishes one
    /// EndpointDescription per security policy carrying
    /// <see cref="Profiles.UaQuicTransport"/>, alongside whatever it
    /// publishes for <c>opc.tcp</c>. A client then chooses, and the rule
    /// that fallback shall not be a downgrade governs what it may choose
    /// instead when QUIC is unreachable.
    /// </remarks>
    public sealed class QuicTransportListenerFactory : ITransportListenerFactory
    {
        /// <summary>
        /// Creates a factory using the default buffer-manager factory.
        /// </summary>
        public QuicTransportListenerFactory()
            : this(DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a factory.
        /// </summary>
        /// <param name="bufferManagerFactory">Factory used to create
        /// listener buffer managers.</param>
        public QuicTransportListenerFactory(IBufferManagerFactory bufferManagerFactory)
        {
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new ArgumentNullException(nameof(bufferManagerFactory));
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcQuic;

        /// <inheritdoc/>
        public ITransportListener Create(ITelemetryContext telemetry)
        {
            return new QuicTransportListener(telemetry, m_bufferManagerFactory);
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
            if (serverBase == null)
            {
                throw new ArgumentNullException(nameof(serverBase));
            }

            if (hosts == null)
            {
                throw new ArgumentNullException(nameof(hosts));
            }

            string hostName = "/Quic";

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", hosts.Count);
            }

            var uris = new List<Uri>();
            var endpoints = new List<EndpointDescription>();

            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            string computerName = Utils.GetHostName();

            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                if (!baseAddresses[ii].StartsWith(Utils.UriSchemeOpcQuic, StringComparison.Ordinal))
                {
                    continue;
                }

                var uri = new UriBuilder(baseAddresses[ii]);

                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    uri.Host = computerName;
                }

                ITransportListener listener = Create(serverBase.MessageContext.Telemetry);
                var listenerEndpoints = new List<EndpointDescription>();
                uris.Add(uri.Uri);

                foreach (ServerSecurityPolicy policy in securityPolicies)
                {
                    var description = new EndpointDescription
                    {
                        EndpointUrl = uri.ToString(),
                        Server = serverDescription,
                        TransportProfileUri = Profiles.UaQuicTransport,
                        SecurityMode = policy.SecurityMode,
                        SecurityPolicyUri = policy.SecurityPolicyUri,
                        SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(
                            policy.SecurityMode,
                            policy.SecurityPolicyUri,
                            serverBase.MessageContext.Telemetry
                                .CreateLogger<QuicTransportListenerFactory>())
                    };

                    description.UserIdentityTokens = serverBase.GetUserTokenPolicies(
                        configuration,
                        description);

                    ServerBase.SetServerCertificateInEndpointDescription(
                        description,
                        serverCertificates);

                    listenerEndpoints.Add(description);
                }

                await serverBase.CreateServiceHostEndpointAsync(
                    uri.Uri,
                    listenerEndpoints,
                    endpointConfiguration,
                    listener,
                    clientCertificateValidator,
                    ct).ConfigureAwait(false);

                endpoints.AddRange(listenerEndpoints);
            }

            if (uris.Count > 0)
            {
                hosts[hostName] = serverBase.CreateServiceHost(serverBase, [.. uris])!;
            }

            return endpoints;
        }

        private readonly IBufferManagerFactory m_bufferManagerFactory;
    }
}
