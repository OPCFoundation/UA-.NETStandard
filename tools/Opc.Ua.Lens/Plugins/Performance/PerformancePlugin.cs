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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Storage;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Time unit applied to the Performance run duration field.  Allows
/// users to express the duration as seconds, minutes or hours via the
/// "[N] [Unit]" TimeSpan-style composite editor in the view.
/// </summary>
internal enum DurationUnit
{
    Seconds,
    Minutes,
    Hours
}

/// <summary>
/// View model for a single Performance tab.  Owns the Target descriptor,
/// the workload config (mode / rate / duration / value generator), the
/// <see cref="BenchmarkRunner"/> and the live aggregated stats consumed
/// by <see cref="PerformanceView"/>.  Implements
/// <see cref="IPlugin"/> so it slots into the workbench tab
/// strip exactly like every other tab kind.
/// </summary>
internal sealed partial class PerformancePlugin : ObservableObject, IPlugin, IWorkspaceState
{
    private static int s_nextNumber;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private readonly LatencyHistogram m_histogram = new();
    private readonly long[] m_histogramScratch = new long[LatencyHistogram.BucketCount];
    private readonly ConcurrentQueue<(double seconds, double opsPerSec)> m_throughputQueue = new();
    private BenchmarkRunner? m_runner;
    private PerformanceView? m_view;

    private DispatcherTimer? m_aggregationTimer;
    private long m_lastAggregationOps;
    private long m_runStartTicks;
    private TimeSpan m_runDuration;
    private BenchmarkConfiguration? m_runConfiguration;
    private string m_runNotes = string.Empty;
    private readonly Lock m_finishGate = new();
    private BenchmarkRun? m_pendingRun;
    private string? m_pendingError;
    private bool m_isDisposed;

    /// <summary>
    /// Total ops completed in the current run.
    /// </summary>
    private long m_totalOps;

