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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.ViewModels;

/// <summary>
/// Per-tab subscription view model.  Each tab in the MainWindow's
/// subscription tab strip owns one of these and one
/// <see cref="ISubscriptionAdapter"/> (created via
/// <c>ConnectionService.CreateAdapter</c>).  This view model holds the
/// per-tab publishing config, the list of monitored items, and the
/// commands that mutate them.
/// </summary>
internal sealed partial class SubscriptionViewModel : ObservableObject, IPlugin
{
    private readonly ILogger m_log;
    private ISubscriptionAdapter? m_adapter;

    /// <summary>
    /// The live adapter, or <c>null</c> when the tab was created in a
    /// disconnected state and not yet bound.  Commands that mutate the
    /// subscription early-return when null; the chart binders treat
    /// null as "no source".  Bound via <see cref="AttachAdapterAsync"/>
    /// once a session is available.
    /// </summary>
    public ISubscriptionAdapter? Adapter => m_adapter;

    /// <summary>True when this tab has an adapter wired to a live session.</summary>
    public bool IsBound => m_adapter is not null;

    public PluginKind Kind => PluginKind.Subscription;

    /// <summary>Tab header text.</summary>
    [ObservableProperty]
    private string m_title = "Sub";

    /// <summary>True while the user is editing this tab's title inline.</summary>
    [ObservableProperty]
    private bool m_isRenaming;

    /// <summary>
    /// Per-(this tab, mode) cached ScottPlot axis limits — preserves the
    /// user's pan/zoom across mode switches so re-entering a mode restores
    /// the previously viewed framing.
    /// </summary>
    internal Dictionary<AnimationMode, ScottPlot.AxisLimits> AxisLimitsCache { get; }
        = new();

    /// <summary>
    /// Rolling buffer of recently received notifications for this tab.
    /// Populated by whichever consumer is currently draining the channel
    /// (AnimationCanvas or ScottPlotView).  Exposed for the File → Export
    /// Tab Data... flow.
    /// </summary>
    public UaLens.Connection.NotificationRecorder Recorder { get; }
        = new();

    /// <summary>Active publishing config for this tab.</summary>
    [ObservableProperty]
    private SubscriptionConfig m_subscription = new();

    /// <summary>Status string rendered under the animation.</summary>
    [ObservableProperty]
    private string m_subscriptionStatus = "● No subscription";

    /// <summary>Monitored items currently subscribed in this tab.</summary>
    public ObservableCollection<MonitoredItemConfig> Items { get; } = new();

    /// <summary>
    /// Per-monitored-item status rows for the B1 status sub-pane.  Kept
    /// in lock-step with <see cref="Items"/>: rows are added/removed by
    /// <see cref="OnItemsCollectionChanged"/> and dynamic columns
    /// (Mode / Samples / Last status / Last value) are refreshed by the
    /// 250 ms <see cref="m_statusRefreshTimer"/> from the adapter's
    /// live-stats dictionary.
    /// </summary>
    public ObservableCollection<MonitoredItemStatusRow> ItemStatuses { get; } = new();

    /// <summary>
    /// Toggles visibility of the per-item status sub-pane.  Persists per
    /// tab (each <see cref="SubscriptionViewModel"/> tracks its own
    /// preference).  When true the VM also drives a 250 ms status
    /// refresh timer; when false the timer stays parked.
    /// </summary>
    [ObservableProperty]
    private bool m_showItemStatusGrid;

    /// <summary>Chart legend visibility (ScottPlot signal/histogram/heatmap modes).
    /// Defaults to false — chart is busy enough without legend / axis chrome;
    /// the user can opt-in via the per-tab checkboxes.</summary>
    [ObservableProperty]
    private bool m_showLegend;

    /// <summary>Chart X-axis visibility (labels + ticks; bottom axis).
    /// Defaults to false — see <see cref="ShowLegend"/>.</summary>
    [ObservableProperty]
    private bool m_showXAxis;

    /// <summary>Chart Y-axis visibility (labels + ticks; left axis).
    /// Defaults to false — see <see cref="ShowLegend"/>.</summary>
    [ObservableProperty]
    private bool m_showYAxis;

    /// <summary>250 ms throttle for status sub-pane refresh.</summary>
    private static readonly TimeSpan s_statusRefreshInterval
        = TimeSpan.FromMilliseconds(250);
    private DispatcherTimer? m_statusRefreshTimer;

    /// <summary>Currently-selected item (mostly for display / future actions).</summary>
    [ObservableProperty]
    private MonitoredItemConfig? m_selectedItem;

