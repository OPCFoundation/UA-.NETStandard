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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using UaLens.Plugins.GdsPush;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Single-row VM for a certificate displayed in the
/// <see cref="CertGroupView"/>'s per-group trust-list listbox.  Kept
/// tiny + reflection-free so it stays AOT-clean.
/// </summary>
internal sealed partial class GdsAppCertItem : ObservableObject
{
    [ObservableProperty]
    private string m_thumbprint = string.Empty;

    [ObservableProperty]
    private string m_subject = string.Empty;

    [ObservableProperty]
    private string m_issuer = string.Empty;

    [ObservableProperty]
    private string m_notBefore = string.Empty;

    [ObservableProperty]
    private string m_notAfter = string.Empty;

    [ObservableProperty]
    private string m_bucket = string.Empty;
}

/// <summary>
/// One certificate group attached to a registered application as
/// returned by <see cref="GlobalDiscoveryServerClient.GetCertificateGroupsAsync"/>.
/// Drives the right-hand cert-groups panel in the Management view.
/// </summary>
internal sealed partial class GdsCertGroupVm : ObservableObject
{
    public NodeId GroupId { get; init; } = NodeId.Null;

    [ObservableProperty]
    private string m_displayName = string.Empty;

    /// <summary>Trust-list / issuers / rejected entries for this group,
    /// populated by <see cref="GdsManagementPlugin.RefreshCertGroupsAsync"/>.</summary>
    public ObservableCollection<GdsAppCertItem> Trusted { get; } = new();
    public ObservableCollection<GdsAppCertItem> Issuers { get; } = new();
}

/// <summary>
/// GDS Management plugin.  Owns a per-tab
/// <see cref="GlobalDiscoveryServerClient"/>, enumerates registered
/// applications via <c>QueryApplications</c>, resolves per-app records via
/// <c>FindApplication</c>, and surfaces Register / Unregister / Issue
/// Cert / Cert-Groups commands targeting the selected app.  Auto-numbered
/// title per kind ("GDS Management 1"…).
/// </summary>
internal sealed partial class GdsManagementPlugin : ObservableObject, IPlugin
{
    private static readonly Dictionary<PluginKind, int> s_perKindCounter = new();

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private GlobalDiscoveryServerClient? m_client;
    private GdsManagementView? m_view;
    private EndpointDescription? m_boundEndpoint;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    /// <summary>Endpoint URL of the Global Discovery Server to connect to.
    /// Defaults to the host's current endpoint so the common case is
    /// one-click.</summary>
    [ObservableProperty]
    private string m_endpointUrl;

    [ObservableProperty]
    private string m_connectionStatus = "● Disconnected";

    private bool m_secondaryConnected;

    /// <summary>
    /// True when GDS Management can run: either a secondary GDS
    /// session is live, or the outer Connection-pane session is
    /// connected with SignAndEncrypt.
    /// </summary>
    public bool IsConnected => m_secondaryConnected || OuterIsSuitable();

    /// <summary>
    /// True only when this plugin owns a live secondary GDS session.
    /// Drives the Disconnect button's visibility.
    /// </summary>
    public bool HasSecondarySession => m_client?.Session is { Connected: true };

    /// <summary>
    /// Telegraphs to the user that the outer session, while connected,
    /// can't be reused as-is because it lacks SignAndEncrypt.
    /// </summary>
    public string ConnectButtonText => OuterIsInsecure()
        ? "Connect securely…"
        : "Use different endpoint…";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string m_filterText = string.Empty;

    [ObservableProperty]
    private RegisteredApp? m_selectedApp;

    [ObservableProperty]
    private string m_selectedAppDetail = "No application selected.";

    [ObservableProperty]
    private string m_lastOperationResult = string.Empty;

    [ObservableProperty]
    private string m_status = "● Disconnected";

    /// <summary>All apps reported by the most recent
    /// <c>QueryApplications</c> call.  Unfiltered.</summary>
    public ObservableCollection<RegisteredApp> AllApps { get; } = new();

    /// <summary>Apps after applying <see cref="FilterText"/> — what the
    /// ListBox actually binds to.</summary>
    public ObservableCollection<RegisteredApp> FilteredApps { get; } = new();

