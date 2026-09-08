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

namespace UaLens.Connection;

internal enum ReverseConnectionPhase
{
    Stopped,
    Starting,
    Listening,
    Waiting,
    Stopping,
    Failed
}

internal sealed record ReverseConnectionSnapshot(
    ReverseConnectionPhase Phase,
    ReverseConnectionProfile? Profile,
    string? Error = null);

internal interface IReverseConnectionRuntime : IAsyncDisposable
{
    ReverseConnectManager Manager { get; }

    Task StartAsync(CancellationToken ct);

    Task<ITransportWaitingConnection> WaitAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);
}

internal interface IReverseConnectionRuntimeFactory
{
    IReverseConnectionRuntime Create(ReverseConnectionProfile profile);
}

/// <summary>
/// One app-owned listener, shared by discovery and the primary session. Merely
/// constructing/restoring profiles never creates or starts a runtime. Leases
/// prevent a settings dialog from stopping a session's listener underneath it.
/// </summary>
internal sealed class ReverseConnectionService : IAsyncDisposable
{
    public ReverseConnectionService(IReverseConnectionRuntimeFactory factory)
    {
        m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public ReverseConnectionSnapshot Snapshot => Volatile.Read(ref m_snapshot);

    public async Task StartAsync(ReverseConnectionProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await EnterOperationAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                m_cancelStart = cancellation.CancelAsync;
            }
            if (m_runtime is not null)
            {
                if (Snapshot.Profile == profile && Snapshot.Phase is
                    ReverseConnectionPhase.Listening or ReverseConnectionPhase.Waiting)
                {
                    return;
                }
                throw new InvalidOperationException(
                    "Stop the existing reverse listener before changing its configuration.");
            }
            SetSnapshot(ReverseConnectionPhase.Starting, profile);
            IReverseConnectionRuntime? runtime = null;
            bool installed = false;
            try
            {
                runtime = m_factory.Create(profile);
                await runtime.StartAsync(cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    ObjectDisposedException.ThrowIf(m_disposed, this);
                    m_runtime = runtime;
                    installed = true;
                    SetSnapshot(ReverseConnectionPhase.Listening, profile);
                }
            }
            finally
            {
                if (!installed)
                {
                    try
                    {
                        if (runtime is not null)
                        {
                            await runtime.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        SetSnapshot(ReverseConnectionPhase.Stopped, profile);
                    }
                }
            }
        }
        finally
        {
            lock (m_gate)
            {
                m_cancelStart = null;
            }
            ExitOperation();
        }
    }

