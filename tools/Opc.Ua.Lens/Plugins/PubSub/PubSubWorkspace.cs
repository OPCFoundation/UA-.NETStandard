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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.StateMachine;

namespace UaLens.Plugins.PubSub;

internal enum PubSubDocumentPhase
{
    Offline,
    Starting,
    Running,
    Stopping,
    Faulted,
    Disposed
}

internal sealed record PubSubCounterRow(string Counter, long Application, long Transport);

internal sealed record PubSubComponentRow(string Name, PubSubState State, StatusCode Status);

internal sealed record PubSubDiscoveryRow(string Kind, string Identity, StatusCode Status, string Detail);

internal sealed record PubSubActionResult(
    ushort RequestId,
    string Correlation,
    StatusCode Status,
    ActionState State,
    ArrayOf<PubSubFieldValue> Outputs);

internal sealed record PubSubWorkspaceSnapshot(
    PubSubDocumentPhase Phase,
    string Status,
    PubSubObservationSnapshot Observations,
    ArrayOf<PubSubCounterRow> Counters,
    ArrayOf<PubSubComponentRow> Components,
    ArrayOf<PubSubDiscoveryRow> Discovery,
    PubSubActionResult? Action,
    int SourceSamples);

/// <summary>
/// Owns the entire network workload. Restore is configuration-only; no primary
/// session, UI dispatcher, generic host, or global PubSub application is required.
/// </summary>
internal sealed class PubSubWorkspace : IAsyncDisposable
{
    public PubSubWorkspace(
        IPubSubRuntimeFactory factory,
        ITelemetryContext telemetry,
        TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        ArgumentNullException.ThrowIfNull(telemetry);
        m_logger = telemetry.CreateLogger<PubSubWorkspace>();
        m_clock = clock ?? TimeProvider.System;
        m_delay = delay ?? ((duration, token) => Task.Delay(duration, m_clock, token));
        m_observations = new PubSubObservationStore(m_configuration.RetainedMessages, m_clock);
    }

    public PubSubConfiguration Configuration
    {
        get
        {
            lock (m_gate)
            {
                return m_configuration;
            }
        }
    }

    public ArrayOf<PubSubPrerequisite> Prerequisites
    {
        get
        {
            lock (m_gate)
            {
                return m_factory.Inspect(m_configuration, m_primarySession is not null);
            }
        }
    }

    public PubSubWorkspaceSnapshot Snapshot()
    {
        lock (m_gate)
        {
            if (m_active is not null && m_phase == PubSubDocumentPhase.Running)
            {
                CaptureRuntimeCounters(m_active);
            }
            bool unexpectedFailure = m_run.IsFaulted && m_phase != PubSubDocumentPhase.Faulted;
            return new PubSubWorkspaceSnapshot(
                unexpectedFailure ? PubSubDocumentPhase.Faulted : m_phase,
                unexpectedFailure
                    ? "The runtime task failed. Stop/close observes the failure; no restart is automatic." : m_status,
                m_observations.Snapshot(),
                m_counters,
                m_components,
                m_discovery,
                m_action,
                m_sourceSamples);
        }
    }

