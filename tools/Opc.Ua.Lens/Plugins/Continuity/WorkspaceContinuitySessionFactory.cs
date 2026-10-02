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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using UaLens.Connection;

namespace UaLens.Plugins.Continuity;

/// <summary>
/// Per-document, same-endpoint auxiliary sessions through the existing connection
/// backend and credential owner. It never copies a live identity or accept-once
/// trust, changes the primary connection, or implements reconnect itself.
/// </summary>
internal sealed class WorkspaceContinuitySessionFactory : IContinuitySessionFactory
{
    public WorkspaceContinuitySessionFactory(
        ConnectionService connection,
        ITelemetryContext telemetry,
        IConnectionBackend? backend = null,
        bool gracefulDurableSampleConfigured = false)
        : this(
            () => connection.Profile,
            (profile, ct) => connection.ResolveCredentialsAsync(profile, ct),
            telemetry,
            backend,
            gracefulDurableSampleConfigured)
    {
        ArgumentNullException.ThrowIfNull(connection);
    }

    public WorkspaceContinuitySessionFactory(
        Func<ConnectionProfile?> profile,
        Func<ConnectionProfile, CancellationToken, ValueTask<IClientIdentityProvider>> resolveCredentials,
        ITelemetryContext telemetry,
        IConnectionBackend? backend = null,
        bool gracefulDurableSampleConfigured = false)
    {
        m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
        m_resolveCredentials = resolveCredentials ?? throw new ArgumentNullException(nameof(resolveCredentials));
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_backend = backend;
        m_gracefulDurableSampleConfigured = gracefulDurableSampleConfigured;
    }

    public ContinuitySetup CheckSetup(ContinuityScenario scenario)
    {
        ConnectionProfile? profile = m_profile();
        if (profile is null)
        {
            return new(ContinuityAvailability.RequiresConfiguration, "Connect using an explicit profile first.");
        }
        if (scenario == ContinuityScenario.ConfiguredFailover)
        {
            return new(ContinuityAvailability.RequiresConfiguration,
                "Inject ConfiguredContinuitySessionFactory with an explicitly authorized redundant set and " +
                "the existing redundancy provider. Same-endpoint auxiliary sessions are not server redundancy.");
        }
        if (scenario == ContinuityScenario.GracefulDurableRestore && !m_gracefulDurableSampleConfigured)
        {
            return new(ContinuityAvailability.RequiresConfiguration,
                "Configure the repository durable sample/store and enable its lab setup. Quickstarts persists " +
                "only graceful shutdowns and excludes issued-token subscriptions.");
        }
        if (profile.ReverseConnection is not null)
        {
            return new(ContinuityAvailability.RequiresConfiguration,
                "Reverse-connect lab sessions require their own explicitly configured listener/peer owner.");
        }
        if (profile.ApplicationIdentityId is not null && m_backend is not IConfiguredConnectionBackend)
        {
            return new(ContinuityAvailability.RequiresConfiguration,
                "Inject the configured connection backend to preserve the selected application identity.");
        }
        if (profile.IdentityType == UserTokenType.UserName && profile.CredentialReference is null)
        {
            return new(ContinuityAvailability.RequiresConfiguration,
                "Configure a credential-provider reference for the same username. Interactive passwords are " +
                "not copied from the primary session into lab sessions.");
        }
        return new(ContinuityAvailability.Supported,
            "Same-endpoint lab sessions reacquire the selected identity through its owner. The certificate " +
            "must be persistently trusted; accept-once trust is not inherited. Server transfer support is unproven.");
    }

