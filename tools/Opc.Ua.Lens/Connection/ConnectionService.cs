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
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using UaLens.Diagnostics;
using UaLens.Subscriptions;

namespace UaLens.Connection;

/// <summary>
/// The primary connection's owner. Keeps profiles separate from credential
/// material, serializes replacement/cleanup, and coordinates asynchronous trust
/// decisions outside the stack's synchronous validation callback.
/// </summary>
internal sealed class ConnectionService : IConnectionWorkspace
{
    public ConnectionService(ITelemetryContext telemetry)
        : this(telemetry, null)
    {
    }

    public ConnectionService(ITelemetryContext telemetry, PublishLogObserver? publishLog)
        : this(telemetry, publishLog, new StackConnectionBackend(telemetry), new ProfileCredentialProvider())
    {
    }

    public ConnectionService(
        ITelemetryContext telemetry,
        PublishLogObserver? publishLog,
        IConnectionBackend backend,
        IConnectionCredentialProvider credentialProvider)
    {
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
        m_credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        m_log = telemetry.CreateLogger<ConnectionService>();
        PublishLog = publishLog;
    }

    public PublishLogObserver? PublishLog { get; }

    public ConnectionSnapshot Snapshot => Volatile.Read(ref m_snapshot);

    public ConnectionProfile? Profile => Snapshot.Profile;

    public bool IsConnected
    {
        get
        {
            lock (m_stateGate)
            {
                return m_snapshot.IsConnected &&
                    m_connection is { State.Phase: ConnectionPhase.Connected } current &&
                    current.Session.Connected;
            }
        }
    }

    public ISession? CurrentSession
    {
        get
        {
            lock (m_stateGate)
            {
                return m_connection?.Session;
            }
        }
    }

    public ManagedSession? Session => CurrentSession as ManagedSession;

    public SubscriptionEngineKind Engine => Snapshot.Profile?.Engine ?? SubscriptionEngineKind.ChannelV2;

    public ISubscriptionAdapter? Adapter
    {
        get
        {
            lock (m_stateGate)
            {
                return m_activeAdapter;
            }
        }
        set
        {
            lock (m_stateGate)
            {
                m_activeAdapter = value;
            }
        }
    }

    /// <summary>
    /// Raised on the thread completing a transition. Subscribers must marshal
    /// UI work and use Snapshot.Generation to distinguish replacement from a
    /// transient reconnect. No subscription adapters are created by this event.
    /// </summary>
    public event Action? StateChanged;

    /// <inheritdoc/>
    public event Func<CancellationToken, Task>? ConnectionChangedAsync;

    public ISubscriptionAdapter CreateAdapter()
    {
        return CreateAdapter(trackLifetime: true);
    }

    /// <summary>
    /// Creates an adapter. When trackLifetime is false, the caller owns disposal
    /// and must await it during null-session ConnectionChangedAsync delivery.
    /// These adapters are not passed to ForgetAdapter; the workspace is their owner.
    /// </summary>
    public ISubscriptionAdapter CreateAdapter(bool trackLifetime)
    {
        lock (m_stateGate)
        {
            ThrowIfDisposed();
            if (!IsConnected || m_connection?.Session is not ManagedSession session)
            {
                throw new InvalidOperationException("Cannot create a subscription adapter while not connected.");
            }
            ISubscriptionAdapter adapter = Engine == SubscriptionEngineKind.Classic
                ? new ClassicEngineAdapter(session, m_telemetry, PublishLog)
                : new ChannelV2EngineAdapter(session, m_telemetry, PublishLog);
            if (trackLifetime)
            {
                m_adapters.Add(adapter);
            }
            m_activeAdapter ??= adapter;
            return adapter;
        }
    }

    /// <summary>
    /// Transfers disposal responsibility to the caller only when the adapter was
    /// still tracked. Disconnect owns all adapters it has already removed.
    /// </summary>
    public bool ForgetAdapter(ISubscriptionAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (m_stateGate)
        {
            bool removed = m_adapters.Remove(adapter);
            if (ReferenceEquals(m_activeAdapter, adapter))
            {
                m_activeAdapter = null;
            }
            return removed;
        }
    }

