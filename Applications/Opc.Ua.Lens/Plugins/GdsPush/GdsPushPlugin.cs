/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Selected sub-tab in the trust-list view.  Drives Add / Remove
/// destination semantics.
/// </summary>
internal enum TrustListBucket
{
    Trusted,
    Issuer,
    Rejected
}

/// <summary>
/// Single-row VM for a certificate displayed in any of the three trust-list
/// tabs.  Kept tiny and free of any control references so it stays
/// AOT-clean and re-bindable across panel switches.
/// </summary>
internal sealed partial class GdsCertItem : ObservableObject
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

    public X509Certificate2 Certificate { get; init; } = null!;
}

/// <summary>
/// GDS Push tab application.  Owns a per-tab
/// <see cref="ServerPushConfigurationClient"/>, surfaces the connected
/// server's trust list (Trusted Peers / Issuers / Rejected), exposes the
/// CSR + UpdateCertificate flow via <see cref="GdsCertRequestHelper"/>, and
/// runs ApplyChanges.  Auto-numbered title per kind ("GDS Push 1"…).
/// </summary>
internal sealed partial class GdsPushPlugin : ObservableObject, IPlugin
{
    private static readonly Dictionary<PluginKind, int> s_perKindCounter = new();

    private readonly PluginHost m_host;
    private readonly ILogger m_log;

    private ServerPushConfigurationClient? m_client;
    private GdsPushView? m_view;
    private CancellationTokenSource? m_busyCts;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    /// <summary>Endpoint URL — for display.  Reflects the secondary
    /// Push session's bound endpoint when connected; otherwise mirrors
    /// the main Connection pane's <see cref="MainViewModel.EndpointUrl"/>.</summary>
    public string EndpointUrl => m_boundEndpoint?.EndpointUrl
                                 ?? (m_host.Main.EndpointUrl ?? string.Empty);

    private EndpointDescription? m_boundEndpoint;

    [ObservableProperty]
    private string m_connectionStatus = "● Disconnected";

    private bool m_secondaryConnected;

    /// <summary>
    /// True when GDS Push can actually run: either a secondary Push
    /// session is live, or the outer Connection-pane session is
    /// connected with SignAndEncrypt and can be piggy-backed on by
    /// <see cref="EnsureSessionAsync"/>.
    /// </summary>
    public bool IsConnected => m_secondaryConnected || OuterIsSuitable();

    /// <summary>
    /// True only when this plugin owns a live secondary Push session.
    /// Drives the Disconnect button's visibility — Disconnect tears
    /// down the secondary, never touches the outer session.
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
    private string m_serverApplicationName = "—";

    [ObservableProperty]
    private string m_serverApplicationUri = "—";

    [ObservableProperty]
    private string m_serverCertSubject = "—";

    [ObservableProperty]
    private string m_serverCertIssuer = "—";

    [ObservableProperty]
    private string m_serverCertExpiry = "—";

    [ObservableProperty]
    private TrustListBucket m_activeBucket = TrustListBucket.Trusted;

    [ObservableProperty]
    private GdsCertItem? m_selectedCertificate;

    [ObservableProperty]
    private string m_lastOperationResult = string.Empty;

    [ObservableProperty]
    private string m_status = "● Disconnected";

    public ObservableCollection<GdsCertItem> Trusted { get; } = new();
    public ObservableCollection<GdsCertItem> Issuers { get; } = new();
    public ObservableCollection<GdsCertItem> Rejected { get; } = new();