    /// <summary>
    /// Per-tab animation view-mode (Dots / Bars / Lines).  Each tab keeps
    /// its own choice so switching tabs restores the user's preferred
    /// visualisation for that subscription.  Default is
    /// <see cref="Views.AnimationMode.Dots"/>.
    /// </summary>
    [ObservableProperty]
    private UaLens.Views.AnimationMode m_animationMode
        = UaLens.Views.AnimationMode.Dots;

    /// <summary>
    /// Per-tab time-axis stretch factor for the AnimationCanvas.  1.0 =
    /// default; doubled by [+] / Ctrl-+, halved by [-] / Ctrl--, reset to
    /// 1.0 by Ctrl-0.  Clamped 0.125..8 in the UI handlers.
    /// </summary>
    [ObservableProperty]
    private double m_animationTimeScale = 1.0;

    /// <summary>
    /// Per-tab toggle for the CPU / memory overlay rendered on top of the
    /// animation canvas.  Off by default.
    /// </summary>
    [ObservableProperty]
    private bool m_showResourceOverlay;

    public SubscriptionViewModel(string title, ISubscriptionAdapter? adapter, ILogger log)
    {
        m_title = title;
        m_adapter = adapter;
        m_log = log;
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    /// <summary>
    /// Mirror Items mutations into <see cref="ItemStatuses"/>.  Runs on
    /// the UI thread because <see cref="Items"/> is only ever mutated via
    /// <see cref="Dispatcher.UIThread.Post"/>.
    /// </summary>
    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                {
                    foreach (object? obj in e.NewItems)
                    {
                        if (obj is MonitoredItemConfig cfg)
                        {
                            ItemStatuses.Add(new MonitoredItemStatusRow(cfg));
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                {
                    foreach (object? obj in e.OldItems)
                    {
                        if (obj is MonitoredItemConfig cfg)
                        {
                            for (int i = ItemStatuses.Count - 1; i >= 0; i--)
                            {
                                if (ItemStatuses[i].Id == cfg.Id)
                                {
                                    ItemStatuses.RemoveAt(i);
                                    break;
                                }
                            }
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                ItemStatuses.Clear();
                break;
        }
    }

    partial void OnShowItemStatusGridChanged(bool value)
    {
        if (value)
        {
            EnsureStatusTimerStarted();
            // Push an immediate refresh so the user sees populated rows
            // without waiting up to 250 ms after toggling the pane on.
            RefreshItemStatuses();
        }
        else
        {
            m_statusRefreshTimer?.Stop();
        }
    }

    private void EnsureStatusTimerStarted()
    {
        if (m_statusRefreshTimer is null)
        {
            m_statusRefreshTimer = new DispatcherTimer
            {
                Interval = s_statusRefreshInterval
            };
            m_statusRefreshTimer.Tick += (_, _) => RefreshItemStatuses();
        }
        if (!m_statusRefreshTimer.IsEnabled)
        {
            m_statusRefreshTimer.Start();
        }
    }

    /// <summary>
    /// Snapshot per-row state from the adapter's live-stats dictionary and
    /// the adapter's <see cref="ISubscriptionAdapter.Items"/> (for the
    /// server-confirmed Mode column).  Runs on the UI thread under the
    /// 250 ms timer.  No-op when unbound — rows keep their last-seen text.
    /// </summary>
    private void RefreshItemStatuses()
    {
        ISubscriptionAdapter? adapter = m_adapter;
        if (adapter is null || ItemStatuses.Count == 0)
        {
            return;
        }
        // Build a lookup from the adapter's confirmed configs so we can
        // pick up server-revised Mode / SamplingInterval / QueueSize.
        var confirmed = new Dictionary<int, MonitoredItemConfig>(adapter.Items.Count);
        foreach (MonitoredItemConfig c in adapter.Items)
        {
            confirmed[c.Id] = c;
        }
        foreach (MonitoredItemStatusRow row in ItemStatuses)
        {
            if (confirmed.TryGetValue(row.Id, out MonitoredItemConfig? cfg) && cfg is not null)
            {
                string mode = cfg.MonitoringMode.ToString();
                if (row.Mode != mode)
                {
                    row.Mode = mode;
                }
                string sampling = string.Format(CultureInfo.InvariantCulture,
                    "{0:0}ms", cfg.SamplingInterval.TotalMilliseconds);
                if (row.Sampling != sampling)
                {
                    row.Sampling = sampling;
                }
                string queue = cfg.QueueSize.ToString(CultureInfo.InvariantCulture);
                if (row.Queue != queue)
                {
                    row.Queue = queue;
                }
            }
            if (adapter.TryGetItemStats(row.Id, out MonitoredItemLiveStats? stats))
            {
                string samples = stats.Samples.ToString(CultureInfo.InvariantCulture);
                if (row.Samples != samples)
                {
                    row.Samples = samples;
                }
                if (stats.HasValue)
                {
                    string status = stats.LastStatus.ToString();
                    if (row.LastStatus != status)
                    {
                        row.LastStatus = status;
                    }
                    string value = stats.LastValueText;
                    if (row.LastValue != value)
                    {
                        row.LastValue = value;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Attach (or re-attach) an adapter to this tab.  Applies the current
    /// <see cref="Subscription"/> config and reapplies all
    /// <see cref="Items"/> against the new adapter so the tab resumes
    /// where it was before disconnect.  Called by MainViewModel when a
    /// new connection comes online and the tab was previously unbound.
    /// </summary>
    public async Task AttachAdapterAsync(ISubscriptionAdapter adapter)
    {
        m_adapter = adapter;
        OnPropertyChanged(nameof(Adapter));
        OnPropertyChanged(nameof(IsBound));
        try
        {
            await adapter.ApplySubscriptionAsync(Subscription, CancellationToken.None).ConfigureAwait(false);
            // Re-add previously-collected items against the fresh adapter.
            // The Id is server-assigned and changes on rebind.
            var snapshot = System.Linq.Enumerable.ToArray(Items);
            Dispatcher.UIThread.Post(() => Items.Clear());
            foreach (MonitoredItemConfig item in snapshot)
            {
                int id = await adapter.AddItemAsync(item with { Id = 0 }, CancellationToken.None).ConfigureAwait(false);
                MonitoredItemConfig assigned = item with { Id = id };
                Dispatcher.UIThread.Post(() => Items.Add(assigned));
            }
            RefreshStatus();
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Tab {Title} attach-adapter failed.", Title);
        }
    }

    /// <summary>
    /// Detach the current adapter (called on disconnect).  Keeps the
    /// <see cref="Items"/> collection populated as the user's intent so a
    /// later <see cref="AttachAdapterAsync"/> can restore them.
    /// </summary>
    public async ValueTask DetachAdapterAsync()
    {
        ISubscriptionAdapter? a = m_adapter;
        m_adapter = null;
        OnPropertyChanged(nameof(Adapter));
        OnPropertyChanged(nameof(IsBound));
        if (a is not null)
        {
            try
            {
                await a.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "Tab {Title} detach-adapter dispose failed.", Title);
            }
        }
        SubscriptionStatus = "● Disconnected — items preserved for reconnect.";
    }

    // ----- IPlugin members -----

    /// <summary>
    /// Subscription tabs render through the shared Subscription chart
    /// panel in <see cref="MainWindow"/> (which is rebound to the active
    /// Subscription tab on selection change).  We deliberately return
    /// <c>null</c> here so the right pane uses the shared chart Grid +
    /// AnimationCanvas + ScottPlotView instead of a per-tab control.
    /// </summary>
    Control? IPlugin.View => null;
    Control? IPlugin.HeaderToolbar => null;

    /// <summary>
    /// Single-line status for the bottom of the right pane.  Forwarded
    /// from <see cref="SubscriptionStatus"/>; <see cref="OnSubscriptionStatusChanged"/>
    /// raises PropertyChanged for "Status" so the binding refreshes.
    /// </summary>
    public string Status => SubscriptionStatus;

    public bool SupportsDuplicate => true;

    public IReadOnlyList<MenuItem> ContributeMenuItems() => Array.Empty<MenuItem>();

    public void OnActivated() { }
    public void OnDeactivated() { }

    partial void OnSubscriptionStatusChanged(string value)
        => OnPropertyChanged(nameof(Status));

    // ----- Commands -----

    [RelayCommand]
    private async Task ApplySubscriptionAsync(SubscriptionConfig newConfig)
    {
        Subscription = newConfig;
        if (m_adapter is null)
        {
            // Disconnected: just remember the new config; it'll be applied on AttachAdapterAsync.
            m_log.LogInformation("Tab {Title} stored subscription pub={Pub}ms (no live adapter).",
                Title, newConfig.PublishingInterval.TotalMilliseconds);
            return;
        }
        try
        {
            await m_adapter.ApplySubscriptionAsync(newConfig, CancellationToken.None).ConfigureAwait(false);
            m_log.LogInformation("Tab {Title} applied subscription pub={Pub}ms KA={KA} life={Life}",
                Title,
                newConfig.PublishingInterval.TotalMilliseconds,
                newConfig.KeepAliveCount,
                newConfig.LifetimeCount);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Apply subscription failed (tab {Title}).", Title);
        }
    }

    [RelayCommand]
    private async Task AddItemAsync(MonitoredItemConfig config)
    {
        if (m_adapter is null)
        {
            // Disconnected: remember the item locally so it gets applied on reconnect.
            Dispatcher.UIThread.Post(() => Items.Add(config));
            return;
        }
        try
        {
            int id = await m_adapter.AddItemAsync(config, CancellationToken.None).ConfigureAwait(false);
            MonitoredItemConfig assigned = config with { Id = id };
            Dispatcher.UIThread.Post(() => Items.Add(assigned));
            m_log.LogInformation("Tab {Title} added monitored item {Id} {DisplayName}", Title, id, config.DisplayName);
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "AddItem failed (tab {Title}).", Title);
        }
    }

    [RelayCommand]
    private async Task RemoveItemAsync(MonitoredItemConfig item)
    {
        if (m_adapter is null)
        {
            Dispatcher.UIThread.Post(() => Items.Remove(item));
            return;
        }
        try
        {
            await m_adapter.RemoveItemAsync(item.Id, CancellationToken.None).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => Items.Remove(item));
            m_log.LogInformation("Tab {Title} removed monitored item {Id}", Title, item.Id);
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "RemoveItem failed (tab {Title}).", Title);
        }
    }

    /// <summary>
    /// Right-click "Set monitoring mode →" handler on a status sub-pane row
    /// (B2).  Issues a SetMonitoringMode service call via the active
    /// adapter; on success the affected row's Mode column is refreshed
    /// inline (the 250 ms status timer would catch it too, this just
    /// avoids the perceptible lag).
    /// </summary>
    public async Task SetMonitoringModeAsync(MonitoredItemStatusRow row, MonitoringMode mode)
    {
        if (row is null)
        {
            return;
        }
        if (m_adapter is null)
        {
            // Disconnected: persist the user's intent into the local Items
            // collection so the next AttachAdapterAsync re-creates the item
            // with the chosen mode.
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id == row.Id)
                {
                    Items[i] = Items[i] with { MonitoringMode = mode };
                    break;
                }
            }
            row.Mode = mode.ToString();
            return;
        }
        try
        {
            await m_adapter.SetMonitoringModeAsync(row.Id, mode, CancellationToken.None)
                .ConfigureAwait(true);
            // Mirror the adapter's confirmed mode back into the VM's Items
            // collection so a later reconnect restores the new mode.
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id == row.Id)
                {
                    Items[i] = Items[i] with { MonitoringMode = mode };
                    break;
                }
            }
            row.Mode = mode.ToString();
            m_log.LogInformation("Tab {Title} monitored item {Id} mode -> {Mode}",
                Title, row.Id, mode);
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "SetMonitoringMode failed (tab {Title}, id {Id}, mode {Mode}).",
                Title, row.Id, mode);
        }
    }

    /// <summary>Bound to the per-row "Disabled" sub-menu item.</summary>
    [RelayCommand]
    private Task SetMonitoringModeDisabledAsync(MonitoredItemStatusRow row)
        => SetMonitoringModeAsync(row, MonitoringMode.Disabled);

    /// <summary>Bound to the per-row "Sampling" sub-menu item.</summary>
    [RelayCommand]
    private Task SetMonitoringModeSamplingAsync(MonitoredItemStatusRow row)
        => SetMonitoringModeAsync(row, MonitoringMode.Sampling);

    /// <summary>Bound to the per-row "Reporting" sub-menu item.</summary>
    [RelayCommand]
    private Task SetMonitoringModeReportingAsync(MonitoredItemStatusRow row)
        => SetMonitoringModeAsync(row, MonitoringMode.Reporting);

    /// <summary>
    /// Recomputes the status text from the adapter's current revised values.
    /// Called after ApplySubscription and on connect.  No-op when unbound.
    /// </summary>
    public void RefreshStatus()
    {
        if (m_adapter is null)
        {
            SubscriptionStatus = "● Not connected";
            return;
        }
        SubscriptionStatus = string.Format(CultureInfo.InvariantCulture,
            "● pub={0:0}ms / KA={1} / life={2}",
            m_adapter.CurrentPublishingInterval.TotalMilliseconds,
            m_adapter.CurrentKeepAliveCount,
            m_adapter.CurrentLifetimeCount);
    }

    public async ValueTask DisposeAsync()
    {
        Items.CollectionChanged -= OnItemsCollectionChanged;
        m_statusRefreshTimer?.Stop();
        m_statusRefreshTimer = null;
        ISubscriptionAdapter? a = m_adapter;
        m_adapter = null;
        if (a is null)
        {
            return;
        }

        try
        {
            await a.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Tab {Title} adapter dispose failed.", Title);
        }
    }
}
