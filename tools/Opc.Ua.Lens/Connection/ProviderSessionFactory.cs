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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace UaLens.Connection;

/// <summary>
/// Supplies the selected identity before the FIRST activation. The stack's
/// ManagedSession provider path otherwise initially opens anonymously, then
/// updates identity. Keeping the same provider on ManagedSession also enables
/// its existing proactive refresh and reacquisition machinery.
/// </summary>
internal sealed class ProviderSessionFactory : ISessionFactory, ISecurityPolicyRegistryProvider
{
    public ProviderSessionFactory(
        ISessionFactory inner,
        IClientIdentityProvider provider,
        ConnectionProfile profile)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public DiagnosticsMasks ReturnDiagnostics
    {
        get => m_inner.ReturnDiagnostics;
        set => m_inner.ReturnDiagnostics = value;
    }

    public ITelemetryContext Telemetry => m_inner.Telemetry;

    public ISecurityPolicyRegistry? SecurityPolicyRegistry =>
        (m_inner as ISecurityPolicyRegistryProvider)?.SecurityPolicyRegistry;

    public ISession Create(
        ITransportChannel channel,
        ApplicationConfiguration configuration,
        ConfiguredEndpoint endpoint,
        Certificate? clientCertificate = null,
        CertificateCollection? clientCertificateChain = null,
        ArrayOf<EndpointDescription> availableEndpoints = default,
        ArrayOf<string> discoveryProfileUris = default)
    {
        m_profile.RequireMatch(endpoint.Description);
        return m_inner.Create(channel, configuration, endpoint, clientCertificate,
            clientCertificateChain, availableEndpoints, discoveryProfileUris);
    }

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
        return CreateAsync(configuration, endpoint, updateBeforeConnect, true,
            sessionName, sessionTimeout, identity, preferredLocales, ct);
    }

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
        IUserIdentity selected = await AcquireAsync(configuration, endpoint, ct).ConfigureAwait(false);
        return await m_inner.CreateAsync(configuration, endpoint, false, true,
            sessionName, sessionTimeout, selected, preferredLocales, ct).ConfigureAwait(false);
    }

    public Task<ITransportChannel> CreateChannelAsync(
        ApplicationConfiguration configuration,
        ITransportWaitingConnection connection,
        ConfiguredEndpoint endpoint,
        bool updateBeforeConnect,
        bool checkDomain,
        CancellationToken ct = default)
    {
        RequirePeer(endpoint, connection);
        return m_inner.CreateChannelAsync(configuration, connection, endpoint, false, true, ct);
    }

    public async Task<ISession> CreateAsync(
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
        RequirePeer(endpoint, connection);
        IUserIdentity selected = await AcquireAsync(configuration, endpoint, ct).ConfigureAwait(false);
        return await m_inner.CreateAsync(configuration, connection, endpoint, false, true,
            sessionName, sessionTimeout, selected, preferredLocales, ct).ConfigureAwait(false);
    }

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
        IUserIdentity selected = await AcquireAsync(configuration, endpoint, ct).ConfigureAwait(false);
        return await m_inner.CreateAsync(configuration, reverseConnectManager, endpoint, false, true,
            sessionName, sessionTimeout, selected, preferredLocales, ct).ConfigureAwait(false);
    }

    public Task<ISession> RecreateAsync(ISession sessionTemplate, CancellationToken ct = default)
    {
        m_profile.RequireMatch(sessionTemplate.ConfiguredEndpoint.Description);
        return m_inner.RecreateAsync(sessionTemplate, ct);
    }

    public Task<ISession> RecreateAsync(
        ISession sessionTemplate,
        ITransportWaitingConnection connection,
        CancellationToken ct = default)
    {
        RequirePeer(sessionTemplate.ConfiguredEndpoint, connection);
        return m_inner.RecreateAsync(sessionTemplate, connection, ct);
    }

    public Task<ISession> RecreateAsync(
        ISession sessionTemplate,
        ITransportChannel transportChannel,
        CancellationToken ct = default)
    {
        m_profile.RequireMatch(sessionTemplate.ConfiguredEndpoint.Description);
        return m_inner.RecreateAsync(sessionTemplate, transportChannel, ct);
    }

    private ValueTask<IUserIdentity> AcquireAsync(
        ApplicationConfiguration configuration,
        ConfiguredEndpoint endpoint,
        CancellationToken ct)
    {
        m_profile.RequireMatch(endpoint.Description);
        return m_provider.AcquireIdentityAsync(new IdentitySelectionContext(
            endpoint.Description, endpoint.Description.UserIdentityTokens, configuration.CreateMessageContext(),
            configuration.SecurityConfiguration.SupportedSecurityPolicies)
        {
            SecurityPolicyRegistry = SecurityPolicyRegistry
        }, ct);
    }

    private void RequirePeer(ConfiguredEndpoint endpoint, ITransportWaitingConnection connection)
    {
        m_profile.RequireMatch(endpoint.Description);
        if (m_profile.ReverseConnection is not { } reverse ||
            !reverse.MatchesPeer(connection.ServerUri, connection.EndpointUrl))
        {
            throw new ServiceResultException(
                StatusCodes.BadTcpEndpointUrlInvalid,
                "The waiting peer does not match the selected reverse connection.");
        }
    }

    private readonly ISessionFactory m_inner;
    private readonly IClientIdentityProvider m_provider;
    private readonly ConnectionProfile m_profile;
}