    /// <summary>
    /// Returns a borrowed configuration for local certificate/discovery tools.
    /// Its manager is deliberately different from the primary connection's
    /// scoped validator, so accept-once cannot authorize a secondary session.
    /// </summary>
    public Task<ApplicationConfiguration> GetConfigAsync()
    {
        return GetConfigAsync(CancellationToken.None);
    }

    /// <summary>
    /// Resolves local-tools configuration independently of primary-session
    /// transitions, including from an awaited document lifecycle observer.
    /// </summary>
    public async Task<ApplicationConfiguration> GetConfigAsync(CancellationToken ct)
    {
        await EnterConfigurationOperationAsync(ct).ConfigureAwait(false);
        try
        {
            m_toolsConfiguration ??= await m_backend
                .CreateConfigurationAsync(ct)
                .ConfigureAwait(false);
            return m_toolsConfiguration;
        }
        finally
        {
            ExitConfigurationOperation();
        }
    }

    /// <summary>
    /// Compatibility probe overload. An existing profile is resumed exactly,
    /// even if an old caller supplies UseSecurity=false. A new target after a
    /// profile has been selected requires an explicit endpoint/profile selection.
    /// Only a first, explicitly insecure probe may discover an Anonymous/None endpoint.
    /// </summary>
    public Task ConnectAsync(ConnectionOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ConnectCoreAsync(new ConnectRequest(options) { Resume = true }, ct);
    }

    /// <summary>
    /// Compatibility picker overload. Takes ownership of the supplied identity;
    /// known credentials are copied into a connection-lifetime stack provider.
    /// An unknown identity can be used once only and must then be reacquired.
    /// A missing certificate prompt always rejects an untrusted certificate.
    /// </summary>
    public Task ConnectAsync(
        ConnectionOptions options,
        EndpointDescription endpoint,
        IUserIdentity identity,
        Func<X509Certificate2, ServiceResult, Task<TrustChoice>>? certPrompt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(identity);
        CertificateTrustPrompt? prompt = certPrompt is null
            ? null
            : (request, _) => InvokeLegacyPromptAsync(certPrompt, request);
        return ConnectCoreAsync(new ConnectRequest(options)
        {
            Endpoint = CopyEndpoint(endpoint),
            Identity = identity,
            Prompt = prompt
        }, ct);
    }

    public Task ConnectAsync(
        ConnectionProfile profile,
        IClientIdentityProvider? identityProvider = null,
        CertificateTrustPrompt? certificatePrompt = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        return ConnectCoreAsync(new ConnectRequest(new ConnectionOptions
        {
            EndpointUrl = profile.EndpointUrl,
            UseSecurity = profile.SecurityMode != MessageSecurityMode.None,
            Engine = profile.Engine
        })
        {
            Profile = profile,
            Provider = identityProvider,
            Prompt = certificatePrompt
        }, ct);
    }

    /// <summary>
    /// Replaces the session without changing its endpoint/security/identity.
    /// Unlike DisconnectAsync followed by ConnectAsync, retains the current
    /// credential source for the replacement. Trust-once is never retained.
    /// </summary>
    public Task ReconnectAsync(SubscriptionEngineKind engine, CancellationToken ct = default)
    {
        ConnectionProfile profile = Profile ??
            throw new InvalidOperationException("Select a connection profile before reconnecting.");
        return ConnectCoreAsync(new ConnectRequest(new ConnectionOptions
        {
            EndpointUrl = profile.EndpointUrl,
            UseSecurity = profile.SecurityMode != MessageSecurityMode.None,
            Engine = engine
        })
        {
            Resume = true
        }, ct);
    }

    public Task CancelAsync()
    {
        lock (m_stateGate)
        {
            return m_cancelConnect?.Invoke() ?? Task.CompletedTask;
        }
    }

