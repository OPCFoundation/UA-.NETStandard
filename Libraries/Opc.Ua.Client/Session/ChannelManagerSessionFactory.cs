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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Session factory that creates raw <see cref="Session"/> instances whose
    /// transport channels are acquired from an <see cref="IClientChannelManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is one of the built-in <see cref="ISessionFactory"/> options:
    /// <see cref="DefaultSessionFactory"/> creates a raw <see cref="Session"/>
    /// without managed reconnect, <see cref="ManagedSessionFactory"/> creates a
    /// <see cref="ManagedSession"/> with the built-in reconnect state machine,
    /// and <see cref="ChannelManagerSessionFactory"/> creates a raw
    /// <see cref="Session"/> that acquires its transport from a shared
    /// <see cref="IClientChannelManager"/>.
    /// </para>
    /// <para>
    /// Use this factory when multiple sessions or session-based clients should
    /// share compatible channels and have transport reconnect coordinated
    /// centrally by the channel manager, while retaining the raw
    /// <see cref="Session"/> programming model.
    /// </para>
    /// </remarks>
    public sealed class ChannelManagerSessionFactory : ISessionFactory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelManagerSessionFactory"/> class.
        /// </summary>
        /// <param name="manager">The client channel manager that owns shared channels.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="returnDiagnostics">Diagnostics flags applied to created sessions.</param>
        /// <param name="timeProvider">Optional time provider forwarded to created sessions.</param>
        public ChannelManagerSessionFactory(
            IClientChannelManager manager,
            ITelemetryContext telemetry,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            TimeProvider? timeProvider = null)
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
        public Task<ISession> CreateAsync(
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
            return CreateCoreAsync(
                configuration,
                connection: null,
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
            return CreateCoreAsync(
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
        public async Task<ISession> CreateAsync(
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
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (reverseConnectManager == null)
            {
                return await CreateAsync(
                    configuration,
                    endpoint,
                    updateBeforeConnect,
                    checkDomain,
                    sessionName,
                    sessionTimeout,
                    userIdentity,
                    preferredLocales,
                    ct).ConfigureAwait(false);
            }

            ITransportWaitingConnection? connection;
            do
            {
                connection = await reverseConnectManager.WaitForConnectionAsync(
                    endpoint.EndpointUrl!,
                    endpoint.ReverseConnect?.ServerUri,
                    ct).ConfigureAwait(false);

                if (updateBeforeConnect)
                {
                    await endpoint.UpdateFromServerAsync(
                        endpoint.EndpointUrl!,
                        connection,
                        endpoint.Description.SecurityMode,
                        endpoint.Description.SecurityPolicyUri!,
                        Telemetry,
                        ct).ConfigureAwait(false);
                    updateBeforeConnect = false;
                    connection = null;
                }
            } while (connection == null);

            return await CreateAsync(
                configuration,
                connection,
                endpoint,
                updateBeforeConnect: false,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct = default)
        {
            await PrepareEndpointAndManagerAsync(
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                ct).ConfigureAwait(false);

            return await m_manager.GetAsync(
                endpoint,
                _ => new ChannelOnlyReconnectParticipant(endpoint),
                connection,
                ct).ConfigureAwait(false);
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

        private async Task<ISession> CreateCoreAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct)
        {
            await PrepareEndpointAndManagerAsync(
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                ct).ConfigureAwait(false);

            Session? session = null;
            IManagedTransportChannel? managedChannel = null;
            try
            {
                managedChannel = await m_manager.GetAsync(
                    endpoint,
                    channel =>
                    {
                        session = new Session(
                            channel,
                            configuration,
                            endpoint,
                            engineFactory: null,
                            timeProvider: m_timeProvider);
                        session.BindManagedChannel(m_manager, channel);
                        return session;
                    },
                    connection,
                    ct).ConfigureAwait(false);

                Session activeSession = session
                    ?? throw new InvalidOperationException("Participant factory did not create a session.");
                activeSession.ReturnDiagnostics = ReturnDiagnostics;

                UserIdentity? tempIdentity = identity == null ? new UserIdentity() : null;
                try
                {
                    await activeSession.OpenAsync(
                        sessionName,
                        sessionTimeout,
                        identity ?? tempIdentity!,
                        preferredLocales,
                        checkDomain,
                        ct).ConfigureAwait(false);
                }
                finally
                {
                    tempIdentity = null;
                }

                return activeSession;
            }
            catch
            {
                if (session != null)
                {
                    session.Dispose();
                }
                else
                {
                    managedChannel?.Dispose();
                }
                throw;
            }
        }

        private async Task<ServiceMessageContext> PrepareEndpointAndManagerAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            endpoint.UpdateBeforeConnect = updateBeforeConnect;
            endpoint.Configuration ??= EndpointConfiguration.Create(configuration);
            ServiceMessageContext messageContext = configuration.CreateMessageContext();

            if (updateBeforeConnect && connection == null)
            {
                await endpoint.UpdateFromServerAsync(messageContext.Telemetry, ct).ConfigureAwait(false);
            }

            if (checkDomain && endpoint.Description.ServerCertificate.Length > 0)
            {
                using var certificate = Certificate.FromRawData(endpoint.Description.ServerCertificate);
                configuration.CertificateManager?.ValidateDomains(certificate, endpoint);
            }

            string securityPolicyUri = endpoint.Description.SecurityPolicyUri ?? SecurityPolicies.None;
            if (securityPolicyUri != SecurityPolicies.None)
            {
                using CertificateEntry clientEntry = await Session.LoadInstanceCertificateEntryAsync(
                    configuration,
                    securityPolicyUri,
                    messageContext.Telemetry,
                    ct).ConfigureAwait(false);
#pragma warning disable CA2000 // ownership of the chain transfers to the channel manager, which disposes it
                m_manager.UpdateClientCertificate(
                    clientEntry.Certificate.AddRef(),
                    Session.BuildTransportChain(clientEntry));
#pragma warning restore CA2000
            }

            return messageContext;
        }

        private sealed class ChannelOnlyReconnectParticipant : IReconnectParticipant
        {
            public ChannelOnlyReconnectParticipant(ConfiguredEndpoint endpoint)
            {
                Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
                Id = nameof(ChannelManagerSessionFactory) + "-" + Guid.NewGuid().ToString("N");
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                return new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated);
            }
        }

        private readonly IClientChannelManager m_manager;
        private readonly DefaultSessionFactory m_innerFactory;
        private readonly TimeProvider? m_timeProvider;
    }
}
