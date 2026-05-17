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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.GdsDiscovery;

/// <summary>
/// The four discovery roots shown at the top of the
/// <see cref="GdsDiscoveryPlugin"/> tree, mirroring the sample's
/// <c>DiscoveryControl</c> (Local Machine / Local Network / Global
/// Discovery / Custom Discovery).
/// </summary>
internal enum DiscoveryRootKind
{
    LocalMachine,
    LocalNetwork,
    GlobalDiscovery,
    CustomDiscovery
}

/// <summary>
/// Single tree node row.  Represents either a discovery root, a server
/// entry (returned by FindServers / FindServersOnNetwork / QueryServers),
/// or a saved custom endpoint.
/// </summary>
internal sealed partial class DiscoveryNode : ObservableObject
{
    [ObservableProperty]
    private bool m_isExpanded;

    public DiscoveryRootKind? RootKind { get; init; }
    public ApplicationDescription? Application { get; init; }
    public ServerOnNetwork? ServerOnNetwork { get; init; }
    public EndpointDescription? Endpoint { get; init; }

    public string Display { get; init; } = string.Empty;
    public string Glyph { get; init; } = "•";
    public ObservableCollection<DiscoveryNode> Children { get; } = new();

    /// <summary>
    /// Picks the best endpoint URL representation for this node — the
    /// canonical DiscoveryUrl when present, otherwise the underlying
    /// SDK record's URL.
    /// </summary>
    public string EndpointUrl
    {
        get
        {
            if (Endpoint is { } ep && !string.IsNullOrEmpty(ep.EndpointUrl))
            {
                return ep.EndpointUrl!;
            }
            if (Application is { } app)
            {
                if (app.DiscoveryUrls is { Count: > 0 } urls)
                {
                    foreach (string u in urls)
                    {
                        if (!string.IsNullOrEmpty(u))
                        {
                            return u!;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(app.ApplicationUri))
                {
                    return app.ApplicationUri!;
                }
            }
            if (ServerOnNetwork is { DiscoveryUrl: { Length: > 0 } u2 })
            {
                return u2;
            }
            return string.Empty;
        }
    }
}

/// <summary>
/// Single endpoint row shown in the right-pane <c>EndpointsList</c>.
/// </summary>
internal sealed partial class DiscoveryEndpointRow : ObservableObject
{
    public required string Url { get; init; }
    public required string SecurityMode { get; init; }
    public required string SecurityProfile { get; init; }
    public required EndpointDescription Endpoint { get; init; }
}

/// <summary>
/// GDS Discovery tab — a Windows-Explorer-style 4-root tree of
/// reachable OPC UA servers driven by the OPC Foundation reference
/// <see cref="LocalDiscoveryServerClient"/> and
/// <see cref="GlobalDiscoveryServerClient"/>. Mirrors the
/// <c>DiscoveryControl</c> from
/// <c>UA-.NETStandard-Samples/Samples/GDS/ClientControls</c>.
/// </summary>
/// <remarks>
/// Roots lazy-load on first expansion: <c>Local Machine</c> calls
/// <see cref="LocalDiscoveryServerClient.FindServersAsync(CancellationToken)"/>
/// against <c>opc.tcp://localhost:4840</c>, <c>Local Network</c> calls
/// <see cref="LocalDiscoveryServerClient.FindServersOnNetworkAsync(uint, uint, CancellationToken)"/>,
/// <c>Global Discovery</c> queries the GDS via
/// <see cref="GlobalDiscoveryServerClient.QueryServersAsync(uint, string?, string?, string?, ArrayOf{string}, CancellationToken)"/>
/// after the user picks a filter, and <c>Custom Discovery</c> caches
/// any endpoints the user has added manually.  Selecting a node loads
/// its endpoints via <see cref="LocalDiscoveryServerClient.GetEndpointsAsync(string, CancellationToken)"/>
/// into the right pane.
/// </remarks>
internal sealed partial class GdsDiscoveryPlugin : ObservableObject, IPlugin
{
    private static int s_nextNumber;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private GdsDiscoveryView? m_view;
    // CA2213: m_lds and m_gds ARE disposed in DisposeAsync below, but the
    // analyzer can't see ownership through async patterns + null checks.
#pragma warning disable CA2213
    private LocalDiscoveryServerClient? m_lds;
    private GlobalDiscoveryServerClient? m_gds;
#pragma warning restore CA2213
    private QueryServersFilter m_gdsFilter = new();

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private DiscoveryNode? m_selectedNode;

    [ObservableProperty]
    private DiscoveryEndpointRow? m_selectedEndpoint;

    [ObservableProperty]
    private string m_status = "● Idle";

    [ObservableProperty]
    private string m_localMachineUrl = "opc.tcp://localhost:4840";

    public ObservableCollection<DiscoveryNode> Roots { get; } = new();
    public ObservableCollection<DiscoveryEndpointRow> Endpoints { get; } = new();

    public GdsDiscoveryPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        m_title = $"GDS Discovery {Interlocked.Increment(ref s_nextNumber)}";

        Roots.Add(MakeRoot(DiscoveryRootKind.LocalMachine, "Local Machine", "💻"));
        Roots.Add(MakeRoot(DiscoveryRootKind.LocalNetwork, "Local Network", "🌐"));
        Roots.Add(MakeRoot(DiscoveryRootKind.GlobalDiscovery, "Global Discovery", "🛰"));
        Roots.Add(MakeRoot(DiscoveryRootKind.CustomDiscovery, "Custom Discovery", "📂"));
    }

    public PluginKind Kind => PluginKind.GdsDiscovery;
    Control? IPlugin.View => m_view ??= new GdsDiscoveryView { DataContext = this };
    Control? IPlugin.HeaderToolbar => null;
    public bool SupportsDuplicate => true;
    public void OnActivated() { }
    public void OnDeactivated() { }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        var refresh = new MenuItem { Header = "_Refresh selected root" };
        refresh.Click += async (_, _) => await RefreshSelectedRootAsync().ConfigureAwait(true);
        var filter = new MenuItem { Header = "_Filter…" };
        filter.Click += async (_, _) => await EditFilterAsync().ConfigureAwait(true);
        var connect = new MenuItem { Header = "_Connect to selection…" };
        connect.Click += (_, _) => ConnectSelectedToConnectionPane();
        var openPush = new MenuItem { Header = "Open as _Push…" };
        openPush.Click += async (_, _) => await OpenAsPluginAsync(PluginKind.GdsPush).ConfigureAwait(true);
        var openMgmt = new MenuItem { Header = "Open as _Management…" };
        openMgmt.Click += async (_, _) => await OpenAsPluginAsync(PluginKind.GdsManagement).ConfigureAwait(true);
        return new[] { refresh, filter, connect, openPush, openMgmt };
    }

