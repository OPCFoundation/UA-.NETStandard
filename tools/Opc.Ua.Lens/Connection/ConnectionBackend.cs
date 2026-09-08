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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace UaLens.Connection;

/// <summary>
/// Protocol boundary for connection orchestration. Each configuration has a
/// private certificate manager, owned by the caller. Connect performs one
/// initial attempt; only an established ManagedSession reconnects automatically.
/// </summary>
internal interface IConnectionBackend
{
    Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken ct);

    Task<ArrayOf<EndpointDescription>> DiscoverAsync(
        ApplicationConfiguration configuration,
        string endpointUrl,
        CancellationToken ct);

    Task<IConnectionSession> ConnectAsync(
        ApplicationConfiguration configuration,
        EndpointDescription endpoint,
        ConnectionProfile profile,
        IClientIdentityProvider identityProvider,
        CancellationToken ct);
}

internal interface IConfiguredConnectionBackend : IConnectionBackend
{
    ConnectionTransportCatalog Transports { get; }

    ConnectionConfigurationCatalog Configurations { get; }

    ReverseConnectionService ReverseConnections { get; }

    Task<ApplicationConfiguration> CreateConfigurationAsync(
        ConnectionProfile? profile,
        CancellationToken ct);

    Task<ArrayOf<EndpointDescription>> DiscoverAsync(
        ApplicationConfiguration configuration,
        ConnectionSetupSelection setup,
        CancellationToken ct);
}

internal sealed record ConnectionSessionState(ConnectionPhase Phase, string? Error = null);

/// <summary>
/// Owns the connected stack session and its identity. Notifications include their
/// sender so late events from an old session cannot change a newer connection.
/// </summary>
internal interface IConnectionSession : IAsyncDisposable
{
    ISession Session { get; }

    ConnectionSessionState State { get; }

    event Action<IConnectionSession, ConnectionSessionState>? StateChanged;
}

internal sealed class StackConnectionBackend : IConfiguredConnectionBackend, IAsyncDisposable
{
    public StackConnectionBackend(ITelemetryContext telemetry)
        : this(telemetry, ct => AppConfig.BuildAsync(telemetry, ct))
    {
    }

    public StackConnectionBackend(
        ITelemetryContext telemetry,
        Func<CancellationToken, Task<ApplicationConfiguration>> configurationFactory,
        ConnectionTransportCatalog? transports = null,
        ConnectionConfigurationCatalog? configurations = null,
        ReverseConnectionService? reverseConnections = null)
    {
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_configurationFactory = configurationFactory ?? throw new ArgumentNullException(nameof(configurationFactory));
        Transports = transports ?? new ConnectionTransportCatalog();
        Configurations = configurations ?? new ConnectionConfigurationCatalog();
        ReverseConnections = reverseConnections ?? new ReverseConnectionService(
            new StackReverseConnectionRuntimeFactory(telemetry, Transports, Configurations));
        m_ownsReverseConnections = reverseConnections is null;
    }

    public ConnectionTransportCatalog Transports { get; }

    public ConnectionConfigurationCatalog Configurations { get; }

    public ReverseConnectionService ReverseConnections { get; }

