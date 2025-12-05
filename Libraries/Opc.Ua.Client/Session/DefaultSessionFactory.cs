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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Object that creates instances of an Opc.Ua.Client.Session object.
    /// </summary>
    public class DefaultSessionFactory : ISessionFactory
    {
        /// <summary>
        /// The default instance of the factory.
        /// </summary>
        [Obsolete("Use new DefaultSessionFactory instead.")]
        public static readonly DefaultSessionFactory Instance = new(null!);

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; init; }

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <summary>
        /// Obsolete default constructor
        /// </summary>
        [Obsolete("Use DefaultSessionFactory(ITelemetryContext) instead.")]
        public DefaultSessionFactory()
            : this(null!)
        {
        }

        /// <summary>
        /// Force use of the default instance.
        /// </summary>
        public DefaultSessionFactory(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
        }

        /// <inheritdoc/>
        public virtual Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            IList<string>? preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                configuration,
                endpoint,
                updateBeforeConnect,
                false,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                ct);
        }

        /// <inheritdoc/>
        public virtual Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            IList<string>? preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                configuration,
                null,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                ReturnDiagnostics,
                ct);
        }

        /// <inheritdoc/>
        public virtual Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            IList<string>? preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                ReturnDiagnostics,
                ct);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? userIdentity,
            IList<string>? preferredLocales,
            CancellationToken ct = default)
        {
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
                connection = await reverseConnectManager
                    .WaitForConnectionAsync(
                        endpoint.EndpointUrl,
                        endpoint.ReverseConnect?.ServerUri,
                        ct)
                    .ConfigureAwait(false);

                if (updateBeforeConnect)
                {
                    await endpoint.UpdateFromServerAsync(
                        endpoint.EndpointUrl,
                        connection,
                        endpoint.Description.SecurityMode,
                        endpoint.Description.SecurityPolicyUri,
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
                false,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual async Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct = default)
        {
            endpoint.UpdateBeforeConnect = updateBeforeConnect;

            EndpointDescription endpointDescription = endpoint.Description;

            // create the endpoint configuration (use the application configuration to provide default values).
            EndpointConfiguration endpointConfiguration = endpoint.Configuration;

            if (endpointConfiguration == null)
            {
                endpoint.Configuration = endpointConfiguration = EndpointConfiguration.Create(
                    configuration);
            }

            // create message context.
            ServiceMessageContext messageContext = configuration.CreateMessageContext(true);

            // update endpoint description using the discovery endpoint.
            if (endpoint.UpdateBeforeConnect && connection == null)
            {
                await endpoint.UpdateFromServerAsync(messageContext.Telemetry, ct).ConfigureAwait(false);
                endpointDescription = endpoint.Description;
                endpointConfiguration = endpoint.Configuration;
            }

            // checks the domains in the certificate.
            if (checkDomain &&
                endpoint.Description.ServerCertificate != null &&
                endpoint.Description.ServerCertificate.Length > 0)
            {
                configuration.CertificateValidator?.ValidateDomains(
                    CertificateFactory.Create(endpoint.Description.ServerCertificate),
                    endpoint);
            }

            X509Certificate2? clientCertificate = null;
            X509Certificate2Collection? clientCertificateChain = null;
            if (endpointDescription.SecurityPolicyUri != SecurityPolicies.None)
            {
                clientCertificate = await Session.LoadInstanceCertificateAsync(
                    configuration,
                    endpointDescription.SecurityPolicyUri,
                    messageContext.Telemetry,
                    ct)
                    .ConfigureAwait(false);
                clientCertificateChain = await Session.LoadCertificateChainAsync(
                    configuration,
                    clientCertificate,
                    ct)
                    .ConfigureAwait(false);
            }

            // initialize the channel which will be created with the server.
            if (connection != null)
            {
                return await UaChannelBase.CreateUaBinaryChannelAsync(
                    configuration,
                    connection,
                    endpointDescription,
                    endpointConfiguration,
                    clientCertificate,
                    clientCertificateChain,
                    messageContext,
                    ct).ConfigureAwait(false);
            }

            return await UaChannelBase.CreateUaBinaryChannelAsync(
                configuration,
                endpointDescription,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            CancellationToken ct = default)
        {
            if (sessionTemplate is not Session template)
            {
                throw new ArgumentException(
                    "The ISession provided is not of a supported type.",

                    nameof(sessionTemplate));
            }

            template.ReturnDiagnostics = ReturnDiagnostics;
            return await template.RecreateAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            if (sessionTemplate is not Session template)
            {
                throw new ArgumentException(
                    "The ISession provided is not of a supported type.",

                    nameof(sessionTemplate));
            }

            template.ReturnDiagnostics = ReturnDiagnostics;
            return await template.RecreateAsync(connection, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual async Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportChannel transportChannel,
            CancellationToken ct = default)
        {
            if (sessionTemplate is not Session template)
            {
                throw new ArgumentException(
                    "The ISession provided is not of a supported type.",

                    nameof(sessionTemplate));
            }
            template.ReturnDiagnostics = ReturnDiagnostics;
            return await template.RecreateAsync(transportChannel, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public virtual ISession Create(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            X509Certificate2? clientCertificate = null,
            X509Certificate2Collection? clientCertificateChain = null,
            EndpointDescriptionCollection? availableEndpoints = null,
            StringCollection? discoveryProfileUris = null)
        {
            return new Session(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                clientCertificateChain,
                availableEndpoints,
                discoveryProfileUris)
            {
                ReturnDiagnostics = ReturnDiagnostics
            };
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connection.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to
        /// update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match
        /// the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="returnDiagnostics">The return diagnostics to use on this session</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        private async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            IList<string>? preferredLocales,
            DiagnosticsMasks returnDiagnostics,
            CancellationToken ct = default)
        {
            // initialize the channel which will be created with the server.
            ITransportChannel channel = await CreateChannelAsync(
                    configuration,
                    connection,
                    endpoint,
                    updateBeforeConnect,
                    checkDomain,
                    ct)
                .ConfigureAwait(false);

            // create the session object.
            ISession session = Create(channel, configuration, endpoint, null);
            session.ReturnDiagnostics = returnDiagnostics;

            // create the session.
            try
            {
                await session
                    .OpenAsync(
                        sessionName,
                        sessionTimeout,
                        identity ?? new UserIdentity(),
                        preferredLocales,
                        checkDomain,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                session.Dispose();
                throw;
            }

            return session;
        }
    }
}
