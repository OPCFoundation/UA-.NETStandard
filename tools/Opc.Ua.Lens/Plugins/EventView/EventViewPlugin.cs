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
using UaLens.ViewModels;
using UaLens.Views;
using ClassicMonitoredItem = Opc.Ua.Client.MonitoredItem;
using ClassicMonitoredItemOptions = Opc.Ua.Client.MonitoredItemOptions;
using ClassicSubscription = Opc.Ua.Client.Subscription;
using ClassicSubscriptionOptions = Opc.Ua.Client.SubscriptionOptions;

namespace UaLens.Plugins.EventView;

/// <summary>
/// One configured event source attached to the Event View tab.  Wraps
/// the OPC UA <see cref="ClassicMonitoredItem"/> registered on the
/// per-tab event subscription plus the display metadata shown in the
/// left-hand sources panel.
/// </summary>
internal sealed partial class EventSourceVm : ObservableObject
{
    public NodeId NodeId { get; }
    public string Name { get; }

    /// <summary>
    /// The live monitored item bound to the current subscription generation, or
    /// null while the tab is offline or between reconnects. The source's NodeId
    /// and name are the persistent configuration; the item is (re)created on each
    /// install so a reconnect never leaves a stale, dead handle behind.
    /// </summary>
    internal ClassicMonitoredItem? MonitoredItem { get; set; }

    [ObservableProperty]
    private string m_state = "pending";

    /// <summary>
    /// Set when client-side ApplyChanges throws (transport error, session
    /// closed) so that <see cref="RefreshState"/> doesn't overwrite the
    /// failure message on subsequent publishes.
    /// </summary>
    internal string? CreationFailure { get; set; }

    public EventSourceVm(NodeId nodeId, string name)
    {
        NodeId = nodeId;
        Name = name;
    }

    /// <summary>
    /// Refreshes the per-source state string from the monitored item's
    /// current Status (created vs. bad-status vs. filter rejected).
    /// Called by the host plug-in after ApplyChanges and on every
    /// publish/keep-alive so the user sees the live state without
    /// having to dig through SDK logs.
    /// </summary>
    internal void RefreshState()
    {
        if (CreationFailure is { } fail)
        {
            State = fail;
            return;
        }
        ClassicMonitoredItem? item = MonitoredItem;
        if (item is null)
        {
            State = "○ offline";
            return;
        }
        var st = item.Status;
        if (st.Error is { } err && ServiceResult.IsBad(err))
        {
            State = $"BAD: {err.StatusCode}";
            return;
        }
        if (!st.Created)
        {
            State = "pending";
            return;
        }
        State = "✓ created";
    }
}

/// <summary>
/// View model for an Event View tab. Owns a per-tab event subscription — a classic
/// <see cref="ClassicSubscription"/> on the managed session — whose
/// <c>FastEventCallback</c> surfaces the underlying <see cref="EventFieldList"/> data.
/// The subscription is installed, rebound and released through the awaited connection
/// lifecycle so a reconnect never leaves a stale handle, duplicate reader, or hidden
/// failure behind. Pausing the display never stops collection or server publishing.
/// </summary>
internal sealed partial class EventViewPlugin : ObservableObject, IPlugin, IWorkspaceState
{
    private static int s_number;

    private const int MaxLogEntries = 2000;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;

    /// <summary>
    /// Serializes every subscription mutation — install, rebind, release, add, remove
    /// and filter changes — so the awaited connection lifecycle and UI commands never
    /// interleave into duplicate readers or a half-installed subscription.
    /// </summary>
    private readonly SemaphoreSlim m_gate = new(1, 1);

    /// <summary>UI-thread-only. Events collected while the display is paused.</summary>
    private readonly List<EventLogEntry> m_pausedBuffer = [];

    // CA2213: m_subscription is disposed via ReleaseSubscriptionAsync (from
    // DisposeCoreAsync), but the analyzer can't see the lifetime through the gate.
#pragma warning disable CA2213
    private ClassicSubscription? m_subscription;
#pragma warning restore CA2213
    private SimpleAttributeOperand[] m_selectClauses;
    private string[] m_selectPaths;
    private ContentFilter? m_whereClause;
    private long m_eventCount;
    private long m_droppedCount;
    private long m_installedGeneration = -1;
    private bool m_closed;
    private Task? m_disposal;
    private EventViewView? m_view;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private bool m_isPaused;

    [ObservableProperty]
    private bool m_isOffline = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private EventFilterConfig m_filter = new(
        SeverityThreshold: 0,
        Fields:
        [
            "EventId",
            "EventType",
            "SourceName",
            "Time",
            "Message",
            "Severity"
        ]);

    /// <summary>
    /// One-line summary of the active event filter shown next to the Filter… button so
    /// the current selection is visible without opening the detailed editor.
    /// </summary>
    public string FilterSummary
    {
        get
        {
            EventFilterConfig filter = Filter;
            string type = filter.EventTypeNodeId is { IsNull: false } typeId
                ? typeId.ToString() ?? "BaseEventType"
                : "BaseEventType";
            int where = filter.WhereClause is { } clause ? clause.Elements.Count : 0;
            return string.Format(CultureInfo.InvariantCulture,
                "Severity ≥ {0} · {1} field{2} · {3}{4}",
                filter.SeverityThreshold,
                filter.Fields.Count,
                filter.Fields.Count == 1 ? string.Empty : "s",
                type,
                where > 0 ? $" · where ({where})" : " · no where clause");
        }
    }