    public Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken ct)
    {
        return m_configurationFactory(ct);
    }

    public Task<ApplicationConfiguration> CreateConfigurationAsync(
        ConnectionProfile? profile,
        CancellationToken ct)
    {
        return profile?.ApplicationIdentityId is { } id
            ? Configurations.ResolveApplication(id).CreateAsync(profile.SecurityPolicyUri, ct)
            : CreateConfigurationAsync(ct);
    }

    public Task<ArrayOf<EndpointDescription>> DiscoverAsync(
        ApplicationConfiguration configuration,
        string endpointUrl,
        CancellationToken ct)
    {
        return DiscoverAsync(configuration, new ConnectionSetupSelection(endpointUrl), ct);
    }

    public async Task<ArrayOf<EndpointDescription>> DiscoverAsync(
        ApplicationConfiguration configuration,
        ConnectionSetupSelection setup,
        CancellationToken ct)
    {
        Transports.RequireForward(setup.EndpointUrl);
        if (configuration.CertificateManager is null ||
            configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates ||
            configuration.SecurityConfiguration.UseValidatedCertificates)
        {
            throw new InvalidOperationException("Discovery requires a fail-closed certificate configuration.");
        }
        var endpointConfiguration = EndpointConfiguration.Create(configuration);
        endpointConfiguration.OperationTimeout = 10_000;
        var channels = new ClientChannelManager(configuration, Transports);
        await using (channels.ConfigureAwait(false))
        {
            if (setup.ReverseConnection is not { } reverse)
            {
                using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                    channels, new Uri(setup.EndpointUrl), endpointConfiguration, m_telemetry, ct: ct)
                    .ConfigureAwait(false);
                return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
            }
            reverse.Validate(setup.EndpointUrl);
            using ReverseConnectionLease lease = ReverseConnections.Acquire(reverse);
            ITransportWaitingConnection waiting = await ReverseConnections.WaitAsync(reverse, ct).ConfigureAwait(false);
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription(reverse.EndpointUrl)
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            }, endpointConfiguration);
            ITransportChannel channel = await channels.CreateChannelAsync(
                endpoint, configuration.CreateMessageContext(), clientCertificate: null, connection: waiting, ct: ct)
                .ConfigureAwait(false);
            using DiscoveryClient discovery =
                await DiscoveryClient.CreateAsync(channel, m_telemetry, ct: ct).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(default, ct).ConfigureAwait(false);
            var matched = new System.Collections.Generic.List<EndpointDescription>();
            foreach (EndpointDescription candidate in endpoints)
            {
                if (ConnectionProfile.EndpointUrlsMatch(candidate.EndpointUrl, reverse.EndpointUrl) &&
                    string.Equals(candidate.Server?.ApplicationUri, reverse.ServerUri, StringComparison.Ordinal))
                {
                    matched.Add(candidate);
                }
            }
            if (matched.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTcpEndpointUrlInvalid,
                    "The reverse server did not advertise the configured endpoint and ServerUri.");
            }
            return [.. matched];
        }
    }

    public async Task<IConnectionSession> ConnectAsync(
        ApplicationConfiguration configuration,
        EndpointDescription endpoint,
        ConnectionProfile profile,
        IClientIdentityProvider identityProvider,
        CancellationToken ct)
    {
        profile.RequireMatch(endpoint);
        Transports.RequireForward(profile.EndpointUrl);
        if (ConnectionTransportCatalog.GetSessionProfileUnavailableReason(endpoint) is { } unavailable)
        {
            throw new NotSupportedException(unavailable);
        }
        var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration))
        {
            UpdateBeforeConnect = false
        };
        ISubscriptionEngineFactory engineFactory = profile.Engine == SubscriptionEngineKind.Classic
            ? ClassicSubscriptionEngineFactory.Instance
            : DefaultSubscriptionEngineFactory.Instance;
        string tokenSecurityPolicy = string.IsNullOrEmpty(profile.UserTokenSecurityPolicyUri)
            ? profile.SecurityPolicyUri
            : profile.UserTokenSecurityPolicyUri;
        if (!endpoint.ServerCertificate.IsEmpty &&
            (profile.SecurityMode != MessageSecurityMode.None ||
                (profile.IdentityType != UserTokenType.Anonymous && tokenSecurityPolicy != SecurityPolicies.None)))
        {
            using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                endpoint.ServerCertificate,
                m_telemetry);
            if (chain.Count > 0)
            {
                configuration.CertificateManager.ValidateDomains(chain[0], configuredEndpoint);
                configuration.CertificateManager.ValidateApplicationUri(chain[0], configuredEndpoint);
            }
            Opc.Ua.CertificateValidationResult validation = await configuration.CertificateManager
                .ValidateAsync(chain, ct: ct)
                .ConfigureAwait(false);
            validation.ThrowIfInvalid();
        }
        var resources = new PendingConnectionResources(configuration, Transports, identityProvider);
        await using (resources.ConfigureAwait(false))
        {
            var factory = new ProviderSessionFactory(
                new ChannelManagerSessionFactory(resources.Channels, m_telemetry, engineFactory: engineFactory),
                resources.Identities,
                profile);
            var builder = new ManagedSessionBuilder(configuration, m_telemetry)
                .UseEndpoint(configuredEndpoint)
                .WithIdentityProvider(resources.Identities)
                .UseSessionFactory(factory)
                .WithSessionName("UaLens")
                .WithCheckDomain()
                .WithReconnectPolicy(new InitialConnectPolicy())
                .WithServerRedundancy(new ProfileRedundancyHandler(
                    profile,
                    new DefaultServerRedundancyHandler(new DefaultRedundantServerEndpointResolver(m_telemetry))))
                .UseSubscriptionEngine(engineFactory);
            if (profile.ReverseConnection is { } reverse)
            {
                resources.ReverseLease = ReverseConnections.Acquire(reverse);
                builder.UseReverseConnect(resources.ReverseLease.Manager, new Uri(reverse.ServerUri));
            }
            resources.Session = await builder.ConnectAsync(ct).ConfigureAwait(false);
            return resources.Transfer();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (m_ownsReverseConnections)
        {
            await ReverseConnections.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class PendingConnectionResources : IAsyncDisposable
    {
        public PendingConnectionResources(
            ApplicationConfiguration configuration,
            ConnectionTransportCatalog transports,
            IClientIdentityProvider identityProvider)
        {
            ArgumentNullException.ThrowIfNull(identityProvider);
            Identities = new ConnectionSessionIdentityProvider(identityProvider);
            Channels = new ClientChannelManager(configuration, transports);
        }

        public ConnectionSessionIdentityProvider Identities { get; }

        public ClientChannelManager Channels { get; }

        public ManagedSession? Session { get; set; }

        public ReverseConnectionLease? ReverseLease { get; set; }

        public ManagedConnectionSession Transfer()
        {
            if (m_transferred || Session is null)
            {
                throw new InvalidOperationException("A completed connection can be transferred exactly once.");
            }
            var connection = new ManagedConnectionSession(Session, Identities, Channels, ReverseLease);
            m_transferred = true;
            return connection;
        }

        public async ValueTask DisposeAsync()
        {
            if (m_transferred)
            {
                return;
            }
            try
            {
                if (Session is not null)
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await Identities.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await Channels.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        ReverseLease?.Dispose();
                    }
                }
            }
        }
        private bool m_transferred;
    }

    private readonly ITelemetryContext m_telemetry;
    private readonly Func<CancellationToken, Task<ApplicationConfiguration>> m_configurationFactory;
    private readonly bool m_ownsReverseConnections;
}

