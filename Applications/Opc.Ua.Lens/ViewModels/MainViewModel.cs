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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.Telemetry;

namespace UaLens.ViewModels;

/// <summary>
/// 4-state toggle for the side-by-side Attributes / References panels under
/// the address-space tree. Cycled by the Attrs button on the toolbar (and
/// Ctrl-A): None → AttrsOnly → AttrsAndRefs → RefsOnly → None.
/// </summary>
internal enum SidePanelMode
{
    None,
    AttrsOnly,
    AttrsAndRefs,
    RefsOnly
}

/// <summary>
/// Top-level view model for the main window. Owns the live
/// <see cref="ConnectionService"/>, the visible monitored-item list, the
/// active <see cref="SubscriptionConfig"/>, and the log surface bound to the
/// <see cref="LogRingBuffer"/>.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly LogRingBuffer m_logBuffer;
    private readonly ILogger m_log;
    private readonly DispatcherTimer m_logPump;
    private long m_lastLogIndex;

    public AppTelemetryContext Telemetry { get; }
    public ConnectionService Connection { get; }
    public BrowserViewModel Browser { get; }
    public NodeAttributesViewModel Attributes { get; }
    public ReferencesViewModel References { get; }

    /// <summary>
    /// Singleton sink for low-level publish-message metadata.  Bound by
    /// the "Publishes" sub-tab of <c>DiagnosticsView</c>; populated by
    /// every subscription adapter created on the live
    /// <see cref="ConnectionService"/>.
    /// </summary>
    public UaLens.Diagnostics.PublishLogObserver PublishLog { get; } = new();

    /// <summary>
    /// All open tabs (any kind from <see cref="PluginKind"/>).
    /// Subscription tabs are auto-spawned on first connect; other kinds
    /// are user-opt-in via the Tabs → New menu.
    /// </summary>
    public ObservableCollection<IPlugin> Tabs { get; } = new();

    /// <summary>The currently-active tab.  Drives the right-pane content.</summary>
    [ObservableProperty]
    private IPlugin? m_selectedTab;

    /// <summary>
    /// Convenience cast — returns the active tab as a
    /// <see cref="SubscriptionViewModel"/> when it is one, else null.
    /// Many subscription-specific code paths and bindings use this.
    /// </summary>
    public SubscriptionViewModel? SelectedSubscriptionTab
        => SelectedTab as SubscriptionViewModel;

    /// <summary>True when the currently-selected tab is a Subscription
    /// (drives visibility of the shared chart + toolbar).</summary>
    public bool IsSubscriptionTabActive => SelectedTab is SubscriptionViewModel;

    /// <summary>True when the currently-selected tab is non-Subscription
    /// (drives visibility of the generic ContentControl bound to
    /// <see cref="IPlugin.View"/>).</summary>
    public bool IsCustomTabActive => SelectedTab is { } t && t is not SubscriptionViewModel;

    /// <summary>True iff any open tab is a Subscription
    /// (drives visibility of the Subscri_ption top-level menu).</summary>
    public bool HasAnySubscriptionTab => Tabs.OfType<SubscriptionViewModel>().Any();

    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty]
    private string m_endpointUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";

    [ObservableProperty]
    private SubscriptionEngineKind m_engine = SubscriptionEngineKind.ChannelV2;

    [ObservableProperty]
    private string m_connectionStatus = "● Disconnected";

    [ObservableProperty]
    private bool m_isConnected;

    [ObservableProperty]
    private bool m_isAddressSpaceVisible = true;

    [ObservableProperty]
    private string m_engineButtonText = "↻ Engine: ChannelV2";

    [ObservableProperty]
    private SidePanelMode m_attributesPanelMode = SidePanelMode.AttrsOnly;

    /// <summary>
    /// The registered application UaLens is currently working with.
    /// Set by GDS plug-ins (Management resolves it, Push delivers
    /// against it, Discovery surfaces it from picked endpoints). Other
    /// plug-ins consume it via the <see cref="PluginHost"/> /
    /// <see cref="MainViewModel"/>; updating it raises PropertyChanged
    /// so cooperating tabs can react.
    /// </summary>
    [ObservableProperty]
    private UaLens.Plugins.Gds.RegisteredApplicationContext? m_currentRegisteredApp;

    /// <summary>
    /// Cycles the side-panel visibility through:
    /// None → AttrsOnly → AttrsAndRefs → RefsOnly → None.
    /// </summary>
    public void CycleAttributesPanelMode()
    {
        AttributesPanelMode = AttributesPanelMode switch
        {
            SidePanelMode.None => SidePanelMode.AttrsOnly,
            SidePanelMode.AttrsOnly => SidePanelMode.AttrsAndRefs,
            SidePanelMode.AttrsAndRefs => SidePanelMode.RefsOnly,
            SidePanelMode.RefsOnly => SidePanelMode.None,
            _ => SidePanelMode.None
        };
    }

    public bool ShowAttributes
        => AttributesPanelMode is SidePanelMode.AttrsOnly or SidePanelMode.AttrsAndRefs;
    public bool ShowReferences
        => AttributesPanelMode is SidePanelMode.AttrsAndRefs or SidePanelMode.RefsOnly;

    partial void OnAttributesPanelModeChanged(SidePanelMode value)
    {
        OnPropertyChanged(nameof(ShowAttributes));
        OnPropertyChanged(nameof(ShowReferences));
    }

    // ----- Selection lifecycle for "Add Item" -----
    private NodeViewModel? m_selectedNode;
    private CancellationTokenSource? m_selectionCts;

    /// <summary>True iff the currently-selected address-space node is a Variable, or
    /// an Object whose EventNotifier carries the SubscribeToEvents bit.</summary>
    [ObservableProperty]
    private bool m_canAddSelectedItem;

    /// <summary>True iff the selected node will be subscribed in Event mode.</summary>
    [ObservableProperty]
    private bool m_selectedItemIsEvent;

    /// <summary>One-line caption rendered next to the Add button.</summary>
    [ObservableProperty]
    private string m_selectedItemStatus = "Pick a Variable, or an Object that emits events.";

    /// <summary>True when the selected Object emits events (SubscribeToEvents bit).</summary>
    [ObservableProperty]
    private bool m_selectionHasEvents;

    /// <summary>True when the selected Object has at least one HasComponent child Variable.</summary>
    [ObservableProperty]
    private bool m_selectionHasVariables;

    /// <summary>True when the selected node is a Method (Call dialog visible).</summary>
    [ObservableProperty]
    private bool m_canCallMethod;

    /// <summary>True when the selected node is a writable Variable (Write dialog visible).</summary>
    [ObservableProperty]
    private bool m_canWriteVariable;

    /// <summary>
    /// Cached list of HasComponent child Variables of the currently-selected
    /// Object (NodeId + DisplayName).  Populated by
    /// <see cref="UpdateSelectionAsync"/>; consumed by the bulk-add path in
    /// <c>MainWindow.OnAddItemClick</c>.
    /// </summary>
    public IReadOnlyList<(NodeId NodeId, string DisplayName)> SelectionVariables { get; private set; }
        = Array.Empty<(NodeId, string)>();

    [ObservableProperty]
    private string m_resourceStatus = "CPU --   Mem --";

    public MainViewModel()
    {
        m_logBuffer = new LogRingBuffer(capacity: 4096);
        Telemetry = new AppTelemetryContext(m_logBuffer);
        m_log = Telemetry.CreateLogger("Main");
        Connection = new ConnectionService(Telemetry, PublishLog);
        Browser = new BrowserViewModel(Telemetry, Connection);
        Attributes = new NodeAttributesViewModel(Telemetry, Connection);
        References = new ReferencesViewModel(Telemetry, Connection);

        Connection.StateChanged += () => Dispatcher.UIThread.Post(SyncFromConnection);
        Connection.StateChanged += () => Dispatcher.UIThread.Post(NotifyPluginsOfConnectionChange);

        m_logPump = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => PumpLog());
        m_logPump.Start();

        // CPU / memory pane: 1 Hz sampling via Microsoft.Extensions.Diagnostics.ResourceMonitoring.
        m_resourcePump = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => PumpResources());
        m_resourcePump.Start();

        SyncFromConnection();
        m_log.LogInformation("UaLens started — Tab cycles focus, F1 for help.");

        // Watch Tabs so HasAnySubscriptionTab refreshes for menu visibility.
        Tabs.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasAnySubscriptionTab));
    }

    /// <summary>
    /// Builds the <see cref="PluginHost"/> snapshot passed to each new
    /// tab's factory.  Tabs that need to react to mid-session state
    /// changes (session reconnect, etc.) should observe the
    /// <see cref="Main"/> view model rather than relying on the
    /// captured values.
    /// </summary>
    public PluginHost CreatePluginHost() =>
        new(this, Connection.Session, Connection, Browser, m_log);

    /// <summary>
    /// Adds a tab of the requested kind to the workbench.  Optional
    /// post-creation seeding hooks let the caller (typically the
    /// address-space context menu) pre-configure the new tab so the
    /// user lands with their selection ready.
    /// </summary>
    public async Task AddPluginAsync(
        PluginKind kind,
        NodeViewModel? seedEventSource = null,
        bool seedPickTarget = false,
        UaLens.Plugins.Gds.RegisteredApplicationContext? seedRegisteredApp = null,
        EndpointDescription? seedDiscoveryEndpoint = null,
        NodeViewModel? seedBenchNode = null)
    {
        if (kind == PluginKind.Subscription)
        {
            await AddTabCommand.ExecuteAsync(null).ConfigureAwait(true);
            return;
        }
        // Promote the seeded registered-app context to the shared
        // top-level state so cooperating tabs (the freshly-spawned one
        // plus any already-open GDS tabs) all see the same record.
        if (seedRegisteredApp is not null)
        {
            CurrentRegisteredApp = seedRegisteredApp;
        }

        // Subscription Bench reuses an existing tab when present so the
        // user can stream additional pool entries into one running bench
        // instead of spawning many parallel tabs.  Spawn a fresh tab only
        // when the user explicitly asks for one (no seed) or none exists.
        IPlugin? tab = null;
        if (kind == PluginKind.SubscriptionBench && seedBenchNode is not null)
        {
            tab = Tabs.OfType<UaLens.Plugins.SubscriptionBench.SubscriptionBenchPlugin>()
                .LastOrDefault();
            if (tab is not null)
            {
                SelectedTab = tab;
            }
        }
        if (tab is null)
        {
            PluginRegistration reg = PluginRegistry.For(kind);
            tab = reg.Factory(this);
            IPlugin newTab = tab;
            Dispatcher.UIThread.Post(() =>
            {
                Tabs.Add(newTab);
                SelectedTab = newTab;
            });
        }

        // Post-creation seeding:
        if (seedEventSource is not null
            && tab is UaLens.Plugins.EventView.EventViewPlugin evView)
        {
            _ = evView.SeedSourceAsync(seedEventSource.NodeId, seedEventSource.Text);
        }
        if (seedPickTarget
            && tab is UaLens.Plugins.Performance.PerformancePlugin perf)
        {
            // Defer to next dispatcher cycle so the tab body is mounted
            // before the modal target dialog opens.
            Dispatcher.UIThread.Post(() =>
            {
                if (perf.PickTargetCommand.CanExecute(null))
                {
                    perf.PickTargetCommand.Execute(null);
                }
            });
        }
        if (seedBenchNode is not null
            && tab is UaLens.Plugins.SubscriptionBench.SubscriptionBenchPlugin bench)
        {
            _ = bench.SeedFromNodeAsync(
                seedBenchNode.NodeId,
                seedBenchNode.NodeClass,
                seedBenchNode.Text);
        }
        // seedDiscoveryEndpoint is consumed by the (future)
        // GdsDiscoveryPlugin via a dedicated SeedEndpointAsync hook —
        // wired here as a no-op until the plug-in lands.
        _ = seedDiscoveryEndpoint;
    }

    /// <summary>Set by Program.cs once the resource-monitor host is up.</summary>
    public UaLens.Diagnostics.ResourceMonitorHost? ResourceMonitor { get; set; }

    private readonly DispatcherTimer m_resourcePump;

    private void PumpResources()
    {
        if (ResourceMonitor is { } m)
        {
            ResourceStatus = m.Sample();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            if (Connection.IsConnected)
            {
                await Connection.DisconnectAsync().ConfigureAwait(true);
                return;
            }
            ConnectionStatus = "● Connecting…";
            await Connection.ConnectAsync(
                new ConnectionOptions
                {
                    EndpointUrl = EndpointUrl,
                    UseSecurity = false,
                    Engine = Engine
                },
                CancellationToken.None).ConfigureAwait(true);
            // First tab is created on the StateChanged callback (SyncFromConnection).
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Connect failed.");
            ConnectionStatus = $"● Connect failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleEngineAsync()
    {
        // Per user request the toggle now recreates the session on the
        // fly when connected (rather than refusing as the older C4
        // guard did) — the menu binding makes the change deliberate, so
        // tab disposal-then-rebind is the expected behaviour.
        Engine = Engine == SubscriptionEngineKind.ChannelV2
            ? SubscriptionEngineKind.Classic
            : SubscriptionEngineKind.ChannelV2;
        EngineButtonText = $"↻ Engine: {Engine}";
        OnPropertyChanged(nameof(UseChannelV2Engine));
        if (Connection.IsConnected)
        {
            await Connection.DisconnectAsync().ConfigureAwait(true);
            await ConnectAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Two-way binding helper for the <c>Session → Use ChannelV2 Engine</c>
    /// menu checkbox.  Reading returns <c>true</c> iff the live engine is
    /// <see cref="SubscriptionEngineKind.ChannelV2"/>; setting to the
    /// opposite value invokes <see cref="ToggleEngineCommand"/> which
    /// recreates the session when connected.  Setting to the current
    /// value is a no-op so the checkbox can be re-synced without side
    /// effects.
    /// </summary>
    public bool UseChannelV2Engine
    {
        get => Engine == SubscriptionEngineKind.ChannelV2;
        set
        {
            if (value == (Engine == SubscriptionEngineKind.ChannelV2))
            {
                return;
            }
            _ = ToggleEngineCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Adds a new subscription tab.  When connected the tab is bound to a
    /// fresh adapter immediately; when disconnected the tab is created
    /// "unbound" and will receive an adapter automatically the next time
    /// <see cref="SyncFromConnection"/> sees IsConnected flip to true.
    /// </summary>
    [RelayCommand]
    private async Task AddTabAsync()
    {
        try
        {
            int n = Tabs.Count + 1;
            ISubscriptionAdapter? adapter = Connection.IsConnected
                ? Connection.CreateAdapter()
                : null;
            // CA2000: ownership of the SubscriptionViewModel transfers to
            // the Tabs collection (managed via CloseTabAsync /
            // SyncFromConnection); no leak.
#pragma warning disable CA2000
            var vm = new SubscriptionViewModel($"Sub {n}", adapter, m_log);
#pragma warning restore CA2000
            // Inherit per-tab UI state from the currently-selected Subscription
            // tab (if any).
            if (SelectedTab is SubscriptionViewModel prev)
            {
                vm.AnimationMode = prev.AnimationMode;
                vm.AnimationTimeScale = prev.AnimationTimeScale;
                vm.ShowResourceOverlay = prev.ShowResourceOverlay;
            }
            if (adapter is not null)
            {
                await adapter.ApplySubscriptionAsync(vm.Subscription, CancellationToken.None).ConfigureAwait(true);
                vm.RefreshStatus();
            }
            Dispatcher.UIThread.Post(() =>
            {
                Tabs.Add(vm);
                SelectedTab = vm;
            });
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "AddTab failed.");
        }
    }

    /// <summary>
    /// Duplicate an existing subscription tab: same subscription config,
    /// same monitored items (re-added with fresh server-side IDs), same
    /// per-tab UI state.  Triggered by the tab right-click menu.
    /// </summary>
    public async Task<SubscriptionViewModel?> DuplicateTabAsync(SubscriptionViewModel source)
    {
        if (!Connection.IsConnected)
        {
            return null;
        }

        try
        {
            ISubscriptionAdapter adapter = Connection.CreateAdapter();
            int n = Tabs.Count + 1;
            var vm = new SubscriptionViewModel($"{source.Title} (copy)", adapter, m_log)
            {
                Subscription = source.Subscription,
                AnimationMode = source.AnimationMode,
                AnimationTimeScale = source.AnimationTimeScale,
                ShowResourceOverlay = source.ShowResourceOverlay
            };
            await adapter.ApplySubscriptionAsync(vm.Subscription, CancellationToken.None).ConfigureAwait(true);
            vm.RefreshStatus();
            // Clone monitored items.
            foreach (MonitoredItemConfig item in source.Items.ToArray())
            {
                await vm.AddItemCommand.ExecuteAsync(item with { Id = 0 }).ConfigureAwait(true);
            }
            Dispatcher.UIThread.Post(() =>
            {
                Tabs.Add(vm);
                SelectedTab = vm;
            });
            return vm;
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "DuplicateTab failed.");
            return null;
        }
    }

    /// <summary>Removes a tab, disposing its adapter.  Now permits closing every tab — the user can re-add via Tabs → New.</summary>
    [RelayCommand]
    private async Task CloseTabAsync(IPlugin? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
        {
            return;
        }
        if (tab is SubscriptionViewModel sub)
        {
            // ForgetAdapter returns false if DisconnectInternalAsync already
            // took ownership of disposal — don't double-dispose in that case.
            // Adapter is nullable post-rebind: skip Forget if there's nothing
            // to forget.
            if (sub.Adapter is { } adapter && Connection.ForgetAdapter(adapter))
            {
                await sub.DisposeAsync().ConfigureAwait(true);
            }
            else if (sub.Adapter is null)
            {
                // Unbound tab — Dispose is a no-op but keep symmetry.
                await sub.DisposeAsync().ConfigureAwait(true);
            }
        }
        else
        {
            await tab.DisposeAsync().ConfigureAwait(true);
        }
        int idx = Tabs.IndexOf(tab);
        Dispatcher.UIThread.Post(() =>
        {
            Tabs.Remove(tab);
            if (Tabs.Count > 0)
            {
                SelectedTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
            }
            else
            {
                SelectedTab = null;
            }
        });
    }

    /// <summary>
    /// Builds an in-memory <see cref="SessionFile"/> snapshot of the
    /// current connection + open tabs + monitored items.  Used by the
    /// File → Save Session button on the toolbar.
    /// </summary>
    public SessionFile SnapshotSession()
    {
        var file = new SessionFile
        {
            EndpointUrl = EndpointUrl,
            Engine = Engine.ToString()
        };
        foreach (IPlugin generic in Tabs)
        {
            if (generic is not SubscriptionViewModel t)
            {
                continue;
            }

            var tab = new SessionFile.TabSnapshot
            {
                Title = t.Title,
                PublishingInterval = SessionFile.TimeSpanMs.From(t.Subscription.PublishingInterval),
                LifetimeCount = t.Subscription.LifetimeCount,
                KeepAliveCount = t.Subscription.KeepAliveCount,
                MaxNotificationsPerPublish = t.Subscription.MaxNotificationsPerPublish,
                Priority = t.Subscription.Priority,
                PublishingEnabled = t.Subscription.PublishingEnabled,
                MinPublishRequestCount = t.Subscription.MinPublishRequestCount,
                MaxPublishRequestCount = t.Subscription.MaxPublishRequestCount,
                AnimationMode = t.AnimationMode.ToString(),
                AnimationTimeScale = t.AnimationTimeScale,
                ShowResourceOverlay = t.ShowResourceOverlay
            };
            foreach (MonitoredItemConfig i in t.Items)
            {
                tab.Items.Add(new SessionFile.ItemSnapshot
                {
                    DisplayName = i.DisplayName,
                    NodeId = i.NodeId.ToString() ?? "",
                    AttributeId = i.AttributeId,
                    SamplingInterval = SessionFile.TimeSpanMs.From(i.SamplingInterval),
                    QueueSize = i.QueueSize,
                    DiscardOldest = i.DiscardOldest,
                    MonitoringMode = (byte)i.MonitoringMode,
                    IsEvent = i.IsEvent
                });
            }
            file.Tabs.Add(tab);
        }
        return file;
    }

    /// <summary>
    /// Restores a previously-saved session: disconnects, sets endpoint +
    /// engine, connects, then recreates each tab and its monitored items
    /// in order.  Called from the toolbar Load Session button.
    /// </summary>
    public async Task LoadSessionAsync(SessionFile file)
    {
        try
        {
            if (Connection.IsConnected)
            {
                await Connection.DisconnectAsync().ConfigureAwait(true);
            }
            EndpointUrl = file.EndpointUrl;
            if (Enum.TryParse(file.Engine, true, out SubscriptionEngineKind eng))
            {
                Engine = eng;
                EngineButtonText = $"↻ Engine: {Engine}";
            }
            await ConnectAsync().ConfigureAwait(true);
            // Drop the auto-created default tab so we replace it with the
            // user's saved tabs.
            while (Tabs.Count > 0)
            {
                await CloseTabAsync(Tabs[0]).ConfigureAwait(true);
            }
            foreach (SessionFile.TabSnapshot ts in file.Tabs)
            {
                var cfg = new SubscriptionConfig
                {
                    PublishingInterval = ts.PublishingInterval.ToTimeSpan(),
                    LifetimeCount = ts.LifetimeCount,
                    KeepAliveCount = ts.KeepAliveCount,
                    MaxNotificationsPerPublish = ts.MaxNotificationsPerPublish,
                    Priority = ts.Priority,
                    PublishingEnabled = ts.PublishingEnabled,
                    MinPublishRequestCount = ts.MinPublishRequestCount,
                    MaxPublishRequestCount = ts.MaxPublishRequestCount
                };
                ISubscriptionAdapter adapter = Connection.CreateAdapter();
                var vm = new SubscriptionViewModel(ts.Title, adapter, m_log) { Subscription = cfg };
                // Restore per-tab UI state (defaults gracefully on older saves).
                if (Enum.TryParse(ts.AnimationMode, true, out UaLens.Views.AnimationMode mode))
                {
                    vm.AnimationMode = mode;
                }
                vm.AnimationTimeScale = ts.AnimationTimeScale > 0 ? ts.AnimationTimeScale : 1.0;
                vm.ShowResourceOverlay = ts.ShowResourceOverlay;
                await adapter.ApplySubscriptionAsync(cfg, CancellationToken.None).ConfigureAwait(true);
                vm.RefreshStatus();
                Tabs.Add(vm);
                SelectedTab = vm;
                foreach (SessionFile.ItemSnapshot item in ts.Items)
                {
                    await vm.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
                    {
                        DisplayName = item.DisplayName,
                        NodeId = NodeId.Parse(item.NodeId),
                        AttributeId = item.AttributeId,
                        SamplingInterval = item.SamplingInterval.ToTimeSpan(),
                        QueueSize = item.QueueSize,
                        DiscardOldest = item.DiscardOldest,
                        MonitoringMode = (MonitoringMode)item.MonitoringMode,
                        IsEvent = item.IsEvent
                    }).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "LoadSession failed.");
            ConnectionStatus = $"● Load failed: {ex.Message}";
        }
    }

    public NodeViewModel? SelectedNode => m_selectedNode;

    /// <summary>
    /// Drives the "Add Item" button enable state and decides Value vs Event
    /// subscription mode based on the selected node's class.  Variables go
    /// straight to "addable, value-mode"; Objects asynchronously read their
    /// EventNotifier attribute and become "addable, event-mode" only when
    /// the SubscribeToEvents bit is set; everything else (Method, DataType,
    /// ObjectType, …) is non-addable.
    /// </summary>
    public async Task UpdateSelectionAsync(NodeViewModel? node)
    {
        m_selectedNode = node;
        m_selectionCts?.Cancel();
        m_selectionCts = new CancellationTokenSource();
        CancellationToken ct = m_selectionCts.Token;

        if (node is null)
        {
            CanAddSelectedItem = false;
            SelectedItemIsEvent = false;
            SelectionHasEvents = false;
            SelectionHasVariables = false;
            CanCallMethod = false;
            CanWriteVariable = false;
            SelectionVariables = Array.Empty<(NodeId, string)>();
            SelectedItemStatus = "Pick a Variable, or an Object that emits events.";
            return;
        }

        if (node.NodeClass == NodeClass.Variable)
        {
            SelectedItemIsEvent = false;
            SelectionHasEvents = false;
            SelectionHasVariables = false;
            CanCallMethod = false;
            SelectionVariables = Array.Empty<(NodeId, string)>();
            CanAddSelectedItem = true;
            // Read AccessLevel to drive the Write Value context-menu entry.
            CanWriteVariable = false;
            SelectedItemStatus = $"○ Variable · probing AccessLevel…";
            try
            {
                if (Connection.Session is { } session)
                {
                    ArrayOf<ReadValueId> ids =
                    [
                        new ReadValueId { NodeId = node.NodeId, AttributeId = Opc.Ua.Attributes.AccessLevel }
                    ];
                    ReadResponse resp = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, ids, ct).ConfigureAwait(true);
                    if (ct.IsCancellationRequested || !ReferenceEquals(node, m_selectedNode))
                    {
                        return;
                    }
                    if (resp.Results.Count > 0
                        && !StatusCode.IsBad(resp.Results[0].StatusCode)
                        && resp.Results[0].WrappedValue.TryGetValue(out byte access))
                    {
                        CanWriteVariable = (access & AccessLevels.CurrentWrite) != 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                m_log.LogDebug(ex, "AccessLevel read failed for {NodeId}", node.NodeId);
            }
            SelectedItemStatus = CanWriteVariable
                ? $"○ Variable (writable) · {node.NodeId}"
                : $"○ Variable (read-only) · {node.NodeId}";
            return;
        }

        if (node.NodeClass == NodeClass.Method)
        {
            CanAddSelectedItem = false;
            SelectedItemIsEvent = false;
            SelectionHasEvents = false;
            SelectionHasVariables = false;
            CanWriteVariable = false;
            SelectionVariables = Array.Empty<(NodeId, string)>();
            CanCallMethod = true;
            SelectedItemStatus = $"▶ Method · {node.NodeId}";
            return;
        }

        if (node.NodeClass == NodeClass.Object)
        {
            SelectedItemIsEvent = false;
            SelectedItemStatus = $"◉ Probing {node.NodeId}…";
            CanAddSelectedItem = false;
            SelectionHasEvents = false;
            SelectionHasVariables = false;
            CanCallMethod = false;
            CanWriteVariable = false;
            SelectionVariables = Array.Empty<(NodeId, string)>();

            byte? notifier;
            IReadOnlyList<(NodeId NodeId, string DisplayName)> vars;
            try
            {
                Task<byte?> notifierTask = Browser.GetEventNotifierAsync(node.NodeId, ct);
                Task<IReadOnlyList<(NodeId, string)>> varsTask = Browser.GetChildVariablesAsync(node.NodeId, ct);
                await Task.WhenAll(notifierTask, varsTask).ConfigureAwait(true);
                notifier = notifierTask.Result;
                vars = varsTask.Result;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (ct.IsCancellationRequested || !ReferenceEquals(node, m_selectedNode))
            {
                return;
            }
            bool subscribesToEvents = notifier.HasValue
                && (notifier.Value & EventNotifiers.SubscribeToEvents) != 0;

            SelectionHasEvents = subscribesToEvents;
            SelectionHasVariables = vars.Count > 0;
            SelectionVariables = vars;
            // Default to event mode when only events are available so the existing
            // single-AddItemDialog path stays consistent for event-only Objects.
            SelectedItemIsEvent = subscribesToEvents && vars.Count == 0;
            CanAddSelectedItem = SelectionHasEvents || SelectionHasVariables;

            string evt = subscribesToEvents
                // notifier.HasValue is implied by subscribesToEvents being true.
                ? $"events (EN=0x{notifier!.Value:X2})"
                : "no events";
            string varSummary = vars.Count switch
            {
                0 => "no child variables",
                1 => "1 child variable",
                _ => $"{vars.Count} child variables"
            };
            SelectedItemStatus = CanAddSelectedItem
                ? $"◉ {node.NodeId} · {evt} · {varSummary}"
                : $"◉ {node.NodeId} · {evt} · {varSummary} — nothing to subscribe.";
            return;
        }

        SelectedItemIsEvent = false;
        SelectionHasEvents = false;
        SelectionHasVariables = false;
        CanCallMethod = false;
        CanWriteVariable = false;
        SelectionVariables = Array.Empty<(NodeId, string)>();
        SelectedItemStatus = $"{node.NodeClass} nodes cannot be subscribed.";
        CanAddSelectedItem = false;
    }

    private void SyncFromConnection()
    {
        IsConnected = Connection.IsConnected;
        if (Connection.IsConnected)
        {
            ConnectionStatus = string.Format(CultureInfo.InvariantCulture,
                "● Connected — {0}", Connection.Engine);
            // Bind any pre-existing unbound Subscription tabs to fresh adapters.
            foreach (IPlugin tab in Tabs.ToArray())
            {
                if (tab is SubscriptionViewModel sub && !sub.IsBound)
                {
                    ISubscriptionAdapter adapter = Connection.CreateAdapter();
                    _ = sub.AttachAdapterAsync(adapter);
                }
            }
            // First connect with no tabs at all — auto-create the default tab.
            if (Tabs.Count == 0)
            {
                _ = AddTabCommand.ExecuteAsync(null);
            }
        }
        else
        {
            ConnectionStatus = "● Disconnected";
            // Detach (not remove!) the adapter on each Subscription tab so
            // the user keeps the tab + its items list across the disconnect.
            // Non-Subscription tabs stay as-is.
            foreach (IPlugin tab in Tabs.ToArray())
            {
                if (tab is SubscriptionViewModel sub && sub.IsBound)
                {
                    // Fire-and-forget the ValueTask detach.  AsTask
                    // materialises a Task so the discard is explicit
                    // (CA2012 — ValueTasks must be awaited or converted).
                    _ = sub.DetachAdapterAsync().AsTask();
                }
            }
            Attributes.Clear();
            References.Clear();
        }
        EngineButtonText = $"↻ Engine: {Engine}";
    }

    /// <summary>
    /// Fan out <see cref="ConnectionService.StateChanged"/> to every open
    /// plug-in via <see cref="IPlugin.OnConnectionStateChanged"/> so each
    /// can refresh its own command CanExecute / cached state without
    /// having to subscribe to the event individually (avoids the
    /// subscribe / unsubscribe-on-Dispose boilerplate and the risk of
    /// leaking handlers).
    /// </summary>
    /// <remarks>
    /// Always runs on the UI thread via <c>Dispatcher.UIThread.Post</c>.
    /// We snapshot <see cref="Tabs"/> before iterating because an
    /// override may mutate the collection (e.g. close itself), and
    /// swallow per-tab exceptions so a single misbehaving plug-in
    /// can't break the others' notification.
    /// </remarks>
    private void NotifyPluginsOfConnectionChange()
    {
        foreach (IPlugin tab in Tabs.ToArray())
        {
            try
            {
                tab.OnConnectionStateChanged();
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex,
                    "Plug-in {Kind} threw from OnConnectionStateChanged — continuing with the next tab.",
                    tab.Kind);
            }
        }
    }

    /// <summary>
    /// When the active tab changes, mirror its adapter into
    /// <see cref="ConnectionService.Adapter"/> so legacy call sites
    /// (status text, header callbacks) observe the right one.  Also
    /// raises PropertyChanged for the IsXxxTabActive helpers so the
    /// XAML visibility bindings refresh.
    /// </summary>
    partial void OnSelectedTabChanged(IPlugin? oldValue, IPlugin? newValue)
    {
        oldValue?.OnDeactivated();
        Connection.Adapter = (newValue as SubscriptionViewModel)?.Adapter;
        OnPropertyChanged(nameof(SelectedSubscriptionTab));
        OnPropertyChanged(nameof(IsSubscriptionTabActive));
        OnPropertyChanged(nameof(IsCustomTabActive));
        newValue?.OnActivated();
    }

    private void PumpLog()
    {
        long total = m_logBuffer.TotalWritten;
        if (total == m_lastLogIndex)
        {
            return;
        }
        List<LogEntry> snap = m_logBuffer.SnapshotList();
        long start = Math.Max(m_lastLogIndex, total - snap.Count);
        long skip = start - (total - snap.Count);
        for (int i = (int)skip; i < snap.Count; i++)
        {
            LogEntry e = snap[i];
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} {1,-5} {2}: {3}",
                e.TimestampUtc.ToLocalTime(),
                LevelTag(e.Level),
                e.Category, e.Message);
            LogLines.Add(line);
            while (LogLines.Count > 1000)
            {
                LogLines.RemoveAt(0);
            }
        }
        m_lastLogIndex = total;
    }

    private static string LevelTag(LogLevel l) => l switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "err ",
        LogLevel.Critical => "crit",
        _ => "   "
    };

    public async ValueTask DisposeAsync()
    {
        m_logPump.Stop();
        m_resourcePump.Stop();
        try
        { m_selectionCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        m_selectionCts?.Dispose();
        m_selectionCts = null;
        Attributes.Dispose();
        References.Dispose();
        await Connection.DisposeAsync().ConfigureAwait(false);
    }
}
