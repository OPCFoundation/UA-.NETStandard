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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.ViewModels;
using UaLens.Views;
using ClassicMonitoredItem = Opc.Ua.Client.MonitoredItem;
using ClassicMonitoredItemOptions = Opc.Ua.Client.MonitoredItemOptions;
using ClassicSubscription = Opc.Ua.Client.Subscription;
using ClassicSubscriptionOptions = Opc.Ua.Client.SubscriptionOptions;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// View model for a single Subscription Bench tab. Owns a per-tab
/// classic <see cref="ClassicSubscription"/>, a pool of pickable
/// Variable NodeIds and a 1 Hz aggregation timer that fans out
/// throughput / resource / engine metrics to <see cref="SubscriptionBenchView"/>.
/// The slider commits drive add/remove of <see cref="ClassicMonitoredItem"/>s
/// against the live subscription via a single
/// <see cref="ClassicSubscription.ApplyChangesAsync"/> per change.
/// </summary>
internal sealed partial class SubscriptionBenchPlugin : ObservableObject, IPlugin
{
    /// <summary>One-second resolution sliding history used to compute
    /// the 1s / 10s / 30s / 60s throughput averages.</summary>
    private const int BucketCount = 60;

    private static readonly Dictionary<PluginKind, int> s_perKindCounter = new();

    private readonly PluginHost m_host;
    private readonly ILogger m_log;

    /// <summary>Pool of variable NodeIds the slider draws from
    /// (round-robin, modulo Count). Mirrored by <see cref="m_poolNames"/>
    /// for display.</summary>
    private readonly List<NodeId> m_poolNodeIds = new();
    private readonly List<string> m_poolNames = new();
    private readonly object m_poolLock = new();

    /// <summary>Live monitored items in their order of insertion. The
    /// slider's "shrink" path tears items off the tail; the "grow" path
    /// appends from the pool round-robin so removed items rejoin in the
    /// same slot the next time the slider grows.</summary>
    private readonly List<ClassicMonitoredItem> m_liveItems = new();
    private readonly SemaphoreSlim m_resizeLock = new(1, 1);

    private readonly ConcurrentQueue<ChartSample> m_chartQueue = new();

    // CA2213: m_subscription is awaited+disposed in DisposeAsync but the
    // analyzer can't track lifecycle through Interlocked.Exchange.
#pragma warning disable CA2213
    private ClassicSubscription? m_subscription;
#pragma warning restore CA2213

    private DispatcherTimer? m_aggregationTimer;
    private long m_runStartTicks;
    private bool m_applyInFlight;

    /// <summary>Total values delivered since the last clear (incremented
    /// from <see cref="OnFastDataChange"/> on the publish callback thread).</summary>
    private long m_totalValues;

    /// <summary>Total bad-status events observed across all monitored
    /// items in the live subscription.</summary>
    private long m_totalErrors;

    /// <summary>1-second bucket counts (ring buffer indexed by
    /// <see cref="m_currentBucket"/>). Holds the last 60 seconds of
    /// per-second value counts so we can compute rolling averages.</summary>
    private readonly int[] m_bucketsPerSec = new int[BucketCount];
    private int m_currentBucket;

    private SubscriptionBenchView? m_view;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "● Not connected";

    [ObservableProperty]
    private string m_poolDescription = "0 variables in pool";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private int m_sliderMax = 1000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private int m_sliderValue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool m_isRunning;

    [ObservableProperty]
    private string m_stats1sText = "1s   :       0 val/s";

    [ObservableProperty]
    private string m_stats10sText = "10s  :       0 val/s";

    [ObservableProperty]
    private string m_stats30sText = "30s  :       0 val/s";

    [ObservableProperty]
    private string m_stats60sText = "60s  :       0 val/s";

    [ObservableProperty]
    private string m_totalValuesText = "Total values: 0";

    [ObservableProperty]
    private string m_totalErrorsText = "Errors      : 0";

