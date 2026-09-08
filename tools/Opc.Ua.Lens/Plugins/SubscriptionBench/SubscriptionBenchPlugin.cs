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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Storage;
using UaLens.Subscriptions;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// Diagnose document that scale-tests a server's subscription pipeline. Two live
/// sliders shape the bench — the number of subscriptions and the number of monitored
/// items per subscription — drawing nodes from a user-curated variable pool. The
/// live topology is owned by an injected <see cref="BenchTopology"/> so the desktop
/// drives the real V2 stack while tests drive a fake. Session-dependent work runs in
/// <see cref="OnConnectionStateChangedAsync"/>, never in the constructor; every
/// scaling operation is cancelled and awaited on disconnect and disposal so no stale
/// work recreates resources after the document closes. The session-wide publish
/// pipeline is deliberately not configured here — that belongs to the connection.
/// </summary>
internal sealed partial class SubscriptionBenchPlugin : ObservableObject, IPlugin, IWorkspaceState
{
    private static readonly System.Threading.Lock s_counterLock = new();
    private static readonly Dictionary<PluginKind, int> s_perKindCounter = new();

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private readonly BenchThroughputCounters m_counters = new();
    private readonly ConcurrentQueue<ChartSample> m_chartQueue = new();

    private readonly List<NodeId> m_poolNodeIds = new();
    private readonly List<string> m_poolNames = new();
    private readonly System.Threading.Lock m_poolLock = new();

    // Serializes the coalescing converge loop. A slider move sets m_convergePending
    // and (re)starts the loop if it is not already running; the loop re-reads the
    // latest targets so rapid drags collapse into a single converge wave.
    private readonly System.Threading.Lock m_convergeSync = new();
    private Task m_convergeLoop = Task.CompletedTask;
    private bool m_loopRunning;
    private int m_convergePending;

    private BenchTopology? m_topology;
    private CancellationTokenSource? m_sessionCts;
    private long m_boundGeneration = -1;

    private SubscriptionConfig m_subConfig = new()
    {
        PublishingInterval = TimeSpan.FromMilliseconds(1000),
        KeepAliveCount = 10,
        LifetimeCount = 1000,
        MaxNotificationsPerPublish = 0,
        Priority = 0,
        PublishingEnabled = true
    };

    private MonitoredItemSettings m_itemSettings = new();

    private int m_savedSubsIntent;
    private int m_savedItemsIntent;