/// <summary>
/// Preserves stack redundancy discovery without allowing automatic failover to
/// leave the explicitly authorized endpoint/security/identity profile.
/// Accept-once cannot follow the same certificate to a different endpoint.
/// </summary>
internal sealed class ProfileRedundancyHandler : IServerRedundancyHandler
{
    public ProfileRedundancyHandler(ConnectionProfile profile, IServerRedundancyHandler inner)
    {
        m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
        ISession session,
        CancellationToken ct = default)
    {
        return m_inner.FetchRedundancyInfoAsync(session, ct);
    }

    public ConfiguredEndpoint? SelectFailoverTarget(
        ServerRedundancyInfo redundancyInfo,
        ConfiguredEndpoint currentEndpoint)
    {
        ConfiguredEndpoint? endpoint = m_inner.SelectFailoverTarget(redundancyInfo, currentEndpoint);
        if (endpoint is not null)
        {
            m_profile.RequireMatch(endpoint.Description);
        }
        return endpoint;
    }

    private readonly ConnectionProfile m_profile;
    private readonly IServerRedundancyHandler m_inner;
}

/// <summary>
/// Lets the async trust coordinator observe an initial validation failure
/// immediately. After the first success, all retry mechanics remain the stack's
/// default policy; this policy creates no worker, timer or reconnect controller.
/// </summary>
internal sealed class InitialConnectPolicy : IReconnectPolicy
{
    public TimeSpan? GetNextDelay(int attempt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Volatile.Read(ref m_connected) ? m_inner.GetNextDelay(attempt, ct) : null;
    }