    [ObservableProperty]
    private string m_publishIntervalText = "Publish int.: —";

    [ObservableProperty]
    private string m_engineMetricsText = "(no engine metrics — start the subscription to populate)";

    /// <summary>Flag the view consumes to clear its DataLoggers after a
    /// counter reset.  Set by <see cref="Clear"/>, cleared by the view.</summary>
    public bool WasReset { get; set; }

    /// <summary>Formatted slider readout — "<current> / <max>".</summary>
    public string SizeText => string.Format(CultureInfo.InvariantCulture,
        "{0:N0} / {1:N0}", SliderValue, SliderMax);

    public SubscriptionBenchPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n;
        lock (s_perKindCounter)
        {
            s_perKindCounter.TryGetValue(PluginKind.SubscriptionBench, out int prev);
            n = prev + 1;
            s_perKindCounter[PluginKind.SubscriptionBench] = n;
        }
        m_title = $"Subscription Bench {n}";
    }

    // ---- IPlugin members ----

    public PluginKind Kind => PluginKind.SubscriptionBench;

    Control? IPlugin.View => m_view ??= new SubscriptionBenchView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return new[]
        {
            CreateMenuItem("Pick _Variables…", PickVariablesCommand),
            CreateMenuItem("Pick Sub_tree…",   PickSubtreeCommand),
            CreateMenuItem("_Run",             RunCommand),
            CreateMenuItem("_Stop",            StopCommand),
            CreateMenuItem("_Clear counters",  ClearCommand)
        };
    }

    private static MenuItem CreateMenuItem(string header, System.Windows.Input.ICommand cmd)
    {
        return new MenuItem { Header = header, Command = cmd };
    }

    public void OnActivated() { }
    public void OnDeactivated() { }

    public async ValueTask DisposeAsync()
    {
        m_aggregationTimer?.Stop();
        ClassicSubscription? sub = Interlocked.Exchange(ref m_subscription, null);
        if (sub is not null)
        {
            try
            {
                await sub.DeleteAsync(silent: true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "Subscription Bench {Title} delete failed during dispose.", Title);
            }
            sub.Dispose();
        }
        m_resizeLock.Dispose();
    }

    // ---- Sample type for the view's chart pump ----

    public readonly record struct ChartSample(
        double Seconds,
        double ValuesPerSec1s,
        double ValuesPerSec10s,
        double ValuesPerSec30s,
        double ValuesPerSec60s,
        double Cpu,
        double MemMiB);

    /// <summary>Drain one chart sample for the view to plot.</summary>
    public bool TryDequeueChartSample(out ChartSample sample) => m_chartQueue.TryDequeue(out sample);

    // ---- Pool commands ----

    [RelayCommand]
    private async Task PickVariablesAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            Status = "● Not connected — connect first.";
            return;
        }

        Window? owner = TopLevelWindow();
        var dlg = new VariablePoolPickerDialog(
            session,
            ObjectIds.ObjectsFolder,
            "Browse the address space and tick the Variable nodes you want to add to the bench pool.");
        IReadOnlyList<(NodeId NodeId, string DisplayName)>? picked = owner is null
            ? await dlg.ShowDialog<IReadOnlyList<(NodeId, string)>?>(new Window()).ConfigureAwait(true)
            : await dlg.ShowDialog<IReadOnlyList<(NodeId, string)>?>(owner).ConfigureAwait(true);
        if (picked is null || picked.Count == 0)
        {
            Status = "● Pick cancelled — pool unchanged.";
            return;
        }

        AppendPool(picked);
    }

    [RelayCommand]
    private async Task PickSubtreeAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            Status = "● Not connected — connect first.";
            return;
        }

        Window? owner = TopLevelWindow();
        // Stage 1: let the user choose any starting node via the
        // existing BrowsePickerDialog (no NodeClass restriction so
        // they can pick Objects or any other container).
        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: session,
            Root: ObjectIds.ObjectsFolder,
            Title: "Pick subtree root for Subscription Bench pool",
            AcceptedClasses: NodeClass.Unspecified,
            Header: "Pick a starting node. Every Variable beneath it will be added to the bench pool."));
        NodeId? root = owner is null
            ? await picker.ShowDialog<NodeId?>(new Window()).ConfigureAwait(true)
            : await picker.ShowDialog<NodeId?>(owner).ConfigureAwait(true);
        if (!root.HasValue || root.Value.IsNull)
        {
            Status = "● Subtree pick cancelled — pool unchanged.";
            return;
        }

        // Stage 2: walk the subtree breadth-first collecting every
        // Variable.  Same BFS shape as FlattenedBrowseDialog but without
        // the modal results UI — we just need the flat list.
        Status = "● Walking subtree…";
        var collected = new List<(NodeId NodeId, string DisplayName)>();
        try
        {
            await WalkVariablesAsync(session, root.Value, collected, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Subtree walk failed: {ex.Message}";
            m_log.LogWarning(ex, "Subscription Bench subtree walk failed.");
            return;
        }

        if (collected.Count == 0)
        {
            Status = "● Subtree contains no Variable nodes.";
            return;
        }
        AppendPool(collected);
    }

    private static async Task WalkVariablesAsync(
        ManagedSession session,
        NodeId root,
        List<(NodeId NodeId, string DisplayName)> sink,
        CancellationToken ct)
    {
        const int maxDepth = 16;
        var queue = new Queue<(NodeId Node, string Path, int Depth)>();
        var seen = new HashSet<NodeId> { root };
        queue.Enqueue((root, string.Empty, 0));

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            (NodeId node, string parentPath, int depth) = queue.Dequeue();
            if (depth > maxDepth)
            {
                continue;
            }

            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = node,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse br;
            try
            {
                br = await session.BrowseAsync(null, null, 0, browse, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }
            if (br.Results.Count == 0 || StatusCode.IsBad(br.Results[0].StatusCode))
            {
                continue;
            }
            // Snapshot before any subsequent awaits because References
            // is a ref-struct enumerator.
            var refs = new List<ReferenceDescription>();
            foreach (ReferenceDescription r in br.Results[0].References)
            {
                refs.Add(r);
            }
            foreach (ReferenceDescription r in refs)
            {
                NodeId childId = ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
                if (childId.IsNull || !seen.Add(childId))
                {
                    continue;
                }
                string display = r.DisplayName.IsNull
                    ? r.BrowseName.Name ?? childId.ToString()
                    : r.DisplayName.Text ?? childId.ToString();
                string path = parentPath + "/" + display;
                queue.Enqueue((childId, path, depth + 1));
                if (r.NodeClass == NodeClass.Variable)
                {
                    sink.Add((childId, path));
                }
            }
        }
    }

    private void AppendPool(IReadOnlyList<(NodeId NodeId, string DisplayName)> additions)
    {
        int added = 0;
        lock (m_poolLock)
        {
            var existing = new HashSet<NodeId>(m_poolNodeIds);
            foreach ((NodeId nodeId, string name) in additions)
            {
                if (nodeId.IsNull || !existing.Add(nodeId))
                {
                    continue;
                }
                m_poolNodeIds.Add(nodeId);
                m_poolNames.Add(name);
                added++;
            }
        }
        RefreshPoolDescription();
        Status = string.Format(CultureInfo.InvariantCulture,
            "● Added {0} variable(s) to the pool (skipped duplicates).", added);
    }

    /// <summary>
    /// Seed the pool from the address-space context menu.  If <paramref name="nodeClass"/>
    /// is <see cref="NodeClass.Variable"/> the node itself is added; otherwise the subtree
    /// rooted at <paramref name="nodeId"/> is walked and every variable descendant is
    /// appended (mirrors the behaviour of the in-plugin "Pick Subtree…" command).
    /// </summary>
    /// <remarks>
    /// Never auto-starts the run — the user still controls Run/Stop via the toolbar so
    /// the pool can grow across several context-menu invocations before the bench
    /// starts streaming.
    /// </remarks>
    public async Task SeedFromNodeAsync(NodeId nodeId, NodeClass nodeClass, string? displayName)
    {
        if (nodeId.IsNull)
        {
            return;
        }
        string name = string.IsNullOrWhiteSpace(displayName) ? nodeId.ToString() : displayName!;
        if (nodeClass == NodeClass.Variable)
        {
            AppendPool(new[] { (nodeId, name) });
            return;
        }
        if (m_host.Connection.Session is not { } session)
        {
            Status = "● Connect to a server before seeding the pool.";
            return;
        }
        var collected = new List<(NodeId NodeId, string DisplayName)>();
        try
        {
            await WalkVariablesAsync(session, nodeId, collected, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = "● Subtree walk failed: " + ex.Message;
            return;
        }
        if (collected.Count == 0)
        {
            Status = "● No variables found beneath '" + name + "'.";
            return;
        }
        // Prepend the root name to each path so the user can tell which
        // seed produced which entry once several have been added.
        var prefixed = new List<(NodeId NodeId, string DisplayName)>(collected.Count);
        foreach ((NodeId childId, string childPath) in collected)
        {
            prefixed.Add((childId, name + childPath));
        }
        AppendPool(prefixed);
    }

    [RelayCommand]
    private void ClearPool()
    {
        lock (m_poolLock)
        {
            m_poolNodeIds.Clear();
            m_poolNames.Clear();
        }
        RefreshPoolDescription();
        Status = "● Pool cleared.";
    }

    private void RefreshPoolDescription()
    {
        int count;
        lock (m_poolLock)
        {
            count = m_poolNodeIds.Count;
        }
        PoolDescription = count == 0
            ? "0 variables in pool"
            : string.Format(CultureInfo.InvariantCulture,
                "{0:N0} variable(s) in pool", count);
    }

    // ---- Subscription lifecycle ----

    private bool CanRun() =>
        !IsRunning && m_host.Connection.Session is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            Status = "● Not connected — connect first.";
            return;
        }
        int poolCount;
        lock (m_poolLock)
        {
            poolCount = m_poolNodeIds.Count;
        }
        if (poolCount == 0)
        {
            Status = "● Pool is empty — pick variables before running.";
            return;
        }

        try
        {
            var sub = new ClassicSubscription(session.MessageContext.Telemetry, new ClassicSubscriptionOptions
            {
                DisplayName = $"UaLens.Bench/{Title}",
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 1000,
                MaxNotificationsPerPublish = 0,
                Priority = 0,
                PublishingEnabled = true,
                MinLifetimeInterval = 60_000
            })
            {
                FastDataChangeCallback = OnFastDataChange,
                FastKeepAliveCallback = OnFastKeepAlive
            };
            if (!session.AddSubscription(sub))
            {
                m_log.LogWarning("Subscription Bench: AddSubscription returned false.");
                sub.Dispose();
                Status = "● Failed to register subscription.";
                return;
            }
            await sub.CreateAsync(CancellationToken.None).ConfigureAwait(true);
            m_subscription = sub;

            // Read the server's max-items-per-subscription ceiling
            // (lives on ServerCapabilities, not OperationLimits).  Fall
            // back to a sensible default when the server doesn't
            // advertise one.
            uint serverMax = session.ServerCapabilities?.MaxMonitoredItemsPerSubscription ?? 0;
            SliderMax = serverMax > 0 && serverMax < int.MaxValue
                ? (int)serverMax
                : Math.Max(1000, poolCount);
            SliderValue = 0;

            Clear();
            IsRunning = true;
            m_runStartTicks = Stopwatch.GetTimestamp();
            StartAggregationTimer();

            Status = string.Format(CultureInfo.InvariantCulture,
                "● Subscription created · 0 / {0} items · 0 val/s", SliderMax);
            m_log.LogInformation(
                "Subscription Bench run started: id={Id}, max={Max}, publishInterval={Pi}ms",
                sub.Id, SliderMax, sub.CurrentPublishingInterval);
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Subscription Bench run failed.");
            Status = $"● Run failed: {ex.Message}";
        }
    }

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        m_aggregationTimer?.Stop();
        ClassicSubscription? sub = Interlocked.Exchange(ref m_subscription, null);
        if (sub is not null)
        {
            try
            {
                await sub.DeleteAsync(silent: true, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "Subscription Bench stop: DeleteAsync failed.");
            }
            sub.Dispose();
        }
        lock (m_liveItems)
        {
            m_liveItems.Clear();
        }
        IsRunning = false;
        SliderValue = 0;
        SliderMax = 1000;
        Status = "● Stopped.";
        m_log.LogInformation("Subscription Bench {Title} stopped.", Title);
    }

    [RelayCommand]
    private void Clear()
    {
        Interlocked.Exchange(ref m_totalValues, 0);
        Interlocked.Exchange(ref m_totalErrors, 0);
        for (int i = 0; i < m_bucketsPerSec.Length; i++)
        {
            m_bucketsPerSec[i] = 0;
        }
        m_currentBucket = 0;
        m_runStartTicks = Stopwatch.GetTimestamp();
        while (m_chartQueue.TryDequeue(out _))
        {
            // drain
        }
        WasReset = true;
        Stats1sText = "1s   :       0 val/s";
        Stats10sText = "10s  :       0 val/s";
        Stats30sText = "30s  :       0 val/s";
        Stats60sText = "60s  :       0 val/s";
        TotalValuesText = "Total values: 0";
        TotalErrorsText = "Errors      : 0";
    }

    // ---- Slider resize ----

    partial void OnSliderValueChanged(int value)
    {
        if (!IsRunning || m_subscription is null)
        {
            return;
        }
        if (m_applyInFlight)
        {
            return;
        }
        // Schedule the resize on the dispatcher so consecutive slider
        // commits (drag) coalesce: by the time the task runs we read
        // SliderValue again.
        _ = ResizeAsync(value);
    }

    private async Task ResizeAsync(int targetCount)
    {
        m_applyInFlight = true;
        await m_resizeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ClassicSubscription? sub = m_subscription;
            if (sub is null)
            {
                return;
            }
            int target = Math.Clamp(SliderValue, 0, SliderMax);
            List<NodeId> poolSnapshot;
            List<string> poolNames;
            lock (m_poolLock)
            {
                if (m_poolNodeIds.Count == 0)
                {
                    return;
                }
                poolSnapshot = new List<NodeId>(m_poolNodeIds);
                poolNames = new List<string>(m_poolNames);
            }

            int currentCount;
            lock (m_liveItems)
            {
                currentCount = m_liveItems.Count;
            }
            if (target == currentCount)
            {
                return;
            }

            if (target > currentCount)
            {
                int toAdd = target - currentCount;
                var toAddList = new List<ClassicMonitoredItem>(toAdd);
                for (int i = 0; i < toAdd; i++)
                {
                    int slot = currentCount + i;
                    int poolIdx = slot % poolSnapshot.Count;
                    var mi = new ClassicMonitoredItem(
                        sub.Session?.MessageContext.Telemetry ?? AmbientMessageContext.Telemetry!,
                        new ClassicMonitoredItemOptions
                        {
                            DisplayName = $"bench[{slot}]/{poolNames[poolIdx]}",
                            StartNodeId = poolSnapshot[poolIdx],
                            AttributeId = Attributes.Value,
                            MonitoringMode = MonitoringMode.Reporting,
                            SamplingInterval = 0,
                            QueueSize = 1,
                            DiscardOldest = true
                        });
                    toAddList.Add(mi);
                }
                sub.AddItems(toAddList);
                lock (m_liveItems)
                {
                    m_liveItems.AddRange(toAddList);
                }
            }
            else
            {
                int toRemove = currentCount - target;
                var toRemoveList = new List<ClassicMonitoredItem>(toRemove);
                lock (m_liveItems)
                {
                    int startIdx = m_liveItems.Count - toRemove;
                    for (int i = startIdx; i < m_liveItems.Count; i++)
                    {
                        toRemoveList.Add(m_liveItems[i]);
                    }
                    m_liveItems.RemoveRange(startIdx, toRemove);
                }
                sub.RemoveItems(toRemoveList);
            }

            await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // Recount bad-status items after the round-trip.
            long bad = 0;
            foreach (ClassicMonitoredItem mi in sub.MonitoredItems)
            {
                ServiceResult? err = mi.Status?.Error;
                if (err is not null && ServiceResult.IsBad(err))
                {
                    bad++;
                }
            }
            Interlocked.Exchange(ref m_totalErrors, bad);
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Subscription Bench resize to {Target} failed.", targetCount);
            Status = $"● Resize failed: {ex.Message}";
        }
        finally
        {
            m_resizeLock.Release();
            m_applyInFlight = false;
        }
    }

    // ---- Aggregation ----

    private void StartAggregationTimer()
    {
        m_aggregationTimer?.Stop();
        m_aggregationTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => OnAggregationTick());
        m_aggregationTimer.Start();
    }

    private void OnAggregationTick()
    {
        // Rotate the ring buffer: advance to the next bucket and zero it
        // so subsequent callbacks land in a fresh slot.
        int prev = m_currentBucket;
        int next = (prev + 1) % BucketCount;
        m_bucketsPerSec[next] = 0;
        m_currentBucket = next;

        int last1 = m_bucketsPerSec[prev];
        double avg10 = AverageOver(prev, 10);
        double avg30 = AverageOver(prev, 30);
        double avg60 = AverageOver(prev, 60);

        long total = Interlocked.Read(ref m_totalValues);
        long errors = Interlocked.Read(ref m_totalErrors);

        Stats1sText = string.Format(CultureInfo.InvariantCulture, "1s   : {0,7:N0} val/s", last1);
        Stats10sText = string.Format(CultureInfo.InvariantCulture, "10s  : {0,7:N1} val/s", avg10);
        Stats30sText = string.Format(CultureInfo.InvariantCulture, "30s  : {0,7:N1} val/s", avg30);
        Stats60sText = string.Format(CultureInfo.InvariantCulture, "60s  : {0,7:N1} val/s", avg60);

        TotalValuesText = string.Format(CultureInfo.InvariantCulture,
            "Total values: {0:N0}", total);
        TotalErrorsText = string.Format(CultureInfo.InvariantCulture,
            "Errors      : {0:N0}", errors);
        if (m_subscription is { } sub)
        {
            PublishIntervalText = string.Format(CultureInfo.InvariantCulture,
                "Publish int.: {0:N0} ms (revised)", sub.CurrentPublishingInterval);
        }

        (double cpu, double mem) = m_host.Main.ResourceMonitor?.SampleNumeric() ?? (double.NaN, 0);

        double secondsSinceStart = (Stopwatch.GetTimestamp() - m_runStartTicks)
            / (double)Stopwatch.Frequency;
        m_chartQueue.Enqueue(new ChartSample(
            secondsSinceStart, last1, avg10, avg30, avg60, cpu, mem));

        // Status line + engine metrics.
        int liveCount;
        lock (m_liveItems)
        {
            liveCount = m_liveItems.Count;
        }
        Status = string.Format(CultureInfo.InvariantCulture,
            "● {0:N0} items / {1:N0} max · {2:N0} val/s",
            liveCount, SliderMax, last1);

        EngineMetricsText = BuildEngineMetricsText(total);
    }

    private double AverageOver(int latestBucket, int window)
    {
        long sum = 0;
        for (int i = 0; i < window && i < BucketCount; i++)
        {
            int idx = latestBucket - i;
            if (idx < 0)
            {
                idx += BucketCount;
            }
            sum += m_bucketsPerSec[idx];
        }
        return sum / (double)window;
    }

    private string BuildEngineMetricsText(long total)
    {
        var sb = new StringBuilder();
        if (m_subscription is { } sub)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"Subscription id   : {sub.Id}\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"Keep-alive count  : {sub.CurrentKeepAliveCount}\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"Lifetime count    : {sub.CurrentLifetimeCount}\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"Sequence number   : {sub.SequenceNumber}\n");
        }
        if (m_host.Connection.Session is { } session)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"Session good pubs : {session.GoodPublishRequestCount}\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"Session min/max   : {session.MinPublishRequestCount} / {session.MaxPublishRequestCount}\n");

            // V2 SubscriptionManager exposes worker-pool / republish
            // counters. The classic engine throws here and we fall back
            // to a TODO note. See ManagedSession.SubscriptionManager.
            try
            {
                Opc.Ua.Client.Subscriptions.ISubscriptionManager mgr = session.SubscriptionManager;
                sb.Append(CultureInfo.InvariantCulture,
                    $"Mgr subscriptions : {mgr.Count}\n");
                sb.Append(CultureInfo.InvariantCulture,
                    $"Workers (cur)     : {mgr.PublishWorkerCount}\n");
                sb.Append(CultureInfo.InvariantCulture,
                    $"Workers (min/max) : {mgr.MinPublishWorkerCount} / {mgr.MaxPublishWorkerCount}\n");
                sb.Append(CultureInfo.InvariantCulture,
                    $"Good / bad pubs   : {mgr.GoodPublishRequestCount} / {mgr.BadPublishRequestCount}\n");
                sb.Append(CultureInfo.InvariantCulture,
                    $"Missing / republ. : {mgr.MissingMessageCount} / {mgr.RepublishMessageCount}\n");
            }
            catch (InvalidOperationException)
            {
                // TODO: surface ClassicSubscriptionEngine metrics once
                // the engine exposes a public accessor parallel to
                // ManagedSession.SubscriptionManager.
                sb.Append("(engine: classic — extra metrics unavailable)\n");
            }
        }
        sb.Append(CultureInfo.InvariantCulture,
            $"Cumulative values : {total:N0}");
        return sb.ToString();
    }

    // ---- Subscription callbacks ----

    private void OnFastDataChange(
        ClassicSubscription subscription,
        DataChangeNotification notification,
        ArrayOf<string> stringTable)
    {
        int n = notification.MonitoredItems.Count;
        if (n <= 0)
        {
            return;
        }
        Interlocked.Add(ref m_totalValues, n);
        // Read the bucket index atomically — the timer thread only
        // advances it; we tolerate a single-bucket drift at the rollover
        // boundary because the contributions to total throughput are
        // identical either way.
        int idx = Volatile.Read(ref m_currentBucket);
        Interlocked.Add(ref m_bucketsPerSec[idx], n);

        // Capture status errors observed in this notification batch.
        long bad = 0;
        for (int i = 0; i < n; i++)
        {
            MonitoredItemNotification mi = notification.MonitoredItems[i];
            if (mi.Value is not null && StatusCode.IsBad(mi.Value.StatusCode))
            {
                bad++;
            }
        }
        if (bad > 0)
        {
            Interlocked.Add(ref m_totalErrors, bad);
        }
    }

    private void OnFastKeepAlive(
        ClassicSubscription subscription,
        NotificationData notification)
    {
        // No-op: keep-alives only keep the publish pipeline warm.
        // The classic engine emits these when no DataChange or Event
        // notifications are pending so the publish round-trip stays
        // live; bench throughput maths intentionally ignores them.
    }

    private Window? TopLevelWindow()
    {
        if (m_view is null)
        {
            return null;
        }
        return TopLevel.GetTopLevel(m_view) as Window;
    }
}