    /// <summary>
    /// Errors observed in the current run.
    /// </summary>
    private long m_errorOps;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "● Idle";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyPropertyChangedFor(nameof(ConcurrencyHint))]
    private BenchmarkMode m_mode = BenchmarkMode.Write;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyPropertyChangedFor(nameof(TargetDescription))]
    private BenchmarkTarget? target;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetRateText))]
    [NotifyPropertyChangedFor(nameof(ConcurrencyHint))]
    private double targetRate = 200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateEditable))]
    [NotifyPropertyChangedFor(nameof(ConcurrencyHint))]
    private bool unboundedBurst;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveDurationText))]
    private int m_durationSeconds = 1;

    /// <summary>
    /// Unit applied to <see cref="DurationSeconds"/> to compute the
    /// effective run duration.  Choices: Seconds / Minutes / Hours.
    /// Defaults to Hours so a freshly opened tab is wired up for a
    /// realistic long-running benchmark instead of a 10-second burst.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveDurationText))]
    private DurationUnit m_durationUnit = DurationUnit.Hours;

    public IReadOnlyList<DurationUnit> DurationUnitOptions { get; } =
        new[] { DurationUnit.Seconds, DurationUnit.Minutes, DurationUnit.Hours };

    /// <summary>
    /// Effective run duration based on <see cref="DurationSeconds"/> +
    /// <see cref="DurationUnit"/>.  Clamped so callers always see a
    /// positive duration.
    /// </summary>
    public TimeSpan EffectiveDuration => DurationUnit switch
    {
        DurationUnit.Minutes => TimeSpan.FromMinutes(Math.Max(1, DurationSeconds)),
        DurationUnit.Hours => TimeSpan.FromHours(Math.Max(1, DurationSeconds)),
        _ => TimeSpan.FromSeconds(Math.Max(1, DurationSeconds))
    };

    [ObservableProperty]
    private ValueGenerator m_generator = ValueGenerator.Random;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadResultsCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickTargetCommand))]
    [NotifyPropertyChangedFor(nameof(ConfigurationEditable))]
    private bool isRunning;

    [ObservableProperty]
    private string m_totalOpsText = "0";

    [ObservableProperty]
    private string m_meanRateText = "0";

    [ObservableProperty]
    private string m_p50Text = "—";

    [ObservableProperty]
    private string m_p95Text = "—";

    [ObservableProperty]
    private string m_p99Text = "—";

    [ObservableProperty]
    private string m_maxLatencyText = "—";

    [ObservableProperty]
    private string m_errorsText = "0";

    /// <summary>
    /// Legacy workspace display preference. It only highlights the latest three
    /// rows and is independent of the chosen baseline and selected comparison.
    /// </summary>
    [ObservableProperty]
    private bool m_compareLast3;

    /// <summary>
    /// Maximum retained runs; the oldest is evicted.
    /// </summary>
    public const int MaxHistorySize = BenchmarkHistory.Capacity;

    /// <summary>
    /// Completed snapshots, newest first, including genuine distributions when captured.
    /// </summary>
    public ReadOnlyObservableCollection<BenchmarkRunRow> RunHistory => History.Runs;

    public BenchmarkHistory History { get; }
    public bool ConfigurationEditable => !IsRunning && !m_isDisposed;

    /// <summary>
    /// Current p50 latency in milliseconds — read by the view to drive the marker line.
    /// </summary>
    public double P50Ms { get; private set; }

    /// <summary>
    /// Current p95 latency in milliseconds — read by the view to drive the marker line.
    /// </summary>
    public double P95Ms { get; private set; }

    /// <summary>
    /// Current p99 latency in milliseconds — read by the view to drive the marker line.
    /// </summary>
    public double P99Ms { get; private set; }

    /// <summary>
    /// Total histogram samples — used by the view to skip Y autoscale when empty.
    /// </summary>
    public long HistogramTotal => m_histogram.Count;

    /// <summary>
    /// Flag the view consumes to clear its DataLogger after a Reset.
    /// </summary>
    public bool WasReset { get; set; }

    /// <summary>
    /// Static items for the Mode dropdown (compiled bindings).
    /// </summary>
    public IReadOnlyList<BenchmarkMode> ModeOptions { get; } =
        new[] { BenchmarkMode.Write, BenchmarkMode.Call };

    /// <summary>
    /// Static items for the Value-generator dropdown.
    /// </summary>
    public IReadOnlyList<ValueGenerator> GeneratorOptions { get; } =
        new[] { ValueGenerator.Random, ValueGenerator.Sequential, ValueGenerator.Fixed };

    public PerformancePlugin(PluginHost host, BenchmarkHistory? history = null)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        History = history ?? new BenchmarkHistory();
        int n = System.Threading.Interlocked.Increment(ref s_nextNumber);
        m_title = $"Performance {n}";
    }

    public PluginKind Kind => PluginKind.Performance;

    Control? IPlugin.View => m_view ??= new PerformanceView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return new[]
        {
            CreateMenuItem("_Run",                RunCommand),
            CreateMenuItem("_Stop",               StopCommand),
            CreateMenuItem("_Reset",              ResetCommand),
            CreateMenuItem("_Configure Target…",  PickTargetCommand),
            CreateMenuItem("_Export Stats CSV",   ExportStatsCommand),
            CreateMenuItem("_Save Results…",      SaveResultsCommand),
            CreateMenuItem("_Load Results…",      LoadResultsCommand)
        };
    }

    private static MenuItem CreateMenuItem(string header, System.Windows.Input.ICommand cmd)
    {
        var item = new MenuItem { Header = header, Command = cmd };
        return item;
    }

    public void OnActivated() { }
    public void OnDeactivated() { }

    /// <summary>
    /// Re-evaluate Run / Stop CanExecute when the host connects or
    /// disconnects — <see cref="CanRun"/> pivots on
    /// <c>m_host.Connection.Session</c>.
    /// </summary>
    public void OnConnectionStateChanged()
    {
        RunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        PickTargetCommand.NotifyCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        m_isDisposed = true;
        if (m_runner is not null)
        {
            try
            {
                await m_runner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is ServiceResultException or IOException or TimeoutException or OperationCanceledException)
            {
                PerformancePluginLog.StopFailed(m_log, Title, ex);
            }
            m_runner.OnSample -= HandleSample;
            m_runner.OnFinished -= HandleFinished;
            m_runner = null;
        }
        m_aggregationTimer?.Stop();
    }

    /// <summary>
    /// Human-readable description of the configured Target (or "(no Target)").
    /// </summary>
    public string TargetDescription => Target is null
        ? "(no Target — pick a Variable or Method first)"
        : Target.DisplayName;

    /// <summary>
    /// NodeId of the configured target as a mono-spaced string for the toolbar label.
    /// </summary>
    public string TargetNodeIdText => Target is null
        ? "(no target selected)"
        : Target.NodeId.ToString() ?? "(null)";

    /// <summary>
    /// Short summary used by the Settings dialog button label.
    /// </summary>
    public string EffectiveDurationText
    {
        get
        {
            TimeSpan d = EffectiveDuration;
            if (d.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0:0.##} h", d.TotalHours);
            }
            if (d.TotalMinutes >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0:0.##} min", d.TotalMinutes);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0:0} s", d.TotalSeconds);
        }
    }

    public bool RateEditable => !UnboundedBurst && !IsRunning;

    public string TargetRateText => string.Format(CultureInfo.InvariantCulture,
        "{0:N0}", Math.Round(TargetRate));

    public string ConcurrencyHint
    {
        get
        {
            int rec = BenchmarkRunner.RecommendConcurrency(TargetRate);
            return UnboundedBurst
                ? $"Burst mode — capped at {BenchmarkRunner.MaxConcurrencyCap} in-flight ops."
                : $"≈{rec} max in-flight ops (capped at {BenchmarkRunner.MaxConcurrencyCap}).";
        }
    }

    /// <summary>
    /// Opens a modal <see cref="PerformanceSettingsDialog"/> hosting the
    /// workload editor (rate / unbounded burst / duration / value
    /// generator). The dialog binds the same plug-in instance so live
    /// changes propagate into the toolbar's effective-duration label.
    /// </summary>
    [RelayCommand]
    private async Task OpenSettingsDialogAsync()
    {
        Window? owner = GetOwnerWindow();
        var dlg = new PerformanceSettingsDialog { DataContext = this };
        if (owner is null)
        {
            await dlg.ShowDialog(new Window()).ConfigureAwait(true);
        }
        else
        {
            await dlg.ShowDialog(owner).ConfigureAwait(true);
        }
    }

    private bool CanPickTarget() => ConfigurationEditable && m_host.Connection.CurrentSession is not null;

    [RelayCommand(CanExecute = nameof(CanPickTarget))]
    private async Task PickTargetAsync()
    {
        if (m_host.Connection.CurrentSession is not { } session)
        {
            Status = "● Not connected — connect first.";
            return;
        }

        Window? owner = null;
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
        {
            owner = desk.MainWindow;
        }

        // If the main address-space tree doesn't have a node whose
        // NodeClass matches the current Mode (Write → Variable,
        // Call → Method), run BrowsePickerDialog first so the user can
        // pick one without un-hiding the tree.
        NodeViewModel? hint = null;
        NodeViewModel? sel = m_host.Workspace.SelectedNode;
        bool selValid = sel is not null
            && (Mode == BenchmarkMode.Write
                ? sel.NodeClass == NodeClass.Variable
                : sel.NodeClass == NodeClass.Method);
        if (!selValid)
        {
            NodeClass accepted = Mode == BenchmarkMode.Write
                ? NodeClass.Variable
                : NodeClass.Method;
            string label = Mode == BenchmarkMode.Write ? "Variable" : "Method";
            var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
                Session: session,
                Root: ObjectIds.ObjectsFolder,
                Title: $"Pick {label} for Performance target",
                AcceptedClasses: accepted,
                Header: $"Browse the address space and pick a {label} to use as the Performance target."));
            NodeId pickedId = owner is null
                ? await picker.ShowDialog<NodeId>(new Window()).ConfigureAwait(true)
                : await picker.ShowDialog<NodeId>(owner).ConfigureAwait(true);
            if (pickedId.IsNull)
            {
                Status = "● Pick target cancelled.";
                return;
            }
            // Build a synthetic NodeViewModel hint that
            // PerformanceTargetDialog can read.
            hint = new NodeViewModel(
                m_host.Browser,
                NodeId.Null,
                pickedId,
                picker.PickedDisplay,
                picker.PickedNodeClass);
        }

        var dialog = new PerformanceTargetDialog(m_host.Workspace, session, hint);
        BenchmarkTarget? result = owner is null
            ? await dialog.ShowDialog<BenchmarkTarget?>(new Window()).ConfigureAwait(true)
            : await dialog.ShowDialog<BenchmarkTarget?>(owner).ConfigureAwait(true);

        if (result is not null)
        {
            Target = result;
            Mode = result.Mode;
            Status = $"● Target set: {result.DisplayName}";
        }
    }

    private bool CanRun() =>
        ConfigurationEditable
        && Target is not null
        && m_host.Connection.CurrentSession is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (m_host.Connection.CurrentSession is not { } session || Target is not { } runTarget)
        {
            Status = "● Select a target and connect before running a benchmark.";
            return;
        }
        if (!ConfigurationEditable)
        {
            throw new InvalidOperationException("Stop the active workload before starting another benchmark.");
        }
        // Verify mode matches the Target.
        if (runTarget.Mode != Mode)
        {
            Status = $"● Target is a {runTarget.Mode} target - switch the Mode combo to match.";
            return;
        }

        BenchmarkConfiguration configuration;
        try
        {
            configuration = BenchmarkConfiguration.Capture(
                session, runTarget, Generator, TargetRate, UnboundedBurst, EffectiveDuration);
        }
        catch (FormatException ex)
        {
            Status = $"● Invalid workload: {ex.Message}";
            PerformancePluginLog.RunFailed(m_log, ex.Message);
            return;
        }
        if (m_runner is { } previous)
        {
            await previous.DisposeAsync().ConfigureAwait(true);
            previous.OnSample -= HandleSample;
            previous.OnFinished -= HandleFinished;
        }
        Reset();
        m_runConfiguration = configuration;
        m_runDuration = TimeSpan.FromSeconds(configuration.DurationSeconds);
        m_runNotes = string.Format(CultureInfo.InvariantCulture,
            "mode={0}; gen={1}; burst={2}; target={3}",
            configuration.Mode, configuration.Generator, configuration.UnboundedBurst, runTarget.DisplayName);
        m_runner = new BenchmarkRunner(
            session,
            runTarget,
            configuration.Generator,
            configuration.TargetRate,
            configuration.UnboundedBurst,
            m_runDuration);
        m_runner.OnSample += HandleSample;
        m_runner.OnFinished += HandleFinished;
        IsRunning = true;
        Status = "● Running…";
        m_runStartTicks = Stopwatch.GetTimestamp();
        m_runner.Start();

        m_aggregationTimer?.Stop();
        m_aggregationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => OnAggregationTick());
        m_aggregationTimer.Start();
        PerformancePluginLog.RunStarted(
            m_log, configuration.Mode, configuration.TargetRate, configuration.UnboundedBurst,
            configuration.DurationSeconds, runTarget.DisplayName);
    }

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (m_runner is { } r)
        {
            Status = "● Stopping…";
            await r.StopAsync().ConfigureAwait(true);
        }
        CompletePendingRun();
    }

    private bool CanReset() => ConfigurationEditable;

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        if (!ConfigurationEditable)
        {
            throw new InvalidOperationException("Stop the workload before resetting live statistics.");
        }
        m_histogram.Reset();
        System.Threading.Interlocked.Exchange(ref m_totalOps, 0);
        System.Threading.Interlocked.Exchange(ref m_errorOps, 0);
        m_lastAggregationOps = 0;
        m_runStartTicks = Stopwatch.GetTimestamp();
        while (m_throughputQueue.TryDequeue(out _))
        { }
        WasReset = true;
        UpdateStatsTexts(0);
    }

    [RelayCommand]
    private async Task ExportStatsAsync()
    {
        Window? owner = null;
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
        {
            owner = desk.MainWindow;
        }
        if (owner is null)
        {
            return;
        }

        IStorageProvider? storage = owner.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var opts = new FilePickerSaveOptions
        {
            Title = "Export performance stats",
            SuggestedFileName = $"ualens-perf-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            DefaultExtension = "csv"
        };
        IStorageFile? file = await storage.SaveFilePickerAsync(opts).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        try
        {
            Stream s = await file.OpenWriteAsync().ConfigureAwait(true);
            await using (s.ConfigureAwait(false))
            {
                var w = new StreamWriter(s, Encoding.UTF8);
                await using (w.ConfigureAwait(false))
                {
                    await w.WriteLineAsync("metric,value").ConfigureAwait(true);
                    await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                        "total_ops,{0}", System.Threading.Interlocked.Read(ref m_totalOps))).ConfigureAwait(true);
                    await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                        "errors,{0}", System.Threading.Interlocked.Read(ref m_errorOps))).ConfigureAwait(true);
                    await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                        "p50_ms,{0:F3}", P50Ms)).ConfigureAwait(true);
                    await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                        "p95_ms,{0:F3}", P95Ms)).ConfigureAwait(true);
                    await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                        "p99_ms,{0:F3}", P99Ms)).ConfigureAwait(true);
                    await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                        "max_ms,{0:F3}", m_histogram.MaxMsObserved)).ConfigureAwait(true);
                    await w.WriteLineAsync().ConfigureAwait(true);
                    await w.WriteLineAsync("bucket_lower_ms,bucket_upper_ms,count").ConfigureAwait(true);
                    long[] snap = new long[LatencyHistogram.BucketCount];
                    m_histogram.Snapshot(snap);
                    for (int i = 0; i < LatencyHistogram.BucketCount; i++)
                    {
                        if (snap[i] == 0)
                        {
                            continue;
                        }

                        await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                            "{0:G},{1:G},{2}",
                            LatencyHistogram.BucketLowerMs(i),
                            LatencyHistogram.BucketUpperMs(i),
                            snap[i])).ConfigureAwait(true);
                    }
                }
            }
            Status = $"● Stats exported: {file.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Status = $"● Export failed: {ex.Message}";
            PerformancePluginLog.ExportFailed(m_log, ex);
        }
    }

    [RelayCommand]
    private async Task SaveResultsAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }

        IStorageProvider? storage = owner.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var opts = new FilePickerSaveOptions
        {
            Title = "Save benchmark run history",
            SuggestedFileName = string.Format(CultureInfo.InvariantCulture,
                "ualens-perf-runs-{0:yyyyMMdd-HHmmss}.json", DateTime.UtcNow),
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("Performance comparison JSON") { Patterns = ["*.json"] },
                new FilePickerFileType("Legacy CSV (aggregates only)") { Patterns = ["*.csv"] }
            ]
        };
        IStorageFile? file = await storage.SaveFilePickerAsync(opts).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        try
        {
            bool aggregateCsv = file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            string content = ExportResults(aggregateCsv);
            Stream s = await file.OpenWriteAsync().ConfigureAwait(true);
            await using (s.ConfigureAwait(false))
            {
                if (s.CanSeek)
                {
                    s.SetLength(0);
                }
                var w = new StreamWriter(s, new UTF8Encoding(false));
                await using (w.ConfigureAwait(false))
                {
                    await w.WriteAsync(content).ConfigureAwait(true);
                }
            }
            Status = string.Format(CultureInfo.InvariantCulture,
                "● Saved {0} run(s) to {1}.{2}", RunHistory.Count, file.Name,
                aggregateCsv
                    ? " CSV omits configuration, elapsed time, selections and histograms."
                    : " Comparison evidence and distributions retained.");
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or
                FormatException or JsonException)
        {
            Status = $"● Save failed: {ex.Message}";
            PerformancePluginLog.SaveFailed(m_log, ex);
        }
    }

    private bool CanLoadResults() => ConfigurationEditable;

    [RelayCommand(CanExecute = nameof(CanLoadResults))]
    private async Task LoadResultsAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }

        IStorageProvider? storage = owner.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var opts = new FilePickerOpenOptions
        {
            Title = "Load benchmark run history",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Performance results") { Patterns = ["*.json", "*.csv"] }
            ]
        };
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(opts).ConfigureAwait(true);
        if (files.Count == 0)
        {
            return;
        }

        IStorageFile file = files[0];
        try
        {
            Stream s = await file.OpenReadAsync().ConfigureAwait(true);
            await using (s.ConfigureAwait(false))
            {
                BenchmarkArchive archive = await BenchmarkArchiveCodec.ReadAsync(s).ConfigureAwait(true);
                ImportResults(archive);
            }
            Status += $" File: {file.Name}.";
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or
                JsonException or DecoderFallbackException or InvalidOperationException)
        {
            Status = $"● Load failed: {ex.Message}";
            PerformancePluginLog.LoadFailed(m_log, ex);
        }
    }

    public string ExportResults(bool aggregateCsv = false)
    {
        BenchmarkArchive archive = History.Capture();
        return aggregateCsv ? BenchmarkArchiveCodec.SerializeCsv(archive) : BenchmarkArchiveCodec.Serialize(archive);
    }

    public void ImportResults(string content)
    {
        try
        {
            ImportResults(BenchmarkArchiveCodec.Parse(content));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            Status = $"● Load failed: {ex.Message}";
            PerformancePluginLog.LoadFailed(m_log, ex);
            throw;
        }
    }

    private void ImportResults(BenchmarkArchive archive)
    {
        if (!ConfigurationEditable)
        {
            throw new InvalidOperationException("Stop the workload before importing benchmark history.");
        }
        History.Replace(archive);
        Status = string.Format(CultureInfo.InvariantCulture,
            "● Loaded {0} run(s). {1} {2}", RunHistory.Count,
            archive.IsLegacyCsv
                ? "Legacy CSV: configuration, timing and distributions are missing."
                : "Results only; workload settings were not changed and no run was started.",
            History.RetentionNotice);
    }

    /// <summary>
    /// Freezes results after the runner has drained, before posting to the UI.
    /// </summary>
    private BenchmarkRun SnapshotCurrentRun()
    {
        BenchmarkRunner runner = m_runner ?? throw new InvalidOperationException("No runner owns this snapshot.");
        BenchmarkConfiguration configuration = m_runConfiguration
            ?? throw new InvalidOperationException("The run configuration was not captured.");
        long errors = System.Threading.Interlocked.Read(ref m_errorOps);
        return BenchmarkRun.Create(
            configuration, m_histogram.Capture(), errors, runner.Elapsed,
            DateTime.UtcNow, runner.Completion, m_runNotes);
    }

    /// <summary>
    /// Preserves the old workspace preference without presenting highlighting as comparison.
    /// </summary>
    partial void OnCompareLast3Changed(bool value) => History.HighlightLatestThree = value;

    private static Window? GetOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
        {
            return desk.MainWindow;
        }
        return null;
    }

    private void HandleSample(BenchmarkSample sample)
    {
        m_histogram.Record(sample.LatencyMs);
        System.Threading.Interlocked.Increment(ref m_totalOps);
        if (!sample.Success)
        {
            System.Threading.Interlocked.Increment(ref m_errorOps);
        }
    }

    private void HandleFinished(string? error)
    {
        if (m_isDisposed)
        {
            return;
        }
        BenchmarkRun run = SnapshotCurrentRun();
        lock (m_finishGate)
        {
            m_pendingRun = run;
            m_pendingError = error;
        }
        Dispatcher.UIThread.Post(CompletePendingRun);
    }

    private void CompletePendingRun()
    {
        BenchmarkRun? run;
        string? error;
        lock (m_finishGate)
        {
            run = m_pendingRun;
            error = m_pendingError;
            m_pendingRun = null;
            m_pendingError = null;
        }
        if (run is null || m_isDisposed)
        {
            return;
        }
        IsRunning = false;
        m_aggregationTimer?.Stop();
        UpdateStatsTexts(run.ElapsedSeconds ?? throw new InvalidOperationException("The run elapsed time is missing."));
        History.Add(run);
        Status = run.Completion switch
        {
            BenchmarkCompletion.Completed => "● Run complete",
            BenchmarkCompletion.Stopped => "● Stopped; issued operations drained and partial results retained.",
            _ => $"● Run failed: {error}"
        };
        if (error is not null)
        {
            PerformancePluginLog.RunFailed(m_log, error);
        }
        PerformancePluginLog.RunFinished(m_log, run.TotalOps, run.ErrorCount);
    }

    private void OnAggregationTick()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSec = (now - m_runStartTicks) / (double)Stopwatch.Frequency;
        if (elapsedSec < 0)
        {
            elapsedSec = 0;
        }

        long total = System.Threading.Interlocked.Read(ref m_totalOps);
        long delta = total - m_lastAggregationOps;
        m_lastAggregationOps = total;
        // Bucket per 250 ms — convert to ops/sec.
        double opsPerSec = delta * 4.0;
        m_throughputQueue.Enqueue((elapsedSec, opsPerSec));

        UpdateStatsTexts(elapsedSec);

        if (IsRunning)
        {
            double remaining = Math.Max(0, m_runDuration.TotalSeconds - elapsedSec);
            Status = string.Format(CultureInfo.InvariantCulture,
                "● Running {0:N0} ops/sec · {1:F1}s remaining", opsPerSec, remaining);
        }
    }

    private void UpdateStatsTexts(double elapsedSec)
    {
        long total = System.Threading.Interlocked.Read(ref m_totalOps);
        long errors = System.Threading.Interlocked.Read(ref m_errorOps);
        P50Ms = m_histogram.GetPercentile(0.50);
        P95Ms = m_histogram.GetPercentile(0.95);
        P99Ms = m_histogram.GetPercentile(0.99);
        TotalOpsText = total.ToString("N0", CultureInfo.InvariantCulture);
        MeanRateText = elapsedSec > 0
            ? (total / elapsedSec).ToString("N0", CultureInfo.InvariantCulture)
            : "0";
        P50Text = total > 0 ? P50Ms.ToString("F2", CultureInfo.InvariantCulture) : "—";
        P95Text = total > 0 ? P95Ms.ToString("F2", CultureInfo.InvariantCulture) : "—";
        P99Text = total > 0 ? P99Ms.ToString("F2", CultureInfo.InvariantCulture) : "—";
        MaxLatencyText = total > 0
            ? m_histogram.MaxMsObserved.ToString("F2", CultureInfo.InvariantCulture)
            : "—";
        ErrorsText = errors.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Dequeue a throughput sample for the view to plot.
    /// </summary>
    public bool TryDequeueThroughput(out double seconds, out double opsPerSec)
    {
        if (m_throughputQueue.TryDequeue(out var p))
        {
            seconds = p.seconds;
            opsPerSec = p.opsPerSec;
            return true;
        }
        seconds = 0;
        opsPerSec = 0;
        return false;
    }

    /// <summary>
    /// Snapshot the latency histogram for rendering.  Reuses an internal buffer.
    /// </summary>
    public ArrayOf<long> GetHistogramSnapshot()
    {
        m_histogram.Snapshot(m_histogramScratch);
        return new ArrayOf<long>(m_histogramScratch);
    }

    partial void OnUnboundedBurstChanged(bool value) => OnPropertyChanged(nameof(RateEditable));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(RateEditable));
    partial void OnTargetChanged(BenchmarkTarget? value)
    {
        OnPropertyChanged(nameof(TargetDescription));
        OnPropertyChanged(nameof(TargetNodeIdText));
    }

    public JsonElement CaptureState()
    {
        PerformanceStateDto dto = PerformanceState.CreateDto(
            Mode, TargetRate, UnboundedBurst, DurationSeconds, DurationUnit, Generator, CompareLast3, Target);
        return JsonSerializer.SerializeToElement(dto, PerformanceStateJsonContext.Default.PerformanceStateDto);
    }

    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!ConfigurationEditable)
            {
                throw new InvalidOperationException("Stop the workload before restoring configuration.");
            }
            PerformanceStateDto dto = state.Deserialize(PerformanceStateJsonContext.Default.PerformanceStateDto)
                ?? throw new JsonException("Performance configuration cannot be null.");
            PerformanceRestoredState restored = PerformanceState.Validate(dto);
            ApplyRestoredState(restored);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            Status = $"● Restore failed: {ex.Message}";
            PerformancePluginLog.RestoreFailed(m_log, ex);
            throw;
        }
        return Task.CompletedTask;
    }

    private void ApplyRestoredState(PerformanceRestoredState restored)
    {
        // Restore prepares the workload but never starts a run.
        Mode = restored.Mode;
        TargetRate = restored.TargetRate;
        UnboundedBurst = restored.UnboundedBurst;
        DurationSeconds = restored.DurationValue;
        DurationUnit = restored.DurationUnit;
        Generator = restored.Generator;
        CompareLast3 = restored.CompareLast3;
        Target = restored.Target;
        Status = Target is null
            ? "● Configuration restored - pick a target; explicit Run is required."
            : $"● Configuration restored: {Target.DisplayName}. Explicit Run is required.";
    }
}

