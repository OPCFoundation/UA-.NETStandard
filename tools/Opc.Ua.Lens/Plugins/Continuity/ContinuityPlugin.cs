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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Diagnostics;
using UaLens.Storage;
using UaLens.ViewModels;

namespace UaLens.Plugins.Continuity;

internal sealed record ContinuityScenarioChoice(ContinuityScenario Scenario, string Label);

/// <summary>
/// Explicitly armed Continuity document. Connection changes prepare availability,
/// never start or replay experiments. A transient ManagedSession reconnect is
/// observed without releasing or replacing that session.
/// </summary>
internal sealed partial class ContinuityPlugin : ObservableObject, IPlugin, IWorkspaceState
{
    public ContinuityPlugin(PluginHost host)
        : this(host, CreateDefaultFactory(host))
    {
    }

    public ContinuityPlugin(PluginHost host, IContinuityBackendFactory backendFactory)
        : this(
            backendFactory,
            () => host.Session,
            () => SelectedTarget(host),
            (host ?? throw new ArgumentNullException(nameof(host))).Connection.PublishLog)
    {
    }

    public ContinuityPlugin(
        IContinuityBackendFactory backendFactory,
        Func<ISession?> primary,
        Func<string?> selectedTarget,
        PublishLogObserver? publishLog = null)
    {
        m_backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        m_primary = primary ?? throw new ArgumentNullException(nameof(primary));
        m_selectedTarget = selectedTarget ?? throw new ArgumentNullException(nameof(selectedTarget));
        m_publishLog = publishLog;
        m_timeline = new ContinuityTimeline();
        m_backend = m_backendFactory.Create(m_timeline);
        RefreshSetup();
    }

    public PluginKind Kind => PluginKind.Continuity;

    public bool SupportsDuplicate => false;

    public Control? View => m_view ??= new ContinuityView { DataContext = this };

    public Control? HeaderToolbar => null;

    public List<ContinuityScenarioChoice> Scenarios { get; } =
    [
        new(ContinuityScenario.Observe, "Observe / user-managed outage"),
        new(ContinuityScenario.RecreateOwnedSubscription, "Recreate own subscription"),
        new(ContinuityScenario.TransferOnLoad, "Transfer on load (owned auxiliary sessions)"),
        new(ContinuityScenario.RecreateOnLoad, "Recreate on load (owned auxiliary sessions)"),
        new(ContinuityScenario.GracefulDurableRestore, "Graceful durable sample restore"),
        new(ContinuityScenario.ConfiguredFailover, "Configured redundant takeover")
    ];

    public bool CanConfigure => !m_busy && !HasActiveRun && m_disposeTask is null;

    public bool CanStart => CanConfigure && m_setupCanStart;

    public bool CanStop => m_run is not null &&
        m_run.Phase is not (
            ContinuityRunPhase.Ready or ContinuityRunPhase.Stopped or ContinuityRunPhase.RequiresSetup);

    public bool CanStep => !m_busy && m_setupCanStart && (m_run?.Phase is
        ContinuityRunPhase.Running or ContinuityRunPhase.WaitingForRestore) &&
        SelectedScenario.Scenario != ContinuityScenario.Observe;

    public string StepLabel => m_run?.Phase == ContinuityRunPhase.WaitingForRestore
        ? "Restore after your graceful restart"
        : SelectedScenario.Scenario == ContinuityScenario.GracefulDurableRestore
            ? "Save and close owned session"
            : "Run selected step";

