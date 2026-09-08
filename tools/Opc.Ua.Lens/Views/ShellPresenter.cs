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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UaLens.Connection;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Views;

/// <summary>
/// Owns shell presentation: the compact menu, connection strip, document tab
/// strip, welcome area, appearance and the single keyboard-dispatch path. It
/// holds no protocol state and mutates no documents; commands come from the
/// shared registry and tool metadata comes from the plug-in registry.
/// </summary>
internal sealed class ShellPresenter
{
    public ShellPresenter(
        MainWindow window,
        MainViewModel viewModel,
        ConnectionController connection,
        ILogger log,
        Func<ThemePreset, Task> changeThemeAsync)
    {
        m_window = window ?? throw new ArgumentNullException(nameof(window));
        m_vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        m_log = log ?? throw new ArgumentNullException(nameof(log));
        m_changeThemeAsync = changeThemeAsync ?? throw new ArgumentNullException(nameof(changeThemeAsync));
    }

    public void Attach()
    {
        WireMenus();
        WireConnectionStrip();
        WireTabStrip();
        WireWelcome();
        BuildOpenToolMenu();
        SyncTheme();
        SyncInspectorMenu();
        ApplyAddressSpaceVisibility(true);
        UpdateLeftStackRows();
        UpdateConnectionPresentation();
        UpdateTabStatus();
        RefreshActiveToolActions();
        UpdateWorkspacePresence();

        m_vm.PropertyChanged += OnViewModelChanged;
        m_vm.Browser.PropertyChanged += OnBrowserChanged;
        m_vm.Tabs.CollectionChanged += OnTabsChanged;
        m_vm.Connection.StateChanged += OnConnectionStateChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;

        m_connectionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => UpdateConnectionPresentation());
        m_tabStatusTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateTabStatus());
        m_connectionTimer.Start();
        m_tabStatusTimer.Start();

        _ = LoadFavoritesAsync();
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        string? gesture = BuildGesture(e);
        if (gesture is null)
        {
            return;
        }
        bool isTextInput = e.Source is TextBox;

        // Rename needs to also focus the inline editor, so it is handled here.
        if (!isTextInput && Equals(gesture, "F2") && m_vm.SelectedTab is { } toRename)
        {
            BeginInPlaceRename(toRename);
            e.Handled = true;
            return;
        }
        if (m_vm.Commands.TryResolveShortcut(gesture, isTextInput, out CommandDescriptor? command))
        {
            _ = DispatchAsync(command);
            e.Handled = true;
            return;
        }
        if (isTextInput)
        {
            return;
        }
        if (TryWindowLocalShortcut(gesture))
        {
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        m_connectionTimer?.Stop();
        m_tabStatusTimer?.Stop();
        m_vm.PropertyChanged -= OnViewModelChanged;
        m_vm.Browser.PropertyChanged -= OnBrowserChanged;
        m_vm.Tabs.CollectionChanged -= OnTabsChanged;
        m_vm.Connection.StateChanged -= OnConnectionStateChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private bool TryWindowLocalShortcut(string gesture)
    {
        switch (gesture.ToUpperInvariant())
        {
            case "F1": ShowHelp(); return true;
            case "CTRL+E": InvokeMenu("MenuExport"); return true;
            case "CTRL+SHIFT+E": InvokeMenu("MenuExportTab"); return true;
            case "CTRL+O": InvokeMenu("MenuOpenWorkspace"); return true;
            case "CTRL+S": InvokeMenu("MenuSaveWorkspace"); return true;
            case "CTRL+K": InvokeMenu("MenuCertificates"); return true;
            case "CTRL+Q": m_window.Close(); return true;
            case "CTRL+T": InvokeMenu("MenuAddTool"); return true;
            case "CTRL+B": ToggleAddressSpace(); return true;
            case "CTRL+SHIFT+F": ToggleFilters(); return true;
            case "CTRL+SHIFT+G": SetDiagnostics(!m_diagnosticsVisible); return true;
            case "CTRL+L": SetLog(!m_logVisible); return true;
            default: return false;
        }
    }

    private void WireMenus()
    {
        m_window.RequiredControl<MenuItem>("MenuQuit").Click += (_, _) => m_window.Close();
        m_window.RequiredControl<MenuItem>("MenuAbout").Click += (_, _) => ShowHelp();
        m_window.RequiredControl<MenuItem>("MenuAboutDialog").Click += async (_, _) =>
        {
            var dlg = new AboutDialog();
            await dlg.ShowDialog(m_window).ConfigureAwait(true);
        };
        m_window.RequiredControl<MenuItem>("MenuAddTool").Click += async (_, _) =>
            await OpenCatalogAsync().ConfigureAwait(true);

        m_window.RequiredControl<MenuItem>("MenuToggleAddressSpace").Click += (_, _) => ToggleAddressSpace();
        m_window.RequiredControl<MenuItem>("MenuToggleFilters").Click += (_, _) => ToggleFilters();
        m_window.RequiredControl<MenuItem>("MenuToggleAttrs").Click += (_, _) => ApplyInspectorFromMenu();
        m_window.RequiredControl<MenuItem>("MenuToggleRefs").Click += (_, _) => ApplyInspectorFromMenu();
        m_window.RequiredControl<MenuItem>("MenuCycleInspector").Click += (_, _) => m_vm.CycleAttributesPanelMode();
        m_window.RequiredControl<MenuItem>("MenuToggleDiag").Click += (_, _) =>
            SetDiagnostics(m_window.RequiredControl<MenuItem>("MenuToggleDiag").IsChecked);
        m_window.RequiredControl<MenuItem>("MenuToggleLog").Click += (_, _) =>
            SetLog(m_window.RequiredControl<MenuItem>("MenuToggleLog").IsChecked);

        m_window.RequiredControl<ToggleButton>("ToggleDiagButton").IsCheckedChanged += (_, _) =>
        {
            if (!m_syncing)
            {
                SetDiagnostics(m_window.RequiredControl<ToggleButton>("ToggleDiagButton").IsChecked == true);
            }
        };
        m_window.RequiredControl<ToggleButton>("ToggleLogButton").IsCheckedChanged += (_, _) =>
        {
            if (!m_syncing)
            {
                SetLog(m_window.RequiredControl<ToggleButton>("ToggleLogButton").IsChecked == true);
            }
        };

        m_window.RequiredControl<MenuItem>("MenuThemeSystem").Click += async (_, _) =>
            await m_changeThemeAsync(ThemePreset.System).ConfigureAwait(true);
        m_window.RequiredControl<MenuItem>("MenuThemeLight").Click += async (_, _) =>
            await m_changeThemeAsync(ThemePreset.Light).ConfigureAwait(true);
        m_window.RequiredControl<MenuItem>("MenuThemeDark").Click += async (_, _) =>
            await m_changeThemeAsync(ThemePreset.DarkStandard).ConfigureAwait(true);
        m_window.RequiredControl<MenuItem>("MenuThemeDarkNavy").Click += async (_, _) =>
            await m_changeThemeAsync(ThemePreset.DarkNavy).ConfigureAwait(true);

        var diagnostics = m_window.RequiredControl<DiagnosticsView>("DiagnosticsPanel");
        diagnostics.BindPublishLog(m_vm.PublishLog);
        diagnostics.HideRequested += () => SetDiagnostics(false);

        m_window.RequiredControl<Button>("OperationErrorDismiss").Click += (_, _) =>
            m_window.RequiredControl<Border>("OperationErrorBanner").IsVisible = false;
    }

    private void WireConnectionStrip()
    {
        var history = m_window.RequiredControl<Button>("EndpointHistoryButton");
        m_historyFlyout = new MenuFlyout();
        history.Flyout = m_historyFlyout;
        history.Click += (_, _) => RefreshHistoryFlyout();
        m_window.RequiredControl<Button>("FavoriteToggle").Click += async (_, _) =>
            await ToggleFavoriteAsync().ConfigureAwait(true);
    }

    private void WireTabStrip()
    {
        var tabStrip = m_window.RequiredControl<ListBox>("TabStrip");
        tabStrip.SizeChanged += (_, _) =>
        {
            if (m_vm.SelectedTab is { } selected)
            {
                tabStrip.ScrollIntoView(selected);
            }
        };
        tabStrip.AddHandler(Button.ClickEvent, async (object? _, RoutedEventArgs e) =>
        {
            if (e.Source is Button { Name: "CloseTabButton", Tag: IPlugin tab })
            {
                await CloseTabAsync(tab).ConfigureAwait(true);
                e.Handled = true;
            }
        });
        tabStrip.AddHandler(MenuItem.ClickEvent, async (object? _, RoutedEventArgs e) =>
        {
            if (e.Source is not MenuItem item || item.DataContext is not IPlugin tab)
            {
                return;
            }
            switch (item.Name)
            {
                case "TabMenuRename": BeginInPlaceRename(tab); break;
                case "TabMenuDuplicate": await DuplicateTabAsync(tab).ConfigureAwait(true); break;
                case "TabMenuClose": await CloseTabAsync(tab).ConfigureAwait(true); break;
            }
            e.Handled = true;
        });
        tabStrip.AddHandler(InputElement.DoubleTappedEvent, (object? _, TappedEventArgs e) =>
        {
            if (e.Source is StyledElement { DataContext: IPlugin tab })
            {
                BeginInPlaceRename(tab);
                e.Handled = true;
            }
        });
        tabStrip.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Source is TextBox { DataContext: IPlugin tab } && tab.IsRenaming
                && e.Key is Key.Enter or Key.Escape)
            {
                tab.IsRenaming = false;
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        tabStrip.AddHandler(InputElement.LostFocusEvent, (object? _, RoutedEventArgs e) =>
        {
            if (e.Source is TextBox { DataContext: IPlugin tab } && tab.IsRenaming)
            {
                tab.IsRenaming = false;
            }
        });

        m_window.RequiredControl<Button>("AddToolButton").Click += async (_, _) =>
            await OpenCatalogAsync().ConfigureAwait(true);
    }

    private void WireWelcome()
    {
        m_window.RequiredControl<Button>("WelcomeConnectBtn").Click += async (_, _) =>
            await m_vm.ConnectCommand.ExecuteAsync(null).ConfigureAwait(true);
        m_window.RequiredControl<Button>("WelcomeOpenWorkspaceBtn").Click += async (_, _) =>
            await m_connection.OpenWorkspaceAsync().ConfigureAwait(true);
        m_window.RequiredControl<Button>("WelcomeDiscoveryBtn").Click += async (_, _) =>
            await m_vm.OpenToolAsync(PluginKind.GdsDiscovery).ConfigureAwait(true);
        m_window.RequiredControl<Button>("WelcomeCertificatesBtn").Click += async (_, _) =>
            await m_vm.OpenToolAsync(PluginKind.CertificateManager).ConfigureAwait(true);
        m_window.RequiredControl<Button>("WelcomeAddToolBtn").Click += async (_, _) =>
            await OpenCatalogAsync().ConfigureAwait(true);

        var list = m_window.RequiredControl<ListBox>("WelcomeRecentList");
        list.ItemsSource = m_welcomeItems;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string url)
            {
                m_vm.EndpointUrl = url;
            }
        };
        list.AddHandler(InputElement.DoubleTappedEvent, async (_, _) =>
        {
            if (list.SelectedItem is string url)
            {
                await SetEndpointAndConnectAsync(url).ConfigureAwait(true);
            }
        });
    }

    private void BuildOpenToolMenu()
    {
        var host = m_window.RequiredControl<MenuItem>("MenuOpenTool");
        foreach (ToolCatalogGroup group in ToolCatalog.Build())
        {
            var groupItem = new MenuItem { Header = group.Name };
            foreach (ToolCatalogEntry entry in group.Tools)
            {
                PluginKind kind = entry.Kind;
                var toolItem = new MenuItem
                {
                    Header = entry.DisplayName,
                    Icon = new TextBlock
                    {
                        Text = entry.Glyph,
                        Width = 18,
                        TextAlignment = TextAlignment.Center,
                        FontSize = 12
                    }
                };
                if (!string.IsNullOrEmpty(entry.Shortcut))
                {
                    toolItem.InputGesture = KeyGesture.Parse(entry.Shortcut);
                }
                ToolTip.SetTip(toolItem, entry.Description);
                toolItem.Click += async (_, _) => await m_vm.OpenToolAsync(kind).ConfigureAwait(true);
                groupItem.Items.Add(toolItem);
            }
            host.Items.Add(groupItem);
        }
    }

    private async Task OpenCatalogAsync()
    {
        var dlg = new ToolCatalogDialog(m_vm.CreatePluginHost());
        PluginKind? kind;
        await using (dlg.ConfigureAwait(false))
        {
            kind = await dlg.ShowDialog<PluginKind?>(m_window).ConfigureAwait(true);
        }
        if (kind is { } picked)
        {
            await m_vm.OpenToolAsync(picked).ConfigureAwait(true);
        }
    }

    private async Task CloseTabAsync(IPlugin tab)
    {
        try
        {
            await m_vm.CloseTabCommand.ExecuteAsync(tab).ConfigureAwait(true);
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            MainWindowLog.TabActionFailed(m_log, "close", error);
        }
    }

    private async Task DuplicateTabAsync(IPlugin tab)
    {
        if (tab is SubscriptionViewModel subscription)
        {
            await m_vm.DuplicateTabAsync(subscription).ConfigureAwait(true);
        }
    }

    private void BeginInPlaceRename(IPlugin tab)
    {
        tab.IsRenaming = true;
        Dispatcher.UIThread.Post(() =>
        {
            var tabStrip = m_window.RequiredControl<ListBox>("TabStrip");
            if (tabStrip.ContainerFromItem(tab) is not { } container)
            {
                return;
            }
            foreach (Control child in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(container))
            {
                if (child is TextBox { Name: "TabTitleEdit" } editor)
                {
                    editor.Focus();
                    editor.SelectAll();
                    break;
                }
            }
        }, DispatcherPriority.Background);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.AttributesPanelMode):
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateLeftStackRows();
                    SyncInspectorMenu();
                });
                break;
            case nameof(MainViewModel.SelectedTab):
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshActiveToolActions();
                    WireDocumentResourceSample();
                    UpdateTabStatus();
                    if (m_vm.SelectedTab is { } selected)
                    {
                        m_window.RequiredControl<ListBox>("TabStrip").ScrollIntoView(selected);
                    }
                });
                break;
            case nameof(MainViewModel.ConnectionStatus):
                Dispatcher.UIThread.Post(UpdateConnectionPresentation);
                break;
            case nameof(MainViewModel.OperationError):
                Dispatcher.UIThread.Post(() =>
                {
                    if (m_vm.OperationError is { Length: > 0 } error)
                    {
                        m_window.RequiredControl<TextBlock>("OperationErrorText").Text = error;
                        m_window.RequiredControl<Border>("OperationErrorBanner").IsVisible = true;
                    }
                });
                break;
        }
    }

    private void OnBrowserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserViewModel.ShowFilters))
        {
            Dispatcher.UIThread.Post(SyncFiltersState);
        }
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateWorkspacePresence();
            RefreshActiveToolActions();
            WireDocumentResourceSample();
        });
    }

    private void OnConnectionStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateConnectionPresentation();
            UpdateTabStatus();
            TrackConnectedEndpoint();
            if (m_diagnosticsVisible)
            {
                m_window.RequiredControl<DiagnosticsView>("DiagnosticsPanel").Bind(m_vm.Connection.Session);
            }
        });
    }

    private void OnThemeChanged() => Dispatcher.UIThread.Post(SyncTheme);

    private void UpdateWorkspacePresence()
    {
        bool hasDocuments = m_vm.Tabs.Count > 0;
        m_window.RequiredControl<ContentControl>("DocumentHost").IsVisible = hasDocuments;
        m_window.RequiredControl<ScrollViewer>("WelcomePanel").IsVisible = !hasDocuments;
    }

    private void RefreshActiveToolActions()
    {
        var menu = m_window.RequiredControl<MenuItem>("MenuActiveToolActions");
        menu.Items.Clear();
        System.Collections.Generic.IReadOnlyList<MenuItem>? actions = m_vm.SelectedTab?.ContributeMenuItems();
        if (actions is { Count: > 0 })
        {
            foreach (MenuItem action in actions)
            {
                menu.Items.Add(action);
            }
            menu.IsEnabled = true;
        }
        else
        {
            menu.IsEnabled = false;
        }
    }

    private void WireDocumentResourceSample()
    {
        if (m_vm.SelectedTab is SubscriptionViewModel subscription
            && ((IPlugin)subscription).View is SubscriptionDocumentView view)
        {
            // Per-frame render callback: use the cached, no-I/O sample updated by
            // the 1 Hz resource pump rather than sampling on every draw.
            view.ResourceSample = () => m_vm.ResourceMonitor?.LastSample ?? (double.NaN, 0);
        }
    }

    private void UpdateConnectionPresentation()
    {
        ConnectionSnapshot snapshot = m_vm.Connection.Snapshot;
        bool connected = m_vm.Connection.IsConnected;
        bool keepAliveLost = m_vm.Connection.Session?.KeepAliveStopped ?? false;

        (string key, string text) = ConnectionPresentation.Indicator(
            snapshot.Phase, connected, keepAliveLost, HasRecentActivity());
        m_window.RequiredControl<Ellipse>("ConnectionDot").Fill = Brush(key);
        m_window.RequiredControl<TextBlock>("ConnectionDotLabel").Text = text;
        m_window.RequiredControl<Button>("ConnectButton").Content =
            ConnectionPresentation.ConnectButtonLabel(snapshot.Phase);
        m_window.RequiredControl<TextBlock>("ConnectionSecurityText").Text =
            ConnectionPresentation.Describe(snapshot.Profile);
    }

    private bool HasRecentActivity()
    {
        if (m_vm.SelectedSubscriptionTab?.Adapter is not { } adapter)
        {
            return false;
        }
        var counters = adapter.Counters;
        long total = counters.DataValues + counters.EventValues + counters.KeepAlives;
        DateTime now = DateTime.UtcNow;
        if (total > m_lastActivityCount)
        {
            m_lastActivityCount = total;
            m_lastActivityUtc = now;
            return true;
        }
        return m_lastActivityUtc != DateTime.MinValue && (now - m_lastActivityUtc).TotalSeconds < 6.0;
    }

    private void UpdateTabStatus()
    {
        var counters = m_window.RequiredControl<TextBlock>("SubscriptionCountersText");
        var warning = m_window.RequiredControl<TextBlock>("StatusWarningText");
        SubscriptionViewModel? tab = m_vm.SelectedSubscriptionTab;
        var adapter = tab?.Adapter;
        if (tab is null || adapter is null)
        {
            counters.Text = "—";
            warning.IsVisible = false;
            return;
        }
        var c = adapter.Counters;
        long received = c.DataValues + c.EventValues + c.KeepAlives;
        counters.Text =
            $"pub={adapter.CurrentPublishingInterval.TotalMilliseconds:0}ms  items={tab.Items.Count}  " +
            $"received={received}  dropped={adapter.DroppedNotificationCount}";
        long gaps = adapter.MissingMessageCount;
        long republished = adapter.RepublishMessageCount;
        long dropped = adapter.DroppedNotificationCount;
        if (gaps > 0 || republished > 0 || dropped > 0)
        {
            warning.Text = $"gaps {gaps} · republished {republished} · dropped {dropped}";
            warning.IsVisible = true;
        }
        else
        {
            warning.IsVisible = false;
        }
    }

    private void SetDiagnostics(bool show)
    {
        m_diagnosticsVisible = show;
        m_syncing = true;
        try
        {
            m_window.RequiredControl<MenuItem>("MenuToggleDiag").IsChecked = show;
            m_window.RequiredControl<ToggleButton>("ToggleDiagButton").IsChecked = show;
        }
        finally
        {
            m_syncing = false;
        }
        var panel = m_window.RequiredControl<DiagnosticsView>("DiagnosticsPanel");
        panel.IsVisible = show;
        panel.Bind(show ? m_vm.Connection.Session : null);
    }

    private void SetLog(bool show)
    {
        m_logVisible = show;
        m_syncing = true;
        try
        {
            m_window.RequiredControl<MenuItem>("MenuToggleLog").IsChecked = show;
            m_window.RequiredControl<ToggleButton>("ToggleLogButton").IsChecked = show;
        }
        finally
        {
            m_syncing = false;
        }
        m_window.RequiredControl<Border>("LogPanel").IsVisible = show;
    }

    private void ToggleAddressSpace()
    {
        var menu = m_window.RequiredControl<MenuItem>("MenuToggleAddressSpace");
        menu.IsChecked = !menu.IsChecked;
        m_vm.IsAddressSpaceVisible = menu.IsChecked;
        ApplyAddressSpaceVisibility(menu.IsChecked);
    }

    private void ToggleFilters()
    {
        m_vm.Browser.ShowFilters = !m_vm.Browser.ShowFilters;
    }

    private void SyncFiltersState()
    {
        bool show = m_vm.Browser.ShowFilters;
        m_window.RequiredControl<MenuItem>("MenuToggleFilters").IsChecked = show;
        m_window.RequiredControl<ToggleButton>("ToggleFiltersBtn").IsChecked = show;
    }

    private void ApplyInspectorFromMenu()
    {
        bool attrs = m_window.RequiredControl<MenuItem>("MenuToggleAttrs").IsChecked;
        bool refs = m_window.RequiredControl<MenuItem>("MenuToggleRefs").IsChecked;
        m_vm.AttributesPanelMode = (attrs, refs) switch
        {
            (true, true) => SidePanelMode.AttrsAndRefs,
            (true, false) => SidePanelMode.AttrsOnly,
            (false, true) => SidePanelMode.RefsOnly,
            _ => SidePanelMode.None
        };
        UpdateLeftStackRows();
    }

    private void SyncInspectorMenu()
    {
        m_window.RequiredControl<MenuItem>("MenuToggleAttrs").IsChecked = m_vm.ShowAttributes;
        m_window.RequiredControl<MenuItem>("MenuToggleRefs").IsChecked = m_vm.ShowReferences;
    }

    private void UpdateLeftStackRows()
    {
        var grid = m_window.RequiredControl<Grid>("LeftStackGrid");
        if (grid.RowDefinitions.Count < 7)
        {
            return;
        }
        var star = new GridLength(1, GridUnitType.Star);
        var zero = new GridLength(0);
        var splitter = new GridLength(4);
        bool a = m_vm.ShowAttributes;
        bool r = m_vm.ShowReferences;
        grid.RowDefinitions[4].Height = a ? star : zero;
        grid.RowDefinitions[3].Height = a ? splitter : zero;
        grid.RowDefinitions[6].Height = r ? star : zero;
        grid.RowDefinitions[5].Height = a && r ? splitter : zero;
    }

    private void ApplyAddressSpaceVisibility(bool show)
    {
        var grid = m_window.RequiredControl<Grid>("MainBody");
        if (grid.ColumnDefinitions.Count < 3)
        {
            return;
        }
        grid.ColumnDefinitions[0].Width = show ? new GridLength(360) : new GridLength(0);
        grid.ColumnDefinitions[1].Width = show ? new GridLength(4) : new GridLength(0);
    }

    private void SyncTheme()
    {
        ThemePreset current = ThemeManager.Current;
        m_window.RequiredControl<MenuItem>("MenuThemeSystem").IsChecked = current == ThemePreset.System;
        m_window.RequiredControl<MenuItem>("MenuThemeLight").IsChecked = current == ThemePreset.Light;
        m_window.RequiredControl<MenuItem>("MenuThemeDark").IsChecked = current == ThemePreset.DarkStandard;
        var navy = m_window.RequiredControl<MenuItem>("MenuThemeDarkNavy");
        navy.IsChecked = current == ThemePreset.DarkNavy;
        navy.IsVisible = navy.IsVisible || current == ThemePreset.DarkNavy;
    }

    private async Task LoadFavoritesAsync()
    {
        try
        {
            m_favorites = await FavoritesStore.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            m_vm.ConnectionStatus = $"Favourites could not be loaded: {error.Message}";
            MainWindowLog.TabActionFailed(m_log, "favorites-load", error);
        }
        RebuildWelcomeList();
    }

    private async Task ToggleFavoriteAsync()
    {
        string url = m_vm.EndpointUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            m_vm.ConnectionStatus = "Enter an absolute endpoint URL before saving a favourite.";
            return;
        }
        int existing = m_favorites.FindIndex(f => ConnectionProfile.EndpointUrlsMatch(f, url));
        if (existing >= 0)
        {
            m_favorites.RemoveAt(existing);
        }
        else
        {
            m_favorites.Insert(0, url);
        }
        RebuildWelcomeList();
        try
        {
            await FavoritesStore.SaveAsync(m_favorites).ConfigureAwait(true);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            m_vm.ConnectionStatus = $"Favourites could not be saved: {error.Message}";
            MainWindowLog.TabActionFailed(m_log, "favorites-save", error);
        }
    }

    private void TrackConnectedEndpoint()
    {
        ConnectionSnapshot snapshot = m_vm.Connection.Snapshot;
        if (snapshot.Phase != ConnectionPhase.Connected || snapshot.Profile is not { } profile)
        {
            return;
        }
        if (!string.IsNullOrEmpty(profile.EndpointUrl))
        {
            m_vm.EndpointUrl = profile.EndpointUrl;
            AddRecent(profile.EndpointUrl);
        }
    }

    private void AddRecent(string url)
    {
        m_recent.RemoveAll(r => ConnectionProfile.EndpointUrlsMatch(r, url));
        m_recent.Insert(0, url);
        while (m_recent.Count > 8)
        {
            m_recent.RemoveAt(m_recent.Count - 1);
        }
        RebuildWelcomeList();
    }

    private void RebuildWelcomeList()
    {
        m_welcomeItems.Clear();
        foreach (string favorite in m_favorites)
        {
            m_welcomeItems.Add(favorite);
        }
        foreach (string recent in m_recent)
        {
            if (!m_favorites.Exists(f => ConnectionProfile.EndpointUrlsMatch(f, recent)))
            {
                m_welcomeItems.Add(recent);
            }
        }
    }

    private void RefreshHistoryFlyout()
    {
        if (m_historyFlyout is null)
        {
            return;
        }
        m_historyFlyout.Items.Clear();
        AddHistorySection("Favourites", m_favorites);
        AddHistorySection("Recent", m_recent);
        if (m_historyFlyout.Items.Count == 0)
        {
            m_historyFlyout.Items.Add(new MenuItem { Header = "No saved endpoints", IsEnabled = false });
        }
    }

    private void AddHistorySection(string title, System.Collections.Generic.List<string> urls)
    {
        if (urls.Count == 0 || m_historyFlyout is null)
        {
            return;
        }
        m_historyFlyout.Items.Add(new MenuItem { Header = title, IsEnabled = false });
        foreach (string url in urls)
        {
            string target = url;
            var item = new MenuItem { Header = url };
            item.Click += (_, _) => m_vm.EndpointUrl = target;
            m_historyFlyout.Items.Add(item);
        }
    }

    private async Task SetEndpointAndConnectAsync(string url)
    {
        m_vm.EndpointUrl = url;
        if (!m_vm.Connection.IsConnected && m_vm.Connection.Snapshot.Phase != ConnectionPhase.Connecting)
        {
            await m_vm.ConnectCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private async Task DispatchAsync(CommandDescriptor command)
    {
        try
        {
            await command.ExecuteAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            MainWindowLog.CommandDispatchFailed(m_log, command.Id, error);
        }
    }

    private void InvokeMenu(string name)
    {
        var item = m_window.RequiredControl<MenuItem>(name);
        if (item.IsEnabled)
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
    }

    private IBrush Brush(string key)
        => Application.Current?.FindResource(key) as IBrush ?? Brushes.Transparent;

    private static string? BuildGesture(KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
        {
            return null;
        }
        var sb = new StringBuilder();
        KeyModifiers modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            sb.Append("Ctrl+");
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append("Alt+");
        }
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append("Shift+");
        }
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            sb.Append("Meta+");
        }
        sb.Append(e.Key.ToString());
        return sb.ToString();
    }

    private void ShowHelp()
    {
        var sb = new StringBuilder();
        sb.Append("  ─── Navigation ───\n")
          .Append("  Ctrl+Tab / Ctrl+Shift+Tab   cycle documents\n")
          .Append("  Ctrl+B    address space      Ctrl+Shift+F  filters\n")
          .Append("  Ctrl+Alt+I  cycle inspector\n\n")
          .Append("  ─── File ───\n")
          .Append("  Ctrl+E  Export NodeSet2      Ctrl+Shift+E  Export document data\n")
          .Append("  Ctrl+O  Open workspace       Ctrl+S        Save workspace\n")
          .Append("  Ctrl+K  Manage certificates  Ctrl+Q        Quit\n\n")
          .Append("  ─── Connection ───\n")
          .Append("  Ctrl+N  Connect / Cancel / Disconnect     Esc  Cancel\n\n")
          .Append("  ─── Documents ───\n")
          .Append("  Ctrl+T  Add tool             Ctrl+W  Close document\n")
          .Append("  F2      Rename document      Ctrl+Alt+D  Duplicate monitor\n\n")
          .Append("  ─── Open tool ───\n");
        foreach (PluginRegistration registration in PluginRegistry.All)
        {
            if (!string.IsNullOrEmpty(registration.InputGesture))
            {
                sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  {0,-18}{1}\n", registration.InputGesture, registration.DisplayName));
            }
        }
        sb.Append("\n  ─── View ───\n")
          .Append("  Ctrl+Shift+G  Diagnostics    Ctrl+L  Log\n")
          .Append("  F1            Cheat sheet\n");

        var help = new Window
        {
            Title = "Cheat sheet",
            Width = 620,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("AppBg"),
            Foreground = Brush("TextPrimary"),
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Margin = new Thickness(20),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                    Text = sb.ToString()
                }
            }
        };
        help.Show(m_window);
    }

    private readonly MainWindow m_window;
    private readonly MainViewModel m_vm;
    private readonly ConnectionController m_connection;
    private readonly ILogger m_log;
    private readonly Func<ThemePreset, Task> m_changeThemeAsync;
    private readonly ObservableCollection<string> m_welcomeItems = [];
    private readonly System.Collections.Generic.List<string> m_recent = [];
    private System.Collections.Generic.List<string> m_favorites = [];
    private MenuFlyout? m_historyFlyout;
    private DispatcherTimer? m_connectionTimer;
    private DispatcherTimer? m_tabStatusTimer;
    private long m_lastActivityCount;
    private DateTime m_lastActivityUtc = DateTime.MinValue;
    private bool m_diagnosticsVisible;
    private bool m_logVisible;
    private bool m_syncing;
}
