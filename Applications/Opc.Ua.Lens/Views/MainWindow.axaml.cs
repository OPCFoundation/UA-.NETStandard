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
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Views;

internal sealed partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel m_vm;

    // File-picker pattern arrays — hoisted to static readonly so each
    // OpenFilePicker / SaveFilePicker invocation doesn't allocate fresh
    // (analyzer CA1861).
    private static readonly string[] s_csvPatterns = ["*.csv"];
    private static readonly string[] s_jsonPatterns = ["*.json"];
    private static readonly string[] s_sessionPatterns = ["*.subex", "*.json"];
    private static readonly string[] s_xmlPatterns = ["*.xml"];

    /// <summary>
    /// Set by <c>Program.cs</c> before Avalonia starts the desktop lifetime so
    /// the freshly-constructed <see cref="MainViewModel"/> can attach to the
    /// already-running <see cref="Diagnostics.ResourceMonitorHost"/>.
    /// </summary>
    public static UaLens.Diagnostics.ResourceMonitorHost? PendingResourceMonitor { get; set; }

    public MainWindow()
    {
        m_vm = new MainViewModel { ResourceMonitor = PendingResourceMonitor };
        DataContext = m_vm;
        InitializeComponent();
        WireUp();
        Closed += async (_, _) =>
        {
            // Dispose the view-model on window close so its
            // CancellationTokenSources, log pump, and connection get
            // released deterministically.
            try
            { await m_vm.DisposeAsync().ConfigureAwait(false); }
            catch { /* shutdown is best-effort */ }
        };
    }

    /// <summary>
    /// Implements IDisposable to satisfy CA1001 (owning disposable field
    /// m_vm).  The Closed handler above handles the async disposal during
    /// normal app shutdown; this method is here for the rare programmatic
    /// teardown case.  Fire-and-forget the async dispose so we don't block
    /// callers; the underlying VM uses bounded timers + cancellation.
    /// </summary>
    public void Dispose()
    {
        _ = m_vm.DisposeAsync().AsTask();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp()
    {
        var connectBtn = this.RequiredControl<Button>("ConnectButton");
        var toggleBtn = this.RequiredControl<Button>("ToggleEngineButton");
        var addBtn = this.RequiredControl<Button>("AddItemButton");
        var removeBtn = this.RequiredControl<Button>("RemoveItemButton");
        var settingsBtn = this.RequiredControl<Button>("SettingsButton");
        var logPanel = this.RequiredControl<Border>("LogPanel");
        var anim = this.RequiredControl<AnimationCanvas>("Animation");
        var liveTree = this.RequiredControl<AddressSpaceView>("LiveTree");
        var viewModeCombo = this.RequiredControl<ComboBox>("ViewModeCombo");
        var timeScaleDown = this.RequiredControl<Button>("TimeScaleDownBtn");
        var timeScaleUp = this.RequiredControl<Button>("TimeScaleUpBtn");
        var timeScaleLbl = this.RequiredControl<TextBlock>("TimeScaleLabel");
        var refreshBtn = this.RequiredControl<Button>("RefreshAddressSpaceBtn");
        var tabStrip = this.RequiredControl<ListBox>("TabStrip");
        var resourceCheck = this.RequiredControl<CheckBox>("ResourceOverlayCheck");
        var diagnosticsPanel = this.RequiredControl<DiagnosticsView>("DiagnosticsPanel");

        // Menu items.
        var menuLoad = this.RequiredControl<MenuItem>("MenuLoadSession");
        var menuSave = this.RequiredControl<MenuItem>("MenuSaveSession");
        var menuExport = this.RequiredControl<MenuItem>("MenuExport");
        var menuExportTab = this.RequiredControl<MenuItem>("MenuExportTab");
        var menuQuit = this.RequiredControl<MenuItem>("MenuQuit");
        var menuCerts = this.RequiredControl<MenuItem>("MenuCertificates");
        var menuAddTab = this.RequiredControl<MenuItem>("MenuNewSubscription");
        var menuNewGdsPush = this.RequiredControl<MenuItem>("MenuNewGdsPush");
        var menuNewGdsManagement = this.RequiredControl<MenuItem>("MenuNewGdsManagement");
        var menuNewPerformance = this.RequiredControl<MenuItem>("MenuNewPerformance");
        var menuNewEventView = this.RequiredControl<MenuItem>("MenuNewEventView");
        var menuNewHistorian = this.RequiredControl<MenuItem>("MenuNewHistorian");
        var menuNewFileSystem = this.RequiredControl<MenuItem>("MenuNewFileSystem");
        var menuRenameTab = this.RequiredControl<MenuItem>("MenuRenameTab");
        var menuCloseTab = this.RequiredControl<MenuItem>("MenuCloseTab");
        var menuAddItem = this.RequiredControl<MenuItem>("MenuAddItemMenu");
        var menuAddRecursive = this.RequiredControl<MenuItem>("MenuAddRecursive");
        var menuRemoveItem = this.RequiredControl<MenuItem>("MenuRemoveItem");
        var menuSubSettings = this.RequiredControl<MenuItem>("MenuSettings");
        var menuDiag = this.RequiredControl<MenuItem>("MenuToggleDiag");
        var menuLog = this.RequiredControl<MenuItem>("MenuToggleLog");
        var menuAS = this.RequiredControl<MenuItem>("MenuToggleAddressSpace");
        var menuAttrs = this.RequiredControl<MenuItem>("MenuToggleAttrs");
        var menuRefs = this.RequiredControl<MenuItem>("MenuToggleRefs");
        var menuAbout = this.RequiredControl<MenuItem>("MenuAbout");

        // Connection panel Change ▾ flyout items.
        var menuConnDisconnect = this.RequiredControl<MenuItem>("MenuConnectionDisconnect");
        var menuConnChangeUser = this.RequiredControl<MenuItem>("MenuConnectionChangeUser");
        var menuConnReconnect = this.RequiredControl<MenuItem>("MenuConnectionReconnect");

        connectBtn.Click += async (_, _) => await OnConnect().ConfigureAwait(false);
        menuConnDisconnect.Click += async (_, _) => await OnDisconnect().ConfigureAwait(false);
        menuConnChangeUser.Click += async (_, _) => await OnChangeUser().ConfigureAwait(false);
        menuConnReconnect.Click += async (_, _) => await OnReconnect().ConfigureAwait(false);
        toggleBtn.Click += async (_, _) => await m_vm.ToggleEngineCommand.ExecuteAsync(null).ConfigureAwait(false);

        addBtn.Click += async (_, _) => await OnAddItem().ConfigureAwait(false);
        removeBtn.Click += async (_, _) => await OnRemoveItem().ConfigureAwait(false);
        settingsBtn.Click += async (_, _) => await OnSettings().ConfigureAwait(false);

        refreshBtn.Click += (_, _) => m_vm.Browser.Reload();

        // --- File menu ---
        menuLoad.Click += async (_, _) => await OnLoadSessionAsync().ConfigureAwait(false);
        menuSave.Click += async (_, _) => await OnSaveSessionAsync().ConfigureAwait(false);
        menuExport.Click += async (_, _) => await OnExportNodeSetAsync().ConfigureAwait(false);
        menuExportTab.Click += async (_, _) => await OnExportTabDataAsync().ConfigureAwait(false);
        menuQuit.Click += (_, _) => Close();

        // --- Certificates menu ---
        menuCerts.Click += async (_, _) => await OnOpenCertStoreAsync().ConfigureAwait(false);

        // --- Subscription menu ---
        menuAddTab.Click += async (_, _) =>
        {
            if (!m_vm.IsConnected)
            {
                return;
            }

            try
            {
                await m_vm.AddTabCommand.ExecuteAsync(null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddTab failed: {ex}");
            }
        };
        menuAddItem.Click += async (_, _) => await OnAddItem().ConfigureAwait(false);
        menuAddRecursive.Click += async (_, _) => await OnAddRecursivelyAsync().ConfigureAwait(false);
        menuRemoveItem.Click += async (_, _) => await OnRemoveItem().ConfigureAwait(false);
        menuSubSettings.Click += async (_, _) => await OnSettings().ConfigureAwait(false);

        // --- Tabs → New … wiring ---
        menuNewGdsPush.Click += async (_, _) => await m_vm.AddPluginAsync(PluginKind.GdsPush).ConfigureAwait(false);
        menuNewGdsManagement.Click += async (_, _) => await m_vm.AddPluginAsync(PluginKind.GdsManagement).ConfigureAwait(false);
        var menuNewGdsDiscovery = this.RequiredControl<MenuItem>("MenuNewGdsDiscovery");
        menuNewGdsDiscovery.Click += async (_, _) => await m_vm.AddPluginAsync(PluginKind.GdsDiscovery).ConfigureAwait(false);
        var menuLocales = this.RequiredControl<MenuItem>("MenuLocales");
        menuLocales.Click += async (_, _) => await OnLocalesAsync().ConfigureAwait(false);
        menuNewPerformance.Click += async (_, _) => await m_vm.AddPluginAsync(PluginKind.Performance).ConfigureAwait(false);
        menuNewEventView.Click += async (_, _) => await m_vm.AddPluginAsync(PluginKind.EventView).ConfigureAwait(false);
        menuNewHistorian.Click += async (_, _) => await m_vm.AddPluginAsync(PluginKind.Historian).ConfigureAwait(false);
        menuNewFileSystem.Click += async (_, _) => await m_vm.AddPluginAsync(PluginKind.FileSystem).ConfigureAwait(false);

        // --- Tabs → Rename / Duplicate / Close Active Tab ---
        menuRenameTab.Click += (_, _) =>
        {
            if (m_vm.SelectedTab is { } t)
            {
                BeginInPlaceRename(t);
            }
        };
        menuCloseTab.Click += async (_, _) =>
        {
            if (m_vm.SelectedTab is { } t)
            {
                await m_vm.CloseTabCommand.ExecuteAsync(t).ConfigureAwait(true);
            }
        };

        // --- View menu: Diagnostics ---
        diagnosticsPanel.BindPublishLog(m_vm.PublishLog);
        menuDiag.Click += (_, _) =>
        {
            bool show = menuDiag.IsChecked;
            diagnosticsPanel.IsVisible = show;
            diagnosticsPanel.Bind(show ? m_vm.Connection.Session : null);
        };
        diagnosticsPanel.HideRequested += () =>
        {
            menuDiag.IsChecked = false;
            diagnosticsPanel.IsVisible = false;
            diagnosticsPanel.Bind(null);
        };

        // --- View menu: Log ---
        menuLog.Click += (_, _) =>
        {
            logPanel.IsVisible = menuLog.IsChecked;
        };

        // --- View menu: Address Space + nested Attributes/References ---
        menuAS.Click += (_, _) =>
        {
            m_vm.IsAddressSpaceVisible = menuAS.IsChecked;
            ApplyAddressSpaceVisibility(menuAS.IsChecked);
            UpdateViewMenuEnabled();
        };
        menuAttrs.Click += (_, _) =>
        {
            m_vm.AttributesPanelMode = ComposePanelMode(menuAttrs.IsChecked, menuRefs.IsChecked);
            UpdateLeftStackRows();
        };
        menuRefs.Click += (_, _) =>
        {
            m_vm.AttributesPanelMode = ComposePanelMode(menuAttrs.IsChecked, menuRefs.IsChecked);
            UpdateLeftStackRows();
        };

        // --- Help menu ---
        menuAbout.Click += (_, _) => ShowHelp();

        // Sync menu check state from the view model's initial state.
        SyncViewMenuFromVm(menuAttrs, menuRefs);
        UpdateViewMenuEnabled();
        UpdateConnectionIndicator();
        UpdateTabStatusLabel();
        // Connection state change: refresh menu + indicator.
        m_vm.Connection.StateChanged += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateViewMenuEnabled();
                UpdateConnectionIndicator();
                UpdateTabStatusLabel();
            });
        // 2 Hz KA poll: indicator can transition green → amber even when
        // ConnectionState doesn't fire (KeepAliveStopped is a property).
        var connIndicatorTimer = new Avalonia.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(500),
            Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => UpdateConnectionIndicator());
        connIndicatorTimer.Start();
        // 1 Hz tab status counters.
        var tabStatusTimer = new Avalonia.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => UpdateTabStatusLabel());
        tabStatusTimer.Start();

        // Per-tab × close — bubbled click from anywhere in the tab strip.
        tabStrip.AddHandler(Button.ClickEvent, async (object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        {
            if (e.Source is Button { Name: "CloseTabButton" } btn
                && btn.Tag is UaLens.ViewModels.IPlugin tab)
            {
                try
                {
                    await m_vm.CloseTabCommand.ExecuteAsync(tab).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CloseTab failed: {ex}");
                }
                e.Handled = true;
            }
        });

        // Right-click tab context menu — Rename / Duplicate / Close.
        // MenuItem.Click bubbles, so a single handler on the strip dispatches all 3.
        tabStrip.AddHandler(MenuItem.ClickEvent, async (object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        {
            if (e.Source is not MenuItem mi)
            {
                return;
            }

            if (mi.DataContext is not UaLens.ViewModels.IPlugin tab)
            {
                return;
            }

            try
            {
                switch (mi.Name)
                {
                    case "TabMenuRename":
                        BeginInPlaceRename(tab);
                        break;
                    case "TabMenuDuplicate":
                        if (tab is UaLens.ViewModels.SubscriptionViewModel sub)
                        {
                            await OnDuplicateTab(sub).ConfigureAwait(true);
                        }
                        break;
                    case "TabMenuClose":
                        await m_vm.CloseTabCommand.ExecuteAsync(tab).ConfigureAwait(true);
                        break;
                }
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tab context action failed: {ex}");
            }
        });

        // Double-click a tab title → in-place rename.
        tabStrip.AddHandler(InputElement.DoubleTappedEvent,
            (object? _, Avalonia.Input.TappedEventArgs e) =>
        {
            if (e.Source is Avalonia.StyledElement el
                && el.DataContext is UaLens.ViewModels.IPlugin tab)
            {
                BeginInPlaceRename(tab);
                e.Handled = true;
            }
        });

        // Enter / Escape on a rename TextBox — commit or cancel.
        tabStrip.AddHandler(InputElement.KeyDownEvent,
            (object? _, Avalonia.Input.KeyEventArgs e) =>
        {
            if (e.Source is not TextBox tb)
            {
                return;
            }

            if (tb.DataContext is not UaLens.ViewModels.IPlugin tab)
            {
                return;
            }

            if (!tab.IsRenaming)
            {
                return;
            }

            if (e.Key == Avalonia.Input.Key.Enter)
            {
                // CompiledBinding Mode=TwoWay has already written tb.Text into tab.Title.
                tab.IsRenaming = false;
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.Escape)
            {
                // Bail out without committing — but the TwoWay binding may have
                // already flushed.  Best-effort: leave whatever the user typed
                // since reverting requires snapshotting the title at edit-start.
                tab.IsRenaming = false;
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // LostFocus on rename TextBox — auto-commit when user clicks away.
        tabStrip.AddHandler(InputElement.LostFocusEvent,
            (object? _, Avalonia.Interactivity.RoutedEventArgs e) =>
        {
            if (e.Source is TextBox tb
                && tb.DataContext is UaLens.ViewModels.IPlugin tab
                && tab.IsRenaming)
            {
                tab.IsRenaming = false;
            }
        });

        // Initial sizing + label.
        UpdateLeftStackRows();
        m_vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AttributesPanelMode))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UpdateLeftStackRows();
                    SyncViewMenuFromVm(menuAttrs, menuRefs);
                });
            }
            else if (e.PropertyName == nameof(MainViewModel.CanAddSelectedItem))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(UpdateViewMenuEnabled);
            }
        };

        // Selection in the address-space tree drives both the attribute panel
        // and the "Add Item" enable / mode state on the Subscription panel.
        liveTree.NodeSelected += async n =>
        {
            try
            {
                Task attrs = m_vm.Attributes.LoadAsync(n.NodeId, n.NodeClass);
                Task refs = m_vm.References.LoadAsync(n.NodeId, n.NodeClass);
                Task sel = m_vm.UpdateSelectionAsync(n);
                await Task.WhenAll(attrs, refs, sel).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NodeSelected handler failed: {ex}");
            }
        };

        // Address-space context menu — visibility policy + wire actions.
        liveTree.ContextMenuPolicy = new ContextMenuPolicy(m_vm);
        liveTree.AddItemRequested += async _ => await OnAddItem().ConfigureAwait(false);
        liveTree.AddRecursivelyRequested += async _ => await OnAddRecursivelyAsync().ConfigureAwait(false);
        liveTree.CallMethodRequested += async n => await OnCallMethod(n).ConfigureAwait(false);
        liveTree.WriteValueRequested += async n => await OnWriteValue(n).ConfigureAwait(false);
        liveTree.ReadHistoryRequested += async _ =>
            await m_vm.AddPluginAsync(PluginKind.Historian).ConfigureAwait(false);
        liveTree.ShowEventsRequested += async n =>
            await m_vm.AddPluginAsync(PluginKind.EventView, seedEventSource: n).ConfigureAwait(false);
        liveTree.PerfRequested += async _ =>
            await m_vm.AddPluginAsync(PluginKind.Performance, seedPickTarget: true).ConfigureAwait(false);
        liveTree.ExportValueRequested += async n => await OnExportValueAsync(n).ConfigureAwait(false);
        liveTree.FindByPathRequested += n => OnFindByPath(n);
        liveTree.ViewNodeStateRequested += n => OnViewNodeState(n);

        // Animation view-mode dropdown — Dots (0) / Bars (1) / Lines (2) / Signal (3) / Histogram (4) / Heatmap (5).
        // Per-tab: reads/writes m_vm.SelectedTab?.AnimationMode.  Re-syncs on tab switch.
        SyncTabUiState();
        viewModeCombo.SelectionChanged += (_, _) =>
        {
            if (m_vm.SelectedTab is not SubscriptionViewModel tab)
            {
                return;
            }
            AnimationMode mode = viewModeCombo.SelectedIndex switch
            {
                1 => AnimationMode.Bars,
                2 => AnimationMode.Lines,
                3 => AnimationMode.Signal,
                4 => AnimationMode.Histogram,
                5 => AnimationMode.Heatmap,
                _ => AnimationMode.Dots
            };
            tab.AnimationMode = mode;
            ApplyMode(mode);
        };

        anim.GetHeaderText = () =>
        {
            if (m_vm.Connection.Adapter is { } a && m_vm.Connection.IsConnected)
            {
                return $"engine={m_vm.Connection.Engine}   " +
                       $"pub={a.CurrentPublishingInterval.TotalMilliseconds:0}ms   " +
                       $"KA={a.CurrentKeepAliveCount}   life={a.CurrentLifetimeCount}   " +
                       $"workers={a.PublishWorkerCount}/{a.MinPublishWorkerCount}-{a.MaxPublishWorkerCount}   " +
                       $"in-flight={a.GoodPublishRequestCount}/{a.MinPublishRequestCount}-{a.MaxPublishRequestCount}   " +
                       $"bad={a.BadPublishRequestCount}";
            }
            return "(disconnected — click ⏻ Connect)";
        };

        // Resource overlay: callback returning live (cpu%, memMiB) numeric sample.
        anim.GetResourceSample = () => m_vm.ResourceMonitor?.SampleNumeric() ?? (double.NaN, 0);

        // CPU/Mem checkbox — per-tab.
        resourceCheck.IsCheckedChanged += (_, _) =>
        {
            if (m_vm.SelectedTab is SubscriptionViewModel tab && resourceCheck.IsChecked is { } v)
            {
                tab.ShowResourceOverlay = v;
                anim.ShowResourceOverlay = v;
                anim.InvalidateVisual();
            }
        };

        // Per-item lanes / bars / legend rely on the live items list of the
        // currently-active Subscription tab (each tab keeps its own Items
        // collection).  Non-Subscription tabs have no items.
        anim.GetItems = () => m_vm.SelectedSubscriptionTab is { } st
            ? st.Items.ToArray()
            : Array.Empty<MonitoredItemConfig>();

        // Surface the gap/republish/drop counters from the engine on the seq line.
        anim.GetGapMetrics = () =>
        {
            if (m_vm.Connection.Adapter is { } a && m_vm.Connection.IsConnected)
            {
                return (a.MissingMessageCount, a.RepublishMessageCount, a.DroppedNotificationCount);
            }
            return (0L, 0L, 0L);
        };

        timeScaleDown.Click += (_, _) =>
        {
            ZoomOut();
        };
        timeScaleUp.Click += (_, _) =>
        {
            ZoomIn();
        };

        var sp = this.RequiredControl<ScottPlotView>("ScottPlot");

        m_vm.Connection.StateChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            anim.Bind(m_vm.Connection.Adapter?.Events, m_vm.Connection.Adapter?.Counters, m_vm.SelectedSubscriptionTab?.Recorder);
            if (m_vm.SelectedTab is SubscriptionViewModel tab && ScottPlotView.IsScottPlotMode(tab.AnimationMode))
            {
                ApplyMode(tab.AnimationMode);
            }
        });

        // When the user switches tabs, rebind the canvas to the new
        // adapter AND mirror the new tab's per-tab UI state (mode,
        // time-scale, overlay) onto the toolbar widgets + canvas.
        m_vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    AttachSelectedTabListener();
                    AttachItemsListener();
                    var sst = m_vm.SelectedSubscriptionTab;
                    var ssa = sst?.Adapter;
                    anim.Bind(ssa?.Events, ssa?.Counters, sst?.Recorder);
                    SyncTabUiState();
                    UpdateViewMenuEnabled();
                    UpdateTabStatusLabel();
                    anim.InvalidateVisual();
                });
            }
        };

        AttachSelectedTabListener();
        AttachItemsListener();

        // Dynamic per-kind menu injection — the currently-selected
        // non-Subscription plugin contributes its top-level menu via
        // IPlugin.ContributeMenuItems(), inserted after
        // DynamicKindMenuAnchor.  Switching to a different plugin swaps the
        // kind menu in / out.  Subscription's menu stays statically wired in
        // XAML (gated via IsSubscriptionTabActive).
        m_vm.Tabs.CollectionChanged += (_, _) => RefreshDynamicKindMenus();
        m_vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshDynamicKindMenus);
            }
        };
        RefreshDynamicKindMenus();

        KeyDown += (_, e) =>
        {
            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            // Help.
            if (e.Key == Key.F1)
            {
                InvokeMenu(menuAbout);
                e.Handled = true;
                return;
            }

            // Ctrl+Tab / Ctrl+Shift+Tab — cycle subscription tabs (wraps).
            if (ctrl && e.Key == Key.Tab)
            {
                CycleTab(shift ? -1 : +1);
                e.Handled = true;
                return;
            }

            // File.
            if (ctrl && e.Key == Key.O)
            { InvokeMenu(menuLoad); e.Handled = true; return; }
            if (ctrl && e.Key == Key.S)
            { InvokeMenu(menuSave); e.Handled = true; return; }
            if (ctrl && shift && e.Key == Key.E)
            { InvokeMenu(menuExportTab); e.Handled = true; return; }
            if (ctrl && e.Key == Key.E)
            { InvokeMenu(menuExport); e.Handled = true; return; }
            if (ctrl && e.Key == Key.Q)
            { InvokeMenu(menuQuit); e.Handled = true; return; }

            // Certificates.
            if (ctrl && e.Key == Key.K)
            { InvokeMenu(menuCerts); e.Handled = true; return; }

            // Endpoint URL focus shortcut.
            if (ctrl && e.Key == Key.U)
            {
                var urlBox = this.FindControl<TextBox>("EndpointBox");
                urlBox?.Focus();
                urlBox?.SelectAll();
                e.Handled = true;
                return;
            }

            // Subscription.
            if (ctrl && e.Key == Key.T)
            { InvokeMenu(menuAddTab); e.Handled = true; return; }
            if (ctrl && shift && e.Key == Key.I)
            { InvokeMenu(menuAddRecursive); e.Handled = true; return; }
            if (ctrl && e.Key == Key.I)
            { InvokeMenu(menuAddItem); e.Handled = true; return; }
            if (ctrl && shift && e.Key == Key.R)
            { InvokeMenu(menuRemoveItem); e.Handled = true; return; }
            if (ctrl && e.Key == Key.OemComma)
            { InvokeMenu(menuSubSettings); e.Handled = true; return; }

            // View — checkable items: toggle then invoke handler.
            if (e.Key == Key.F2)
            { ToggleMenu(menuDiag); e.Handled = true; return; }
            if (ctrl && e.Key == Key.L)
            { ToggleMenu(menuLog); e.Handled = true; return; }
            if (ctrl && e.Key == Key.B)
            { ToggleMenu(menuAS); e.Handled = true; return; }
            if (ctrl && e.Key == Key.A && !shift)
            { ToggleMenu(menuAttrs); e.Handled = true; return; }
            if (ctrl && e.Key == Key.R && !shift)
            { ToggleMenu(menuRefs); e.Handled = true; return; }

            // View-cycle and time-scale (no menu items — direct).
            if (ctrl && e.Key == Key.V)
            {
                if (m_vm.SelectedTab is not SubscriptionViewModel tab)
                { e.Handled = true; return; }
                AnimationMode next = tab.AnimationMode switch
                {
                    AnimationMode.Dots => AnimationMode.Bars,
                    AnimationMode.Bars => AnimationMode.Lines,
                    AnimationMode.Lines => AnimationMode.Signal,
                    AnimationMode.Signal => AnimationMode.Histogram,
                    AnimationMode.Histogram => AnimationMode.Heatmap,
                    _ => AnimationMode.Dots
                };
                tab.AnimationMode = next;
                ApplyMode(next);
                viewModeCombo.SelectedIndex = next switch
                {
                    AnimationMode.Bars => 1,
                    AnimationMode.Lines => 2,
                    AnimationMode.Signal => 3,
                    AnimationMode.Histogram => 4,
                    AnimationMode.Heatmap => 5,
                    _ => 0
                };
                e.Handled = true;
                return;
            }
            if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
            {
                ZoomIn();
                e.Handled = true;
                return;
            }
            if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
            {
                ZoomOut();
                e.Handled = true;
                return;
            }
            if (ctrl && e.Key == Key.D0)
            {
                ZoomReset();
                e.Handled = true;
                return;
            }
        };

        Closing += async (_, _) => await m_vm.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Adjusts the address-space column's row heights so hidden Attrs/Refs
    /// panels release their share to the tree (and to each other when only
    /// one is visible).  Called when the side-panel mode changes.
    /// </summary>
    private void UpdateLeftStackRows()
    {
        var grid = this.FindControl<Grid>("LeftStackGrid");
        if (grid is null || grid.RowDefinitions.Count < 6)
        {
            return;
        }
        var star = new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
        var zero = new Avalonia.Controls.GridLength(0);
        var splitterPx = new Avalonia.Controls.GridLength(4);

        bool a = m_vm.ShowAttributes;
        bool r = m_vm.ShowReferences;
        // Row indices: 0=Header, 1=Tree, 2=Splitter1, 3=Attrs, 4=Splitter2, 5=Refs.
        grid.RowDefinitions[3].Height = a ? star : zero;
        grid.RowDefinitions[2].Height = a ? splitterPx : zero;
        grid.RowDefinitions[5].Height = r ? star : zero;
        grid.RowDefinitions[4].Height = (a && r) ? splitterPx : zero;
    }

    // ----- View-menu helpers -----

    /// <summary>
    /// Toggle the left-hand Address-space column.  When hidden, the column
    /// definition is collapsed to 0 (including the splitter) so the main
    /// body uses the full window width.
    /// </summary>
    private void ApplyAddressSpaceVisibility(bool show)
    {
        var grid = this.FindControl<Grid>("MainBody");
        if (grid is null || grid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        grid.ColumnDefinitions[0].Width = show ? new GridLength(360) : new GridLength(0);
        grid.ColumnDefinitions[1].Width = show ? new GridLength(4) : new GridLength(0);
    }

    /// <summary>
    /// Map (showAttrs, showRefs) toggle states to <see cref="SidePanelMode"/>.
    /// </summary>
    private static SidePanelMode ComposePanelMode(bool attrs, bool refs)
    {
        if (attrs && refs)
        {
            return SidePanelMode.AttrsAndRefs;
        }

        if (attrs)
        {
            return SidePanelMode.AttrsOnly;
        }

        if (refs)
        {
            return SidePanelMode.RefsOnly;
        }

        return SidePanelMode.None;
    }

    /// <summary>Mirror the view-model's <see cref="SidePanelMode"/> onto the menu checkboxes.</summary>
    private void SyncViewMenuFromVm(MenuItem? menuAttrs, MenuItem? menuRefs)
    {
        if (menuAttrs is null || menuRefs is null)
        {
            return;
        }

        menuAttrs.IsChecked = m_vm.ShowAttributes;
        menuRefs.IsChecked = m_vm.ShowReferences;
    }

    /// <summary>
    /// Refresh the enabled state of menu items that depend on the runtime
    /// state of the app (connection alive, address-space visible, etc.).
    /// </summary>
    private void UpdateViewMenuEnabled()
    {
        var menuAS = this.FindControl<MenuItem>("MenuToggleAddressSpace");
        var menuAttrs = this.FindControl<MenuItem>("MenuToggleAttrs");
        var menuRefs = this.FindControl<MenuItem>("MenuToggleRefs");
        var menuExport = this.FindControl<MenuItem>("MenuExport");
        var menuCerts = this.FindControl<MenuItem>("MenuCertificates");
        var menuAddItem = this.FindControl<MenuItem>("MenuAddItemMenu");
        var menuAddTab = this.FindControl<MenuItem>("MenuNewSubscription");

        bool asVisible = menuAS?.IsChecked ?? true;
        if (menuAttrs is not null)
        {
            menuAttrs.IsEnabled = asVisible;
        }

        if (menuRefs is not null)
        {
            menuRefs.IsEnabled = asVisible;
        }

        bool connected = m_vm.Connection.IsConnected;
        if (menuExport is not null)
        {
            menuExport.IsEnabled = connected && asVisible;
        }

        if (menuCerts is not null)
        {
            menuCerts.IsEnabled = true; // always available
        }

        if (menuAddItem is not null)
        {
            menuAddItem.IsEnabled = connected && m_vm.CanAddSelectedItem;
        }

        if (menuAddTab is not null)
        {
            menuAddTab.IsEnabled = connected;
        }

        var menuAddRecursive = this.FindControl<MenuItem>("MenuAddRecursive");
        if (menuAddRecursive is not null)
        {
            menuAddRecursive.IsEnabled = connected && SelectedNodeIsBrowsableContainer();
        }

        var menuExportTab = this.FindControl<MenuItem>("MenuExportTab");
        if (menuExportTab is not null)
        {
            menuExportTab.IsEnabled = m_vm.SelectedTab is UaLens.ViewModels.SubscriptionViewModel;
        }
    }

    /// <summary>
    /// True iff the currently-selected node is a NodeId we can browse from
    /// (Object / ObjectType / View / Variable / VariableType).  Used to
    /// gate the Subscription → Add Recursively menu item.
    /// </summary>
    private bool SelectedNodeIsBrowsableContainer()
    {
        if (m_vm.SelectedNode is not { } n)
        {
            return false;
        }

        return n.NodeClass is NodeClass.Object or NodeClass.ObjectType
            or NodeClass.View or NodeClass.Variable or NodeClass.VariableType;
    }

    /// <summary>
    /// Updates the bottom status bar with per-active-tab counters
    /// (publishing interval, monitored item count, received notifications,
    /// dropped notifications).
    /// </summary>
    private void UpdateTabStatusLabel()
    {
        var lbl = this.FindControl<TextBlock>("TabStatusLabel");
        if (lbl is null)
        {
            return;
        }

        if (m_vm.SelectedTab is not SubscriptionViewModel tab || tab.Adapter is not { } a)
        {
            lbl.Text = "—";
            return;
        }
        SubscriptionCounters c = a.Counters;
        long received = c.DataValues + c.EventValues + c.KeepAlives;
        lbl.Text =
            $"pub={a.CurrentPublishingInterval.TotalMilliseconds:0}ms" +
            $"  items={tab.Items.Count}" +
            $"  received={received}" +
            $"  dropped={a.DroppedNotificationCount}";
    }

    /// <summary>
    /// Cached notification-count from the previous indicator poll.  Used to
    /// override <see cref="Session.KeepAliveStopped"/> when notifications
    /// are still arriving — the SDK property can latch at "stopped" if a
    /// transient keep-alive Read fails with a bad status code other than
    /// <c>Good</c> / <c>BadNoCommunication</c>, even though Publish
    /// notifications continue to flow through a separate code path.
    /// </summary>
    private long m_lastIndicatorCount;
    private DateTime m_lastIndicatorCountUtc = DateTime.MinValue;

    /// <summary>
    /// Updates the green/amber/gray indicator dot and label in the menu bar.
    /// Green = connected + activity; amber = connected but no activity AND
    /// SDK reports keep-alive stopped; gray = disconnected.
    /// </summary>
    private void UpdateConnectionIndicator()
    {
        var dot = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("ConnectionDot");
        var label = this.FindControl<TextBlock>("ConnectionDotLabel");
        if (dot is null || label is null)
        {
            return;
        }

        bool connected = m_vm.Connection.IsConnected;
        bool kaSdkLost = connected && (m_vm.Connection.Session?.KeepAliveStopped ?? false);

        // Cross-check: did we actually receive ANY notifications recently?
        // If yes, the connection is alive even if the SDK's KeepAliveStopped
        // property is latched at "stopped" because the keep-alive Read
        // returned a transient bad status.
        bool activity = false;
        if (connected && m_vm.SelectedTab is SubscriptionViewModel tab && tab.Adapter is { } a)
        {
            SubscriptionCounters c = a.Counters;
            long total = c.DataValues + c.EventValues + c.KeepAlives;
            DateTime now = DateTime.UtcNow;
            if (total > m_lastIndicatorCount)
            {
                m_lastIndicatorCount = total;
                m_lastIndicatorCountUtc = now;
                activity = true;
            }
            else if (m_lastIndicatorCountUtc != DateTime.MinValue
                && (now - m_lastIndicatorCountUtc).TotalSeconds < 6.0)
            {
                // Counter hasn't moved this poll but the last move was recent —
                // still consider the connection active.
                activity = true;
            }
        }

        bool kaLost = kaSdkLost && !activity;
        (string color, string text) = (connected, kaLost) switch
        {
            (false, _) => ("#64748B", "disconnected"),
            (true, true) => ("#FBBF24", "keep-alive lost"),
            (true, false) => ("#22C55E", "connected"),
        };
        dot.Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color));
        label.Text = text;
    }

    /// <summary>
    /// File → Export Tab Data... — dumps the active tab's
    /// <see cref="UaLens.Connection.NotificationRecorder"/>
    /// buffer to CSV or JSON via a save picker.
    /// </summary>
    private async Task OnExportTabDataAsync()
    {
        if (m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            m_vm.ConnectionStatus = "● No active subscription tab to export.";
            return;
        }
        try
        {
            string suggested = $"tab-{tab.Title}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            Avalonia.Platform.Storage.IStorageProvider sp = StorageProvider;
            Avalonia.Platform.Storage.IStorageFile? file =
                await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export tab notification history",
                    DefaultExtension = "csv",
                    SuggestedFileName = suggested,
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("CSV")  { Patterns = s_csvPatterns },
                        new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = s_jsonPatterns }
                    }
                }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }

            string path = file.Path.LocalPath;
            // Build name lookup from current items.
            var names = new Dictionary<int, string>();
            foreach (var i in tab.Items)
            {
                names[i.Id] = i.DisplayName ?? "";
            }
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await tab.Recorder.ExportJsonAsync(path, names).ConfigureAwait(true);
            }
            else
            {
                await tab.Recorder.ExportCsvAsync(path, names).ConfigureAwait(true);
            }
            m_vm.ConnectionStatus = $"● Exported tab data to {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Export tab data failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OnExportTabDataAsync failed: {ex}");
        }
    }

    /// <summary>
    /// File → Save Session... — write JSON snapshot of the current
    /// connection + tabs + monitored items via the OS save picker.
    /// </summary>
    private async Task OnSaveSessionAsync()
    {
        try
        {
            Avalonia.Platform.Storage.IStorageProvider sp = StorageProvider;
            Avalonia.Platform.Storage.IStorageFile? file =
                await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Save UaLens session",
                    DefaultExtension = "subex",
                    SuggestedFileName = "session.subex",
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("UaLens session")
                        {
                            Patterns = s_sessionPatterns
                        }
                    }
                }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }

            string path = file.Path.LocalPath;
            await UaLens.Connection.SessionFile.SaveAsync(m_vm.SnapshotSession(), path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveSession failed: {ex}");
        }
    }

    /// <summary>
    /// File → Load Session... — replay a previously-saved JSON snapshot
    /// (reconnect, recreate tabs and items).
    /// </summary>
    private async Task OnLoadSessionAsync()
    {
        try
        {
            Avalonia.Platform.Storage.IStorageProvider sp = StorageProvider;
            System.Collections.Generic.IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files =
                await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Load UaLens session",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("UaLens session")
                        {
                            Patterns = s_sessionPatterns
                        }
                    }
                }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                return;
            }

            string path = files[0].Path.LocalPath;
            UaLens.Connection.SessionFile? sf =
                await UaLens.Connection.SessionFile.LoadAsync(path).ConfigureAwait(true);
            if (sf is null)
            {
                return;
            }

            await m_vm.LoadSessionAsync(sf).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadSession failed: {ex}");
        }
    }

    /// <summary>
    /// Apply a new animation time scale, clamped to [0.125, 8].  Pushes the
    /// value into the currently-selected tab's <see cref="SubscriptionViewModel.AnimationTimeScale"/>
    /// which fires the per-tab PropertyChanged handler that forwards to the
    /// AnimationCanvas via <see cref="OnSelectedTabPropertyChanged"/>.
    /// </summary>
    private void SetTimeScale(double v)
    {
        v = Math.Clamp(v, 0.125, 8.0);
        if (m_vm.SelectedTab is SubscriptionViewModel tab)
        {
            tab.AnimationTimeScale = v;
        }
    }

    /// <summary>
    /// + button / Ctrl+Plus dispatcher: scales canvas time on canvas modes,
    /// shrinks the X axis on ScottPlot modes.  Always updates the per-tab
    /// <see cref="SubscriptionViewModel.AnimationTimeScale"/> so the label
    /// shows the current multiplier regardless of view mode.
    /// </summary>
    private void ZoomIn()
    {
        if (m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            return;
        }

        double oldScale = tab.AnimationTimeScale;
        SetTimeScale(oldScale * 2.0);
        if (oldScale == tab.AnimationTimeScale)
        {
            return; // hit clamp, skip plot zoom
        }

        if (ScottPlotView.IsScottPlotMode(tab.AnimationMode))
        {
            this.FindControl<ScottPlotView>("ScottPlot")?.ApplyXZoom(0.5);
        }
    }

    /// <summary>
    /// − button / Ctrl+Minus dispatcher: shrinks canvas time on canvas modes,
    /// widens the X axis on ScottPlot modes.  Updates the per-tab
    /// <see cref="SubscriptionViewModel.AnimationTimeScale"/> so the label
    /// stays in sync.
    /// </summary>
    private void ZoomOut()
    {
        if (m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            return;
        }

        double oldScale = tab.AnimationTimeScale;
        SetTimeScale(oldScale / 2.0);
        if (oldScale == tab.AnimationTimeScale)
        {
            return; // hit clamp
        }

        if (ScottPlotView.IsScottPlotMode(tab.AnimationMode))
        {
            this.FindControl<ScottPlotView>("ScottPlot")?.ApplyXZoom(2.0);
        }
    }

    /// <summary>
    /// Ctrl+0 dispatcher: reset time-scale on canvas modes, reset
    /// X-axis auto-fit on ScottPlot modes.  Both paths reset the per-tab
    /// <see cref="SubscriptionViewModel.AnimationTimeScale"/> to 1.0.
    /// </summary>
    private void ZoomReset()
    {
        if (m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            return;
        }

        SetTimeScale(1.0);
        if (ScottPlotView.IsScottPlotMode(tab.AnimationMode))
        {
            this.FindControl<ScottPlotView>("ScottPlot")?.ResetXZoom();
        }
    }

    private static string FormatTimeScale(double v)
    {
        if (v >= 1.0)
        {
            return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
        }
        return v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') + "×";
    }

    /// <summary>
    /// Invoke a menu item as if it had been clicked.  Skips disabled items
    /// so keyboard shortcuts respect the same enable state.
    /// </summary>
    private static void InvokeMenu(MenuItem item)
    {
        if (!item.IsEnabled)
        {
            return;
        }

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    }

    /// <summary>
    /// Cycle the active subscription tab forward (+1) or backward (-1),
    /// wrapping at the ends.  Bound to Ctrl+Tab / Ctrl+Shift+Tab.
    /// </summary>
    private void CycleTab(int direction)
    {
        if (m_vm.Tabs.Count == 0)
        {
            return;
        }

        int idx = m_vm.SelectedTab is { } t ? m_vm.Tabs.IndexOf(t) : -1;
        idx = (idx + direction + m_vm.Tabs.Count) % m_vm.Tabs.Count;
        m_vm.SelectedTab = m_vm.Tabs[idx];
    }

    /// <summary>
    /// Begin an in-place tab rename: flip <see cref="IPlugin.IsRenaming"/>
    /// to true and focus the corresponding TextBox in the TabStrip so the user
    /// can immediately start typing.  Dispatched async because the TextBox is
    /// not actually realized until the IsVisible binding flips on the UI thread.
    /// </summary>
    /// <summary>
    /// Tracks the dynamically-injected per-kind top-level menus so they can be
    /// added/removed in lockstep with the live tab kinds.  Subscription is
    /// NOT included — its menu is statically wired in XAML with
    /// HasAnySubscriptionTab IsVisible gating.
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<
        UaLens.ViewModels.PluginKind, MenuItem> m_kindMenus = new();

    /// <summary>
    /// Diffs the active-kinds set (non-Subscription kinds) against the
    /// currently-injected kind-menus.  For each kind newly added, finds the
    /// first tab of that kind, calls <see cref="UaLens.ViewModels.IPlugin.ContributeMenuItems"/>
    /// on it, wraps the result in a top-level MenuItem and inserts it after
    /// the DynamicKindMenuAnchor.  Only the **currently-selected** plugin's
    /// kind gets a menu — switching tabs swaps the kind menu in / out.
    /// </summary>
    private void RefreshDynamicKindMenus()
    {
        var menu = this.FindControl<Menu>("MainMenu");
        // The top-level <Menu> in MainWindow.axaml has no x:Name; locate it
        // via the anchor's logical parent.
        var anchor = this.FindControl<MenuItem>("DynamicKindMenuAnchor");
        if (anchor is null)
        {
            return;
        }

        if (anchor.Parent is not Menu mainMenu)
        {
            return;
        }

        // Desired set: the currently-selected non-Subscription kind, if any.
        // Subscription has a static menu in XAML gated by IsSubscriptionTabActive,
        // so we exclude it here.
        var desired = new System.Collections.Generic.HashSet<UaLens.ViewModels.PluginKind>();
        if (m_vm.SelectedTab is { Kind: var sk } selected
            && sk != UaLens.ViewModels.PluginKind.Subscription)
        {
            desired.Add(sk);
        }

        // Remove menus whose kind is no longer present.
        var toRemove = new System.Collections.Generic.List<UaLens.ViewModels.PluginKind>();
        foreach (var kv in m_kindMenus)
        {
            if (!desired.Contains(kv.Key))
            {
                toRemove.Add(kv.Key);
            }
        }
        foreach (var k in toRemove)
        {
            mainMenu.Items.Remove(m_kindMenus[k]);
            m_kindMenus.Remove(k);
        }

        // Add menus for kinds newly present — use the first tab of the kind
        // to source ContributeMenuItems().  No-op if the tab returns empty.
        int anchorIdx = mainMenu.Items.IndexOf(anchor);
        foreach (UaLens.ViewModels.PluginKind k in desired)
        {
            if (m_kindMenus.ContainsKey(k))
            {
                continue;
            }

            UaLens.ViewModels.IPlugin? firstOfKind = null;
            foreach (UaLens.ViewModels.IPlugin t in m_vm.Tabs)
            {
                if (t.Kind == k)
                { firstOfKind = t; break; }
            }
            if (firstOfKind is null)
            {
                continue;
            }

            var children = firstOfKind.ContributeMenuItems();
            if (children is null || children.Count == 0)
            {
                continue;
            }

            UaLens.ViewModels.PluginRegistration reg = UaLens.ViewModels.PluginRegistry.For(k);
            var top = new MenuItem { Header = reg.MenuHeader };
            foreach (MenuItem child in children)
            {
                top.Items.Add(child);
            }
            // Insert just after the anchor so kind-menus sit between
            // Subscription/anchor and View/Help.
            mainMenu.Items.Insert(anchorIdx + 1, top);
            m_kindMenus[k] = top;
        }
    }

    private void BeginInPlaceRename(UaLens.ViewModels.IPlugin tab)
    {
        tab.IsRenaming = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var tabStrip = this.FindControl<ListBox>("TabStrip");
            if (tabStrip is null)
            {
                return;
            }

            Control? container = tabStrip.ContainerFromItem(tab);
            if (container is null)
            {
                return;
            }
            // Find the TextBox in the container's visual subtree (named "TabTitleEdit").
            foreach (Avalonia.Controls.Control? child
                in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(container))
            {
                if (child is TextBox tb && tb.Name == "TabTitleEdit")
                {
                    tb.Focus();
                    tb.SelectAll();
                    break;
                }
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Right-click → Duplicate: clones the tab's subscription + items into a new tab.</summary>
    private async Task OnDuplicateTab(UaLens.ViewModels.SubscriptionViewModel tab)
    {
        await m_vm.DuplicateTabAsync(tab).ConfigureAwait(false);
    }

    /// <summary>
    /// Toggle a checkable menu item's IsChecked state and invoke its
    /// Click handler so the side-effects run.  Skips disabled items.
    /// </summary>
    private static void ToggleMenu(MenuItem item)
    {
        if (!item.IsEnabled)
        {
            return;
        }

        item.IsChecked = !item.IsChecked;
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    }

    /// <summary>
    /// Mirror the currently-selected tab's per-tab UI state (AnimationMode,
    /// AnimationTimeScale, ShowResourceOverlay) onto the toolbar widgets and
    /// the AnimationCanvas.  Called on tab switch, after rewiring the
    /// per-tab listener, and on initial layout.
    /// </summary>
    private void SyncTabUiState()
    {
        var anim = this.RequiredControl<AnimationCanvas>("Animation");
        var viewModeCombo = this.RequiredControl<ComboBox>("ViewModeCombo");
        var timeScaleLbl = this.RequiredControl<TextBlock>("TimeScaleLabel");
        var resourceCheck = this.RequiredControl<CheckBox>("ResourceOverlayCheck");
        if (m_vm.SelectedTab is SubscriptionViewModel tab)
        {
            viewModeCombo.SelectedIndex = tab.AnimationMode switch
            {
                AnimationMode.Dots => 0,
                AnimationMode.Bars => 1,
                AnimationMode.Lines => 2,
                AnimationMode.Signal => 3,
                AnimationMode.Histogram => 4,
                AnimationMode.Heatmap => 5,
                _ => 0
            };
            anim.Mode = tab.AnimationMode;
            anim.TimeScale = tab.AnimationTimeScale;
            timeScaleLbl.Text = FormatTimeScale(tab.AnimationTimeScale);
            anim.ShowResourceOverlay = tab.ShowResourceOverlay;
            resourceCheck.IsChecked = tab.ShowResourceOverlay;
            ApplyMode(tab.AnimationMode);
        }
        else
        {
            viewModeCombo.SelectedIndex = 0;
            anim.Mode = AnimationMode.Dots;
            anim.TimeScale = 1.0;
            timeScaleLbl.Text = FormatTimeScale(1.0);
            anim.ShowResourceOverlay = false;
            resourceCheck.IsChecked = false;
            ApplyMode(AnimationMode.Dots);
        }
    }

    /// <summary>
    /// Switch the chart cell between the canvas-based modes (Dots/Bars/Lines)
    /// and the ScottPlot modes (Signal/Histogram/Heatmap).  Toggles
    /// visibility and (re)binds the ScottPlot pump to the active tab's
    /// adapter when needed.  Only one consumer owns the subscription
    /// channel at a time — both readers compete for <c>TryRead</c> so
    /// the inactive view is explicitly unbound from the channel here.
    /// </summary>
    private void ApplyMode(AnimationMode mode)
    {
        var anim = this.RequiredControl<AnimationCanvas>("Animation");
        var sp = this.RequiredControl<ScottPlotView>("ScottPlot");
        bool useSp = ScottPlotView.IsScottPlotMode(mode);

        // Save the OUTGOING ScottPlot mode's axis limits to the per-tab
        // cache so the user's pan/zoom is restored on re-entry.
        if (m_lastAppliedMode is { } prevMode
            && ScottPlotView.IsScottPlotMode(prevMode)
            && m_vm.SelectedTab is SubscriptionViewModel prevTab
            && sp.CurrentLimits is { } prevLim)
        {
            prevTab.AxisLimitsCache[prevMode] = prevLim;
        }
        m_lastAppliedMode = mode;

        anim.IsVisible = !useSp;
        sp.IsVisible = useSp;
        if (useSp)
        {
            // Hand the channel exclusively to ScottPlot — detach the canvas
            // so its Tick/Drain doesn't compete for events.
            anim.Bind(null, null);
            anim.Mode = AnimationMode.Dots;
            SubscriptionViewModel? subTab = m_vm.SelectedSubscriptionTab;
            UaLens.Subscriptions.ISubscriptionAdapter? subAdapter = subTab?.Adapter;
            ChannelReader<UaLens.Subscriptions.NotificationEvent>? events
                = subAdapter?.Events;
            IReadOnlyList<UaLens.Subscriptions.MonitoredItemConfig> items
                = subTab?.Items.ToArray()
                  ?? Array.Empty<UaLens.Subscriptions.MonitoredItemConfig>();
            // Restore previously cached axis state for this (tab, mode), if any.
            ScottPlot.AxisLimits? restore =
                subTab is { } cur && cur.AxisLimitsCache.TryGetValue(mode, out ScottPlot.AxisLimits lim)
                    ? lim
                    : null;
            sp.Bind(events, items, mode, restore, subTab?.Recorder);
        }
        else
        {
            // Hand the channel back to the canvas; release ScottPlot's reader.
            sp.Bind(null, null, mode);
            anim.Mode = mode;
            SubscriptionViewModel? sst = m_vm.SelectedSubscriptionTab;
            UaLens.Subscriptions.ISubscriptionAdapter? ssa = sst?.Adapter;
            anim.Bind(ssa?.Events, ssa?.Counters, sst?.Recorder);
        }
    }

    /// <summary>
    /// Last animation mode passed to <see cref="ApplyMode"/>.  Used so the
    /// outgoing ScottPlot mode can checkpoint its axis state into the
    /// per-tab cache before the new pump replaces it.
    /// </summary>
    private AnimationMode? m_lastAppliedMode;

    /// <summary>
    /// Currently-attached SubscriptionViewModel — tracked so we can detach
    /// the PropertyChanged listener when SelectedTab changes.
    /// </summary>
    private SubscriptionViewModel? m_attachedTab;

    /// <summary>
    /// SubscriptionViewModel whose Items collection we're currently
    /// subscribed to (for live ScottPlot pump updates).
    /// </summary>
    private SubscriptionViewModel? m_attachedItemsTab;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? m_itemsHandler;

    /// <summary>
    /// Hook the active <see cref="SubscriptionViewModel"/>'s PropertyChanged
    /// so live mutations of per-tab UI state (e.g. AnimationTimeScale via
    /// keyboard shortcut) propagate to the canvas/widgets.
    /// </summary>
    private void AttachSelectedTabListener()
    {
        if (m_attachedTab is { } prev)
        {
            prev.PropertyChanged -= OnSelectedTabPropertyChanged;
        }
        m_attachedTab = m_vm.SelectedSubscriptionTab;
        if (m_attachedTab is { } cur)
        {
            cur.PropertyChanged += OnSelectedTabPropertyChanged;
        }
    }

    /// <summary>
    /// Hook the selected tab's Items collection so changes propagate to
    /// the active ScottPlot pump (add/remove plottables).
    /// </summary>
    private void AttachItemsListener()
    {
        if (m_attachedItemsTab is { } prev && m_itemsHandler is not null)
        {
            prev.Items.CollectionChanged -= m_itemsHandler;
        }
        m_attachedItemsTab = m_vm.SelectedSubscriptionTab;
        m_itemsHandler = (_, _) =>
        {
            var sp = this.FindControl<ScottPlotView>("ScottPlot");
            if (sp is null || m_vm.SelectedTab is not SubscriptionViewModel tab)
            {
                return;
            }

            sp.OnItemsChanged(tab.Items.ToArray());
        };
        if (m_attachedItemsTab is { } cur)
        {
            cur.Items.CollectionChanged += m_itemsHandler;
        }
    }

    private void OnSelectedTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not SubscriptionViewModel tab || !ReferenceEquals(tab, m_vm.SelectedTab))
        {
            return;
        }
        var anim = this.RequiredControl<AnimationCanvas>("Animation");
        var viewModeCombo = this.RequiredControl<ComboBox>("ViewModeCombo");
        var timeScaleLbl = this.RequiredControl<TextBlock>("TimeScaleLabel");
        var resourceCheck = this.RequiredControl<CheckBox>("ResourceOverlayCheck");
        switch (e.PropertyName)
        {
            case nameof(SubscriptionViewModel.AnimationMode):
                viewModeCombo.SelectedIndex = tab.AnimationMode switch
                {
                    AnimationMode.Dots => 0,
                    AnimationMode.Bars => 1,
                    AnimationMode.Lines => 2,
                    AnimationMode.Signal => 3,
                    AnimationMode.Histogram => 4,
                    AnimationMode.Heatmap => 5,
                    _ => 0
                };
                ApplyMode(tab.AnimationMode);
                anim.InvalidateVisual();
                break;
            case nameof(SubscriptionViewModel.AnimationTimeScale):
                anim.TimeScale = tab.AnimationTimeScale;
                timeScaleLbl.Text = FormatTimeScale(tab.AnimationTimeScale);
                anim.InvalidateVisual();
                break;
            case nameof(SubscriptionViewModel.ShowResourceOverlay):
                anim.ShowResourceOverlay = tab.ShowResourceOverlay;
                resourceCheck.IsChecked = tab.ShowResourceOverlay;
                anim.InvalidateVisual();
                break;
        }
    }

    /// <summary>
    /// 📤 Export — write the connected server's address space (every node
    /// reachable via HierarchicalReferences from the Root, excluding the
    /// OPC UA base namespace) to a NodeSet2 XML file chosen via the OS
    /// file picker.  No-op when not connected.
    /// </summary>
    private async Task OnExportNodeSetAsync()
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "● Connect to a server before exporting NodeSet2.";
            return;
        }
        try
        {
            // Phase 1: namespace picker — user chooses which namespaces to include.
            var nsDlg = new NodeSetExportDialog(session.NamespaceUris);
            IReadOnlyList<string>? picked = await nsDlg.ShowDialog<IReadOnlyList<string>?>(this).ConfigureAwait(false);
            if (picked is null)
            {
                return; // cancelled
            }

            string suggestedName = $"nodeset2-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xml";
            Avalonia.Platform.Storage.IStorageProvider sp = StorageProvider;
            Avalonia.Platform.Storage.IStorageFile? file =
                await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export server address space as NodeSet2",
                    DefaultExtension = "xml",
                    SuggestedFileName = suggestedName,
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("OPC UA NodeSet2 XML")
                        {
                            Patterns = s_xmlPatterns
                        }
                    }
                }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }

            string path = file.Path.LocalPath;
            m_vm.ConnectionStatus = $"● Exporting NodeSet2 to {System.IO.Path.GetFileName(path)}…";
            var exporter = new UaLens.Connection.NodeSetExporter(m_vm.Telemetry);
            int count = await exporter.ExportAsync(session, path, namespaceFilter: picked).ConfigureAwait(true);
            m_vm.ConnectionStatus = $"● Exported {count} nodes to {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Export failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"NodeSet2 export failed: {ex}");
        }
    }

    /// <summary>
    /// 🛡 Certs — open the certificate-store-management dialog.  Resolves
    /// the application configuration via <see cref="UaLens.Connection.ConnectionService.GetConfigAsync"/>
    /// so the dialog can enumerate the trusted, issuer, and rejected
    /// stores.  Available regardless of connection state — users may want
    /// to clean up rejected certs before connecting.
    /// </summary>
    private async Task OnOpenCertStoreAsync()
    {
        try
        {
            Opc.Ua.ApplicationConfiguration cfg = await m_vm.Connection.GetConfigAsync().ConfigureAwait(true);
            var dlg = new CertificateStoreDialog(cfg, m_vm.Telemetry);
            await dlg.ShowDialog(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Certs dialog failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OpenCertStore failed: {ex}");
        }
    }

    private async Task OnConnect()
    {
        // When connected the ⏻ Connect button is hidden; ChangeButton's
        // flyout handles Disconnect / Change User / Reconnect.  If the
        // user somehow invokes this path while connected (e.g. via a
        // keyboard shortcut we add later), treat it as a no-op rather
        // than as Disconnect — Disconnect is now an explicit menu item.
        if (m_vm.Connection.IsConnected)
        {
            return;
        }

        try
        {
            var pick = await UaLens.Connection.EndpointCredentialsPicker.PromptAsync(
                this, m_vm.Telemetry, m_vm.EndpointUrl, System.Threading.CancellationToken.None)
                .ConfigureAwait(true);
            if (pick is null)
            {
                return;
            }

            // Connect.  ConnectionService owns disposal of the IUserIdentity
            // alongside the session.
            await m_vm.Connection.ConnectAsync(
                new UaLens.Connection.ConnectionOptions
                {
                    EndpointUrl = pick.Endpoint.EndpointUrl ?? m_vm.EndpointUrl,
                    UseSecurity = pick.Endpoint.SecurityMode != MessageSecurityMode.None,
                    Engine = m_vm.Engine
                },
                pick.Endpoint,
                pick.Identity,
                certPrompt: PromptCertTrustAsync,
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            // No direct ApplySubscriptionAsync here — MainViewModel reacts
            // to the StateChanged event by creating the default tab via
            // AddTabCommand, which applies the default subscription config.
        }
        catch (Exception ex)
        {
            // The Connection panel status label binds to ConnectionStatus; surface a brief banner.
            m_vm.ConnectionStatus = $"● Connect failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Connect failed: {ex}");
        }
    }

    private async Task OnDisconnect()
    {
        if (!m_vm.Connection.IsConnected)
        {
            return;
        }
        try
        {
            await m_vm.Connection.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Disconnect failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Disconnect failed: {ex}");
        }
    }

    private async Task OnChangeUser()
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "● Not connected.";
            return;
        }

        try
        {
            // Credentials only — no endpoint picker (per requirements).
            var dlg = new UaLens.Views.CredentialsDialog();
            var pair = await dlg.ShowDialog<(string, string)?>(this).ConfigureAwait(true);
            if (pair is null)
            {
                return;
            }
            (string user, string pass) = pair.Value;

            // CA2000: ownership transfers to UpdateSessionAsync — the
            // session retains and disposes the identity.
#pragma warning disable CA2000
            var identity = new UserIdentity(user, System.Text.Encoding.UTF8.GetBytes(pass));
#pragma warning restore CA2000

            await session.UpdateSessionAsync(identity, default, System.Threading.CancellationToken.None)
                .ConfigureAwait(false);
            m_vm.ConnectionStatus = $"● User changed to {user}.";
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Change user failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Change user failed: {ex}");
        }
    }

    private async Task OnReconnect()
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "● Not connected.";
            return;
        }
        try
        {
            m_vm.ConnectionStatus = "● Reconnecting…";
            await session.ReconnectAsync(null, null, System.Threading.CancellationToken.None).ConfigureAwait(false);
            m_vm.ConnectionStatus = "● Reconnected.";
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Reconnect failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Reconnect failed: {ex}");
        }
    }

    private Task<UaLens.Connection.TrustChoice> PromptCertTrustAsync(
        System.Security.Cryptography.X509Certificates.X509Certificate2 cert,
        ServiceResult error)
    {
        // The validator runs on a worker thread. Marshal to the UI thread to show
        // the modal trust dialog and block the validator until the user decides.
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dlg = new UaLens.Views.CertificateTrustDialog(cert, error);
            var choice = await dlg.ShowDialog<Connection.TrustChoice?>(this).ConfigureAwait(false);
            return choice ?? UaLens.Connection.TrustChoice.Reject;
        });
    }

    private async Task OnAddItem()
    {
        if (!m_vm.IsConnected || !m_vm.CanAddSelectedItem || m_vm.SelectedNode is not { } node
            || m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            return;
        }

        // Variable selection: existing single-item dialog.
        if (node.NodeClass == Opc.Ua.NodeClass.Variable)
        {
            var dlg = new AddItemDialog(node, isEvent: false);
            MonitoredItemConfig? r = await dlg.ShowDialog<MonitoredItemConfig?>(this).ConfigureAwait(false);
            if (r is not null)
            {
                await tab.AddItemCommand.ExecuteAsync(r).ConfigureAwait(false);
            }
            return;
        }

        // Object selection: branch on what's available.
        if (node.NodeClass == Opc.Ua.NodeClass.Object)
        {
            // Events-only Object → existing single-item dialog (event mode).
            if (m_vm.SelectionHasEvents && !m_vm.SelectionHasVariables)
            {
                var dlg = new AddItemDialog(node, isEvent: true);
                MonitoredItemConfig? r = await dlg.ShowDialog<MonitoredItemConfig?>(this).ConfigureAwait(false);
                if (r is not null)
                {
                    await tab.AddItemCommand.ExecuteAsync(r).ConfigureAwait(false);
                }
                return;
            }

            // Variables-only Object → ask for sampling interval, bulk-add all child variables.
            if (m_vm.SelectionHasVariables && !m_vm.SelectionHasEvents)
            {
                var dlg = new AddItemDialog(node, isEvent: false);
                MonitoredItemConfig? r = await dlg.ShowDialog<MonitoredItemConfig?>(this).ConfigureAwait(false);
                if (r is not null)
                {
                    await BulkAddVariablesAsync(tab, r.SamplingInterval).ConfigureAwait(false);
                }
                return;
            }

            // Both available → use the dedicated dialog.
            if (m_vm.SelectionHasEvents && m_vm.SelectionHasVariables)
            {
                byte? notifier = await m_vm.Browser.GetEventNotifierAsync(node.NodeId).ConfigureAwait(true);
                var dlg = new AddObjectChildrenDialog(node, notifier, m_vm.SelectionVariables.Count);
                ObjectAddDecision? d = await dlg.ShowDialog<ObjectAddDecision?>(this).ConfigureAwait(false);
                if (d is not { } dec)
                {
                    return;
                }
                var sampling = TimeSpan.FromMilliseconds(dec.SamplingIntervalMs);
                if (dec.Mode == ObjectAddMode.EventsOnly || dec.Mode == ObjectAddMode.Both)
                {
                    var evt = new MonitoredItemConfig
                    {
                        DisplayName = "event:" + node.NodeId,
                        NodeId = node.NodeId,
                        AttributeId = Opc.Ua.Attributes.EventNotifier,
                        SamplingInterval = sampling,
                        QueueSize = 100u,
                        DiscardOldest = true,
                        IsEvent = true,
                        MonitoringMode = Opc.Ua.MonitoringMode.Reporting
                    };
                    await tab.AddItemCommand.ExecuteAsync(evt).ConfigureAwait(false);
                }
                if (dec.Mode == ObjectAddMode.VariablesOnly || dec.Mode == ObjectAddMode.Both)
                {
                    await BulkAddVariablesAsync(tab, sampling).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Adds every child Variable cached on <see cref="MainViewModel.SelectionVariables"/>
    /// (HasComponent forward, no subtypes — so Properties are excluded) as a
    /// value-mode monitored item with the given sampling interval, on the
    /// supplied <paramref name="tab"/>.
    /// </summary>
    private async Task BulkAddVariablesAsync(SubscriptionViewModel tab, TimeSpan sampling)
    {
        foreach ((Opc.Ua.NodeId nodeId, string displayName) in m_vm.SelectionVariables)
        {
            var cfg = new MonitoredItemConfig
            {
                DisplayName = "value:" + displayName,
                NodeId = nodeId,
                AttributeId = Opc.Ua.Attributes.Value,
                SamplingInterval = sampling,
                QueueSize = 1u,
                DiscardOldest = true,
                IsEvent = false,
                MonitoringMode = Opc.Ua.MonitoringMode.Reporting
            };
            await tab.AddItemCommand.ExecuteAsync(cfg).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Subscription → Add Recursively...  Browses the subtree under the
    /// currently-selected node via HierarchicalReferences and adds every
    /// Variable (as a Value-mode item) and every Object whose EventNotifier
    /// carries the SubscribeToEvents bit (as an Event-mode item).  Bounded
    /// by max-items and max-depth knobs in the dialog so a huge address
    /// space can't accidentally explode the subscription.
    /// </summary>
    private async Task OnAddRecursivelyAsync()
    {
        if (!m_vm.IsConnected)
        {
            m_vm.ConnectionStatus = "● Connect first.";
            return;
        }
        if (m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            m_vm.ConnectionStatus = "● Open a subscription tab first.";
            return;
        }
        if (m_vm.SelectedNode is not { } selected || m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "● Pick a node in the address space.";
            return;
        }

        var dlg = new RecursiveAddDialog($"{selected.NodeId}  ({selected.NodeClass})");
        RecursiveAddOptions? opts = await dlg.ShowDialog<RecursiveAddOptions?>(this).ConfigureAwait(false);
        if (opts is null)
        {
            return;
        }

        try
        {
            m_vm.ConnectionStatus = "● Browsing subtree…";
            var (variables, eventEmitters) = await BrowseSubtreeAsync(
                session, selected.NodeId, opts.MaxDepth, opts.MaxItems).ConfigureAwait(true);

            int addedVars = 0, addedEvents = 0;
            if (opts.IncludeVariables)
            {
                foreach ((Opc.Ua.NodeId nodeId, string displayName) in variables)
                {
                    if (addedVars + addedEvents >= opts.MaxItems)
                    {
                        break;
                    }

                    await tab.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
                    {
                        DisplayName = "value:" + displayName,
                        NodeId = nodeId,
                        AttributeId = Opc.Ua.Attributes.Value,
                        SamplingInterval = opts.SamplingInterval,
                        QueueSize = 1u,
                        DiscardOldest = true,
                        IsEvent = false,
                        MonitoringMode = Opc.Ua.MonitoringMode.Reporting
                    }).ConfigureAwait(true);
                    addedVars++;
                }
            }
            if (opts.IncludeEvents)
            {
                foreach ((Opc.Ua.NodeId nodeId, string displayName) in eventEmitters)
                {
                    if (addedVars + addedEvents >= opts.MaxItems)
                    {
                        break;
                    }

                    await tab.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
                    {
                        DisplayName = "event:" + displayName,
                        NodeId = nodeId,
                        AttributeId = Opc.Ua.Attributes.EventNotifier,
                        SamplingInterval = opts.SamplingInterval,
                        QueueSize = 100u,
                        DiscardOldest = true,
                        IsEvent = true,
                        MonitoringMode = Opc.Ua.MonitoringMode.Reporting
                    }).ConfigureAwait(true);
                    addedEvents++;
                }
            }
            m_vm.ConnectionStatus = $"● Added {addedVars} variables + {addedEvents} events recursively.";
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Add recursively failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OnAddRecursivelyAsync failed: {ex}");
        }
    }

    /// <summary>
    /// BFS-browse from <paramref name="startNode"/> via
    /// <see cref="Opc.Ua.ReferenceTypeIds.HierarchicalReferences"/> (subtypes
    /// included), returning two lists: variables (NodeId, DisplayName) and
    /// event-emitting objects (Objects whose EventNotifier carries
    /// <see cref="Opc.Ua.EventNotifiers.SubscribeToEvents"/>).  Bounded by
    /// <paramref name="maxDepth"/> and <paramref name="maxItems"/>.
    /// </summary>
#pragma warning disable CA1859 // ISession is the SDK's polymorphic surface — keep the abstraction.
    private static async Task<(
        System.Collections.Generic.List<(Opc.Ua.NodeId NodeId, string DisplayName)> Variables,
        System.Collections.Generic.List<(Opc.Ua.NodeId NodeId, string DisplayName)> EventEmitters)>
        BrowseSubtreeAsync(
            Opc.Ua.Client.ISession session,
            Opc.Ua.NodeId startNode,
            int maxDepth,
            int maxItems)
    {
        var variables = new System.Collections.Generic.List<(Opc.Ua.NodeId, string)>();
        var objectCandidates = new System.Collections.Generic.List<(Opc.Ua.NodeId NodeId, string Name)>();
        var visited = new System.Collections.Generic.HashSet<Opc.Ua.ExpandedNodeId>();
        var frontier = new Opc.Ua.ArrayOf<Opc.Ua.ExpandedNodeId>(
            new[] { Opc.Ua.NodeId.ToExpandedNodeId(startNode, session.NamespaceUris) });
        Opc.Ua.ArrayOf<Opc.Ua.NodeId> hierRefs = new(
            new[] { Opc.Ua.ReferenceTypeIds.HierarchicalReferences });

        for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            Opc.Ua.ArrayOf<Opc.Ua.INode> hits = await session.NodeCache
                .FindReferencesAsync(frontier, hierRefs, isInverse: false, includeSubtypes: true)
                .ConfigureAwait(false);
            var next = new System.Collections.Generic.List<Opc.Ua.ExpandedNodeId>();
            foreach (Opc.Ua.INode n in hits)
            {
                if (!visited.Add(n.NodeId))
                {
                    continue;
                }

                if (variables.Count + objectCandidates.Count >= maxItems)
                {
                    break;
                }

                Opc.Ua.NodeId localId = Opc.Ua.ExpandedNodeId.ToNodeId(n.NodeId, session.NamespaceUris);
                string name = !n.DisplayName.IsNull && !string.IsNullOrEmpty(n.DisplayName.Text)
                    ? n.DisplayName.Text!
                    : (n.BrowseName.Name ?? localId.ToString() ?? "");
                switch (n.NodeClass)
                {
                    case Opc.Ua.NodeClass.Variable:
                        variables.Add((localId, name));
                        next.Add(n.NodeId);
                        break;
                    case Opc.Ua.NodeClass.Object:
                    case Opc.Ua.NodeClass.View:
                        objectCandidates.Add((localId, name));
                        next.Add(n.NodeId);
                        break;
                    case Opc.Ua.NodeClass.ObjectType:
                    case Opc.Ua.NodeClass.VariableType:
                        next.Add(n.NodeId);
                        break;
                }
            }
            if (variables.Count + objectCandidates.Count >= maxItems)
            {
                break;
            }

            frontier = new Opc.Ua.ArrayOf<Opc.Ua.ExpandedNodeId>(next.ToArray());
        }

        var eventEmitters = await FilterEventEmittersAsync(session, objectCandidates).ConfigureAwait(false);
        return (variables, eventEmitters);
    }

    /// <summary>
    /// Batch-reads the EventNotifier attribute for every candidate Object/View
    /// in a single Read service call and returns those whose attribute has the
    /// <see cref="Opc.Ua.EventNotifiers.SubscribeToEvents"/> bit set.  Doing one
    /// batched read avoids one round-trip per node and avoids relying on the
    /// NodeCache populating EventNotifier (which it doesn't reliably do for
    /// references-only fetches).
    /// </summary>
    private static async Task<System.Collections.Generic.List<(Opc.Ua.NodeId NodeId, string DisplayName)>>
        FilterEventEmittersAsync(
            Opc.Ua.Client.ISession session,
            System.Collections.Generic.List<(Opc.Ua.NodeId NodeId, string Name)> candidates)
    {
        var emitters = new System.Collections.Generic.List<(Opc.Ua.NodeId, string)>();
        if (candidates.Count == 0)
        {
            return emitters;
        }

        var rvids = new Opc.Ua.ReadValueId[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            rvids[i] = new Opc.Ua.ReadValueId
            {
                NodeId = candidates[i].NodeId,
                AttributeId = Opc.Ua.Attributes.EventNotifier
            };
        }
        var ids = new Opc.Ua.ArrayOf<Opc.Ua.ReadValueId>(rvids);
        try
        {
            Opc.Ua.ReadResponse resp = await session.ReadAsync(
                null, 0, Opc.Ua.TimestampsToReturn.Neither, ids, default).ConfigureAwait(false);
            int n = System.Math.Min(resp.Results.Count, candidates.Count);
            for (int i = 0; i < n; i++)
            {
                Opc.Ua.DataValue dv = resp.Results[i];
                if (Opc.Ua.StatusCode.IsBad(dv.StatusCode))
                {
                    continue;
                }

                if (!dv.WrappedValue.TryGetValue(out byte mask))
                {
                    continue;
                }

                if ((mask & Opc.Ua.EventNotifiers.SubscribeToEvents) != 0)
                {
                    emitters.Add(candidates[i]);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FilterEventEmittersAsync failed: {ex.Message}");
        }
        return emitters;
    }
#pragma warning restore CA1859


    private async Task OnRemoveItem()
    {
        if (!m_vm.IsConnected || m_vm.SelectedTab is not SubscriptionViewModel tab
            || tab.Adapter is not { } adapter || adapter.Items.Count == 0)
        {
            return;
        }
        var dlg = new RemoveItemDialog(adapter.Items);
        IReadOnlyList<MonitoredItemConfig>? r =
            await dlg.ShowDialog<IReadOnlyList<MonitoredItemConfig>?>(this).ConfigureAwait(false);
        if (r is null)
        {
            return;
        }
        foreach (MonitoredItemConfig item in r)
        {
            await tab.RemoveItemCommand.ExecuteAsync(item).ConfigureAwait(false);
        }
    }

    // ----- B2: per-row "Set monitoring mode →" context-menu handlers -----
    //
    // Each MenuItem under the status-row ContextMenu fires the matching
    // entrypoint below.  The MenuItem's CommandParameter (or DataContext)
    // carries the bound row; we forward it to the per-tab VM's
    // SetMonitoringModeAsync method, which calls the adapter and refreshes
    // the row's Mode column on success.

    private void OnStatusRowModeDisabledClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => InvokeRowMonitoringMode(sender, MonitoringMode.Disabled);

    private void OnStatusRowModeSamplingClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => InvokeRowMonitoringMode(sender, MonitoringMode.Sampling);

    private void OnStatusRowModeReportingClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => InvokeRowMonitoringMode(sender, MonitoringMode.Reporting);

    private void InvokeRowMonitoringMode(object? sender, MonitoringMode mode)
    {
        if (sender is MenuItem mi
            && mi.CommandParameter is MonitoredItemStatusRow row
            && m_vm.SelectedSubscriptionTab is { } tab)
        {
            _ = tab.SetMonitoringModeAsync(row, mode);
        }
    }

    private async Task OnSettings()
    {
        if (m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            return;
        }
        // Adapter may be null on a disconnected/unbound tab — fall back to
        // a default 'true' for HasWorkerPool so the settings dialog can still
        // edit the SubscriptionConfig pre-connect.
        bool hasPool = tab.Adapter?.HasWorkerPool ?? true;
        var dlg = new SubscriptionSettingsDialog(tab.Subscription, hasPool);
        SubscriptionConfig? r = await dlg.ShowDialog<SubscriptionConfig?>(this).ConfigureAwait(false);
        if (r is not null)
        {
            await tab.ApplySubscriptionCommand.ExecuteAsync(r).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Context-menu "Call Method…" entry handler.  Opens the
    /// <see cref="MethodCallDialog"/> bound to the active session.
    /// </summary>
    private async Task OnCallMethod(UaLens.ViewModels.NodeViewModel node)
    {
        if (m_vm.Connection.Session is not { } session)
        {
            return;
        }
        var dlg = new MethodCallDialog(node, session);
        await dlg.ShowDialog(this).ConfigureAwait(false);
    }

    /// <summary>
    /// Context-menu "Write Value…" entry handler.  Opens the
    /// <see cref="WriteValueDialog"/> bound to the active session.
    /// </summary>
    private async Task OnWriteValue(UaLens.ViewModels.NodeViewModel node)
    {
        if (m_vm.Connection.Session is not { } session)
        {
            return;
        }
        var dlg = new WriteValueDialog(node, session);
        await dlg.ShowDialog(this).ConfigureAwait(false);
    }

    /// <summary>
    /// Address-space "Export value to file…" handler: read the
    /// variable's DataValue, run the encoding + save-file pickers,
    /// then write the encoded bytes to disk.
    /// </summary>
    private async Task OnExportValueAsync(UaLens.ViewModels.NodeViewModel node)
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "● Connect to export a value.";
            return;
        }
        try
        {
            Opc.Ua.ArrayOf<Opc.Ua.ReadValueId> ids =
            [
                new Opc.Ua.ReadValueId { NodeId = node.NodeId, AttributeId = Opc.Ua.Attributes.Value }
            ];
            Opc.Ua.ReadResponse rr = await session
                .ReadAsync(null, 0, Opc.Ua.TimestampsToReturn.Both, ids, System.Threading.CancellationToken.None)
                .ConfigureAwait(true);
            if (rr.Results.Count == 0)
            {
                m_vm.ConnectionStatus = "● Read returned no rows.";
                return;
            }
            Opc.Ua.DataValue dv = rr.Results[0];
            string safeName = SanitiseFileName(node.Text);
            (Avalonia.Platform.Storage.IStorageFile? file,
                UaLens.Connection.EncodingFormat fmt) =
                await UaLens.Views.EncodedValueIO.SaveAsync(this, safeName).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            byte[] bytes = UaLens.Connection.DataValueCodec.EncodeDataValue(
                dv, fmt, session.MessageContext);
            System.IO.Stream output = await file.OpenWriteAsync().ConfigureAwait(true);
            await using (output.ConfigureAwait(false))
            {
                await output.WriteAsync(bytes).ConfigureAwait(false);
            }
            m_vm.ConnectionStatus = $"● Exported value to {file.Name} ({fmt}).";
        }
        catch (Exception ex)
        {
            m_vm.ConnectionStatus = $"● Export failed: {ex.Message}";
        }
    }

    /// <summary>Strip filename-hostile characters from a node's display name.</summary>
    private static string SanitiseFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "value";
        }
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }

    private void OnFindByPath(UaLens.ViewModels.NodeViewModel? n)
    {
        var dlg = new UaLens.Views.FindNodeDialog(m_vm.Browser, n?.NodeId);
        dlg.Show(this);
    }

    private void OnViewNodeState(UaLens.ViewModels.NodeViewModel n)
    {
        var dlg = new UaLens.Views.ViewNodeStateDialog(m_vm.Browser, m_vm.Connection, n.NodeId);
        dlg.Show(this);
    }

    private async Task OnLocalesAsync()
    {
        var dlg = new UaLens.Views.LocalePickerDialog(m_vm.Connection);
        await dlg.ShowDialog(this).ConfigureAwait(true);
    }

    private void ShowHelp()
    {
        var help = new Window
        {
            Title = "Cheat sheet",
            Width = 600,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black,
            Foreground = Avalonia.Media.Brushes.White
        };
        help.Content = new TextBlock
        {
            Margin = new Thickness(20),
            FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
            Text =
                "  ─── Navigation ───\n" +
                "  Tab / Shift+Tab     cycle focus across controls\n" +
                "  Ctrl+Tab / Ctrl+Sh+Tab  cycle subscription tabs\n" +
                "  Ctrl+U              focus the Endpoint URL field\n" +
                "  Arrow keys          navigate inside the focused tree / list / text\n" +
                "  Right / Left        expand / collapse a tree node\n" +
                "  Enter               default button (OK in dialogs)\n" +
                "  Esc                 Cancel in dialogs\n" +
                "\n" +
                "  ─── File ───\n" +
                "  Ctrl+O              Load session\n" +
                "  Ctrl+S              Save session\n" +
                "  Ctrl+E              Export NodeSet2 XML\n" +
                "  Ctrl+Shift+E        Export Tab Data (CSV / JSON)\n" +
                "  Ctrl+Q              Quit\n" +
                "\n" +
                "  ─── Certificates ───\n" +
                "  Ctrl+K              Manage trust stores\n" +
                "\n" +
                "  ─── Subscription ───\n" +
                "  Ctrl+T              New subscription tab\n" +
                "  Ctrl+I              Add item…\n" +
                "  Ctrl+Shift+I        Add recursively…\n" +
                "  Ctrl+Shift+R        Remove item…\n" +
                "  Ctrl+,              Subscription settings…\n" +
                "  (right-click tab)   Rename… / Duplicate / Close\n" +
                "\n" +
                "  ─── View ───\n" +
                "  F2                  Toggle diagnostics panel\n" +
                "  Ctrl+L              Toggle log panel\n" +
                "  Ctrl+B              Toggle address-space pane\n" +
                "  Ctrl+A              Toggle Attributes pane (sub of address space)\n" +
                "  Ctrl+R              Toggle References pane (sub of address space)\n" +
                "  Ctrl+V              Cycle chart view mode\n" +
                "  Ctrl++ / Ctrl+−     Zoom chart time-scale in / out\n" +
                "  Ctrl+0              Reset chart time-scale\n" +
                "\n" +
                "  ─── Help ───\n" +
                "  F1                  Cheat sheet (this dialog)\n" +
                "\n" +
                "  Tree-view glyphs:\n" +
                "    ◉ Object   ◇ ObjectType   ○ Variable   ◎ VariableType\n" +
                "    ▶ Method   ◦ ReferenceType   □ DataType   ▣ View"
        };
        help.ShowDialog(this);
    }
}

/// <summary>
/// Bridge from <see cref="MainViewModel"/> flags to the address-space
/// context-menu's per-entry visibility, decoupling
/// <see cref="AddressSpaceView"/> from the main view-model directly.
/// </summary>
internal sealed class ContextMenuPolicy : IContextMenuPolicy
{
    private readonly UaLens.ViewModels.MainViewModel m_vm;

    public ContextMenuPolicy(UaLens.ViewModels.MainViewModel vm)
    {
        m_vm = vm;
    }

    public ContextMenuVisibility Inspect(UaLens.ViewModels.NodeViewModel node)
    {
        bool canRec = m_vm.IsConnected
            && node.NodeClass is Opc.Ua.NodeClass.Object
                or Opc.Ua.NodeClass.ObjectType
                or Opc.Ua.NodeClass.View
                or Opc.Ua.NodeClass.Variable
                or Opc.Ua.NodeClass.VariableType;
        bool canReadHistory = m_vm.IsConnected
            && node.NodeClass == Opc.Ua.NodeClass.Variable;
        bool canShowEvents = m_vm.IsConnected && m_vm.SelectionHasEvents;
        bool canPerf = m_vm.IsConnected
            && (m_vm.CanCallMethod || m_vm.CanWriteVariable);
        bool canExportValue = m_vm.IsConnected
            && node.NodeClass == Opc.Ua.NodeClass.Variable;
        return new ContextMenuVisibility(
            CanAdd: m_vm.CanAddSelectedItem,
            CanAddRecursive: canRec,
            CanCall: m_vm.CanCallMethod,
            CanWrite: m_vm.CanWriteVariable,
            CanReadHistory: canReadHistory,
            CanShowEvents: canShowEvents,
            CanPerf: canPerf,
            CanExportValue: canExportValue);
    }
}