    public async Task<IContinuitySessionLease> OpenAsync(ContinuitySessionPurpose purpose, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (purpose == ContinuitySessionPurpose.RedundantTarget)
        {
            throw new InvalidOperationException("An explicitly configured redundancy provider is required.");
        }
        if (purpose == ContinuitySessionPurpose.Source)
        {
            m_sourceProfile = m_profile() ??
                throw new InvalidOperationException("The primary profile was released.");
        }
        ConnectionProfile profile = m_sourceProfile ??
            throw new InvalidOperationException("Open the scenario's source session before restoring.");
        if (profile.ReverseConnection is not null)
        {
            throw new InvalidOperationException("A dedicated reverse-connect lab owner is required.");
        }
        var resources = new WorkspaceContinuitySessionLease(m_telemetry, m_backend);
        try
        {
            IConnectionBackend backend = resources.Backend;
            ApplicationConfiguration configuration = backend is IConfiguredConnectionBackend configured
                ? await configured.CreateConfigurationAsync(profile, ct).ConfigureAwait(false)
                : await backend.CreateConfigurationAsync(ct).ConfigureAwait(false);
            resources.Configuration = configuration;
            IClientIdentityProvider provider = await m_resolveCredentials(profile, ct).ConfigureAwait(false);
            resources.Provider = provider;
            ArrayOf<EndpointDescription> endpoints = await backend.DiscoverAsync(
                configuration, profile.EndpointUrl, ct).ConfigureAwait(false);
            if (endpoints.Count > 256)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            EndpointDescription? selected = null;
            foreach (EndpointDescription endpoint in endpoints)
            {
                if (profile.MatchesEndpoint(endpoint))
                {
                    foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
                    {
                        if (profile.MatchesPolicy(policy))
                        {
                            selected = endpoint;
                            break;
                        }
                    }
                }
                if (selected is not null)
                {
                    break;
                }
            }
            if (selected is null)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }
            resources.Connection = await backend.ConnectAsync(
                configuration, selected, profile, new ProfileIdentityProvider(profile, provider), ct)
                .ConfigureAwait(false);
            if (resources.Session is not ManagedSession)
            {
                throw new InvalidOperationException("The configured backend must return a ManagedSession.");
            }
            return resources;
        }
        catch
        {
            await resources.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private readonly Func<ConnectionProfile?> m_profile;
    private readonly Func<ConnectionProfile, CancellationToken, ValueTask<IClientIdentityProvider>>
        m_resolveCredentials;
    private readonly ITelemetryContext m_telemetry;
    private readonly IConnectionBackend? m_backend;
    private readonly bool m_gracefulDurableSampleConfigured;
    private ConnectionProfile? m_sourceProfile;
}

/// <summary>
/// Owns an auxiliary connection's configuration and acquired provider; the injected
/// shared backend, when present, is deliberately not part of this ownership.
/// </summary>
internal sealed class WorkspaceContinuitySessionLease : IContinuitySessionLease
{
    public WorkspaceContinuitySessionLease(ITelemetryContext telemetry, IConnectionBackend? backend)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        if (backend is null)
        {
            m_ownedBackend = new StackConnectionBackend(telemetry);
            Backend = m_ownedBackend;
        }
        else
        {
            Backend = backend;
        }
    }

    public IConnectionBackend Backend { get; }

    public ISession Session => Connection?.Session ??
        throw new InvalidOperationException("The auxiliary session is not connected.");

    public ApplicationConfiguration? Configuration { get; set; }

    public IClientIdentityProvider? Provider { get; set; }

    public IConnectionSession? Connection { get; set; }

    public async Task CloseAsync(bool retainSubscriptions, CancellationToken ct)
    {
        if (Session is not ManagedSession managed)
        {
            throw new InvalidOperationException("The auxiliary session must be a ManagedSession.");
        }
        bool wasConnected = managed.Connected;
        managed.DeleteSubscriptionsOnClose = !retainSubscriptions;
        StatusCode result = await managed.CloseAsync(10000, closeChannel: true, ct).ConfigureAwait(false);
        if (StatusCode.IsBad(result))
        {
            throw new ServiceResultException(result);
        }
        if (!wasConnected)
        {
            throw new ServiceResultException(StatusCodes.BadNotConnected,
                "The auxiliary session was already disconnected; its server close outcome is unconfirmed.");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_disposeTask ??= DisposeCoreAsync();
            return new ValueTask(m_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>(4);
        try
        {
            try
            {
                try
                {
                    await DisposeResourceAsync(Connection, failures).ConfigureAwait(false);
                }
                finally
                {
                    await DisposeResourceAsync(Provider, failures).ConfigureAwait(false);
                }
            }
            finally
            {
                await DisposeResourceAsync(Configuration?.CertificateManager, failures).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await DisposeResourceAsync(m_ownedBackend, failures).ConfigureAwait(false);
            }
            finally
            {
                Connection = null;
                Provider = null;
                Configuration = null;
            }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Auxiliary connection cleanup failed.", failures);
        }
    }

    private static async ValueTask DisposeResourceAsync<T>(T? resource, List<Exception> failures)
        where T : class
    {
        try
        {
            if (resource is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync().ConfigureAwait(false);
            }
            else if (resource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            TimeoutException or IOException or InvalidOperationException or NotSupportedException or
            UnauthorizedAccessException or AggregateException)
        {
            failures.Add(exception);
        }
    }

    private readonly System.Threading.Lock m_gate = new();
    private readonly StackConnectionBackend? m_ownedBackend;
    private Task? m_disposeTask;
}