    /// <summary>Certificate groups attached to the selected app.</summary>
    public ObservableCollection<GdsCertGroupVm> CertGroups { get; } = new();

    public GdsManagementPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n;
        lock (s_perKindCounter)
        {
            s_perKindCounter.TryGetValue(PluginKind.GdsManagement, out int prev);
            n = prev + 1;
            s_perKindCounter[PluginKind.GdsManagement] = n;
        }
        m_title = $"GDS Management {n}";
        m_endpointUrl = host.Main.EndpointUrl ?? string.Empty;
        // Outer session changes: re-evaluate computed status props.
        m_host.Connection.StateChanged += OnOuterStateChanged;
    }

    private void OnOuterStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(HasSecondarySession));
            OnPropertyChanged(nameof(ConnectButtonText));
            UpdateStatus();
        });
    }

    private bool OuterIsSuitable()
    {
        ManagedSession? s = m_host.Connection.Session;
        return s is { Connected: true }
            && s.ConfiguredEndpoint?.Description is { } d
            && d.SecurityMode == MessageSecurityMode.SignAndEncrypt;
    }

    private bool OuterIsInsecure()
    {
        ManagedSession? s = m_host.Connection.Session;
        return s is { Connected: true }
            && s.ConfiguredEndpoint?.Description is { } d
            && d.SecurityMode != MessageSecurityMode.SignAndEncrypt;
    }

    private void SetSecondaryConnected(bool value)
    {
        if (m_secondaryConnected == value)
        {
            OnPropertyChanged(nameof(HasSecondarySession));
            return;
        }
        m_secondaryConnected = value;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasSecondarySession));
        OnPropertyChanged(nameof(ConnectButtonText));
    }

    // ----- IPlugin members -----

    public PluginKind Kind => PluginKind.GdsManagement;

    Control? IPlugin.View => m_view ??= new GdsManagementView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        var connect = new MenuItem { Header = "_Use different endpoint…" };
        connect.Click += async (_, _) => await UseDifferentEndpointCommand.ExecuteAsync(null).ConfigureAwait(true);
        var disconnect = new MenuItem { Header = "_Disconnect" };
        disconnect.Click += async (_, _) => await DisconnectCommand.ExecuteAsync(null).ConfigureAwait(true);
        var refresh = new MenuItem { Header = "_Refresh" };
        refresh.Click += async (_, _) => await RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        var register = new MenuItem { Header = "_Register App…" };
        register.Click += async (_, _) => await RegisterApplicationCommand.ExecuteAsync(null).ConfigureAwait(true);
        var unregister = new MenuItem { Header = "_Unregister Selected" };
        unregister.Click += async (_, _) => await UnregisterApplicationCommand.ExecuteAsync(null).ConfigureAwait(true);
        var issue = new MenuItem { Header = "_Issue Cert…" };
        issue.Click += async (_, _) => await IssueNewCertificateCommand.ExecuteAsync(null).ConfigureAwait(true);
        var groups = new MenuItem { Header = "View _Cert Groups" };
        groups.Click += async (_, _) => await ViewCertGroupsCommand.ExecuteAsync(null).ConfigureAwait(true);
        return new[] { connect, disconnect, refresh, register, unregister, issue, groups };
    }

    public void OnActivated() { }

    public void OnDeactivated() { }

    public async ValueTask DisposeAsync()
    {
        try
        {
            m_host.Connection.StateChanged -= OnOuterStateChanged;
        }
        catch
        {
            // tolerate detach failures
        }
        if (m_client is not null)
        {
            try
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "GdsManagement tab {Title}: client dispose failed.", Title);
            }
            m_client = null;
        }
    }

    // ----- Commands -----

    [RelayCommand]
    private async Task UseDifferentEndpointAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await EstablishOrSwitchSessionAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Connect failed: {ex.Message}");
            m_log.LogError(ex, "GdsManagement tab {Title}: UseDifferentEndpoint failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    /// <summary>
    /// Runs the picker flow, then either updates credentials on the
    /// existing GDS session (same endpoint) or tears it down and connects
    /// a fresh one.  Returns true if the session is usable afterward.
    /// </summary>
    private async Task<bool> EstablishOrSwitchSessionAsync(CancellationToken ct)
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            SetResult("Use different endpoint: no owner window.");
            return false;
        }

        string seed = !string.IsNullOrWhiteSpace(EndpointUrl)
            ? EndpointUrl
            : (m_host.Main.EndpointUrl ?? string.Empty);
        if (string.IsNullOrWhiteSpace(seed))
        {
            SetResult("Enter a GDS endpoint URL first.");
            return false;
        }

        ConnectionStatus = "● Selecting endpoint…";
        UaLens.Connection.EndpointCredentialsPicker.Result? pick;
        try
        {
            pick = await UaLens.Connection.EndpointCredentialsPicker
                .PromptAsync(owner, m_host.Main.Telemetry, seed, ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Endpoint picker failed: {ex.Message}");
            ConnectionStatus = IsConnected ? "● Connected" : "● Disconnected";
            return false;
        }
        if (pick is null)
        {
            ConnectionStatus = IsConnected ? "● Connected" : "● Disconnected";
            return false;
        }

        // Reuse path: same endpoint as the existing session → swap identity.
        if (m_client?.Session is { Connected: true } existing
            && UaLens.Connection.EndpointCredentialsPicker.EndpointsMatch(m_boundEndpoint, pick.Endpoint))
        {
            ConnectionStatus = "● Updating credentials…";
            try
            {
                await existing.UpdateSessionAsync(pick.Identity, default, ct).ConfigureAwait(true);
                m_client.AdminCredentials = pick.Identity;
                EndpointUrl = pick.Endpoint.EndpointUrl ?? EndpointUrl;
                ConnectionStatus = "● Connected";
                SetResult("Credentials updated on existing session.");
                return true;
            }
            catch (Exception ex)
            {
                SetResult($"UpdateSession failed: {ex.Message} — reconnecting fresh.");
                m_log.LogWarning(ex, "GdsManagement tab {Title}: UpdateSession failed; reconnecting.", Title);
                // fall through to fresh-connect path
            }
        }

        // Fresh-connect path: dispose existing client, build a new one.
        await SafeDisposeClientAsync().ConfigureAwait(true);
        ConnectionStatus = "● Connecting…";
        try
        {
            ApplicationConfiguration cfg = await m_host.Connection.GetConfigAsync().ConfigureAwait(true);
            var client = new GlobalDiscoveryServerClient(cfg, pick.Identity);
            var configured = new ConfiguredEndpoint(
                null, pick.Endpoint, EndpointConfiguration.Create(cfg));
            await client.ConnectAsync(configured, ct).ConfigureAwait(true);
            m_client = client;
            m_boundEndpoint = pick.Endpoint;
            EndpointUrl = pick.Endpoint.EndpointUrl ?? EndpointUrl;
            SetSecondaryConnected(true);
            ConnectionStatus = "● Connected";
            m_log.LogInformation(
                "GdsManagement tab {Title}: connected to {Endpoint}", Title, pick.Endpoint.EndpointUrl);
            await RefreshAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            SetSecondaryConnected(false);
            ConnectionStatus = "● Disconnected";
            SetResult($"Connect failed: {ex.Message}");
            m_log.LogError(ex, "GdsManagement tab {Title}: connect failed.", Title);
            await SafeDisposeClientAsync().ConfigureAwait(true);
            m_boundEndpoint = null;
            return false;
        }
    }

    /// <summary>
    /// Returns a connected GDS client; auto-piggybacks on the outer
    /// session when it is connected with SignAndEncrypt, otherwise
    /// prompts the user via the picker flow.  Null on cancel /
    /// connect failure.
    /// </summary>
    private async Task<GlobalDiscoveryServerClient?> EnsureSessionAsync(CancellationToken ct)
    {
        if (m_client is { Session: { Connected: true } } good)
        {
            return good;
        }
        if (OuterIsSuitable()
            && await TryAutoPiggybackAsync(ct).ConfigureAwait(true)
            && m_client is { Session: { Connected: true } } piggy)
        {
            return piggy;
        }
        if (await EstablishOrSwitchSessionAsync(ct).ConfigureAwait(true))
        {
            return m_client;
        }
        return null;
    }

    /// <summary>
    /// Spawns a fresh <see cref="GlobalDiscoveryServerClient"/>
    /// against the outer Connection-pane session's endpoint when its
    /// identity is Anonymous.  UserName / X509 outers can't be cloned
    /// safely (the password isn't recoverable), so those fall through
    /// to the picker.
    /// </summary>
    private async Task<bool> TryAutoPiggybackAsync(CancellationToken ct)
    {
        if (m_host.Connection.Session is not { Connected: true } outer
            || outer.ConfiguredEndpoint?.Description is not { } desc)
        {
            return false;
        }
        if (outer.Identity is not { TokenType: UserTokenType.Anonymous })
        {
            return false;
        }
        ConnectionStatus = "● Connecting (piggyback)…";
        try
        {
            ApplicationConfiguration cfg = await m_host.Connection.GetConfigAsync().ConfigureAwait(true);
#pragma warning disable CA2000
            IUserIdentity identity = new UserIdentity(new AnonymousIdentityToken());
#pragma warning restore CA2000
            var client = new GlobalDiscoveryServerClient(cfg, identity);
            var configured = new ConfiguredEndpoint(
                null, desc, EndpointConfiguration.Create(cfg));
            await client.ConnectAsync(configured, ct).ConfigureAwait(true);
            m_client = client;
            m_boundEndpoint = desc;
            EndpointUrl = desc.EndpointUrl ?? EndpointUrl;
            SetSecondaryConnected(true);
            ConnectionStatus = "● Connected (piggyback)";
            m_log.LogInformation(
                "GdsManagement tab {Title}: piggy-backed on outer session at {Endpoint}.",
                Title, desc.EndpointUrl);
            return true;
        }
        catch (Exception ex)
        {
            SetResult($"Piggyback failed: {ex.Message}");
            m_log.LogWarning(ex, "GdsManagement tab {Title}: piggyback to outer failed.", Title);
            await SafeDisposeClientAsync().ConfigureAwait(true);
            m_boundEndpoint = null;
            ConnectionStatus = "● Disconnected";
            return false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await SafeDisposeClientAsync().ConfigureAwait(true);
            SetSecondaryConnected(false);
            m_boundEndpoint = null;
            ConnectionStatus = "● Disconnected";
            AllApps.Clear();
            FilteredApps.Clear();
            CertGroups.Clear();
            SelectedApp = null;
            SelectedAppDetail = "No application selected.";
            SetResult("Disconnected.");
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            GlobalDiscoveryServerClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            (ArrayOf<ApplicationDescription> apps, _, _) = await client.QueryApplicationsAsync(
                0,
                0,
                string.Empty,
                string.Empty,
                0,
                string.Empty,
                Array.Empty<string>(),
                CancellationToken.None).ConfigureAwait(true);

            AllApps.Clear();
            if (!apps.IsNull)
            {
                foreach (ApplicationDescription desc in apps)
                {
                    AllApps.Add(RegisteredApp.FromDescription(desc));
                }
            }
            await ResolveAllRecordsAsync().ConfigureAwait(true);
            ApplyFilter();
            SetResult($"Refreshed: {AllApps.Count} applications.");
            m_log.LogInformation("GdsManagement tab {Title}: refresh ok ({Count} apps).", Title, AllApps.Count);
        }
        catch (Exception ex)
        {
            SetResult($"Refresh failed: {ex.Message}");
            m_log.LogError(ex, "GdsManagement tab {Title}: refresh failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task RegisterApplicationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            SetResult("Register: no owner window.");
            return;
        }
        ApplicationRecordDataType? record;
        try
        {
            var dlg = new RegisterApplicationDialog();
            record = await dlg.ShowDialog<ApplicationRecordDataType?>(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Register dialog failed: {ex.Message}");
            return;
        }
        if (record is null)
        {
            SetResult("Register cancelled.");
            return;
        }
        IsBusy = true;
        try
        {
            GlobalDiscoveryServerClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            NodeId id = await client.RegisterApplicationAsync(
                record, CancellationToken.None).ConfigureAwait(true);
            SetResult($"Registered {record.ApplicationUri} → {id}.");
            m_log.LogInformation("GdsManagement tab {Title}: registered {Uri} → {Id}.",
                Title, record.ApplicationUri, id);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Register failed: {ex.Message}");
            m_log.LogError(ex, "GdsManagement tab {Title}: register failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task UnregisterApplicationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedApp is not { } sel)
        {
            SetResult("Select an application first.");
            return;
        }
        if (sel.ApplicationId.IsNull)
        {
            SetResult("Selected application has no resolved NodeId — refresh first.");
            return;
        }
        IsBusy = true;
        try
        {
            GlobalDiscoveryServerClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            await client.UnregisterApplicationAsync(
                sel.ApplicationId, CancellationToken.None).ConfigureAwait(true);
            SetResult($"Unregistered {sel.ApplicationName}.");
            m_log.LogInformation("GdsManagement tab {Title}: unregistered {Id}.", Title, sel.ApplicationId);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Unregister failed: {ex.Message}");
            m_log.LogError(ex, "GdsManagement tab {Title}: unregister failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task IssueNewCertificateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedApp is not { } sel || sel.Record is null)
        {
            SetResult("Select a resolved application first.");
            return;
        }
        if (sel.ApplicationId.IsNull)
        {
            SetResult("Selected application has no resolved NodeId — refresh first.");
            return;
        }
        IsBusy = true;
        try
        {
            GlobalDiscoveryServerClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            // Discover cert groups for the application; if none are
            // reported, fall back to the default application group (the
            // server may still accept a key-pair request against null).
            ArrayOf<NodeId> groups = await client.GetCertificateGroupsAsync(
                sel.ApplicationId, CancellationToken.None).ConfigureAwait(true);
            NodeId groupId = NodeId.Null;
            if (!groups.IsNull && groups.Count > 0)
            {
                groupId = groups[0];
            }
            NodeId typeId = Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
            string subject = "CN=" + sel.ApplicationName;
            ArrayOf<string> domainNames = Array.Empty<string>();
            NodeId requestId = await client.StartNewKeyPairRequestAsync(
                sel.ApplicationId,
                groupId,
                typeId,
                subject,
                domainNames,
                "PEM",
                Array.Empty<char>(),
                CancellationToken.None).ConfigureAwait(true);
            (ByteString publicKey, ByteString privateKey, ArrayOf<ByteString> issuers) =
                await PollFinishRequestAsync(sel.ApplicationId, requestId, CancellationToken.None)
                    .ConfigureAwait(true);
            string summary = SummariseCert(publicKey);
            int issuerCount = issuers.IsNull ? 0 : issuers.Count;
            SetResult($"Issued cert for {sel.ApplicationName}: {summary} (+{issuerCount} issuer cert(s)).");
            m_log.LogInformation(
                "GdsManagement tab {Title}: issued cert for {App} (request {Req}). Private-key length {Len}.",
                Title, sel.ApplicationName, requestId, privateKey.Memory.Length);
        }
        catch (Exception ex)
        {
            SetResult($"Issue cert failed: {ex.Message}");
            m_log.LogError(ex, "GdsManagement tab {Title}: issue cert failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task ViewCertGroupsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedApp is not { } sel || sel.ApplicationId.IsNull)
        {
            SetResult("Select a resolved application first.");
            return;
        }
        IsBusy = true;
        try
        {
            GlobalDiscoveryServerClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            await RefreshCertGroupsAsync(sel, CancellationToken.None).ConfigureAwait(true);
            SetResult($"Cert groups loaded for {sel.ApplicationName}: {CertGroups.Count} group(s).");
        }
        catch (Exception ex)
        {
            SetResult($"View cert groups failed: {ex.Message}");
            m_log.LogError(ex, "GdsManagement tab {Title}: view cert groups failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    // ----- Helpers -----

    /// <summary>
    /// Resolves each <see cref="RegisteredApp"/> in <see cref="AllApps"/>
    /// against <c>FindApplication</c> to fill in the NodeId required for
    /// management operations.  Best-effort — failures are logged but do
    /// not abort the refresh.
    /// </summary>
    private async Task ResolveAllRecordsAsync()
    {
        if (m_client is null)
        {
            return;
        }

        var snapshot = AllApps.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            RegisteredApp app = snapshot[i];
            if (string.IsNullOrEmpty(app.ApplicationUri))
            {
                continue;
            }

            try
            {
                ArrayOf<ApplicationRecordDataType> hits = await m_client.FindApplicationAsync(
                    app.ApplicationUri, CancellationToken.None).ConfigureAwait(true);
                if (hits.IsNull || hits.Count == 0)
                {
                    continue;
                }

                ApplicationRecordDataType rec = hits[0];
                RegisteredApp resolved = RegisteredApp.FromRecord(rec);
                int idx = AllApps.IndexOf(app);
                if (idx >= 0)
                {
                    AllApps[idx] = resolved;
                }
            }
            catch (Exception ex)
            {
                m_log.LogDebug(ex,
                    "GdsManagement tab {Title}: FindApplication failed for {Uri}.",
                    Title, app.ApplicationUri);
            }
        }
    }

    /// <summary>
    /// Reapplies <see cref="FilterText"/> to <see cref="AllApps"/>, writing
    /// the result into <see cref="FilteredApps"/>.  Case-insensitive
    /// substring match against the display fields.
    /// </summary>
    private void ApplyFilter()
    {
        FilteredApps.Clear();
        string needle = (FilterText ?? string.Empty).Trim();
        foreach (RegisteredApp app in AllApps)
        {
            if (needle.Length == 0 || MatchesFilter(app, needle))
            {
                FilteredApps.Add(app);
            }
        }
    }

    private static bool MatchesFilter(RegisteredApp app, string needle)
    {
        return Contains(app.ApplicationName, needle) ||
               Contains(app.ApplicationUri, needle) ||
               Contains(app.ProductUri, needle) ||
               Contains(app.ApplicationType, needle) ||
               Contains(app.ServerCapabilities, needle);

        static bool Contains(string hay, string n) =>
            !string.IsNullOrEmpty(hay) &&
            hay.Contains(n, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Populates the <see cref="CertGroups"/> collection for the given
    /// application.  Reads the cert groups, then for each group reads the
    /// trust list to surface trusted + issuer certificates.
    /// </summary>
    private async Task RefreshCertGroupsAsync(RegisteredApp app, CancellationToken ct)
    {
        if (m_client is null)
        {
            return;
        }

        CertGroups.Clear();
        ArrayOf<NodeId> groupIds = await m_client.GetCertificateGroupsAsync(
            app.ApplicationId, ct).ConfigureAwait(true);
        if (groupIds.IsNull)
        {
            return;
        }

        for (int i = 0; i < groupIds.Count; i++)
        {
            NodeId gid = groupIds[i];
            var group = new GdsCertGroupVm
            {
                GroupId = gid,
                DisplayName = gid.ToString() ?? "(unnamed)"
            };
            try
            {
                NodeId trustListId = await m_client.GetTrustListAsync(
                    app.ApplicationId, gid, ct).ConfigureAwait(true);
                if (!trustListId.IsNull)
                {
                    TrustListDataType list = await m_client.ReadTrustListAsync(trustListId, ct)
                        .ConfigureAwait(true);
                    PopulateGroupCerts(group, list);
                }
            }
            catch (Exception ex)
            {
                m_log.LogDebug(ex, "GdsManagement tab {Title}: trust-list read failed for group {Group}.",
                    Title, gid);
            }
            CertGroups.Add(group);
        }
    }

    private static void PopulateGroupCerts(GdsCertGroupVm group, TrustListDataType list)
    {
        if (list?.TrustedCertificates is { } trusted)
        {
            foreach (ByteString der in trusted)
            {
                if (TryParseCert(der, out X509Certificate2? cert) && cert is not null)
                {
                    group.Trusted.Add(ToItem(cert, "Trusted"));
                }
            }
        }
        if (list?.IssuerCertificates is { } issuers)
        {
            foreach (ByteString der in issuers)
            {
                if (TryParseCert(der, out X509Certificate2? cert) && cert is not null)
                {
                    group.Issuers.Add(ToItem(cert, "Issuer"));
                }
            }
        }
    }

    private static bool TryParseCert(ByteString bs, out X509Certificate2? cert)
    {
        try
        {
            if (bs.Memory.IsEmpty)
            {
                cert = null;
                return false;
            }
            cert = X509CertificateLoader.LoadCertificate(bs.Memory.ToArray());
            return true;
        }
        catch
        {
            cert = null;
            return false;
        }
    }

    private static GdsAppCertItem ToItem(X509Certificate2 cert, string bucket)
    {
        return new GdsAppCertItem
        {
            Thumbprint = cert.Thumbprint,
            Subject = ShortName(cert.Subject),
            Issuer = ShortName(cert.Issuer),
            NotBefore = cert.NotBefore.ToUniversalTime()
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            NotAfter = cert.NotAfter.ToUniversalTime()
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Bucket = bucket
        };
    }

    private static string ShortName(string distinguished)
    {
        if (string.IsNullOrEmpty(distinguished))
        {
            return "—";
        }

        foreach (string rdn in distinguished.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rdn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return rdn[3..];
            }
        }
        return distinguished;
    }

    /// <summary>
    /// Polls <see cref="GlobalDiscoveryServerClient.FinishRequestAsync"/>
    /// until the server delivers the signed certificate or the call
    /// throws.  Uses a short backoff so the UI stays responsive while
    /// the certificate authority signs the request.
    /// </summary>
    private async Task<(ByteString publicKey, ByteString privateKey, ArrayOf<ByteString> issuers)>
        PollFinishRequestAsync(NodeId applicationId, NodeId requestId, CancellationToken ct)
    {
        if (m_client is null)
        {
            throw new InvalidOperationException("Client disconnected.");
        }
        for (int attempt = 0; attempt < 30; attempt++)
        {
            (ByteString publicKey, ByteString privateKey, ArrayOf<ByteString> issuers) =
                await m_client.FinishRequestAsync(applicationId, requestId, ct).ConfigureAwait(true);
            if (!publicKey.Memory.IsEmpty)
            {
                return (publicKey, privateKey, issuers);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(true);
        }
        throw new TimeoutException("FinishRequest did not produce a certificate within 30 seconds.");
    }

    private static string SummariseCert(ByteString der)
    {
        try
        {
            if (der.Memory.IsEmpty)
            {
                return "(empty)";
            }

            X509Certificate2 cert = GdsCertRequestHelper.ParseCertificate(der.Memory.ToArray());
            return $"{ShortName(cert.Subject)} (expires {cert.NotAfter:yyyy-MM-dd})";
        }
        catch (Exception ex)
        {
            return $"(unparsable: {ex.Message})";
        }
    }

    private async Task SafeDisposeClientAsync()
    {
        if (m_client is null)
        {
            return;
        }

        try
        {
            await m_client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "GdsManagement tab {Title}: disconnect threw (suppressed).", Title);
        }
        try
        {
            await m_client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "GdsManagement tab {Title}: dispose threw (suppressed).", Title);
        }
        m_client = null;
    }

    private static Window? GetOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private void SetResult(string text)
    {
        LastOperationResult = text;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (!IsConnected)
        {
            Status = string.IsNullOrEmpty(LastOperationResult)
                ? "● Disconnected"
                : $"● Disconnected · {LastOperationResult}";
            return;
        }
        Status = $"● Connected · {AllApps.Count} apps · {CertGroups.Count} group(s)" +
                 (string.IsNullOrEmpty(LastOperationResult)
                     ? string.Empty
                     : $" · {LastOperationResult}");
    }

    partial void OnFilterTextChanged(string value) => Dispatcher.UIThread.Post(ApplyFilter);

    partial void OnSelectedAppChanged(RegisteredApp? value)
    {
        if (value is null)
        {
            SelectedAppDetail = "No application selected.";
            CertGroups.Clear();
            UpdateStatus();
            return;
        }
        var sb = new StringBuilder();
        sb.Append("Application Name: ").AppendLine(value.ApplicationName);
        sb.Append("Application URI : ").AppendLine(value.ApplicationUri);
        sb.Append("Product URI     : ").AppendLine(value.ProductUri);
        sb.Append("Application Type: ").AppendLine(value.ApplicationType);
        sb.Append("NodeId          : ").AppendLine(value.Identifier);
        if (!string.IsNullOrEmpty(value.DiscoveryUrls))
        {
            sb.AppendLine().AppendLine("Discovery URLs:");
            sb.AppendLine(value.DiscoveryUrls);
        }
        if (!string.IsNullOrEmpty(value.ServerCapabilities))
        {
            sb.AppendLine().Append("Server Capabilities: ").AppendLine(value.ServerCapabilities);
        }
        SelectedAppDetail = sb.ToString();
        UpdateStatus();
    }

    partial void OnLastOperationResultChanged(string value) => UpdateStatus();
}
