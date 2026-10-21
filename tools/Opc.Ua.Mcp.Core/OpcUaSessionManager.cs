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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Manages the OPC UA client session lifecycle for the MCP server.
    /// </summary>
    public sealed class OpcUaSessionManager : IDisposable
    {
        private const string kApplicationName = "OPC UA MCP Server";
        private const string kConfigSectionName = "Opc.Ua.Mcp";
        private const string kConfigFileName = "Opc.Ua.Mcp.Config.xml";

        private readonly ILogger<OpcUaSessionManager> m_logger;
        private readonly IServiceProvider m_serviceProvider;
        private readonly OpcUaClientOptions m_clientOptions;

        /// <summary>
        /// Trusted transport scopes distinguishing host provisioning from caller-owned connections.
        /// </summary>
        private readonly McpCallerContext m_callers;
        private readonly SemaphoreSlim m_lock = new(1, 1);
        private readonly ConcurrentDictionary<string, SessionInfo> m_sessions = new(StringComparer.OrdinalIgnoreCase);
        private ApplicationConfiguration? m_configuration;
        private bool m_disposed;

        /// <summary>
        /// Initializes the session manager.
        /// </summary>
        /// <param name="logger">The logger for session lifecycle messages.</param>
        /// <param name="serviceProvider">The provider used to resolve client services.</param>
        /// <param name="clientOptions">The OPC UA client application options.</param>
        /// <param name="telemetry">The telemetry context sessions are created with.</param>
        /// <exception cref="ArgumentNullException">
        /// Any argument is <c>null</c>.
        /// </exception>
        public OpcUaSessionManager(
            ILogger<OpcUaSessionManager> logger,
            IServiceProvider serviceProvider,
            OpcUaClientOptions clientOptions,
            ITelemetryContext telemetry)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            m_clientOptions = clientOptions ?? throw new ArgumentNullException(nameof(clientOptions));
            m_callers = serviceProvider.GetService<McpCallerContext>() ?? new McpCallerContext();
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            OperationPolicy = serviceProvider.GetService<IMcpOperationPolicy>() ??
                new McpExecutionPolicy(
                    serviceProvider.GetService<OpcUaMcpOptions>()?.Execution ?? new McpExecutionOptions(),
                    m_callers,
                    telemetry);
            UaOperationGuard = new McpUaOperationGuard(OperationPolicy);
        }

        /// <summary>
        /// Gets the injected host authority shared by semantic operations and generic UA request guards.
        /// </summary>
        public IMcpOperationPolicy OperationPolicy { get; }

        /// <summary>
        /// Gets the shared protected-model guard used before generic UA mutations.
        /// </summary>
        public McpUaOperationGuard UaOperationGuard { get; }

        /// <summary>
        /// Holds information about a named OPC UA session.
        /// </summary>
        public sealed class SessionInfo
        {
            /// <summary>
            /// Gets the name the session is registered under.
            /// </summary>
            public required string Name { get; init; }

            /// <summary>
            /// Gets the underlying session.
            /// </summary>
            public required ISession Session { get; init; }

            /// <summary>
            /// Gets the endpoint the session is connected to.
            /// </summary>
            public required EndpointDescription Endpoint { get; init; }

            /// <summary>
            /// Gets the user authentication kind the session was created with.
            /// </summary>
            public required string AuthType { get; init; }

            /// <summary>
            /// Gets the time the session was established.
            /// </summary>
            public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;

            /// <summary>
            /// Gets whether the underlying session is currently connected.
            /// </summary>
            public bool IsConnected => Session.Connected;

            /// <summary>
            /// Gets the immutable registration identity, or an empty string for an unregistered service view.
            /// </summary>
            public string RegistrationId => Authority?.RegistrationId ?? string.Empty;

            /// <summary>
            /// Gets the host-established lifetime, ownership and authenticated-target binding.
            /// </summary>
            internal McpSessionAuthority? Authority { get; init; }

            /// <summary>
            /// Gets the certificate-validation resources owned by this registration.
            /// </summary>
            internal ConnectionValidationContext? ValidationContext { get; init; }

            /// <summary>
            /// Gets whether the manager owns the underlying session's lifetime.
            /// </summary>
            internal bool OwnsSession { get; init; } = true;

            /// <summary>
            /// Creates a non-owning captured service view without losing registration provenance.
            /// </summary>
            public SessionInfo WithSession(ISession session)
            {
                ArgumentNullException.ThrowIfNull(session);
                return new SessionInfo
                {
                    Name = Name,
                    Session = session,
                    Endpoint = CoreUtils.Clone(Endpoint)!,
                    AuthType = AuthType,
                    ConnectedAt = ConnectedAt,
                    Authority = Authority,
                    OwnsSession = false
                };
            }
        }

        /// <summary>
        /// Gets the first session, or null if none connected. For backward compatibility.
        /// </summary>
        public ISession? Session => GetVisibleSessions().FirstOrDefault()?.Session;

        /// <summary>
        /// Gets whether the call originates in trusted embedding code rather than an MCP request scope.
        /// A local transport request is still caller-bound.
        /// </summary>
        public bool IsTrustedHostCall => !m_callers.HasRequestScope;

        /// <summary>
        /// Gets the telemetry context used by this session manager.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the loaded application configuration, or null if not yet loaded.
        /// </summary>
        public ApplicationConfiguration? Configuration => m_configuration;

        /// <summary>
        /// Gets the active transport channels for connected sessions.
        /// </summary>
        public IReadOnlyList<ITransportChannel> GetActiveTransportChannels()
        {
            return [.. GetVisibleSessions()
                .Where(static info => info.IsConnected)
                .Select(static info => info.Session.TransportChannel)
                .OfType<ITransportChannel>()];
        }

        /// <summary>
        /// Ensures the application configuration is loaded, loading it if necessary.
        /// </summary>
        public async Task<ApplicationConfiguration> EnsureConfigurationAsync(CancellationToken ct = default)
        {
            if (m_configuration != null)
            {
                return m_configuration;
            }

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureConfigurationInternalAsync(ct).ConfigureAwait(false);
                return m_configuration!;
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Gets whether any session is currently connected.
        /// </summary>
        public bool IsConnected => GetVisibleSessions().Any(s => s.IsConnected);

        /// <summary>
        /// Gets a session by name, or the only active session if name is null.
        /// </summary>
        /// <exception cref="InvalidOperationException">Not connected or ambiguous session.</exception>
        public ISession GetSessionOrThrow(string? name = null)
        {
            return GetSessionInfoOrThrow(name).Session;
        }

        /// <summary>
        /// Resolves a connected session and its actual registered name as one ownership snapshot.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The requested session is missing, disconnected or ambiguous.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The current caller has no access to a registered session.
        /// </exception>
        public SessionInfo GetSessionInfoOrThrow(string? name = null)
        {
            if (name != null)
            {
                if (m_sessions.TryGetValue(name, out SessionInfo? info) && info.IsConnected)
                {
                    RequireSessionAccess(info);
                    return info;
                }

                throw new InvalidOperationException(
                    $"Session '{name}' not found or not connected. Use the 'Connect' tool first.");
            }

            SessionInfo[] visible = [.. GetVisibleSessions()];
            int count = visible.Length;
            if (count == 0)
            {
                if (!m_sessions.IsEmpty)
                {
                    throw SessionAccessDenied();
                }
                throw new InvalidOperationException(
                    "Not connected to an OPC UA server. Use the 'Connect' tool first.");
            }

            if (count == 1)
            {
                SessionInfo single = visible[0];
                if (single.IsConnected)
                {
                    return single;
                }

                throw new InvalidOperationException(
                    "Not connected to an OPC UA server. Use the 'Connect' tool first.");
            }

            string names = string.Join(", ", visible.Select(info => info.Name));
            throw new InvalidOperationException(
                $"Multiple sessions active. Specify a sessionName: {names}");
        }

        /// <summary>
        /// Gets all active sessions.
        /// </summary>
        public IReadOnlyCollection<SessionInfo> GetAllSessions()
        {
            return GetVisibleSessions().ToList().AsReadOnly();
        }

        /// <summary>
        /// Gets information about a specific named session.
        /// </summary>
        public SessionInfo? GetSessionInfo(string name)
        {
            if (!m_sessions.TryGetValue(name, out SessionInfo? info))
            {
                return null;
            }
            RequireSessionAccess(info);
            return info;
        }

        /// <summary>
        /// Resolves the optional host contract for fixed custom sessions without changing managed-session capture.
        /// </summary>
        internal IMcpFixedSessionContract? GetFixedSessionContract()
        {
            return m_serviceProvider.GetService<IMcpFixedSessionContract>();
        }

        /// <summary>
        /// Discovers all endpoints available at the given discovery URL.
        /// </summary>
        public async Task<ArrayOf<EndpointDescription>> DiscoverEndpointsAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);

            ApplicationConfiguration configuration =
                await EnsureConfigurationAsync(ct).ConfigureAwait(false);
            return await DiscoverEndpointsInternalAsync(
                configuration,
                discoveryUrl,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Connects to an OPC UA server with endpoint selection and authentication options.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Task<string> ConnectAsync(
            string? name,
            string endpointUrl,
            string? securityMode,
            string? securityPolicy,
            string authType,
            string? username,
            string? password,
            bool autoAcceptCerts,
            CancellationToken ct)
        {
            return ConnectAsync(name, endpointUrl, securityMode, securityPolicy, authType,
                username, password, autoAcceptCerts, certificateIdentity: null, ct);
        }

        /// <summary>
        /// Connects using the selected authentication kind and an optional host-approved X.509 identity reference.
        /// </summary>
        public async Task<string> ConnectAsync(
            string? name,
            string endpointUrl,
            string? securityMode,
            string? securityPolicy,
            string authType,
            string? username,
            string? password,
            bool autoAcceptCerts,
            string? certificateIdentity,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            McpCallerIdentity? owner = IsTrustedHostCall ? null : OperationPolicy.RequireCaller();
            if (certificateIdentity != null &&
                !string.Equals(authType, "Certificate", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "A certificate identity reference requires Certificate authentication.",
                    nameof(certificateIdentity));
            }

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Auto-generate name from endpoint URL if not specified
                if (string.IsNullOrWhiteSpace(name))
                {
                    var uri = new Uri(endpointUrl);
                    name = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}-{1}",
                        uri.Host,
                        uri.Port);
                }

                // Establish replacement authority before loading configuration or contacting an endpoint.
                if (m_sessions.TryGetValue(name, out SessionInfo? existing))
                {
                    RequireSessionMutation(existing, owner);
                    await DisconnectInternalAsync(existing, ct).ConfigureAwait(false);
                    existing.Authority?.Retire();
                    m_sessions.TryRemove(name, out _);
                }

                IMcpSessionConnector? connector = m_serviceProvider.GetService<IMcpSessionConnector>();
                if (connector is not null)
                {
                    ISession connected = await connector.ConnectAsync(
                        name, endpointUrl, securityMode, securityPolicy, authType, username, password,
                        autoAcceptCerts, certificateIdentity, ct).ConfigureAwait(false);
                    return RegisterConnectedSession(
                        name, connected, authType, owner,
                        autoAcceptCerts, null, endpointUrl);
                }

                await EnsureConfigurationInternalAsync(ct).ConfigureAwait(false);
                IClientIdentityProvider? identityProvider =
                    string.Equals(authType, "Certificate", StringComparison.OrdinalIgnoreCase)
                        ? CreateCertificateIdentityProvider(certificateIdentity, m_configuration!)
                        : null;
                ConnectionValidationContext? validationContext =
                    await CreateConnectionValidationContextAsync(
                        m_configuration!,
                        Telemetry,
                        autoAcceptCerts,
                        ct).ConfigureAwait(false);
                try
                {
                    ApplicationConfiguration configuration = validationContext.Configuration;

                    m_logger.Connecting(endpointUrl, name);

                    EndpointDescription selectedEndpoint = await SelectEndpointAsync(
                        configuration,
                        endpointUrl,
                        securityMode,
                        securityPolicy,
                        authType,
                        ct).ConfigureAwait(false);

                    var endpointConfiguration = EndpointConfiguration.Create(configuration);
                    var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
                    ManagedSessionBuilder builder = new ManagedSessionBuilder(configuration, Telemetry)
                        .UseEndpoint(endpoint)
                        .WithSessionName(name)
                        .WithSessionTimeout(TimeSpan.FromMilliseconds(60_000));
                    if (identityProvider != null)
                    {
                        builder.WithIdentityProvider(identityProvider);
                    }
                    else
                    {
                        builder.WithUserIdentity(BuildUserIdentity(authType, username, password));
                    }

                    if (validationContext.UseSharedChannelManager)
                    {
                        builder.WithChannelManager(
                            m_serviceProvider.GetRequiredService<IClientChannelManager>());
                    }

                    ManagedSession session = await builder
                        .ConnectAsync(ct)
                        .ConfigureAwait(false);

                    session.KeepAliveInterval = 5000;
                    SubscribeSessionStateLogging(name, session);

                    string result = RegisterConnectedSession(
                        name, session, authType, owner, autoAcceptCerts,
                        validationContext, endpointUrl);
                    validationContext = null;
                    return result;
                }
                finally
                {
                    validationContext?.Dispose();
                }
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Registers an already-connected session without transferring ownership to the MCP session manager.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The operation did not originate from a trusted host call.
        /// </exception>
        public async Task<string> RegisterExistingSessionAsync(
            string name,
            ISession session,
            string authType,
            CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentException.ThrowIfNullOrWhiteSpace(authType);
            if (!IsTrustedHostCall)
            {
                throw SessionAccessDenied();
            }

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_sessions.TryGetValue(name, out SessionInfo? existing))
                {
                    if (!ReferenceEquals(existing.Session, session))
                    {
                        await DisconnectInternalAsync(existing, ct).ConfigureAwait(false);
                    }
                    existing.Authority?.Retire();
                    m_sessions.TryRemove(name, out _);
                }

                SessionInfo? retained = existing is not null && ReferenceEquals(existing.Session, session)
                    ? existing : null;
                m_sessions[name] = new SessionInfo
                {
                    Name = name,
                    Session = session,
                    Endpoint = CoreUtils.Clone(session.ConfiguredEndpoint.Description)!,
                    AuthType = authType,
                    ConnectedAt = DateTime.UtcNow,
                    OwnsSession = retained?.OwnsSession == true,
                    ValidationContext = retained?.ValidationContext,
                    Authority = new McpSessionAuthority(
                        null, retained?.Authority?.AutoAccepted == true,
                        session.ConfiguredEndpoint.Description)
                };

                m_logger.Connected(name, session.SessionName, session.SessionId);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Registered existing session '{0}'. SessionName={1}, SessionId={2}",
                    name,
                    session.SessionName,
                    session.SessionId);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Disconnects a named session, or the only session if name is null.
        /// </summary>
        public async Task<string> DisconnectAsync(string? name = null, CancellationToken ct = default)
        {
            McpCallerIdentity? caller = IsTrustedHostCall ? null : OperationPolicy.RequireCaller();
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (name != null)
                {
                    if (!m_sessions.TryGetValue(name, out SessionInfo? info))
                    {
                        return $"Session '{name}' not found.";
                    }

                    RequireSessionMutation(info, caller);
                    m_sessions.TryRemove(name, out _);
                    info.Authority?.Retire();
                    await DisconnectInternalAsync(info, ct).ConfigureAwait(false);
                    return $"Disconnected session '{name}'.";
                }

                int count = m_sessions.Count;
                if (count == 0)
                {
                    return "No active sessions to disconnect.";
                }

                if (count > 1)
                {
                    string names = string.Join(", ", m_sessions.Keys);
                    return $"Multiple sessions active. Specify a session name to disconnect: {names}";
                }

                // Exactly one session
                KeyValuePair<string, SessionInfo> single = m_sessions.First();
                RequireSessionMutation(single.Value, caller);
                m_sessions.TryRemove(single.Key, out _);
                single.Value.Authority?.Retire();
                await DisconnectInternalAsync(single.Value, ct).ConfigureAwait(false);
                return $"Disconnected session '{single.Key}'.";
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Gets connection status information for one or all sessions.
        /// </summary>
        public string GetConnectionStatus(string? name = null)
        {
            if (name != null)
            {
                if (!m_sessions.TryGetValue(name, out SessionInfo? info))
                {
                    return $"Session '{name}' not found.";
                }

                RequireSessionAccess(info);
                return FormatSessionStatus(info);
            }

            if (m_sessions.IsEmpty)
            {
                return "Not connected.";
            }

            var lines = new List<string>();
            foreach (SessionInfo info in GetVisibleSessions())
            {
                lines.Add(FormatSessionStatus(info));
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Disconnects and disposes every managed session.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            foreach (SessionInfo info in m_sessions.Values)
            {
                info.Authority?.Retire();
                try
                {
                    if (info.OwnsSession)
                    {
                        info.Session.Dispose();
                    }
                }
                finally
                {
                    info.ValidationContext?.Dispose();
                }
            }

            m_sessions.Clear();
            m_lock.Dispose();
        }

        /// <summary>
        /// Publishes only the session and provenance established by the connection path.
        /// </summary>
        private string RegisterConnectedSession(
            string name, ISession session, string authType,
            McpCallerIdentity? owner, bool autoAccepted, ConnectionValidationContext? validationContext,
            string endpointUrl)
        {
            EndpointDescription endpoint = session.ConfiguredEndpoint.Description;
            m_sessions[name] = new SessionInfo
            {
                Name = name,
                Session = session,
                Endpoint = CoreUtils.Clone(endpoint)!,
                AuthType = authType,
                ValidationContext = validationContext,
                Authority = new McpSessionAuthority(owner, autoAccepted, endpoint)
            };
            m_logger.Connected(name, session.SessionName, session.SessionId);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Connected to {0} as '{1}'. SecurityMode={2}, SecurityPolicy={3}, Auth={4}, " +
                "SessionName={5}, SessionId={6}",
                endpointUrl, name, endpoint.SecurityMode, endpoint.SecurityPolicyUri,
                authType, session.SessionName, session.SessionId);
        }

        /// <summary>
        /// Prevents a reusable name grant from lending one caller's connection to another caller.
        /// Host registrations are borrowable only with an explicit frozen session grant.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The registration belongs to another caller, or the caller has no grant to borrow it.
        /// </exception>
        private void RequireSessionAccess(SessionInfo info)
        {
            if (IsTrustedHostCall)
            {
                return;
            }
            McpCallerIdentity caller = OperationPolicy.RequireCaller();
            if (info.Authority?.Owner is { } owner)
            {
                if (owner != caller)
                {
                    throw SessionAccessDenied();
                }
                return;
            }
            _ = OperationPolicy.RequireSessionAccess(info.Name);
        }

        /// <summary>
        /// A session grant permits use, never replacement or removal of a host or another caller's registration.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The caller does not own the registration.
        /// </exception>
        private static void RequireSessionMutation(SessionInfo info, McpCallerIdentity? caller)
        {
            if (caller is not null && info.Authority?.Owner != caller)
            {
                throw SessionAccessDenied();
            }
        }

        /// <summary>
        /// Filters enumeration without revealing registrations the current caller cannot borrow.
        /// </summary>
        private IEnumerable<SessionInfo> GetVisibleSessions()
        {
            foreach (SessionInfo info in m_sessions.Values)
            {
                bool allowed;
                try
                {
                    RequireSessionAccess(info);
                    allowed = true;
                }
                catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    allowed = false;
                }
                if (allowed)
                {
                    yield return info;
                }
            }
        }

        /// <summary>
        /// Returns a bounded refusal without disclosing another registration's identity or endpoint.
        /// </summary>
        private static ServiceResultException SessionAccessDenied()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The caller cannot replace, remove or borrow this UA session registration.");
        }

        /// <summary>
        /// Resolves a host-owned selector through the existing X.509 identity and certificate providers.
        /// </summary>
        internal IClientIdentityProvider CreateCertificateIdentityProvider(
            string? reference,
            ApplicationConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                    "Certificate authentication requires a host-configured certificateIdentity reference.");
            }
            ArrayOf<McpCertificateIdentity> identities =
                m_serviceProvider.GetRequiredService<OpcUaMcpOptions>().CertificateIdentities;
            McpCertificateIdentity? selected = null;
            foreach (McpCertificateIdentity candidate in identities)
            {
                if (candidate.Name != reference)
                {
                    continue;
                }
                if (selected != null)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "The configured certificate identity name is ambiguous.");
                }
                selected = candidate;
            }
            if (selected?.Certificate == null)
            {
                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                    "The requested certificateIdentity is not configured by the host.");
            }
            ICertificateManager manager = configuration.CertificateManager ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The application certificate manager is not initialized.");
            ICertificatePasswordProvider passwords = m_serviceProvider
                .GetService<ICertificatePasswordProvider>() ?? new CertificatePasswordProvider();
            return new CompositeClientIdentityProviderBuilder()
                .AddX509(options =>
                {
                    options.StoreType = selected.Certificate.StoreType;
                    options.StorePath = selected.Certificate.StorePath;
                    options.SubjectName = selected.Certificate.SubjectName;
                    options.Thumbprint = selected.Certificate.Thumbprint;
                }, manager.CertificateProvider, passwords)
                .Build();
        }

        private static async Task DisconnectInternalAsync(SessionInfo info, CancellationToken ct)
        {
            if (!info.OwnsSession)
            {
                info.ValidationContext?.Dispose();
                return;
            }

            try
            {
                await info.Session.CloseAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    info.Session.Dispose();
                }
                finally
                {
                    info.ValidationContext?.Dispose();
                }
            }
        }

        internal static async Task<ConnectionValidationContext>
            CreateConnectionValidationContextAsync(
                ApplicationConfiguration configuration,
                ITelemetryContext telemetry,
                bool autoAcceptCertificates,
                CancellationToken ct)
        {
            if (!autoAcceptCertificates)
            {
                return new ConnectionValidationContext(configuration, null);
            }

            CertificateManager? certificateManager = null;
            try
            {
                certificateManager = CertificateManagerFactory.Create(
                    configuration.SecurityConfiguration,
                    telemetry);
                certificateManager.AcceptError = AutoAcceptError;
                await certificateManager.LoadApplicationCertificatesAsync(
                    configuration.SecurityConfiguration,
                    configuration.ApplicationUri,
                    ct).ConfigureAwait(false);

                var isolatedConfiguration = new ApplicationConfiguration(configuration)
                {
                    CertificateManager = certificateManager
                };
                var result = new ConnectionValidationContext(
                    isolatedConfiguration,
                    certificateManager);
                certificateManager = null;
                return result;
            }
            finally
            {
                if (certificateManager is not null)
                {
                    await certificateManager.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task EnsureConfigurationInternalAsync(CancellationToken ct)
        {
            if (m_configuration != null)
            {
                return;
            }
            if (m_clientOptions.Configuration is { } supplied)
            {
                m_configuration = supplied;
                return;
            }

#pragma warning disable CA2000 // Dispose objects before losing scope
            var application = new ApplicationInstance(Telemetry)
            {
                ApplicationName = kApplicationName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = kConfigSectionName
            };
#pragma warning restore CA2000 // Dispose objects before losing scope

            string configFile = Path.Combine(AppContext.BaseDirectory, kConfigFileName);
            ApplicationConfiguration config;
            if (File.Exists(configFile))
            {
                config = await application.LoadApplicationConfigurationAsync(configFile, false, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                config = await application.LoadApplicationConfigurationAsync(false, ct)
                    .ConfigureAwait(false);
            }

            bool hasAppCert = await application.CheckApplicationInstanceCertificatesAsync(false, ct: ct)
                .ConfigureAwait(false);
            if (!hasAppCert)
            {
                m_logger.ApplicationCertificateNotFound();
            }

            m_clientOptions.Configuration = config;
            m_configuration = config;
        }

        private static async Task<ArrayOf<EndpointDescription>> DiscoverEndpointsInternalAsync(
            ApplicationConfiguration configuration,
            string discoveryUrl,
            CancellationToken ct)
        {
            var uri = new Uri(discoveryUrl);
            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                configuration,
                uri,
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);

            return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
        }

        private async Task<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration configuration,
            string endpointUrl,
            string? securityMode,
            string? securityPolicy,
            string authType,
            CancellationToken ct)
        {
            bool hasFilter = securityMode != null || securityPolicy != null;

            if (!hasFilter)
            {
                // Auto-select most secure, fall back to no-security
                return (await CoreClientUtils.SelectEndpointAsync(
                    configuration,
                    endpointUrl,
                    true,
                    Telemetry,
                    ct: ct).ConfigureAwait(false) ??
                    await CoreClientUtils.SelectEndpointAsync(
                        configuration,
                        endpointUrl,
                        false,
                        Telemetry,
                        ct: ct).ConfigureAwait(false)) ??
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound,
                        "No endpoints found at the specified URL.");
            }

            ArrayOf<EndpointDescription> allEndpoints =
                await DiscoverEndpointsInternalAsync(
                    configuration,
                    endpointUrl,
                    ct).ConfigureAwait(false);

            IEnumerable<EndpointDescription> candidates = allEndpoints.ToArray() ??
                [];

            // Filter to match the same transport scheme as the requested URL
            var requestUri = new Uri(endpointUrl);
            candidates = candidates.Where(ep =>
                ep.EndpointUrl != null &&
                ep.EndpointUrl.StartsWith(requestUri.Scheme, StringComparison.OrdinalIgnoreCase));

            if (securityMode != null)
            {
                MessageSecurityMode mode = ParseSecurityMode(securityMode);
                candidates = candidates.Where(ep => ep.SecurityMode == mode);
            }

            if (securityPolicy != null)
            {
                candidates = candidates.Where(ep =>
                    ep.SecurityPolicyUri != null &&
                    ep.SecurityPolicyUri.EndsWith(
                        "#" + securityPolicy, StringComparison.OrdinalIgnoreCase));
            }

            // Filter by auth compatibility
            UserTokenType requiredTokenType = ParseAuthTokenType(authType);
            candidates = candidates.Where(ep =>
                (ep.UserIdentityTokens.ToArray() ?? [])
                    .Any(t => t.TokenType == requiredTokenType));

            return candidates
                .OrderByDescending(ep => ep.SecurityLevel)
                .FirstOrDefault() ??
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "No endpoint matches the specified criteria: securityMode={0}, " +
                        "securityPolicy={1}, authType={2}. " +
                        "Use GetEndpoints to see available configurations.",
                        securityMode ?? "(any)",
                        securityPolicy ?? "(any)",
                        authType));
        }

        private static MessageSecurityMode ParseSecurityMode(string securityMode)
        {
            return securityMode.ToUpperInvariant() switch
            {
                "NONE" => MessageSecurityMode.None,
                "SIGN" => MessageSecurityMode.Sign,
                "SIGNANDENCRYPT" => MessageSecurityMode.SignAndEncrypt,
                _ => throw new ArgumentException(
                    $"Invalid securityMode '{securityMode}'. " +
                    "Expected 'None', 'Sign', or 'SignAndEncrypt'.",
                    nameof(securityMode))
            };
        }

        private static UserTokenType ParseAuthTokenType(string authType)
        {
            return authType.ToUpperInvariant() switch
            {
                "ANONYMOUS" => UserTokenType.Anonymous,
                "USERNAME" => UserTokenType.UserName,
                "CERTIFICATE" => UserTokenType.Certificate,
                _ => throw new ArgumentException(
                    $"Invalid authType '{authType}'. " +
                    "Expected 'Anonymous', 'Username', or 'Certificate'.",
                    nameof(authType))
            };
        }

        private static UserIdentity BuildUserIdentity(
            string authType,
            string? username,
            string? password)
        {
            switch (authType.ToUpperInvariant())
            {
                case "ANONYMOUS":
                    return new UserIdentity();
                case "USERNAME":
                    if (string.IsNullOrEmpty(username))
                    {
                        throw new ArgumentException(
                            "Username is required for 'Username' authentication.",
                            nameof(username));
                    }
                    return new UserIdentity(
                        username,
                        System.Text.Encoding.UTF8.GetBytes(password ?? string.Empty));
                default:
                    throw new ArgumentException(
                        $"Invalid authType '{authType}'. " +
                        "Expected 'Anonymous', 'Username', or 'Certificate'.",
                        nameof(authType));
            }
        }

        private void SessionConnectionStateChanged(string name, ConnectionStateChangedEventArgs e)
        {
            if (e.Error != null && ServiceResult.IsNotGood(e.Error))
            {
                m_logger.SessionStateChangedWithStatus(
                    name,
                    e.PreviousState,
                    e.NewState,
                    e.ReconnectAttempt,
                    e.Error);
                return;
            }

            m_logger.SessionStateChanged(
                name,
                e.PreviousState,
                e.NewState,
                e.ReconnectAttempt);
        }

        private void SubscribeSessionStateLogging(string name, ManagedSession session)
        {
            session.ConnectionStateChanged += (_, e) => SessionConnectionStateChanged(name, e);
            session.ChannelStateChanged += (_, e) => SessionChannelStateChanged(name, e);
        }

        private void SessionChannelStateChanged(string name, ChannelStateChange e)
        {
            if (e.Error != null && ServiceResult.IsNotGood(e.Error))
            {
                m_logger.SessionChannelStateChangedWithStatus(
                    name,
                    e.PreviousState,
                    e.NewState,
                    e.ReconnectAttempt,
                    e.Error);
                return;
            }

            m_logger.SessionChannelStateChanged(
                name,
                e.PreviousState,
                e.NewState,
                e.ReconnectAttempt);
        }

        private static string FormatSessionStatus(SessionInfo info)
        {
            if (!info.IsConnected)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}': Disconnected (was {1})",
                    info.Name,
                    info.Endpoint.EndpointUrl);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "'{0}': Connected to {1}. SessionName={2}, SessionId={3}, ServerUri={4}",
                info.Name,
                info.Endpoint.EndpointUrl,
                info.Session.SessionName,
                info.Session.SessionId,
                info.Session.ServerUris?.ToArray().FirstOrDefault() ?? "unknown");
        }

        private static bool AutoAcceptError(Certificate _, ServiceResult error)
        {
            return error.StatusCode == StatusCodes.BadCertificateUntrusted;
        }

        internal sealed class ConnectionValidationContext : IDisposable
        {
            public ConnectionValidationContext(
                ApplicationConfiguration configuration,
                CertificateManager? ownedCertificateManager)
            {
                Configuration = configuration ??
                    throw new ArgumentNullException(nameof(configuration));
                m_ownedCertificateManager = ownedCertificateManager;
            }

            public ApplicationConfiguration Configuration { get; }

            public bool IsIsolated => m_ownedCertificateManager != null;

            public bool UseSharedChannelManager => !IsIsolated;

            public void Dispose()
            {
                CertificateManager? certificateManager = Interlocked.Exchange(
                    ref m_ownedCertificateManager,
                    null);
                certificateManager?.Dispose();
            }

            private CertificateManager? m_ownedCertificateManager;
        }
    }

    internal static partial class OpcUaSessionManagerLog
    {
        [LoggerMessage(EventId = McpServerEventIds.OpcUaSessionManager + 0, Level = LogLevel.Information,
            Message = "Connecting to {EndpointUrl} as '{Name}'...")]
        public static partial void Connecting(this ILogger logger, [NetworkAddress] string endpointUrl, string name);

        [LoggerMessage(EventId = McpServerEventIds.OpcUaSessionManager + 1, Level = LogLevel.Information,
            Message = "Connected '{Name}'. SessionName={SessionName}, SessionId={SessionId}")]
        public static partial void Connected(this ILogger logger, string name, string sessionName, NodeId sessionId);

        [LoggerMessage(EventId = McpServerEventIds.OpcUaSessionManager + 2, Level = LogLevel.Warning,
            Message = "Application certificate not found. Security may be limited.")]
        public static partial void ApplicationCertificateNotFound(this ILogger logger);

        [LoggerMessage(EventId = McpServerEventIds.OpcUaSessionManager + 3, Level = LogLevel.Warning,
            Message = "Session '{Name}' state changed from {PreviousState} to {NewState}. " +
                "ReconnectAttempt={ReconnectAttempt}, Status={Status}")]
        public static partial void SessionStateChangedWithStatus(
            this ILogger logger,
            string name,
            ConnectionState previousState,
            ConnectionState newState,
            int reconnectAttempt,
            ServiceResult status);

        [LoggerMessage(EventId = McpServerEventIds.OpcUaSessionManager + 4, Level = LogLevel.Information,
            Message = "Session '{Name}' state changed from {PreviousState} to {NewState}. " +
                "ReconnectAttempt={ReconnectAttempt}")]
        public static partial void SessionStateChanged(
            this ILogger logger,
            string name,
            ConnectionState previousState,
            ConnectionState newState,
            int reconnectAttempt);

        [LoggerMessage(EventId = McpServerEventIds.OpcUaSessionManager + 5, Level = LogLevel.Warning,
            Message = "Session '{Name}' channel state changed from {PreviousState} to {NewState}. " +
                "ReconnectAttempt={ReconnectAttempt}, Status={Status}")]
        public static partial void SessionChannelStateChangedWithStatus(
            this ILogger logger,
            string name,
            ChannelState previousState,
            ChannelState newState,
            int reconnectAttempt,
            ServiceResult status);

        [LoggerMessage(EventId = McpServerEventIds.OpcUaSessionManager + 6, Level = LogLevel.Information,
            Message = "Session '{Name}' channel state changed from {PreviousState} to {NewState}. " +
                "ReconnectAttempt={ReconnectAttempt}")]
        public static partial void SessionChannelStateChanged(
            this ILogger logger,
            string name,
            ChannelState previousState,
            ChannelState newState,
            int reconnectAttempt);
    }
}