    public string ScenarioDescription => SelectedScenario.Scenario switch
    {
        ContinuityScenario.Observe =>
            "A read-only subscription observes an outage you manage. ManagedSession handles reconnect; " +
            "UaLens never kills a server.",
        ContinuityScenario.RecreateOwnedSubscription =>
            "Start observation, then recreate only this lab's logical subscription. Old queues may be lost.",
        ContinuityScenario.TransferOnLoad =>
            "Save only this lab's subscription, close its auxiliary source session without deleting subscriptions, " +
            "then load on a same-user session. Actual transfer and recreate evidence are separate.",
        ContinuityScenario.RecreateOnLoad =>
            "Save configuration, delete the lab's source resources, then explicitly load with transfer disabled.",
        ContinuityScenario.GracefulDurableRestore =>
            "Requires the configured Quickstarts durable store. Save and close the owned session; you gracefully " +
            "restart the sample, then explicitly Restore. Abrupt-crash recovery and issued-token persistence " +
            "are not supported.",
        ContinuityScenario.ConfiguredFailover =>
            "Requires an explicitly configured redundant set and the existing redundancy/identity provider. " +
            "A lab-only takeover does not create another primary workspace or replicate server data.",
        _ => "Select a scenario."
    };

    public List<ContinuityEvidence> TimelineEntries
    {
        get => m_timelineEntries;
        private set => SetProperty(ref m_timelineEntries, value);
    }

    public List<DiagnosticMetric> DiagnosticRows
    {
        get => m_diagnosticRows;
        private set => SetProperty(ref m_diagnosticRows, value);
    }

    private bool HasActiveRun => m_run is not null && m_run.Phase is not (
        ContinuityRunPhase.Ready or ContinuityRunPhase.Stopped or ContinuityRunPhase.RequiresSetup);

    public IReadOnlyList<MenuItem> ContributeMenuItems() => Array.Empty<MenuItem>();

