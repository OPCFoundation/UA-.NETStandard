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
