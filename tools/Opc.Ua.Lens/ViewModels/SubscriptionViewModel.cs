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
        private readonly IWorkspaceDispatcher m_dispatcher;
        private ISubscriptionAdapter? m_adapter;
        // Sources are disposed by awaited capture/mutation drains and DisposeCoreAsync.
        // TODO: teach disposal analysis to follow these dispatcher-owned lifetime transfers.
#pragma warning disable CA2213
        private CancellationTokenSource? m_captureCancellation;
        private CancellationTokenSource m_mutationCancellation = new();
#pragma warning restore CA2213
        private Task? m_captureTask;
        private Task? m_disposal;
        private SubscriptionDocumentView? m_view;
        private int m_nextItemId;
        private int m_generation;
        private bool m_closed;
        private Task m_mutations = Task.CompletedTask;
        private Task m_detachment = Task.CompletedTask;

        [ObservableProperty]
        public partial string ErrorText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial int DisplayModeIndex { get; set; }

        /// <summary>
        /// The live adapter, or <c>null</c> when the tab was created in a
        /// disconnected state and not yet bound. Offline edits update retained
        /// intent. Bound via <see cref="AttachAdapterAsync"/> once a session is available.
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

        public SubscriptionViewModel(
            string title,
            ISubscriptionAdapter? adapter,
            ILogger log,
            IWorkspaceDispatcher? dispatcher = null)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            m_adapter = adapter;
            m_log = log ?? throw new ArgumentNullException(nameof(log));
            m_dispatcher = dispatcher ?? (Avalonia.Application.Current is null
                ? InlineWorkspaceDispatcher.Instance
                : new AvaloniaWorkspaceDispatcher());
            if (adapter is not null)
            {
                foreach (MonitoredItemConfig item in adapter.Items)
                {
                    m_nextItemId = Math.Max(m_nextItemId, item.Id);
                }
            }
            Items.CollectionChanged += OnItemsCollectionChanged;
            if (adapter is not null)
            {
                StartCapture(adapter);
            }
        }

        /// <summary>
        /// Mirrors item mutations into status rows on the document dispatcher.
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
                                m_nextItemId = Math.Max(m_nextItemId, cfg.Id);
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
                case NotifyCollectionChangedAction.Replace:
                    if (e.NewItems is not null)
                    {
                        foreach (object? value in e.NewItems)
                        {
                            if (value is MonitoredItemConfig config)
                            {
                                for (int index = 0; index < ItemStatuses.Count; index++)
                                {
                                    if (ItemStatuses[index].Id == config.Id)
                                    {
                                        ItemStatuses[index].Mode = config.MonitoringMode.ToString();
                                        ItemStatuses[index].Sampling = string.Format(CultureInfo.InvariantCulture,
                                            "{0:0}ms", config.SamplingInterval.TotalMilliseconds);
                                        ItemStatuses[index].Queue =
                                            config.QueueSize.ToString(CultureInfo.InvariantCulture);
                                        break;
                                    }
                                }
                            }
                        }
                    }
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
        public Task AttachAdapterAsync(
            ISubscriptionAdapter adapter,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            // The action must run even when cancelled so it can dispose the supplied adapter.
            return m_dispatcher.InvokeAsync(
                () => AttachCoreAsync(adapter, cancellationToken), CancellationToken.None);
        }

        /// <summary>
        /// Detach the current adapter (called on disconnect).  Keeps the
        /// <see cref="Items"/> collection populated as the user's intent so a
        /// later <see cref="AttachAdapterAsync"/> can restore them.
        /// </summary>
        public ValueTask DetachAdapterAsync()
        {
            return new(m_dispatcher.InvokeAsync(() => m_closed ? m_disposal ?? m_detachment : BeginDetach()));
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
        public Task ApplySubscriptionAsync(SubscriptionConfig newConfig)
        {
            ArgumentNullException.ThrowIfNull(newConfig);
            return m_dispatcher.InvokeAsync(() =>
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                Subscription = newConfig;
                return QueueMutation(
                    (adapter, token) => adapter.ApplySubscriptionAsync(newConfig, token), "Subscription settings");
            });
        }

        [RelayCommand]
        public Task AddItemAsync(MonitoredItemConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return m_dispatcher.InvokeAsync(() =>
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                MonitoredItemConfig assigned = config with { Id = checked(++m_nextItemId) };
                Items.Add(assigned);
                return QueueMutation(async (adapter, token) =>
                {
                    int id = await adapter.AddItemAsync(assigned, token).ConfigureAwait(true);
                    token.ThrowIfCancellationRequested();
                    if (id != assigned.Id)
                    {
                        throw new InvalidOperationException("The adapter changed the document item identity.");
                    }
                }, "Adding the monitored item");
            });
        }

        [RelayCommand]
        public Task RemoveItemAsync(MonitoredItemConfig item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return m_dispatcher.InvokeAsync(() =>
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                int index = FindItem(item.Id);
                if (index < 0)
                {
                    return Task.CompletedTask;
                }
                Items.RemoveAt(index);
                return QueueMutation((adapter, token) => adapter.RemoveItemAsync(item.Id, token),
                    "Removing the monitored item");
            });
        }

        public Task ConfigureItemAsync(
            MonitoredItemConfig item,
            MonitoredItemSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(settings);
            return m_dispatcher.InvokeAsync(() =>
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                cancellationToken.ThrowIfCancellationRequested();
                int index = FindItem(item.Id);
                if (index < 0)
                {
                    throw new InvalidOperationException("The monitored item is no longer in this document.");
                }
                MonitoredItemConfig updated = Items[index] with
                {
                    SamplingInterval = settings.SamplingInterval,
                    QueueSize = settings.QueueSize,
                    DiscardOldest = settings.DiscardOldest,
                    MonitoringMode = settings.MonitoringMode,
                    DataChangeFilter = settings.DataChangeFilter
                };
                Items[index] = updated;
                return QueueMutation((adapter, token) => adapter.ConfigureItemAsync(updated, token),
                    "Item settings", propagateFailure: true, cancellationToken: cancellationToken);
            }, cancellationToken);
        }

        /// <summary>
        /// Right-click "Set monitoring mode →" handler on a status sub-pane row
        /// (B2).  Issues a SetMonitoringMode service call via the active
        /// adapter; on success the affected row's Mode column is refreshed
        /// inline (the 250 ms status timer would catch it too, this just
        /// avoids the perceptible lag).
        /// </summary>
        public Task SetMonitoringModeAsync(MonitoredItemStatusRow row, MonitoringMode mode)
        {
            ArgumentNullException.ThrowIfNull(row);
            return m_dispatcher.InvokeAsync(() =>
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                int index = FindItem(row.Id);
                if (index < 0)
                {
                    return Task.CompletedTask;
                }
                Items[index] = Items[index] with { MonitoringMode = mode };
                row.Mode = mode.ToString();
                return QueueMutation((adapter, token) => adapter.SetMonitoringModeAsync(row.Id, mode, token),
                    "Monitoring mode");
            });
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
            return new(m_dispatcher.InvokeAsync(() => m_disposal ??= DisposeCoreAsync()));
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
            m_closed = true;
            m_view?.Dispose();
            Items.CollectionChanged -= OnItemsCollectionChanged;
            m_statusRefreshTimer?.Stop();
            m_statusRefreshTimer = null;
            try
            {
                await BeginDetach().ConfigureAwait(true);
            }
            finally
            {
                Recorder.Complete();
                m_mutationCancellation.Dispose();
            }
        }

        private int FindItem(int id)
        {
            for (int index = 0; index < Items.Count; index++)
            {
                if (Items[index].Id == id)
                {
                    return index;
                }
            }
            return -1;
        }

        private async Task AttachCoreAsync(ISubscriptionAdapter adapter, CancellationToken cancellationToken)
        {
            bool adopted = false;
            bool attached = false;
            try
            {
                ObjectDisposedException.ThrowIf(m_closed, this);
                cancellationToken.ThrowIfCancellationRequested();
                Task detachment = BeginDetach();
                int generation = m_generation;
                await detachment.ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                if (m_closed || generation != m_generation)
                {
                    return;
                }
                m_adapter = adapter;
                adopted = true;
                StartCapture(adapter);
                OnPropertyChanged(nameof(Adapter));
                OnPropertyChanged(nameof(IsBound));
                SubscriptionConfig subscription = Subscription;
                MonitoredItemConfig[] snapshot = [.. Items];
                await QueueMutation(async (current, token) =>
                {
                    await current.ApplySubscriptionAsync(subscription, token).ConfigureAwait(true);
                    foreach (MonitoredItemConfig item in snapshot)
                    {
                        token.ThrowIfCancellationRequested();
                        int id = await current.AddItemAsync(item, token).ConfigureAwait(true);
                        if (id != item.Id)
                        {
                            throw new InvalidOperationException("The adapter changed the document item identity.");
                        }
                    }
                }, "Restoring the monitor", propagateFailure: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(true);
                attached = true;
            }
            finally
            {
                if (!adopted)
                {
                    await adapter.DisposeAsync().ConfigureAwait(true);
                }
                else if (!attached && ReferenceEquals(m_adapter, adapter))
                {
                    await BeginDetach().ConfigureAwait(true);
                }
            }
        }

        /// <summary>
        /// Accepts local intent immediately; only server work is serialized. A disconnect
        /// cancels and drains this generation without undoing edits needed for reconnect.
        /// </summary>
        private Task QueueMutation(
            Func<ISubscriptionAdapter, CancellationToken, Task> action,
            string operation,
            bool propagateFailure = false,
            CancellationToken cancellationToken = default)
        {
            ErrorText = string.Empty;
            if (m_adapter is null)
            {
                return Task.CompletedTask;
            }
            CancellationToken generationToken = m_mutationCancellation.Token;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(generationToken, cancellationToken);
            Task previous = m_mutations;
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            m_mutations = drained.Task;
            return RunMutationAsync(previous, m_adapter, m_generation, action,
                operation, cancellation, propagateFailure, drained, generationToken);
        }

        private async Task RunMutationAsync(
            Task previous,
            ISubscriptionAdapter adapter,
            int generation,
            Func<ISubscriptionAdapter, CancellationToken, Task> action,
            string operation,
            CancellationTokenSource cancellation,
            bool propagateFailure,
            TaskCompletionSource drained,
            CancellationToken generationToken)
        {
            try
            {
                try
                {
                    await previous.ConfigureAwait(true);
                    // CancelAsync propagates to linked tokens asynchronously; the generation flag changes immediately.
                    generationToken.ThrowIfCancellationRequested();
                    cancellation.Token.ThrowIfCancellationRequested();
                    await action(adapter, cancellation.Token).ConfigureAwait(true);
                    generationToken.ThrowIfCancellationRequested();
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!m_closed && generation == m_generation)
                    {
                        RefreshStatus();
                    }
                }
                catch (OperationCanceledException) when (
                    generationToken.IsCancellationRequested || cancellation.IsCancellationRequested)
                {
                    if (propagateFailure)
                    {
                        throw;
                    }
                }
                catch (Exception) when (
                    generationToken.IsCancellationRequested || cancellation.IsCancellationRequested)
                {
                    if (propagateFailure)
                    {
                        throw new OperationCanceledException(
                            generationToken.IsCancellationRequested ? generationToken : cancellation.Token);
                    }
                }
                catch (Exception error)
                {
                    if (!m_closed && generation == m_generation)
                    {
                        ErrorText = $"{operation} failed: {error.Message}";
                        m_log.SubscriptionMutationFailed(error, Title, operation);
                    }
                    if (propagateFailure)
                    {
                        throw;
                    }
                }
            }
            finally
            {
                cancellation.Dispose();
                drained.TrySetResult();
            }
        }

        private Task BeginDetach()
        {
            ISubscriptionAdapter? adapter = m_adapter;
            m_adapter = null;
            m_generation++;
            CancellationTokenSource cancellation = m_mutationCancellation;
            m_mutationCancellation = new CancellationTokenSource();
            Task mutations = m_mutations;
            m_mutations = Task.CompletedTask;
            Task cancelling = cancellation.CancelAsync();
            Task capture = StopCaptureAsync();
            m_detachment = DrainAndDisposeAsync(m_detachment, mutations, cancelling, capture, adapter, cancellation);
            OnPropertyChanged(nameof(Adapter));
            OnPropertyChanged(nameof(IsBound));
            SubscriptionStatus = "● Disconnected — items preserved for reconnect.";
            return m_detachment;
        }

        private static async Task DrainAndDisposeAsync(
            Task previous,
            Task mutations,
            Task cancelling,
            Task capture,
            ISubscriptionAdapter? adapter,
            CancellationTokenSource cancellation)
        {
            try
            {
                await mutations.ConfigureAwait(false);
                await Task.WhenAll(previous, cancelling, capture).ConfigureAwait(false);
            }
            finally
            {
                cancellation.Dispose();
                if (adapter is not null)
                {
                    await adapter.DisposeAsync().ConfigureAwait(false);
                }
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
        [LoggerMessage(EventId = UaLensEventIds.SubscriptionViewModel + 9, Level = LogLevel.Error,
            Message = "Monitor {Title}: {Operation} failed.")]
        public static partial void SubscriptionMutationFailed(
            this ILogger logger, Exception exception, string title, string operation);

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