    public async Task ChangeIdentityAsync(IUserIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ConnectRequest request;
        try
        {
            lock (m_stateGate)
            {
                ThrowIfDisposed();
                if (!IsConnected || m_connection is null)
                {
                    throw new InvalidOperationException("Connect before changing identity.");
                }
                EndpointDescription endpoint = CopyEndpoint(m_connection.Session.ConfiguredEndpoint.Description);
                request = new ConnectRequest(new ConnectionOptions
                {
                    EndpointUrl = endpoint.EndpointUrl ?? string.Empty,
                    UseSecurity = endpoint.SecurityMode != MessageSecurityMode.None,
                    Engine = Engine
                })
                {
                    Endpoint = endpoint,
                    Identity = identity,
                    Prompt = m_certificatePrompt
                };
            }
        }
        catch
        {
            await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
            throw;
        }
        await ConnectCoreAsync(request, ct).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        Exception? cancellationFailure = await CancelForCleanupAsync().ConfigureAwait(false);
        await EnterOperationAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await DisconnectInternalAsync().ConfigureAwait(false);
            SetSnapshot(ConnectionPhase.Disconnected, Profile);
            if (cancellationFailure is not null)
            {
                throw new AggregateException("A cancellation callback failed during disconnect.", cancellationFailure);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetSnapshot(ConnectionPhase.Failed, Profile, ex.Message);
            m_log.ConnectionCleanupFailed(ex);
            throw;
        }
        finally
        {
            ExitOperation();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref m_disposeStarted, 1) != 0)
        {
            await m_disposeCompleted.Task.ConfigureAwait(false);
            return;
        }
        try
        {
            Exception? cancellationFailure = await CancelForCleanupAsync().ConfigureAwait(false);
            await EnterOperationAsync(CancellationToken.None, disposing: true).ConfigureAwait(false);
            try
            {
                try
                {
                    await DisconnectInternalAsync().ConfigureAwait(false);
                }
                finally
                {
                    await DisposeToolsConfigurationAsync().ConfigureAwait(false);
                }
                SetSnapshot(ConnectionPhase.Disconnected, Profile);
                if (cancellationFailure is not null)
                {
                    throw new AggregateException(
                        "A cancellation callback failed during disposal.",
                        cancellationFailure);
                }
            }
            finally
            {
                lock (m_stateGate)
                {
                    m_shutdownComplete = true;
                }
                ExitOperation();
            }
            m_disposeCompleted.TrySetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetSnapshot(ConnectionPhase.Failed, Profile, ex.Message);
            m_log.ConnectionCleanupFailed(ex);
            m_disposeCompleted.TrySetException(ex);
            throw;
        }
    }

    private async Task ConnectCoreAsync(ConnectRequest request, CancellationToken ct)
    {
        var operation = new PendingConnection(ct);
        await using (operation.ConfigureAwait(false))
        {
            await ConnectCoreAsync(request, operation).ConfigureAwait(false);
        }
    }

    private async Task ConnectCoreAsync(ConnectRequest request, PendingConnection operation)
    {
        ApplicationConfiguration? configuration = null;
        ConnectionTrustScope? trust = null;
        IConnectionSession? connection = null;
        IUserIdentity? suppliedIdentity = request.Identity;
        bool entered = false;
        bool registered = false;
        bool installed = false;
        ConnectionProfile? profile = request.Profile;
        CertificateTrustPrompt? prompt = request.Prompt;
        Exception? connectFailure = null;
        AggregateException? cleanupFailure = null;
        var cleanupFailures = new List<Exception>();
        try
        {
            lock (m_stateGate)
            {
                ThrowIfDisposed();
                if (m_cancelConnect is not null)
                {
                    throw new InvalidOperationException("A connection attempt is already in progress.");
                }
                m_cancelConnect = operation.CancelAsync;
                registered = true;
            }
            await EnterOperationAsync(operation.Token).ConfigureAwait(false);
            entered = true;
            ThrowIfDisposed();
            operation.Token.ThrowIfCancellationRequested();

            if (request.Resume && Profile is { } previous)
            {
                if (!ConnectionProfile.EndpointUrlsMatch(previous.EndpointUrl, request.Options.EndpointUrl))
                {
                    throw new InvalidOperationException("Select an explicit security profile for the new endpoint.");
                }
                profile = previous with { Engine = request.Options.Engine };
                profile.Validate();
                operation.Credentials = m_credentials;
                m_credentials = null;
                prompt = m_certificatePrompt;
            }
            if (request.Endpoint is { } explicitEndpoint && suppliedIdentity is not null)
            {
                profile = CreateProfile(explicitEndpoint, suppliedIdentity, request.Options.Engine);
                IUserIdentity identity = suppliedIdentity;
                suppliedIdentity = null;
                operation.Credentials = await ConnectionCredentials
                    .FromIdentityAsync(profile, identity, operation.Token, m_identities)
                    .ConfigureAwait(false);
            }
            profile?.Validate();

            await DisconnectInternalAsync().ConfigureAwait(false);
            SetSnapshot(ConnectionPhase.Connecting, profile);
            configuration = await CreateConnectionConfigurationAsync(operation.Token).ConfigureAwait(false);
            if (ReferenceEquals(configuration, m_toolsConfiguration) ||
                ReferenceEquals(configuration.CertificateManager, m_toolsConfiguration?.CertificateManager))
            {
                configuration = null;
                throw new InvalidOperationException(
                    "The primary connection cannot borrow the local-tools certificate manager.");
            }
            if (configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates ||
                configuration.SecurityConfiguration.UseValidatedCertificates ||
                configuration.CertificateManager is null)
            {
                throw new InvalidOperationException(
                    "Primary connections require a private, fail-closed certificate configuration " +
                    "without validation caching.");
            }

            EndpointDescription endpoint;
            if (request.Endpoint is { } selectedEndpoint)
            {
                endpoint = selectedEndpoint;
            }
            else
            {
                ArrayOf<EndpointDescription> endpoints = await m_backend.DiscoverAsync(
                    configuration,
                    request.Options.EndpointUrl,
                    operation.Token).ConfigureAwait(false);
                endpoint = SelectEndpoint(endpoints, request.Options, profile);
            }
            profile ??= CreateAnonymousProfile(endpoint, request.Options.Engine);
            profile.RequireMatch(endpoint);
            SetSnapshot(ConnectionPhase.Connecting, profile);
            if (operation.Credentials is null)
            {
                IClientIdentityProvider provider = request.Provider ??
                    await m_credentialProvider.GetAsync(profile, operation.Token).ConfigureAwait(false);
                operation.Credentials = new ConnectionCredentials(profile, provider, m_identities);
            }

            trust = new ConnectionTrustScope(profile, endpoint, configuration.CertificateManager, m_telemetry);
            m_log.ConnectionOpening(
                profile.EndpointUrl,
                profile.SecurityMode,
                profile.SecurityPolicyUri,
                profile.IdentityType,
                profile.Engine);
            connection = await OpenWithTrustAsync(
                configuration,
                endpoint,
                profile,
                operation.Credentials.Provider,
                trust,
                prompt,
                operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();

            lock (m_stateGate)
            {
                ThrowIfDisposed();
                m_connection = connection;
                m_connectionConfiguration = configuration;
                m_trust = trust;
                m_credentials = operation.TakeCredentials();
                m_certificatePrompt = prompt;
                connection.StateChanged += OnSessionStateChanged;
                ConnectionSessionState state = connection.State;
                m_snapshot = new ConnectionSnapshot(state.Phase, profile, state.Error, m_snapshot.Generation + 1);
                connection = null;
                configuration = null;
                trust = null;
                installed = true;
            }
            await NotifyConnectionChangedAsync(operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            m_log.ConnectionOpened(profile.EndpointUrl);
            RaiseStateChanged();
        }
        catch (OperationCanceledException ex) when (operation.Token.IsCancellationRequested)
        {
            connectFailure = ex;
            if (installed)
            {
                await CleanupInstalledConnectionAsync(cleanupFailures).ConfigureAwait(false);
            }
            if (registered && CurrentSession is null)
            {
                SetSnapshot(ConnectionPhase.Disconnected, profile ?? Profile);
            }
            m_log.ConnectionCancelled();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            connectFailure = ex;
            if (installed)
            {
                await CleanupInstalledConnectionAsync(cleanupFailures).ConfigureAwait(false);
            }
            if (registered && CurrentSession is null)
            {
                SetSnapshot(ConnectionPhase.Failed, profile ?? Profile, ex.Message);
            }
            m_log.ConnectionFailed(ex);
        }
        finally
        {
            try
            {
                try
                {
                    await DisposeResourcesAsync(connection, trust, configuration, credentials: null)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    cleanupFailures.Add(ex);
                }
                try
                {
                    if (suppliedIdentity is not null)
                    {
                        await ConnectionCredentials.ReleaseIdentityAsync(suppliedIdentity).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    cleanupFailures.Add(ex);
                }
                try
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    cleanupFailures.Add(ex);
                }
                if (cleanupFailures.Count > 0)
                {
                    if (connectFailure is not null)
                    {
                        cleanupFailures.Insert(0, connectFailure);
                    }
                    cleanupFailure = new AggregateException("Connection attempt cleanup failed.", cleanupFailures);
                    if (registered && CurrentSession is null)
                    {
                        SetSnapshot(ConnectionPhase.Failed, profile ?? Profile, cleanupFailure.Message);
                    }
                    m_log.ConnectionCleanupFailed(cleanupFailure);
                }
            }
            finally
            {
                if (registered)
                {
                    lock (m_stateGate)
                    {
                        m_cancelConnect = null;
                    }
                }
                if (entered)
                {
                    ExitOperation();
                }
            }
        }
        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }
        if (connectFailure is not null)
        {
            ExceptionDispatchInfo.Capture(connectFailure).Throw();
        }
    }

    private async Task<IConnectionSession> OpenWithTrustAsync(
        ApplicationConfiguration configuration,
        EndpointDescription endpoint,
        ConnectionProfile profile,
        IClientIdentityProvider identityProvider,
        ConnectionTrustScope trust,
        CertificateTrustPrompt? prompt,
        CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            trust.BeginAttempt();
            try
            {
                return await m_backend.ConnectAsync(configuration, endpoint, profile, identityProvider, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (attempt == 0 && prompt is not null)
            {
                CertificateTrustRequest? request = trust.TakeRequest();
                if (request is null || !IsCertificateFailure(ex.Result))
                {
                    throw;
                }

                m_log.ConnectionTrustRequested(profile.EndpointUrl);
                Task<TrustChoice> promptTask = prompt(request, ct);
                // Observe late prompt failures even when cancellation wins. The
                // request holds copied public bytes, not a validator-owned handle.
                _ = promptTask.ContinueWith(
                    failed => m_log.ConnectionPromptFailed(failed.Exception!),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                TrustChoice choice = await promptTask.WaitAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                switch (choice)
                {
                    case TrustChoice.AcceptOnce:
                        trust.AcceptOnce(request);
                        break;
                    case TrustChoice.TrustPermanently:
                        await ConnectionTrustScope.PersistAsync(configuration.CertificateManager, request, ct)
                            .ConfigureAwait(false);
                        m_log.ConnectionCertificatePersisted(profile.EndpointUrl);
                        break;
                    case TrustChoice.Reject:
                        throw;
                    default:
                        throw new InvalidOperationException("The certificate prompt returned an unsupported decision.");
                }
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    private static async Task<TrustChoice> InvokeLegacyPromptAsync(
        Func<X509Certificate2, ServiceResult, Task<TrustChoice>> prompt,
        CertificateTrustRequest request)
    {
        using var certificate = new Certificate(request.CertificateData.Span);
        using X509Certificate2 copy = certificate.AsX509Certificate2();
        return await prompt(copy, request.Error).ConfigureAwait(false);
    }

    private async Task<Exception?> CancelForCleanupAsync()
    {
        try
        {
            await CancelAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            m_log.ConnectionCleanupFailed(ex);
            return ex;
        }
    }

    private async Task DisconnectInternalAsync()
    {
        var failures = new List<Exception>();
        ISubscriptionAdapter[] adapters;
        IConnectionSession? connection;
        ConnectionTrustScope? trust;
        ApplicationConfiguration? configuration;
        ConnectionCredentials? credentials;
        lock (m_stateGate)
        {
            adapters = [.. m_adapters];
            m_adapters.Clear();
            m_activeAdapter = null;
            connection = m_connection;
            m_connection = null;
            trust = m_trust;
            m_trust = null;
            configuration = m_connectionConfiguration;
            m_connectionConfiguration = null;
            credentials = m_credentials;
            m_credentials = null;
            if (connection is not null)
            {
                connection.StateChanged -= OnSessionStateChanged;
                m_snapshot = m_snapshot with { Phase = ConnectionPhase.Disconnected, Error = null };
            }
        }

        if (connection is not null)
        {
            try
            {
                await NotifyConnectionChangedAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add(ex);
            }
            RaiseStateChanged();
        }

        foreach (ISubscriptionAdapter adapter in adapters)
        {
            try
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_log.ConnectionAdapterCleanupFailed(ex);
                failures.Add(ex);
            }
        }
        try
        {
            await DisposeResourcesAsync(connection, trust, configuration, credentials).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failures.Add(ex);
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Connection resources could not all be released.", failures);
        }
    }

    private async Task CleanupInstalledConnectionAsync(List<Exception> failures)
    {
        try
        {
            await DisconnectInternalAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failures.Add(ex);
        }
    }

    private async Task NotifyConnectionChangedAsync(CancellationToken ct)
    {
        Func<CancellationToken, Task>? handlers = ConnectionChangedAsync;
        if (handlers is null)
        {
            return;
        }
        var failures = new List<Exception>();
        foreach (Func<CancellationToken, Task> handler in handlers.GetInvocationList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await handler(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_log.ConnectionLifecycleObserverFailed(ex);
                failures.Add(ex);
            }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Connection change observers failed.", failures);
        }
    }

    private async Task DisposeResourcesAsync(
        IConnectionSession? connection,
        ConnectionTrustScope? trust,
        ApplicationConfiguration? configuration,
        ConnectionCredentials? credentials)
    {
        var failures = new List<Exception>();
        trust?.Dispose();
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_log.ConnectionCleanupFailed(ex);
                failures.Add(ex);
            }
        }
        try
        {
            await DisposeConfigurationAsync(configuration).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            m_log.ConnectionCleanupFailed(ex);
            failures.Add(ex);
        }
        if (credentials is not null)
        {
            try
            {
                await credentials.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_log.ConnectionCleanupFailed(ex);
                failures.Add(ex);
            }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Connection cleanup failed.", failures);
        }
    }

    private static async ValueTask DisposeConfigurationAsync(ApplicationConfiguration? configuration)
    {
        if (configuration?.CertificateManager is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (configuration?.CertificateManager is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task<ApplicationConfiguration> CreateConnectionConfigurationAsync(CancellationToken ct)
    {
        await EnterConfigurationOperationAsync(ct).ConfigureAwait(false);
        try
        {
            return await m_backend.CreateConfigurationAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ExitConfigurationOperation();
        }
    }

    private async Task DisposeToolsConfigurationAsync()
    {
        await EnterConfigurationOperationAsync(CancellationToken.None, disposing: true).ConfigureAwait(false);
        try
        {
            ApplicationConfiguration? configuration = m_toolsConfiguration;
            m_toolsConfiguration = null;
            await DisposeConfigurationAsync(configuration).ConfigureAwait(false);
        }
        finally
        {
            lock (m_stateGate)
            {
                m_configurationShutdownComplete = true;
            }
            ExitConfigurationOperation();
        }
    }

    private async Task EnterConfigurationOperationAsync(CancellationToken ct, bool disposing = false)
    {
        lock (m_stateGate)
        {
            if (disposing)
            {
                m_configurationClosing = true;
            }
            else
            {
                ObjectDisposedException.ThrowIf(m_configurationClosing, this);
            }
            m_configurationUsers++;
        }
        bool entered = false;
        try
        {
            await m_configurationOperations.WaitAsync(ct).ConfigureAwait(false);
            entered = true;
            if (!disposing)
            {
                lock (m_stateGate)
                {
                    ObjectDisposedException.ThrowIf(m_configurationClosing, this);
                }
            }
        }
        catch
        {
            if (entered)
            {
                m_configurationOperations.Release();
            }
            ReleaseConfigurationUser();
            throw;
        }
    }

    private void ExitConfigurationOperation()
    {
        m_configurationOperations.Release();
        ReleaseConfigurationUser();
    }

    private void ReleaseConfigurationUser()
    {
        lock (m_stateGate)
        {
            m_configurationUsers--;
            if (m_configurationUsers == 0 && m_configurationShutdownComplete)
            {
                m_configurationOperations.Dispose();
            }
        }
    }

    private async Task EnterOperationAsync(CancellationToken ct, bool disposing = false)
    {
        lock (m_stateGate)
        {
            if (!disposing)
            {
                ThrowIfDisposed();
            }
            m_operationUsers++;
        }
        try
        {
            await m_operations.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            ReleaseOperationUser();
            throw;
        }
    }

    private void ExitOperation()
    {
        m_operations.Release();
        ReleaseOperationUser();
    }

    private void ReleaseOperationUser()
    {
        lock (m_stateGate)
        {
            m_operationUsers--;
            if (m_operationUsers == 0 && m_shutdownComplete)
            {
                m_operations.Dispose();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposeStarted) != 0, this);
    }

    private void OnSessionStateChanged(IConnectionSession connection, ConnectionSessionState state)
    {
        lock (m_stateGate)
        {
            if (!ReferenceEquals(connection, m_connection) || m_disposeStarted != 0)
            {
                return;
            }
            m_snapshot = m_snapshot with { Phase = state.Phase, Error = state.Error };
        }
        m_log.ConnectionStateUpdated(state.Phase);
        RaiseStateChanged();
    }

    private void SetSnapshot(ConnectionPhase phase, ConnectionProfile? profile, string? error = null)
    {
        lock (m_stateGate)
        {
            m_snapshot = new ConnectionSnapshot(phase, profile, error, m_snapshot.Generation);
        }
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        Action? handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }
        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_log.ConnectionStateSubscriberFailed(ex);
            }
        }
    }

    private static EndpointDescription SelectEndpoint(
        ArrayOf<EndpointDescription> endpoints,
        ConnectionOptions options,
        ConnectionProfile? profile)
    {
        EndpointDescription? selected = null;
        foreach (EndpointDescription endpoint in endpoints)
        {
            if (profile is not null)
            {
                if (!profile.MatchesEndpoint(endpoint))
                {
                    continue;
                }
                foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
                {
                    if (profile.MatchesPolicy(policy))
                    {
                        return CopyEndpoint(endpoint);
                    }
                }
                continue;
            }
            if ((endpoint.SecurityMode != MessageSecurityMode.None) != options.UseSecurity)
            {
                continue;
            }
            bool anonymous = false;
            foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
            {
                anonymous |= policy.TokenType == UserTokenType.Anonymous;
            }
            if (anonymous && (selected is null || endpoint.SecurityLevel > selected.SecurityLevel))
            {
                selected = endpoint;
            }
        }
        return selected is null
            ? throw new ServiceResultException(
                StatusCodes.BadSecurityPolicyRejected,
                "No endpoint matches the requested security and identity profile. Select an endpoint explicitly.")
            : CopyEndpoint(selected);
    }

    private static ConnectionProfile CreateProfile(
        EndpointDescription endpoint,
        IUserIdentity identity,
        SubscriptionEngineKind engine)
    {
        UserTokenPolicy? selected = null;
        foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
        {
            if (policy.TokenType != identity.TokenType)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(identity.PolicyId))
            {
                if (string.Equals(policy.PolicyId, identity.PolicyId, StringComparison.Ordinal))
                {
                    selected = policy;
                    break;
                }
                continue;
            }
            if (selected is not null)
            {
                throw new InvalidOperationException("Select an explicit user-token policy for this identity.");
            }
            selected = policy;
        }
        return ConnectionProfile.Create(
            endpoint,
            selected ?? throw new ServiceResultException(
                StatusCodes.BadIdentityTokenRejected,
                "The selected endpoint does not advertise this identity policy."),
            engine,
            identity.TokenHandler.Token is UserNameIdentityToken userName
                ? userName.UserName
                : identity.DisplayName);
    }

    private static ConnectionProfile CreateAnonymousProfile(
        EndpointDescription endpoint,
        SubscriptionEngineKind engine)
    {
        foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
        {
            if (policy.TokenType == UserTokenType.Anonymous)
            {
                return ConnectionProfile.Create(endpoint, policy, engine);
            }
        }
        throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected, "Anonymous access is not advertised.");
    }

    private static EndpointDescription CopyEndpoint(EndpointDescription endpoint)
    {
        return (EndpointDescription)endpoint.Clone();
    }

    private static bool IsCertificateFailure(ServiceResult error)
    {
        for (ServiceResult? current = error; current is not null; current = current.InnerResult)
        {
            if (current.StatusCode == StatusCodes.BadCertificateInvalid ||
                current.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                return true;
            }
        }
        return false;
    }

    private sealed record ConnectRequest(ConnectionOptions Options)
    {
        public ConnectionProfile? Profile { get; init; }

        public EndpointDescription? Endpoint { get; init; }

        public IUserIdentity? Identity { get; init; }

        public IClientIdentityProvider? Provider { get; init; }

        public CertificateTrustPrompt? Prompt { get; init; }

        public bool Resume { get; init; }
    }

    private sealed class PendingConnection : IAsyncDisposable
    {
        public PendingConnection(CancellationToken ct)
        {
            m_source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        public CancellationToken Token => m_source.Token;

        public ConnectionCredentials? Credentials { get; set; }

        public ConnectionCredentials? TakeCredentials()
        {
            ConnectionCredentials? credentials = Credentials;
            Credentials = null;
            return credentials;
        }

        public Task CancelAsync()
        {
            lock (m_gate)
            {
                return m_disposed ? Task.CompletedTask : m_cancellation ??= m_source.CancelAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task cancellation;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                cancellation = m_cancellation ?? Task.CompletedTask;
            }
            try
            {
                if (Credentials is not null)
                {
                    await Credentials.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Credentials = null;
                try
                {
                    await cancellation.ConfigureAwait(false);
                }
                finally
                {
                    m_source.Dispose();
                }
            }
        }

        private readonly Lock m_gate = new();
        private readonly CancellationTokenSource m_source;
        private Task? m_cancellation;
        private bool m_disposed;
    }

    private readonly ITelemetryContext m_telemetry;
    private readonly ILogger m_log;
    private readonly IConnectionBackend m_backend;
    private readonly IConnectionCredentialProvider m_credentialProvider;
    private readonly ConnectionIdentityTracker m_identities = new();
    private readonly SemaphoreSlim m_operations = new(1, 1);
    private readonly SemaphoreSlim m_configurationOperations = new(1, 1);
    private readonly Lock m_stateGate = new();
    private readonly List<ISubscriptionAdapter> m_adapters = [];
    private readonly TaskCompletionSource m_disposeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ConnectionSnapshot m_snapshot = new(ConnectionPhase.Disconnected, null, null, 0);
    private IConnectionSession? m_connection;
    private ApplicationConfiguration? m_connectionConfiguration;
    private ApplicationConfiguration? m_toolsConfiguration;
    private ConnectionCredentials? m_credentials;
    private ConnectionTrustScope? m_trust;
    private CertificateTrustPrompt? m_certificatePrompt;
    private Func<Task>? m_cancelConnect;
    private ISubscriptionAdapter? m_activeAdapter;
    private int m_operationUsers;
    private int m_disposeStarted;
    private bool m_shutdownComplete;
    private int m_configurationUsers;
    private bool m_configurationClosing;
    private bool m_configurationShutdownComplete;
}

internal static partial class ConnectionServiceLog
{
    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 0, Level = LogLevel.Information,
        Message = "Connecting to {Endpoint} ({Mode}/{Policy}), identity={Identity}, engine={Engine}.")]
    public static partial void ConnectionOpening(
        this ILogger logger,
        string endpoint,
        MessageSecurityMode mode,
        string policy,
        UserTokenType identity,
        SubscriptionEngineKind engine);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 1, Level = LogLevel.Information,
        Message = "Connected to {Endpoint}.")]
    public static partial void ConnectionOpened(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 2, Level = LogLevel.Error,
        Message = "Connection failed.")]
    public static partial void ConnectionFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 3, Level = LogLevel.Information,
        Message = "Connection attempt cancelled.")]
    public static partial void ConnectionCancelled(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 4, Level = LogLevel.Information,
        Message = "Awaiting a certificate trust decision for {Endpoint}.")]
    public static partial void ConnectionTrustRequested(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 5, Level = LogLevel.Information,
        Message = "Certificate trust for {Endpoint} was persisted successfully.")]
    public static partial void ConnectionCertificatePersisted(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 6, Level = LogLevel.Warning,
        Message = "Certificate trust prompt failed.")]
    public static partial void ConnectionPromptFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 7, Level = LogLevel.Error,
        Message = "Connection resource cleanup failed.")]
    public static partial void ConnectionCleanupFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 8, Level = LogLevel.Error,
        Message = "Subscription adapter cleanup failed.")]
    public static partial void ConnectionAdapterCleanupFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 9, Level = LogLevel.Debug,
        Message = "Connection state changed to {Phase}.")]
    public static partial void ConnectionStateUpdated(this ILogger logger, ConnectionPhase phase);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 10, Level = LogLevel.Error,
        Message = "A connection state subscriber failed.")]
    public static partial void ConnectionStateSubscriberFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.ConnectionService + 11, Level = LogLevel.Error,
        Message = "An awaited connection lifecycle observer failed.")]
    public static partial void ConnectionLifecycleObserverFailed(this ILogger logger, Exception exception);
}
