/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
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
internal sealed partial class PerformancePlugin : ObservableObject, IPlugin
{
    private static int s_nextNumber;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private readonly LatencyHistogram m_histogram = new();
    private readonly long[] m_histogramScratch = new long[LatencyHistogram.BucketCount];
    private readonly ConcurrentQueue<(double seconds, double opsPerSec)> m_throughputQueue = new();
    // CA2213: m_runner.StopAsync() in DisposeAsync awaits the runner's task
    // loop to drain; runner's own DisposeAsync (via IAsyncDisposable) is
    // semantically equivalent, but the analyzer can't see the lifecycle.
#pragma warning disable CA2213
    private BenchmarkRunner? m_runner;
#pragma warning restore CA2213
    private PerformanceView? m_view;

    private DispatcherTimer? m_aggregationTimer;
    private long m_lastAggregationOps;
    private long m_runStartTicks;
    private TimeSpan m_runDuration;

    /// <summary>Total ops completed in the current run.</summary>
    private long m_totalOps;

    /// <summary>Errors observed in the current run.</summary>
    private long m_errorOps;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "● Idle";

    // ---- Workload config ----

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
    private int m_durationSeconds = 10;

    /// <summary>
    /// Unit applied to <see cref="DurationSeconds"/> to compute the
    /// effective run duration.  Choices: Seconds / Minutes / Hours.
    /// Defaults to Seconds to preserve prior behavior.
    /// </summary>
    [ObservableProperty]
    private DurationUnit m_durationUnit = DurationUnit.Seconds;

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
    private bool isRunning;

    // ---- Aggregated stats ----

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

    /// <summary>Current p50 latency in milliseconds — read by the view to drive the marker line.</summary>
    public double P50Ms { get; private set; }

    /// <summary>Current p95 latency in milliseconds — read by the view to drive the marker line.</summary>
    public double P95Ms { get; private set; }

    /// <summary>Current p99 latency in milliseconds — read by the view to drive the marker line.</summary>
    public double P99Ms { get; private set; }

    /// <summary>Total histogram samples — used by the view to skip Y autoscale when empty.</summary>
    public long HistogramTotal => m_histogram.Count;

    /// <summary>Flag the view consumes to clear its DataLogger after a Reset.</summary>
    public bool WasReset { get; set; }

    /// <summary>Static items for the Mode dropdown (compiled bindings).</summary>
    public IReadOnlyList<BenchmarkMode> ModeOptions { get; } =
        new[] { BenchmarkMode.Write, BenchmarkMode.Call };

    /// <summary>Static items for the Value-generator dropdown.</summary>
    public IReadOnlyList<ValueGenerator> GeneratorOptions { get; } =
        new[] { ValueGenerator.Random, ValueGenerator.Sequential, ValueGenerator.Fixed };

    public PerformancePlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n = System.Threading.Interlocked.Increment(ref s_nextNumber);
        m_title = $"Performance {n}";
    }

    // ---- IPlugin members ----

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
            CreateMenuItem("_Export Stats CSV",   ExportStatsCommand)
        };
    }

    private static MenuItem CreateMenuItem(string header, System.Windows.Input.ICommand cmd)
    {
        var item = new MenuItem { Header = header, Command = cmd };
        return item;
    }

    public void OnActivated() { }
    public void OnDeactivated() { }

    public async ValueTask DisposeAsync()
    {
        if (m_runner is { } r)
        {
            try
            {
                await r.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "Performance tab {Title} stop failed during dispose.", Title);
            }
        }
        m_aggregationTimer?.Stop();
    }

    // ---- Bindings-only derived properties ----

    /// <summary>Human-readable description of the configured Target (or "(no Target)").</summary>
    public string TargetDescription => Target is null
        ? "(no Target — pick a Variable or Method first)"
        : Target.DisplayName;

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

    // ---- Commands ----

    [RelayCommand]
    private async Task PickTargetAsync()
    {
        if (m_host.Main.Connection.Session is not { } session)
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
        NodeViewModel? sel = m_host.Main.SelectedNode;
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
            NodeId? pickedId = owner is null
                ? await picker.ShowDialog<NodeId?>(new Window()).ConfigureAwait(true)
                : await picker.ShowDialog<NodeId?>(owner).ConfigureAwait(true);
            if (!pickedId.HasValue || pickedId.Value.IsNull)
            {
                Status = "● Pick target cancelled.";
                return;
            }
            // Build a synthetic NodeViewModel hint that
            // PerformanceTargetDialog can read.
            hint = new NodeViewModel(
                m_host.Main.Browser,
                NodeId.Null,
                pickedId.Value,
                picker.PickedDisplay,
                picker.PickedNodeClass);
        }

        var dialog = new PerformanceTargetDialog(m_host.Main, session, hint);
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
        !IsRunning
        && Target is not null
        && m_host.Main.Connection.Session is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (m_host.Main.Connection.Session is not { } session || Target is null)
        {
            return;
        }
        // Verify mode matches the Target.
        if (Target.Mode != Mode)
        {
            Status = $"● Target is a {Target.Mode} Target — switch the Mode combo to match.";
            return;
        }

        Reset();
        m_runStartTicks = Stopwatch.GetTimestamp();
        m_runDuration = EffectiveDuration;
        m_runner = new BenchmarkRunner(
            session,
            Target,
            Generator,
            TargetRate,
            UnboundedBurst,
            m_runDuration);
        m_runner.OnSample += HandleSample;
        m_runner.OnFinished += HandleFinished;
        m_runner.Start();

        IsRunning = true;
        Status = "● Running…";

        m_aggregationTimer?.Stop();
        m_aggregationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => OnAggregationTick());
        m_aggregationTimer.Start();
        m_log.LogInformation(
            "Performance run started — mode={Mode} rate={Rate} burst={Burst} duration={Duration}s Target={Target}",
            Mode, TargetRate, UnboundedBurst, DurationSeconds, Target.DisplayName);

        await Task.CompletedTask.ConfigureAwait(true);
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
        IsRunning = false;
        m_aggregationTimer?.Stop();
        Status = "● Stopped";
    }

    [RelayCommand]
    private void Reset()
    {
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
        catch (Exception ex)
        {
            Status = $"● Export failed: {ex.Message}";
            m_log.LogWarning(ex, "Performance stats export failed.");
        }
    }

    // ---- Runner callbacks ----

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
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            m_aggregationTimer?.Stop();
            OnAggregationTick(); // final refresh
            Status = error is null ? "● Run complete" : $"● Run failed: {error}";
            m_log.LogInformation("Performance run finished — total={Total} errors={Errors}",
                System.Threading.Interlocked.Read(ref m_totalOps),
                System.Threading.Interlocked.Read(ref m_errorOps));
        });
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
        MeanRateText = elapsedSec > 0.1
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

    /// <summary>Dequeue a throughput sample for the view to plot.</summary>
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

    /// <summary>Snapshot the latency histogram for rendering.  Reuses an internal buffer.</summary>
    public long[] GetHistogramSnapshot()
    {
        m_histogram.Snapshot(m_histogramScratch);
        return m_histogramScratch;
    }

    partial void OnUnboundedBurstChanged(bool value) => OnPropertyChanged(nameof(RateEditable));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(RateEditable));
    partial void OnTargetChanged(BenchmarkTarget? value) => OnPropertyChanged(nameof(TargetDescription));
}