/// <summary>
/// Session-lifetime ownership for identities from both initial activation and
/// the stack's refresh loop, including failed/canceled updates. Providers and
/// device/secret sources are borrowed; only acquired identity material is owned.
/// </summary>
internal sealed class ConnectionSessionIdentityProvider : IClientIdentityProvider, IAsyncDisposable
{
    public ConnectionSessionIdentityProvider(IClientIdentityProvider inner)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IReadOnlyList<UserTokenType> SupportedTokenTypes => m_inner.SupportedTokenTypes;

    public IReadOnlyList<string> SupportedIssuedTokenProfileUris => m_inner.SupportedIssuedTokenProfileUris;

    public DateTime ExpiresAt => m_inner.ExpiresAt;

    public ValueTask<CanSatisfyResult> CanSatisfyAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }
        return m_inner.CanSatisfyAsync(policy, context, ct);
    }

    public async ValueTask<IUserIdentity> GetIdentityAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }
        IUserIdentity identity = await m_inner.GetIdentityAsync(policy, context, ct).ConfigureAwait(false);
        bool transferred = false;
        try
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                ct.ThrowIfCancellationRequested();
                m_identities.Add(identity);
                transferred = true;
            }
            return identity;
        }
        finally
        {
            if (!transferred)
            {
                await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? start = null;
        Task disposal;
        lock (m_gate)
        {
            if (m_disposeTask is null)
            {
                m_disposed = true;
                List<IUserIdentity> identities = m_identities;
                m_identities = [];
                start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposeTask = DisposeIdentitiesAsync(identities, start.Task);
            }
            disposal = m_disposeTask;
        }
        start?.TrySetResult();
        return new ValueTask(disposal);
    }

    private static async Task DisposeIdentitiesAsync(List<IUserIdentity> identities, Task started)
    {
        await started.ConfigureAwait(false);
        var releases = new Task[identities.Count];
        for (int i = 0; i < identities.Count; i++)
        {
            releases[i] = ConnectionCredentials.ReleaseIdentityAsync(identities[i]).AsTask();
        }
        await Task.WhenAll(releases).ConfigureAwait(false);
    }

    private readonly IClientIdentityProvider m_inner;
    private readonly Lock m_gate = new();
    private List<IUserIdentity> m_identities = [];
    private Task? m_disposeTask;
    private bool m_disposed;
}
