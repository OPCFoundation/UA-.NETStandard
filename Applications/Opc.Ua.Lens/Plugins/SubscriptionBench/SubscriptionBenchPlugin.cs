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

    /// <summary>
    /// Live subscriptions, in creation order.  Index <c>i</c> here
    /// corresponds to index <c>i</c> in <see cref="m_liveItems"/> so the
    /// per-sub item lists track their owning subscription without
    /// requiring a dictionary lookup on every resize.
    /// </summary>
    private readonly List<ClassicSubscription> m_subscriptions = new();

    /// <summary>Per-subscription monitored items, in their order of
    /// insertion.  The "shrink" path tears items off the tail; the
    /// "grow" path appends from the pool round-robin so removed items
    /// rejoin in the same slot the next time the slider grows.</summary>
    private readonly List<List<ClassicMonitoredItem>> m_liveItems = new();

    /// <summary>Serialises every <see cref="ConvergeAsync"/> call so
    /// concurrent slider moves (drag) coalesce into one converge wave
    /// at a time; the converge body always re-reads
    /// <see cref="SubsSliderValue"/> + <see cref="ItemsSliderValue"/>
    /// inside the lock so the *latest* targets are always honoured.</summary>
    private readonly SemaphoreSlim m_resizeLock = new(1, 1);

    private readonly ConcurrentQueue<ChartSample> m_chartQueue = new();

    private DispatcherTimer? m_aggregationTimer;
    private long m_runStartTicks;
    private bool m_serverLimitsRefreshed;

    /// <summary>Current subscription parameters applied to every live
    /// sub and used as the template for any sub created by a subsequent
    /// <see cref="ConvergeAsync"/> grow.  Edited via
    /// <see cref="EditSubscriptionAsync"/>.</summary>
    private UaLens.Subscriptions.SubscriptionConfig m_subConfig = new()
    {
        PublishingInterval = TimeSpan.FromMilliseconds(1000),
        KeepAliveCount = 10,
        LifetimeCount = 1000,
        MaxNotificationsPerPublish = 0,
        Priority = 0,
        PublishingEnabled = true
    };

    /// <summary>Current monitored-item defaults applied to every live
    /// item and used as the template for any item created by a
    /// subsequent <see cref="ConvergeAsync"/> grow.  Edited via
    /// <see cref="EditItemSettingsAsync"/>.</summary>
    private UaLens.Views.MonitoredItemSettings m_itemSettings = new();

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
    [NotifyPropertyChangedFor(nameof(CanResize))]
    private string m_poolDescription = "0 variables in pool";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemsSizeText))]
    private int m_itemsSliderMax = 1000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemsSizeText))]
    private int m_itemsSliderValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubsSizeText))]
    private int m_subsSliderMax = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubsSizeText))]
    private int m_subsSliderValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResize))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditItemSettingsCommand))]
    private bool m_isConnected;

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
    private string m_engineMetricsText = "(no engine metrics — connect and move the sliders to populate)";

    /// <summary>Flag the view consumes to clear its DataLoggers after a
    /// counter reset.  Set by <see cref="Clear"/>, cleared by the view.</summary>
    public bool WasReset { get; set; }

    /// <summary>Formatted items-slider readout — "<current> / <max>".</summary>
    public string ItemsSizeText => string.Format(CultureInfo.InvariantCulture,
        "{0:N0} / {1:N0}", ItemsSliderValue, ItemsSliderMax);

    /// <summary>Formatted subscriptions-slider readout — "<current> / <max>".</summary>
    public string SubsSizeText => string.Format(CultureInfo.InvariantCulture,
        "{0:N0} / {1:N0}", SubsSliderValue, SubsSliderMax);

    /// <summary>True when the user can move the sliders to (re)shape
    /// the bench — connected to a server and at least one variable
    /// available in the pool to draw from.</summary>
    public bool CanResize =>
        IsConnected && m_poolNodeIds.Count > 0;

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
            CreateMenuItem("_Subscription…",   EditSubscriptionCommand),
            CreateMenuItem("_Item settings…",  EditItemSettingsCommand),
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

    /// <summary>
    /// Mirror the host's connection state onto our own
    /// <see cref="IsConnected"/> flag (drives <see cref="CanResize"/>
    /// and the Pick / Stop CanExecute), refresh server capability
    /// limits on the first connect of each session, and start / stop
    /// the 1 Hz aggregation timer so the chart shows a live "0 val/s"
    /// line before the user touches a slider.  On disconnect the live
    /// subscriptions are torn down because their server-side handles
    /// die with the session.
    /// </summary>
    public void OnConnectionStateChanged()
    {
        bool connected = m_host.Connection.Session is not null;
        IsConnected = connected;
        PickVariablesCommand.NotifyCanExecuteChanged();
        PickSubtreeCommand.NotifyCanExecuteChanged();

        if (connected)
        {
            if (!m_serverLimitsRefreshed)
            {
                RefreshServerLimits();
                m_serverLimitsRefreshed = true;
            }
            StartAggregationTimer();
        }
        else
        {
            m_serverLimitsRefreshed = false;
            m_aggregationTimer?.Stop();
            // Drop every sub from our local state; the server-side
            // handles are gone with the session.  Zero the sliders too
            // so the user has a clean slate when they reconnect.
            lock (m_liveItems)
            {
                m_subscriptions.Clear();
                m_liveItems.Clear();
            }
            SubsSliderValue = 0;
            ItemsSliderValue = 0;
            Status = "● Disconnected — connect to resume.";
        }
    }

    private void RefreshServerLimits()
    {
        if (m_host.Connection.Session is not { } session)
        {
            return;
        }
        ServerCapabilities? caps = session.ServerCapabilities;
        uint mi = caps?.MaxMonitoredItemsPerSubscription ?? 0;
        uint subs = caps?.MaxSubscriptionsPerSession ?? 0;
        // Clamp to int.MaxValue (slider type) and fall back to
        // sensible defaults when the server reports 0 / max-uint.
        ItemsSliderMax = mi > 0 && mi < int.MaxValue ? (int)mi : 1000;
        SubsSliderMax = subs > 0 && subs < int.MaxValue ? (int)subs : 100;
    }

    public async ValueTask DisposeAsync()
    {
        m_aggregationTimer?.Stop();
        List<ClassicSubscription> snapshot;
        lock (m_liveItems)
        {
            snapshot = new List<ClassicSubscription>(m_subscriptions);
            m_subscriptions.Clear();
            m_liveItems.Clear();
        }
        foreach (ClassicSubscription sub in snapshot)
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
        OnPropertyChanged(nameof(CanResize));
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
        OnPropertyChanged(nameof(CanResize));
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

    /// <summary>
    /// Emergency reset: zero both sliders so the next
    /// <see cref="ConvergeAsync"/> tears every subscription down (and
    /// frees server-side state), then clear the local counters so the
    /// chart restarts from zero.  Always available while connected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task StopAsync()
    {
        SubsSliderValue = 0;
        ItemsSliderValue = 0;
        await ConvergeAsync().ConfigureAwait(true);
        Clear();
        Status = "● Stopped — sliders reset.";
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

    // ---- Settings dialogs ----

    /// <summary>
    /// Open the (same as Subscription tab) settings dialog with the
    /// current <see cref="m_subConfig"/>; on OK, save the new config and
    /// push it to every live subscription via <c>ModifyAsync</c>.
    /// Subsequent grows pick the new config up automatically.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task EditSubscriptionAsync()
    {
        Window? owner = TopLevelWindow();
        bool engineHasWorkerPool =
            m_host.Connection.Engine == UaLens.Connection.SubscriptionEngineKind.ChannelV2;
        var dlg = new UaLens.Views.SubscriptionSettingsDialog(m_subConfig, engineHasWorkerPool);
        UaLens.Subscriptions.SubscriptionConfig? result = owner is null
            ? await dlg.ShowDialog<UaLens.Subscriptions.SubscriptionConfig?>(new Window()).ConfigureAwait(true)
            : await dlg.ShowDialog<UaLens.Subscriptions.SubscriptionConfig?>(owner).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        m_subConfig = result;

        // Push to every live sub.  Share the converge lock so we don't
        // race with a slider-driven grow / shrink.
        await m_resizeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<ClassicSubscription> snapshot;
            lock (m_liveItems)
            {
                snapshot = new List<ClassicSubscription>(m_subscriptions);
            }
            foreach (ClassicSubscription sub in snapshot)
            {
                sub.PublishingInterval = (int)m_subConfig.PublishingInterval.TotalMilliseconds;
                sub.KeepAliveCount = m_subConfig.KeepAliveCount;
                sub.LifetimeCount = m_subConfig.LifetimeCount;
                sub.MaxNotificationsPerPublish = m_subConfig.MaxNotificationsPerPublish;
                sub.Priority = m_subConfig.Priority;
                sub.PublishingEnabled = m_subConfig.PublishingEnabled;
                try
                {
                    await sub.ModifyAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_log.LogWarning(ex, "Subscription Bench: ModifyAsync failed for sub {Id}.", sub.Id);
                }
            }
            Status = string.Format(CultureInfo.InvariantCulture,
                "● Subscription parameters applied to {0} sub(s).", snapshot.Count);
        }
        finally
        {
            m_resizeLock.Release();
        }
    }

    /// <summary>
    /// Open the generic monitored-item settings dialog with the current
    /// <see cref="m_itemSettings"/>; on OK, save and apply across every
    /// live item in every sub.  Monitoring mode (if changed) is pushed
    /// via the dedicated <c>SetMonitoringModeAsync</c>; the rest piggy-
    /// back on the existing <c>ApplyChangesAsync</c> per sub which
    /// turns into ModifyMonitoredItems on the wire.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task EditItemSettingsAsync()
    {
        Window? owner = TopLevelWindow();
        var dlg = new UaLens.Views.MonitoredItemSettingsDialog(m_itemSettings);
        UaLens.Views.MonitoredItemSettings? result = owner is null
            ? await dlg.ShowDialog<UaLens.Views.MonitoredItemSettings?>(new Window()).ConfigureAwait(true)
            : await dlg.ShowDialog<UaLens.Views.MonitoredItemSettings?>(owner).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        MonitoringMode oldMode = m_itemSettings.MonitoringMode;
        m_itemSettings = result;

        await m_resizeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<ClassicSubscription> subs;
            List<List<ClassicMonitoredItem>> itemsByIndex;
            lock (m_liveItems)
            {
                subs = new List<ClassicSubscription>(m_subscriptions);
                itemsByIndex = new List<List<ClassicMonitoredItem>>(m_liveItems);
            }
            int totalItems = 0;
            for (int subIdx = 0; subIdx < subs.Count; subIdx++)
            {
                ClassicSubscription sub = subs[subIdx];
                List<ClassicMonitoredItem> items = itemsByIndex[subIdx];
                if (items.Count == 0)
                {
                    continue;
                }
                int samplingMs = (int)m_itemSettings.SamplingInterval.TotalMilliseconds;
                foreach (ClassicMonitoredItem mi in items)
                {
                    mi.SamplingInterval = samplingMs;
                    mi.QueueSize = m_itemSettings.QueueSize;
                    mi.DiscardOldest = m_itemSettings.DiscardOldest;
                    mi.Filter = m_itemSettings.DataChangeFilter;
                }
                try
                {
                    await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_log.LogWarning(ex, "Subscription Bench: ApplyChangesAsync failed for sub {Id}.", sub.Id);
                }
                if (m_itemSettings.MonitoringMode != oldMode)
                {
                    try
                    {
                        await sub.SetMonitoringModeAsync(m_itemSettings.MonitoringMode, items, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_log.LogWarning(ex, "Subscription Bench: SetMonitoringModeAsync failed for sub {Id}.", sub.Id);
                    }
                }
                totalItems += items.Count;
            }
            Status = string.Format(CultureInfo.InvariantCulture,
                "● Item settings applied to {0} item(s) across {1} sub(s).",
                totalItems, subs.Count);
        }
        finally
        {
            m_resizeLock.Release();
        }
    }

    // ---- Slider-driven converge ----

    partial void OnItemsSliderValueChanged(int value)
    {
        _ = ConvergeAsync();
    }

    partial void OnSubsSliderValueChanged(int value)
    {
        _ = ConvergeAsync();
    }

    /// <summary>
    /// Idempotent converge: brings the live topology
    /// (<see cref="m_subscriptions"/>, <see cref="m_liveItems"/>) in
    /// line with the current slider targets.  Serialised on
    /// <see cref="m_resizeLock"/> so concurrent slider moves coalesce;
    /// reads the targets *inside* the lock so the latest values are
    /// always honoured (fixes the previous m_applyInFlight-swallow that
    /// could drop mid-drag updates).
    /// </summary>
    private async Task ConvergeAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            return;
        }
        await m_resizeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Re-read targets inside the lock so we always converge to
            // the latest slider values, even if several changes coalesced
            // while a previous converge was in flight.
            int targetSubs = Math.Clamp(SubsSliderValue, 0, SubsSliderMax);
            int targetItems = Math.Clamp(ItemsSliderValue, 0, ItemsSliderMax);

            List<NodeId> poolSnapshot;
            List<string> poolNames;
            lock (m_poolLock)
            {
                poolSnapshot = new List<NodeId>(m_poolNodeIds);
                poolNames = new List<string>(m_poolNames);
            }
            // Pool may legitimately be empty (user cleared it).  Without
            // items we can still tear subs down to zero; we just can't
            // grow.  Cap the target item count at zero in that case so
            // the rest of the body becomes a no-op grow / a clean shrink.
            if (poolSnapshot.Count == 0)
            {
                targetItems = 0;
            }

            await ConvergeSubscriptionsAsync(session, targetSubs).ConfigureAwait(false);
            await ConvergeItemsAsync(targetItems, poolSnapshot, poolNames).ConfigureAwait(false);
            RecountErrors();
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Subscription Bench converge failed.");
            Status = $"● Converge failed: {ex.Message}";
        }
        finally
        {
            m_resizeLock.Release();
        }
    }

    /// <summary>
    /// Bring <see cref="m_subscriptions"/>.Count to
    /// <paramref name="targetSubs"/> by creating missing subs (on the
    /// session, with the bench's classic-engine options) or deleting
    /// trailing subs.  Per-sub items are managed by
    /// <see cref="ConvergeItemsAsync"/> afterwards.
    /// </summary>
    private async Task ConvergeSubscriptionsAsync(ManagedSession session, int targetSubs)
    {
        int currentSubs;
        lock (m_liveItems)
        {
            currentSubs = m_subscriptions.Count;
        }
        if (targetSubs > currentSubs)
        {
            int toAdd = targetSubs - currentSubs;
            for (int i = 0; i < toAdd; i++)
            {
                var sub = new ClassicSubscription(session.MessageContext.Telemetry, new ClassicSubscriptionOptions
                {
                    DisplayName = $"UaLens.Bench/{Title}/{currentSubs + i}",
                    PublishingInterval = (int)m_subConfig.PublishingInterval.TotalMilliseconds,
                    KeepAliveCount = m_subConfig.KeepAliveCount,
                    LifetimeCount = m_subConfig.LifetimeCount,
                    MaxNotificationsPerPublish = m_subConfig.MaxNotificationsPerPublish,
                    Priority = m_subConfig.Priority,
                    PublishingEnabled = m_subConfig.PublishingEnabled,
                    MinLifetimeInterval = 60_000
                })
                {
                    FastDataChangeCallback = OnFastDataChange,
                    FastKeepAliveCallback = OnFastKeepAlive
                };
                if (!session.AddSubscription(sub))
                {
                    m_log.LogWarning("Subscription Bench: AddSubscription returned false at sub #{Idx}.", currentSubs + i);
                    sub.Dispose();
                    break;
                }
                try
                {
                    await sub.CreateAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_log.LogWarning(ex, "Subscription Bench: CreateAsync failed at sub #{Idx}.", currentSubs + i);
                    sub.Dispose();
                    // Surface the failure via SubsSliderValue rollback
                    // so the slider matches reality.
                    Dispatcher.UIThread.Post(() => SubsSliderValue = currentSubs + i);
                    return;
                }
                lock (m_liveItems)
                {
                    m_subscriptions.Add(sub);
                    m_liveItems.Add(new List<ClassicMonitoredItem>());
                }
            }
        }
        else if (targetSubs < currentSubs)
        {
            int toRemove = currentSubs - targetSubs;
            var doomed = new List<ClassicSubscription>(toRemove);
            lock (m_liveItems)
            {
                int startIdx = m_subscriptions.Count - toRemove;
                for (int i = startIdx; i < m_subscriptions.Count; i++)
                {
                    doomed.Add(m_subscriptions[i]);
                }
                m_subscriptions.RemoveRange(startIdx, toRemove);
                m_liveItems.RemoveRange(startIdx, toRemove);
            }
            foreach (ClassicSubscription sub in doomed)
            {
                try
                {
                    await sub.DeleteAsync(silent: true, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_log.LogWarning(ex, "Subscription Bench: DeleteAsync failed during shrink.");
                }
                sub.Dispose();
            }
        }
    }

    /// <summary>
    /// For each live subscription, grow / shrink its item list to
    /// <paramref name="targetItems"/>.  Each sub fills slots
    /// <c>0..N-1</c> from <c>pool[i % pool.Count]</c> independently so
    /// subs with the same target end up with the same item set —
    /// the simplest, most predictable layout for a scale test.
    /// </summary>
    private async Task ConvergeItemsAsync(
        int targetItems,
        List<NodeId> poolSnapshot,
        List<string> poolNames)
    {
        // Snapshot of subs to iterate without holding the lock during
        // ApplyChangesAsync round-trips.
        List<ClassicSubscription> subs;
        List<List<ClassicMonitoredItem>> liveItemsByIndex;
        lock (m_liveItems)
        {
            subs = new List<ClassicSubscription>(m_subscriptions);
            liveItemsByIndex = new List<List<ClassicMonitoredItem>>(m_liveItems);
        }
        for (int subIdx = 0; subIdx < subs.Count; subIdx++)
        {
            ClassicSubscription sub = subs[subIdx];
            List<ClassicMonitoredItem> live = liveItemsByIndex[subIdx];
            int currentCount = live.Count;
            if (targetItems == currentCount)
            {
                continue;
            }
            if (targetItems > currentCount)
            {
                if (poolSnapshot.Count == 0)
                {
                    continue;
                }
                int toAdd = targetItems - currentCount;
                var toAddList = new List<ClassicMonitoredItem>(toAdd);
                for (int i = 0; i < toAdd; i++)
                {
                    int slot = currentCount + i;
                    int poolIdx = slot % poolSnapshot.Count;
                    var mi = new ClassicMonitoredItem(
                        sub.Session?.MessageContext.Telemetry ?? AmbientMessageContext.Telemetry!,
                        new ClassicMonitoredItemOptions
                        {
                            DisplayName = $"bench[{subIdx}.{slot}]/{poolNames[poolIdx]}",
                            StartNodeId = poolSnapshot[poolIdx],
                            AttributeId = Attributes.Value,
                            MonitoringMode = m_itemSettings.MonitoringMode,
                            SamplingInterval = (int)m_itemSettings.SamplingInterval.TotalMilliseconds,
                            QueueSize = m_itemSettings.QueueSize,
                            DiscardOldest = m_itemSettings.DiscardOldest,
                            Filter = m_itemSettings.DataChangeFilter
                        });
                    toAddList.Add(mi);
                }
                sub.AddItems(toAddList);
                lock (m_liveItems)
                {
                    live.AddRange(toAddList);
                }
            }
            else
            {
                int toRemove = currentCount - targetItems;
                var toRemoveList = new List<ClassicMonitoredItem>(toRemove);
                lock (m_liveItems)
                {
                    int startIdx = live.Count - toRemove;
                    for (int i = startIdx; i < live.Count; i++)
                    {
                        toRemoveList.Add(live[i]);
                    }
                    live.RemoveRange(startIdx, toRemove);
                }
                sub.RemoveItems(toRemoveList);
            }
            try
            {
                await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "Subscription Bench: ApplyChangesAsync failed on sub {Id}.", sub.Id);
            }
        }
    }

    /// <summary>Recount bad-status items across every live sub.</summary>
    private void RecountErrors()
    {
        long bad = 0;
        List<ClassicSubscription> subs;
        lock (m_liveItems)
        {
            subs = new List<ClassicSubscription>(m_subscriptions);
        }
        foreach (ClassicSubscription sub in subs)
        {
            foreach (ClassicMonitoredItem mi in sub.MonitoredItems)
            {
                ServiceResult? err = mi.Status?.Error;
                if (err is not null && ServiceResult.IsBad(err))
                {
                    bad++;
                }
            }
        }
        Interlocked.Exchange(ref m_totalErrors, bad);
    }

    // ---- Aggregation ----

    private void StartAggregationTimer()
    {
        m_runStartTicks = Stopwatch.GetTimestamp();
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
        if (m_subscriptions.Count > 0)
        {
            // Use the first subscription's revised publishing interval
            // as the representative (all subs use the same options).
            ClassicSubscription rep;
            lock (m_liveItems)
            {
                rep = m_subscriptions[0];
            }
            PublishIntervalText = string.Format(CultureInfo.InvariantCulture,
                "Publish int.: {0:N0} ms (revised)", rep.CurrentPublishingInterval);
        }
        else
        {
            PublishIntervalText = "Publish int.: —";
        }

        (double cpu, double mem) = m_host.Main.ResourceMonitor?.SampleNumeric() ?? (double.NaN, 0);

        double secondsSinceStart = (Stopwatch.GetTimestamp() - m_runStartTicks)
            / (double)Stopwatch.Frequency;
        m_chartQueue.Enqueue(new ChartSample(
            secondsSinceStart, last1, avg10, avg30, avg60, cpu, mem));

        // Status line + engine metrics.
        int subCount;
        int itemCount;
        lock (m_liveItems)
        {
            subCount = m_subscriptions.Count;
            itemCount = 0;
            for (int i = 0; i < m_liveItems.Count; i++)
            {
                itemCount += m_liveItems[i].Count;
            }
        }
        Status = string.Format(CultureInfo.InvariantCulture,
            "● {0} sub(s) · {1:N0} items · {2:N0} val/s",
            subCount, itemCount, last1);

        EngineMetricsText = BuildEngineMetricsText(total, subCount, itemCount);
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

    private string BuildEngineMetricsText(long total, int subCount, int itemCount)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Subscriptions     : {subCount} (×{itemCount} items)\n");
        // List up to 4 sub IDs so the user can correlate with server logs;
        // beyond that, just show the count and rely on the aggregate metrics.
        List<ClassicSubscription> subs;
        lock (m_liveItems)
        {
            subs = new List<ClassicSubscription>(m_subscriptions);
        }
        int show = Math.Min(subs.Count, 4);
        if (show > 0)
        {
            sb.Append("Sub ids           : ");
            for (int i = 0; i < show; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(CultureInfo.InvariantCulture, $"{subs[i].Id}");
            }
            if (subs.Count > show)
            {
                sb.Append(CultureInfo.InvariantCulture, $", … (+{subs.Count - show})");
            }
            sb.Append('\n');
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