    public ReverseConnectionLease Acquire(ReverseConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_runtime is null || Snapshot.Profile != profile ||
                Snapshot.Phase is not (ReverseConnectionPhase.Listening or ReverseConnectionPhase.Waiting))
            {
                throw new InvalidOperationException(
                    "The saved reverse listener is stopped or differs from this profile. " +
                    "Start it explicitly in connection setup.");
            }
            if (m_leases++ == 0)
            {
                m_leasesDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return new ReverseConnectionLease(m_runtime, Release);
        }
    }

    public async Task<ITransportWaitingConnection> WaitAsync(
        ReverseConnectionProfile profile,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using ReverseConnectionLease lease = Acquire(profile);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (m_gate)
        {
            if (m_cancelWait is not null)
            {
                throw new InvalidOperationException("A reverse-discovery wait is already active.");
            }
            m_cancelWait = cancellation.CancelAsync;
            m_waitCompleted = completed.Task;
            SetSnapshot(ReverseConnectionPhase.Waiting, profile);
        }
        try
        {
            ITransportWaitingConnection connection =
                await lease.Runtime.WaitAsync(cancellation.Token).ConfigureAwait(false);
            if (!profile.MatchesPeer(connection.ServerUri, connection.EndpointUrl))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTcpEndpointUrlInvalid,
                    "The reverse peer did not match the configured endpoint and ServerUri.");
            }
            return connection;
        }
        finally
        {
            lock (m_gate)
            {
                m_cancelWait = null;
                m_waitCompleted = Task.CompletedTask;
                if (Snapshot.Phase == ReverseConnectionPhase.Waiting)
                {
                    SetSnapshot(ReverseConnectionPhase.Listening, profile);
                }
                // Complete only after releasing the lease so Stop can observe
                // zero users once the canceled discovery wait has drained.
                lease.Dispose();
                completed.TrySetResult();
            }
        }
    }

    public Task CancelWaitAsync()
    {
        lock (m_gate)
        {
            return m_cancelWait?.Invoke() ?? Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        Task? disposal;
        lock (m_gate)
        {
            disposal = m_disposeTask;
        }
        if (disposal is not null)
        {
            await disposal.WaitAsync(ct).ConfigureAwait(false);
            return;
        }
        try
        {
            await StopInternalAsync(ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (m_disposeTask is not null)
        {
            await m_disposeTask.WaitAsync(ct).ConfigureAwait(false);
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
                start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposeTask = DisposeCoreAsync(start.Task);
            }
            disposal = m_disposeTask;
        }
        start?.TrySetResult();
        return new ValueTask(disposal);
    }

    private async Task StopInternalAsync(CancellationToken ct, bool disposing = false)
    {
        Task cancellation;
        Task wait;
        lock (m_gate)
        {
            cancellation = Task.WhenAll(
                m_cancelStart?.Invoke() ?? Task.CompletedTask,
                m_cancelWait?.Invoke() ?? Task.CompletedTask);
            wait = m_waitCompleted;
        }
        AggregateException? cancellationFailure = null;
        try
        {
            await cancellation.ConfigureAwait(false);
        }
        catch (AggregateException error)
        {
            cancellationFailure = error;
        }
        await wait.WaitAsync(ct).ConfigureAwait(false);
        await EnterOperationAsync(ct, disposing).ConfigureAwait(false);
        try
        {
            IReverseConnectionRuntime? runtime;
            lock (m_gate)
            {
                if (m_leases > 0)
                {
                    throw new InvalidOperationException(
                        "Disconnect the primary reverse session before stopping its listener.");
                }
                runtime = m_runtime;
                if (runtime is null)
                {
                    if (cancellationFailure is not null)
                    {
                        throw cancellationFailure;
                    }
                    return;
                }
                SetSnapshot(ReverseConnectionPhase.Stopping, Snapshot.Profile);
            }
            try
            {
                await runtime.StopAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetSnapshot(ReverseConnectionPhase.Listening, Snapshot.Profile);
                throw;
            }
            finally
            {
                if (Snapshot.Phase == ReverseConnectionPhase.Stopping)
                {
                    lock (m_gate)
                    {
                        m_runtime = null;
                    }
                    try
                    {
                        await runtime.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        SetSnapshot(ReverseConnectionPhase.Stopped, Snapshot.Profile);
                    }
                }
            }
            if (cancellationFailure is not null)
            {
                throw cancellationFailure;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task DisposeCoreAsync(Task started)
    {
        await started.ConfigureAwait(false);
        Task cancellation;
        lock (m_gate)
        {
            cancellation = Task.WhenAll(
                m_cancelStart?.Invoke() ?? Task.CompletedTask,
                m_cancelWait?.Invoke() ?? Task.CompletedTask);
        }
        AggregateException? cancellationFailure = null;
        try
        {
            await cancellation.ConfigureAwait(false);
        }
        catch (AggregateException error)
        {
            cancellationFailure = error;
        }
        Task leases;
        lock (m_gate)
        {
            leases = m_leasesDrained?.Task ?? Task.CompletedTask;
        }
        await leases.ConfigureAwait(false);
        try
        {
            await StopInternalAsync(CancellationToken.None, disposing: true).ConfigureAwait(false);
            if (cancellationFailure is not null)
            {
                throw cancellationFailure;
            }
        }
        finally
        {
            bool dispose;
            lock (m_gate)
            {
                m_shutdownComplete = true;
                dispose = m_operationUsers == 0;
            }
            if (dispose)
            {
                m_operations.Dispose();
            }
        }
    }

    private async Task EnterOperationAsync(CancellationToken ct, bool disposing = false)
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_shutdownComplete || (m_disposed && !disposing), this);
            m_operationUsers++;
        }
        bool entered = false;
        bool completed = false;
        try
        {
            await m_operations.WaitAsync(ct).ConfigureAwait(false);
            entered = true;
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_disposed && !disposing, this);
            }
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                ExitOperation(entered);
            }
        }
    }

    private void ExitOperation(bool entered = true)
    {
        bool dispose;
        lock (m_gate)
        {
            if (entered)
            {
                m_operations.Release();
            }
            m_operationUsers--;
            dispose = m_shutdownComplete && m_operationUsers == 0;
        }
        if (dispose)
        {
            m_operations.Dispose();
        }
    }

    private void Release()
    {
        lock (m_gate)
        {
            if (--m_leases == 0)
            {
                m_leasesDrained?.TrySetResult();
            }
        }
    }

    private void SetSnapshot(ReverseConnectionPhase phase, ReverseConnectionProfile? profile)
    {
        Volatile.Write(ref m_snapshot, new ReverseConnectionSnapshot(phase, profile));
    }

    private readonly IReverseConnectionRuntimeFactory m_factory;
    private readonly SemaphoreSlim m_operations = new(1, 1);
    private readonly Lock m_gate = new();
    private ReverseConnectionSnapshot m_snapshot = new(ReverseConnectionPhase.Stopped, null);
    private IReverseConnectionRuntime? m_runtime;
    private Func<Task>? m_cancelWait;
    private Func<Task>? m_cancelStart;
    private Task m_waitCompleted = Task.CompletedTask;
    private Task? m_disposeTask;
    private TaskCompletionSource? m_leasesDrained;
    private int m_leases;
    private int m_operationUsers;
    private bool m_disposed;
    private bool m_shutdownComplete;
}