    public async Task ConfigureAsync(PubSubConfiguration configuration, CancellationToken cancellationToken = default)
    {
        PubSubConfigurationValidation.RequireValid(configuration, requireEndpoint: false);
        cancellationToken.ThrowIfCancellationRequested();
        await StopAsync().ConfigureAwait(false);
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            cancellationToken.ThrowIfCancellationRequested();
            m_configuration = configuration;
            m_observations = new PubSubObservationStore(configuration.RetainedMessages, m_clock);
            m_discovery = [];
            m_action = null;
            m_counters = [];
            m_components = [];
            m_sourceSamples = 0;
            m_phase = PubSubDocumentPhase.Offline;
            m_run = Task.CompletedTask;
            m_status = "Configuration ready offline. No listeners, publication, writes or Actions have been started.";
        }
    }

    public async Task StartAsync(PubSubStartAuthorization authorization, CancellationToken cancellationToken = default)
    {
        Task started;
        Task run;
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            if (!m_run.IsCompleted)
            {
                throw new InvalidOperationException("Stop the current PubSub workload before starting another.");
            }
            PubSubConfigurationValidation.RequireValid(m_configuration);
            PubSubConfigurationValidation.RequireAuthorization(m_configuration, authorization);
            cancellationToken.ThrowIfCancellationRequested();
            m_lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            m_observations = new PubSubObservationStore(m_configuration.RetainedMessages, m_clock);
            m_discovery = [];
            m_action = null;
            m_counters = [];
            m_components = [];
            m_phase = PubSubDocumentPhase.Starting;
            m_status = "Acquiring the explicitly configured providers and starting the document-owned transport…";
            var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            started = startup.Task;
            m_run = RunAsync(m_configuration, authorization, m_primarySession, m_lifetime, startup);
            run = m_run;
        }
        Task outcome = await Task.WhenAny(started, run).ConfigureAwait(false);
        await outcome.ConfigureAwait(false);
        if (outcome == run && !started.IsCompletedSuccessfully)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException("The PubSub start was stopped before becoming operational.");
        }
    }

    public async Task StopAsync()
    {
        Task run;
        Task cancellation = Task.CompletedTask;
        lock (m_gate)
        {
            run = m_run;
            if (!run.IsCompleted)
            {
                m_phase = PubSubDocumentPhase.Stopping;
                m_status = "Stopping and awaiting document-owned network resources…";
                cancellation = m_lifetime?.CancelAsync() ?? Task.CompletedTask;
            }
        }
        try
        {
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            finally
            {
                await run.ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (PubSubFailure.IsExpected(exception))
        {
            RecordFailure("Stopped workload", exception);
        }
    }

    public async Task BindPrimarySessionAsync(ISession? session, CancellationToken cancellationToken = default)
    {
        Task? stop = null;
        Task stoppingCancellation = Task.CompletedTask;
        PubSubRuntimeHandle? detach = null;
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            if ((!ReferenceEquals(m_primarySession, session) || session is { Connected: false }) &&
                m_factory.UsesPrimarySession(m_configuration))
            {
                if (m_phase == PubSubDocumentPhase.Running)
                {
                    detach = m_active;
                }
                else if (!m_run.IsCompleted)
                {
                    stoppingCancellation = m_lifetime?.CancelAsync() ?? Task.CompletedTask;
                    stop = m_run;
                }
            }
            m_primarySession = session;
        }
        if (detach is not null)
        {
            await detach.ReleasePrimaryBindingsAsync().ConfigureAwait(false);
            m_observations.RecordEvidence("UA adapter", StatusCodes.BadSessionClosed,
                "Primary UA bindings stopped and drained. Local reception continues; no automatic rebinding.");
            lock (m_gate)
            {
                m_status = "Primary UA bindings stopped. Local reception continues. Stop/Start explicitly to rebind.";
            }
        }
        if (stop is not null)
        {
            try
            {
                await stoppingCancellation.ConfigureAwait(false);
            }
            finally
            {
                await stop.ConfigureAwait(false);
            }
            m_observations.RecordEvidence("UA adapter", StatusCodes.BadSessionClosed,
                "The selected primary-session bindings stopped. Select Start explicitly to rebind them.");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task<ArrayOf<PubSubDiscoveryRow>> DiscoverAsync(
        UadpDiscoveryType discoveryType,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (discoveryType is not (UadpDiscoveryType.DataSetMetaData or
            UadpDiscoveryType.DataSetWriterConfiguration or UadpDiscoveryType.PublisherEndpoints))
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryType));
        }
        RequireTimeout(timeout);
        return ExecuteAsync(async (runtime, configuration, observations, token) =>
        {
            if (!configuration.ReceiveEnabled)
            {
                throw new InvalidOperationException("Enable reception to collect discovery responses.");
            }
            observations.BeginDiscovery();
            try
            {
                PubSubDiscoveryResult result = await runtime.Application.RequestDiscoveryAsync(
                    new PubSubDiscoveryRequest
                    {
                        DiscoveryType = discoveryType,
                        DataSetWriterIds = [configuration.DataSetWriterId]
                    },
                    timeout,
                    token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                ArrayOf<PubSubDiscoveryRow> rows = DescribeDiscovery(result);
                lock (m_gate)
                {
                    RequireActiveOperation(runtime, token);
                    m_discovery = rows;
                }
                observations.RecordEvidence("Discovery", StatusCodes.Good,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Discovery retained {rows.Count} responses; zero means no response, not success."));
                return rows;
            }
            finally
            {
                observations.EndDiscovery();
            }
        }, cancellationToken);
    }

    public Task<PubSubActionResult> InvokeActionAsync(
        PubSubActionRequest request,
        bool authorized,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTimeout(timeout);
        if (!authorized)
        {
            throw new InvalidOperationException("Explicit confirmation is required for each Action invocation.");
        }
        if (request.Target is null || request.ResponseAddress is null ||
            request.Target.DataSetWriterId == 0 || request.Target.ActionTargetId == 0 ||
            request.Target.ActionName is null || request.Target.ActionName.Length > 64 ||
            request.InputFields.Count > PubSubConfigurationValidation.MaxFields ||
            request.InputFields.Contains(field => field is null || string.IsNullOrEmpty(field.Name) ||
                field.Name.Length > 64 || PubSubValueDisplay.Create(field).Truncated) ||
            request.ResponseAddress.Length > 256 ||
            request.ResponseAddress.IndexOfAny(['*', '+', '#', '\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException(
                "Use an explicit bounded Action target, scalar inputs and response topic.", nameof(request));
        }
        return ExecuteAsync(async (runtime, configuration, observations, token) =>
        {
            if (!configuration.ReceiveEnabled)
            {
                throw new InvalidOperationException("Enable reception to await the correlated Action response.");
            }
            int inputBudget = 256 + (request.Target.ActionName.Length + request.ResponseAddress.Length) * 6;
            foreach (DataSetField field in request.InputFields)
            {
                inputBudget += 64 + (field.Name.Length + PubSubValueDisplay.Create(field).Text.Length) * 6;
            }
            if (inputBudget > configuration.MaxNetworkMessageBytes)
            {
                throw new ArgumentException(
                    "The Action input exceeds the document's conservative message budget.", nameof(request));
            }
            PubSubActionResponse response = await runtime.Application.InvokeActionAsync(
                request with
                {
                    Target = request.Target with { ConnectionName = PubSubStackConfiguration.ConnectionName },
                    TimeoutHint = timeout.TotalMilliseconds
                }, timeout, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (response.RequestId == 0 || response.CorrelationData.IsNull || response.CorrelationData.Length == 0 ||
                response.CorrelationData.Length > 64 ||
                response.OutputFields.Count > PubSubConfigurationValidation.MaxFields)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "The Action result did not contain bounded correlation/output evidence.");
            }
            var result = new PubSubActionResult(
                response.RequestId,
                Convert.ToHexString(response.CorrelationData.Span),
                response.StatusCode,
                response.ActionState,
                [.. response.OutputFields.ToList().Select(PubSubValueDisplay.Create)]);
            lock (m_gate)
            {
                RequireActiveOperation(runtime, token);
                m_action = result;
            }
            observations.RecordEvidence("Action", response.StatusCode,
                string.Create(CultureInfo.InvariantCulture,
                    $"Request {response.RequestId}: {response.ActionState}; correlation is from the stack."));
            return result;
        }, cancellationToken);
    }

    public Task<ArrayOf<PubSubDiscoveryRow>> InspectRuntimeConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync((runtime, _, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            PubSubConfigurationDataType actual = runtime.Application.GetConfiguration();
            var rows = new List<PubSubDiscoveryRow>();
            foreach (PubSubConnectionDataType connection in actual.Connections.SafeSlice(0, 8))
            {
                rows.Add(new PubSubDiscoveryRow(
                    "Runtime connection",
                    PubSubValueDisplay.Bound(connection.Name, 96),
                    StatusCodes.Good,
                    string.Create(CultureInfo.InvariantCulture,
                        $"{connection.WriterGroups.Count} writer groups; " +
                        $"{connection.ReaderGroups.Count} reader groups.")));
            }
            rows.Add(new PubSubDiscoveryRow("Runtime configuration", "Published datasets", StatusCodes.Good,
                actual.PublishedDataSets.Count.ToString(CultureInfo.InvariantCulture)));
            ArrayOf<PubSubDiscoveryRow> result = [.. rows];
            lock (m_gate)
            {
                RequireActiveOperation(runtime, token);
                m_discovery = result;
            }
            return ValueTask.FromResult(result);
        }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_closed = true;
            m_disposal ??= DisposeCoreAsync();
            return new ValueTask(m_disposal);
        }
    }

    private async Task RunAsync(
        PubSubConfiguration configuration,
        PubSubStartAuthorization authorization,
        ISession? primarySession,
        CancellationTokenSource lifetime,
        TaskCompletionSource startup)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        PubSubRuntimeHandle? runtime = null;
        bool started = false;
        bool failed = false;
        try
        {
            runtime = await m_factory.CreateAsync(
                configuration, authorization, primarySession, m_observations, lifetime.Token)
                .ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            using var binding = new PubSubObservationBinding(runtime.Application, m_observations);
            lock (m_gate)
            {
                m_active = runtime;
            }
            await runtime.Application.StartAsync(lifetime.Token).ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            if (runtime.Application.State.State != PubSubState.Operational)
            {
                throw new InvalidOperationException(
                    "The application did not enter Operational; inspect component evidence.");
            }
            lock (m_gate)
            {
                m_phase = PubSubDocumentPhase.Running;
                m_status = configuration.IsBoundedWorkload
                    ? "Running the explicitly authorized, bounded workload. Stop/close releases its resources."
                    : "Receiving locally. No UA writes; discovery and Actions remain explicit.";
            }
            started = true;
            startup.TrySetResult();
            Task duration = m_delay(configuration.IsBoundedWorkload
                ? TimeSpan.FromSeconds(configuration.DurationSeconds)
                : Timeout.InfiniteTimeSpan, lifetime.Token);
            if (runtime.Source is { } source)
            {
                Task completion = await Task.WhenAny(duration, source.Completed).ConfigureAwait(false);
                await completion.ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                if (completion == source.Completed)
                {
                    await runtime.CompletePublicationAsync(lifetime.Token).ConfigureAwait(false);
                }
            }
            else
            {
                await duration.ConfigureAwait(false);
            }
            m_observations.RecordEvidence("Lifecycle", StatusCodes.Good,
                "The configured duration/sample bound was reached; the document stopped its workload.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            m_observations.RecordEvidence(
                "Lifecycle", StatusCodes.Good, "The document workload was canceled or stopped.");
        }
        catch (Exception exception) when (PubSubFailure.IsExpected(exception))
        {
            failed = true;
            RecordFailure("Runtime", exception);
            if (!started)
            {
                throw;
            }
        }
        finally
        {
            try
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                await m_operations.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (runtime is not null)
                    {
                        try
                        {
                            await runtime.Application.StopAsync(CancellationToken.None).ConfigureAwait(false);
                            lock (m_gate)
                            {
                                CaptureRuntimeCounters(runtime);
                            }
                        }
                        finally
                        {
                            await runtime.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception exception) when (PubSubFailure.IsExpected(exception))
                {
                    failed = true;
                    RecordFailure("Cleanup", exception);
                }
                finally
                {
                    m_operations.Release();
                    lock (m_gate)
                    {
                        m_active = null;
                        m_lifetime = null;
                        lifetime.Dispose();
                        if (!failed)
                        {
                            m_phase = m_closed ? PubSubDocumentPhase.Disposed : PubSubDocumentPhase.Offline;
                            m_status = "Stopped. Retained evidence is local; a new workload requires explicit Start.";
                        }
                    }
                }
            }
        }
    }

    private async Task<TResult> ExecuteAsync<TResult>(
        Func<PubSubRuntimeHandle, PubSubConfiguration, PubSubObservationStore,
            CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        PubSubRuntimeHandle runtime;
        PubSubConfiguration configuration;
        PubSubObservationStore observations;
        CancellationTokenSource linked;
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            if (m_phase != PubSubDocumentPhase.Running || m_active is null || m_lifetime is null)
            {
                throw new InvalidOperationException("Start the document-owned PubSub runtime first.");
            }
            runtime = m_active;
            configuration = m_configuration;
            observations = m_observations;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime.Token);
        }
        using (linked)
        {
            if (!await m_operations.WaitAsync(0, linked.Token).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Only one explicit discovery or Action operation can run at a time.");
            }
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                TResult result = await operation(runtime, configuration, observations, linked.Token)
                    .ConfigureAwait(false);
                lock (m_gate)
                {
                    RequireActiveOperation(runtime, linked.Token);
                }
                return result;
            }
            catch (Exception exception) when (PubSubFailure.IsExpected(exception))
            {
                observations.RecordEvidence(
                    "Operation", PubSubFailure.Status(exception), PubSubFailure.Describe(exception));
                m_logger.OperationFailed("PubSub operation", PubSubFailure.Status(exception).Code);
                throw;
            }
            finally
            {
                m_operations.Release();
            }
        }
    }

    private void RequireActiveOperation(PubSubRuntimeHandle runtime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (m_closed || m_phase != PubSubDocumentPhase.Running || !ReferenceEquals(m_active, runtime))
        {
            throw new OperationCanceledException("The PubSub operation's runtime is stopping or was replaced.");
        }
    }

    private void CaptureRuntimeCounters(PubSubRuntimeHandle runtime)
    {
        m_counters = [.. Enum.GetValues<PubSubDiagnosticsCounterKind>().Select(counter => new PubSubCounterRow(
            counter.ToString(),
            runtime.Application.Diagnostics.Read(counter),
            m_observations.TransportDiagnostics.Read(counter)))];
        m_components = [.. PubSubObservationBinding.EnumerateStates(runtime.Application).ToList().Select(state =>
            new PubSubComponentRow(state.ComponentName, state.State, state.StatusCode))];
        m_sourceSamples = runtime.Source?.Samples ?? 0;
    }

    private void RecordFailure(string operation, Exception exception)
    {
        StatusCode status = PubSubFailure.Status(exception);
        string detail = PubSubFailure.Describe(exception);
        lock (m_gate)
        {
            m_phase = PubSubDocumentPhase.Faulted;
            m_status = operation + ": " + detail;
        }
        m_observations.RecordEvidence(operation, status, detail);
        m_logger.OperationFailed(operation, status.Code);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (m_gate)
            {
                m_lifetime?.Dispose();
                m_lifetime = null;
                m_primarySession = null;
                m_phase = PubSubDocumentPhase.Disposed;
            }
            m_operations.Dispose();
        }
    }

    private static void RequireTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.FromMilliseconds(100) || timeout > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout), "Use a discovery/Action timeout between 0.1 and 5 seconds.");
        }
    }

    private static ArrayOf<PubSubDiscoveryRow> DescribeDiscovery(PubSubDiscoveryResult result)
    {
        var rows = new List<PubSubDiscoveryRow>();
        foreach (PubSubDataSetMetaDataDiscoveryResult entry in
            result.DataSetMetaDataEntries.SafeSlice(0, PubSubConfigurationValidation.MaxDiscoveryResponses))
        {
            rows.Add(new PubSubDiscoveryRow("Metadata",
                PubSubValueDisplay.Bound(entry.PublisherId.ToString(), 96),
                entry.StatusCode,
                string.Create(CultureInfo.InvariantCulture,
                    $"Writer {entry.DataSetWriterId}, group {entry.WriterGroupId}, " +
                    $"fields {entry.DataSetMetaData?.Fields.Count}.")));
        }
        foreach (PubSubDataSetWriterConfigurationDiscoveryResult entry in result.WriterConfigurations
            .SafeSlice(0, PubSubConfigurationValidation.MaxDiscoveryResponses - rows.Count))
        {
            rows.Add(new PubSubDiscoveryRow("Writer configuration",
                PubSubValueDisplay.Bound(entry.PublisherId.ToString(), 96),
                entry.StatusCode,
                string.Create(CultureInfo.InvariantCulture,
                    $"Group {entry.WriterGroupId}, " +
                    $"{entry.DataSetWriterIds.Count} writer identifiers. Credentials omitted.")));
        }
        foreach (EndpointDescription endpoint in result.PublisherEndpoints
            .SafeSlice(0, PubSubConfigurationValidation.MaxDiscoveryResponses - rows.Count))
        {
            string host = Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute, out Uri? uri)
                ? PubSubValueDisplay.Bound(uri.Host, 96) : "Invalid endpoint";
            rows.Add(new PubSubDiscoveryRow("Publisher endpoint", host, StatusCodes.Good,
                "Advertised endpoint only; not connected, trusted, or authorized by discovery."));
        }
        return [.. rows];
    }

    private readonly Lock m_gate = new();
    private readonly IPubSubRuntimeFactory m_factory;
    private readonly ILogger m_logger;
    private readonly TimeProvider m_clock;
    private readonly Func<TimeSpan, CancellationToken, Task> m_delay;
    private readonly SemaphoreSlim m_operations = new(1, 1);
    private PubSubConfiguration m_configuration = new();
    private PubSubObservationStore m_observations;
    private PubSubRuntimeHandle? m_active;
    private ISession? m_primarySession;
    private CancellationTokenSource? m_lifetime;
    private Task m_run = Task.CompletedTask;
    private Task? m_disposal;
    private PubSubDocumentPhase m_phase;
    private string m_status = "Offline. Configure a transport and select Start; no primary UA session is required.";
    private ArrayOf<PubSubCounterRow> m_counters = [];
    private ArrayOf<PubSubComponentRow> m_components = [];
    private ArrayOf<PubSubDiscoveryRow> m_discovery = [];
    private PubSubActionResult? m_action;
    private int m_sourceSamples;
    private bool m_closed;
}
