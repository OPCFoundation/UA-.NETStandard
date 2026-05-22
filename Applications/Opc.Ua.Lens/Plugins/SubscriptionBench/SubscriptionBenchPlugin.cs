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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.ViewModels;
using UaLens.Views;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

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
    /// Live subscriptions (V2 <see cref="ISubscription"/>), in creation
    /// order.  Index <c>i</c> here corresponds to index <c>i</c> in
    /// <see cref="m_liveItems"/> so the per-sub item lists track their
    /// owning subscription without requiring a dictionary lookup on
    /// every resize.
    /// </summary>
    private readonly List<ISubscription> m_subscriptions = new();

    /// <summary>Per-subscription monitored items + their per-item
    /// option monitors (one OptionsMonitor per item because each
    /// carries its own StartNodeId).  The "shrink" path tears items
    /// off the tail; the "grow" path appends from the pool round-robin
    /// so removed items rejoin in the same slot the next time the
    /// slider grows.</summary>
    private readonly List<List<BenchItem>> m_liveItems = new();

    /// <summary>Single shared options monitor for every live
    /// subscription.  All subs in a bench tab share the same
    /// subscription parameters, so one monitor + a single
    /// CurrentValue update propagates an edit to every sub.</summary>
    private readonly OptionsMonitor<V2SubscriptionOptions> m_sharedSubOptions =
        new(new V2SubscriptionOptions
        {
            PublishingInterval = TimeSpan.FromMilliseconds(1000),
            KeepAliveCount = 10,
            LifetimeCount = 1000,
            MaxNotificationsPerPublish = 0,
            Priority = 0,
            PublishingEnabled = true,
            Disabled = false,
            MinLifetimeInterval = TimeSpan.FromMinutes(1)
        });

    /// <summary>Single shared notification handler for every live
    /// subscription — keeps counters thread-safe via Interlocked.</summary>
    private readonly BenchHandler m_handler;

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
    /// from the V2 notification handler on the publish dispatcher thread).</summary>
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
        m_handler = new BenchHandler(this);
        int n;
        lock (s_perKindCounter)
        {
            s_perKindCounter.TryGetValue(PluginKind.SubscriptionBench, out int prev);
            n = prev + 1;
            s_perKindCounter[PluginKind.SubscriptionBench] = n;
        }
        m_title = $"Subscription Bench {n}";

        // Mirror the current host connection state into our IsConnected
        // flag so the sliders are usable from the moment the tab opens.
        // Without this call IsConnected stays false (the field default)
        // until the next connect / disconnect transition, leaving the
        // bench inoperable when the user opens it on an already-connected
        // session.  Subsequent transitions arrive via the central
        // IPlugin.OnConnectionStateChanged hook; the body is idempotent
        // (RefreshServerLimits + StartAggregationTimer guard against
        // re-running) so calling it twice is harmless.
        OnConnectionStateChanged();
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
        List<ISubscription> snapshot;
        lock (m_liveItems)
        {
            snapshot = new List<ISubscription>(m_subscriptions);
            m_subscriptions.Clear();
            m_liveItems.Clear();
        }
        foreach (ISubscription sub in snapshot)
        {
            try
            {
                await sub.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "Subscription Bench {Title} delete failed during dispose.", Title);
            }
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
    /// <summary>
    /// Open the (same as Subscription tab) settings dialog with the
    /// current <see cref="m_subConfig"/>; on OK, save the new config and
    /// push it via the shared <see cref="m_sharedSubOptions"/> monitor.
    /// Every live <see cref="ISubscription"/> subscribed to that monitor
    /// re-applies on the next dispatch cycle.  Subsequent grows pick
    /// the new config up automatically.
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

        // Update the shared options monitor; the V2 dispatcher
        // re-applies every subscribed sub on the next cycle.  Share the
        // converge lock so we don't race with a slider-driven
        // grow / shrink.
        await m_resizeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            int subCount;
            lock (m_liveItems)
            {
                subCount = m_subscriptions.Count;
            }
            m_sharedSubOptions.CurrentValue = new V2SubscriptionOptions
            {
                PublishingInterval = m_subConfig.PublishingInterval,
                KeepAliveCount = m_subConfig.KeepAliveCount,
                LifetimeCount = m_subConfig.LifetimeCount,
                MaxNotificationsPerPublish = m_subConfig.MaxNotificationsPerPublish,
                Priority = m_subConfig.Priority,
                PublishingEnabled = m_subConfig.PublishingEnabled,
                Disabled = false,
                MinLifetimeInterval = TimeSpan.FromMinutes(1)
            };
            Status = string.Format(CultureInfo.InvariantCulture,
                "● Subscription parameters applied to {0} sub(s).", subCount);
        }
        finally
        {
            m_resizeLock.Release();
        }
    }

    /// <summary>
    /// Open the generic monitored-item settings dialog with the current
    /// <see cref="m_itemSettings"/>; on OK, save and walk every live
    /// item's <see cref="OptionsMonitor{V2MonitoredItemOptions}"/>
    /// updating CurrentValue with the new sampling / queue / mode /
    /// filter — the V2 dispatcher modifies the items on the wire.
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
        m_itemSettings = result;

        await m_resizeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<List<BenchItem>> snapshot;
            lock (m_liveItems)
            {
                snapshot = new List<List<BenchItem>>(m_liveItems);
            }
            int totalItems = 0;
            foreach (List<BenchItem> bucket in snapshot)
            {
                foreach (BenchItem entry in bucket)
                {
                    V2MonitoredItemOptions current = entry.Opts.CurrentValue;
                    // Preserve the per-item StartNodeId / AttributeId; only
                    // overwrite the sampling / queue / mode / filter knobs.
                    entry.Opts.CurrentValue = new V2MonitoredItemOptions
                    {
                        StartNodeId = current.StartNodeId,
                        AttributeId = current.AttributeId,
                        SamplingInterval = m_itemSettings.SamplingInterval,
                        QueueSize = m_itemSettings.QueueSize,
                        DiscardOldest = m_itemSettings.DiscardOldest,
                        MonitoringMode = m_itemSettings.MonitoringMode,
                        Filter = m_itemSettings.DataChangeFilter
                    };
                    totalItems++;
                }
            }
            Status = string.Format(CultureInfo.InvariantCulture,
                "● Item settings applied to {0} item(s) across {1} sub(s).",
                totalItems, snapshot.Count);
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
    /// <paramref name="targetSubs"/> by adding fresh V2
    /// <see cref="ISubscription"/>s via
    /// <see cref="ISubscriptionManager.Add"/> (which feeds the V2
    /// publish dispatcher so the bench's <see cref="BenchHandler"/>
    /// actually receives notifications), or by tearing trailing subs
    /// off the tail.  All subs share <see cref="m_sharedSubOptions"/>
    /// so a single CurrentValue update from
    /// <see cref="EditSubscriptionAsync"/> propagates everywhere.
    /// Per-sub items are managed by <see cref="ConvergeItemsAsync"/>
    /// afterwards.
    /// </summary>
    private Task ConvergeSubscriptionsAsync(ManagedSession session, int targetSubs)
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
                ISubscription sub;
                try
                {
                    sub = session.SubscriptionManager.Add(m_handler, m_sharedSubOptions);
                }
                catch (InvalidOperationException ex)
                {
                    m_log.LogWarning(ex, "Subscription Bench requires the V2 (channel) subscription engine.");
                    Status = "● Subscription Bench requires the V2 engine — switch via Connection ↻ Engine.";
                    Dispatcher.UIThread.Post(() => SubsSliderValue = currentSubs + i);
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    m_log.LogWarning(ex, "Subscription Bench: SubscriptionManager.Add failed at sub #{Idx}.", currentSubs + i);
                    Dispatcher.UIThread.Post(() => SubsSliderValue = currentSubs + i);
                    return Task.CompletedTask;
                }
                lock (m_liveItems)
                {
                    m_subscriptions.Add(sub);
                    m_liveItems.Add(new List<BenchItem>());
                }
            }
        }
        else if (targetSubs < currentSubs)
        {
            int toRemove = currentSubs - targetSubs;
            var doomed = new List<ISubscription>(toRemove);
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
            // V2 subs are IAsyncDisposable; DisposeAsync deletes server-side
            // state and removes the sub from the SubscriptionManager.
            foreach (ISubscription sub in doomed)
            {
                _ = DisposeSubAsync(sub);
            }
        }
        return Task.CompletedTask;
    }

    private async Task DisposeSubAsync(ISubscription sub)
    {
        try
        {
            await sub.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Subscription Bench: ISubscription.DisposeAsync failed during shrink.");
        }
    }

    /// <summary>
    /// For each live subscription, grow / shrink its item list to
    /// <paramref name="targetItems"/>.  Each sub fills slots
    /// <c>0..N-1</c> from <c>pool[i % pool.Count]</c> independently so
    /// subs with the same target end up with the same item set —
    /// the simplest, most predictable layout for a scale test.
    /// </summary>
    private Task ConvergeItemsAsync(
        int targetItems,
        List<NodeId> poolSnapshot,
        List<string> poolNames)
    {
        // Snapshot of subs to iterate without holding the lock during
        // TryAdd / TryRemove operations.
        List<ISubscription> subs;
        List<List<BenchItem>> liveItemsByIndex;
        lock (m_liveItems)
        {
            subs = new List<ISubscription>(m_subscriptions);
            liveItemsByIndex = new List<List<BenchItem>>(m_liveItems);
        }
        for (int subIdx = 0; subIdx < subs.Count; subIdx++)
        {
            ISubscription sub = subs[subIdx];
            List<BenchItem> live = liveItemsByIndex[subIdx];
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
                var added = new List<BenchItem>(toAdd);
                for (int i = 0; i < toAdd; i++)
                {
                    int slot = currentCount + i;
                    int poolIdx = slot % poolSnapshot.Count;
                    var opts = new V2MonitoredItemOptions
                    {
                        StartNodeId = poolSnapshot[poolIdx],
                        AttributeId = Attributes.Value,
                        SamplingInterval = m_itemSettings.SamplingInterval,
                        QueueSize = m_itemSettings.QueueSize,
                        DiscardOldest = m_itemSettings.DiscardOldest,
                        MonitoringMode = m_itemSettings.MonitoringMode,
                        Filter = m_itemSettings.DataChangeFilter
                    };
                    var monitor = new OptionsMonitor<V2MonitoredItemOptions>(opts);
                    string name = $"bench[{subIdx}.{slot}]";
                    if (sub.MonitoredItems.TryAdd(name, monitor, out IMonitoredItem? created)
                        && created is not null)
                    {
                        added.Add(new BenchItem(created, monitor));
                    }
                    else
                    {
                        m_log.LogWarning(
                            "Subscription Bench: MonitoredItems.TryAdd returned false at sub#{S} slot#{I}.",
                            subIdx, slot);
                    }
                }
                lock (m_liveItems)
                {
                    live.AddRange(added);
                }
            }
            else
            {
                int toRemove = currentCount - targetItems;
                List<BenchItem> doomed;
                lock (m_liveItems)
                {
                    int startIdx = live.Count - toRemove;
                    doomed = new List<BenchItem>(toRemove);
                    for (int i = startIdx; i < live.Count; i++)
                    {
                        doomed.Add(live[i]);
                    }
                    live.RemoveRange(startIdx, toRemove);
                }
                foreach (BenchItem item in doomed)
                {
                    if (!sub.MonitoredItems.TryRemove(item.Item.ClientHandle))
                    {
                        m_log.LogDebug(
                            "Subscription Bench: TryRemove returned false for handle {H}.",
                            item.Item.ClientHandle);
                    }
                }
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Recount bad-status items across every live sub.</summary>
    private void RecountErrors()
    {
        long bad = 0;
        List<List<BenchItem>> snapshot;
        lock (m_liveItems)
        {
            snapshot = new List<List<BenchItem>>(m_liveItems);
        }
        foreach (List<BenchItem> bucket in snapshot)
        {
            foreach (BenchItem entry in bucket)
            {
                ServiceResult? err = entry.Item.Error;
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
            ISubscription rep;
            lock (m_liveItems)
            {
                rep = m_subscriptions[0];
            }
            PublishIntervalText = string.Format(CultureInfo.InvariantCulture,
                "Publish int.: {0:N0} ms (revised)", rep.CurrentPublishingInterval.TotalMilliseconds);
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

    // ---- V2 notification handler + per-item bookkeeping ----

    /// <summary>
    /// Per-monitored-item bookkeeping: the V2 <see cref="IMonitoredItem"/>
    /// handle returned by <see cref="IMonitoredItemCollection.TryAdd"/>
    /// plus the per-item <see cref="OptionsMonitor{V2MonitoredItemOptions}"/>
    /// we kept around so <see cref="EditItemSettingsAsync"/> can push a
    /// new sampling / queue / mode / filter without recreating the item.
    /// </summary>
    private sealed record BenchItem(
        IMonitoredItem Item,
        OptionsMonitor<V2MonitoredItemOptions> Opts);

    /// <summary>
    /// Single notification handler shared by every live
    /// <see cref="ISubscription"/>.  Increments the bench's atomic
    /// counters on every data-change notification so the chart and
    /// the "Cumulative values" stat reflect actual throughput.
    /// </summary>
    /// <remarks>
    /// Lives in the V2 publish dispatcher thread context; everything
    /// is Interlocked so multiple subs feeding concurrently is safe.
    /// </remarks>
    private sealed class BenchHandler : ISubscriptionNotificationHandler
    {
        private readonly SubscriptionBenchPlugin m_owner;

        public BenchHandler(SubscriptionBenchPlugin owner)
        {
            m_owner = owner;
        }

        public ValueTask OnDataChangeNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            int n = notification.Length;
            if (n <= 0)
            {
                return ValueTask.CompletedTask;
            }
            Interlocked.Add(ref m_owner.m_totalValues, n);
            // Read the bucket index atomically — the timer thread only
            // advances it; we tolerate a single-bucket drift at the
            // rollover boundary because the contributions to total
            // throughput are identical either way.
            int idx = Volatile.Read(ref m_owner.m_currentBucket);
            Interlocked.Add(ref m_owner.m_bucketsPerSec[idx], n);

            // Capture status errors observed in this notification batch.
            long bad = 0;
            ReadOnlySpan<DataValueChange> span = notification.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (StatusCode.IsBad(span[i].Value.StatusCode))
                {
                    bad++;
                }
            }
            if (bad > 0)
            {
                Interlocked.Add(ref m_owner.m_totalErrors, bad);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask OnEventDataNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<EventNotification> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            // The bench only adds value-monitored items; ignore events.
            return ValueTask.CompletedTask;
        }

        public ValueTask OnKeepAliveNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            PublishState publishStateMask)
        {
            // Keep-alives only keep the publish pipeline warm and don't
            // carry data; bench throughput maths intentionally ignores them.
            return ValueTask.CompletedTask;
        }
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
