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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Storage;
using UaLens.Subscriptions;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.ViewModels
{
    /// <summary>
    /// Per-tab subscription view model.  Each tab in the MainWindow's
    /// subscription tab strip owns one of these and one
    /// <see cref="ISubscriptionAdapter"/> (created via
    /// <c>ConnectionService.CreateAdapter</c>).  This view model holds the
    /// per-tab publishing config, the list of monitored items, and the
    /// commands that mutate them.
    /// </summary>
    internal sealed partial class SubscriptionViewModel : ObservableObject, IPlugin, IWorkspaceState
    {
        private readonly ILogger m_log;
        private ISubscriptionAdapter? m_adapter;
        private CancellationTokenSource? m_captureCancellation;
        private Task? m_captureTask;
        private Task? m_disposal;
        private SubscriptionDocumentView? m_view;
        private int m_nextOfflineItemId;

        [ObservableProperty]
        public partial string ErrorText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial int DisplayModeIndex { get; set; }

        /// <summary>
        /// The live adapter, or <c>null</c> when the tab was created in a
        /// disconnected state and not yet bound.  Commands that mutate the
        /// subscription early-return when null; the chart binders treat
        /// null as "no source".  Bound via <see cref="AttachAdapterAsync"/>
        /// once a session is available.
        /// </summary>
        public ISubscriptionAdapter? Adapter => m_adapter;

        /// <summary>
        /// True when this tab has an adapter wired to a live session.
        /// </summary>
        public bool IsBound => m_adapter is not null;

        public PluginKind Kind => PluginKind.Subscription;

        /// <summary>
        /// Tab header text.
        /// </summary>
        [ObservableProperty]
        public partial string Title { get; set; } = "Sub";

        /// <summary>
        /// True while the user is editing this tab's title inline.
        /// </summary>
        [ObservableProperty]
        public partial bool IsRenaming { get; set; }

        /// <summary>
        /// Per-(this tab, mode) cached ScottPlot axis limits — preserves the
        /// user's pan/zoom across mode switches so re-entering a mode restores
        /// the previously viewed framing.
        /// </summary>
        internal Dictionary<AnimationMode, ScottPlot.AxisLimits> AxisLimitsCache { get; }
            = [];

        /// <summary>
        /// Rolling buffer of recently received notifications for this tab.
        /// Captured by the document independently of chart visibility.
        /// </summary>
        public Connection.NotificationRecorder Recorder { get; }
            = new();

        /// <summary>
        /// Active publishing config for this tab.
        /// </summary>
        [ObservableProperty]
        public partial SubscriptionConfig Subscription { get; set; } = new();

        /// <summary>
        /// Status string rendered under the animation.
        /// </summary>
        [ObservableProperty]
        public partial string SubscriptionStatus { get; set; } = "● No subscription";

        /// <summary>
        /// Monitored items currently subscribed in this tab.
        /// </summary>
        public ObservableCollection<MonitoredItemConfig> Items { get; } = [];

        /// <summary>
        /// Per-monitored-item status rows for the B1 status sub-pane.  Kept
        /// in lock-step with <see cref="Items"/>: rows are added/removed by
        /// <see cref="OnItemsCollectionChanged"/> and dynamic columns
        /// (Mode / Samples / Last status / Last value) are refreshed by the
        /// 250 ms <see cref="m_statusRefreshTimer"/> from the adapter's
        /// live-stats dictionary.
        /// </summary>
        public ObservableCollection<MonitoredItemStatusRow> ItemStatuses { get; } = [];

        /// <summary>
        /// Toggles visibility of the per-item status sub-pane.  Persists per
        /// tab (each <see cref="SubscriptionViewModel"/> tracks its own
        /// preference).  When true the VM also drives a 250 ms status
        /// refresh timer; when false the timer stays parked.
        /// </summary>
        [ObservableProperty]
        public partial bool ShowItemStatusGrid { get; set; } = true;

        /// <summary>
        /// Chart legend visibility (ScottPlot signal/histogram/heatmap modes).
        /// Defaults to false — chart is busy enough without legend / axis chrome;
        /// the user can opt-in via the per-tab checkboxes.
        /// </summary>
        [ObservableProperty]
        public partial bool ShowLegend { get; set; }

        /// <summary>
        /// Chart X-axis visibility (labels + ticks; bottom axis).
        /// Defaults to false — see <see cref="ShowLegend"/>.
        /// </summary>
        [ObservableProperty]
        public partial bool ShowXAxis { get; set; }

        /// <summary>
        /// Chart Y-axis visibility (labels + ticks; left axis).
        /// Defaults to false — see <see cref="ShowLegend"/>.
        /// </summary>
        [ObservableProperty]
        public partial bool ShowYAxis { get; set; }

        /// <summary>
        /// 250 ms throttle for status sub-pane refresh.
        /// </summary>
        private static readonly TimeSpan s_statusRefreshInterval
            = TimeSpan.FromMilliseconds(250);

        private DispatcherTimer? m_statusRefreshTimer;

        /// <summary>
        /// Currently-selected item (mostly for display / future actions).
        /// </summary>
        [ObservableProperty]
        public partial MonitoredItemConfig? SelectedItem { get; set; }

        /// <summary>
        /// Per-tab animation view-mode (Dots / Bars / Lines).  Each tab keeps
        /// its own choice so switching tabs restores the user's preferred
        /// visualisation for that subscription.  Default is
        /// <see cref="AnimationMode.Dots"/>.
        /// </summary>
        [ObservableProperty]
        public partial AnimationMode AnimationMode { get; set; } = AnimationMode.Dots;

        /// <summary>
        /// Per-tab time-axis stretch factor for the AnimationCanvas.  1.0 =
        /// default; doubled by [+] / Ctrl-+, halved by [-] / Ctrl--, reset to
        /// 1.0 by Ctrl-0.  Clamped 0.125..8 in the UI handlers.
        /// </summary>
        [ObservableProperty]
        public partial double AnimationTimeScale { get; set; } = 1.0;

        /// <summary>
        /// Per-tab toggle for the CPU / memory overlay rendered on top of the
        /// animation canvas.  Off by default.
        /// </summary>
        [ObservableProperty]
        public partial bool ShowResourceOverlay { get; set; }

        public SubscriptionViewModel(string title, ISubscriptionAdapter? adapter, ILogger log)
        {
            Title = title;
            m_adapter = adapter;
            m_log = log;
            Items.CollectionChanged += OnItemsCollectionChanged;
            if (adapter is not null)
            {
                StartCapture(adapter);
            }
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
                    MonitoredItemSample snapshot = stats.Snapshot();
                    string samples = snapshot.Samples.ToString(CultureInfo.InvariantCulture);
                    if (row.Samples != samples)
                    {
                        row.Samples = samples;
                    }
                    if (snapshot.HasValue)
                    {
                        string status = snapshot.Status.ToString();
                        if (row.LastStatus != status)
                        {
                            row.LastStatus = status;
                        }
                        string value = snapshot.Value;
                        if (row.LastValue != value)
                        {
                            row.LastValue = value;
                        }
                        row.SourceTimestamp = snapshot.SourceTimestamp.IsNull
                            ? "--" : snapshot.SourceTimestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        row.ServerTimestamp = snapshot.ServerTimestamp.IsNull
                            ? "--" : snapshot.ServerTimestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    }
                }
                if (adapter.TryGetItemResult(row.Id, out MonitoredItemResult result))
                {
                    if (StatusCode.IsBad(result.Status))
                    {
                        row.LastStatus = result.Status.ToString();
                    }
                    else if (!result.Created || result.Pending)
                    {
                        row.LastStatus = result.Created ? "Applying settings" : "Creating item";
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
        public async Task AttachAdapterAsync(
            ISubscriptionAdapter adapter,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            bool adopted = false;
            bool attached = false;
            try
            {
                ObjectDisposedException.ThrowIf(m_disposal is not null, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (m_adapter is not null)
                {
                    await DetachAdapterAsync().ConfigureAwait(true);
                }
                m_adapter = adapter;
                adopted = true;
                StartCapture(adapter);
                OnPropertyChanged(nameof(Adapter));
                OnPropertyChanged(nameof(IsBound));
                await adapter.ApplySubscriptionAsync(Subscription, cancellationToken).ConfigureAwait(true);
                MonitoredItemConfig[] snapshot = System.Linq.Enumerable.ToArray(Items);
                var assigned = new List<MonitoredItemConfig>(snapshot.Length);
                foreach (MonitoredItemConfig item in snapshot)
                {
                    int id = await adapter.AddItemAsync(item with { Id = 0 }, cancellationToken).ConfigureAwait(true);
                    assigned.Add(item with { Id = id });
                }
                cancellationToken.ThrowIfCancellationRequested();
                Items.Clear();
                foreach (MonitoredItemConfig item in assigned)
                {
                    Items.Add(item);
                }
                attached = true;
                RefreshStatus();
            }
            finally
            {
                if (!attached)
                {
                    if (adopted)
                    {
                        await DetachAdapterAsync().ConfigureAwait(true);
                    }
                    else
                    {
                        await adapter.DisposeAsync().ConfigureAwait(true);
                    }
                }
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
            try
            {
                await StopCaptureAsync().ConfigureAwait(true);
            }
            finally
            {
                if (a is not null)
                {
                    await a.DisposeAsync().ConfigureAwait(true);
                }
            }
            SubscriptionStatus = "● Disconnected — items preserved for reconnect.";
        }

        /// <summary>
        /// Retains this document's view across activation changes. Charts use
        /// independent recorder cursors while the document owns adapter capture.
        /// </summary>
        Control? IPlugin.View => m_view ??= new SubscriptionDocumentView(this);
        Control? IPlugin.HeaderToolbar => null;

        /// <summary>
        /// Single-line status for the bottom of the right pane.  Forwarded
        /// from <see cref="SubscriptionStatus"/>; <see cref="OnSubscriptionStatusChanged"/>
        /// raises PropertyChanged for "Status" so the binding refreshes.
        /// </summary>
        public string Status => SubscriptionStatus;

        public bool SupportsDuplicate => true;

        public IReadOnlyList<MenuItem> ContributeMenuItems()
        {
            return [];
        }

        public void OnActivated()
        {
            if (ShowItemStatusGrid)
            {
                EnsureStatusTimerStarted();
                RefreshItemStatuses();
            }
        }

        public void OnDeactivated()
        {
            m_statusRefreshTimer?.Stop();
        }

        partial void OnSubscriptionStatusChanged(string value)
            => OnPropertyChanged(nameof(Status));

        [RelayCommand]
        private async Task ApplySubscriptionAsync(SubscriptionConfig newConfig)
        {
            ErrorText = string.Empty;
            // The Subscription setter fires PropertyChanged synchronously, so it
            // (and every other binding-touching mutation in this method) MUST
            // run on the UI thread.  Marshal explicitly so this works regardless
            // of how the caller awaited us (ConfigureAwait(true) vs false).
            if (!Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(() => Subscription = newConfig);
            }
            else
            {
                Subscription = newConfig;
            }
            if (m_adapter is null)
            {
                // Disconnected: just remember the new config; it'll be applied on AttachAdapterAsync.
                m_log.SubscriptionTabSettingsStored(Title, newConfig.PublishingInterval.TotalMilliseconds);
                return;
            }
            try
            {
                await m_adapter.ApplySubscriptionAsync(newConfig, CancellationToken.None).ConfigureAwait(false);
                m_log.SubscriptionTabSettingsApplied(
                    Title,
                    newConfig.PublishingInterval.TotalMilliseconds,
                    newConfig.KeepAliveCount,
                    newConfig.LifetimeCount);
                Dispatcher.UIThread.Post(RefreshStatus);
            }
            catch (Exception ex)
            {
                ErrorText = $"Subscription settings failed: {ex.Message}";
                m_log.SubscriptionTabSettingsFailed(ex, Title);
            }
        }

        [RelayCommand]
        private async Task AddItemAsync(MonitoredItemConfig config)
        {
            ErrorText = string.Empty;
            if (m_adapter is null)
            {
                MonitoredItemConfig local = config with { Id = --m_nextOfflineItemId };
                await Dispatcher.UIThread.InvokeAsync(() => Items.Add(local));
                return;
            }
            try
            {
                int id = await m_adapter.AddItemAsync(config, CancellationToken.None).ConfigureAwait(false);
                MonitoredItemConfig assigned = config with { Id = id };
                await Dispatcher.UIThread.InvokeAsync(() => Items.Add(assigned));
                m_log.SubscriptionTabItemAdded(Title, id, config.DisplayName);
            }
            catch (Exception ex)
            {
                ErrorText = $"Adding the monitored item failed: {ex.Message}";
                m_log.SubscriptionTabAddItemFailed(ex, Title);
            }
        }

        [RelayCommand]
        private async Task RemoveItemAsync(MonitoredItemConfig item)
        {
            ErrorText = string.Empty;
            if (m_adapter is null)
            {
                Dispatcher.UIThread.Post(() => Items.Remove(item));
                return;
            }
            try
            {
                await m_adapter.RemoveItemAsync(item.Id, CancellationToken.None).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => Items.Remove(item));
                m_log.SubscriptionTabItemRemoved(Title, item.Id);
            }
            catch (Exception ex)
            {
                ErrorText = $"Removing the monitored item failed: {ex.Message}";
                m_log.SubscriptionTabRemoveItemFailed(ex, Title);
            }
        }

        public async Task ConfigureItemAsync(
            MonitoredItemConfig item,
            MonitoredItemSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(settings);
            int index = Items.IndexOf(item);
            if (index < 0)
            {
                throw new InvalidOperationException("The monitored item is no longer in this document.");
            }
            MonitoredItemConfig updated = item with
            {
                SamplingInterval = settings.SamplingInterval,
                QueueSize = settings.QueueSize,
                DiscardOldest = settings.DiscardOldest,
                MonitoringMode = settings.MonitoringMode,
                DataChangeFilter = settings.DataChangeFilter
            };
            if (m_adapter is not null)
            {
                await m_adapter.ConfigureItemAsync(updated, cancellationToken).ConfigureAwait(true);
            }
            Items[index] = updated;
            RefreshItemStatuses();
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
                m_log.SubscriptionTabMonitoringModeChanged(Title, row.Id, mode);
            }
            catch (Exception ex)
            {
                m_log.SubscriptionTabSetMonitoringModeFailed(ex, Title, row.Id, mode);
            }
        }

        /// <summary>
        /// Bound to the per-row "Disabled" sub-menu item.
        /// </summary>
        [RelayCommand]
        private Task SetMonitoringModeDisabledAsync(MonitoredItemStatusRow row)
        {
            return SetMonitoringModeAsync(row, MonitoringMode.Disabled);
        }

        /// <summary>
        /// Bound to the per-row "Sampling" sub-menu item.
        /// </summary>
        [RelayCommand]
        private Task SetMonitoringModeSamplingAsync(MonitoredItemStatusRow row)
        {
            return SetMonitoringModeAsync(row, MonitoringMode.Sampling);
        }

        /// <summary>
        /// Bound to the per-row "Reporting" sub-menu item.
        /// </summary>
        [RelayCommand]
        private Task SetMonitoringModeReportingAsync(MonitoredItemStatusRow row)
        {
            return SetMonitoringModeAsync(row, MonitoringMode.Reporting);
        }

        /// <summary>
        /// Recomputes the status text from the adapter's current revised values.
        /// Called after ApplySubscription and on connect.  No-op when unbound.
        /// </summary>
        public void RefreshStatus()
        {
            if (m_captureTask is { IsFaulted: true, Exception: { } failure })
            {
                SubscriptionStatus = $"Notification capture failed: {failure.GetBaseException().Message}";
                return;
            }
            if (m_adapter is null)
            {
                SubscriptionStatus = "● Not connected";
                return;
            }
            SubscriptionStatus = string.Format(CultureInfo.InvariantCulture,
                "Publishing {0:0} ms / keep-alive {1} / lifetime {2} / history retired {3}",
                m_adapter.CurrentPublishingInterval.TotalMilliseconds,
                m_adapter.CurrentKeepAliveCount,
                m_adapter.CurrentLifetimeCount,
                Recorder.HistoryDiscarded);
        }

        public ValueTask DisposeAsync()
        {
            return new(m_disposal ??= DisposeCoreAsync());
        }

        public JsonElement CaptureState()
        {
            return JsonSerializer.SerializeToElement(
                        SubscriptionDocumentState.Capture(this).Export(),
                        Connection.SessionFileJsonContext.Default.TabSnapshot);
        }

        public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
        {
            Connection.SessionFile.TabSnapshot snapshot = state.Deserialize(
                Connection.SessionFileJsonContext.Default.TabSnapshot)
                ?? throw new JsonException("Monitor configuration cannot be null.");
            return SubscriptionDocumentState.Import(snapshot).ApplyToAsync(this, cancellationToken);
        }

        private async Task DisposeCoreAsync()
        {
            m_view?.Dispose();
            Items.CollectionChanged -= OnItemsCollectionChanged;
            m_statusRefreshTimer?.Stop();
            m_statusRefreshTimer = null;
            try
            {
                await DetachAdapterAsync().ConfigureAwait(true);
            }
            finally
            {
                Recorder.Complete();
            }
        }

        private void StartCapture(ISubscriptionAdapter adapter)
        {
            m_captureCancellation = new CancellationTokenSource();
            m_captureTask = Recorder.CaptureAsync(adapter.Events, m_captureCancellation.Token);
        }

        private async Task StopCaptureAsync()
        {
            CancellationTokenSource? cancellation = m_captureCancellation;
            Task? capture = m_captureTask;
            m_captureCancellation = null;
            m_captureTask = null;
            if (cancellation is null)
            {
                return;
            }
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                if (capture is not null)
                {
                    await capture.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }

    internal static partial class SubscriptionViewModelLog
    {
        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 0, Level = LogLevel.Information,
            Message = "Tab {Title} stored subscription pub={Pub}ms (no live adapter).")]
        public static partial void SubscriptionTabSettingsStored(this ILogger logger, string title, double pub);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 1, Level = LogLevel.Information,
            Message = "Tab {Title} applied subscription pub={Pub}ms KA={KA} life={Life}")]
        public static partial void SubscriptionTabSettingsApplied(
            this ILogger logger, string title, double pub, uint ka, uint life);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 2, Level = LogLevel.Error,
            Message = "Apply subscription failed (tab {Title}).")]
        public static partial void SubscriptionTabSettingsFailed(
            this ILogger logger, Exception exception, string title);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 3, Level = LogLevel.Information,
            Message = "Tab {Title} added monitored item {Id} {DisplayName}")]
        public static partial void SubscriptionTabItemAdded(
            this ILogger logger, string title, int id, string displayName);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 4, Level = LogLevel.Error,
            Message = "AddItem failed (tab {Title}).")]
        public static partial void SubscriptionTabAddItemFailed(this ILogger logger, Exception exception, string title);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 5, Level = LogLevel.Information,
            Message = "Tab {Title} removed monitored item {Id}")]
        public static partial void SubscriptionTabItemRemoved(this ILogger logger, string title, int id);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 6, Level = LogLevel.Error,
            Message = "RemoveItem failed (tab {Title}).")]
        public static partial void SubscriptionTabRemoveItemFailed(
            this ILogger logger, Exception exception, string title);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 7, Level = LogLevel.Information,
            Message = "Tab {Title} monitored item {Id} mode -> {Mode}")]
        public static partial void SubscriptionTabMonitoringModeChanged(
            this ILogger logger, string title, int id, MonitoringMode mode);

        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 8, Level = LogLevel.Error,
            Message = "SetMonitoringMode failed (tab {Title}, id {Id}, mode {Mode}).")]
        public static partial void SubscriptionTabSetMonitoringModeFailed(
            this ILogger logger, Exception exception, string title, int id, MonitoringMode mode);
    }
}
