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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Session factory that routes forward session channels through an <see cref="IClientChannelManager"/>.
    /// </summary>
    internal sealed class ChannelManagerSessionFactory : ISessionFactory
    {
        /// <summary>
        /// Initializes a session factory backed by a client channel manager.
        /// </summary>
        /// <param name="manager">The client channel manager.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="returnDiagnostics">Diagnostics flags applied to created sessions.</param>
        /// <param name="timeProvider">Optional time provider forwarded to created sessions.</param>
        public ChannelManagerSessionFactory(
            IClientChannelManager manager,
            ITelemetryContext telemetry,
            DiagnosticsMasks returnDiagnostics,
            TimeProvider? timeProvider)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider;
            m_innerFactory = new DefaultSessionFactory(telemetry)
            {
                TimeProvider = timeProvider
            };
            ReturnDiagnostics = returnDiagnostics;
        }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; }

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get => m_innerFactory.ReturnDiagnostics;
            set => m_innerFactory.ReturnDiagnostics = value;
        }

        /// <inheritdoc/>
        public ISession Create(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Certificate? clientCertificate = null,
            CertificateCollection? clientCertificateChain = null,
            ArrayOf<EndpointDescription> availableEndpoints = default,
            ArrayOf<string> discoveryProfileUris = default)
        {
            return m_innerFactory.Create(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                clientCertificateChain,
                availableEndpoints,
                discoveryProfileUris);
        }

        /// <inheritdoc/>
        public Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                configuration,
                endpoint,
                updateBeforeConnect,
                checkDomain: false,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                ct);
        }

        /// <inheritdoc/>
        public async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            Session session = await Session.CreateAsync(
                m_manager,
                configuration,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                engineFactory: null,
                m_timeProvider,
                ct).ConfigureAwait(false);

            session.ReturnDiagnostics = ReturnDiagnostics;
            return session;
        }

        /// <inheritdoc/>
        public Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            return m_innerFactory.CreateAsync(
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                ct);
        }

        /// <inheritdoc/>
        public Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? userIdentity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            return m_innerFactory.CreateAsync(
                configuration,
                reverseConnectManager,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales,
                ct);
        }

        /// <inheritdoc/>
        public Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct = default)
        {
            return m_innerFactory.CreateChannelAsync(
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                ct);
        }

        /// <inheritdoc/>
        public Task<ISession> RecreateAsync(ISession sessionTemplate, CancellationToken ct = default)
        {
            return m_innerFactory.RecreateAsync(sessionTemplate, ct);
        }

        /// <inheritdoc/>
        public Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            return m_innerFactory.RecreateAsync(sessionTemplate, connection, ct);
        }

        /// <inheritdoc/>
        public Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportChannel transportChannel,
            CancellationToken ct = default)
        {
            return m_innerFactory.RecreateAsync(sessionTemplate, transportChannel, ct);
        }

        private readonly IClientChannelManager m_manager;
        private readonly DefaultSessionFactory m_innerFactory;
        private readonly TimeProvider? m_timeProvider;
    }
}