internal static partial class PerformancePluginLog
{
    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceRunStarted,
        Level = LogLevel.Information,
        Message = "Performance run started - mode={Mode} rate={Rate} burst={Burst} " +
            "duration={DurationSeconds}s target={Target}.")]
    public static partial void RunStarted(
        ILogger logger, BenchmarkMode mode, double rate, bool burst, double durationSeconds, string target);

    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceRunFinished,
        Level = LogLevel.Information,
        Message = "Performance run finished — total={Total} errors={Errors}.")]
    public static partial void RunFinished(ILogger logger, long total, long errors);

    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceStopFailed,
        Level = LogLevel.Warning,
        Message = "Performance tab {Title} stop failed during dispose.")]
    public static partial void StopFailed(ILogger logger, string title, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceExportFailed,
        Level = LogLevel.Warning,
        Message = "Performance stats export failed.")]
    public static partial void ExportFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceSaveFailed,
        Level = LogLevel.Warning,
        Message = "Performance run history save failed.")]
    public static partial void SaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceLoadFailed,
        Level = LogLevel.Warning,
        Message = "Performance run history load failed.")]
    public static partial void LoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceRestoreFailed,
        Level = LogLevel.Warning,
        Message = "Performance could not restore its saved configuration.")]
    public static partial void RestoreFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.PerformanceRunFailed,
        Level = LogLevel.Warning,
        Message = "Performance run failed: {Reason}")]
    public static partial void RunFailed(ILogger logger, string reason);
}