    private DispatcherTimer? m_aggregationTimer;
    private SubscriptionBenchView? m_view;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "Not connected.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResize))]
    private string m_poolDescription = "0 variables in pool.";

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
    [NotifyCanExecuteChangedFor(nameof(ApplySavedSizeCommand))]
    private bool m_isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySavedSizeCommand))]
    [NotifyPropertyChangedFor(nameof(SavedSizeText))]
    private bool m_hasSavedSize;

    [ObservableProperty]
    private bool m_showEngineDetails;

    [ObservableProperty]
    private string m_stats1sText = "1 s   :       0 val/s";

    [ObservableProperty]
    private string m_stats10sText = "10 s  :       0 val/s";

    [ObservableProperty]
    private string m_stats30sText = "30 s  :       0 val/s";

    [ObservableProperty]
    private string m_stats60sText = "60 s  :       0 val/s";

    [ObservableProperty]
    private string m_totalValuesText = "Total values: 0";

    [ObservableProperty]
    private string m_totalErrorsText = "Errors      : 0";

    [ObservableProperty]
    private string m_publishIntervalText = "Publish interval: —";

    [ObservableProperty]
    private string m_engineMetricsText =
        "Connect and move the sliders to populate engine metrics.";

    /// <summary>
    /// Flag the view consumes to clear its chart loggers after a counter reset.
    /// </summary>
    public bool WasReset { get; set; }

    /// <summary>
    /// Formatted items-slider readout — "current / max".
    /// </summary>
    public string ItemsSizeText => string.Format(CultureInfo.InvariantCulture,
        "{0:N0} / {1:N0}", ItemsSliderValue, ItemsSliderMax);

    /// <summary>
    /// Formatted subscriptions-slider readout — "current / max".
    /// </summary>
    public string SubsSizeText => string.Format(CultureInfo.InvariantCulture,
        "{0:N0} / {1:N0}", SubsSliderValue, SubsSliderMax);

    /// <summary>
    /// Describes a restored bench size that is prepared but not yet running.
    /// </summary>
    public string SavedSizeText => HasSavedSize
        ? string.Format(CultureInfo.InvariantCulture,
            "Saved bench size: {0:N0} subscription(s) × {1:N0} item(s). Connect, then Apply saved size to start.",
            m_savedSubsIntent, m_savedItemsIntent)
        : string.Empty;

    /// <summary>
    /// True when the sliders can shape the bench — connected with at least one pool
    /// variable to draw from.
    /// </summary>
    public bool CanResize
    {
        get
        {
            lock (m_poolLock)
            {
                return IsConnected && m_poolNodeIds.Count > 0;
            }
        }
    }

    public SubscriptionBenchPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n;
        lock (s_counterLock)
        {
            s_perKindCounter.TryGetValue(PluginKind.SubscriptionBench, out int prev);
            n = prev + 1;
            s_perKindCounter[PluginKind.SubscriptionBench] = n;
        }
        m_title = $"Subscription Bench {n}";
    }

    public PluginKind Kind => PluginKind.SubscriptionBench;

    Control? IPlugin.View => m_view ??= new SubscriptionBenchView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return new[]
        {
            CreateMenuItem("Pick _Variables…", PickVariablesCommand),
            CreateMenuItem("Pick Sub_tree…", PickSubtreeCommand),
            CreateMenuItem("_Subscription settings…", EditSubscriptionCommand),
            CreateMenuItem("_Item settings…", EditItemSettingsCommand),
            CreateMenuItem("_Stop", StopCommand),
            CreateMenuItem("_Clear counters", ClearCommand)
        };
    }

    private static MenuItem CreateMenuItem(string header, System.Windows.Input.ICommand cmd)
    {
        return new MenuItem { Header = header, Command = cmd };
    }

    public void OnActivated()
    {
    }

    public void OnDeactivated()
    {
    }

    /// <summary>
    /// Binds or releases the primary session. A new session generation rebuilds the
    /// topology and refreshes server limits; a same-generation reconnect keeps the
    /// existing resources; a lost session cancels and awaits in-flight scaling and
    /// releases every subscription. Binding never starts scaling — only the sliders do.
    /// </summary>
    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        ManagedSession? session = m_host.Connection.Session;
        long generation = m_host.Connection.Snapshot.Generation;

        if (session is null)
        {
            await TearDownSessionAsync("Disconnected — connect to resume.").ConfigureAwait(true);
            return;
        }
        if (generation == m_boundGeneration && m_topology is not null)
        {
            // Same-generation ManagedSession reconnect: not a new session.
            IsConnected = true;
            EnsureAggregationTimer();
            return;
        }

        await TearDownSessionAsync(status: null).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        m_counters.Reset();
        m_sessionCts = new CancellationTokenSource();
        m_topology = new BenchTopology(
            new V2BenchResourceFactory(session, m_counters, m_subConfig),
            m_subConfig,
            m_itemSettings,
            m_log);
        m_boundGeneration = generation;
        RefreshServerLimits(session);
        IsConnected = true;
        EnsureAggregationTimer();
        Status = HasSavedSize
            ? "Connected — Apply saved size or move a slider to start."
            : "Connected — move a slider to create subscriptions.";
    }

    public async ValueTask DisposeAsync()
    {
        await TearDownSessionAsync(status: null).ConfigureAwait(true);
        m_aggregationTimer?.Stop();
        m_aggregationTimer = null;
    }

    /// <summary>
    /// Chart sample drained by the view's plot pump.
    /// </summary>
    public readonly record struct ChartSample(
        double Seconds,
        double ValuesPerSec1s,
        double ValuesPerSec10s,
        double ValuesPerSec30s,
        double ValuesPerSec60s,
        double Cpu,
        double MemMiB);

    /// <summary>
    /// Drains one chart sample for the view to plot.
    /// </summary>
    public bool TryDequeueChartSample(out ChartSample sample) => m_chartQueue.TryDequeue(out sample);

    [RelayCommand]
    private async Task PickVariablesAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            Status = "Not connected — connect first.";
            return;
        }

        Window? owner = TopLevelWindow();
        var dlg = new VariablePoolPickerDialog(
            session,
            ObjectIds.ObjectsFolder,
            "Browse the address space and tick the Variable nodes to add to the bench pool.");
        IReadOnlyList<(NodeId NodeId, string DisplayName)>? picked = owner is null
            ? await dlg.ShowDialog<IReadOnlyList<(NodeId, string)>?>(new Window()).ConfigureAwait(true)
            : await dlg.ShowDialog<IReadOnlyList<(NodeId, string)>?>(owner).ConfigureAwait(true);
        if (picked is null || picked.Count == 0)
        {
            Status = "Pick cancelled — pool unchanged.";
            return;
        }
        AppendPool(picked);
    }

    [RelayCommand]
    private async Task PickSubtreeAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            Status = "Not connected — connect first.";
            return;
        }

        Window? owner = TopLevelWindow();
        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: session,
            Root: ObjectIds.ObjectsFolder,
            Title: "Pick subtree root for Subscription Bench pool",
            AcceptedClasses: NodeClass.Unspecified,
            Header: "Pick a starting node. Every Variable beneath it is added to the bench pool."));
        NodeId? root = owner is null
            ? await picker.ShowDialog<NodeId?>(new Window()).ConfigureAwait(true)
            : await picker.ShowDialog<NodeId?>(owner).ConfigureAwait(true);
        if (!root.HasValue || root.Value.IsNull)
        {
            Status = "Subtree pick cancelled — pool unchanged.";
            return;
        }

        Status = "Walking subtree…";
        var collected = new List<(NodeId NodeId, string DisplayName)>();
        try
        {
            await WalkVariablesAsync(session, root.Value, collected, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"Subtree walk failed: {ex.Message}";
            SubscriptionBenchPluginLog.SubtreeWalkFailed(m_log, ex);
            return;
        }
        if (collected.Count == 0)
        {
            Status = "Subtree contains no Variable nodes.";
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
                br = await session.BrowseAsync(null, null, 0, browse, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }
            if (br.Results.Count == 0 || StatusCode.IsBad(br.Results[0].StatusCode))
            {
                continue;
            }
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
            "Added {0} variable(s) to the pool (skipped duplicates).", added);
    }

    /// <summary>
    /// Seeds the pool from the address-space context menu. A Variable node is added
    /// directly; a container node has its Variable descendants walked in. Never
    /// auto-starts the bench — the user still shapes it via the sliders.
    /// </summary>
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
            Status = "Connect to a server before seeding the pool.";
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
            Status = "Subtree walk failed: " + ex.Message;
            SubscriptionBenchPluginLog.SubtreeWalkFailed(m_log, ex);
            return;
        }
        if (collected.Count == 0)
        {
            Status = "No variables found beneath '" + name + "'.";
            return;
        }
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
        Status = "Pool cleared.";
    }

    private void RefreshPoolDescription()
    {
        int count;
        lock (m_poolLock)
        {
            count = m_poolNodeIds.Count;
        }
        PoolDescription = count == 0
            ? "0 variables in pool."
            : string.Format(CultureInfo.InvariantCulture, "{0:N0} variable(s) in pool.", count);
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task StopAsync()
    {
        SubsSliderValue = 0;
        ItemsSliderValue = 0;
        if (m_topology is { } topology)
        {
            CancellationToken ct = m_sessionCts?.Token ?? CancellationToken.None;
            try
            {
                await topology.ConvergeAsync(0, 0, Array.Empty<NodeId>(), ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Disconnect raced the stop; the teardown path releases resources.
            }
        }
        ClearCounters();
        Status = "Stopped — sliders reset and subscriptions released.";
        SubscriptionBenchPluginLog.Stopped(m_log, Title);
    }

    [RelayCommand]
    private void Clear() => ClearCounters();

    private bool CanApplySavedSize() => HasSavedSize && IsConnected;

    [RelayCommand(CanExecute = nameof(CanApplySavedSize))]
    private void ApplySavedSize()
    {
        int subs = Math.Clamp(m_savedSubsIntent, 0, SubsSliderMax);
        int items = Math.Clamp(m_savedItemsIntent, 0, ItemsSliderMax);
        HasSavedSize = false;
        SubsSliderValue = subs;
        ItemsSliderValue = items;
        Status = "Applying saved bench size…";
    }

    private void ClearCounters()
    {
        m_counters.Reset();
        while (m_chartQueue.TryDequeue(out _))
        {
        }
        WasReset = true;
        Stats1sText = "1 s   :       0 val/s";
        Stats10sText = "10 s  :       0 val/s";
        Stats30sText = "30 s  :       0 val/s";
        Stats60sText = "60 s  :       0 val/s";
        TotalValuesText = "Total values: 0";
        TotalErrorsText = "Errors      : 0";
    }

    [RelayCommand]
    private async Task EditSubscriptionAsync()
    {
        Window? owner = TopLevelWindow();
        bool engineHasWorkerPool =
            m_host.Connection.Engine == UaLens.Connection.SubscriptionEngineKind.ChannelV2;
        var dlg = new SubscriptionSettingsDialog(m_subConfig, engineHasWorkerPool);
        SubscriptionConfig? result = owner is null
            ? await dlg.ShowDialog<SubscriptionConfig?>(new Window()).ConfigureAwait(true)
            : await dlg.ShowDialog<SubscriptionConfig?>(owner).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        m_subConfig = result;
        if (m_topology is { } topology)
        {
            CancellationToken ct = m_sessionCts?.Token ?? CancellationToken.None;
            try
            {
                await topology.ApplySubscriptionSettingsAsync(m_subConfig, ct).ConfigureAwait(true);
                Status = string.Format(CultureInfo.InvariantCulture,
                    "Subscription parameters applied to {0} subscription(s).", topology.SubscriptionCount);
            }
            catch (OperationCanceledException)
            {
                // Disconnected while applying; the settings persist for the next session.
            }
        }
        else
        {
            Status = "Subscription parameters saved — they apply when connected.";
        }
    }

    [RelayCommand]
    private async Task EditItemSettingsAsync()
    {
        Window? owner = TopLevelWindow();
        const string scope =
            "Applied to every monitored item across all bench subscriptions, and to items added later.";
        var dlg = new MonitoredItemSettingsDialog(m_itemSettings, scope);
        MonitoredItemSettings? result = owner is null
            ? await dlg.ShowDialog<MonitoredItemSettings?>(new Window()).ConfigureAwait(true)
            : await dlg.ShowDialog<MonitoredItemSettings?>(owner).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        m_itemSettings = result;
        if (m_topology is { } topology)
        {
            CancellationToken ct = m_sessionCts?.Token ?? CancellationToken.None;
            try
            {
                int items = await topology.ApplyItemSettingsAsync(m_itemSettings, ct).ConfigureAwait(true);
                Status = string.Format(CultureInfo.InvariantCulture,
                    "Item settings applied to {0} item(s) across {1} subscription(s).",
                    items, topology.SubscriptionCount);
            }
            catch (OperationCanceledException)
            {
                // Disconnected while applying; the settings persist for the next session.
            }
        }
        else
        {
            Status = "Item settings saved — they apply when connected.";
        }
    }

    partial void OnSubsSliderValueChanged(int value) => RequestConverge();

    partial void OnItemsSliderValueChanged(int value) => RequestConverge();

    private void RequestConverge()
    {
        CancellationTokenSource? cts = m_sessionCts;
        if (cts is null)
        {
            // Offline (or tearing down): only intent is retained; no live scaling.
            return;
        }
        CancellationToken token = cts.Token;
        lock (m_convergeSync)
        {
            m_convergePending = 1;
            if (!m_loopRunning)
            {
                m_loopRunning = true;
                m_convergeLoop = Task.Run(() => RunConvergeLoopAsync(token));
            }
        }
    }

    private async Task RunConvergeLoopAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                lock (m_convergeSync)
                {
                    if (m_convergePending == 0)
                    {
                        m_loopRunning = false;
                        return;
                    }
                    m_convergePending = 0;
                }
                ct.ThrowIfCancellationRequested();
                await ConvergeOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            lock (m_convergeSync)
            {
                m_loopRunning = false;
            }
        }
    }

    private async Task ConvergeOnceAsync(CancellationToken ct)
    {
        BenchTopology? topology = m_topology;
        if (topology is null)
        {
            return;
        }
        int targetSubs = SubsSliderValue;
        int targetItems = ItemsSliderValue;
        List<NodeId> pool = SnapshotPoolNodeIds();
        BenchTopology.ConvergeResult result =
            await topology.ConvergeAsync(targetSubs, targetItems, pool, ct).ConfigureAwait(false);

        if (result.EngineUnavailable)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SubsSliderValue = result.Subscriptions;
                Status = "Subscription Bench requires the V2 engine — switch it in the connection settings.";
            });
        }
        else if (result.Error is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SubsSliderValue = result.Subscriptions;
                ItemsSliderValue = result.ItemsPerSubscription;
                Status = "Converge failed: " + result.Error;
            });
        }
    }

    private List<NodeId> SnapshotPoolNodeIds()
    {
        lock (m_poolLock)
        {
            return new List<NodeId>(m_poolNodeIds);
        }
    }

    private List<BenchPoolNode> SnapshotPoolNodes()
    {
        lock (m_poolLock)
        {
            var nodes = new List<BenchPoolNode>(m_poolNodeIds.Count);
            for (int i = 0; i < m_poolNodeIds.Count; i++)
            {
                nodes.Add(new BenchPoolNode(m_poolNodeIds[i], m_poolNames[i]));
            }
            return nodes;
        }
    }

    private async Task TearDownSessionAsync(string? status)
    {
        CancellationTokenSource? cts = m_sessionCts;
        m_sessionCts = null;
        m_boundGeneration = -1;

        Task loop;
        lock (m_convergeSync)
        {
            m_convergePending = 0;
            loop = m_convergeLoop;
        }
        if (cts is not null)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(true);
            }
            catch (ObjectDisposedException)
            {
            }
        }
        try
        {
            await loop.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }

        BenchTopology? topology = m_topology;
        m_topology = null;
        if (topology is not null)
        {
            await topology.DisposeAsync().ConfigureAwait(true);
        }
        cts?.Dispose();

        m_aggregationTimer?.Stop();
        IsConnected = false;
        SubsSliderValue = 0;
        ItemsSliderValue = 0;
        if (status is not null)
        {
            Status = status;
        }
    }

    private void RefreshServerLimits(ManagedSession session)
    {
        ServerCapabilities? caps = session.ServerCapabilities;
        uint mi = caps?.MaxMonitoredItemsPerSubscription ?? 0;
        uint subs = caps?.MaxSubscriptionsPerSession ?? 0;
        ItemsSliderMax = mi > 0 && mi < int.MaxValue ? (int)mi : 1000;
        SubsSliderMax = subs > 0 && subs < int.MaxValue ? (int)subs : 100;
    }

    private void EnsureAggregationTimer()
    {
        m_aggregationTimer ??= new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => OnAggregationTick());
        if (!m_aggregationTimer.IsEnabled)
        {
            m_aggregationTimer.Start();
        }
    }

    private void OnAggregationTick()
    {
        BenchThroughputCounters.Sample sample = m_counters.Rotate();
        Stats1sText = string.Format(CultureInfo.InvariantCulture, "1 s   : {0,7:N0} val/s", sample.Last1s);
        Stats10sText = string.Format(CultureInfo.InvariantCulture, "10 s  : {0,7:N1} val/s", sample.Avg10s);
        Stats30sText = string.Format(CultureInfo.InvariantCulture, "30 s  : {0,7:N1} val/s", sample.Avg30s);
        Stats60sText = string.Format(CultureInfo.InvariantCulture, "60 s  : {0,7:N1} val/s", sample.Avg60s);
        TotalValuesText = string.Format(CultureInfo.InvariantCulture, "Total values: {0:N0}", sample.TotalValues);
        TotalErrorsText = string.Format(CultureInfo.InvariantCulture, "Errors      : {0:N0}", sample.TotalErrors);

        BenchTopology? topology = m_topology;
        int subCount = topology?.SubscriptionCount ?? 0;
        int itemCount = topology?.TotalItemCount ?? 0;
        double? interval = topology?.RepresentativePublishingIntervalMs;
        PublishIntervalText = interval is { } ms
            ? string.Format(CultureInfo.InvariantCulture, "Publish interval: {0:N0} ms (revised)", ms)
            : "Publish interval: —";

        (double cpu, double mem) = m_host.ResourceMonitor?.LastSample ?? (double.NaN, double.NaN);
        m_chartQueue.Enqueue(new ChartSample(
            sample.Seconds, sample.Last1s, sample.Avg10s, sample.Avg30s, sample.Avg60s, cpu, mem));

        Status = string.Format(CultureInfo.InvariantCulture,
            "{0} subscription(s) · {1:N0} items · {2:N0} val/s", subCount, itemCount, sample.Last1s);

        if (ShowEngineDetails)
        {
            EngineMetricsText = BuildEngineMetricsText(sample.TotalValues, subCount, itemCount, topology);
        }
    }

    private string BuildEngineMetricsText(long total, int subCount, int itemCount, BenchTopology? topology)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Subscriptions     : {subCount} (×{itemCount} items)\n");
        if (topology is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"Items in error    : {topology.CountBadItems()}\n");
        }
        if (m_host.Connection.Session is { } session)
        {
            sb.Append(CultureInfo.InvariantCulture, $"Session good pubs : {session.GoodPublishRequestCount}\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"Session min/max   : {session.MinPublishRequestCount} / {session.MaxPublishRequestCount}\n");
            if (session.TryGetSubscriptionManager(
                out Opc.Ua.Client.Subscriptions.ISubscriptionManager? mgr))
            {
                sb.Append(CultureInfo.InvariantCulture, $"Mgr subscriptions : {mgr.Count}\n");
                sb.Append(CultureInfo.InvariantCulture, $"Workers (cur)     : {mgr.PublishWorkerCount}\n");
                sb.Append(CultureInfo.InvariantCulture,
                    $"Workers (min/max) : {mgr.MinPublishWorkerCount} / {mgr.MaxPublishWorkerCount}\n");
                sb.Append(CultureInfo.InvariantCulture,
                    $"Good / bad pubs   : {mgr.GoodPublishRequestCount} / {mgr.BadPublishRequestCount}\n");
                sb.Append(CultureInfo.InvariantCulture,
                    $"Missing / republ. : {mgr.MissingMessageCount} / {mgr.RepublishMessageCount}\n");
            }
            else
            {
                sb.Append("(classic engine — extra metrics unavailable)\n");
            }
        }
        sb.Append(CultureInfo.InvariantCulture, $"Cumulative values : {total:N0}\n");
        sb.Append("Publish pipeline (min/max requests) is configured in the connection settings.");
        return sb.ToString();
    }

    public JsonElement CaptureState()
    {
        List<BenchPoolNode> pool = SnapshotPoolNodes();
        int subs = HasSavedSize ? m_savedSubsIntent : SubsSliderValue;
        int items = HasSavedSize ? m_savedItemsIntent : ItemsSliderValue;
        SubscriptionBenchStateDto dto = SubscriptionBenchState.CreateDto(
            pool, subs, items, m_subConfig, m_itemSettings, ShowEngineDetails);
        return JsonSerializer.SerializeToElement(
            dto, SubscriptionBenchStateJsonContext.Default.SubscriptionBenchStateDto);
    }

    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubscriptionBenchStateDto? dto;
        try
        {
            dto = state.Deserialize(SubscriptionBenchStateJsonContext.Default.SubscriptionBenchStateDto);
        }
        catch (JsonException ex)
        {
            SubscriptionBenchPluginLog.RestoreFailed(m_log, ex);
            throw;
        }
        if (dto is null)
        {
            throw new JsonException("Subscription Bench configuration cannot be null.");
        }
        BenchRestoredState restored = SubscriptionBenchState.Validate(dto);
        ApplyRestoredState(restored);
        return Task.CompletedTask;
    }

    private void ApplyRestoredState(BenchRestoredState restored)
    {
        lock (m_poolLock)
        {
            m_poolNodeIds.Clear();
            m_poolNames.Clear();
            foreach (BenchPoolNode node in restored.Pool)
            {
                m_poolNodeIds.Add(node.NodeId);
                m_poolNames.Add(node.DisplayName);
            }
        }
        m_subConfig = restored.Subscription;
        m_itemSettings = restored.ItemSettings;
        ShowEngineDetails = restored.ShowEngineDetails;
        m_savedSubsIntent = restored.SavedSubscriptions;
        m_savedItemsIntent = restored.SavedItemsPerSubscription;
        HasSavedSize = m_savedSubsIntent > 0 || m_savedItemsIntent > 0;

        // Sliders stay at zero: restore prepares configuration in a stopped state and
        // never starts live scaling. The user starts it explicitly.
        RefreshPoolDescription();
        OnPropertyChanged(nameof(CanResize));
        OnPropertyChanged(nameof(SavedSizeText));
        Status = HasSavedSize
            ? "Configuration restored. Connect, then Apply saved size to start."
            : "Configuration restored.";
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

internal static partial class SubscriptionBenchPluginLog
{
    [LoggerMessage(
        EventId = UaLensEventIds.SubscriptionBenchStopped,
        Level = LogLevel.Information,
        Message = "Subscription Bench {Title} stopped and released its subscriptions.")]
    public static partial void Stopped(ILogger logger, string title);

    [LoggerMessage(
        EventId = UaLensEventIds.SubscriptionBenchSubtreeWalkFailed,
        Level = LogLevel.Warning,
        Message = "Subscription Bench subtree walk failed.")]
    public static partial void SubtreeWalkFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.SubscriptionBenchRestoreFailed,
        Level = LogLevel.Warning,
        Message = "Subscription Bench could not restore its saved configuration.")]
    public static partial void RestoreFailed(ILogger logger, Exception exception);
}
