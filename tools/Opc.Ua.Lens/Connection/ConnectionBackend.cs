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

internal sealed class StackConnectionBackend : IConnectionBackend
{
    public StackConnectionBackend(ITelemetryContext telemetry)
        : this(telemetry, ct => AppConfig.BuildAsync(telemetry, ct))
    {
    }

    public StackConnectionBackend(
        ITelemetryContext telemetry,
        Func<CancellationToken, Task<ApplicationConfiguration>> configurationFactory)
    {
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_configurationFactory = configurationFactory ?? throw new ArgumentNullException(nameof(configurationFactory));
    }

    public Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken ct)
    {
        return m_configurationFactory(ct);
    }

    public async Task<ArrayOf<EndpointDescription>> DiscoverAsync(
        ApplicationConfiguration configuration,
        string endpointUrl,
        CancellationToken ct)
    {
        var endpointConfiguration = EndpointConfiguration.Create(configuration);
        endpointConfiguration.OperationTimeout = 10_000;
        using DiscoveryClient client = await DiscoveryClient.CreateAsync(
            configuration,
            new Uri(endpointUrl),
            endpointConfiguration,
            ct: ct).ConfigureAwait(false);
        return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
    }

    public async Task<IConnectionSession> ConnectAsync(
        ApplicationConfiguration configuration,
        EndpointDescription endpoint,
        ConnectionProfile profile,
        IClientIdentityProvider identityProvider,
        CancellationToken ct)
    {
        profile.RequireMatch(endpoint);
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
        IUserIdentity identity = await identityProvider.AcquireIdentityAsync(
            endpoint,
            configuration.CreateMessageContext(),
            configuration.SecurityConfiguration.SupportedSecurityPolicies,
            ct).ConfigureAwait(false);

        try
        {
            // Master's WithIdentityProvider first opens an Anonymous session and
            // only then updates identity. Materialize before opening instead, so
            // authenticated-only endpoints never receive an Anonymous activation.
            ManagedSession session = await new ManagedSessionBuilder(configuration, m_telemetry)
                .UseEndpoint(configuredEndpoint)
                .WithUserIdentity(identity)
                .WithSessionName("UaLens")
                .WithCheckDomain()
                .WithReconnectPolicy(new InitialConnectPolicy())
                .WithServerRedundancy(new ProfileRedundancyHandler(
                    profile,
                    new DefaultServerRedundancyHandler(new DefaultRedundantServerEndpointResolver(m_telemetry))))
                .UseSubscriptionEngine(engineFactory)
                .ConnectAsync(ct)
                .ConfigureAwait(false);
            return new ManagedConnectionSession(session, identity);
        }
        catch
        {
            await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
            throw;
        }
    }

    private readonly ITelemetryContext m_telemetry;
    private readonly Func<CancellationToken, Task<ApplicationConfiguration>> m_configurationFactory;
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
    {
        m_session = session;
        m_identity = identity;
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
            await ConnectionCredentials.ReleaseIdentityAsync(m_identity).ConfigureAwait(false);
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
    private readonly IUserIdentity m_identity;
    private ConnectionSessionState m_state;
    private int m_disposed;
}