    public async ValueTask DisposeAsync()
    {
        if (m_lds is { } lds)
        {
            try
            {
                await lds.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogDebug(ex, "GdsDiscovery: LDS dispose threw.");
            }
            m_lds = null;
        }
        try
        {
            m_gds?.Dispose();
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "GdsDiscovery: GDS dispose threw.");
        }
        m_gds = null;
    }

    // ----- Property hooks -----

    partial void OnSelectedNodeChanged(DiscoveryNode? value)
    {
        _ = RefreshEndpointsAsync(value);
    }

    // ----- Commands -----

    /// <summary>Reloads the root currently selected (or all roots when none is).</summary>
    [RelayCommand]
    public async Task RefreshSelectedRootAsync()
    {
        DiscoveryNode? root = ResolveRoot(SelectedNode);
        if (root is null)
        {
            foreach (DiscoveryNode r in Roots)
            {
                await LoadRootAsync(r).ConfigureAwait(true);
            }
            return;
        }
        root.Children.Clear();
        await LoadRootAsync(root).ConfigureAwait(true);
    }

    /// <summary>Opens the QueryServers filter dialog for the Global Discovery root.</summary>
    [RelayCommand]
    public async Task EditFilterAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        var dlg = new QueryServersFilterDialog(m_gdsFilter);
        QueryServersFilter? next = await dlg.ShowDialog<QueryServersFilter?>(owner)
            .ConfigureAwait(true);
        if (next is null)
        {
            return;
        }
        m_gdsFilter = next;
        DiscoveryNode? gdsRoot = FindRoot(DiscoveryRootKind.GlobalDiscovery);
        if (gdsRoot is not null)
        {
            gdsRoot.Children.Clear();
            await LoadRootAsync(gdsRoot).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Adds the URL entered in the toolbar to the Custom Discovery
    /// folder, then expands it.
    /// </summary>
    [RelayCommand]
    public Task AddCustomEndpointAsync()
    {
        string url = (LocalMachineUrl ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(url))
        {
            Status = "● Enter an endpoint URL first.";
            return Task.CompletedTask;
        }
        DiscoveryNode? custom = FindRoot(DiscoveryRootKind.CustomDiscovery);
        if (custom is null)
        {
            return Task.CompletedTask;
        }
        var node = new DiscoveryNode
        {
            Display = url,
            Glyph = "🔗",
            Endpoint = new EndpointDescription(url)
        };
        custom.Children.Add(node);
        SelectedNode = node;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Hands the selected endpoint back to the main connection pane —
    /// writes the URL into <see cref="MainViewModel.EndpointUrl"/> and
    /// triggers the regular connect flow.
    /// </summary>
    [RelayCommand]
    public void ConnectSelectedToConnectionPane()
    {
        string url = SelectedEndpoint?.Url ?? SelectedNode?.EndpointUrl ?? string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            Status = "● Pick a server or endpoint first.";
            return;
        }
        m_host.Main.EndpointUrl = url;
        Status = $"● {url} → Connection pane.";
    }

    private async Task OpenAsPluginAsync(PluginKind kind)
    {
        EndpointDescription? ep = SelectedEndpoint?.Endpoint;
        if (ep is null && SelectedNode?.Endpoint is { } se)
        {
            ep = se;
        }
        if (ep is null && SelectedNode is { } node && !string.IsNullOrEmpty(node.EndpointUrl))
        {
            ep = new EndpointDescription(node.EndpointUrl);
        }
        if (ep is null)
        {
            Status = "● Pick a server or endpoint first.";
            return;
        }
        // Seed the picked URL on the main pane so the target plug-in's
        // bring-up dialog defaults to it.
        m_host.Main.EndpointUrl = ep.EndpointUrl ?? string.Empty;
        await m_host.Main.AddPluginAsync(kind, seedDiscoveryEndpoint: ep).ConfigureAwait(true);
    }

    // ----- Internals -----

    private DiscoveryNode MakeRoot(DiscoveryRootKind kind, string label, string glyph)
    {
        var node = new DiscoveryNode
        {
            RootKind = kind,
            Display = label,
            Glyph = glyph
        };
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiscoveryNode.IsExpanded)
                && node.IsExpanded && node.Children.Count == 0)
            {
                _ = LoadRootAsync(node);
            }
        };
        return node;
    }

    private async Task LoadRootAsync(DiscoveryNode root)
    {
        try
        {
            Status = $"● Loading {root.Display}…";
            switch (root.RootKind)
            {
                case DiscoveryRootKind.LocalMachine:
                    await LoadLocalMachineAsync(root).ConfigureAwait(true);
                    break;
                case DiscoveryRootKind.LocalNetwork:
                    await LoadLocalNetworkAsync(root).ConfigureAwait(true);
                    break;
                case DiscoveryRootKind.GlobalDiscovery:
                    await LoadGlobalDiscoveryAsync(root).ConfigureAwait(true);
                    break;
                case DiscoveryRootKind.CustomDiscovery:
                    // Custom is user-populated only; nothing to load.
                    break;
            }
            Status = $"● {root.Display}: {root.Children.Count} entry/entries.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "GdsDiscovery: loading {Root} failed.", root.Display);
            Status = $"● {root.Display}: {ex.Message}";
        }
    }

    private async Task LoadLocalMachineAsync(DiscoveryNode root)
    {
        LocalDiscoveryServerClient lds = await EnsureLdsAsync().ConfigureAwait(true);
        ArrayOf<ApplicationDescription> apps = await lds
            .FindServersAsync(LocalMachineUrl, null, CancellationToken.None)
            .ConfigureAwait(true);
        var loaded = new List<DiscoveryNode>(apps.Count);
        foreach (ApplicationDescription app in apps)
        {
            loaded.Add(new DiscoveryNode
            {
                Display = SafeName(app),
                Glyph = "🖥",
                Application = app
            });
        }
        await ApplyChildrenAsync(root, loaded).ConfigureAwait(true);
    }

    private async Task LoadLocalNetworkAsync(DiscoveryNode root)
    {
        LocalDiscoveryServerClient lds = await EnsureLdsAsync().ConfigureAwait(true);
        (ArrayOf<ServerOnNetwork> servers, _) = await lds
            .FindServersOnNetworkAsync(0, 100, CancellationToken.None)
            .ConfigureAwait(true);
        var loaded = new List<DiscoveryNode>(servers.Count);
        foreach (ServerOnNetwork s in servers)
        {
            loaded.Add(new DiscoveryNode
            {
                Display = string.IsNullOrEmpty(s.ServerName) ? (s.DiscoveryUrl ?? "?") : s.ServerName!,
                Glyph = "🛰",
                ServerOnNetwork = s
            });
        }
        await ApplyChildrenAsync(root, loaded).ConfigureAwait(true);
    }

    private async Task LoadGlobalDiscoveryAsync(DiscoveryNode root)
    {
        GlobalDiscoveryServerClient gds = await EnsureGdsAsync().ConfigureAwait(true);
        ArrayOf<ServerOnNetwork> servers = await gds.QueryServersAsync(
            maxRecordsToReturn: 100,
            applicationName: m_gdsFilter.ApplicationName,
            applicationUri: m_gdsFilter.ApplicationUri,
            productUri: m_gdsFilter.ProductUri,
            serverCapabilities: m_gdsFilter.ServerCapabilities,
            CancellationToken.None).ConfigureAwait(true);
        var loaded = new List<DiscoveryNode>(servers.Count);
        foreach (ServerOnNetwork s in servers)
        {
            loaded.Add(new DiscoveryNode
            {
                Display = string.IsNullOrEmpty(s.ServerName) ? (s.DiscoveryUrl ?? "?") : s.ServerName!,
                Glyph = "🛰",
                ServerOnNetwork = s
            });
        }
        await ApplyChildrenAsync(root, loaded).ConfigureAwait(true);
    }

    private async Task RefreshEndpointsAsync(DiscoveryNode? node)
    {
        await Dispatcher.UIThread.InvokeAsync(() => Endpoints.Clear())
            .GetTask().ConfigureAwait(true);
        if (node is null || string.IsNullOrEmpty(node.EndpointUrl))
        {
            return;
        }
        try
        {
            LocalDiscoveryServerClient lds = await EnsureLdsAsync().ConfigureAwait(true);
            ArrayOf<EndpointDescription> endpoints = await lds
                .GetEndpointsAsync(node.EndpointUrl, CancellationToken.None)
                .ConfigureAwait(true);
            var rows = new List<DiscoveryEndpointRow>(endpoints.Count);
            foreach (EndpointDescription ep in endpoints)
            {
                rows.Add(new DiscoveryEndpointRow
                {
                    Url = ep.EndpointUrl ?? string.Empty,
                    SecurityMode = ep.SecurityMode.ToString(),
                    SecurityProfile = ShortenProfile(ep.SecurityPolicyUri),
                    Endpoint = ep
                });
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Endpoints.Clear();
                foreach (DiscoveryEndpointRow r in rows)
                {
                    Endpoints.Add(r);
                }
                Status = string.Format(CultureInfo.InvariantCulture,
                    "● {0} endpoint{1} for {2}",
                    rows.Count, rows.Count == 1 ? "" : "s", node.EndpointUrl);
            }).GetTask().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "GdsDiscovery: GetEndpoints({Url}) failed.", node.EndpointUrl);
            Status = $"● GetEndpoints({node.EndpointUrl}) failed: {ex.Message}";
        }
    }

    private async Task<LocalDiscoveryServerClient> EnsureLdsAsync()
    {
        if (m_lds is not null)
        {
            return m_lds;
        }
        ApplicationConfiguration cfg = await m_host.Connection.GetConfigAsync().ConfigureAwait(true);
        m_lds = new LocalDiscoveryServerClient(cfg);
        return m_lds;
    }

    private async Task<GlobalDiscoveryServerClient> EnsureGdsAsync()
    {
        if (m_gds is not null)
        {
            return m_gds;
        }
        ApplicationConfiguration cfg = await m_host.Connection.GetConfigAsync().ConfigureAwait(true);
        m_gds = new GlobalDiscoveryServerClient(cfg);
        return m_gds;
    }

    private async Task ApplyChildrenAsync(DiscoveryNode root, List<DiscoveryNode> loaded)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            root.Children.Clear();
            foreach (DiscoveryNode n in loaded)
            {
                root.Children.Add(n);
            }
            if (loaded.Count == 0)
            {
                root.Children.Add(new DiscoveryNode { Display = "(no servers)", Glyph = "—" });
            }
        }).GetTask().ConfigureAwait(true);
    }

    private DiscoveryNode? FindRoot(DiscoveryRootKind kind)
    {
        foreach (DiscoveryNode r in Roots)
        {
            if (r.RootKind == kind)
            {
                return r;
            }
        }
        return null;
    }

    private static DiscoveryNode? ResolveRoot(DiscoveryNode? node)
    {
        if (node is null)
        {
            return null;
        }
        return node.RootKind is null ? null : node;
    }

    private static string SafeName(ApplicationDescription app)
    {
        if (app.ApplicationName is { IsNull: false } lt && !string.IsNullOrEmpty(lt.Text))
        {
            return lt.Text!;
        }
        if (!string.IsNullOrEmpty(app.ApplicationUri))
        {
            return app.ApplicationUri!;
        }
        if (app.DiscoveryUrls is { Count: > 0 } urls && !string.IsNullOrEmpty(urls[0]))
        {
            return urls[0]!;
        }
        return "(unnamed)";
    }

    private static string ShortenProfile(string? policyUri)
    {
        if (string.IsNullOrEmpty(policyUri))
        {
            return "—";
        }
        int slash = policyUri!.LastIndexOf('#');
        return slash >= 0 ? policyUri[(slash + 1)..] : policyUri;
    }

    private static Window? GetOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desk)
        {
            return desk.MainWindow;
        }
        return null;
    }
}

/// <summary>
/// Minimal filter applied to <c>QueryServersAsync</c> against the GDS.
/// Mutable so that <see cref="QueryServersFilterDialog"/> can edit the
/// caller's instance without round-tripping JSON.
/// </summary>
internal sealed class QueryServersFilter
{
    public string? ApplicationName { get; set; }
    public string? ApplicationUri { get; set; }
    public string? ProductUri { get; set; }
    public List<string> ServerCapabilities { get; } = new();
}
