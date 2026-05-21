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
using Opc.Ua.Client;
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
    public uint ClientHandle { get; }
    internal ClassicMonitoredItem MonitoredItem { get; }

    [ObservableProperty]
    private string m_state = "pending";

    /// <summary>
    /// Set when client-side ApplyChanges throws (transport error, session
    /// closed) so that <see cref="RefreshState"/> doesn't overwrite the
    /// failure message on subsequent publishes.
    /// </summary>
    internal string? CreationFailure { get; set; }

    public EventSourceVm(NodeId nodeId, string name, ClassicMonitoredItem item)
    {
        NodeId = nodeId;
        Name = name;
        ClientHandle = item.ClientHandle;
        MonitoredItem = item;
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
        var st = MonitoredItem.Status;
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
/// View model for an Event View tab.  Owns a per-tab event subscription
/// (a raw <see cref="ClassicSubscription"/> on the managed session) and
/// a parallel <see cref="ISubscriptionAdapter"/> created via
/// <c>ConnectionService.CreateAdapter</c> — the adapter is the
/// project-standard handoff for engine accounting + lifetime parity
/// with Subscription tabs, while the raw subscription is what surfaces
/// the actual <see cref="EventFieldList"/> via FastEventCallback (the
/// adapter contract only delivers per-message counters, not the
/// underlying event fields).
/// </summary>
internal sealed partial class EventViewPlugin : ObservableObject, IPlugin
{
    /// <summary>Per-kind auto-numbering counter for the default tab title.</summary>
    private static readonly Dictionary<PluginKind, int> s_perKindCounter = new();

    private const int MaxLogEntries = 2000;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private readonly Dictionary<uint, EventSourceVm> m_byHandle = new();
    private readonly SemaphoreSlim m_lock = new(1, 1);
    // CA2213: m_subscription IS disposed in DisposeAsync below, but the
    // analyzer can't see the lifecycle through Interlocked.Exchange.
#pragma warning disable CA2213
    private ClassicSubscription? m_subscription;
    private readonly TaskCompletionSource<bool> m_subscriptionReady = new();
#pragma warning restore CA2213
    private SimpleAttributeOperand[] m_selectClauses;
    private string[] m_selectPaths;
    private ContentFilter? m_whereClause;
    private long m_eventCount;
    private long m_droppedCount;
    private EventViewView? m_view;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private bool m_isPaused;

    [ObservableProperty]
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
        m_host = host;
        m_log = host.Log;
        int n;
        lock (s_perKindCounter)
        {
            s_perKindCounter.TryGetValue(PluginKind.EventView, out int prev);
            n = prev + 1;
            s_perKindCounter[PluginKind.EventView] = n;
        }
        m_title = $"Event View {n}";

        (m_selectClauses, m_selectPaths) = BuildSelectClauses(m_filter);

        if (m_host.Connection.Session is { } session)
        {
            _ = InitializeSubscriptionAsync(session);
        }
        else
        {
            m_log.LogWarning("Event View opened without an active session — no subscription will be created.");
        }
    }

    // ----- IPlugin members -----

    public PluginKind Kind => PluginKind.EventView;

    Control? IPlugin.View => m_view ??= new EventViewView { DataContext = this };
    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public void OnActivated() { }
    public void OnDeactivated() { }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        var addSrc = new MenuItem { Header = "_Add Source\u2026" };
        var removeSrc = new MenuItem { Header = "_Remove Source\u2026" };
        var editFilter = new MenuItem { Header = "Edit _Filter\u2026" };
        var clear = new MenuItem { Header = "_Clear Log" };
        var pause = new MenuItem
        {
            Header = "_Pause Stream",
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

    public async ValueTask DisposeAsync()
    {
        // Signal any pending SeedSourceAsync awaiters that the tab is
        // gone before they get to AddSourceCoreAsync.  Without this,
        // a Show-Events context-menu invocation immediately followed
        // by tab-close would leave the seeding task hung forever.
        m_subscriptionReady.TrySetResult(false);
        ClassicSubscription? sub = Interlocked.Exchange(ref m_subscription, null);
        if (sub is not null)
        {
            try
            {
                await sub.DeleteAsync(silent: true, CancellationToken.None).ConfigureAwait(false);
                sub.Dispose();
            }
            catch (Exception ex)
            {
                m_log.LogDebug(ex, "Event View subscription dispose threw — ignored.");
            }
        }
        m_lock.Dispose();
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
            m_log.LogInformation("Event View AddSource: not connected.");
            return;
        }

        // Wait for the per-tab subscription to finish CreateAsync.  Without
        // this, a quick click on "+ Add Source" right after opening a tab
        // silently drops the request because m_subscription is still null
        // and AddSourceCoreAsync bails out.
        bool ready = await m_subscriptionReady.Task.ConfigureAwait(true);
        if (!ready)
        {
            m_log.LogWarning("Event View AddSource: subscription failed to initialise.");
            return;
        }

        // If the user already has a valid event-emitting Object/View
        // selected in the address-space tree, accept it directly without
        // popping a picker.
        NodeViewModel? node = m_host.Main.SelectedNode;
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
        await m_lock.WaitAsync().ConfigureAwait(true);
        try
        {
            ClassicSubscription? sub = m_subscription;
            if (sub is null)
            {
                return;
            }
            sub.RemoveItem(source.MonitoredItem);
            await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(true);
            m_byHandle.Remove(source.ClientHandle);
            Dispatcher.UIThread.Post(() =>
            {
                EventSources.Remove(source);
                RefreshStatus();
            });
            m_log.LogInformation("Event View removed source {Name} ({Node}).", source.Name, source.NodeId);
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Event View RemoveSource failed for {Node}.", source.NodeId);
        }
        finally
        {
            m_lock.Release();
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
            m_log.LogInformation("Event View Trigger: not connected.");
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
            m_log.LogWarning(
                "Event View Trigger: picked node {Node} of class {NodeClass} " +
                "cannot be invoked directly (only Method/Variable are actionable).",
                item.NodeId, item.NodeClass);
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
    /// Builds the per-tab classic <see cref="ClassicSubscription"/> with
    /// the fixed Event View defaults (1 s publish / KA=10 / life=1000).
    /// </summary>
    private async Task InitializeSubscriptionAsync(ManagedSession session)
    {
        try
        {
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
            if (!session.AddSubscription(sub))
            {
                m_log.LogWarning("Event View: AddSubscription returned false.");
                sub.Dispose();
                return;
            }
            await sub.CreateAsync(CancellationToken.None).ConfigureAwait(true);
            m_subscription = sub;
            m_subscriptionReady.TrySetResult(true);
            m_log.LogInformation(
                "Event View subscription created (tab {Title}, id={Id}, " +
                "publishingInterval={Pi}, publishingEnabled={Pub}).",
                Title, sub.Id, sub.CurrentPublishingInterval, sub.PublishingEnabled);
            Dispatcher.UIThread.Post(RefreshSubscriptionStatus);
        }
        catch (Exception ex)
        {
            m_subscriptionReady.TrySetResult(false);
            m_log.LogError(ex, "Event View subscription creation failed (tab {Title}).", Title);
        }
    }

    /// <summary>
    /// Seeds the tab with an initial event source.  Used by the
    /// address-space "Show Events…" context-menu entry which creates
    /// a fresh EventView tab and immediately registers the right-clicked
    /// node as the source.  Awaits subscription initialisation so the
    /// AddSource call always finds a live subscription.
    /// </summary>
    public async Task SeedSourceAsync(NodeId nodeId, string displayName)
    {
        bool ready = await m_subscriptionReady.Task.ConfigureAwait(true);
        if (!ready)
        {
            return;
        }
        await AddSourceCoreAsync(nodeId, displayName).ConfigureAwait(true);
    }

    private async Task AddSourceCoreAsync(NodeId nodeId, string displayName)
    {
        await m_lock.WaitAsync().ConfigureAwait(true);
        try
        {
            ClassicSubscription? sub = m_subscription;
            if (sub is null)
            {
                m_log.LogWarning("Event View AddSource: subscription not yet created.");
                return;
            }
            ITelemetryContext telemetry = SessionTelemetry();
            var mi = new ClassicMonitoredItem(telemetry, new ClassicMonitoredItemOptions
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

            // Register the source in the UI list BEFORE the network round-trip
            // so the user gets immediate feedback that the click was accepted
            // — and so a server-side failure (filter rejection, bad NodeId)
            // doesn't silently leave the Event sources panel empty. We're on
            // the UI thread here (every await uses ConfigureAwait(true)), so
            // mutating EventSources directly is safe.
            var source = new EventSourceVm(nodeId, displayName, mi);
            m_byHandle[mi.ClientHandle] = source;
            EventSources.Add(source);
            RefreshStatus();
            m_log.LogInformation(
                "Event View added source {Name} ({Node}) ch={Handle}.",
                displayName, nodeId, mi.ClientHandle);

            sub.AddItem(mi);
            try
            {
                await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception applyEx)
            {
                m_log.LogError(applyEx,
                    "Event View source {Name} ({Node}) ApplyChanges failed; " +
                    "source remains in the list but won't deliver events.",
                    displayName, nodeId);
                // Make the failure visible in the source list so the user
                // can see WHY no events flow and choose to Remove the row.
                // Sticky so it survives the next publish/keep-alive refresh.
                source.CreationFailure = $"FAILED: {applyEx.Message}";
                RefreshStatus();
                return;
            }

            // Surface the server's filter feedback so users see why no
            // events flow when the filter is rejected (e.g. unknown field
            // path against the chosen EventType).
            ServiceResult? createError = mi.Status.Error;
            if (createError is not null && ServiceResult.IsBad(createError))
            {
                m_log.LogWarning(
                    "Event View source {Name} create returned bad status: {Status}",
                    displayName, createError);
            }
            else
            {
                m_log.LogInformation(
                    "Event View source {Name} ({Node}) accepted by server " +
                    "(ch={Handle}, miId={MiId}, filter={Filter}).",
                    displayName, nodeId, mi.ClientHandle, mi.Status.Id,
                    mi.Status.FilterResult is null ? "(no diagnostics)" : "with diagnostics");
            }
            // Update the source row + toolbar indicator now that the server
            // round-trip has populated mi.Status.
            RefreshStatus();
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Event View AddSource failed for {Node}.", nodeId);
        }
        finally
        {
            m_lock.Release();
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
        m_selectClauses = clauses;
        m_selectPaths = paths;
        m_whereClause = newFilter.WhereClause;
        await m_lock.WaitAsync().ConfigureAwait(true);
        try
        {
            ClassicSubscription? sub = m_subscription;
            if (sub is null)
            {
                return;
            }
            foreach (EventSourceVm src in EventSources)
            {
                src.MonitoredItem.Filter = BuildEventFilter(clauses, m_whereClause);
            }
            await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(true);
            m_log.LogInformation("Event View filter applied: severity≥{Sev}, {N} fields, where={W}.",
                newFilter.SeverityThreshold, newFilter.Fields.Count,
                m_whereClause is null ? 0 : m_whereClause.Elements.Count);
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Event View ApplyFilter failed.");
        }
        finally
        {
            m_lock.Release();
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
        if (IsPaused)
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
        Interlocked.Add(ref m_eventCount, batch.Count);
        Dispatcher.UIThread.Post(() =>
        {
            foreach (EventLogEntry e in batch)
            {
                Events.Insert(0, e);
            }
            while (Events.Count > MaxLogEntries)
            {
                Events.RemoveAt(Events.Count - 1);
                Interlocked.Increment(ref m_droppedCount);
            }
            RefreshStatus();
        });
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
        Status = string.Format(CultureInfo.InvariantCulture,
            "● {0} source{1} · {2} event{3}{4}{5}{6}",
            EventSources.Count, EventSources.Count == 1 ? "" : "s",
            received, received == 1 ? "" : "s",
            dropped > 0 ? $" · {dropped} dropped" : "",
            IsPaused ? " · paused" : "",
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
                sub.PublishingEnabled ? "publishing" : "paused")
            : string.Format(CultureInfo.InvariantCulture,
                "◑ Subscription: pending · MI {0} queued", total);
    }

    partial void OnIsPausedChanged(bool value) => RefreshStatus();

    private Window? TopLevelWindow()
    {
        if (m_view is null)
        {
            return null;
        }
        return TopLevel.GetTopLevel(m_view) as Window;
    }
}