    [ObservableProperty]
    private EventLogEntry? m_selectedEntry;

    [ObservableProperty]
    private string m_status = "● 0 sources · 0 events";

    [ObservableProperty]
    private string m_subscriptionStatus = "○ Subscription: not created";

    /// <summary>UI-thread-only.  Newest entries inserted at index 0.</summary>
    public ObservableCollection<EventLogEntry> Events { get; } = new();

    /// <summary>UI-thread-only.  Sources displayed in the left panel.</summary>
    public ObservableCollection<EventSourceVm> EventSources { get; } = new();

    public EventViewPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        m_title = string.Create(CultureInfo.InvariantCulture, $"Event View {Interlocked.Increment(ref s_number)}");

        (m_selectClauses, m_selectPaths) = BuildSelectClauses(m_filter);

        // The per-tab subscription is installed through the awaited connection
        // lifecycle (OnConnectionStateChangedAsync) or on first AddSource — never a
        // fire-and-forget constructor, which hid failures and raced with disposal.
        RefreshStatus();
    }

    // ----- IPlugin members -----

    public PluginKind Kind => PluginKind.EventView;

    Control? IPlugin.View => m_view ??= new EventViewView { DataContext = this };
    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public void OnActivated() { }
    public void OnDeactivated() { }

    /// <summary>
    /// Awaited connection lifecycle. Installs the per-tab subscription against a new
    /// session generation, leaves an existing subscription untouched across a
    /// transport-only reconnect (same generation), and releases every owned handle
    /// when the primary session is gone. The old session stays alive until this
    /// returns, so the release deletes the subscription rather than orphaning it.
    /// </summary>
    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (m_closed)
            {
                return;
            }
            await SynchronizeSubscriptionAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            m_gate.Release();
        }
    }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        var addSrc = new MenuItem { Header = "_Add Source\u2026" };
        var removeSrc = new MenuItem { Header = "_Remove Source\u2026" };
        var editFilter = new MenuItem { Header = "Edit _Filter\u2026" };
        var clear = new MenuItem { Header = "_Clear Log" };
        var pause = new MenuItem
        {
            Header = "_Pause Display",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = IsPaused
        };

        addSrc.Click += async (_, _) => await AddSourceAsync().ConfigureAwait(true);
        removeSrc.Click += async (_, _) => await RemoveSourceAsync(SelectedSource()).ConfigureAwait(true);
        editFilter.Click += async (_, _) => await EditFilterAsync().ConfigureAwait(true);
        clear.Click += (_, _) => ClearLog();
        pause.Click += (_, _) =>
        {
            IsPaused = !IsPaused;
            pause.IsChecked = IsPaused;
        };

        return [addSrc, removeSrc, editFilter, clear, pause];
    }

    public ValueTask DisposeAsync() => new(m_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        await m_gate.WaitAsync().ConfigureAwait(true);
        try
        {
            m_closed = true;
            await ReleaseSubscriptionAsync().ConfigureAwait(true);
        }
        finally
        {
            m_gate.Release();
            m_gate.Dispose();
        }
    }

    // ----- Commands -----

    /// <summary>
    /// <summary>
    /// Spawns an "Add Source" flow.  Prefers the currently-selected
    /// address-space node when it's an event-emitting Object/View; falls
    /// back to <see cref="BrowsePickerDialog"/> otherwise (covers the
    /// case where the address-space view is hidden or nothing matching
    /// is selected).
    /// </summary>
    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            m_log.AddSourceNotConnected();
            return;
        }

        // The per-tab subscription is created on demand under the gate by
        // AddSourceCoreAsync, so no readiness wait is needed here.

        // If the user already has a valid event-emitting Object/View
        // selected in the address-space tree, accept it directly without
        // popping a picker.
        NodeViewModel? node = m_host.Workspace.SelectedNode;
        bool valid = node is not null
            && node.NodeClass is NodeClass.Object or NodeClass.View;
        if (valid)
        {
            byte? notifier = await m_host.Browser
                .GetEventNotifierAsync(node!.NodeId, CancellationToken.None)
                .ConfigureAwait(true);
            if (notifier is null || (notifier.Value & EventNotifiers.SubscribeToEvents) == 0)
            {
                valid = false;
            }
        }

        if (valid)
        {
            await AddSourceCoreAsync(node!.NodeId, node.Text).ConfigureAwait(true);
            return;
        }

        // Fallback: prompt via BrowsePickerDialog rooted at ObjectsFolder.
        Window? owner = TopLevelWindow();
        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: session,
            Root: ObjectIds.ObjectsFolder,
            Title: "Pick event source",
            AcceptedClasses: NodeClass.Object | NodeClass.View,
            AcceptPredicate: async (id, _) =>
            {
                byte? n = await m_host.Browser.GetEventNotifierAsync(id, CancellationToken.None).ConfigureAwait(true);
                return n is not null && (n.Value & EventNotifiers.SubscribeToEvents) != 0;
            },
            Header: "Pick an Object or View that emits events (EventNotifier has SubscribeToEvents)."));
        NodeId? pickedId = owner is null
            ? await picker.ShowDialog<NodeId?>(new Window()).ConfigureAwait(true)
            : await picker.ShowDialog<NodeId?>(owner).ConfigureAwait(true);
        if (!pickedId.HasValue || pickedId.Value.IsNull)
        {
            return;
        }
        await AddSourceCoreAsync(pickedId.Value, picker.PickedDisplay).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveSourceAsync(EventSourceVm? source)
    {
        if (source is null)
        {
            return;
        }
        await m_gate.WaitAsync().ConfigureAwait(true);
        try
        {
            ClassicSubscription? sub = m_subscription;
            ClassicMonitoredItem? item = source.MonitoredItem;
            if (sub is not null && item is not null)
            {
                sub.RemoveItem(item);
                await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(true);
            }
            source.MonitoredItem = null;
            EventSources.Remove(source);
            RefreshStatus();
            m_log.SourceRemoved(source.Name, source.NodeId);
        }
        catch (Exception ex)
        {
            m_log.RemoveSourceFailed(ex, source.NodeId);
        }
        finally
        {
            m_gate.Release();
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Events.Clear();
            SelectedEntry = null;
            Interlocked.Exchange(ref m_eventCount, 0);
            Interlocked.Exchange(ref m_droppedCount, 0);
            RefreshStatus();
        });
    }

    [RelayCommand]
    private async Task EditFilterAsync()
    {
        Window? owner = TopLevelWindow();
        // Hand the live session to the dialog so its "Pick type…"
        // button can browse subtypes of BaseEventType and discover
        // fields in-place.  When disconnected the button is disabled.
        var dlg = new EventFilterDialog(Filter, m_host.Connection.Session);
        EventFilterConfig? result;
        if (owner is not null)
        {
            result = await dlg.ShowDialog<EventFilterConfig?>(owner).ConfigureAwait(true);
        }
        else
        {
            dlg.Show();
            return;
        }
        if (result is null)
        {
            return;
        }
        Filter = result;
        await ApplyFilterAsync(result).ConfigureAwait(true);
    }

    [RelayCommand]
    private void PauseStream()
    {
        IsPaused = !IsPaused;
    }

    // Opens a flat-browse picker over the address space filtered to nodes
    // with at least one outgoing GeneratesEvent reference, then opens the
    // method-call or write-value dialog depending on the picked node class.
    // No hardcoded NodeIds — works against any server that declares
    // GeneratesEvent references for its event-source surface.
    [RelayCommand]
    private async Task TriggerEventAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            m_log.TriggerNotConnected();
            return;
        }

        Window? owner = TopLevelWindow();
        var options = new BrowsePickerDialog.Options(
            Session: session,
            Root: ObjectIds.ObjectsFolder,
            Title: "Trigger event source",
            AcceptedClasses: NodeClass.Method | NodeClass.Variable | NodeClass.Object,
            ReferenceTypeId: ReferenceTypeIds.HierarchicalReferences,
            AcceptPredicate: (id, _) => HasGeneratesEventAsync(session, id),
            Header: "Pick a Method (Call) or Variable (Write) whose " +
                "GeneratesEvent reference fires a server-side event.");

        var dlg = new FlattenedBrowseDialog(options);
        NodeId? picked = owner is null
            ? await dlg.ShowDialog<NodeId?>(new Window()).ConfigureAwait(true)
            : await dlg.ShowDialog<NodeId?>(owner).ConfigureAwait(true);
        if (picked is null || picked.Value.IsNull || dlg.PickedItem is null)
        {
            return;
        }

        FlattenedNode item = dlg.PickedItem;
        var node = new NodeViewModel(
            m_host.Browser, NodeId.Null, item.NodeId, item.DisplayName, item.NodeClass);

        if (item.NodeClass == NodeClass.Method)
        {
            var callDlg = new MethodCallDialog(node, session);
            if (owner is not null)
            {
                await callDlg.ShowDialog(owner).ConfigureAwait(true);
            }
            else
            {
                callDlg.Show();
            }
        }
        else if (item.NodeClass == NodeClass.Variable)
        {
            var writeDlg = new WriteValueDialog(node, session);
            if (owner is not null)
            {
                await writeDlg.ShowDialog(owner).ConfigureAwait(true);
            }
            else
            {
                writeDlg.Show();
            }
        }
        else
        {
            m_log.TriggerNotActionable(item.NodeId, item.NodeClass);
        }
    }

    private static async Task<bool> HasGeneratesEventAsync(ManagedSession session, NodeId id)
    {
        try
        {
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = id,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.GeneratesEvent,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.None
                }
            };
            BrowseResponse br = await session
                .BrowseAsync(null, null, 1, browse, CancellationToken.None)
                .ConfigureAwait(false);
            return br.Results.Count > 0
                && !StatusCode.IsBad(br.Results[0].StatusCode)
                && br.Results[0].References.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    // ----- Wiring helpers -----

    /// <summary>
    /// Reconciles the subscription with the current connection, assuming the gate is
    /// held. A missing session releases the subscription; a new session generation
    /// reinstalls it; the same generation leaves the transport-reconnected
    /// subscription in place so its monitored items are not needlessly recreated.
    /// </summary>
    private async Task SynchronizeSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (m_host.Connection.Session is not { } session)
        {
            await ReleaseSubscriptionAsync().ConfigureAwait(true);
            RefreshStatus();
            return;
        }
        long generation = m_host.Connection.Snapshot.Generation;
        if (m_subscription is not null && m_installedGeneration == generation)
        {
            RefreshStatus();
            return;
        }
        await InstallSubscriptionAsync(session, generation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Ensures a subscription exists for the current session generation, assuming the
    /// gate is held. Returns null when the tab is offline.
    /// </summary>
    private async Task<ClassicSubscription?> EnsureSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (m_closed || m_host.Connection.Session is not { } session)
        {
            return null;
        }
        long generation = m_host.Connection.Snapshot.Generation;
        if (m_subscription is not null && m_installedGeneration == generation)
        {
            return m_subscription;
        }
        await InstallSubscriptionAsync(session, generation, cancellationToken).ConfigureAwait(true);
        return m_subscription;
    }

    /// <summary>
    /// Builds the per-tab classic <see cref="ClassicSubscription"/> with the fixed
    /// Event View defaults (1 s publish / KA=10 / life=1000), replacing any dead
    /// subscription from a previous generation and re-binding every configured source
    /// to a fresh monitored item. Assumes the gate is held. The subscription is only
    /// published as installed once creation and binding both succeed; a failure is
    /// cleaned up and propagated so the connection lifecycle can report it.
    /// </summary>
    private async Task InstallSubscriptionAsync(
        ManagedSession session, long generation, CancellationToken cancellationToken)
    {
        await ReleaseSubscriptionAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        var sub = new ClassicSubscription(session.MessageContext.Telemetry, new ClassicSubscriptionOptions
        {
            DisplayName = $"UaLens.EventView/{Title}",
            PublishingInterval = 1000,
            KeepAliveCount = 10,
            LifetimeCount = 1000,
            MaxNotificationsPerPublish = 0,
            Priority = 0,
            PublishingEnabled = true,
            MinLifetimeInterval = 60_000
        })
        {
            FastEventCallback = OnFastEvent,
            FastKeepAliveCallback = OnFastKeepAlive
        };
        var creation = new UaLens.Subscriptions.ClassicSubscriptionLease(session, sub);
        await using (creation.ConfigureAwait(false))
        {
            if (!session.AddSubscription(sub))
            {
                throw new InvalidOperationException("The session rejected the Event View subscription.");
            }
            await sub.CreateAsync(cancellationToken).ConfigureAwait(true);
            m_log.SubscriptionCreated(Title, sub.Id, sub.CurrentPublishingInterval, sub.PublishingEnabled);
            await RebindSourcesAsync(sub, cancellationToken).ConfigureAwait(true);
            m_subscription = creation.Transfer();
            m_installedGeneration = generation;
            RefreshStatus();
        }
    }

    /// <summary>
    /// Re-creates a fresh monitored item for every configured source on a newly
    /// installed subscription. Assumes the gate is held.
    /// </summary>
    private async Task RebindSourcesAsync(ClassicSubscription sub, CancellationToken cancellationToken)
    {
        if (EventSources.Count == 0)
        {
            return;
        }
        ITelemetryContext telemetry = SessionTelemetry();
        foreach (EventSourceVm source in EventSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClassicMonitoredItem item = CreateMonitoredItem(telemetry, source.NodeId, source.Name);
            source.MonitoredItem = item;
            source.CreationFailure = null;
            sub.AddItem(item);
        }
        await sub.ApplyChangesAsync(cancellationToken).ConfigureAwait(true);
        foreach (EventSourceVm source in EventSources)
        {
            source.RefreshState();
        }
    }

    /// <summary>
    /// Deletes the current subscription and clears every source's live handle,
    /// assuming the gate is held. The configured sources themselves are retained so a
    /// later reconnect can re-bind them. Safe to call when nothing is installed.
    /// </summary>
    private async Task ReleaseSubscriptionAsync()
    {
        ClassicSubscription? sub = m_subscription;
        m_subscription = null;
        m_installedGeneration = -1;
        foreach (EventSourceVm source in EventSources)
        {
            source.MonitoredItem = null;
            source.RefreshState();
        }
        if (sub is null)
        {
            return;
        }
        try
        {
            if (sub.Session is { } owner)
            {
                await owner.RemoveSubscriptionsAsync([sub], CancellationToken.None).ConfigureAwait(true);
            }
        }
        finally
        {
            sub.Dispose();
        }
    }

    private ClassicMonitoredItem CreateMonitoredItem(
        ITelemetryContext telemetry, NodeId nodeId, string displayName)
        => new(telemetry, new ClassicMonitoredItemOptions
        {
            DisplayName = $"event:{displayName}",
            StartNodeId = nodeId,
            AttributeId = Attributes.EventNotifier,
            MonitoringMode = MonitoringMode.Reporting,
            SamplingInterval = 0,
            QueueSize = 100,
            DiscardOldest = true,
            Filter = BuildEventFilter(m_selectClauses, m_whereClause)
        });

    /// <summary>
    /// Seeds the tab with an initial event source. Used by the address-space "Show
    /// Events…" flow which opens a fresh Event View tab and registers the
    /// right-clicked node. The subscription is created on demand under the gate.
    /// </summary>
    public Task SeedSourceAsync(NodeId nodeId, string displayName)
        => AddSourceCoreAsync(nodeId, displayName);

    private async Task AddSourceCoreAsync(NodeId nodeId, string displayName)
    {
        await m_gate.WaitAsync().ConfigureAwait(true);
        try
        {
            ClassicSubscription? sub = await EnsureSubscriptionAsync(CancellationToken.None).ConfigureAwait(true);
            if (sub is null)
            {
                m_log.AddSourceNoSubscription();
                return;
            }
            ITelemetryContext telemetry = SessionTelemetry();
            ClassicMonitoredItem mi = CreateMonitoredItem(telemetry, nodeId, displayName);

            // Register the source in the UI list BEFORE the network round-trip so the
            // user gets immediate feedback that the click was accepted, and a
            // server-side failure (filter rejection, bad NodeId) doesn't silently
            // leave the panel empty. Gate-held work runs on the UI thread, so
            // mutating EventSources directly is safe.
            var source = new EventSourceVm(nodeId, displayName) { MonitoredItem = mi };
            EventSources.Add(source);
            RefreshStatus();
            m_log.SourceAdded(displayName, nodeId, mi.ClientHandle);

            sub.AddItem(mi);
            try
            {
                await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception applyEx)
            {
                m_log.SourceApplyFailed(applyEx, displayName, nodeId);
                // Make the failure visible in the source list so the user can see WHY
                // no events flow and choose to Remove the row. Sticky so it survives
                // the next publish/keep-alive refresh.
                source.CreationFailure = $"FAILED: {applyEx.Message}";
                source.RefreshState();
                RefreshStatus();
                return;
            }

            // Surface the server's filter feedback so users see why no events flow
            // when the filter is rejected (e.g. unknown field path against the type).
            ServiceResult? createError = mi.Status.Error;
            if (createError is not null && ServiceResult.IsBad(createError))
            {
                m_log.SourceBadStatus(displayName, createError);
            }
            else
            {
                m_log.SourceAccepted(displayName, nodeId, mi.ClientHandle, mi.Status.Id,
                    mi.Status.FilterResult is null ? "(no diagnostics)" : "with diagnostics");
            }
            source.RefreshState();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            m_log.AddSourceFailed(ex, nodeId);
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Re-applies the SelectClause set to every active monitored item
    /// after the user edits the filter.  Each item is mutated and the
    /// subscription is asked to push the change to the server.
    /// </summary>
    private async Task ApplyFilterAsync(EventFilterConfig newFilter)
    {
        (SimpleAttributeOperand[] clauses, string[] paths) = BuildSelectClauses(newFilter);
        await m_gate.WaitAsync().ConfigureAwait(true);
        try
        {
            m_selectClauses = clauses;
            m_selectPaths = paths;
            m_whereClause = newFilter.WhereClause;
            ClassicSubscription? sub = m_subscription;
            if (sub is null)
            {
                return;
            }
            foreach (EventSourceVm src in EventSources)
            {
                if (src.MonitoredItem is { } item)
                {
                    item.Filter = BuildEventFilter(clauses, m_whereClause);
                }
            }
            await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(true);
            m_log.FilterApplied(newFilter.SeverityThreshold, newFilter.Fields.Count,
                m_whereClause is null ? 0 : m_whereClause.Elements.Count);
        }
        catch (Exception ex)
        {
            m_log.FilterApplyFailed(ex);
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// FastEventCallback hook — one invocation per publish that carries
    /// event data for this subscription.  Drains every event in the
    /// list, builds an <see cref="EventLogEntry"/>, applies the
    /// severity UI filter, and pushes the result onto the observable
    /// collection (newest-first).
    /// </summary>
    private void OnFastEvent(ClassicSubscription subscription,
        EventNotificationList notification, ArrayOf<string> stringTable)
    {
        if (notification?.Events is null)
        {
            return;
        }
        int total = notification.Events.Count;
        if (total == 0)
        {
            return;
        }
        var batch = new List<EventLogEntry>(total);
        for (int i = 0; i < total; i++)
        {
            EventFieldList fl = notification.Events[i];
            if (fl is null)
            {
                continue;
            }
            EventLogEntry entry = BuildEntry(fl);
            if (entry.Severity < Filter.SeverityThreshold)
            {
                continue;
            }
            batch.Add(entry);
        }
        if (batch.Count == 0)
        {
            return;
        }
        // Collection and the server's publishing keep running while the display is
        // paused, so count every event here — the received counter proves data still
        // flows. The UI post then decides whether to show or buffer the batch.
        Interlocked.Add(ref m_eventCount, batch.Count);
        Dispatcher.UIThread.Post(() =>
        {
            if (IsPaused)
            {
                foreach (EventLogEntry e in batch)
                {
                    m_pausedBuffer.Insert(0, e);
                }
                TrimPausedBuffer();
            }
            else
            {
                foreach (EventLogEntry e in batch)
                {
                    Events.Insert(0, e);
                }
                TrimEvents();
            }
            RefreshStatus();
        });
    }

    /// <summary>UI-thread-only. Bounds the visible log, counting overflow as dropped.</summary>
    private void TrimEvents()
    {
        while (Events.Count > MaxLogEntries)
        {
            Events.RemoveAt(Events.Count - 1);
            Interlocked.Increment(ref m_droppedCount);
        }
    }

    /// <summary>UI-thread-only. Bounds the paused backlog, counting overflow as dropped.</summary>
    private void TrimPausedBuffer()
    {
        while (m_pausedBuffer.Count > MaxLogEntries)
        {
            m_pausedBuffer.RemoveAt(m_pausedBuffer.Count - 1);
            Interlocked.Increment(ref m_droppedCount);
        }
    }

    /// <summary>
    /// Keep-alive ticks let us refresh the subscription-state indicator
    /// even when the server hasn't sent any events — important when the
    /// user is trying to diagnose why a Condition.Enable / .Disable call
    /// produced nothing in the log.
    /// </summary>
    private void OnFastKeepAlive(ClassicSubscription subscription, NotificationData notification)
    {
        Dispatcher.UIThread.Post(RefreshSubscriptionStatus);
    }

    /// <summary>
    /// Maps an <see cref="EventFieldList"/> back to an
    /// <see cref="EventLogEntry"/> using the current
    /// <see cref="m_selectPaths"/> as the BrowsePath keys.  Resilient
    /// to missing/short field lists.
    /// </summary>
    private EventLogEntry BuildEntry(EventFieldList fl)
    {
        ArrayOf<Variant> fields = fl.EventFields;
        DateTime time = DateTime.UtcNow;
        ushort severity = 0;
        string sourceName = string.Empty;
        string message = string.Empty;
        string eventType = string.Empty;

        int count = Math.Min(fields.Count, m_selectPaths.Length);
        var raw = new List<(string, object?)>(count);
        for (int i = 0; i < count; i++)
        {
            string path = m_selectPaths[i];
            object? val = ConvertVariant(fields[i]);
            raw.Add((path, val));
            switch (path)
            {
                case "/Time":
                    if (fields[i].TryGetValue(out DateTimeUtc dt))
                    {
                        time = dt.ToDateTime();
                    }
                    break;
                case "/Severity":
                    if (fields[i].TryGetValue(out ushort s))
                    {
                        severity = s;
                    }
                    break;
                case "/SourceName":
                    if (fields[i].TryGetValue(out string sn))
                    {
                        sourceName = sn ?? string.Empty;
                    }
                    break;
                case "/Message":
                    if (fields[i].TryGetValue(out LocalizedText lt) && !lt.IsNull)
                    {
                        message = lt.Text ?? string.Empty;
                    }
                    break;
                case "/EventType":
                    if (fields[i].TryGetValue(out NodeId nid) && !nid.IsNull)
                    {
                        eventType = nid.ToString() ?? string.Empty;
                    }
                    break;
            }
        }
        return new EventLogEntry(time, severity, sourceName, eventType, message, raw);
    }

    /// <summary>
    /// Turns a single <see cref="Variant"/> into a binding-friendly
    /// payload (the actual boxed CLR value, or a string for OPC UA
    /// reference types like <see cref="LocalizedText"/> and
    /// <see cref="NodeId"/>).
    /// </summary>
    private static object? ConvertVariant(Variant v)
    {
        if (v.IsNull)
        {
            return null;
        }
        object? raw = v.Value;
        if (raw is LocalizedText lt)
        {
            return lt.IsNull ? string.Empty : lt.Text;
        }
        if (raw is QualifiedName qn)
        {
            return qn.IsNull ? string.Empty : qn.Name;
        }
        if (raw is NodeId nid)
        {
            return nid.IsNull ? string.Empty : nid.ToString();
        }
        if (raw is byte[] bytes)
        {
            return Convert.ToHexString(bytes);
        }
        return raw;
    }

    private static (SimpleAttributeOperand[], string[]) BuildSelectClauses(EventFilterConfig config)
    {
        NodeId typeId = config.EventTypeNodeId ?? ObjectTypeIds.BaseEventType;
        IReadOnlyList<string> fields = config.Fields;
        var ops = new SimpleAttributeOperand[fields.Count];
        var paths = new string[fields.Count];
        for (int i = 0; i < fields.Count; i++)
        {
            string name = fields[i];
            var clause = new SimpleAttributeOperand
            {
                TypeDefinitionId = typeId,
                AttributeId = Attributes.Value
            };
            clause.BrowsePath = clause.BrowsePath.AddItem(QualifiedName.From(name));
            ops[i] = clause;
            paths[i] = "/" + name;
        }
        return (ops, paths);
    }

    private static EventFilter BuildEventFilter(
        IReadOnlyList<SimpleAttributeOperand> clauses,
        ContentFilter? whereClause)
    {
        var filter = new EventFilter();
        foreach (SimpleAttributeOperand op in clauses)
        {
            filter.SelectClauses = filter.SelectClauses.AddItem(op);
        }
        if (whereClause is not null && whereClause.Elements.Count > 0)
        {
            filter.WhereClause = whereClause;
        }
        return filter;
    }

    private EventSourceVm? SelectedSource()
    {
        if (EventSources.Count > 0)
        {
            return EventSources[0];
        }
        return null;
    }

    /// <summary>
    /// Returns the telemetry context bound to the active managed session;
    /// falls back to <see cref="AmbientMessageContext.Telemetry"/> when
    /// the session is gone (e.g. mid-dispose).  Used to construct
    /// classic <see cref="ClassicMonitoredItem"/> instances since
    /// <c>Subscription.Telemetry</c> is non-public.
    /// </summary>
    private ITelemetryContext SessionTelemetry()
    {
        if (m_host.Connection.Session is { } session)
        {
            return session.MessageContext.Telemetry;
        }
        // The ambient context is null only when there is no active
        // scoped context at all — never the case for monitored-item
        // construction which is gated by a live subscription. The `!`
        // matches the pre-nullable signature; in the dispose-time
        // window when both session and ambient context are null, this
        // method is not reached because AddSourceCoreAsync exits early
        // on a null m_subscription.
        return AmbientMessageContext.Telemetry!;
    }

    private void RefreshStatus()
    {
        IsOffline = m_host.Connection.CurrentSession is null;
        long received = Interlocked.Read(ref m_eventCount);
        long dropped = Interlocked.Read(ref m_droppedCount);
        string suffix;
        if (EventSources.Count == 0)
        {
            suffix = " · pick an event source to subscribe";
        }
        else if (received == 0)
        {
            suffix = " · waiting for the server to emit events…";
        }
        else
        {
            suffix = string.Empty;
        }
        string paused = IsPaused
            ? string.Format(CultureInfo.InvariantCulture, " · display paused ({0} buffered)", m_pausedBuffer.Count)
            : string.Empty;
        Status = string.Format(CultureInfo.InvariantCulture,
            "● {0} source{1} · {2} event{3}{4}{5}{6}",
            EventSources.Count, EventSources.Count == 1 ? string.Empty : "s",
            received, received == 1 ? string.Empty : "s",
            dropped > 0 ? $" · {dropped} dropped" : string.Empty,
            paused,
            suffix);

        RefreshSubscriptionStatus();
    }

    /// <summary>
    /// Refreshes the toolbar's subscription-state indicator so users can
    /// diagnose why events aren't flowing without diving into the SDK
    /// log buffer.  Shows subscription id, current publishing interval,
    /// publishing-enabled state, how many of the registered monitored
    /// items the server has acknowledged as Created, and how many
    /// publish responses arrived since open.  Also refreshes every
    /// <see cref="EventSourceVm"/>'s per-row State string.
    /// </summary>
    private void RefreshSubscriptionStatus()
    {
        ClassicSubscription? sub = m_subscription;
        if (sub is null)
        {
            SubscriptionStatus = "○ Subscription: not created";
            return;
        }
        int total = 0;
        int created = 0;
        int badStatus = 0;
        foreach (ClassicMonitoredItem mi in sub.MonitoredItems)
        {
            total++;
            if (mi.Status.Created)
            {
                created++;
            }
            if (mi.Status.Error is { } err && ServiceResult.IsBad(err))
            {
                badStatus++;
            }
        }
        foreach (EventSourceVm src in EventSources)
        {
            src.RefreshState();
        }
        string badSuffix = badStatus > 0
            ? $" · {badStatus} bad"
            : string.Empty;
        SubscriptionStatus = sub.Created
            ? string.Format(CultureInfo.InvariantCulture,
                "● Sub {0} · PI {1:F0} ms · MI {2}/{3}{4} · {5}",
                sub.Id,
                sub.CurrentPublishingInterval,
                created,
                total,
                badSuffix,
                sub.PublishingEnabled ? "server publishing" : "server publishing off")
            : string.Format(CultureInfo.InvariantCulture,
                "◑ Subscription: pending · MI {0} queued", total);
    }

    /// <summary>
    /// Display pause toggled. Collection and the server's publishing continue; on
    /// resume the bounded backlog collected while paused is flushed into the visible
    /// log, newest first.
    /// </summary>
    partial void OnIsPausedChanged(bool value)
    {
        if (!value && m_pausedBuffer.Count > 0)
        {
            for (int i = m_pausedBuffer.Count - 1; i >= 0; i--)
            {
                Events.Insert(0, m_pausedBuffer[i]);
            }
            m_pausedBuffer.Clear();
            TrimEvents();
        }
        RefreshStatus();
    }

    private Window? TopLevelWindow()
    {
        if (m_view is null)
        {
            return null;
        }
        return TopLevel.GetTopLevel(m_view) as Window;
    }

    public JsonElement CaptureState()
    {
        var sources = new List<EventSourceSelection>(EventSources.Count);
        foreach (EventSourceVm source in EventSources)
        {
            sources.Add(new EventSourceSelection(source.NodeId, source.Name));
        }
        var snapshot = new EventViewStateSnapshot(Title, Filter, IsPaused, [.. sources]);
        return EventViewStateCodec.Capture(snapshot, MessageContext());
    }

    public async Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        // Validate before touching any state so an invalid snapshot is reported
        // without partially overwriting the current configuration.
        EventViewStateSnapshot snapshot = EventViewStateCodec.Restore(state, MessageContext());
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Title = snapshot.Title;
            Filter = snapshot.Filter;
            (m_selectClauses, m_selectPaths) = BuildSelectClauses(snapshot.Filter);
            m_whereClause = snapshot.Filter.WhereClause;
            IsPaused = snapshot.DisplayPaused;
            // Restore configuration only. Live monitored items are (re)created against
            // the session by the connection lifecycle, never during restore.
            EventSources.Clear();
            foreach (EventSourceSelection selection in snapshot.Sources)
            {
                EventSources.Add(new EventSourceVm(selection.NodeId, selection.Name));
            }
            RefreshStatus();
        }
        finally
        {
            m_gate.Release();
        }
    }

    private IServiceMessageContext MessageContext()
    {
        if (m_host.Connection.Session is { } session)
        {
            return session.MessageContext;
        }
        return new ServiceMessageContext(m_host.Telemetry);
    }
}

/// <summary>
/// Source-generated log messages for the Event View document. Event ids are offset
/// from <see cref="UaLensEventIds.EventViewPluginBase"/>.
/// </summary>
internal static partial class EventViewPluginLog
{
    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 0, Level = LogLevel.Information,
        Message = "Event View AddSource: not connected.")]
    public static partial void AddSourceNotConnected(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 1, Level = LogLevel.Warning,
        Message = "Event View AddSource: could not create a subscription (not connected).")]
    public static partial void AddSourceNoSubscription(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 2, Level = LogLevel.Warning,
        Message = "Event View: AddSubscription returned false.")]
    public static partial void SubscriptionAddRejected(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 3, Level = LogLevel.Information,
        Message = "Event View subscription created (tab {Title}, id={SubscriptionId}, " +
            "publishingInterval={PublishingInterval}, publishingEnabled={PublishingEnabled}).")]
    public static partial void SubscriptionCreated(
        this ILogger logger,
        string title,
        uint subscriptionId,
        double publishingInterval,
        bool publishingEnabled);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 4, Level = LogLevel.Debug,
        Message = "Event View subscription dispose threw — ignored.")]
    public static partial void SubscriptionDisposeIgnored(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 5, Level = LogLevel.Information,
        Message = "Event View added source {Name} ({Node}) ch={Handle}.")]
    public static partial void SourceAdded(this ILogger logger, string name, NodeId node, uint handle);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 6, Level = LogLevel.Error,
        Message = "Event View source {Name} ({Node}) ApplyChanges failed; the source remains " +
            "listed but won't deliver events.")]
    public static partial void SourceApplyFailed(
        this ILogger logger, Exception exception, string name, NodeId node);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 7, Level = LogLevel.Warning,
        Message = "Event View source {Name} create returned bad status: {Status}")]
    public static partial void SourceBadStatus(this ILogger logger, string name, ServiceResult status);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 8, Level = LogLevel.Information,
        Message = "Event View source {Name} ({Node}) accepted by server " +
            "(ch={Handle}, miId={MonitoredItemId}, filter={Filter}).")]
    public static partial void SourceAccepted(
        this ILogger logger,
        string name,
        NodeId node,
        uint handle,
        uint monitoredItemId,
        string filter);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 9, Level = LogLevel.Error,
        Message = "Event View AddSource failed for {Node}.")]
    public static partial void AddSourceFailed(this ILogger logger, Exception exception, NodeId node);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 10, Level = LogLevel.Information,
        Message = "Event View removed source {Name} ({Node}).")]
    public static partial void SourceRemoved(this ILogger logger, string name, NodeId node);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 11, Level = LogLevel.Error,
        Message = "Event View RemoveSource failed for {Node}.")]
    public static partial void RemoveSourceFailed(this ILogger logger, Exception exception, NodeId node);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 12, Level = LogLevel.Information,
        Message = "Event View filter applied: severity≥{Severity}, {FieldCount} fields, " +
            "where={WhereElementCount}.")]
    public static partial void FilterApplied(
        this ILogger logger, ushort severity, int fieldCount, int whereElementCount);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 13, Level = LogLevel.Error,
        Message = "Event View ApplyFilter failed.")]
    public static partial void FilterApplyFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 14, Level = LogLevel.Information,
        Message = "Event View Trigger: not connected.")]
    public static partial void TriggerNotConnected(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.EventViewPluginBase + 15, Level = LogLevel.Warning,
        Message = "Event View Trigger: picked node {Node} of class {NodeClass} cannot be invoked " +
            "directly (only Method/Variable are actionable).")]
    public static partial void TriggerNotActionable(this ILogger logger, NodeId node, NodeClass nodeClass);
}