    public void OnActivated()
    {
        if (m_disposeTask is not null)
        {
            return;
        }
        m_timer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh());
        m_timer.Start();
        Refresh();
    }

    public void OnDeactivated() => m_timer?.Stop();

    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        if (m_boundPrimary is not null && !ReferenceEquals(m_boundPrimary, m_primary()))
        {
            m_timeline.Record(ContinuityEvidenceKind.Connection,
                "Primary workspace session explicitly detached/replaced; stopping the lab, " +
                "not simulating a transport outage.");
            await StopAsync().ConfigureAwait(true);
            m_boundPrimary = null;
        }
        RefreshSetup();
        NotifyAvailability();
    }

    public JsonElement CaptureState()
    {
        ContinuityConfiguration configuration = ReadConfiguration();
        return JsonSerializer.SerializeToElement(
            ContinuityState.Capture(configuration), ContinuityStateJsonContext.Default.ContinuityStateDto);
    }

    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanConfigure)
        {
            throw new InvalidOperationException("Stop the running lab before restoring configuration.");
        }
        ContinuityStateDto dto = state.Deserialize(ContinuityStateJsonContext.Default.ContinuityStateDto) ??
            throw new FormatException("The Continuity configuration is missing.");
        ContinuityConfiguration configuration = ContinuityState.Validate(dto);
        ApplyConfiguration(configuration);
        m_startFailure = null;
        Status = "Saved configuration prepared offline. Start is required; " +
            "no runtime snapshot or workload was restored.";
        return Task.CompletedTask;
    }

    public ByteString CaptureEvidence()
    {
        return DiagnosticEvidenceExport.Create(
            m_primary(), m_publishLog, ReadConfiguration(), m_timeline.Snapshot());
    }

    public ValueTask DisposeAsync()
    {
        lock (m_disposeGate)
        {
            m_disposeTask ??= DisposeCoreAsync();
            return new ValueTask(m_disposeTask);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!CanStart)
        {
            return;
        }
        m_busy = true;
        NotifyAvailability();
        bool started = false;
        m_startFailure = null;
        try
        {
            ContinuityConfiguration configuration = ReadConfiguration();
            if (m_run is not null)
            {
                await m_run.DisposeAsync().ConfigureAwait(true);
                m_timeline = new ContinuityTimeline();
                m_backend = m_backendFactory.Create(m_timeline);
            }
            m_run = new ContinuityRun(m_backend, m_timeline);
            m_boundPrimary = m_primary();
            Task starting = m_run.StartAsync(configuration, CancellationToken.None);
            NotifyAvailability();
            await starting.ConfigureAwait(true);
            started = true;
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            InvalidOperationException or NotSupportedException or TimeoutException or IOException or
            UnauthorizedAccessException or ArgumentException or FormatException or AggregateException)
        {
            m_startFailure = "Start failed: " + CorrelatedDiagnostics.Failure(exception);
        }
        finally
        {
            try
            {
                if (!started)
                {
                    await CleanupFailedStartAsync().ConfigureAwait(true);
                }
            }
            finally
            {
                m_busy = false;
                Refresh();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (m_run is null)
        {
            return;
        }
        try
        {
            await m_run.StopAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            InvalidOperationException or NotSupportedException or TimeoutException or IOException or
            UnauthorizedAccessException or AggregateException)
        {
            Status = "Cleanup unconfirmed: " + CorrelatedDiagnostics.Failure(exception);
        }
        finally
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private async Task StepAsync()
    {
        if (!CanStep || m_run is null)
        {
            return;
        }
        m_busy = true;
        NotifyAvailability();
        try
        {
            Task stepping = m_run.StepAsync(CancellationToken.None);
            NotifyAvailability();
            await stepping.ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            InvalidOperationException or NotSupportedException or TimeoutException or IOException or
            UnauthorizedAccessException or ArgumentException or FormatException or AggregateException)
        {
            Status = "Step failed: " + CorrelatedDiagnostics.Failure(exception);
        }
        finally
        {
            m_busy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private void UseSelectedVariable()
    {
        string? selected = m_selectedTarget();
        if (string.IsNullOrEmpty(selected))
        {
            Status = "Select a Variable in the address space first. No server value is written by this lab.";
            return;
        }
        TargetText = selected;
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private void UseServerClockSample()
    {
        TargetText = "i=2258";
        MonotonicSample = false;
        Status = "Selected ServerStatus.CurrentTime (read-only). For +1 counters select a repository sample Variable.";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        TopLevel? topLevel = m_view is null ? null : TopLevel.GetTopLevel(m_view);
        if (topLevel?.StorageProvider is not { CanSave: true } storage)
        {
            Status = "Export requires a desktop storage provider.";
            return;
        }
        try
        {
            ByteString evidence = CaptureEvidence();
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export bounded redacted continuity evidence",
                SuggestedFileName = "continuity-evidence.json",
                DefaultExtension = "json"
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            using (file)
            {
                Stream stream = await file.OpenWriteAsync().ConfigureAwait(true);
                await using (stream.ConfigureAwait(true))
                {
                    stream.SetLength(0);
                    await stream.WriteAsync(evidence.Memory, CancellationToken.None).ConfigureAwait(true);
                }
            }
            Status = "Exported bounded, structurally redacted evidence; " +
                "raw values and runtime snapshots were excluded.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or InvalidOperationException or OperationCanceledException or
            ServiceResultException or JsonException or FormatException)
        {
            Status = "Export failed: " + CorrelatedDiagnostics.Failure(exception);
        }
    }

    private void Refresh()
    {
        if (m_disposeTask is not null)
        {
            return;
        }
        ContinuityTimelineSnapshot snapshot = m_timeline.Snapshot();
        TimelineEntries = snapshot.Entries.ToList();
        ContinuityCounters counters = snapshot.Counters;
        CounterText = string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} values · {1:N0} non-good · {2:N0} V2 missing-slot increases · {3:N0} republish-attempt increases · " +
            "{4:N0} recovery observations · {5:N0} transfer observations · {6:N0} timeline evictions",
            counters.Values, counters.BadValues, counters.MissingMessages, counters.RepublishAttempts,
            counters.RecoveryObservations, counters.TransferObservations, counters.EvictedEvidence);
        ObservationText = string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} sequence discontinuities · {1:N0} unknown-partition values · {2:N0} +1 sample discontinuities · " +
            "{3:N0} non-integer samples · {4:N0} untracked streams · {5:N0} server queue overflow observations. " +
            "{6:N0} counter boundaries (counts there are uncertain). Discontinuities alone are not proven loss.",
            counters.SequenceDiscontinuities, counters.UnknownPartitionValues,
            counters.MonotonicDiscontinuities, counters.NonNumericSamples,
            counters.UntrackedStreams, counters.QueueOverflowObservations, counters.CounterBoundaryObservations);
        UiTimingText = "UI snapshot rendered at " +
            DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
            " UTC (250 ms display cadence; not packet receipt).";
        try
        {
            DiagnosticRows = m_backend.CaptureDiagnostics().ToList();
        }
        catch (Exception exception) when (exception is ServiceResultException or InvalidOperationException or
            NotSupportedException)
        {
            DiagnosticRows = [new DiagnosticMetric("Snapshot unavailable", CorrelatedDiagnostics.Failure(exception))];
        }
        if (m_startFailure is not null)
        {
            Status = m_run is null ? m_startFailure : m_startFailure + " " + m_run.Status;
        }
        else if (m_run is not null)
        {
            Status = m_run.Status;
        }
        RefreshSetup();
        NotifyAvailability();
    }

    private void RefreshSetup()
    {
        try
        {
            ContinuitySetup setup = m_backend.CheckSetup(ReadConfiguration());
            SetupText = (setup.CanStart ? "Ready to attempt: " : setup.Availability + ": ") + setup.Description;
            m_setupCanStart = setup.CanStart;
        }
        catch (FormatException exception)
        {
            SetupText = exception.Message;
            m_setupCanStart = false;
        }
        catch (Exception exception) when (exception is ServiceResultException or InvalidOperationException or
            NotSupportedException)
        {
            SetupText = "Availability unknown: " + CorrelatedDiagnostics.Failure(exception);
            m_setupCanStart = false;
        }
        NotifyAvailability();
    }

    private ContinuityConfiguration ReadConfiguration()
    {
        if (TargetText.Length > ContinuityState.MaxTargets * 2050)
        {
            throw new FormatException("The target text exceeds the bounded lab configuration limit.");
        }
        string[] targets = TargetText.Split(
            s_targetSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ContinuityState.Validate(new ContinuityStateDto
        {
            Scenario = (int)SelectedScenario.Scenario,
            Targets = new List<string>(targets),
            PublishingIntervalMs = PublishingIntervalMs,
            SamplingIntervalMs = SamplingIntervalMs,
            QueueSize = QueueSize,
            KeepAliveCount = KeepAliveCount,
            LifetimeCount = LifetimeCount,
            ItemsPerPartition = ItemsPerPartition,
            Durable = Durable,
            DurableLifetimeHours = DurableLifetimeHours,
            MonotonicSample = MonotonicSample
        });
    }

    private void ApplyConfiguration(ContinuityConfiguration configuration)
    {
        foreach (ContinuityScenarioChoice choice in Scenarios)
        {
            if (choice.Scenario == configuration.Scenario)
            {
                SelectedScenario = choice;
                break;
            }
        }
        var targets = new List<string>();
        foreach (ExpandedNodeId target in configuration.Targets)
        {
            targets.Add(target.ToString());
        }
        TargetText = string.Join(Environment.NewLine, targets);
        PublishingIntervalMs = configuration.PublishingIntervalMs;
        SamplingIntervalMs = configuration.SamplingIntervalMs;
        QueueSize = configuration.QueueSize;
        KeepAliveCount = configuration.KeepAliveCount;
        LifetimeCount = configuration.LifetimeCount;
        ItemsPerPartition = configuration.ItemsPerPartition;
        Durable = configuration.Durable;
        DurableLifetimeHours = configuration.DurableLifetimeHours;
        MonotonicSample = configuration.MonotonicSample;
        RefreshSetup();
    }

    private async Task CleanupFailedStartAsync()
    {
        if (m_run is null)
        {
            return;
        }
        try
        {
            await m_run.StopAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            InvalidOperationException or NotSupportedException or TimeoutException or IOException or
            UnauthorizedAccessException or AggregateException)
        {
            // The run retains the specific cleanup evidence and status.
        }
    }

    private async Task DisposeCoreAsync()
    {
        m_timer?.Stop();
        try
        {
            if (m_run is not null)
            {
                await m_run.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await m_backend.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            m_boundPrimary = null;
        }
    }

    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanStep));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(ScenarioDescription));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        StepCommand.NotifyCanExecuteChanged();
        UseSelectedVariableCommand.NotifyCanExecuteChanged();
        UseServerClockSampleCommand.NotifyCanExecuteChanged();
    }

    private static V2ContinuityBackendFactory CreateDefaultFactory(PluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new V2ContinuityBackendFactory(
            () => host.Session,
            host.Telemetry,
            host.Connection.PublishLog,
            new WorkspaceContinuitySessionFactory(host.Connection, host.Telemetry));
    }

    private static string? SelectedTarget(PluginHost host)
    {
        ISession? session = host.Session;
        return session is not null && host.Workspace.SelectedNode is { NodeClass: NodeClass.Variable } node
            ? NodeId.ToExpandedNodeId(node.NodeId, session.NamespaceUris).ToString()
            : null;
    }

    partial void OnSelectedScenarioChanged(ContinuityScenarioChoice value) => RefreshSetup();

    partial void OnTargetTextChanged(string value) => RefreshSetup();

    partial void OnPublishingIntervalMsChanged(double value) => RefreshSetup();

    partial void OnSamplingIntervalMsChanged(double value) => RefreshSetup();

    partial void OnQueueSizeChanged(uint value) => RefreshSetup();

    partial void OnKeepAliveCountChanged(uint value) => RefreshSetup();

    partial void OnLifetimeCountChanged(uint value) => RefreshSetup();

    partial void OnItemsPerPartitionChanged(uint value) => RefreshSetup();

    partial void OnDurableChanged(bool value) => RefreshSetup();

    partial void OnDurableLifetimeHoursChanged(int value) => RefreshSetup();

    [ObservableProperty]
    private string m_title = "Continuity Lab";

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "Ready. Start is explicit; the lab creates no server resource while idle.";

    [ObservableProperty]
    private string m_setupText = string.Empty;

    [ObservableProperty]
    private string m_counterText = "No observations yet.";

    [ObservableProperty]
    private string m_observationText = string.Empty;

    [ObservableProperty]
    private string m_uiTimingText = string.Empty;

    [ObservableProperty]
    private ContinuityScenarioChoice m_selectedScenario = new(
        ContinuityScenario.Observe, "Observe / user-managed outage");

    [ObservableProperty]
    private string m_targetText = "i=2258";

    [ObservableProperty]
    private double m_publishingIntervalMs = 1000;

    [ObservableProperty]
    private double m_samplingIntervalMs = 250;

    [ObservableProperty]
    private uint m_queueSize = 100;

    [ObservableProperty]
    private uint m_keepAliveCount = 10;

    [ObservableProperty]
    private uint m_lifetimeCount = 300;

    [ObservableProperty]
    private uint m_itemsPerPartition = 16;

    [ObservableProperty]
    private bool m_durable;

    [ObservableProperty]
    private int m_durableLifetimeHours = 1;

    [ObservableProperty]
    private bool m_monotonicSample;

    private static readonly char[] s_targetSeparators = ['\r', '\n'];
    private readonly System.Threading.Lock m_disposeGate = new();
    private readonly IContinuityBackendFactory m_backendFactory;
    private readonly Func<ISession?> m_primary;
    private readonly Func<string?> m_selectedTarget;
    private readonly PublishLogObserver? m_publishLog;
    private ContinuityTimeline m_timeline;
    private IContinuityBackend m_backend;
    private ContinuityRun? m_run;
    private ISession? m_boundPrimary;
    private ContinuityView? m_view;
    private DispatcherTimer? m_timer;
    private Task? m_disposeTask;
    private bool m_busy;
    private bool m_setupCanStart;
    private string? m_startFailure;
    private List<ContinuityEvidence> m_timelineEntries = [];
    private List<DiagnosticMetric> m_diagnosticRows = [];
}