    public bool TryGetNextDelay(
        int attempt,
        StatusCode lastStatus,
        TimeSpan? serverRetryAfter,
        out TimeSpan? delay,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Volatile.Read(ref m_connected))
        {
            return m_inner.TryGetNextDelay(attempt, lastStatus, serverRetryAfter, out delay, ct);
        }
        delay = null;
        return true;
    }

    public void Reset()
    {
        m_inner.Reset();
        Volatile.Write(ref m_connected, true);
    }

    private readonly ReconnectPolicy m_inner = new();
    private bool m_connected;
}

internal sealed class ManagedConnectionSession : IConnectionSession
{
    public ManagedConnectionSession(ManagedSession session, IUserIdentity identity)
        : this(session)
    {
        m_identity = identity;
    }

    public ManagedConnectionSession(
        ManagedSession session,
        ConnectionSessionIdentityProvider identities,
        IAsyncDisposable channels,
        ReverseConnectionLease? reverseLease)
        : this(session)
    {
        m_identities = identities;
        m_channels = channels;
        m_reverseLease = reverseLease;
    }

    private ManagedConnectionSession(ManagedSession session)
    {
        m_session = session;
        m_state = new ConnectionSessionState(session.Connected
            ? ConnectionPhase.Connected
            : ConnectionPhase.Disconnected);
        session.ConnectionStateChanged += OnConnectionStateChanged;
        session.ChannelStateChanged += OnChannelStateChanged;
    }

    public ISession Session => m_session;

    public ConnectionSessionState State => Volatile.Read(ref m_state);

    public event Action<IConnectionSession, ConnectionSessionState>? StateChanged;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) != 0)
        {
            return;
        }
        m_session.ConnectionStateChanged -= OnConnectionStateChanged;
        m_session.ChannelStateChanged -= OnChannelStateChanged;
        try
        {
            await m_session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (m_identity is not null)
                {
                    await ConnectionCredentials.ReleaseIdentityAsync(m_identity).ConfigureAwait(false);
                }
                if (m_identities is not null)
                {
                    await m_identities.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (m_channels is not null)
                    {
                        await m_channels.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_reverseLease?.Dispose();
                }
            }
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs args)
    {
        SetState(args.NewState switch
        {
            Opc.Ua.Client.ConnectionState.Connected => ConnectionPhase.Connected,
            Opc.Ua.Client.ConnectionState.Connecting => ConnectionPhase.Connecting,
            Opc.Ua.Client.ConnectionState.Reconnecting or Opc.Ua.Client.ConnectionState.Failover =>
                ConnectionPhase.Reconnecting,
            _ => args.Error is not null && ServiceResult.IsBad(args.Error)
                ? ConnectionPhase.Failed
                : ConnectionPhase.Disconnected
        }, args.Error);
    }

    private void OnChannelStateChanged(ManagedSession session, ChannelStateChange change)
    {
        SetState(change.NewState switch
        {
            ChannelState.Ready when !session.Reconnecting => ConnectionPhase.Connected,
            ChannelState.Faulted => ConnectionPhase.Failed,
            ChannelState.Closed or ChannelState.Disconnected => ConnectionPhase.Disconnected,
            _ => ConnectionPhase.Reconnecting
        }, change.Error);
    }

    private void SetState(ConnectionPhase phase, ServiceResult? error)
    {
        if (Volatile.Read(ref m_disposed) != 0)
        {
            return;
        }
        var state = new ConnectionSessionState(phase, error?.ToString());
        Volatile.Write(ref m_state, state);
        StateChanged?.Invoke(this, state);
    }

    private readonly ManagedSession m_session;
    private readonly IUserIdentity? m_identity;
    private readonly ConnectionSessionIdentityProvider? m_identities;
    private readonly IAsyncDisposable? m_channels;
    private readonly ReverseConnectionLease? m_reverseLease;
    private ConnectionSessionState m_state;
    private int m_disposed;
}