    public GdsPushPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n;
        lock (s_perKindCounter)
        {
            s_perKindCounter.TryGetValue(PluginKind.GdsPush, out int prev);
            n = prev + 1;
            s_perKindCounter[PluginKind.GdsPush] = n;
        }
        m_title = $"GDS Push {n}";
        // Track changes to the main Connection pane's endpoint URL so our
        // read-only display + Connect button always use the latest value.
        m_host.Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.EndpointUrl))
            {
                OnPropertyChanged(nameof(EndpointUrl));
            }
        };
        // Outer session changes: re-evaluate computed status props so
        // the GDS panel reflects the main Connection panel's state.
        m_host.Connection.StateChanged += OnOuterStateChanged;
    }

    private void OnOuterStateChanged()
    {
        // Marshal to UI thread; StateChanged may fire from a worker.
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

    public PluginKind Kind => PluginKind.GdsPush;

    Control? IPlugin.View => m_view ??= new GdsPushView { DataContext = this };

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
        var addCert = new MenuItem { Header = "_Add Cert…" };
        addCert.Click += async (_, _) => await AddCertificateCommand.ExecuteAsync(null).ConfigureAwait(true);
        var removeCert = new MenuItem { Header = "_Remove Cert" };
        removeCert.Click += async (_, _) => await RemoveCertificateCommand.ExecuteAsync(null).ConfigureAwait(true);
        var newCert = new MenuItem { Header = "Re_quest New Cert…" };
        newCert.Click += async (_, _) => await RequestNewCertificateCommand.ExecuteAsync(null).ConfigureAwait(true);
        var apply = new MenuItem { Header = "_Apply Changes" };
        apply.Click += async (_, _) => await ApplyChangesCommand.ExecuteAsync(null).ConfigureAwait(true);
        return new[] { connect, disconnect, refresh, addCert, removeCert, newCert, apply };
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
        try
        {
            m_busyCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        if (m_client is not null)
        {
            try
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "GdsPush tab {Title}: client dispose failed.", Title);
            }
            m_client = null;
        }
        m_busyCts?.Dispose();
        m_busyCts = null;
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
            m_log.LogError(ex, "GdsPush tab {Title}: UseDifferentEndpoint failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    /// <summary>
    /// Runs the picker flow, then either updates the credentials on
    /// the existing secondary session (same endpoint) or tears down the
    /// old client and connects a fresh one.  Returns true if the
    /// secondary session is usable after this call.  Callers that just
    /// invoke "Use different endpoint…" can ignore the bool; callers
    /// using <see cref="EnsureSessionAsync"/> use it to decide whether
    /// to proceed.
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
            SetResult("Enter an endpoint URL in the Connection pane first.");
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

        // Reuse path: same endpoint as the existing secondary session →
        // just swap identity via UpdateSessionAsync on the underlying ISession.
        if (m_client?.Session is { Connected: true } existing
            && UaLens.Connection.EndpointCredentialsPicker.EndpointsMatch(m_boundEndpoint, pick.Endpoint))
        {
            ConnectionStatus = "● Updating credentials…";
            try
            {
                await existing.UpdateSessionAsync(pick.Identity, default, ct).ConfigureAwait(true);
                m_client.AdminCredentials = pick.Identity;
                ConnectionStatus = "● Connected";
                SetResult("Credentials updated on existing session.");
                await PopulateServerInfoAsync(ct).ConfigureAwait(true);
                await RefreshAsync().ConfigureAwait(true);
                return true;
            }
            catch (Exception ex)
            {
                SetResult($"UpdateSession failed: {ex.Message} — reconnecting fresh.");
                m_log.LogWarning(ex, "GdsPush tab {Title}: UpdateSession failed; falling back to reconnect.", Title);
                // fall through to fresh-connect path
            }
        }

        // Fresh-connect path: dispose existing client, build a new one.
        await SafeDisposeClientAsync().ConfigureAwait(true);
        ConnectionStatus = "● Connecting…";
        try
        {
            ApplicationConfiguration cfg = await m_host.Connection.GetConfigAsync().ConfigureAwait(true);
            var client = new ServerPushConfigurationClient(cfg);
            client.AdminCredentialsRequired += OnAdminCredentialsRequired;
            client.KeepAlive += OnKeepAlive;
            client.AdminCredentials = pick.Identity;
            var configured = new ConfiguredEndpoint(
                null, pick.Endpoint, EndpointConfiguration.Create(cfg));
            await client.ConnectAsync(configured, ct).ConfigureAwait(true);
            m_client = client;
            m_boundEndpoint = pick.Endpoint;
            OnPropertyChanged(nameof(EndpointUrl));
            SetSecondaryConnected(true);
            ConnectionStatus = "● Connected";
            await PopulateServerInfoAsync(ct).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            m_log.LogInformation(
                "GdsPush tab {Title}: connected to {Endpoint}", Title, pick.Endpoint.EndpointUrl);
            return true;
        }
        catch (Exception ex)
        {
            SetSecondaryConnected(false);
            ConnectionStatus = "● Disconnected";
            SetResult($"Connect failed: {ex.Message}");
            m_log.LogError(ex, "GdsPush tab {Title}: connect failed.", Title);
            await SafeDisposeClientAsync().ConfigureAwait(true);
            m_boundEndpoint = null;
            OnPropertyChanged(nameof(EndpointUrl));
            return false;
        }
    }

    /// <summary>
    /// Used by operations that need a secondary session: returns the
    /// connected client if one exists, otherwise prompts the user via
    /// the picker flow.  Returns null if the user cancels or the
    /// connect attempt fails.
    /// </summary>
    private async Task<ServerPushConfigurationClient?> EnsureSessionAsync(CancellationToken ct)
    {
        if (m_client is { Session: { Connected: true } } good)
        {
            return good;
        }
        // Auto-piggyback: when the outer session is suitable
        // (connected + SignAndEncrypt), connect a secondary Push
        // client to the same endpoint with the outer's identity,
        // skipping the picker.  UserName outers fall through to the
        // picker because the password is not recoverable from the
        // existing IUserIdentity.
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
    /// Spawns a fresh <see cref="ServerPushConfigurationClient"/>
    /// against the outer Connection-pane session's endpoint, using its
    /// identity when the identity is Anonymous (UserName outers can't
    /// be cloned because the password isn't exposed).  Sets the
    /// secondary-connected flag on success.
    /// </summary>
    private async Task<bool> TryAutoPiggybackAsync(CancellationToken ct)
    {
        if (m_host.Connection.Session is not { Connected: true } outer
            || outer.ConfiguredEndpoint?.Description is not { } desc)
        {
            return false;
        }
        // Only anonymous identities can be safely cloned without
        // re-prompting; UserName/X509 need fresh user input.
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
            var client = new ServerPushConfigurationClient(cfg);
            client.AdminCredentialsRequired += OnAdminCredentialsRequired;
            client.KeepAlive += OnKeepAlive;
            client.AdminCredentials = identity;
            var configured = new ConfiguredEndpoint(
                null, desc, EndpointConfiguration.Create(cfg));
            await client.ConnectAsync(configured, ct).ConfigureAwait(true);
            m_client = client;
            m_boundEndpoint = desc;
            OnPropertyChanged(nameof(EndpointUrl));
            SetSecondaryConnected(true);
            ConnectionStatus = "● Connected (piggyback)";
            await PopulateServerInfoAsync(ct).ConfigureAwait(true);
            m_log.LogInformation(
                "GdsPush tab {Title}: piggy-backed on outer session at {Endpoint}.",
                Title, desc.EndpointUrl);
            return true;
        }
        catch (Exception ex)
        {
            SetResult($"Piggyback failed: {ex.Message}");
            m_log.LogWarning(ex, "GdsPush tab {Title}: piggyback to outer failed.", Title);
            await SafeDisposeClientAsync().ConfigureAwait(true);
            m_boundEndpoint = null;
            OnPropertyChanged(nameof(EndpointUrl));
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
            OnPropertyChanged(nameof(EndpointUrl));
            ConnectionStatus = "● Disconnected";
            Trusted.Clear();
            Issuers.Clear();
            Rejected.Clear();
            ServerApplicationName = "—";
            ServerApplicationUri = "—";
            ServerCertSubject = "—";
            ServerCertIssuer = "—";
            ServerCertExpiry = "—";
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
            ServerPushConfigurationClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            TrustListDataType list = await client.ReadTrustListAsync(
                TrustListMasks.All,
                0,
                CancellationToken.None).ConfigureAwait(true);
            Opc.Ua.Security.Certificates.CertificateCollection rejected = await client.GetRejectedListAsync(
                CancellationToken.None).ConfigureAwait(true);
            PopulateTrustList(list, rejected);
            SetResult($"Refreshed: {Trusted.Count} trusted · {Issuers.Count} issuers · {Rejected.Count} rejected.");
            m_log.LogInformation("GdsPush tab {Title}: refresh ok.", Title);
        }
        catch (Exception ex)
        {
            SetResult($"Refresh failed: {ex.Message}");
            m_log.LogError(ex, "GdsPush tab {Title}: refresh failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task AddCertificateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            SetResult("Add Cert: no owner window.");
            return;
        }
        AddCertificateResult? result;
        try
        {
            var dlg = new AddCertificateDialog(ActiveBucket);
            result = await dlg.ShowDialog<AddCertificateResult?>(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Add Cert dialog failed: {ex.Message}");
            return;
        }
        if (result is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ServerPushConfigurationClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            bool isTrusted = result.Bucket == TrustListBucket.Trusted;
            using Opc.Ua.Security.Certificates.Certificate wrapper = Opc.Ua.Security.Certificates.Certificate.From(result.Certificate);
            await client.AddCertificateAsync(wrapper, isTrusted, CancellationToken.None)
                .ConfigureAwait(true);
            SetResult($"Added {ShortName(result.Certificate.Subject)} to " +
                      $"{(isTrusted ? "Trusted Peers" : "Issuers")}.");
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Add Cert failed: {ex.Message}");
            m_log.LogError(ex, "GdsPush tab {Title}: add cert failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task RemoveCertificateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedCertificate is not { } sel)
        {
            SetResult("Select a certificate first.");
            return;
        }
        if (ActiveBucket == TrustListBucket.Rejected)
        {
            // Rejected list is server-managed — remove via UpdateTrustList of
            // an empty trust list is impractical; surface a friendly message.
            SetResult("Rejected entries clear automatically once removed by the server.");
            return;
        }
        IsBusy = true;
        try
        {
            ServerPushConfigurationClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            bool isTrusted = ActiveBucket == TrustListBucket.Trusted;
            await client.RemoveCertificateAsync(sel.Thumbprint, isTrusted, CancellationToken.None)
                .ConfigureAwait(true);
            SetResult($"Removed {ShortName(sel.Subject)} from " +
                      $"{(isTrusted ? "Trusted Peers" : "Issuers")}.");
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetResult($"Remove Cert failed: {ex.Message}");
            m_log.LogError(ex, "GdsPush tab {Title}: remove cert failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task RequestNewCertificateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            SetResult("Request New Cert: no owner window.");
            return;
        }
        try
        {
            ServerPushConfigurationClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            IGdsClientLike adapter = new PushClientAdapter(client);
            var groups = new List<CertificateGroupChoice>
            {
                new("DefaultApplicationGroup", client.DefaultApplicationGroup,
                    client.ApplicationCertificateType, "Application")
            };
            if (!NodeId.IsNull(client.DefaultHttpsGroup))
            {
                groups.Add(new CertificateGroupChoice(
                    "DefaultHttpsGroup", client.DefaultHttpsGroup,
                    client.ApplicationCertificateType, "HTTPS"));
            }
            if (!NodeId.IsNull(client.DefaultUserTokenGroup))
            {
                groups.Add(new CertificateGroupChoice(
                    "DefaultUserTokenGroup", client.DefaultUserTokenGroup,
                    client.ApplicationCertificateType, "UserToken"));
            }
            var dlg = new CertificateRequestDialog(adapter, groups);
            CertificateRequestResult? result =
                await dlg.ShowDialog<CertificateRequestResult?>(owner).ConfigureAwait(true);
            if (result is null)
            {
                SetResult("Certificate request cancelled.");
                return;
            }
            SetResult(result.Applied
                ? $"Certificate update applied to {result.GroupName}."
                : $"CSR generated for {result.GroupName} (not applied).");
            if (result.Applied)
            {
                await RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            SetResult($"Request New Cert failed: {ex.Message}");
            m_log.LogError(ex, "GdsPush tab {Title}: request new cert failed.", Title);
        }
        finally
        {
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task ApplyChangesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ServerPushConfigurationClient? client = await EnsureSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (client is null)
            {
                return;
            }
            await client.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(true);
            SetResult("ApplyChanges invoked — server may drop session.");
            m_log.LogInformation("GdsPush tab {Title}: ApplyChanges called.", Title);
        }
        catch (Exception ex)
        {
            SetResult($"ApplyChanges failed: {ex.Message}");
            m_log.LogError(ex, "GdsPush tab {Title}: ApplyChanges failed.", Title);
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    // ----- Helpers -----

    private async Task PopulateServerInfoAsync(CancellationToken ct)
    {
        if (m_client?.Session is not { } session)
        {
            return;
        }
        try
        {
            ConfiguredEndpoint? cep = session.ConfiguredEndpoint;
            EndpointDescription? desc = cep?.Description;
            ApplicationDescription? app = desc?.Server;
            if (app is not null)
            {
                ServerApplicationName = app.ApplicationName.IsNull
                    ? "—"
                    : app.ApplicationName.Text ?? "—";
                ServerApplicationUri = string.IsNullOrEmpty(app.ApplicationUri)
                    ? "—"
                    : app.ApplicationUri;
            }
            byte[]? rawCert = null;
            if (desc is not null)
            {
                ByteString bs = desc.ServerCertificate;
                if (!bs.Memory.IsEmpty)
                {
                    rawCert = bs.Memory.ToArray();
                }
            }
            if (rawCert is { Length: > 0 })
            {
                X509Certificate2 cert = X509CertificateLoader.LoadCertificate(rawCert);
                ServerCertSubject = ShortName(cert.Subject);
                ServerCertIssuer = ShortName(cert.Issuer);
                ServerCertExpiry = cert.NotAfter.ToUniversalTime()
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "GdsPush tab {Title}: server info populate failed.", Title);
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void PopulateTrustList(TrustListDataType list, Opc.Ua.Security.Certificates.CertificateCollection rejected)
    {
        Trusted.Clear();
        Issuers.Clear();
        Rejected.Clear();
        if (list?.TrustedCertificates is { } trusted)
        {
            foreach (ByteString der in trusted)
            {
                if (TryParse(der, out X509Certificate2? cert) && cert is not null)
                {
                    Trusted.Add(ToItem(cert));
                }
            }
        }
        if (list?.IssuerCertificates is { } issuers)
        {
            foreach (ByteString der in issuers)
            {
                if (TryParse(der, out X509Certificate2? cert) && cert is not null)
                {
                    Issuers.Add(ToItem(cert));
                }
            }
        }
        if (rejected is not null)
        {
            foreach (Opc.Ua.Security.Certificates.Certificate cert in rejected)
            {
                Rejected.Add(ToItem(cert.AsX509Certificate2()));
            }
        }
    }

    private static bool TryParse(ByteString bs, out X509Certificate2? cert)
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

    private static GdsCertItem ToItem(X509Certificate2 cert)
    {
        return new GdsCertItem
        {
            Thumbprint = cert.Thumbprint,
            Subject = ShortName(cert.Subject),
            Issuer = ShortName(cert.Issuer),
            NotBefore = cert.NotBefore.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            NotAfter = cert.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Certificate = cert
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

    private async Task SafeDisposeClientAsync()
    {
        if (m_client is null)
        {
            return;
        }

        try
        {
            m_client.AdminCredentialsRequired -= OnAdminCredentialsRequired;
            m_client.KeepAlive -= OnKeepAlive;
            await m_client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "GdsPush tab {Title}: disconnect threw (suppressed).", Title);
        }
        try
        {
            await m_client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "GdsPush tab {Title}: dispose threw (suppressed).", Title);
        }
        m_client = null;
    }

    private void OnAdminCredentialsRequired(object? sender, AdminCredentialsRequiredEventArgs e)
    {
        // Marshal to the UI thread and block this callback until the user
        // either supplies credentials or cancels.  The library calls this
        // synchronously from a worker thread so we have to block.
        var tcs = new TaskCompletionSource<UserIdentity?>();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                Window? owner = GetOwnerWindow();
                if (owner is null)
                {
                    tcs.SetResult(null);
                    return;
                }
                var dlg = new CredentialsDialog();
                var pair = await dlg.ShowDialog<(string Username, string Password)?>(owner)
                    .ConfigureAwait(true);
                if (pair is null)
                {
                    tcs.SetResult(null);
                    return;
                }
                tcs.SetResult(new UserIdentity(
                    pair.Value.Username,
                    System.Text.Encoding.UTF8.GetBytes(pair.Value.Password ?? string.Empty)));
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "GdsPush tab {Title}: credentials prompt failed.", Title);
                tcs.SetResult(null);
            }
        });
        UserIdentity? identity = tcs.Task.GetAwaiter().GetResult();
        if (identity is null)
        {
            throw new OperationCanceledException("Administrator credentials not supplied.");
        }
        e.Credentials = identity;
        e.CacheCredentials = true;
    }

    private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
    {
        // Server may drop the session after ApplyChanges; surface that as
        // a soft disconnect notice.
        if (session is null || e is null || ServiceResult.IsGood(e.Status))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            ConnectionStatus = $"● Keep-alive lost: {e.Status?.StatusCode}";
            UpdateStatus();
        });
    }

    private Window? GetOwnerWindow()
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
        Status = $"● Connected · {Trusted.Count} trusted · {Issuers.Count} issuers · " +
                 $"{Rejected.Count} rejected" +
                 (string.IsNullOrEmpty(LastOperationResult)
                     ? string.Empty
                     : $" · {LastOperationResult}");
    }

    partial void OnLastOperationResultChanged(string value) => UpdateStatus();
}

/// <summary>
/// One row in the certificate-group combo in the CSR wizard.  Identifies
/// the server-side certificate group + type the new cert will replace.
/// </summary>
internal sealed record CertificateGroupChoice(
    string Name,
    NodeId CertificateGroupId,
    NodeId CertificateTypeId,
    string Kind)
{
    public override string ToString() => $"{Name} ({Kind})";
}