internal sealed class ReverseConnectionLease : IDisposable
{
    public ReverseConnectionLease(IReverseConnectionRuntime runtime, Action release)
    {
        Runtime = runtime;
        m_release = release;
    }

    public IReverseConnectionRuntime Runtime { get; }

    public ReverseConnectManager Manager => Runtime.Manager;

    public void Dispose()
    {
        Interlocked.Exchange(ref m_release, null)?.Invoke();
    }

    private Action? m_release;
}

internal sealed class StackReverseConnectionRuntimeFactory : IReverseConnectionRuntimeFactory
{
    public StackReverseConnectionRuntimeFactory(
        ITelemetryContext telemetry,
        ConnectionTransportCatalog transports,
        ConnectionConfigurationCatalog configurations,
        IReverseConnectConfigurationProvider? provider = null)
    {
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_transports = transports ?? throw new ArgumentNullException(nameof(transports));
        m_configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        m_provider = provider ?? DefaultReverseConnectConfigurationProvider.Instance;
    }

    public IReverseConnectionRuntime Create(ReverseConnectionProfile profile)
    {
        m_transports.RequireReverse(profile);
        ConfiguredReverseTlsSource? tls = profile.TlsConfigurationId is { } id
            ? m_configurations.ResolveTls(id)
            : null;
        return new StackReverseConnectionRuntime(
            profile, m_telemetry, m_transports, m_configurations, m_provider, tls);
    }

    private readonly ITelemetryContext m_telemetry;
    private readonly ConnectionTransportCatalog m_transports;
    private readonly ConnectionConfigurationCatalog m_configurations;
    private readonly IReverseConnectConfigurationProvider m_provider;
}

