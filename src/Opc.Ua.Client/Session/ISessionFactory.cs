// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Object that creates instances of an ISessions object.
    /// </summary>
    public interface ISessionFactory
    {
        /// <summary>
        /// Set diagnostics for all sessions created by the factory
        /// </summary>
        DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <summary>
        /// Telemetry configuration to use when creating sessions.
        /// </summary>
        ITelemetryContext Telemetry { get; }

        /// <summary>
        /// The subscription engine the sessions created by this factory use,
        /// or <see langword="null"/> for the classic engine.
        /// </summary>
        ISubscriptionEngineFactory? SubscriptionEngineFactory { get; }

        /// <summary>
        /// Returns a factory that behaves exactly like this one except that
        /// the sessions it creates use <paramref name="engineFactory"/> and,
        /// when given, <paramref name="timeProvider"/>. This factory is not
        /// modified, so a shared instance can be specialised per caller.
        /// </summary>
        /// <param name="engineFactory">The subscription engine to use.</param>
        /// <param name="timeProvider">The time provider for the sessions, or
        /// <see langword="null"/> to keep this factory's.</param>
        ISessionFactory WithSubscriptionEngine(
            ISubscriptionEngineFactory engineFactory,
            TimeProvider? timeProvider = null);

        /// <summary>
        /// Creates a new unconnected session with the channel to the server.
        /// </summary>
        /// <param name="channel">The channel for the server.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <param name="clientCertificateChain">The certificate chain for the client cert.</param>
        /// <param name="availableEndpoints">The list of available endpoints returned by
        /// server in GetEndpoints() response.</param>
        /// <param name="discoveryProfileUris">The value of profileUris used in
        /// GetEndpoints() request.</param>
        ISession Create(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Certificate? clientCertificate = null,
            CertificateCollection? clientCertificateChain = null,
            ArrayOf<EndpointDescription> availableEndpoints = default,
            ArrayOf<string> discoveryProfileUris = default);

        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used
        /// to update the endpoint description before connecting.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The identity.</param>
        /// <param name="preferredLocales">The user identity to associate with the session.</param>
        /// <param name="ct">The cancellation token</param>
        /// <returns>The new session object</returns>
        Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession
        /// service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is
        /// used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate
        /// must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token</param>
        /// <returns>The new session object.</returns>
        Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a secure channel to the specified endpoint.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">A configured endpoint to connect to.</param>
        /// <param name="updateBeforeConnect">Update configuration based on server
        /// prior connect.</param>
        /// <param name="checkDomain">Check that the certificate specifies a valid
        /// domain (computer) name.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a new communication session with a server using a reverse connection.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint
        /// is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the
        /// certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token</param>
        /// <returns>The new session object.</returns>
        Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a new communication session with a server using a reverse connect manager.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="reverseConnectManager">The reverse connect manager for the client
        /// connection.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint
        /// is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate
        /// must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="userIdentity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity? userIdentity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default);

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sessionTemplate">The ISession object to use as template</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            CancellationToken ct = default);

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sessionTemplate">The ISession object to use as template</param>
        /// <param name="connection">The waiting reverse connection.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportWaitingConnection connection,
            CancellationToken ct = default);

        /// <summary>
        /// Recreates a session based on a specified template using the provided channel.
        /// </summary>
        /// <param name="sessionTemplate">The Session object to use as template</param>
        /// <param name="transportChannel">The channel to use to recreate the session.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        Task<ISession> RecreateAsync(
            ISession sessionTemplate,
            ITransportChannel transportChannel,
            CancellationToken ct = default);
    }
}