internal sealed class StackReverseConnectionRuntime : IReverseConnectionRuntime
{
    public StackReverseConnectionRuntime(
        ReverseConnectionProfile profile,
        ITelemetryContext telemetry,
        ConnectionTransportCatalog transports,
        ConnectionConfigurationCatalog configurations,
        IReverseConnectConfigurationProvider provider,
        ConfiguredReverseTlsSource? tls)
    {
        m_profile = profile;
        m_tls = tls;
        m_configurations = configurations;
        Manager = new ReverseConnectManager(telemetry)
        {
            TransportBindings = new MatchedReverseTransportBindings(transports.Bindings, profile),
            ConfigurationProvider = new PinnedReverseConfigurationProvider(profile, provider)
        };
    }

    public ReverseConnectManager Manager { get; }

    public async Task StartAsync(CancellationToken ct)
    {
        if (m_tls is null)
        {
            await Manager.StartServiceAsync(m_profile.CreateConfiguration(), ct).ConfigureAwait(false);
            return;
        }
        ApplicationConfiguration configuration = await m_tls.CreateConfigurationAsync(ct).ConfigureAwait(false);
        if (!m_configurations.TryClaimManager(configuration))
        {
            throw new InvalidOperationException(
                "The reverse listener cannot borrow another operation's certificate manager.");
        }
        m_configuration = configuration;
        if (m_configuration.CertificateManager is null ||
            m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates ||
            m_configuration.SecurityConfiguration.UseValidatedCertificates ||
            m_configuration.SecurityConfiguration.ApplicationCertificates.Count == 0)
        {
            throw new InvalidOperationException(
                "Listener TLS requires a private, fail-closed certificate configuration.");
        }
        ClientConfiguration client = m_configuration.ClientConfiguration ??
            throw new InvalidOperationException("The listener configuration requires a client configuration.");
        client.ReverseConnect = m_profile.CreateConfiguration();
        await Manager.StartServiceAsync(m_configuration, ct).ConfigureAwait(false);
    }

    public Task<ITransportWaitingConnection> WaitAsync(CancellationToken ct)
    {
        return Manager.WaitForConnectionAsync(new Uri(m_profile.EndpointUrl), m_profile.ServerUri, ct);
    }

    public Task StopAsync(CancellationToken ct)
    {
        return Manager.StopServiceAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Manager.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            ApplicationConfiguration? configuration = m_configuration;
            m_configuration = null;
            await ConnectionConfigurationLifetime.DisposeAsync(configuration).ConfigureAwait(false);
        }
    }

    private readonly ReverseConnectionProfile m_profile;
    private readonly ConfiguredReverseTlsSource? m_tls;
    private readonly ConnectionConfigurationCatalog m_configurations;
    private ApplicationConfiguration? m_configuration;
}

internal sealed class PinnedReverseConfigurationProvider : IReverseConnectConfigurationProvider
{
    public PinnedReverseConfigurationProvider(
        ReverseConnectionProfile profile,
        IReverseConnectConfigurationProvider inner)
    {
        m_profile = profile;
        m_inner = inner;
    }

    public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
        ApplicationConfiguration? applicationConfiguration,
        ReverseConnectClientConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ReverseConnectClientConfiguration effective = await m_inner.ConfigureAsync(
            applicationConfiguration, configuration, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (effective.ClientEndpoints.Count != 1 ||
            !ConnectionProfile.EndpointUrlsMatch(effective.ClientEndpoints[0].EndpointUrl, m_profile.ListenerUrl) ||
            effective.WaitTimeout != m_profile.WaitTimeoutSeconds * 1000 ||
            effective.HoldTime != m_profile.HoldTimeSeconds * 1000)
        {
            throw new InvalidOperationException(
                "The reverse configuration provider changed the explicitly selected listener or its bounds.");
        }
        return effective;
    }

    private readonly ReverseConnectionProfile m_profile;
    private readonly IReverseConnectConfigurationProvider m_inner;
}
